# Configuration API

Every method on `PaginateConfigBuilder<TEntity>`: what it declares, and what it refuses. For the ideas behind
these declarations — why the surface is an allow-list, what you have to decide before writing one — read
[Guide → Configuration](/guide/configuration/) first. This page is for looking one method up.

Two kinds of rejection appear below, and the difference matters because they surface at different times:

| | Thrown by | Reaches |
|---|---|---|
| **Configuration-time** | the builder method, or `Create` at the end | your startup, as `ArgumentException` / `InvalidOperationException` |
| **Request-time** | the engine, per request | the caller, as `400` — see [Errors](../errors/) |

A configuration error is a bug in your code and fails loudly at build; a request error is a caller's mistake
and is part of the published contract.

## `Create` and what it defers

```csharp
PaginateConfig<Product> config = PaginateConfig<Product>.Create(builder => builder
    .WithLimits(25, 100)
    .Sortable("name", p => p.Name)
    .WithTieBreaker(p => p.Id));
```

`Create` runs your lambda and then builds. Field names are **arbitrary public aliases** — they need not match
property names, they are matched case-insensitively, and declaring the same name twice for the same kind
replaces the earlier declaration silently rather than throwing.

Building walks expression trees and freezes several dictionaries, so build **once**: a static field, or a
singleton in DI. The result is immutable and safe to share across threads.

Three checks cannot run when the method is called, because they depend on declarations that may come later.
They run at the end of `Create`, and each throws `InvalidOperationException`:

| Check | Message |
|---|---|
| `WithLimits` was never called | `Pagination limits must be configured explicitly via WithLimits(defaultLimit, maxLimit).` |
| a `DefaultSortBy` field is not also `Sortable` | `Default sort field 'x' is not sortable.` |
| a `.When(...)` field has no `.ShowBadge(...)` | `A field configured with .When(...) must also declare .ShowBadge(...) so the condition is documented in the OpenAPI output.` |

So a config that compiles can still throw on first use. Build it in a startup path, or in a test, rather than
lazily on the first request.

---

## Limits

### `WithLimits`

```csharp
.WithLimits(defaultLimit: 25, maxLimit: 100)
```

**The one required call.** `defaultLimit` is the page size when the request sends no `limit`; `maxLimit` is
the largest a caller may ask for. There is no implicit default, because the right page size is a property of
the resource — how wide the row is, how expensive the projection — not of the library.

An over-limit request is **rejected, never clamped**: `?limit=5000` against `maxLimit: 100` returns
`400 Query parameter 'limit' must be between 1 and 100.` rather than quietly serving 100. A caller that
asked for 5000 and received 100 would page through the collection wrongly and never find out.

**Rejects at configuration time:**

- `defaultLimit <= 0` → `ArgumentOutOfRangeException`, `Default limit must be greater than zero.`
- `maxLimit <= 0` → `ArgumentOutOfRangeException`, `Max limit must be greater than zero.`
- `defaultLimit > maxLimit` → `ArgumentException`, `Default limit must not be greater than max limit.`

---

## Guards

Four ceilings that bound what a single request may cost. They are not authorization — they are the answer to
"one caller sent `?filter.tag=$in:` with nine thousand values".

### `WithGuards`

```csharp
.WithGuards(maxFilterValues: 100, maxFilterConditions: 20, maxSortFields: 5, maxSearchLength: 256)
```

Optional; the values above are the defaults. Each parameter is independent, so name only the ones you are
changing:

```csharp
.WithGuards(maxFilterValues: 500)     // large $in lists on this resource, everything else default
```

What each one actually counts is where the surprises live:

