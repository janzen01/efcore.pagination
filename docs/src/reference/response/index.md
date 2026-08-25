# Response contract

What comes back, field by field. The request side is the
[query-string contract](../query-string/); this is its counterpart, and it is just as much a contract — a
client reads `meta` to decide whether to fetch again.

```json
{
  "items": [
    { "id": "7f3c…", "name": "Widget Pro", "status": "Active", "price": 249.00 },
    { "id": "b1a9…", "name": "Widget",     "status": "Active", "price": 199.00 }
  ],
  "meta": {
    "totalItems": 37,
    "itemCount": 2,
    "itemsPerPage": 2,
    "totalPages": 19,
    "currentPage": 2,
    "sortBy": ["name:DESC"],
    "search": null,
    "searchBy": [],
    "filter": { "status": ["$eq:Active"] },
    "hasPreviousPage": true,
    "hasNextPage": true
  },
  "links": {
    "first":    "/products?limit=2&filter.status=%24eq%3AActive&page=1",
    "previous": "/products?limit=2&filter.status=%24eq%3AActive&page=1",
    "next":     "/products?limit=2&filter.status=%24eq%3AActive&page=3",
    "last":     "/products?limit=2&filter.status=%24eq%3AActive&page=19",
    "current":  "/products?limit=2&filter.status=%24eq%3AActive&page=2"
  }
}
```

The C# shape is three records, where `T` is the **projection's** result type, not the entity:

```csharp
sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, PaginatedMeta Meta, PaginatedLinks? Links);

sealed record PaginatedMeta(int TotalItems, int ItemCount, int ItemsPerPage, int TotalPages, int CurrentPage) {
    public IReadOnlyList<string> SortBy { get; init; }
    public string? Search { get; init; }
    public IReadOnlyList<string> SearchBy { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Filter { get; init; }
    public bool HasPreviousPage { get; init; }
    public bool HasNextPage { get; init; }
}

sealed record PaginatedLinks(string? First, string? Previous, string? Next, string? Last) {
    public string? Current { get; init; }
}
```

Those init-only members sit outside the positional lists on purpose, so each record's constructor,
`Deconstruct` and `with` keep the shape they had; the engine sets them, a caller never does.

Nothing here is serializer-specific: the JSON above is what ASP.NET Core's default camelCase settings
produce from those records. The tables below use the JSON names; the CLR members are the same names in
PascalCase.

## `items`

The rows of this page, in the query's sort order. Empty past the last page, which is **not** an error.

## `meta`

| Field | Type | Meaning |
|-------|------|---------|
| `totalItems` | `int` | Rows matching the filter and search across **all** pages, before paging is applied. This is the `COUNT` the engine runs first. |
| `itemCount` | `int` | Rows actually on this page. Smaller than `itemsPerPage` on the last page, `0` past the end. |
| `itemsPerPage` | `int` | The **effective** page size: the requested `limit`, or the config's `DefaultLimit` when `limit` was omitted. Never the maximum. |
| `totalPages` | `int` | Pages at this page size, or `0` when nothing matched. |
| `currentPage` | `int` | The 1-based page that was **requested**. Not clamped, so it can exceed `totalPages`. |
| `sortBy` | `string[]` | The **effective** order, in the request's own `"field:DIR"` form. See [the echo](#the-request-echo). |
| `search` | `string \| null` | The search term that ran, or `null`. |
| `searchBy` | `string[]` | The **effective** fields the term ran over. `[]` when no search ran. |
| `filter` | `object` | The request's filters, echoed verbatim per field. `{}` when there were none. |
| `hasPreviousPage` | `bool` | `currentPage > 1`. |
| `hasNextPage` | `bool` | `currentPage < totalPages`. `false` past the last page, where nothing follows either. |

Two of these are easy to get wrong from the outside:

- `currentPage` reports what was asked for, not what was served. `?page=500` against a 19-page result returns
  `"currentPage": 500` with `"itemCount": 0`. A client that trusts `currentPage` alone to mean "where I am"
  will loop; compare it against `totalPages`.
- `totalPages` is `0`, not `1`, for an empty result set. `currentPage >= totalPages` is therefore a correct
  loop-termination test even on the very first pass.

`meta` is always present and never null. It is the only navigation a caller needs — `links` is a convenience
on top of it.

### The request echo

`sortBy`, `search`, `searchBy` and `filter` report what the query **actually did**, which for the first three
is not the same as what arrived. Defaults are resolved on the server:

| Request | `meta.sortBy` | `meta.searchBy` |
|---------|---------------|-----------------|
| `?sortBy=name:DESC&search=wid&searchBy=name` | `["name:DESC"]` | `["name"]` |
| `?search=wid` (config: `DefaultSortBy("rank")`, two searchable fields) | `["rank:ASC"]` | `["name", "description"]` |
| `?page=2` | `["rank:ASC"]` | `[]` |

That is the whole point of the echo: a client rendering a grid header cannot draw the sort arrow or the
"searching in…" hint for a request it did not spell out, because only the config knows where the defaults
landed. Four details worth knowing:

- **Field names are canonical, not as typed.** Lookup is case-insensitive, so `?sortBy=NAME:desc` echoes
  `name:DESC`.
- **The tie-breaker is not listed.** `WithTieBreaker(...)` orders every page, but nobody requested it and no
  arrow belongs on it.
