# Janzen.Pagination.AspNetCore

ASP.NET Core integration for [Janzen.Pagination](https://github.com/janzen01/efcore.pagination).

Wires the pagination engine into the web pipeline:

- **Query-string model binding** — bind `PaginateQuery` straight from the request
  (`?page=&limit=&sortBy=&search=&filter.<field>=$op:value`).
- **`ProblemDetails` error handling** — invalid queries surface as consistent `400` responses.
- **Pagination links** — `first` / `previous` / `next` / `last` built from the current request.
- **OpenAPI metadata** — documents the pagination query parameters on annotated endpoints; per-field badges
  (configured via `.ShowBadge(...)`) render as chips in the API reference UI, colorable via your custom CSS. Fields
  gated with `.When(...)` stay documented (widest surface) and are enforced at runtime.

The contract it binds is borrowed from [nestjs-paginate](https://github.com/ppetzold/nestjs-paginate)
(MIT) — the same parameters, operator names and response envelope.

## Install

```bash
dotnet add package Janzen.Pagination.AspNetCore
```

Requires [`Janzen.Pagination.EntityFrameworkCore`](https://www.nuget.org/packages/Janzen.Pagination.EntityFrameworkCore)
(referenced transitively).

## Usage

```csharp
// Program.cs — register the query-string model binder and the 400 ProblemDetails filter.
services.AddPagination(pagination => pagination.AddAspNetCore());

// Register the OpenAPI operation transformer on your document so the pagination query
// parameters are documented on endpoints annotated with [PaginatedQuery<TConfigProvider>].
// (The transformer is a normal IOpenApiOperationTransformer — your app owns the document name.)
using Janzen.Pagination.AspNetCore.OpenApi;
services.AddOpenApi(options => options.AddOperationTransformer<PaginatedQueryOperationTransformer>());

// Controller — PaginateQuery is bound from the query string.
[HttpGet]
[PaginatedQuery<ProductConfigProvider>]
public Task<PaginatedResponse<ProductDto>> Get([FromQuery] PaginateQuery request) =>
    _dbContext.Products.PaginateAsync<Product, ProductDto>(request, _config, HttpContext.Request);
```

### Minimal API

```csharp
app.MapGet("/products", async (HttpContext http, AppDbContext db) =>
        await db.Products.PaginateAsync<Product, ProductDto>(http.Request.ToPaginateQuery(), config, http.Request))
   .WithPagination<ProductConfigProvider>();
```

`WithPagination<TConfigProvider>()` attaches the OpenAPI pagination parameters and the documented `400`
response, and maps `PaginateQueryException` to a `400` Problem Details via an endpoint filter.
`Request.ToPaginateQuery()` builds the `PaginateQuery` from the query string.

### Links and the `Link` header

Passing the `HttpRequest` to a `Paginate*Async` call is what fills `response.Links` with
`first`/`prev`/`next`/`last`, built from the current request: path-relative, with every other query parameter
preserved. An absent link (`prev` on page 1, `next` on the last page) is serialized as `null` rather than
dropped, so the shape does not change per page. Omit the `HttpRequest` and `Links` is `null` as a whole. For
the RFC 8288 header as well (a `null` `Links` writes no header):

```csharp
var page = await db.Products.PaginateAsync<Product, ProductDto>(request, config, this.Request, ct);
this.Response.AddPaginationLinkHeader(page.Links);
```

### Errors

Any invalid query becomes `400 Bad Request` with `title: "Invalid query"` and the specific message as
`detail` — via `PaginateExceptionFilter` for controllers (registered by `AddAspNetCore()`) or
`PaginateExceptionEndpointFilter` for Minimal APIs (registered by `WithPagination<T>()`). No per-action
`try`/`catch` needed.

Unknown query parameters are ignored, so clients keep their own tracking parameters; `page` and `limit` are
validated.

## Documentation

- [ASP.NET Core integration](https://github.com/janzen01/efcore.pagination/blob/master/docs/guide/aspnetcore.md)
- [Getting started](https://github.com/janzen01/efcore.pagination/blob/master/docs/guide/getting-started.md)
- [Query-string contract](https://github.com/janzen01/efcore.pagination/blob/master/docs/guide/query-string.md)
- [Full guide](https://github.com/janzen01/efcore.pagination/tree/master/docs/guide)

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/master/LICENSE) © Lubos Jansky
