# Janzen.Pagination — guide

Three pages, in reading order. Each stands on its own, so jumping straight to the one you need also works.

| Page | What it answers |
|------|-----------------|
| **[Getting started](./getting-started/)** | Install, register, and get a paginated endpoint returning JSON. |
| **[Configuration](./configuration/)** | Every `PaginateConfigBuilder<T>` method: what it enables and what it rejects. |
| **[Projections](./projections/)** | The four `Paginate*Async` entry points and how to pick between them. |

The rest of the site is not narrative, so it lives outside the guide:

| Section | What it is for |
|---------|----------------|
| **[Reference](/reference/query-string/)** | The wire contract, looked up mid-task: every parameter, every operator, every `400`, and the SQL each one emits. |
| **[Integrations](/integrations/)** | One page per package — ASP.NET Core, PostgreSQL, NodaTime — plus teaching the engine your own types. |
| **[Cookbook](/recipes/)** | Task-shaped answers: RBAC, collection filters, aggregates, `Link` headers, non-web callers, testing. |

## The 30-second version

```csharp
// 1. Declare the contract for an entity — nothing outside it is addressable from the query string.
var config = PaginateConfig<Product>.Create(b => b
    .WithLimits(defaultLimit: 25, maxLimit: 100)
    .Sortable("name", p => p.Name)
    .DefaultSortBy("name")
    .WithTieBreaker(p => p.Id)
    .Searchable("name", p => p.Name)
    .Filterable("status", p => p.Status, PaginateFilterOperator.Eq, PaginateFilterOperator.In));

// 2. Execute it against any IQueryable.
PaginatedResponse<ProductDto> page = await db.Products.PaginateAsync<Product, ProductDto>(request, config);
```

```http
GET /products?page=2&limit=25&sortBy=name:DESC&search=acme&filter.status=$in:active,pending
```

## How the pieces fit

```mermaid
graph BT
    Core["<b>Janzen.Pagination.EntityFrameworkCore</b><br/>query engine: config, filtering, sorting, paging, projection"]

    Pg["<b>.PostgreSql</b><br/>native ILIKE"]
    Web["<b>.AspNetCore</b><br/>binding · ProblemDetails · links · OpenAPI"]
    Noda["<b>.NodaTime</b><br/>Instant · LocalDate"]

    Pg --> Core
    Web --> Core
    Noda --> Core

    classDef core fill:#512BD4,stroke:#512BD4,color:#fff
    classDef addon fill:none,stroke:#512BD4,stroke-width:2px
    class Core core
    class Pg,Web,Noda addon
```

The engine works on its own against any `IQueryable<T>`. The three add-ons are independent of each other —
take only the ones you need.

## Two things worth knowing up front

**The configuration is the allow-list.** A field that is not declared `Sortable` / `Searchable` / `Filterable`
cannot be sorted, searched or filtered on, and an operator not listed for a field is rejected for that field.
There is no "expose the whole entity" mode, deliberately.

**Paging is offset-based and needs a deterministic order.** `WithTieBreaker(p => p.Id)` appends a unique key as
the final ordering key so two rows that compare equal on the primary sort still land in a stable order. Without
any order at all the engine refuses the query rather than returning silently unstable pages.
