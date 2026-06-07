# Janzen.Pagination.EntityFrameworkCore

Provider-agnostic, configuration-driven **pagination, filtering and sorting** engine for
Entity Framework Core.

The core of [Janzen.Pagination](https://github.com/janzen01/efcore.pagination): a fluent
per-entity configuration, an opinionated query-string contract, strict validation, and a
ready-made paginated response. Usable on its own; pair it with
[`Janzen.Pagination.PostgreSql`](https://www.nuget.org/packages/Janzen.Pagination.PostgreSql)
for native `ILIKE` search and
[`Janzen.Pagination.AspNetCore`](https://www.nuget.org/packages/Janzen.Pagination.AspNetCore)
for web pipeline integration.

## Install

```bash
dotnet add package Janzen.Pagination.EntityFrameworkCore
```

## Quick start

```csharp
// 1. Describe what is sortable / searchable / filterable for an entity.
public sealed class JudgeConfigProvider : IPaginateConfigProvider<Judge>
{
    public PaginateConfig<Judge> GetConfig() =>
        PaginateConfig<Judge>.Create(b => b
            .WithLimits(defaultLimit: 25, maxLimit: 100)
            .Sortable("name", j => j.Name)
            .DefaultSortBy("name")
            .WithTieBreaker(j => j.Id) // unique key appended as the final order → deterministic paging
            .Searchable("name", j => j.Name)
            .Filterable("status", j => j.Status, PaginateFilterOperator.Eq));
}

// 2. Execute against an IQueryable, projecting to a DTO.
PaginatedResponse<JudgeDto> response = await dbContext.Judges
    .PaginateAsync<JudgeDto>(request, config);
```

## Query-string contract

| Parameter        | Example                     | Meaning                                        |
|------------------|-----------------------------|------------------------------------------------|
| `page`           | `?page=2`                   | 1-based page number                            |
| `limit`          | `?limit=50`                 | page size (capped by `MaxLimit`)               |
| `sortBy`         | `?sortBy=name:DESC`         | sort field and direction                       |
| `search`         | `?search=smith`             | free-text search over searchable fields        |
| `filter.<field>` | `?filter.status=$eq:active` | `$operator:value` filter on a filterable field |

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/main/LICENSE) © Lubos Jansky
