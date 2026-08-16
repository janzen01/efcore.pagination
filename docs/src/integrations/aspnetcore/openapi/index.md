# OpenAPI

The same `PaginateConfig` the engine enforces also generates the documented parameters, so the two cannot
drift apart. A field you stop exposing disappears from the document in the same commit it stops working.

```csharp
using Janzen.Pagination.AspNetCore.OpenApi;

builder.Services.AddOpenApi(options =>
    options.AddOperationTransformer<PaginatedQueryOperationTransformer>());
```

`PaginatedQueryOperationTransformer` is a plain `IOpenApiOperationTransformer`, so your app keeps ownership of
the document name and the rest of the pipeline.

## Which operations it touches

Only the ones carrying `[PaginatedQuery<TProvider>]` (controllers) or `WithPagination<TProvider>()` (Minimal
APIs). Everything else passes through untouched.

The provider is created with `ActivatorUtilities.CreateInstance`, so **a provider with a parameterless
constructor needs no DI registration**. Register it only when its constructor takes services.

Before adding anything, the transformer **removes the parameters the framework generated for
`PaginateQuery`** — any query parameter whose name matches one of the six, or begins with `filter.`. Without
that step the document would carry both the framework's guess (`SortBy`, `Filters`, …) and the real contract.

## What it emits

Six parameters plus a `400`, in this order:

| Parameter | Shape | Built from |
|-----------|-------|------------|
| `page` | `integer`, minimum `1`, default `1` | fixed |
| `limit` | `integer`, minimum `1`, **maximum `MaxLimit`**, default `DefaultLimit` | `WithLimits` |
| `sortBy` | `array` of `string`, exploded, **enum of every `field:ASC` / `field:DESC`** | `Sortable`, `DefaultSortBy` |
| `search` | `string` | fixed |
| `searchBy` | `array` of `string`, exploded, enum of the searchable names | `Searchable` |
| `filter.<field>` | `array` of `string`, exploded, one parameter **per filterable field** | `Filterable`, `FilterableMany` |
| `400` response | `application/problem+json` with `type` / `title` / `status` / `detail` / `instance` | fixed |

Two conditions worth knowing:

- **`searchBy` is omitted entirely** when the config calls
  [`IgnoreSearchByInQueryParam()`](/reference/configuration/#ignoresearchbyinqueryparam). It is ignored at
  run time, so advertising it would be a lie.
- **`filter.` parameters are ordered by field name** (ordinal), not by declaration order, so the document is
  stable across config edits that only move lines around.

Exploded array parameters are what tell a client to repeat the key — `?sortBy=a:ASC&sortBy=b:DESC` — rather
than comma-join it.

## What a reader actually sees

Take the config from the [guide](/guide/configuration/#where-a-config-lives), with `price` added as a
filterable decimal and `status` as an enum:

**`limit`** carries the resource's real numbers, not placeholders:

> Number of records per page. Must be between 1 and 100; out-of-range values return 400. Defaults to 25 when
> omitted.

**`sortBy`** lists the fields with their documented types, and its schema default is your `DefaultSortBy`:

> Parameter to sort by. Repeat this parameter to sort by multiple fields. The URL order defines sort priority.
>
> Sortable fields:
>
> - `name` (`string`)
> - `price` (`number`)

```json
"enum": ["name:ASC", "name:DESC", "price:ASC", "price:DESC"],
"default": ["name:ASC"]
```

**`filter.status`** spells out the grammar and the operators *that field* allows:

> Filter by `status`.
>
> Value type: `Draft | Active | Discontinued`
>
> Format: `filter.status={$not:}OPERATION:VALUE`
>
> Available operations:
>
> - `$eq`
> - `$in`
> - `$not`
> - `$and`
> - `$or`

An enum field documents its members as the value type, which is how a caller learns that enums are matched
**by name**. `$not`, `$and` and `$or` are appended to every filter field, because they are modifiers rather
than operators and are always available.

## Types and examples

The CLR type of the selector decides both the documented type name and the generated example value:

| Selector type | Documented as | Example |
|---------------|---------------|---------|
| `string` | `string` | `text` |
| `Guid` | `uuid` | `00000000-0000-0000-0000-000000000000` |
| `bool` | `boolean` | `true` |
| `short`, `int`, `long` | `integer` | `42` |
| `float`, `double`, `decimal` | `number` | `9.99` |
| `DateTime`, `DateTimeOffset` | `date-time` | `2025-01-01T00:00:00Z` |
| `Instant` ([NodaTime](../../nodatime/)) | `date-time (UTC)` | `2025-01-01T00:00:00Z` |
| `LocalDate` ([NodaTime](../../nodatime/)) | `date` | `2025-01-01` |
| an enum | its members, joined by a pipe | the first member |
| anything else | the type's name | `value` |

Nullable types document as their underlying type.

The example's **operator** is the field's first declared one — except that when a
[LIKE strategy](../../postgresql/) advertises a preferred operator and the field allows it, that one wins. So
the same config documents `$eq:text` normally and `$ilike:text` once `UsePostgreSql()` is registered: the
example follows what the deployment can actually do.

## Badges

[`ShowBadge`](/reference/configuration/#showbadge) appends an inline `<code>` chip to the description of the
parameter — or, for a sortable or searchable field, to that field's bullet in the list:

```csharp
.Filterable("isHidden", a => a.IsHidden, PaginateFilterOperator.Eq)
    .When(currentUserIsAdmin).ShowBadge("Admin only", "language-admin")
```

renders as `Filter by isHidden. <code class="language-admin">Admin only</code>`, which you colour from the
reference UI's own custom CSS:

```css
.language-admin { background: #8B1A1A; color: #fff; border-radius: 4px; padding: 1px 6px }
```

The `language-` prefix is not a convention, it is the constraint. An API reference UI such as Scalar renders
descriptions as GitHub-flavoured Markdown through a sanitizer that strips inline `style` and every class on a
`<code>` element except one matching `language-*`. A badge styled any other way arrives as plain text, which
is why `ShowBadge` rejects the class at configuration time rather than letting you discover it in the
rendered page. Badge names are HTML-encoded, so a stray `<` cannot break the markup.

Colouring is limited to descriptions. The `sortBy` and `searchBy` **enum values** are plain strings in the
schema, so a badge cannot reach them — a sortable field's badge shows in the field list above the enum, not
on the entry itself.

## Conditional fields stay documented

A field gated by [`.When(false)`](/reference/configuration/#when) is still emitted. The document therefore
describes the **widest** contract rather than the current caller's, and enforcement happens at query time —
where the rejection is deliberately indistinguishable from an unknown field.

That asymmetry is the reason `.When(...)` insists on a badge: the parameter is visible to everyone, so the
restriction has to be visible too.
