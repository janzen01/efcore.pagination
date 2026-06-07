# Janzen.Pagination.PostgreSql

PostgreSQL provider for [Janzen.Pagination](https://github.com/janzen01/efcore.pagination) —
case-insensitive search via PostgreSQL's native **`ILIKE`** operator.

Without a provider package the engine falls back to a portable `LIKE` whose case-sensitivity
depends on the column collation. This package registers a strategy that emits true `ILIKE`,
giving correct case-insensitive search on PostgreSQL.

## Install

```bash
dotnet add package Janzen.Pagination.PostgreSql
```

Requires [`Janzen.Pagination.EntityFrameworkCore`](https://www.nuget.org/packages/Janzen.Pagination.EntityFrameworkCore)
(referenced transitively).

## Usage

Apply per resource on the `PaginateConfig` builder — it swaps the portable `LIKE` for native `ILIKE`:

```csharp
PaginateConfig<Judge>.Create(b => b
    .WithLimits(25, 100)
    .UsePostgreSql() // emit native ILIKE for case-insensitive search / pattern filtering
    .Sortable("name", j => j.Name)
    .Searchable("name", j => j.Name)
    .WithTieBreaker(j => j.Id));
```

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/main/LICENSE) © Lubos Jansky
