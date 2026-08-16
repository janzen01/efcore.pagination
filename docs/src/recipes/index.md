# Recipes

Task-shaped answers. Each one is self-contained.

- [Role-based configurations](#role-based-configurations)
- [Filter by a value on a child collection](#filter-by-a-value-on-a-child-collection)
- [Return aggregates alongside the page](#return-aggregates-alongside-the-page)
- [Expose the contract as metadata](#expose-the-contract-as-metadata)

Five answers outgrew a section and have a page of their own:

| Page | For |
|------|-----|
| [Pagination without ASP.NET Core](./without-aspnetcore/) | gRPC services, console tools, workers, batch walks |
| [Performance and indexing](./performance/) | what to index, what a page costs, where deep paging stops being cheap |
| [Testing your pagination](./testing/) | asserting a config with no database, and the SQLite limits behind the ones that need one |
| [Troubleshooting](./troubleshooting/) | symptom → cause, for the cases where the error message is not the problem |
| [From nestjs-paginate](./migration/) | what carries over from the contract this one borrowed, and what deliberately does not |

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

### Cache the variants, do not rebuild per request

The obvious alternative — calling `Create(...)` inside the action with the current user's flag — works, and is
the wrong shape. `Create` walks every selector expression tree and freezes several dictionaries; doing that on
every request puts a build step on a hot path to produce one of a handful of distinct results.

Two static fields cover two roles. If the gate is a set of independent flags rather than a role, key a
`ConcurrentDictionary` on the combination and build each config once:

```csharp
private static readonly ConcurrentDictionary<bool, PaginateConfig<Article>> ByModerator = new();

public static PaginateConfig<Article> For(bool isModerator) =>
    ByModerator.GetOrAdd(isModerator, Build);
```

Key it on the **permissions**, never on the user. One config per distinct combination of gates is the whole
population, and it is almost always small — if it is not, the gating probably belongs in the `IQueryable`
rather than in the config.

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
[`PaginateSelectMapAsync`](/guide/projections/#paginateselectmapasync-—-sql-then-finish-in-memory).

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

The same metadata is what the [OpenAPI transformer](/integrations/aspnetcore/#openapi) reads, so the two cannot disagree.
