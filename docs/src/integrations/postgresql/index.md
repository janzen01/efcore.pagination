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

Both strategies pass an explicit `ESCAPE '\'`, and the engine escapes `\`, `%` and `_` in the user's value, so
a search for `100%` matches the literal text rather than everything:

```http
?filter.name=$contains:100%
```

becomes the pattern `%100\%%` — the caller's `%` is escaped into a literal, the surrounding two are the
engine's. A caller therefore cannot smuggle a wildcard through a search box and turn an indexed prefix match
into a full scan.

Registering the strategy also nudges the OpenAPI examples: with PostgreSQL active, a filterable string field
that allows `ILike` gets `$ilike:…` as its example instead of the field's first configured operator.

> `$ilike` without `.UsePostgreSql()` is a plain `LIKE`. The token name describes intent; the guarantee comes
> from the strategy. On SQL Server the common collations are already case-insensitive, so `LIKE` behaves the
> way callers expect — but that is the collation's doing, not the library's.

## A strategy of your own

You almost certainly do not need one. `UsePostgreSql()` is the reason this extension point exists and it
already covers the case it was built for; write your own only for a provider with a pattern-match function of
its own, or a PostgreSQL setup where `ILIKE` is the wrong call — a `citext` column, or a custom collation.

```csharp
internal sealed class CitextLikeStrategy : IPaginateLikeStrategy {

    // Which operator best represents this strategy in generated docs; null = use the field's first operator.
    public PaginateFilterOperator? PreferredExampleOperator => PaginateFilterOperator.ILike;

    // `value` is the column expression, `pattern` the already-escaped and parameterised LIKE pattern.
    public Expression BuildLike(Expression value, Expression pattern) => /* your EF.Functions call */;

}

builder.Services.AddPagination(p => p.UseLikeStrategy(new CitextLikeStrategy()));
```

Call it once at startup, before serving requests. It sets a static, so the last call wins — do not switch it
per request.

### The static behind it

`UseLikeStrategy(...)` assigns `PaginateLikeDefaults.Strategy`, a public, settable, process-wide property.
Reading it tells you which strategy is active; assigning it is the non-DI way in, for a console tool or a
test host with no service collection:

```csharp
PaginateLikeDefaults.Strategy = new CitextLikeStrategy();
```

It defaults to the portable `LIKE` strategy, so nothing has to be registered for the engine to work. Because
it is mutable and shared, a test that swaps it changes behaviour for everything running alongside it — see
[Testing your pagination](/recipes/testing/#watch-the-process-wide-statics).

## One process, one strategy

Because the strategy is process-wide, an application that talks to **PostgreSQL and something else** in the
same process cannot have both. Registering `UsePostgreSql()` makes every pattern match emit `ILIKE`, including
the ones aimed at the other provider, which will reject the keyword at execution time rather than at startup.

If that is your shape, leave the default portable strategy in place and get case-insensitivity from the
column collation instead.

## Testing it

Native `ILIKE` and its `ESCAPE` behaviour need a real PostgreSQL server, so they are **not** covered by this
library's own in-process test suite. If you depend on `UsePostgreSql()`, that seam is worth one integration
test of your own — see [Testing your pagination](/recipes/testing/).
