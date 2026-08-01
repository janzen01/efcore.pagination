# Getting started

From nothing to a paginated, filterable, sortable JSON endpoint. The example is an ASP.NET Core Web API on
PostgreSQL; skip to [Without ASP.NET Core](#without-aspnet-core) if you only want the engine.

## 1. Install

```bash
dotnet add package Janzen.Pagination.EntityFrameworkCore
dotnet add package Janzen.Pagination.AspNetCore
dotnet add package Janzen.Pagination.PostgreSql
```

The core engine is the only required package — the other two are additive. `.AspNetCore` and `.PostgreSql`
both reference the core transitively, so installing either alone also works.

## 2. The entity and the DTO

Nothing is required of the entity. The response DTO should be a **record** (or any type with a public
constructor): the automatic projection maps *constructor parameters*, not settable properties.

```csharp
public sealed class Product {
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ProductStatus Status { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<Review> Reviews { get; set; } = [];
}

public enum ProductStatus { Draft, Active, Discontinued }

// Parameter names are matched against Product members, case-insensitively.
public sealed record ProductDto(Guid Id, string Name, ProductStatus Status, decimal Price, DateTimeOffset CreatedAt);
```

## 3. Describe what clients may do

This is the whole contract. Anything not declared here is not addressable from the query string.

```csharp
using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Model;

public sealed class ProductPaginateConfigProvider : IPaginateConfigProvider<Product> {

    public readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
        .WithLimits(defaultLimit: 25, maxLimit: 100)

        // sortBy=name:ASC | price:DESC | createdAt:DESC
        .Sortable("name", p => p.Name)
        .Sortable("price", p => p.Price)
        .Sortable("createdAt", p => p.CreatedAt)
        .DefaultSortBy("createdAt", PaginateSortDirection.Desc)
        .WithTieBreaker(p => p.Id)          // unique key appended last → stable page boundaries

        // search=foo (and searchBy=name to narrow it)
        .Searchable("name", p => p.Name)
        .Searchable("description", p => p.Description)

        // filter.status=$eq:Active, filter.price=$btw:10,99.90, …
        .Filterable("status", p => p.Status, PaginateFilterOperator.Eq, PaginateFilterOperator.In)
        .Filterable("price", p => p.Price,
            PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThanOrEqual,
            PaginateFilterOperator.LessThanOrEqual, PaginateFilterOperator.Between)
        .Filterable("createdAt", p => p.CreatedAt,
            PaginateFilterOperator.GreaterThan, PaginateFilterOperator.LessThan, PaginateFilterOperator.Between));

    public PaginateConfig<Product> GetConfig() => Config;

}
```

Implementing `IPaginateConfigProvider<T>` is optional for querying — `PaginateAsync` takes the config directly —
but the OpenAPI integration resolves the documented parameters through the provider type, so it is the shape to
reach for in a web app. You only write the typed `GetConfig()`; the non-generic member is provided by a default
interface implementation.

## 4. Register

```csharp
// Program.cs
builder.Services.AddPagination(pagination => pagination
    .AddAspNetCore()     // query-string model binder + 400 ProblemDetails filter
    .UsePostgreSql());   // upgrade LIKE to native ILIKE

builder.Services.AddControllers();

// Optional: document the pagination parameters in the OpenAPI output.
using Janzen.Pagination.AspNetCore.OpenApi;
builder.Services.AddOpenApi(options =>
    options.AddOperationTransformer<PaginatedQueryOperationTransformer>());
```

## 5. The endpoint

```csharp
[ApiController]
[Route("products")]
public sealed class ProductController(AppDbContext db) : ControllerBase {

    [HttpGet]
    [PaginatedQuery<ProductPaginateConfigProvider>]   // documents the query parameters in OpenAPI
    public Task<PaginatedResponse<ProductDto>> List([FromQuery] PaginateQuery request, CancellationToken ct) =>
        db.Products.PaginateAsync<Product, ProductDto>(
            request, ProductPaginateConfigProvider.Config, this.Request, ct);

}
```

`[FromQuery] PaginateQuery` is bound by the model binder that `AddAspNetCore()` registered — you do not declare
`page`, `limit` and friends as action parameters. Passing `this.Request` is what makes the response carry
`first`/`prev`/`next`/`last` links; omit it and those are `null`.

The `Minimal API` equivalent is in [ASP.NET Core → Minimal APIs](aspnetcore.md#minimal-apis).

## 6. What comes back

```http
GET /products?page=2&limit=2&sortBy=price:DESC&filter.status=$eq:Active
```

```json
{
  "items": [
    { "id": "7f3c…", "name": "Widget Pro", "status": "Active", "price": 249.00, "createdAt": "2026-04-02T09:15:00+00:00" },
    { "id": "b1a9…", "name": "Widget",     "status": "Active", "price": 199.00, "createdAt": "2026-03-11T12:00:00+00:00" }
  ],
  "meta": {
    "totalItems": 37,
    "itemCount": 2,
    "itemsPerPage": 2,
    "totalPages": 19,
    "currentPage": 2
  },
  "links": {
    "first": "/products?limit=2&sortBy=price%3ADESC&filter.status=%24eq%3AActive&page=1",
    "previous": "/products?limit=2&sortBy=price%3ADESC&filter.status=%24eq%3AActive&page=1",
    "next": "/products?limit=2&sortBy=price%3ADESC&filter.status=%24eq%3AActive&page=3",
    "last": "/products?limit=2&sortBy=price%3ADESC&filter.status=%24eq%3AActive&page=19"
  }
}
```

`itemCount` is how many rows this page actually returned; `itemsPerPage` is the effective limit. `previous` is
`null` on the first page, `next` on the last; `first` and `last` are always present when a link context was
supplied.

## What the engine does with that request

```mermaid
flowchart TD
    Q["<code>?page=2&limit=25&sortBy=name:DESC&search=acme&filter.status=$eq:active</code>"]
    Q --> Bind["bind → <b>PaginateQuery</b>"]
    Bind --> Valid{"page ≥ 1 and<br/>1 ≤ limit ≤ MaxLimit?"}
    Valid -- no --> Err["<b>400</b> ProblemDetails<br/>title: Invalid query"]
    Valid -- yes --> Filter["<code>Where(...)</code> ← every <code>filter.field</code>"]
    Filter --> Search["<code>Where(...)</code> ← <code>search</code> over the searchBy fields"]
    Search --> Count["<code>COUNT(*)</code> → totalItems, totalPages"]
    Count --> Past{"skip ≥ totalItems?"}
    Past -- yes --> Empty["empty page, no second query"]
    Past -- no --> Sort["<code>OrderBy</code> ← sortBy / DefaultSortBy<br/>+ tie-breaker as the last key"]
    Sort --> Page["<code>Skip/Take</code> → project each row to the DTO"]
    Empty --> Resp["<b>PaginatedResponse</b>: Items · Meta · Links"]
    Page --> Resp
```

Two queries per request: one `COUNT(*)` over the filtered set, one page fetch. Asking for a page past the end
returns an empty `items` with the real `meta`, and skips the second query entirely.

## Without ASP.NET Core

The engine has no web dependency. Build the request yourself:

```csharp
var request = new PaginateQuery {
    Page = 2,
    Limit = 25,
    SortBy = ["name:DESC"],
    Search = "acme",
    Filters = new Dictionary<string, IReadOnlyList<string>> {
        ["status"] = ["$eq:Active"],
    },
};

var page = await db.Products.PaginateAsync<Product, ProductDto>(request, config, ct: ct);
```

`page.Links` will be `null` — links need a `PaginateLinkContext`, which the ASP.NET Core overloads build
from the current request. `page.Meta` still carries the full paging state. You can supply a context yourself;
see [Recipes → links outside ASP.NET Core](recipes.md#build-links-outside-aspnet-core).

## Next

- The complete wire format, operator by operator → **[Query-string contract](query-string.md)**
- Everything the builder can declare → **[Configuration](configuration.md)**
- Sub-collections, aggregates, computed fields → **[Projections](projections.md)**