| Guard | Default | Counted over | Exceeded → |
|-------|--------:|--------------|------------|
| `MaxFilterValues` | 100 | **one comma-separated list, per criterion.** `$in`, `$btw` and `$contains`-on-a-collection are the operators that take lists. Two criteria of 80 values each pass. | `400 Filter 'x' accepts at most N values.` |
| `MaxFilterConditions` | 20 | **every `filter.*` value across every field**, added together — 20 in total, not 20 per field. | `400 Too many filter conditions; at most N are allowed.` |
| `MaxSortFields` | 5 | **only `sortBy` values sent by the request.** `DefaultSortBy` entries and the tie-breaker are appended afterwards and are never measured against it. | `400 Too many sort fields; at most N are allowed.` |
| `MaxSearchLength` | 256 | characters of `search`, checked before the query is built. | `400 Search term must not exceed N characters.` |

`MaxLimit` belongs to the same family but is set by [`WithLimits`](#withlimits), not here.

**Rejects at configuration time:** each of the four values must be greater than zero, otherwise
`ArgumentOutOfRangeException` with `Max filter values must be greater than zero.`,
`Max filter conditions must be greater than zero.`, `Max sort fields must be greater than zero.` or
`Max search length must be greater than zero.` respectively.

---

## Sorting

### `Sortable`

```csharp
.Sortable("name", p => p.Name)
.Sortable("author", p => p.Author.LastName)          // navigation properties are fine
.Sortable("reviewCount", p => p.Reviews.Count)       // anything EF can translate to ORDER BY
```

Enables `?sortBy=name:ASC` and `?sortBy=name:DESC`. The selector is used as-is in `OrderBy` / `ThenBy`, so any
expression your provider can translate works. `TValue` is recorded and surfaces as the documented type in the
OpenAPI output.

**Rejects at configuration time:** a null or whitespace `name`; a null `selector`.

**Rejects at request time:** `?sortBy=` on a name that was never declared →
`400 Sort for field 'x' is not configured.`

### `DefaultSortBy`

```csharp
.DefaultSortBy("isFeatured", PaginateSortDirection.Desc)
.DefaultSortBy("published", PaginateSortDirection.Desc)
.DefaultSortBy("title")                               // Asc is the default
```

Applied in declaration order **only when the request sends no `sortBy` at all**. A request that sends `sortBy`
replaces the defaults entirely; the two never merge. Default sorts do not count against `MaxSortFields`.

A default field disabled by [`.When(false)`](#when) is skipped rather than fatal, so the resource still pages
for callers who cannot see it.

**Rejects at configuration time:** a null or whitespace `field`. The check that the field is also `Sortable`
is deferred to the end of `Create` — see [above](#create-and-what-it-defers).

### `WithTieBreaker`

```csharp
.WithTieBreaker(p => p.Id)
```

Appends a unique key as the **final** ordering key on every query, whether the sort came from the request or
from the defaults, and regardless of `MaxSortFields`.

This is what makes offset paging correct. Rows that compare equal on the primary sort have no defined order
between them, so without a tie-breaker the database is free to return them differently for `page=1` and
`page=2` — the same row appears twice, or never. Any unique column fixes it.

It is also the fallback that keeps a resource queryable at all: with no `sortBy`, no `DefaultSortBy` and no
tie-breaker, the engine refuses the request rather than paging an unordered set —
`400 Pagination requires a deterministic sort order. …`.

**Rejects at configuration time:** a null `selector`.

---

## Searching

### `Searchable`

```csharp
.Searchable("name", p => p.Name)
.Searchable("description", p => p.Description)        // string? is fine
.Searchable("authorName", p => p.Author.DisplayName)
```

The selector must return `string?`. Every searchable field participates in `?search=` — the criteria are
`OR`-ed together and the whole group is `AND`-ed with the filters — and each can be addressed individually
via `?searchBy=`.

**Rejects at configuration time:** a null or whitespace `name`; a null `selector`.

**Rejects at request time:** `?search=` against a config with no searchable field at all →
`400 Search is not configured for this resource.`

### `IgnoreSearchByInQueryParam`

```csharp
.IgnoreSearchByInQueryParam()
```

Drops `searchBy` from the contract. `search` then always spans every searchable field, a supplied `searchBy`
is neither applied nor validated, and the OpenAPI transformer stops emitting the parameter. Use it when
letting callers name individual fields would disclose which columns exist.

**Rejects:** nothing.

---

## Filtering

### `Filterable`

```csharp
.Filterable("status", p => p.Status, PaginateFilterOperator.Eq, PaginateFilterOperator.In)
.Filterable("price",  p => p.Price,
    PaginateFilterOperator.Eq,
    PaginateFilterOperator.GreaterThanOrEqual,
    PaginateFilterOperator.LessThanOrEqual,
    PaginateFilterOperator.Between)
.Filterable("categoryName", p => p.Category.Name, PaginateFilterOperator.Eq, PaginateFilterOperator.ILike)
```

The operator list is the allow-list **for that field**. `?filter.price=$ilike:x` against the declaration above
is a `400`, because `ILike` was granted to `categoryName` and not to `price`.

Grant operators deliberately rather than passing the full set. Each one is a query shape the database has to
serve, and `$ilike` on an unindexed text column is a sequential scan any caller can trigger at will.

`TValue` decides how raw strings are parsed and which operators are legal at run time: the string pattern
operators need a `string` field, `$contains` needs a string or a collection.

**Rejects at configuration time:**

- a null or whitespace `name`; a null `selector`
- an **empty** `operators` list → `ArgumentException`, `At least one filter operator must be configured.`

**Rejects at request time:** the operator-applicability and value-conversion errors in
[Errors](../errors/#filter-operators).

The same grant tends to repeat across every date and numeric field, and there is no built-in preset. A shared
array is the idiom — `params` accepts one directly:

```csharp
private static readonly PaginateFilterOperator[] Comparable = [
    PaginateFilterOperator.Eq,
    PaginateFilterOperator.GreaterThan, PaginateFilterOperator.GreaterThanOrEqual,
    PaginateFilterOperator.LessThan,    PaginateFilterOperator.LessThanOrEqual,
    PaginateFilterOperator.Between
];

// …
.Filterable("price", p => p.Price, Comparable)
.Filterable("createdAt", p => p.CreatedAt, Comparable)
```

Worth naming rather than copying: an allow-list you paste eleven times is one you stop reading, and the point
of the list is that someone reads it.

### `FilterableMany`

Filters the entity by a value on **any element** of a child collection, translated to an `Any(...)` predicate:

```csharp
// ?filter.tag=$eq:dotnet  → articles that have at least one tag named "dotnet"
.FilterableMany("tag", a => a.Tags, t => t.Name,
    PaginateFilterOperator.Eq, PaginateFilterOperator.In, PaginateFilterOperator.ILike)

// ?filter.reviewerId=$in:a,b → orders reviewed by any of these people
.FilterableMany("reviewerId", o => o.Reviews, r => r.ReviewerId,
    PaginateFilterOperator.Eq, PaginateFilterOperator.In)
```

The first lambda selects the collection, the second selects the value on one element. The operator applies to
that value inside the `Any`, so `$in:a,b` means *has an element matching a **or** b*.

Two criteria on the same `FilterableMany` field become **two independent `EXISTS` clauses**, so different
elements may satisfy each — `?filter.tag=$eq:dotnet&filter.tag=$eq:efcore` matches an article carrying both
tags, not an impossible single tag named both things.

For a field that already **is** a collection on the entity — an array column — use plain `Filterable` with
`PaginateFilterOperator.Contains` instead. There `$contains:a,b` means the collection holds **both**.

**Rejects at configuration time:** a null or whitespace `name`; a null on either selector; an empty
`operators` list, same message as `Filterable`.

---

## Documentation and access control

### `ShowBadge`

```csharp
.Sortable("slug", p => p.Slug).ShowBadge("Public", "language-public")
.Searchable("title", p => p.Title).ShowBadge("Beta")     // no class → neutral chip
```

Attaches a label to **the field declared immediately before it**, surfaced in the generated OpenAPI metadata.
It has no effect on what the engine accepts.

The optional CSS class **must start with `language-`**. That is not a style preference — it is the only class
prefix an API reference UI's markdown sanitizer keeps on an inline `<code>` element inside a parameter
description. See [OpenAPI → Badges](/integrations/aspnetcore/openapi/#badges) for how it renders and how to
colour it.

**Rejects at configuration time:**

- a null or whitespace `name`
- no preceding field → `InvalidOperationException`,
  `ShowBadge must be called immediately after a Sortable, Searchable, or Filterable field.`
- a `cssClass` not starting with `language-` → `ArgumentException`,
  `Badge cssClass must start with "language-" — other classes are stripped by the API reference sanitizer.`

### `When`

```csharp
.Filterable("isHidden", a => a.IsHidden, PaginateFilterOperator.Eq)
    .When(currentUserIsAdmin).ShowBadge("Admin only", "language-admin")
```

Marks the preceding field conditional. When the boolean is `false` the field behaves at query time exactly as
if it were **not configured** — and the `400` a caller gets is worded *identically* to the one for a field
that does not exist. That is deliberate: the existence of an admin-only field is not disclosed to callers who
cannot use it. It is not a bug to be fixed by returning a more helpful message.

The field stays in the OpenAPI output either way, which keeps the documented surface the widest one and is why
the pairing with `ShowBadge` is enforced: a restriction nobody can see in the docs is a support ticket waiting
to happen.

The library stays auth-agnostic — you evaluate the boolean from a role, a claim, a tenant, a feature flag.
Because it is captured when the config is **built**, per-user gating means one cached config per role rather
than one per request. See [Recipes → role-based configurations](/recipes/#role-based-configurations).

**Rejects at configuration time:**

- no preceding field → `InvalidOperationException`,
  `When must be called immediately after a Sortable, Searchable, or Filterable field.`
- no paired `ShowBadge` — deferred to the end of `Create`, see [above](#create-and-what-it-defers)

---

## Providers

`IPaginateConfigProvider<TEntity>` is how the ASP.NET Core integration finds a config to document. You
implement only the typed `GetConfig()`; the non-generic member comes from a default interface implementation.

```csharp
public sealed class ProductPaginateConfigProvider : IPaginateConfigProvider<Product> {

    public readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
        .WithLimits(25, 100)
        .Sortable("name", p => p.Name)
        .WithTieBreaker(p => p.Id));

    public PaginateConfig<Product> GetConfig() => Config;

}
```

The OpenAPI transformer activates the provider with `ActivatorUtilities.CreateInstance`, so a provider with a
parameterless constructor works without being registered in DI. Register it when its constructor needs
services.

## Reading the configuration back

Every config exposes its own metadata through `IPaginateConfig`, which is how the OpenAPI transformer
documents itself and is equally available to you — for a `/meta` endpoint, an admin UI, or a contract test:

```csharp
IPaginateConfig meta = provider.GetConfig();

meta.DefaultLimit;        // int
meta.MaxLimit;            // int
meta.DefaultSortBy;       // IReadOnlyList<PaginateSort>                 — Field + Direction
meta.SortableFields;      // IReadOnlyList<PaginateFieldMetadata>        — Name, Type, Badge?
meta.SearchableFields;    // IReadOnlyList<PaginateFieldMetadata>
meta.FilterableFields;    // IReadOnlyList<PaginateFilterFieldMetadata>  — + allowed Operators
meta.MaxFilterValues; meta.MaxFilterConditions; meta.MaxSortFields; meta.MaxSearchLength;
meta.IgnoreSearchByInQueryParam;
```

Conditional fields appear in these lists **regardless of their condition** — the metadata is the documented
surface, not the per-caller one.
