# Janzen.Pagination.EntityFrameworkCore

Provider-agnostic, configuration-driven **pagination, filtering and sorting** engine built for
Entity Framework Core.

The core of [Janzen.Pagination](https://github.com/janzen01/efcore.pagination): a fluent
per-entity configuration, an opinionated query-string contract, strict validation, and a
ready-made paginated response. Usable on its own; pair it with
[`Janzen.Pagination.PostgreSql`](https://www.nuget.org/packages/Janzen.Pagination.PostgreSql)
for native `ILIKE` search and
[`Janzen.Pagination.AspNetCore`](https://www.nuget.org/packages/Janzen.Pagination.AspNetCore)
for web pipeline integration.

The query-string contract is borrowed from [nestjs-paginate](https://github.com/ppetzold/nestjs-paginate)
(MIT) — the same parameters, operator names and response envelope.

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

## Navigating pages

`response.Links` holds `first`/`previous`/`next`/`last` as URLs, but only when a `PaginateLinkContext` (path +
query parameters) was supplied — the ASP.NET Core package builds one from `HttpRequest`. Without a context
`Links` is `null`, because a URL is meaningless with no request to be relative to. Inside it, an absent link
(`previous` on the first page, `next` on the last) is `null` and stays in the payload as `null`.

`Meta` carries the counters plus `hasPreviousPage` / `hasNextPage` and an echo of the **effective** request —
`sortBy`, `search`, `searchBy`, `filter` after the config's defaults have been applied. That is what a grid
header needs: a request that sent no `sortBy` still gets `"sortBy": ["name:ASC"]` back, because only the
server knows where `DefaultSortBy` landed. See
[Response contract](https://janzen01.github.io/efcore.pagination/reference/response/).

Off the web, navigate by `Meta` and `WithPage` instead; the result goes straight back into the engine:

```csharp
var next = response.Meta.HasNextPage
    ? request.WithPage(response.Meta.CurrentPage + 1)
    : null; // last page

// WithPage carries limit, sort, search and filters over — only the page changes.
```

## Declaring fields

| Builder call | Enables | Notes |
|--------------|---------|-------|
| `.WithLimits(default, max)` | `page`, `limit` | **Required** — there is no implicit page size. |
| `.Sortable(name, expr)` | `sortBy=name:ASC\|DESC` | Any expression the provider can put in `ORDER BY`. |
| `.DefaultSortBy(name, dir)` | — | Used when the request sends no `sortBy`; the field must be sortable. |
| `.WithTieBreaker(expr)` | — | Unique key appended as the final ordering key on every query. |
| `.Searchable(name, expr)` | `search`, `searchBy=name` | Selector must return `string?`. |
| `.Filterable(name, expr, ops…)` | `filter.name=$op:value` | At least one operator; the list is that field's allow-list. |
| `.FilterableMany(name, coll, expr, ops…)` | `filter.name=$op:value` | Matches any element of a child collection (`Any(...)`). |
| `.WithGuards(…)` | — | Ceilings on filter values / conditions / sort fields / search length. |
| `.ShowBadge(name, cssClass?)` | — | Labels the preceding field in the OpenAPI output. |
| `.When(bool)` | — | Gates the preceding field at query time; must be paired with `.ShowBadge`. |

```csharp
// Filter articles by any of their tags: ?filter.tag=$in:dotnet,efcore
.FilterableMany("tag", a => a.Tags, t => t.Slug,
    PaginateFilterOperator.Eq, PaginateFilterOperator.In)

// Tighter guards than the defaults (100 / 20 / 5 / 256)
.WithGuards(maxFilterValues: 25, maxSortFields: 3)
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
stays auth-agnostic. The condition is captured when the config is **built**, so per-user gating means one cached config
**per distinct set of permissions** — two roles, two static fields — rather than calling `Create` per request. Building
walks every selector expression tree, which is not work that belongs on a hot path.

## Projection strategies

Four ways to shape each page row into a DTO — pick the cheapest that fits:

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

| Parameter        | Repeatable | Example                              | Meaning |
|------------------|:----------:|--------------------------------------|---------|
| `page`           | no         | `?page=2`                            | 1-based page number; defaults to 1. |
| `limit`          | no         | `?limit=50`                          | Page size; defaults to `DefaultLimit`, rejected above `MaxLimit`. |
| `sortBy`         | yes        | `?sortBy=price:DESC&sortBy=name:ASC` | Applied in the order given. |
| `search`         | no         | `?search=smith`                      | Free text over the searchable fields. |
| `searchBy`       | yes        | `?searchBy=name`                     | Narrows `search` to a subset of them. |
| `filter.<field>` | yes        | `?filter.status=$eq:Active`          | One or more criteria per field. |

Anything else in the query string is ignored, so clients keep their own tracking parameters. `page` and
`limit` are validated and return `400`.

### Filter operators

```
filter.<field> = [$not:] [$and: | $or:] $<operator>[:<value>[,<value>…]]
```

| Token | Applies to | Meaning |
|-------|------------|---------|
| `$eq` | any | `= value` |
| `$in` | any | `IN (a, b, c)` — comma-separated |
| `$null` | any | `IS NULL` |
| `$sw` | string | `LIKE 'value%'` |
| `$ilike` | string | `LIKE '%value%'` — native `ILIKE` with the PostgreSql package |
| `$contains` | string or collection | on a string: same as `$ilike`; on a collection: contains **all** listed values |
| `$lt` `$lte` `$gt` `$gte` | comparable | `<` `<=` `>` `>=` |
| `$btw` | comparable | inclusive range — exactly two comma-separated values |

Each field whitelists its own
[operators](https://janzen01.github.io/efcore.pagination/reference/query-string/#operator-reference); one that is not
granted for that field is a `400`.

```http
?filter.status=$in:Active,Draft
?filter.price=$btw:10,99.90
?filter.deletedAt=$not:$null
?filter.price=$gte:100&filter.price=$lte:500       # criteria on one field default to AND
?filter.status=$eq:Active&filter.status=$or:$eq:Draft
```

Criteria on different fields are always ANDed; there is no cross-field `OR` or grouping. Enums are addressed
**by name** (`Active`), numbers and dates use the invariant culture, and values are emitted as SQL parameters
rather than inlined literals.

The full contract is the [Query-string
contract](https://janzen01.github.io/efcore.pagination/reference/query-string/); what each type accepts is
[value formats](https://janzen01.github.io/efcore.pagination/reference/query-string/#value-formats), and every `400`
a filter can produce is [Errors](https://janzen01.github.io/efcore.pagination/reference/errors/#filter-operators).

## Composing without executing

Two extension methods build the query the engine would run and hand it back unexecuted — for a `ToQueryString()`
you can assert on, and for anything computed over the **matching set** rather than the page:

```csharp
// The page query: filters, search, ordering (tie-breaker included), Skip/Take. No count, no projection.
var composed = db.Products.ApplyPagination(request, config);
string sql   = composed.Query.ToQueryString();   // exactly what PaginateAsync executes
// composed also carries the effective Page/Limit/SortBy/Search/SearchBy/Filter, for a custom envelope.

// The matching set: filters and search only. Facets, sums, exports.
var facets = await db.Products.ApplyPaginateFilters(request, config).Query
    .GroupBy(p => p.Status)
    .Select(g => new { Status = g.Key, Count = g.Count() })
    .ToListAsync(ct);
```

Both return the same `PaginateComposedQuery<TEntity>` and reject exactly what `PaginateAsync` rejects, at
compose time — except that `ApplyPaginateFilters` does not validate `sortBy`, which it never applies, and
reports it as `null` rather than empty. See
[Query composers](https://janzen01.github.io/efcore.pagination/reference/composers/).

## Documentation

- [Getting started](https://janzen01.github.io/efcore.pagination/guide/getting-started/)
- [Configuration](https://janzen01.github.io/efcore.pagination/guide/configuration/)
- [Projections](https://janzen01.github.io/efcore.pagination/guide/projections/)
- [Query-string contract](https://janzen01.github.io/efcore.pagination/reference/query-string/)
- [Response contract](https://janzen01.github.io/efcore.pagination/reference/response/)
- [Configuration API](https://janzen01.github.io/efcore.pagination/reference/configuration/)
- [Query composers](https://janzen01.github.io/efcore.pagination/reference/composers/)
- [Errors](https://janzen01.github.io/efcore.pagination/reference/errors/)
- [Cookbook](https://janzen01.github.io/efcore.pagination/recipes/)

## Trimming & Native AOT

The engine builds LINQ expression trees and uses reflection (DTO projection mapping, `MakeGenericMethod`),
so it is **not compatible with trimming or Native AOT**. Every public `Paginate*Async`
entry point is annotated with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`, so consumers
building trimmed or AOT applications get accurate analyzer warnings rather than silent runtime failures.

## Debugging

The package ships **embedded PDBs with Source Link**, so a debugger steps straight into these sources at the exact
commit the version was built from. Nothing to configure: no symbol server, no separate symbol download, and it works
offline.

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/master/LICENSE) © Lubos Jansky
