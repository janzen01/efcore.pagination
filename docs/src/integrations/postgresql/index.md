# PostgreSQL

```bash
dotnet add package Janzen.Pagination.PostgreSql
```

The package does exactly one thing: it swaps the strategy that decides how a pattern match reaches SQL.

## `LIKE` vs `ILIKE`

Three things emit a pattern match: `search`, the `$ilike` / `$sw` operators, and `$contains` on a string field.
Which SQL they become is a single **process-wide** strategy.

| Strategy | Registered by | Emits | Case-insensitive? |
|----------|---------------|-------|-------------------|
| Portable *(default)* | nothing — it is the fallback | `EF.Functions.Like` → SQL `LIKE` | follows the column collation |
| PostgreSQL | `.UsePostgreSql()` | `NpgsqlDbFunctionsExtensions.ILike` → SQL `ILIKE` | yes, always |

```csharp
builder.Services.AddPagination(pagination => pagination
    .AddAspNetCore()
    .UsePostgreSql());
```

Your `PaginateConfig<T>` definitions do not change — they stay provider-agnostic, and only the emitted SQL
differs. The switch is **global**: it applies to every config in the process, because the provider is a
property of the database, not of a resource.

### The difference, in SQL

The same request, `?filter.name=$ilike:widget`, against the same config. Without the package:

```sql
SELECT p."Id", p."Name", p."Status", p."Rank"
FROM "Products" AS p
WHERE p."Name" LIKE '%widget%' ESCAPE '\'
```

With `.UsePostgreSql()`:

```sql
SELECT p."Id", p."Name", p."Status", p."Rank"
FROM "Products" AS p
WHERE p."Name" ILIKE '%widget%' ESCAPE '\'
```

One keyword. `$sw:Wid` differs the same way — `ILIKE 'Wid%' ESCAPE '\'` instead of `LIKE`. The pattern, the
escaping and the parameterisation are identical; only the operator changes, which is why nothing else about a
config or a query has to know which strategy is registered.

::: info About the SQL on this page
Captured from EF Core's Npgsql provider through `ToQueryString()`, which generates SQL without opening a
connection. That is also how you can check this yourself against your own model. Values are shown inline here
because `ToQueryString()` renders them that way; at run time they are parameters.
:::

Both strategies pass an explicit `ESCAPE '\'`, and the engine escapes `\`, `%`, `_` and `[` in the user's
value, so a search for `100%` matches the literal text rather than everything:

```http
?filter.name=$contains:100%
```

becomes the pattern `%100\%%` — the caller's `%` is escaped into a literal, the surrounding two are the
engine's. A caller therefore cannot smuggle a wildcard through a search box and turn an indexed prefix match
into a full scan. `[` is in the list for SQL Server's sake, where it opens a character range; PostgreSQL
treats `\[` as the literal character, so escaping it costs nothing here.

Registering the strategy also nudges the OpenAPI examples: with PostgreSQL active, a filterable string field
that allows `ILike` gets `$ilike:…` as its example instead of the field's first configured operator.

> `$ilike` without `.UsePostgreSql()` is a plain `LIKE`. The token name describes intent; the guarantee comes
> from the strategy. On SQL Server the common collations are already case-insensitive, so `LIKE` behaves the
> way callers expect — but that is the collation's doing, not the library's.

## A strategy of your own

You almost certainly do not need one. `UsePostgreSql()` is the reason this extension point exists and it
already covers the case it was built for; write your own only for a provider with a pattern-match function of
its own, or a PostgreSQL setup where `ILIKE` is the wrong call — a `citext` column, or a custom collation.

Derive from `PaginateLikeStrategyBase` and hand it the `EF.Functions` overload to call. That is the whole
implementation both shipped strategies are, and it is what passes the `ESCAPE` argument for you:

```csharp
internal sealed class CitextLikeStrategy() : PaginateLikeStrategyBase(ILikeMethod) {

    private static readonly MethodInfo ILikeMethod =
        ((MethodCallExpression)((Expression<Func<string, string, bool>>)
            ((v, p) => EF.Functions.ILike(v, p, PaginateLikeDefaults.EscapeCharacter))).Body).Method;

    // Which operator best represents this strategy in generated docs; null = use the field's first operator.
    public override PaginateFilterOperator? PreferredExampleOperator => PaginateFilterOperator.ILike;

}

builder.Services.AddPagination(p => p.UseLikeStrategy(new CitextLikeStrategy()));
```

::: danger Declare the escape character
The pattern you are handed is **already escaped**, with `PaginateLikeDefaults.EscapeCharacter` — a single
backslash. Whatever call you build has to declare that character as its explicit `ESCAPE` argument. Implement
`IPaginateLikeStrategy` directly and omit it, and the escape characters stay in the pattern as literal text the
data would have to contain: every search for a value containing `%`, `_`, `[` or `\` silently stops matching.
The match set only narrows, never widens — nothing can be smuggled through — but it narrows on every provider
with no default escape character, SQLite and SQL Server among them. PostgreSQL and MySQL take `\` as their
default escape and keep working by accident, which is what makes this easy to ship and not notice. Deriving from
`PaginateLikeStrategyBase` is the way not to get it wrong.
:::

Call it once at startup, before serving requests. `UseLikeStrategy` sets a static, so the last call wins — do
not switch it per request. To give one resource its own strategy instead, use
[`WithLikeStrategy`](/reference/configuration/#withlikestrategy) on its configuration.

### The static behind it

`UseLikeStrategy(...)` assigns `PaginateLikeDefaults.Strategy`, a public, settable, process-wide property.
Reading it tells you which strategy is active; assigning it is the non-DI way in, for a console tool or a
test host with no service collection:

```csharp
PaginateLikeDefaults.Strategy = new CitextLikeStrategy();
```

It defaults to `PaginateLikeDefaults.Portable`, the library's own portable `LIKE` strategy, so nothing has to
be registered for the engine to work. That property is also how you put the process **back**:

```csharp
PaginateLikeDefaults.Strategy = PaginateLikeDefaults.Portable;   // undo a UsePostgreSql() for this process
```

Because the setter is mutable and shared, a test that swaps it changes behaviour for everything running
alongside it — see [Testing your pagination](/recipes/testing/#watch-the-process-wide-statics). Assigning
`null` throws rather than leaving the engine with nothing to call.

## One process, two providers

`UsePostgreSql()` is process-wide, so on its own it makes **every** pattern match emit `ILIKE` — including the
ones aimed at another provider, which rejects the keyword at execution time rather than at startup. The
strategy itself cannot tell the two apart: `BuildLike(value, pattern)` is handed no provider and no context.

The configuration decides instead. `WithLikeStrategy(...)` overrides the process-wide default for one
resource, and is resolved per query:

```csharp
// PostgreSQL stays on native ILIKE via the registration above; this resource is backed by SQL Server.
var invoices = PaginateConfig<Invoice>.Create(b => b
    .WithLimits(25, 100)
    .WithTieBreaker(i => i.Id)
    .WithLikeStrategy(PaginateLikeDefaults.Portable)
    .Filterable("reference", i => i.Reference));
```

See [`WithLikeStrategy`](/reference/configuration/#withlikestrategy) for the full rules. The other route is
still open: leave the default portable strategy in place everywhere and get case-insensitivity from the
column collation instead.

## Testing it

Native `ILIKE` and its `ESCAPE` behaviour need a real PostgreSQL server, so they are **not** covered by this
library's own in-process test suite. If you depend on `UsePostgreSql()`, that seam is worth one integration
test of your own — see [Testing your pagination](/recipes/testing/).
