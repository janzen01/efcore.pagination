# Janzen.Pagination — guide

Three pages, in reading order. Each stands on its own, so jumping straight to the one you need also works.

| Page | What it answers |
|------|-----------------|
| **[Getting started](./getting-started/)** | Install, register, and get a paginated endpoint returning JSON — plus what the engine does with the request once it arrives. |
| **[Configuration](./configuration/)** | What an allow-list buys you, and the three decisions every config has to make. |
| **[Projections](./projections/)** | The four `Paginate*Async` entry points and how to pick between them. |

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

The engine works on its own against any `IQueryable<T>`; the ASP.NET Core, PostgreSQL and NodaTime packages
are independent add-ons, so take only the ones you need.

## When the guide is not what you want

The guide is prose you follow once. The rest of the site is shaped for other moments:

- **[Reference](/reference/query-string/)** is looked up mid-task — the request and response contracts, every
  builder method and what it refuses, the [composers](/reference/composers/) that hand the query back
  unexecuted, and every `400`.
- **[Integrations](/integrations/)** is per package: what changes when you add one.
- **[Cookbook](/recipes/)** is task-shaped — role-based configs, collection filters, aggregates, non-web
  callers, testing.
