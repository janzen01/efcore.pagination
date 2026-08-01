---
title: ASP.NET Core
nav_order: 6
---

# ASP.NET Core

What `Janzen.Pagination.AspNetCore` adds on top of the engine: query-string binding, `400 ProblemDetails`,
navigation links built from the current request, and OpenAPI parameter documentation.

```bash
dotnet add package Janzen.Pagination.AspNetCore
```

## Registration

```csharp
builder.Services.AddPagination(pagination => pagination.AddAspNetCore());
builder.Services.AddControllers();
```

`AddAspNetCore()` configures `MvcOptions`: it inserts the `PaginateQuery` model binder at position 0 and adds
the `PaginateExceptionFilter`. Both only matter for **controllers** — a Minimal-API-only app can skip it and
still get everything, because `WithPagination<T>()` attaches its own endpoint filter and
`Request.ToPaginateQuery()` is an explicit call. You would then use `AddPagination(...)` only to select a
[LIKE strategy](providers-and-types.md) or register NodaTime.

---

## Controllers

```csharp
[ApiController]
[Route("products")]
public sealed class ProductController(AppDbContext db) : ControllerBase {

    [HttpGet]
    [PaginatedQuery<ProductPaginateConfigProvider>]
    public Task<PaginatedResponse<ProductDto>> List([FromQuery] PaginateQuery request, CancellationToken ct) =>
        db.Products.PaginateAsync<Product, ProductDto>(
            request, ProductPaginateConfigProvider.Config, this.Request, ct);

}
```

- `[FromQuery] PaginateQuery request` is filled by the model binder. Do **not** declare `page`, `limit`,
  `sortBy`… as separate action parameters.
- Passing `this.Request` (an `HttpRequest`) selects the overload that builds the link context, so the response
  carries `first`/`prev`/`next`/`last`. Omit it and `Links` is `null`.
- `[PaginatedQuery<TProvider>]` is metadata for OpenAPI only; it has no runtime effect on the query.

All four projection strategies have an `HttpRequest` mirror:

```csharp
db.Products.PaginateAsync<Product, ProductDto>(request, config, this.Request, ct);
db.Products.PaginateSelectAsync(request, config, selector, this.Request, ct);
db.Products.PaginateSelectMapAsync(request, config, selector, postMap, this.Request, ct);
db.Products.PaginateMapAsync(request, config, projector, this.Request, ct);
```

## Minimal APIs

```csharp
app.MapGet("/products", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        await db.Products.PaginateAsync<Product, ProductDto>(
            http.Request.ToPaginateQuery(), ProductPaginateConfigProvider.Config, http.Request, ct))
   .WithPagination<ProductPaginateConfigProvider>();
```

- `Request.ToPaginateQuery()` parses the same six parameters as the model binder.
- `WithPagination<TProvider>()` does two things: attaches the `[PaginatedQuery]` metadata so the operation
  transformer documents the parameters and the `400`, and adds `PaginateExceptionEndpointFilter` so a
  `PaginateQueryException` becomes a Problem Details response instead of a `500`.

---

## Errors as ProblemDetails

Every invalid query — a bad operator, an unknown sort field, an out-of-range limit — is a
`PaginateQueryException`. Both pipelines translate it identically:

