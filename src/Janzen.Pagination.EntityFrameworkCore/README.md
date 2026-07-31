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
            .Sortable("name", p => p.Name).ShowBadge("Public", "language-public") // optional badge shown in the API reference UI
            .DefaultSortBy("name")
            .WithTieBreaker(p => p.Id) // unique key appended as the final ordering → deterministic paging
            .Searchable("name", p => p.Name)
            .Filterable("status", p => p.Status, PaginateFilterOperator.Eq));
}

// 2. Build a request (bound from the query string in ASP.NET, or directly for non-web callers)…
var request = new PaginateQuery { Page = 1, Limit = 25, SortBy = ["name:DESC"] };

// 3. …and execute against an IQueryable, projecting to a DTO.
PaginatedResponse<ProductDto> response = await dbContext.Products
    .PaginateAsync<Product, ProductDto>(request, config);
```

## Badges

Attach an optional presentation **badge** (a label and optional CSS class) to any sortable, searchable or filterable
field with `.ShowBadge(name, cssClass?)` immediately after declaring it. Badges surface in the generated OpenAPI
metadata and render as chips in the API reference UI (e.g. Scalar):

```csharp
.Sortable("slug", p => p.Slug).ShowBadge("Public", "language-public")
.Searchable("title", p => p.Title).ShowBadge("Beta")                    // no class → neutral chip
.Filterable("id", p => p.Id, PaginateFilterOperator.Eq).ShowBadge("Stable", "language-stable")
```

`ShowBadge` targets the field declared immediately before it. The library imposes no palette — you color the chip via
your API reference's **custom CSS**. The class **must start with `language-`**: it is the only class prefix Scalar's
markdown sanitizer keeps in a parameter description (inline styles and other classes are stripped), so `ShowBadge`
throws otherwise. Then register e.g. `.language-public { background:#277A2C; color:#fff; border-radius:4px; padding:1px 6px }`.

## Conditional fields (RBAC)

Mark a field with `.When(bool)` to make it **conditional**: it stays in the OpenAPI docs (the widest surface) but at
query time is treated as not configured whenever the condition is `false` — a request targeting it gets a `400`, worded
exactly like an unknown field so the field's existence isn't disclosed. `.When` **must** be paired with `.ShowBadge(...)`
so the restriction is documented, otherwise `Build()` throws:

```csharp
PaginateConfig<Article>.Create(b => b
    .WithLimits(25, 100)
    .Sortable("title", a => a.Title)
    .Filterable("isDeleted", a => a.IsDeleted, PaginateFilterOperator.Eq)
        .When(currentUser.IsAdmin).ShowBadge("Admin only", "language-admin"));
```

`.When` takes a plain boolean — you evaluate it from your own context (role, claims, tenant, feature flag); the library
stays auth-agnostic. The condition is captured when the config is built, so per-user RBAC means building the config
**per request** (e.g. an `IPaginateConfigProvider<T>` resolved from DI with the current user) rather than a static
singleton.

## Projection strategies

Three ways to shape each page row into a DTO — pick the cheapest that fits:

| Strategy     | Entry point                                        | Runs where     | Use for |
|--------------|----------------------------------------------------|----------------|---------|
| **Auto**     | `PaginateAsync<TEntity, TResult>(request, config)`           | SQL            | DTOs buildable by convention: scalars, single nested objects, `Instant → DateTimeOffset`. |
| **Selector** | `PaginateSelectAsync<TEntity, TResult>(request, config, selector)` | SQL (+ shaper) | Anything expressible as a `Select`: aggregates, **sub-collections**, conversions — in one narrow query. |
| **Selector + finalize** | `PaginateSelectMapAsync<TEntity, TProjection, TResult>(request, config, selector, postMap)` | SQL + in-memory | Most of the row translates, but a field or two needs a CLR computation EF can't translate (weighted aggregate over a sub-collection with a guard/rounding). Narrow `SELECT`; `postMap` finalizes the page only (O(page size)). |
| **Map**      | `PaginateMapAsync<TEntity, TResult>(request, config, map)`   | in memory      | Only when the response needs the **fully loaded entity**. Over-fetches by design. |

Each strategy has its own name rather than being an overload of `PaginateAsync`, so the call site says which one it
uses: `Select` means projected in SQL, `Map` means mapped in memory.

`TEntity` comes first in every entry point: these are C# extension-block members, so explicit type arguments
must name the entity type before the result type. Where a `selector`, `postMap` or `map` lambda is passed,
all type arguments are inferable — `db.Products.PaginateSelectAsync(request, config, selector)` also compiles.

### Sub-collections + NodaTime conversions in a single query

A DTO that has **both** one-to-many sub-collections **and** `Instant → DateTimeOffset` conversions
(even *inside* the sub-collection items) does **not** need `PaginateMapAsync`. Because the selector is the
query's terminal projection, EF Core runs the column reads and sub-collection materialization in SQL and
applies the (free) `Instant → DateTimeOffset` reinterpret in the shaper, over the page rows only:

```csharp
PaginatedResponse<ProductSummary> page = await db.Products.PaginateSelectAsync<Product, ProductSummary>(request, config,
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
so it is **not compatible with trimming or Native AOT**. Every public `Paginate*Async`
entry point is annotated with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`, so consumers
building trimmed or AOT applications get accurate analyzer warnings rather than silent runtime failures.

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/main/LICENSE) © Lubos Jansky
