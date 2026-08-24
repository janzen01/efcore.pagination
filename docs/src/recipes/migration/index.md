# Coming from nestjs-paginate

The query-string contract here is deliberately borrowed from
[nestjs-paginate](https://github.com/ppetzold/nestjs-paginate), so a front end written against a NestJS API
mostly keeps working when the backend becomes .NET. "Mostly" is what this page is about: what maps one to one,
what is shaped differently, and what is not here at all.

::: info
Written against nestjs-paginate's documented contract. It is a separate project on its own release cycle —
check its current README before relying on a row below.
:::

## The query string

| Parameter | Here | Notes |
|-----------|------|-------|
| `page` | **same** | 1-based in both |
| `limit` | **same** | but see [limits](#limits-and-page-size) |
| `sortBy=col:DESC` | **same** | repeat the key for secondary sorts, same as there |
| `search` | **same** | |
| `searchBy` | **same** | repeat the key; can be switched off per resource with `IgnoreSearchByInQueryParam()` |
| `filter.<col>=$op:value` | **same** | |
| `select=id,name` | **absent** | see [columns are not client-selectable](#columns-are-not-client-selectable) |
| `withDeleted` | **absent** | soft delete is your `IQueryable`'s business, not the paginator's |
| `cursor` | **absent** | offset paging only |
| `filter=` expression | **absent** | boolean logic uses [`$and` / `$or` prefixes](/reference/query-string/#and-or-—-combining-criteria-on-one-field) instead |

Anything not on this list is ignored rather than rejected — including parameters that were meaningful to
nestjs-paginate, so a client that still sends `select` or `withDeleted` pages normally instead of failing.
That is worth knowing during a gradual migration, because it also means those parameters silently stop
having an effect.

## Operators

Every operator token carries over, with the same spelling:

`$eq` `$in` `$null` `$sw` `$ilike` `$contains` `$lt` `$lte` `$gt` `$gte` `$btw`, plus the `$not` modifier.

Two differences in how they combine:

- **Repeating `filter.<field>` means `AND` in both.** `?filter.rank=$gte:20&filter.rank=$lte:50` is a range
  either way.
- **`OR` does not need a second syntax.** nestjs-paginate expresses it through a separate `filter=` expression
  language; here it is a prefix on the criterion itself —
  `?filter.status=$eq:Active&filter.status=$or:$eq:Draft`. The cost of that is symmetrical: there is no way to
  express boolean logic *across* different fields, which the expression form allows. Different fields are
  always `AND`.

`$contains` is worth checking against your data. Here it means substring on a string field and set containment
on a collection field, where **all** listed values must be present.

## Configuration

The config is a fluent builder rather than an object literal, and the mapping is direct:

| nestjs-paginate | Here |
|-----------------|------|
| `sortableColumns: ['name']` | `.Sortable("name", p => p.Name)` |
| `searchableColumns: ['name']` | `.Searchable("name", p => p.Name)` |
| `filterableColumns: { age: [FilterOperator.EQ] }` | `.Filterable("age", p => p.Age, PaginateFilterOperator.Eq)` |
| `filterableColumns: { age: true }` *(all operators)* | no equivalent — operators are listed explicitly |
| `defaultSortBy: [['id', 'DESC']]` | `.DefaultSortBy("id", PaginateSortDirection.Desc)` |
| `defaultLimit`, `maxLimit` | `.WithLimits(defaultLimit, maxLimit)` — **required**, no global default |
| `relations: { … }` | absent — the [projection](/guide/projections/) decides what is loaded |
| `select: [...]` | absent — the DTO decides |
| `where: { … }` | absent — filter the `IQueryable` before paginating |
| `nullSort: 'last'` | absent — null ordering is the provider's default |
| `ignoreSearchByInQueryParam` | `.IgnoreSearchByInQueryParam()` |
| `updateGlobalConfig({ defaultLimit })` | absent — limits are per resource, deliberately |

The important shape difference: a column is named by a **lambda**, not a string, so a rename in the entity is
a compile error rather than a runtime surprise, and the public alias is free to differ from the property name.

Two things have no counterpart there:

- **[`WithTieBreaker`](/reference/configuration/#withtiebreaker)**, and it is not optional in practice — see
  [ordering](#ordering-is-stricter) below.
- **[`.When(...)` and `.ShowBadge(...)`](/reference/configuration/#when)**, for a field only some callers may
  use, documented but conditionally enforced.

## The response

Same three parts, different names in two places:

| nestjs-paginate | Here |
|-----------------|------|
| `data` | **`items`** |
| `meta.itemsPerPage` / `totalItems` / `currentPage` / `totalPages` | same names |
| — | **`meta.itemCount`** — rows on this page, which has no counterpart there |
| `meta.sortBy` / `search` / `filter` *(request echo)* | absent — `meta` reports the page, not the request |
| `links.first` / `previous` / `next` / `last` | same names |
| `links.current` | same name, and never `null` |
| links are absolute URLs | links are **path-relative** (path base included), and the whole `links` object is `null` without a link context |

So the two client-side changes that are not optional: read `items` instead of `data`, and do not expect the
echoed request in `meta`. Everything a client needs to navigate is still in `meta` — see
[Response contract](/reference/response/).

## Ordering is stricter

nestjs-paginate lets a resource sort by whatever you configured and leaves it there. Here, if a request ends
up with **no ordering at all** — no `sortBy`, no `DefaultSortBy`, no tie-breaker — the engine refuses it with
a `400` rather than paging an unordered set.

Even with a sort, offset paging over rows that tie on it can show a row twice or never. That is why
`WithTieBreaker(p => p.Id)` is on essentially every config here; it appends a unique key as the last ordering
column. If your NestJS resources relied on the database's incidental ordering, this is the one behavioural
change worth planning for rather than discovering.

## Limits and page size

`WithLimits(defaultLimit, maxLimit)` is **required** — there is no global default to inherit, because the
right page size is a property of the resource. Set it per resource while migrating rather than looking for the
equivalent of `updateGlobalConfig`.

A `limit` above `maxLimit` is **rejected with a `400`**, not reduced. A client that asked for 500 and quietly
received 100 would page through the collection wrongly, so the request fails instead.

There is also **no "disable pagination" escape hatch** — nothing corresponds to `limit=-1`. A caller that
genuinely needs the whole set walks it in pages; see
[Pagination without ASP.NET Core](../without-aspnetcore/) for the loop.

## Columns are not client-selectable

There is no `select` parameter, and this is a deliberate difference rather than a missing feature. What comes
back is decided by the projection you chose on the server — the DTO's shape, or a selector you wrote — so a
caller cannot widen the `SELECT` list, reach a column you did not intend to expose, or turn a narrow query
into a wide one.

Where nestjs-paginate would use `select` and `relations`, pick a
[projection strategy](/guide/projections/) instead: one DTO per shape you want to serve, and a different
endpoint if a caller genuinely needs a different shape.

## Errors

Both reject bad input with a `400`. Here every message comes from one exception type and the wording is part
of the published contract — the full list is in [Errors](/reference/errors/). If your clients matched on
NestJS validation-pipe messages, that matching has to be rewritten; matching on the status code does not.
