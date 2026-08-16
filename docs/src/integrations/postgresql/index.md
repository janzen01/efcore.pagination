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

Both strategies pass an explicit `ESCAPE '\'`, and the engine escapes `\`, `%` and `_` in the user's value, so
a search for `100%` matches the literal text rather than everything.

Registering the strategy also nudges the OpenAPI examples: with PostgreSQL active, a filterable string field
that allows `ILike` gets `$ilike:…` as its example instead of the field's first configured operator.

> `$ilike` without `.UsePostgreSql()` is a plain `LIKE`. The token name describes intent; the guarantee comes
> from the strategy. On SQL Server the common collations are already case-insensitive, so `LIKE` behaves the
> way callers expect — but that is the collation's doing, not the library's.

## A strategy of your own

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
