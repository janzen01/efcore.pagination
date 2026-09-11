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

## A `.When(...)` gate stopped applying

The inverse symptom, and it has one cause worth checking before any other: **declaring the same name twice
for the same kind replaces the earlier declaration silently**, and what is replaced may be the gated one. A
second `.Filterable("isHidden", …)` with no `.When(...)` leaves the field ungated, and `Build()` does not
catch it — the `When`-requires-`ShowBadge` check inspects only the declarations that survived, and the
survivor has no `When`. Search the config for a second declaration of that name; the replacement may sit in a
shared helper rather than next to the original.

## Sorting is ignored, or wrong

- **`sortBy` replaces the defaults, it does not merge with them.** A request that sends any `sortBy` drops
  every `DefaultSortBy` entry.
- **The tie-breaker is always last**, whichever applied. Seeing an extra column at the end of the `ORDER BY`
  is correct.
- **`A pagination configuration requires WithTieBreaker(...)`** is thrown when the configuration is *built*,
  not when a request arrives — so it surfaces at startup or on the first use of that config, never as a `400`.
  Add [`WithTieBreaker`](/reference/configuration/#withtiebreaker) on any unique column; it is also what stops
  rows drifting between pages.
- **Rows appear twice or vanish while paging** and there *is* a sort: the sort is not total. That is the same
  fix — a unique key as the final ordering column.

## `$ilike` is not case-insensitive

`$ilike` names the intent, not a guarantee. Without the `.PostgreSql` package it emits a portable `LIKE`, and
the case behaviour is then the engine's — which is **not the same on every one of them**, and on PostgreSQL is
case-*sensitive* with no collation that changes it before 18.6. The
[per-leg table](/reference/query-string/#ilike-and-contains-on-a-string-—-contains) is the place to check what
yours does. Register [`UsePostgreSql()`](/integrations/postgresql/) for native `ILIKE`, or move the column to a
type or collation that folds case on the engine you deploy on.

## A value with a comma in it does not work

It cannot be expressed. `$in`, `$btw` and `$contains`-on-a-collection split on `,` with **no escaping**.
Single-value operators take everything after the operator's colon verbatim, commas included, so
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

Those warnings are accurate. The engine builds expression trees and uses reflection, so every entry point that
reaches it is annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` — see
[Requirements](/guide/getting-started/#requirements) for the list. Suppressing them converts a build warning
into a runtime failure; there is no trim-safe mode to switch on.

A `[PaginatedQuery<TProvider>]` endpoint has one more requirement the annotations cannot express, so the
provider type carries `[DynamicallyAccessedMembers(PublicConstructors)]` instead: the OpenAPI transformer
activates an unregistered provider with `ActivatorUtilities.CreateInstance`, and `typeof(TProvider)` roots the
type but not its constructor. Without that annotation the document request answered `500` in a trimmed publish
while working in development.

## An audit tool says unknown parameters are silently accepted

They are, and it is deliberate. The binder reads exactly six inputs and ignores everything else, so a
client's own `utm_*` or `offset` does not break the request. Strict binding would reject perfectly ordinary
tracking parameters. The two where a wrong value would change the result — `page` and `limit` — *are*
validated.

## Reading what actually ran

**The page query, before it runs.** `ApplyPagination` composes it and stops, so `ToQueryString()` prints
exactly what `PaginateAsync` would execute — filters, search, ordering, `Skip`/`Take`. A configured
`DbContext` is enough; no server has to answer:

```csharp
string sql = db.Products.ApplyPagination(request, config).Query.ToQueryString();
```

See [Query composers](/reference/composers/). It adds no projection, so pair it with the next one when the
`SELECT` list is what you are chasing.

**A selector, before committing to it.** Apply the same `Select` yourself:

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