- **A field switched off by `When(false)` for this caller is absent**, from the defaults too — the same rule
  that makes it unrequestable.
- **`filter` is raw.** The values are the `"$op:value"` strings as received, grouped per field, which is what
  a filter chip renders. Only validated fields can appear: an unknown one is a `400`, so no envelope exists.

`hasPreviousPage` / `hasNextPage` carry the two comparisons every client would otherwise re-derive — and
`hasNextPage` is `false` past the last page, which the counters alone do not say without a second look.

Like everything else here, **all six keys are always present**, with `null` and `[]` and `{}` carrying the
absent cases rather than the key disappearing.

## `links`

`links` is **`null` as a whole** unless the call supplied a link context. Off the web there is no request for
a URL to be relative to, so there is nothing honest to put there:

```json
"links": null
```

With a link context, every key is present on every page, and an absent link carries `null`:

```json
"links": { "first": "…&page=1", "previous": "…&page=18", "next": null, "last": "…&page=19",
           "current": "…&page=19" }
```

| Link | `null` when |
|------|-------------|
| `first` | never |
| `previous` | on page 1 |
| `next` | on the last page, and whenever nothing matched |
| `last` | never — it is page 1 for an empty result set |
| `current` | never — it echoes the request, so it answers past the last page too |

`current` is the request that was made, not a clamped one: ask for page 900 of a 19-page result and it comes
back pointing at page 900, while `next` and `previous` say what is actually navigable. It is what a client
that stores "where am I" URLs — bookmarks, retry, restoring table state — would otherwise reassemble from
`meta` plus its own knowledge of the path.

**Those nulls are serialized, not omitted.** `"next": null` is the client's answer to "is there another
page", so dropping the key would force it to distinguish "no next page" from "this API does not send next
links". Keeping every key means `links` has the same shape on every page. Payload-size linters flag this;
it is deliberate.

The URLs are **path-relative — no scheme, no host.** Behind a proxy that is what you want; prefix them
yourself if your clients need absolute URLs. The app's **path base is part of the path**, so an app mounted
under `UsePathBase("/api")` emits `/api/products?…`. Every current query parameter except `page` is carried
over and percent-encoded, including parameters the library does not recognise, so client-side state survives
paging.

## Building links: `PaginateLinkContext`

`PaginateLinkContext` is a framework-agnostic record of a path and its query parameters. It lives in the
**core package**, not in `.AspNetCore` — the ASP.NET Core overloads simply build one from `HttpRequest` for
you, and anything else can build one by hand:

```csharp
var linkContext = new PaginateLinkContext(
    Path: "/api/products",
    QueryParameters: [
        new("limit", "25"),
        new("filter.status", "$eq:Active")
    ]);

var page = await source.PaginateAsync<Product, ProductDto>(request, config, linkContext, ct);
// page.Links.Next == "/api/products?limit=25&filter.status=%24eq%3AActive&page=3"
```

Three rules, and the first one bites:

- **Supply keys and values raw.** The builder percent-encodes both, so pre-escaping double-encodes them —
  `$eq:Active`, not `%24eq%3AActive`.
- Any `page` entry is **dropped and re-added** per link, so including one is harmless.
- Repeat a key to carry a multi-valued parameter (`sortBy`, `filter.<field>`).

Pass `null` — the default — and `Links` comes back `null`. That is a reasonable choice for an internal
caller, which pages by `meta` instead.

## Paging without links: `WithPage`

`PaginateQuery.WithPage(n)` returns the same request pointed at another page, carrying limit, sort, search
and filters across unchanged. It is how a caller with no link context navigates:

```csharp
var request = new PaginateQuery { Limit = 25, SortBy = ["createdAt:DESC"] };

while (true) {

    var page = await source.PaginateAsync<Product, ProductDto>(request, config, ct: ct);

    Process(page.Items);

    // totalPages is 0 for an empty result set, so this also ends the very first pass.
    if (!page.Meta.HasNextPage) break;

    request = request.WithPage(page.Meta.CurrentPage + 1);

}
```

`WithPage` does **not** validate: an out-of-range page is rejected when the query executes, so the `400` and
its wording come from one place. Note that `PaginateQuery` is a class rather than a record, and has no `with`
— value equality over its collection properties would compare by reference and lie — so `WithPage` is the
supported way to derive one request from another.

## `Link` response header (RFC 8288)

Opt-in, in addition to the body, and only in ASP.NET Core. Worth adding when a client reads headers before
bodies — a `HEAD` request, a crawler, a generic HTTP client with RFC 8288 support built in, or anything
streaming the body rather than deserialising it whole. If your clients only ever read `links` out of the
JSON, skip it: it is the same four URLs twice.

```csharp
var page = await db.Products.PaginateAsync<Product, ProductDto>(request, config, this.Request, ct);
this.Response.AddPaginationLinkHeader(page.Links);
return page;
```

```http
Link: </products?limit=25&page=1>; rel="first", </products?limit=25&page=3>; rel="next", …
```

Absent links are skipped rather than emitted empty, and if none are present no header is written at all.
Passing a `null` `Links` is a no-op, so the call is safe on a page produced without a link context.

## Errors

A rejected request produces no envelope. It is a `400` with a `ProblemDetails` body instead — see
[Errors](../errors/) for every message, and
[ASP.NET Core → Errors](/integrations/aspnetcore/#errors-as-problemdetails) for the wire shape.
