---
title: Recipes
nav_order: 8
---

# Recipes

Task-shaped answers. Each one is self-contained.

- [Role-based configurations](#role-based-configurations)
- [Filter by a value on a child collection](#filter-by-a-value-on-a-child-collection)
- [Return aggregates alongside the page](#return-aggregates-alongside-the-page)
- [Expose the contract as metadata](#expose-the-contract-as-metadata)
- [Build links outside ASP.NET Core](#build-links-outside-aspnet-core)
- [Navigate without ASP.NET Core](#navigate-without-aspnet-core)
- [Paginate from a gRPC or console caller](#paginate-from-a-grpc-or-console-caller)
- [Test a config without a database](#test-a-config-without-a-database)
- [See the SQL a projection compiles to](#see-the-sql-a-projection-compiles-to)
- [Keep a big table's page count cheap](#keep-a-big-tables-page-count-cheap)

---

## Role-based configurations

`.When(...)` captures a boolean at **build** time, so per-user gating means choosing between pre-built configs
rather than rebuilding one per request. Two roles, two cached configs, one builder:

```csharp
public sealed class ArticlePaginateConfigProvider : IPaginateConfigProvider<Article> {

    private static PaginateConfig<Article> Build(bool isModerator) => PaginateConfig<Article>.Create(b => b
        .WithLimits(20, 100)
        .Sortable("title", a => a.Title)
        .Sortable("published", a => a.Published)
        .DefaultSortBy("published", PaginateSortDirection.Desc)
        .WithTieBreaker(a => a.Id)
        .Searchable("title", a => a.Title)
        .Filterable("published", a => a.Published,
            PaginateFilterOperator.LessThanOrEqual, PaginateFilterOperator.GreaterThanOrEqual)
        .Filterable("isHidden", a => a.IsHidden, PaginateFilterOperator.Eq)
            .When(isModerator).ShowBadge("Moderator", "language-moderator"));

    public readonly static PaginateConfig<Article> Public    = Build(isModerator: false);
    public readonly static PaginateConfig<Article> Moderator = Build(isModerator: true);

    // The provider feeds OpenAPI. Return the widest documented surface — .When keeps the gated
    // fields in the metadata on both configs, so either works; the public one is the honest default.
    public PaginateConfig<Article> GetConfig() => Public;

}
```

```csharp
[HttpGet]
[PaginatedQuery<ArticlePaginateConfigProvider>]
public Task<PaginatedResponse<ArticleDto>> List([FromQuery] PaginateQuery request, CancellationToken ct) {
    var config = this.User.IsInRole("moderator")
        ? ArticlePaginateConfigProvider.Moderator
        : ArticlePaginateConfigProvider.Public;

    return db.Articles.PaginateAsync<Article, ArticleDto>(request, config, this.Request, ct);
}
```

A caller without the role filtering on `isHidden` gets the same `400` as one naming a field that does not
exist, so the gated field cannot be probed by comparing error messages. Note that this gates the **query**, not
the **rows** — restrict the underlying `IQueryable` as usual if the rows themselves are privileged.

## Filter by a value on a child collection

```csharp
.FilterableMany("tag", a => a.Tags, t => t.Slug,
    PaginateFilterOperator.Eq, PaginateFilterOperator.In, PaginateFilterOperator.ILike)
```

```http
GET /articles?filter.tag=$eq:dotnet
GET /articles?filter.tag=$in:dotnet,efcore      ← either tag
```

Translates to `WHERE EXISTS (SELECT 1 FROM tags … WHERE slug = @p)`. The operator applies to a single element,
so `$in` means *has an element matching any of these*.

Need *has **all** of these*? Repeat the parameter — criteria on one field default to `AND`:

```http
GET /articles?filter.tag=$eq:dotnet&filter.tag=$eq:efcore
```

That is two independent `EXISTS` clauses ANDed together, which is the set-containment semantics you want.

## Return aggregates alongside the page

Aggregates belong in a selector, not in the automatic projection:

```csharp
public sealed record ArticleSummary(Guid Id, string Title, int CommentCount, IReadOnlyList<string> Tags);

var page = await db.Articles.PaginateSelectAsync(request, config, a => new ArticleSummary(
    a.Id,
    a.Title,
    a.Comments.Count,
    a.Tags.Select(t => t.Slug).ToList()
), this.Request, ct);
```

One query; the `SELECT` mentions only these columns. If an aggregate needs CLR logic EF cannot translate —
rounding, a divide-by-zero guard — project the raw ingredients and finish them with
[`PaginateSelectMapAsync`](projections.md#paginateselectmapasync--sql-then-finish-in-memory).

## Expose the contract as metadata

Every config can describe itself, which is handy for a self-documenting endpoint or an admin UI that builds
filter controls:

```csharp
[HttpGet("meta")]
public object Meta() {
    IPaginateConfig config = ArticlePaginateConfigProvider.Public;

    return new {
        defaultLimit = config.DefaultLimit,
        maxLimit     = config.MaxLimit,
        sortable     = config.SortableFields.Select(f => f.Name),
        searchable   = config.SearchableFields.Select(f => f.Name),
        filterable   = config.FilterableFields.Select(f => new {
            name      = f.Name,
            type      = f.Type.Name,
            operators = f.Operators.Select(op => op.ToString()),
        }),
    };
}
```

The same metadata is what the [OpenAPI transformer](aspnetcore.md#openapi) reads, so the two cannot disagree.

## Build links outside ASP.NET Core

`PaginateLinkContext` is a framework-agnostic pair of *path* and *query parameters*. The ASP.NET Core overloads
build one from `HttpRequest`; anywhere else, build it yourself:

```csharp
var linkContext = new PaginateLinkContext(
    Path: "/api/products",
    QueryParameters: [
        new("limit", "25"),
        new("filter.status", "$eq:Active"),
        // 'page' is stripped and re-added per link — including it here is harmless.
    ]);

var page = await source.PaginateAsync<Product, ProductDto>(request, config, linkContext, ct);
// page.Links.Next == "/api/products?limit=25&filter.status=%24eq%3AActive&page=3"
```

Pass `null` (the default) and `Links` itself is `null` — a perfectly good choice for internal callers, who page
by `Meta` instead.

## Navigate without ASP.NET Core

A URL is only useful where there is a request to be relative to, so off the web there is nothing to follow.
`Meta` carries the paging state and `WithPage` turns it back into a request:

```csharp
var request = new PaginateQuery { Limit = 25, SortBy = ["createdAt:DESC"] };

while (true) {

    var page = await source.PaginateAsync<Product, ProductDto>(request, config, ct: ct);

    Process(page.Items);

    // TotalPages is 0 for an empty result set, so this also ends the very first pass.
    if (page.Meta.CurrentPage >= page.Meta.TotalPages) break;

    request = request.WithPage(page.Meta.CurrentPage + 1);

}
```

`WithPage` carries limit, sort, search and filters over — only the page changes — so the derived request stays
the same query. It does not validate the page: as everywhere else, an out-of-range page is rejected on
execution, so the `400` and its message come from one place.

## Paginate from a gRPC or console caller

There is no web dependency in the engine. Construct the request directly:

```csharp
var request = new PaginateQuery {
    Page   = pageNumber,
    Limit  = pageSize,
    SortBy = ["createdAt:DESC"],
    Search = searchTerm,
    Filters = new Dictionary<string, IReadOnlyList<string>> {
        ["status"] = ["$eq:Active"],
        ["price"]  = ["$gte:100", "$lte:500"],   // several criteria on one field
    },
};

var page = await db.Products.PaginateAsync<Product, ProductDto>(request, config, ct: ct);
```

The same validation applies — out-of-range values throw `PaginateQueryException`, which you translate to
whatever your transport uses (`InvalidArgument` for gRPC, an exit code for a CLI).

## Test a config without a database

Against a plain `IQueryable`, the engine swaps `EF.Functions.Like` for `string.IndexOf(..., OrdinalIgnoreCase)`
and the async terminal operators for their synchronous equivalents. Filters, search, sort, paging and
projection all still run, so a config is testable in-process:

```csharp
var products = new List<Product> {
    new() { Id = Guid.NewGuid(), Name = "Widget",     Status = ProductStatus.Active,        Price = 10m },
    new() { Id = Guid.NewGuid(), Name = "Wid-gadget", Status = ProductStatus.Active,        Price = 30m },
    new() { Id = Guid.NewGuid(), Name = "Gizmo",      Status = ProductStatus.Discontinued,  Price = 20m },
}.AsQueryable();

var request = new PaginateQuery {
    Limit = 10,
    Search = "wid",
    Filters = new Dictionary<string, IReadOnlyList<string>> { ["status"] = ["$eq:Active"] },
};

var page = await products.PaginateAsync<Product, ProductDto>(request, config);

Assert.Equal(2, page.Meta.TotalItems);                                  // search is case-insensitive
Assert.Equivalent(["Widget", "Wid-gadget"], page.Items.Select(p => p.Name));
```

Assert the **set**, not the order, unless the test data pins the sort keys: LINQ-to-Objects orders strings with
the current culture's comparer, which is not what the database will do.

This is a test of your **configuration** — that the right fields are exposed with the right operators, and that
a bad request is rejected. It is not a test of the generated SQL; LINQ-to-Objects and a real provider do not
always agree on collation and null ordering.

Rejections are equally testable:

```csharp
var ex = await Assert.ThrowsAsync<PaginateQueryException>(() =>
    products.PaginateAsync<Product, ProductDto>(
        new PaginateQuery { Filters = new Dictionary<string, IReadOnlyList<string>> {
            ["price"] = ["$ilike:10"] } }, config));

Assert.Contains("does not support operator", ex.Message);
```

## See the SQL a projection compiles to

The engine composes onto the `IQueryable` you hand it and then executes, so there is no handle to call
`ToQueryString()` on mid-flight. Two ways to look:

**Before you commit to a selector** — apply the same `Select` yourself. This needs a configured `DbContext`,
not a running database:

```csharp
string sql = db.Products
    .Select(p => new ProductSummary(p.Id, p.Name, p.Reviews.Count))
    .ToQueryString();
```

That is enough to confirm the `SELECT` list is narrow, that a sub-collection became a join rather than N+1,
and that nothing silently fell to client evaluation.

**What actually ran** — turn on EF's logging and read the two statements the engine emits (the `COUNT(*)` and
the page fetch):

```csharp
options.UseNpgsql(connectionString).LogTo(Console.WriteLine, LogLevel.Information);
```

## Keep a big table's page count cheap

Every request runs a `COUNT(*)` over the filtered set, which is the expensive half on a large table. Three
things that help, in order of effort:

1. **Index what you expose.** Every `Filterable` field is a `WHERE` clause a client can trigger, and every
   `Sortable` field an `ORDER BY`. Grant operators deliberately — `$ilike` on an unindexed text column is a
   sequential scan on demand.
2. **Keep `MaxLimit` honest.** A limit of 1000 is a page fetch of 1000 rows plus whatever the projection pulls.
3. **Cap how deep clients can page.** The engine's `Skip(n)` is `OFFSET n`, which the database still walks.
   If deep pages have no real use case, reject them at the edge rather than serving an expensive query.

Deterministic ordering is not optional here: with `WithTieBreaker(p => p.Id)` two rows tied on the primary sort
still have one defined order, so a row cannot appear on two pages or on none while the client walks the set.
