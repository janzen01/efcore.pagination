# Query composers

Two extension methods that build the query the engine would run and hand it back **unexecuted**. Everything
else in this library composes onto your `IQueryable` and then executes in the same call; these stop one step
short, so you can look at the query, run it yourself, or reuse the matching set for something that is not a
page of rows.

```csharp
PaginateComposedQuery<TEntity> ApplyPaginateFilters<TEntity>(
    this IQueryable<TEntity> source, PaginateQuery request, PaginateConfig<TEntity> config);

PaginateComposedQuery<TEntity> ApplyPagination<TEntity>(
    this IQueryable<TEntity> source, PaginateQuery request, PaginateConfig<TEntity> config);
```

Both are in the **core package** (`Janzen.Pagination.EntityFrameworkCore`), and both carry
`[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` like the four entry points — the engine builds
expression trees either way.

::: tip They cannot drift from the real thing
`PaginateAsync` and its three siblings compose through the same internal path these do. The library's own
test suite asserts that byte-for-byte: it captures the SQL the engine executes and compares it against
`ApplyPagination(...).Query.ToQueryString()`.
:::

## `ApplyPaginateFilters` — the matching set

Filters and search applied; **no** ordering, **no** `Skip`/`Take`, no count, no projection. What you get back
is "every row this request matches", which is the input to anything computed over the match set rather than
over the page:

```csharp
var matching = db.Products.ApplyPaginateFilters(request, config).Query;

var facets = await matching
    .GroupBy(p => p.Status)
    .Select(g => new { Status = g.Key, Count = g.Count() })
    .ToDictionaryAsync(r => r.Status, r => r.Count, ct);

var revenue = await matching.SumAsync(p => p.Price, ct);
```

Before this existed the only way to do that was to translate the filters a second time by hand, in the
consumer, and keep the two copies in step.

## `ApplyPagination` — the page query

Filters, search, ordering (**tie-breaker included**) and `Skip`/`Take`. No count is issued, no projection is
added, and — unlike `PaginateAsync` — a page past the last row is **not** short-circuited to an empty result:
the composer describes what would run, it does not optimize it away.

```csharp
var composed = db.Products.ApplyPagination(request, config);

string sql = composed.Query.ToQueryString();                       // diagnostics, or an assertion in a test
var rows   = await composed.Query.Select(Dto.From).ToListAsync(ct); // your own execution and envelope
```

### `PaginateComposedQuery<TEntity>`

**Both** composers return it. What differs is `Query` — the page query from one, the matching set from the
other — and `SortBy`.

| Member | Type | What it holds |
|--------|------|---------------|
| `Query` | `IQueryable<TEntity>` | The composed query, unexecuted. |
| `Page` | `int` | The 1-based page requested. Not clamped. |
| `Limit` | `int` | The **effective** page size: the requested `limit`, or the config's `DefaultLimit`. |
| `SortBy` | `string[]?` | The **effective** order in `"field:DIR"` form, tie-breaker excluded — or `null`, see below. |
| `Search` | `string?` | The search term that ran, or `null`. |
| `SearchBy` | `string[]` | The **effective** fields it ran over. `[]` when no search ran. |
| `Filter` | `IReadOnlyDictionary<string, IReadOnlyList<string>>` | The request's filters, verbatim per field. |

These are the same values that reach [`meta`](../response/#the-request-echo) on the normal path, from the same
resolution — so a caller building its own envelope reports the effective request without re-deriving it. It is
a class rather than a record: value equality over an `IQueryable` and three collections would compare by
reference and answer a question it cannot actually answer.

::: warning `SortBy` is nullable, and `null` does not mean `[]`
`null` means the ordering was **never resolved** — what `ApplyPaginateFilters` returns, because it does not
order and so never reads `sortBy` at all. `[]` means the ordering **was** resolved and the request asked for
none (the tie-breaker still orders the query; it is not part of the requested order).

Without that distinction a request carrying `?sortBy=name:DESC` through the filtered composer would report
`[]`, which reads as "nothing is sorted" — an answer the caller cannot tell apart from the truth. Every other
member is resolved on both paths and truthful on both.
:::

## What each one validates

Both reject exactly what `PaginateAsync` rejects, at compose time instead of execute time, with the same
messages — see [Errors](../errors/). One difference, and it is deliberate:

| Checked | `ApplyPaginateFilters` | `ApplyPagination` |
|---------|:----------------------:|:-----------------:|
| `page`, `limit` range | yes | yes |
| Unknown / disallowed filter field and operator, filter guards | yes | yes |
| `search` length, unknown or repeated `searchBy` field | yes | yes |
| Unknown `sortBy` field, `sortBy` grammar, `MaxSortFields` | **no** | yes |
| "requires a deterministic sort order" | **no** | yes |

`ApplyPaginateFilters` never reaches ordering, so rejecting a sort it will not apply would refuse a request it
can serve perfectly well. The sharpest case is the last row of that table: a config with neither
`DefaultSortBy` nor `WithTieBreaker` cannot be paged at all and is rejected outright by `ApplyPagination` —
yet counting or grouping its matching set is perfectly valid, and that is what the filtered composer is for.
Its `null` `SortBy` is the same fact restated. If you need the sort validated, you are asking for the page.

## Asserting SQL in your own tests

The composers make the emitted SQL a thing a consumer test can assert on, without a database and without
reading logs:

```csharp
[Fact]
public void Filtering_by_status_uses_the_index_column() {

    var request = new PaginateQuery { Filters = new Dictionary<string, IReadOnlyList<string>> {
        ["status"] = ["$eq:Active"]
    } };

    string sql = _db.Products.ApplyPagination(request, ProductConfig.Instance).Query.ToQueryString();

    Assert.Contains("\"Status\" = ", sql);
    Assert.Contains("LIMIT", sql);

}
```

`ToQueryString()` needs a real provider (it is what turns the expression tree into SQL), but not a reachable
server — an unopened connection string is enough. Over a plain `List<T>.AsQueryable()` the composers still
work and still return a usable `IQueryable`; there is simply no SQL to print.

## Not covered

- **No count.** Neither composer issues one, so neither can tell you `totalItems`. Ask the matching set
  yourself: `await db.Products.ApplyPaginateFilters(request, config).Query.CountAsync(ct)`.
- **No past-the-end short-circuit.** `PaginateAsync` skips the page query entirely when the count says you are
  past the last row. `ApplyPagination` cannot know that without issuing a count of its own — which a method
  whose whole point is "do not touch the database yet" must not do — so an out-of-range page composes a real
  query that returns nothing.

  What that costs is easy to overestimate. `OFFSET` cannot skip rows that do not exist, so the work is bounded
  by the **size of the matching set**, not by how large the page number is: page 9999 over eight rows costs
  eight rows, same as page 4 does. It is only a real cost when the matching set is large — and then it is
  exactly the cost of legitimate deep paging into that same set, which
  [Performance and indexing](/recipes/performance/) covers. If it matters for your traffic, count the matching
  set first and skip the fetch yourself; you need `totalItems` for the envelope anyway.
- **No envelope.** `PaginatedMeta` and `PaginatedLinks` are built by the entry points; a caller composing by
  hand builds its own response shape, with `PaginateComposedQuery` supplying the effective request half.
