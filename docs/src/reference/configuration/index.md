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

These checks cannot run when their own builder method is called, because they depend on declarations that may
come later or on values that may arrive from a defaults object. They run at the end of `Create`, in this
order, and each throws `InvalidOperationException`:

| Check | Message |
|---|---|
| `WithLimits` was never called, and no defaults object supplied both halves | `Pagination limits must be configured explicitly via WithLimits(defaultLimit, maxLimit).` |
| a resolved guard is zero or negative | `<Guard> must be greater than zero.` |
| `defaultLimit` above `maxLimit` after resolution | `Default limit 50 must not be greater than max limit 25.` |
| a resolved `MaxOffset` below zero | `MaxOffset must not be negative.` |
| `MinSearchLength` above `MaxSearchLength` | `Min search length 10 must not be greater than max search length 5.` |
| a `DefaultSortBy` field is not also `Sortable` | `Default sort field 'x' is not sortable.` |
| [`WithTieBreaker`](#withtiebreaker) was never called | `A pagination configuration requires WithTieBreaker(...): …` |
| a `.When(...)` field has no `.ShowBadge(...)` | `A field configured with .When(...) must also declare .ShowBadge(...) so the condition is documented in the OpenAPI output.` |
| an explicit operator list names one the field's type cannot carry | `Filter 'x' allows operator '$gt', which the engine cannot build for type 'Boolean'. …` |

The tie-breaker row is the one most likely to surprise an upgrade: it is required outright rather than "a
default sort **or** a tie-breaker", and a configuration that omits it no longer builds — see
[`WithTieBreaker`](#withtiebreaker) for why the weaker rule does not hold.

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
- `defaultLimit > maxLimit` → `ArgumentException`, `Default limit must not be greater than max limit.` — and again at `Build()` as an `InvalidOperationException` naming both numbers, for the case where the two halves arrive from different places (see [Shared defaults](#shared-defaults))

---

## Guards

Ceilings that bound what a single request may cost. They are not authorization — they are the answer to
"one caller sent `?filter.tag=$in:` with nine thousand values".

### `WithGuards`

```csharp
.WithGuards(maxFilterValues: 100, maxFilterConditions: 20, maxSortFields: 5, maxSearchLength: 256)
```

Optional; the values above are the engine’s own defaults. Each parameter is independent, so name only the
ones you are changing — an omitted one is **not set here at all**, so it still falls through to the shared
defaults below:

```csharp
.WithGuards(maxFilterValues: 500)     // large $in lists on this resource, everything else default
```

::: warning `MaxFilterValues` is also an expression-tree depth
`$contains` on a collection field folds one predicate per value into a left-leaning `AND` chain, so the
guard's value **is** the depth of the tree the provider is handed. The default of 100 is far below every
ceiling below; raising it a long way is not. Measured, one value count per process:

| `maxFilterValues` | SQLite | LINQ-to-Objects |
|---:|---|---|
| 100 (default) … 900 | fine | fine |
| 1 000 | `SQLite Error 1: 'Expression tree is too large (maximum depth 1000)'`, surfacing as a `500` | fine |
| 10 000 | — | **`StackOverflowException` while compiling the expression, which kills the process** |

The second one cannot be caught: .NET terminates on a stack overflow by design, so no handler, middleware or
`try` block sees it. If you raise this guard past a few hundred, keep the value well inside your provider's
limit and test the in-memory leg at the same number.
:::

What each one actually counts is where the surprises live:

| Guard | Default | Counted over | Exceeded → |
|-------|--------:|--------------|------------|
| `MaxFilterValues` | 100 | **one comma-separated list, per criterion.** `$in`, `$btw` and `$contains`-on-a-collection are the operators that take lists. Two criteria of 80 values each pass. | `400 Filter 'x' accepts at most N values.` |
| `MaxFilterConditions` | 20 | **every `filter.*` value across every field**, added together — 20 in total, not 20 per field. | `400 Too many filter conditions; at most N are allowed.` |
| `MaxSortFields` | 5 | **only `sortBy` values sent by the request.** `DefaultSortBy` entries and the tie-breaker are appended afterwards and are never measured against it. | `400 Too many sort fields; at most N are allowed.` |
| `MaxSearchLength` | 256 | characters of `search`, **and** of a `$ilike` / `$sw` / `$contains` pattern on a string field — the two emit the same `LIKE`. Checked before the query is built. | `400 Search term must not exceed N characters.` / `400 Filter 'x' pattern must not exceed N characters.` |

`MaxLimit` belongs to the same family but is set by [`WithLimits`](#withlimits), not here.

**Rejects at configuration time:** each of the four values must be greater than zero, otherwise
`ArgumentOutOfRangeException` with `Max filter values must be greater than zero.`,
`Max filter conditions must be greater than zero.`, `Max sort fields must be greater than zero.` or
`Max search length must be greater than zero.` respectively.

---


### `WithMinSearchLength` <Badge type="tip" text="10.1.0" />

```csharp
.WithMinSearchLength(3)
```

The other end of `MaxSearchLength`, and the more useful one on a large table: `?search=a` across three
unindexed text columns is the cheapest way a caller can make the database read every row. Defaults to 1 —
any non-blank term runs.

The term is measured **after trimming**, and trimming happens before the search is built either way, so
`?search=%20%20a%20%20` is a three-character term that searches for `a` — not a five-character one that
searches for the spaces.

It bounds the three pattern operators too. `$ilike`, `$sw` and `$contains`-on-a-string emit the identical
`LIKE '%…%'`, and the derived operator set puts all three within reach of the `Filterable("name", x => x.Name)`
shorthand, so guarding only `search` would leave the same scan reachable through any field whitelisted for them
— down to `?filter.name=$ilike:`, a zero-length value that matched every non-`NULL` row. A filter value is never
trimmed, so a pattern is measured **as sent**, and the rejection reads
`400 Filter 'x' pattern must be at least N characters.`

**Rejects at configuration time:** a value below 1 → `ArgumentOutOfRangeException`; a value above
`MaxSearchLength` → `InvalidOperationException` at `Build()`, naming both numbers.

### `WithMaxOffset` <Badge type="tip" text="10.1.0" />

```csharp
.WithMaxOffset(50_000)
```

Caps how many rows a request may skip — `(page - 1) × limit`. Unset by default. The check is arithmetic, so
it runs **before the count query**: a guarded deep page is refused without the database being asked anything
at all.

It is a ceiling on the offset rather than on the page number on purpose. The offset is what the database
pays for, and which page a given offset corresponds to moves with `limit` — at `maxOffset: 100`, page 11 is
reachable at `limit=10` and page 4 is not at `limit=50`.

### `AllowUnlimited` <Badge type="tip" text="10.1.0" />

```csharp
.AllowUnlimited(maxRows: 5_000)
```

Opts this resource into `?limit=-1`, which returns every matching row as one page. Without the call `-1` is
rejected like any other out-of-range limit, and `-2` and `0` stay rejected with or without it.

The ceiling is mandatory — there is no argument-less form. The engine fetches one row past it and answers
`400` rather than materialising a set nobody promised would fit in memory. An unlimited request must ask for
page 1; pages of an unbounded set are meaningless.

What it costs and what comes back:

- **one query, not two.** The fetched set *is* the count, so no `COUNT(*)` is issued. The saving is the
  count, not the ordering: that single query still carries the full `ORDER BY` — your sort keys plus the
  mandatory tie-breaker — over the whole ceiling-bounded set, so `maxRows` is a promise about sort cost as
  much as about row count. Index the ordering, and see
  [Performance](/recipes/performance/) for the one sort no index on the paged table can serve.
- `meta.itemsPerPage` echoes `itemCount` — the honest value, not the requested `-1`.
- `meta.hasNextPage` and `meta.hasPreviousPage` are both `false`; `totalPages` is 1, or 0 when nothing matched.
- `links.first`, `links.last` and `links.current` are the same URL; `next` and `previous` are `null`.
- an unlimited request that matches nothing reports `itemsPerPage: 0` — it is the row count, and the page
  holds none. Do not divide by it.
- [`ApplyPagination`](../composers/) composes the same query bounded at `maxRows + 1`, but cannot apply the
  ceiling itself — only execution can count rows. A caller executing the composed query for `limit=-1` owns
  that check.

The opt-in is per resource because it is a claim about *this* collection's size — which is why there is no
shared default for it, unlike every other guard on this page.

---

## Shared defaults <Badge type="tip" text="10.1.0" />

Every limit and guard above can come from a shared object instead of being retyped per configuration. Two
ways in, and they compose:

```csharp
var defaults = new PaginateConfigDefaults { DefaultLimit = 25, MaxLimit = 100, MaxSearchLength = 128 };

// (a) explicit -- only the configurations naming it are affected
PaginateConfig<Product>.Create(defaults, b => b.Sortable("id", p => p.Id) /* … */);

// (b) ambient -- assign once at startup, every configuration built afterwards picks it up
PaginateConfigDefaults.Shared = defaults;
PaginateConfig<Order>.Create(b => b.Sortable("id", o => o.Id) /* … */);
```

Resolution runs outward from the most specific, and the first source that has a value wins:

**a `WithLimits` / `WithGuards` / `WithX` call** → **the object passed to `Create`** → **`PaginateConfigDefaults.Shared`** → **the engine's own constant**

So a shared value is a default in the ordinary sense: never a ceiling a configuration cannot raise, and never
something that overrides a value someone wrote down. `WithLimits` also stops being mandatory once both halves
are available from somewhere — the two may even arrive from different sources, which is why the
`defaultLimit > maxLimit` check runs again at `Build()`.

Four things worth knowing:

- **`Shared` is read at `Build()` time.** Assign it before the first configuration is built; a configuration
  does not observe a later assignment. It is process-wide mutable state, so tests that assign it want the
  same treatment as [`PaginateLikeDefaults`](/recipes/testing/#watch-the-process-wide-statics) — a non-parallel collection, and
  restore it afterwards.
- **`AllowUnlimited` is deliberately absent** from the object. An unbounded read is a claim about one
  resource's size, and a default that turned it on everywhere would be exactly the claim nobody can make.
- It is a `record`, so `PaginateConfigDefaults.Shared with { MaxLimit = 200 }` is the way to vary one value.
- **A shared value can be raised but not removed.** "Unset" and "explicitly none" are the same absence, so a
  configuration under a shared `MaxOffset` can raise the ceiling and cannot lift it. Put a ceiling that some
  resources must not have on those resources rather than in the shared object.
- The values are validated when a configuration is **built**, not when they are assigned — an `init` accessor
  cannot reject the way a builder method does. A nonsense value is an `InvalidOperationException` naming it.

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

**This call is required.** A configuration without it does not build:

> A pagination configuration requires `WithTieBreaker(...)`: offset paging over a non-unique order can return
> the same row on two pages and skip another. Pass the entity's primary key, e.g. `WithTieBreaker(x => x.Id)`.

It is required outright rather than "a `DefaultSortBy` **or** a tie-breaker", because the weaker rule does not
hold: a default-sort field can be switched off per caller by [`When`](#when), so a configuration whose only
default is disabled would pass that check and still have nothing to order by.

Until `10.1.0` this was a runtime `400` on every request such a configuration could not order. That reported a
configuration defect as a client error, and it stayed invisible for as long as every caller happened to send
`sortBy` — which is exactly the condition under which the paging was silently non-deterministic anyway.

**Rejects at configuration time:** a null `selector`; its absence, as above.

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

`TValue` decides how raw strings are parsed and which operators the engine can build at all: the string
pattern operators need a `string` field, `$contains` needs a string or a collection, and the comparisons need
a type that carries an ordering. Granting one the type cannot carry is a configuration error, not a request
error, so `Build()` refuses it.

**Rejects at configuration time:**

- a null or whitespace `name`; a null `selector`
- an **empty** `operators` list → `ArgumentException`, `At least one filter operator must be configured.` — this is the *explicit* signature only; omitting the argument entirely selects the shorthand below
- an operator the field's `TValue` cannot carry → `InvalidOperationException` at `Build()`, naming the field, the operator and the type. Only the explicit signature can produce this; the shorthand below derives a buildable set

**Rejects at request time:** an operator this field does not grant, the value-count guards and the
value-conversion errors in [Errors](../errors/#filter-operators).

### Operator defaults by type

Omit the operator list and the field is granted every operator the engine can build for `TValue`:

```csharp
.Filterable("age", p => p.Age)                 // Eq, In, Gt, Gte, Lt, Lte, Between
.Filterable("name", p => p.Name)               // Eq, In, Null, StartsWith, Contains, ILike
.FilterableMany("tag", a => a.Tags, t => t.Name)
```

| `TValue` | Derived operators |
|----------|-------------------|
| `string` | `Eq`, `In`, `Null`, `StartsWith`, `Contains`, `ILike` |
| `bool` | `Eq` |
| numbers, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, and [registered types](/integrations/custom-types/) with an ordering of their own | `Eq`, `In`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between` |
| `Guid`, `char`, enums | `Eq`, `In` |
| anything else | `ArgumentException` at configuration time — the shorthand never guesses |

`Null` joins the set exactly when the engine can express it: for reference types always, for value types only
through `Nullable<T>`. So `p => p.Age` (an `int`) has no `$null`, and `p => p.RetiredOn` (a `DateOnly?`) does.

Ranges are deliberately withheld from `string`, `Guid`, `char` and enums. They *translate* — the engine has a
stand-in for each — but the ordering is then the database's collation or byte order rather than anything you
chose, which is rarely what a range filter is being asked for. Grant them explicitly when it is.

Two consequences worth knowing before reaching for the shorthand. A field declared this way **widens when the
library does**: a release that adds an operator to one of these rows grants it to every shorthand field on
rebuild, and any such release says so in its notes. And the derived set is the whole allow-list, so the advice
above still holds — `$ilike` on an unindexed text column is a sequential scan whether you typed the operator
or the type implied it. On a large table, list what you actually serve.

The derivation is public, which is the middle road between the two signatures — start from the set and adjust:

```csharp
.Filterable("price", p => p.Price, PaginateFilterOperators.For<decimal>())
.Filterable("score", p => p.Score, [.. PaginateFilterOperators.For<int>(), PaginateFilterOperator.Null])
```

`PaginateFilterOperators.For(Type)` is the reflection-typed counterpart, for a config assembled dynamically.

One ordering requirement comes with it: the derivation reads the process-wide type registry, so a type from an
add-on package only resolves once that package has registered it. A `static readonly` config using the
shorthand on a NodaTime type will throw during type initialization if it is built before `UseNodaTime()` (or
`PaginateNodaTime.Register()`) runs. Register first, or give such a field the explicit operator list.

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

## Nested attributes

A selector may cross a navigation, and a field name is opaque to the engine — so the convention is to spell
the path out with dots:

```csharp
.Filterable("author.name", a => a.Author!.Name)
.Sortable("author.name",   a => a.Author!.Name)
.Searchable("author.name", a => a.Author!.Name)
.FilterableMany("order.product", c => c.Orders, o => o.Product!.Name)
```

`?filter.author.name=$eq:ann`, `?sortBy=author.name:ASC` and `?searchBy=author.name` then behave like any
other field: there is nothing to register, because EF derives the join from the navigation in the lambda.
Nothing stops you calling the field `authorName` — the dotted form is a convention, and the one the generated
OpenAPI parameter list reads best in.

A row whose intermediate is `null` is treated the way the database treats it: the join yields no value, so a
comparison does not match, a search skips the row, a sort orders it as null, and `$null` **does** match. That
holds on a plain `IQueryable` too — see [Testing without a database](/recipes/testing/).

One thing that shape cannot express: `$null` on a **value-typed** nested member. `p => p.Category!.Id` is an
`int`, and both legs answer "no row is null" from that declared type — the shorthand does not even grant the
operator. Filter the nullable foreign key instead (`p => p.CategoryId`), which is what "has no category"
actually means in the model.

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

::: warning A later redeclaration takes the gate with it
Declaring the same name twice for the same kind [replaces the earlier declaration
silently](#create-and-what-it-defers), and the thing being replaced may be this gate: a second
`.Filterable("isHidden", …)` with no `.When(...)` leaves an **ungated** field, and the `ShowBadge` check
above passes trivially because it only inspects the declarations that survived. If a conditional field
stops being conditional, look for a second declaration of the same name before looking anywhere else.
:::

---

## Pattern matching

### `WithLikeStrategy` <Badge type="tip" text="10.1.0" />

```csharp
.WithLikeStrategy(PaginateLikeDefaults.Portable)
```

Gives this configuration its own `IPaginateLikeStrategy`, used for every `search` and every `$ilike` / `$sw` /
`$contains` filter built from it. Unset by default, in which case the process-wide
`PaginateLikeDefaults.Strategy` applies — so nothing changes for a configuration that does not call this.

It exists because `UseLikeStrategy(...)` and `UsePostgreSql()` assign a **process-wide static**. A host holding
two `DbContext`s on different providers cannot have both, and a strategy cannot decide for itself either:
`BuildLike(value, pattern)` is handed no provider and no context to dispatch on. Naming the strategy on the
configuration that targets a given provider is the way out, and it is resolved per query, so the composed SQL
for a configuration that sets none is unchanged.

```csharp
// The PostgreSQL-backed resource follows the process-wide ILIKE; the SQL Server one keeps portable LIKE.
var invoices = PaginateConfig<Invoice>.Create(b => b
    .WithLimits(25, 100)
    .WithTieBreaker(i => i.Id)
    .WithLikeStrategy(PaginateLikeDefaults.Portable)
    .Filterable("reference", i => i.Reference));
```

The same strategy also decides the `$op:` example the OpenAPI transformer publishes for that resource's filter
parameters, so the document follows the configuration rather than the static.

**Rejects at configuration time:** a null `strategy` → `ArgumentNullException`.

See [PostgreSQL → A strategy of your own](/integrations/postgresql/#a-strategy-of-your-own) for writing one.

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

The OpenAPI transformer resolves the provider from DI and activates it with `ActivatorUtilities.CreateInstance`
only when nothing is registered, so a provider with a parameterless constructor works without being registered.
Register it when its constructor needs services, or when the provider is worth sharing — a registered
instance is the one the transformer asks, and an instance it activated itself it also disposes.

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

Four small records carry that metadata, and you will hold them if you build anything off a config:

| Type | Members | What it is |
|------|---------|------------|
| `PaginateSort` | `Field`, `Direction` | One entry of `DefaultSortBy`. `Direction` is a `PaginateSortDirection` (`Asc` / `Desc`). |
| `PaginateFieldMetadata` | `Name`, `Type`, `Badge?` | A sortable or searchable field. `Type` is the selector's CLR type, which is what decides the documented type name and the example value. |
| `PaginateFilterFieldMetadata` | the same three, plus `Operators` | A filterable field. `Operators` is that field's allow-list, as a **set** — it carries no order, so sort it yourself if you are rendering it. |
| `PaginateBadge` | `Name`, `CssClass?` | What [`ShowBadge`](#showbadge) attached. `CssClass` is `null` for a badge declared without one. |

`Type` is the raw CLR type, not a display name — a nullable field reports `Nullable<int>`, and it is up to
you to unwrap it. The OpenAPI transformer does exactly that, and
[its table](/integrations/aspnetcore/openapi/#types-and-examples) is a reasonable mapping to copy if you are
rendering the same metadata somewhere else.
