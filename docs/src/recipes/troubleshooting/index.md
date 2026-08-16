# Troubleshooting

Symptom first. For the exact wording of any `400`, see [Errors](/reference/errors/) — this page is for the
cases where the message is not the problem.

## "The field is not configured", but I configured it

`Filter for field 'x' is not configured.` and its sort and search equivalents have four causes, in the order
worth checking:

1. **A `.When(false)` gate.** A conditional field reports *exactly* the message of a field that does not
   exist, on purpose — see [`When`](/reference/configuration/#when). If the condition reads a role or a
   claim, the config was probably built with the wrong one, or built once at startup and cached across users.
2. **The alias, not the property.** Field names are arbitrary aliases: `.Sortable("createdAt", p => p.Created)`
   is addressed as `createdAt`, never as `Created`. Matching is case-insensitive, so case is not it.
3. **The wrong config reached the endpoint.** The provider named in `[PaginatedQuery<T>]` /
   `WithPagination<T>()` documents the operation; the config passed to `Paginate*Async` is what enforces it.
   Nothing checks that they are the same one.
4. **You declared the wrong kind.** `Sortable` does not make a field filterable, and neither makes it
   searchable. Each is a separate declaration.

## Sorting is ignored, or wrong

- **`sortBy` replaces the defaults, it does not merge with them.** A request that sends any `sortBy` drops
  every `DefaultSortBy` entry.
- **The tie-breaker is always last**, whichever applied. Seeing an extra column at the end of the `ORDER BY`
  is correct.
- **`Pagination requires a deterministic sort order.`** means none of the three applies. Add
  [`WithTieBreaker`](/reference/configuration/#withtiebreaker); it is the fix in almost every case, and it is
  also the one that stops rows drifting between pages.
- **Rows appear twice or vanish while paging** and there *is* a sort: the sort is not total. That is the same
  fix — a unique key as the final ordering column.

## `$ilike` is not case-insensitive

`$ilike` names the intent, not a guarantee. Without the `.PostgreSql` package it emits a portable `LIKE`, and
case sensitivity is then whatever the column's collation says — which on many collations means it behaves
exactly like `$contains`. Register [`UsePostgreSql()`](/integrations/postgresql/) for native `ILIKE`, or use a
case-insensitive collation.

## A value with a comma in it does not work

It cannot be expressed. `$in`, `$btw` and `$contains`-on-a-collection split on `,` and trim, with **no
escaping**. Single-value operators take everything after the operator's colon verbatim, commas included, so
`$eq:Smith, John` is fine — it is only the list operators that have no way through.

## The links are `null`, or doubly escaped

- **`"links": null`** means no link context was supplied. In ASP.NET Core, that is the overload without the
  `HttpRequest`; elsewhere it is the default. See [Response contract](/reference/response/).
- **`%2524eq%253AActive`** in a link means the values were pre-escaped. `PaginateLinkContext` percent-encodes
  what you give it, so supply `$eq:Active` raw.
- **`"next": null` on a page that clearly has more rows** — check `meta` rather than the link. If
  `currentPage` exceeds `totalPages`, the page requested is past the end and `next` is correctly absent.

## OpenAPI shows the wrong parameters

- **Both real and framework-generated parameters** (`SortBy`, `Filters`, an object-shaped query): the
  transformer was not registered. It is what strips the generated ones —
  `AddOpenApi(o => o.AddOperationTransformer<PaginatedQueryOperationTransformer>())`.
- **No pagination parameters at all**: the operation carries no `[PaginatedQuery<T>]` or
  `WithPagination<T>()`, so the transformer skipped it.
- **`searchBy` is missing**: the config calls `IgnoreSearchByInQueryParam()`, which removes it from the
  contract, so documenting it would be wrong.
- **A badge renders as literal text**: the class does not start with `language-`, or it is on something other
  than a description. See [OpenAPI → Badges](/integrations/aspnetcore/openapi/#badges).

## Something threw a `500`, not a `400`

`PaginateQueryException` is the only exception the ProblemDetails filter maps. Anything else is a bug in your
code rather than in the request, and the most common one is projection:

> `InvalidOperationException` from `PaginateProjectionBuilder`

Automatic projection maps **constructor parameters**, not settable properties, so the target should be a
record whose parameter names match entity members (case-insensitively). A parameter with nothing to bind to,
or a member the provider cannot translate, fails here. Either fix the DTO or switch to
[`PaginateSelectAsync`](/guide/projections/) and write the selector yourself.

## A trimmed or AOT publish warns

Those warnings are accurate. The engine builds expression trees and uses reflection, so every entry point is
annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` — see
[Requirements](/guide/getting-started/#requirements). Suppressing them converts a build warning into a
runtime failure; there is no trim-safe mode to switch on.

## An audit tool says unknown parameters are silently accepted

They are, and it is deliberate. The binder reads exactly six inputs and ignores everything else, so a
client's own `utm_*` or `offset` does not break the request. Strict binding would reject perfectly ordinary
tracking parameters. The two where a wrong value would change the result — `page` and `limit` — *are*
validated.

## Reading what actually ran

The engine composes onto your `IQueryable` and then executes, so there is nothing to call `ToQueryString()`
on mid-flight. Two ways to see the SQL:

**Before committing to a selector**, apply the same `Select` yourself. This needs a configured `DbContext`,
not a running database:

```csharp
string sql = db.Products
    .Select(p => new ProductSummary(p.Id, p.Name, p.Reviews.Count))
    .ToQueryString();
```

Enough to confirm the `SELECT` list is narrow, that a sub-collection became a join rather than N+1, and that
nothing fell to client evaluation.

**What actually ran** — EF's own logging, which shows both statements:

```csharp
options.UseNpgsql(connectionString).LogTo(Console.WriteLine, LogLevel.Information);
```
