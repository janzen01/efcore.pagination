# Pagination without ASP.NET Core

The engine has no web dependency. A gRPC service, a console tool, a background worker or a test all use the
same `PaginateConfig` and the same `Paginate*Async` methods — only the request has to come from somewhere
other than a query string.

## Build the request yourself

`PaginateQuery` is a plain object. Its properties carry exactly what the six query parameters carry, in the
same string form, so the [query-string contract](/reference/query-string/) still describes what the values
mean:

```csharp
var request = new PaginateQuery {
    Page   = 2,
    Limit  = 25,
    SortBy = ["createdAt:DESC", "name:ASC"],     // one entry per sort, in priority order
    Search = searchTerm,
    Filters = new Dictionary<string, IReadOnlyList<string>> {
        ["status"] = ["$eq:Active"],
        ["price"]  = ["$gte:100", "$lte:500"],   // several criteria on one field
    },
};

var page = await db.Products.PaginateAsync<Product, ProductDto>(request, config, ct: ct);
```

Note that the values keep their `$op:` prefixes. There is no typed filter API and that is deliberate: the
engine parses one grammar, so a filter behaves identically whether it arrived over HTTP or was written here
by hand — including which `400` it produces.

Omit `Page` and it is `1`; omit `Limit` and the config's `DefaultLimit` applies.

## Translate the failures

Validation is the same, so the same mistakes throw. Off the web there is no `ProblemDetails` filter to catch
them, so a `PaginateQueryException` reaches your code:

```csharp
try {
    var page = await source.PaginateAsync<Product, ProductDto>(request, config, ct: ct);
    return Ok(page);
}
catch (PaginateQueryException ex) {
    // The message is written to be safe to show a caller — it names fields and operators, never columns.
    throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
}
```

Every message it can carry is in [Errors](/reference/errors/). Anything else that escapes — an
`InvalidOperationException` from projection, say — is a bug in your configuration rather than in the caller's
request, and should not be mapped to an invalid-argument status.

## Walk every page

`Links` comes back `null`, because a URL needs a request to be relative to and there is none. `Meta` carries
the whole paging state instead, and `WithPage` turns it back into a request:

```csharp
var request = new PaginateQuery { Limit = 500, SortBy = ["id:ASC"] };

while (true) {

    var page = await source.PaginateAsync<Product, ProductDto>(request, config, ct: ct);

    await WriteBatchAsync(page.Items, ct);

    if (page.Meta.CurrentPage >= page.Meta.TotalPages) break;

    request = request.WithPage(page.Meta.CurrentPage + 1);

}
```

The termination test is `CurrentPage >= TotalPages` rather than a null check, and it is correct on the first
pass too: `TotalPages` is `0` for an empty result set. See
[Response contract](/reference/response/#paging-without-links-withpage) for what each `Meta` field reports.

For a large export, prefer a big `Limit` and a keyset-friendly sort over deep paging — every page still costs
a `COUNT` plus an `OFFSET` the database has to count through.

## Emit links anyway

If your transport does have something worth putting in a link — a REST facade in front of the service, a
resumable job that stores its position — supply a
[`PaginateLinkContext`](/reference/response/#building-links-paginatelinkcontext) and `Links` is populated
exactly as it is on the web:

```csharp
var linkContext = new PaginateLinkContext(
    Path: "/api/products",
    QueryParameters: [new("limit", "25"), new("filter.status", "$eq:Active")]);

var page = await source.PaginateAsync<Product, ProductDto>(request, config, linkContext, ct);
```

Values go in raw — the builder percent-encodes them.

## Without a database

A plain `IQueryable` works too, which is what makes a config testable in-process. See
[Testing your pagination](../testing/) for what changes on that path.
