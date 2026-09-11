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

The JSON names above are **pinned by the library**, with `[JsonPropertyName]` on every member of the three
records, so the shape is the same whatever the host's `JsonSerializerOptions.PropertyNamingPolicy` says. They
match what ASP.NET Core's default camelCase settings would have produced anyway, which is why the common case
looks unchanged — but an application serving `snake_case` everywhere else still serves this envelope as it is
documented here. The tables below use the JSON names; the CLR members are the same names in PascalCase.

## `items`

The rows of this page, in the query's sort order. Empty past the last page, which is **not** an error.

## `meta`

| Field | Type | Meaning |
|-------|------|---------|
| `totalItems` | `int` | Rows matching the filter and search across **all** pages, before paging is applied. This is the `COUNT` the engine runs first. |
| `itemCount` | `int` | Rows actually on this page. Smaller than `itemsPerPage` on the last page, `0` past the end. |
| `itemsPerPage` | `int` | The **effective** page size: the requested `limit`, or the config's `DefaultLimit` when `limit` was omitted. Never the maximum. For an [unlimited read](../configuration/#allowunlimited) it is `itemCount` — the row count, since `-1` is not a size. |
| `totalPages` | `int` | Pages at this page size, or `0` when nothing matched. |
| `currentPage` | `int` | The 1-based page that was **requested**. Not clamped, so it can exceed `totalPages`. |
| `sortBy` | `string[]` | The **effective** order, in the request's own `"field:DIR"` form. See [the echo](#the-request-echo). |
| `search` | `string \| null` | The search term that ran, or `null`. |
| `searchBy` | `string[]` | The **effective** fields the term ran over. `[]` when no search ran. |
| `filter` | `object` | The request's filters, echoed verbatim per field. `{}` when there were none. |
| `hasPreviousPage` | `bool` | `currentPage > 1`. |
| `hasNextPage` | `bool` | Whether a next page can be **requested**. Normally `currentPage < totalPages`; where the config sets [`WithMaxOffset`](../configuration/#withmaxoffset) it stops at the last reachable page instead, so it is never `true` for a page the same config would answer with a `400`. `false` past the last page. |

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
Where the resource caps the offset it is also the only honest answer: `totalPages` still reports the pages the
data has, and `hasNextPage` reports the ones you may ask for. The two differ on purpose — a client can see
there is more data than paging will reach rather than discovering it as a `400`.

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

`next` and `last` are drawn from the last **reachable** page, not from `totalPages`: with
[`WithMaxOffset`](../configuration/#withmaxoffset) they stop where the guard does, so following `next` can
never walk into a `400`. An [unlimited read](../configuration/#allowunlimited) is one page, so `first`,
`last` and `current` are the same URL and both `next` and `previous` are `null`.
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

Four rules, and the first two bite:

- **Supply keys and values raw.** The builder percent-encodes both, so pre-escaping double-encodes them —
  `$eq:Active`, not `%24eq%3AActive`.
- **Supply the path already escaped.** It is emitted verbatim before the `?`, so the opposite rule applies to
  it: in ASP.NET Core take `PathString.ToUriComponent()`, elsewhere escape each segment with
  `Uri.EscapeDataString` and join with `/`. A path carrying a character no URI path can hold — a space or
  ``? # " < > \ ` ^ { | }`` — is rejected with an `ArgumentException` at construction, because emitting it
  would produce a link to a different resource: `new PaginateLinkContext($"/t/{slug}/products", [])` with
  `slug = "x?y"` yields `/t/x?y/products?page=1`, whose query string is `y/products?page=1`. A `null` path or
  parameter list is rejected the same way, with `ArgumentNullException`.
- Any `page` entry is **dropped and re-added** per link, so including one is harmless.
- Repeat a key to carry a multi-valued parameter (`sortBy`, `filter.<field>`).

Pass `null` — the default — and `Links` comes back `null`. That is a reasonable choice for an internal
caller, which pages by `meta` instead.

Two contexts holding the same path and the same parameters in the same order **compare equal** and hash
equal, so a factory that builds one from your own transport is testable with `Assert.Equal`. Order is part of
the value, because it is the order the parameters are emitted in.

::: warning The parameter set is repeated, and the repetition is the cost
Every parameter is carried into all five links and, if you write the header, into each of its four rels.
Measured on a request whose query string is 8 199 bytes: **41 081 bytes of body links (×5.0) and a 32 928-byte
`Link` header (×4.0)**; the ratios hold from a few hundred bytes upwards. Percent-encoding is round-trip
neutral here — the repetition is the whole of it.

That matters mostly for the header: a reverse proxy buffers response headers in a small fixed buffer —
nginx's `proxy_buffer_size` defaults to one memory page, 4 or 8 KB — and a header over it becomes a
proxy-generated `502` the application never sees. If you emit the header and your callers send long query
strings, either cap the request line below the proxy's header buffer ÷ 4, or build the context from a
filtered parameter list. The library carries everything by design: the binder ignores parameters it does not
recognise precisely because they are yours, and dropping them from navigation links would lose them.
:::

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
— it carries no equality at all rather than one that would compare its collection properties by reference and
lie — so `WithPage` is the supported way to derive one request from another. The envelope records took the
other route; see below.

## Comparing envelopes

`PaginatedResponse<T>`, `PaginatedMeta` and `PaginatedLinks` compare **by value**, all the way down:

```csharp
// Two responses to the same request, fetched separately.
first == second   // true
```

That needs saying because it is not what the record shape gives you for free. A record's synthesized equality
runs every member through `EqualityComparer<T>.Default`, which is reference equality for a list or a
dictionary — so `items`, `sortBy`, `searchBy` and `filter` would have made two envelopes describing the same
page compare unequal. `PaginatedResponse<T>` and `PaginatedMeta` therefore hand-write `Equals` and
`GetHashCode`; `PaginatedLinks` holds nothing but strings, so the synthesized pair is already right for it.
The rules:

- **`items` compares element by element**, each through `T`'s own equality. A projection record compares by
  value; a projection declared as a class compares by reference, because that is its contract, not the
  envelope's.
- **Order is part of the value** for `items`, `sortBy` and `searchBy`. A page is an ordered thing.
- **`filter` is order-independent** — a dictionary has no order — and its **keys match ordinally**. It echoes
  the request's field names verbatim, so `Status` and `status` are different echoes even though the field
  lookup that produced them is case-insensitive.
- Equal envelopes hash equal, so they work as dictionary keys and in a `HashSet`.

An envelope deserialized from a payload carrying explicit `null`s where the contract promises a list reports
inequality rather than throwing.

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

The rels are **appended** as a further `Link` header field rather than assigned, so a relation the response
already carries — a `describedby` written by your handler or by middleware — survives; RFC 8288 §3 allows
several `Link` fields and conforming clients read them as one set. The flip side is that calling
`AddPaginationLinkHeader` twice for one response emits the pagination rels twice. Mind the size, too: the
header repeats the request's whole query string once per rel — see the warning under
[`PaginateLinkContext`](#building-links-paginatelinkcontext).

## Errors

A rejected request produces no envelope. It is a `400` with a `ProblemDetails` body instead — see
[Errors](../errors/) for every message, and
[ASP.NET Core → Errors](/integrations/aspnetcore/#errors-as-problemdetails) for the wire shape.
