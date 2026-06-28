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
public sealed class ProductConfigProvider : IPaginateConfigProvider<Product>
{
    public PaginateConfig<Product> GetConfig() =>
        PaginateConfig<Product>.Create(b => b
            .WithLimits(defaultLimit: 25, maxLimit: 100)
            .Sortable("name", p => p.Name)
            .DefaultSortBy("name")
            .WithTieBreaker(p => p.Id) // unique key appended as the final ordering → deterministic paging
            .Searchable("name", p => p.Name)
            .Filterable("status", p => p.Status, PaginateFilterOperator.Eq));
}

// 2. Build a request (bound from the query string in ASP.NET, or directly for non-web callers)…
var request = new PaginateQuery { Page = 1, Limit = 25, SortBy = ["name:DESC"] };

// 3. …and execute against an IQueryable, projecting to a DTO.
PaginatedResponse<ProductDto> response = await dbContext.Products
    .PaginateAsync<ProductDto>(request, config);
```

## Projection strategies

Three ways to shape each page row into a DTO — pick the cheapest that fits:

| Strategy     | Entry point                                        | Runs where     | Use for |
|--------------|----------------------------------------------------|----------------|---------|
| **Auto**     | `PaginateAsync<TResult>(request, config)`           | SQL            | DTOs buildable by convention: scalars, single nested objects, `Instant → DateTimeOffset`. |
| **Selector** | `PaginateAsync<TResult>(request, config, selector)` | SQL (+ shaper) | Anything expressible as a `Select`: aggregates, **sub-collections**, conversions — in one narrow query. |
| **Map**      | `PaginateMapAsync<TResult>(request, config, map)`   | in memory      | Only when the response needs the **fully loaded entity**. Over-fetches by design. |

### Sub-collections + NodaTime conversions in a single query

A DTO that has **both** one-to-many sub-collections **and** `Instant → DateTimeOffset` conversions
(even *inside* the sub-collection items) does **not** need `PaginateMapAsync`. Because the selector is the
query's terminal projection, EF Core runs the column reads and sub-collection materialization in SQL and
applies the (free) `Instant → DateTimeOffset` reinterpret in the shaper, over the page rows only:

```csharp
PaginatedResponse<ProductSummary> page = await db.Products.PaginateAsync<ProductSummary>(request, config,
    p => new ProductSummary(
        p.Id,
        p.Name,
        p.ReleasedAt.ToDateTimeOffset(),                                     // Instant  → DateTimeOffset
        p.DiscontinuedAt.HasValue ? p.DiscontinuedAt.Value.ToDateTimeOffset() // Instant? → DateTimeOffset?
                                  : (DateTimeOffset?)null,
        p.Reviews.Select(r => new ReviewDto(r.Id, r.Reviewer,
            r.PostedAt.ToDateTimeOffset())).ToList()));                      // conversion INSIDE a sub-collection
```

This executes as a **single** query whose `SELECT` lists only the referenced columns — an unused `jsonb`
column is never fetched. The `Instant → DateTimeOffset` casts come from
[`Janzen.Pagination.NodaTime`](https://www.nuget.org/packages/Janzen.Pagination.NodaTime).

## Query-string contract

| Parameter        | Example                     | Meaning                                        |
|------------------|-----------------------------|------------------------------------------------|
| `page`           | `?page=2`                   | 1-based page number                            |
| `limit`          | `?limit=50`                 | page size (capped by `MaxLimit`)               |
| `sortBy`         | `?sortBy=name:DESC`         | sort field and direction                       |
| `search`         | `?search=smith`             | free-text search over searchable fields        |
| `filter.<field>` | `?filter.status=$eq:active` | `$operator:value` filter on a filterable field |

## Trimming & Native AOT

The engine builds LINQ expression trees and uses reflection (DTO projection mapping, `MakeGenericMethod`),
so it is **not compatible with trimming or Native AOT**. The public `PaginateAsync` / `PaginateMapAsync`
entry points are annotated with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`, so consumers
building trimmed or AOT applications get accurate analyzer warnings rather than silent runtime failures.

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/main/LICENSE) © Lubos Jansky
