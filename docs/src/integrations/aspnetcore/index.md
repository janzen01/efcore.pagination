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

`AddAspNetCore()` configures `MvcOptions`: it inserts the `PaginateQuery` model binder
(`PaginateQueryModelBinderProvider`) at position 0 and adds the `PaginateExceptionFilter`. Both only matter
for **controllers** — a Minimal-API-only app can skip it and still get everything, because
`WithPagination<T>()` attaches its own endpoint filter and `Request.ToPaginateQuery()` is an explicit call.
You would then use `AddPagination(...)` only to select a [LIKE strategy](../postgresql/) or register NodaTime.

The lambda's parameter is an `IPaginationBuilder`. Every add-on hangs an extension method off it, which is
why they all read the same way regardless of which package they come from. It exposes one member, `Services`,
the underlying `IServiceCollection` — the escape hatch for registering your own types inside the same block:

```csharp
builder.Services.AddPagination(pagination => {
    pagination.AddAspNetCore().UsePostgreSql();
    pagination.Services.AddSingleton<ProductPaginateConfigProvider>();
});
```

## Two layers name the same config

This is the part worth getting straight before writing an endpoint, because the two halves are wired
separately and **nothing checks that they agree**:

| | Names the config as | Does |
|---|---|---|
| `[PaginatedQuery<TProvider>]` / `WithPagination<TProvider>()` | a **provider type** | documents the operation — [OpenAPI](./openapi/) reads the config through it |
| the `config` argument to `Paginate*Async` | a **config instance** | enforces it at run time |

The attribute is `PaginatedQueryAttribute<TConfigProvider>`, constrained
`where TConfigProvider : IPaginateConfigProvider` and applicable to methods only;
`WithPagination<TConfigProvider>()` carries the same constraint and attaches the same metadata. Note the
constraint is the **non-generic** `IPaginateConfigProvider`, so a provider satisfies it either way.

So an endpoint documented with one provider and executed against a different config compiles, runs, and
publishes a contract it does not honour. Point both at the same place — a `static readonly` field on the
provider is the shortest way to make that hard to get wrong:

```csharp
public sealed class ProductPaginateConfigProvider : IPaginateConfigProvider<Product> {
    public readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
        .WithLimits(25, 100)
        .Sortable("name", p => p.Name)
        .WithTieBreaker(p => p.Id));

    public PaginateConfig<Product> GetConfig() => Config;
}
```