| Pipeline | Translated by | Registered by |
|----------|---------------|---------------|
| Controllers | `PaginateExceptionFilter` (an `IExceptionFilter`) | `AddAspNetCore()` |
| Minimal APIs | `PaginateExceptionEndpointFilter` (an `IEndpointFilter`) | `WithPagination<T>()` |

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Invalid query",
  "status": 400,
  "detail": "Filter 'price' does not support operator '$ilike'.",
  "traceId": "00-…"
}
```

The `title` is always `Invalid query`; `detail` carries the specific message. The full list is in the
[error catalogue](query-string.md#error-catalogue). No per-action `try`/`catch` is needed anywhere.

The controller path builds the payload through the app's registered `ProblemDetailsFactory`, so your own
`AddProblemDetails` customisation (extra members, `type` URIs, trace identifiers) applies here too.

---

## Links

When you pass an `HttpRequest`, the response's `Links` are built from the current request:

```json
"links": {
  "first":    "/products?limit=25&filter.status=%24eq%3AActive&page=1",
  "previous": "/products?limit=25&filter.status=%24eq%3AActive&page=1",
  "next":     "/products?limit=25&filter.status=%24eq%3AActive&page=3",
  "last":     "/products?limit=25&filter.status=%24eq%3AActive&page=19"
}
```

On the last page the same object comes back with `"next": null`:

```json
"links": { "first": "…&page=1", "previous": "…&page=18", "next": null, "last": "…&page=19" }
```

- **Path-relative, no scheme or host.** Behind a proxy or a path base that is what you want; prefix them
  yourself if your clients need absolute URLs.
- Every current query parameter except `page` is preserved and re-escaped — including parameters the library
  does not know about, so client-side state survives paging.
- `first` and `last` are always present (`last` is at least page 1, even for an empty result set).
  `previous` is `null` on page 1; `next` is `null` on the last page and when there are no results. That `null`
  is serialized, not dropped — it is what tells the client there is no such page, so the shape of `links` is
  the same on every page.
- **No `HttpRequest`, no links.** `Links` is then `null` as a whole, i.e. `"links": null`. Callers outside
  ASP.NET Core page by `meta` instead.

### `Link` response header (RFC 8288)

Opt-in, in addition to the body:

```csharp
var page = await db.Products.PaginateAsync<Product, ProductDto>(request, config, this.Request, ct);
this.Response.AddPaginationLinkHeader(page.Links);
return page;
```

```http
Link: </products?limit=25&page=1>; rel="first", </products?limit=25&page=3>; rel="next", …
```

Absent links are skipped, and if none are present no header is written. Passing a `null` `Links` is a no-op,
so the call is safe on a page produced without a link context.

---

## OpenAPI

```csharp
using Janzen.Pagination.AspNetCore.OpenApi;

builder.Services.AddOpenApi(options =>
    options.AddOperationTransformer<PaginatedQueryOperationTransformer>());
```

`PaginatedQueryOperationTransformer` is a plain `IOpenApiOperationTransformer`, so your app owns the document
name and the rest of the pipeline. It acts only on operations carrying `[PaginatedQuery<TProvider>]` (or
`WithPagination<TProvider>()`), and reads the config through that provider.

For each such operation it adds:

- `page` and `limit`, with the resource's real `DefaultLimit` and `MaxLimit` in the description;
- `sortBy` and `searchBy`, listing the configured field names;
- `search`;
- one `filter.<field>` parameter per filterable field, listing the operators that field allows and carrying an
  example such as `$eq:42` — typed from the field's CLR type, and preferring `$ilike` when the PostgreSQL
  strategy is active and the field allows it;
- a documented `400` response, "The pagination query parameters were invalid."

Because the parameters are generated from the config, they cannot drift from what the engine enforces.

### Badges

`.ShowBadge("Admin only", "language-admin")` renders as an inline `<code>` chip appended to that parameter's
description. The `language-` prefix is required because it is the only class an API reference UI's markdown
sanitizer preserves there — see [Configuration → ShowBadge](configuration.md#showbadge). Colour it from the
reference UI's custom CSS:

```css
.language-admin { background: #8B1A1A; color: #fff; border-radius: 4px; padding: 1px 6px }
```

Fields gated with [`.When(...)`](configuration.md#when--conditional-fields) stay documented regardless of the
condition, so the published contract is the widest one; enforcement happens at query time.

---

## Unknown query parameters

The binder reads exactly `page`, `limit`, `sortBy`, `search`, `searchBy` and `filter.<field>`. Everything else
— `offset`, `utm_*`, your own client state — is ignored and the request pages normally.

This is deliberate. API-audit tools sometimes report it as "invalid value silently accepted"; strict binding
would instead reject consumers' own tracking parameters, which is worse. The two parameters where a wrong
value genuinely changes the result, `page` and `limit`, *are* validated and return `400`.
