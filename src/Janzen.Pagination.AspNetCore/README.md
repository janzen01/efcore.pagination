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
`first`/`previous`/`next`/`last` and `current`, built from the current request: path-relative — including the
app's `UsePathBase` prefix — with every other query parameter preserved. An absent link (`previous` on page 1,
`next` on the last page) is serialized as `null` rather than dropped, so the shape does not change per page;
`current` echoes the request and is always there. Omit the `HttpRequest` and `Links` is `null` as a whole. For
the [RFC 8288 header](https://janzen01.github.io/efcore.pagination/reference/response/#link-response-header-rfc-8288)
as well (a `null` `Links` writes no header):

```csharp
var page = await db.Products.PaginateAsync<Product, ProductDto>(request, config, this.Request, ct);
this.Response.AddPaginationLinkHeader(page.Links);
```

### What the client gets back

```json
{
  "items": [ { "id": "7f3c…", "name": "Widget Pro", "price": 249.00 } ],
  "meta":  { "totalItems": 26, "itemCount": 1, "itemsPerPage": 25, "totalPages": 2, "currentPage": 2,
             "sortBy": ["name:ASC"], "search": null, "searchBy": [],
             "filter": { "status": ["$eq:Active"] },
             "hasPreviousPage": true, "hasNextPage": false },
  "links": { "first": "/products?limit=25&filter.status=%24eq%3AActive&page=1",
             "previous": "/products?limit=25&filter.status=%24eq%3AActive&page=1",
             "next": null,
             "last": "/products?limit=25&filter.status=%24eq%3AActive&page=2",
             "current": "/products?limit=25&filter.status=%24eq%3AActive&page=2" }
}
```

`meta` echoes the **effective** request, not the raw one: this response reports `"sortBy": ["name:ASC"]` even
though the client sent no `sortBy`, because that is where `DefaultSortBy` landed — which is exactly what a
grid header needs to draw its arrow. Every key is always present; `null`, `[]` and `{}` carry the absent
cases. See
[Response contract](https://janzen01.github.io/efcore.pagination/reference/response/#the-request-echo).

### Errors

Any invalid query becomes [`400 Bad Request` with `title: "Invalid
query"`](https://janzen01.github.io/efcore.pagination/integrations/aspnetcore/#errors-as-problemdetails) and the
specific message as `detail` — via `PaginateExceptionFilter` for controllers (registered by `AddAspNetCore()`) or
`PaginateExceptionEndpointFilter` for Minimal APIs (registered by `WithPagination<T>()`). Both build the payload
through the app's `ProblemDetailsFactory` when one is registered, so the two pipelines answer with the same
members. No per-action `try`/`catch` needed.

Unknown query parameters are ignored, so clients keep their own tracking parameters; `page` and `limit` are
validated.

## Documentation

- [ASP.NET Core integration](https://janzen01.github.io/efcore.pagination/integrations/aspnetcore/)
- [OpenAPI](https://janzen01.github.io/efcore.pagination/integrations/aspnetcore/openapi/)
- [Getting started](https://janzen01.github.io/efcore.pagination/guide/getting-started/)
- [Query-string contract](https://janzen01.github.io/efcore.pagination/reference/query-string/)
- [Response contract](https://janzen01.github.io/efcore.pagination/reference/response/)
- [Errors](https://janzen01.github.io/efcore.pagination/reference/errors/)
- [Full guide](https://janzen01.github.io/efcore.pagination/)

## Debugging

The package ships **embedded PDBs with Source Link**, so a debugger steps straight into these sources at the exact
commit the version was built from. Nothing to configure: no symbol server, no separate symbol download, and it works
offline.

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/master/LICENSE) © Lubos Jansky