**Does the provider need to be registered in DI?** Usually no. The OpenAPI transformer asks the container
first and falls back to `ActivatorUtilities.CreateInstance`, which builds a type with a parameterless
constructor without it ever being registered. Register it when its constructor takes services, or when the
provider holds state worth sharing — a registered instance is the one the transformer asks, and one it
activated itself it also disposes. Note that even then, nothing injects the provider into your action: the
interface exists so the attribute has a type to name, while your handler reads the config directly.

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
- **It also takes a route group**, so a set of endpoints is marked once rather than per handler. Applying it to
  the group is not optional decoration: an endpoint mapped inside a group that was never marked carries neither
  the metadata nor the filter, so its `?page=0` escapes as a `500`.

  ```csharp
  var products = app.MapGroup("/products").WithPagination<ProductPaginateConfigProvider>();

  products.MapGet("/", /* … */);
  products.MapGet("/archived", /* … */);
  ```

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
  "code": "FilterOperatorNotAllowed",
  "traceId": "00-…"
}
```

The `title` is always `Invalid query`; `detail` carries the specific message. The full list is in the
[error catalogue](/reference/errors/). No per-action `try`/`catch` is needed anywhere.

`code` is the machine-readable cause — the name of the `PaginateQueryError` member the engine rejected with,
also available in process as `PaginateQueryException.Code`. Branch on it rather than on `detail`, whose
wording is prose: matching the prose pins the wording for your client permanently and cannot survive
localisation. New members are added as the engine grows new rejections, so treat an unrecognised value the
way you would treat `Unspecified`. The library owns this member on its own `400`, so pick another name for
an extension of your own rather than writing `code` from `CustomizeProblemDetails`.

Both responses are served as `Content-Type: application/problem+json`, the media type
[RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) reserves for this payload and the only one the
[generated OpenAPI document](./openapi/) publishes the `400` under.

Each leg is enriched **once**, at its own framework's enrichment point: the controller payload is built by
the app's registered `ProblemDetailsFactory`, the Minimal API payload by the framework's problem-details
writer when the result executes. So an `AddProblemDetails` customisation (extra members, `type` URIs, trace
identifiers) applies to either, and the same error comes back with the same members whichever pipeline served
it. The endpoint filter deliberately does **not** pre-build its payload through `ProblemDetailsFactory` as
well: `Results.Problem` is written by `IProblemDetailsService`, which runs `CustomizeProblemDetails` itself,
so a customizer that adds a key — which is what the documented sample does — would throw on the second pass
and turn the `400` into a `500`.

`type` is filled in on both legs by the framework's problem-details defaults, so it is there even in a
Minimal-API-only app. `traceId` is **per leg, not per app**: on the controller leg it comes from
`ProblemDetailsFactory`, which MVC registers, so it is there whenever MVC services are; on the Minimal API
leg it comes from the problem-details writer, so it is there only if the app called `AddProblemDetails()`.
Registering MVC does not put it on a Minimal API response — an app with controllers but no
`AddProblemDetails()` sends `traceId` from its controller `400`s and not from its Minimal API ones.

---

## Links

When you pass an `HttpRequest`, the response's `Links` are built from the current request:

```json
"links": {
  "first":    "/products?limit=25&filter.status=%24eq%3AActive&page=1",
  "previous": "/products?limit=25&filter.status=%24eq%3AActive&page=1",
  "next":     "/products?limit=25&filter.status=%24eq%3AActive&page=3",
  "last":     "/products?limit=25&filter.status=%24eq%3AActive&page=19",
  "current":  "/products?limit=25&filter.status=%24eq%3AActive&page=2"
}
```

Every current query parameter except `page` is preserved and re-escaped — including ones the library does not
recognise, so client-side state survives paging. The URLs are **path-relative, with no scheme or host**, which
is what you want behind a proxy; prefix them yourself if your clients need absolute URLs. `Request.PathBase`
is part of the path, so an app mounted with `UsePathBase("/api")` emits `/api/products?…` rather than links
that 404.

**No `HttpRequest`, no links.** `Links` then comes back `null` as a whole, and callers page by `meta` instead.

When each individual link is `null`, why those nulls are serialized rather than dropped, the opt-in RFC 8288
`Link` header, and how to build a link context outside ASP.NET Core are all in
[Response contract](/reference/response/).

---

## OpenAPI

```csharp
using Janzen.Pagination.AspNetCore.OpenApi;

builder.Services.AddOpenApi(options =>
    options.AddOperationTransformer<PaginatedQueryOperationTransformer>());
```

It acts only on operations carrying `[PaginatedQuery<TProvider>]` or `WithPagination<TProvider>()`, and reads
the config through that provider — so the documented parameters are generated from the same declaration the
engine enforces and cannot drift from it.

What it emits parameter by parameter, how types and examples are derived, how badges render and why the
`language-` prefix is mandatory: **[OpenAPI](./openapi/)**.

---

## `PaginateQuery` always binds from the query string

The provider sits at position 0 and matches on the **parameter type**, so every `PaginateQuery` action
parameter is filled from `Request.Query` whatever binding source it carries. `[FromQuery]` is the honest
spelling and the one every sample here uses; `[FromBody]`, `[FromHeader]` and `[FromRoute]` are accepted by
the compiler and then ignored, and the request pages from the query string as usual.

This is deliberate rather than incidental: an `[ApiController]` infers `BindingSource.Body` for a bare
`PaginateQuery` parameter, and ASP.NET Core cannot tell that inference apart from an explicit `[FromBody]`.
Standing down for `Body` would therefore answer a working `GET /products?page=2` with
*"A non-empty request body is required."*

The one opt-out is the framework's own per-parameter override. A parameter carrying
`[ModelBinder(typeof(MyBinder))]` is left to `MyBinder`:

```csharp
public Task<PaginatedResponse<ProductDto>> List([ModelBinder(typeof(MyBinder))] PaginateQuery request, …)
```

## Unknown query parameters

The binder reads exactly `page`, `limit`, `sortBy`, `search`, `searchBy` and `filter.<field>`. Everything else
— `offset`, `utm_*`, your own client state — is ignored and the request pages normally.

This is deliberate. API-audit tools sometimes report it as "invalid value silently accepted"; strict binding
would instead reject consumers' own tracking parameters, which is worse. The two parameters where a wrong
value genuinely changes the result, `page` and `limit`, *are* validated and return `400`.
