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
| `sortBy` | `array` of `string`, exploded, **enum of every `field:ASC` / `field:DESC`**, maximum `MaxSortFields` items | `Sortable`, `DefaultSortBy`, `WithGuards` |
| `search` | `string`, maximum `MaxSearchLength` characters | `Searchable`, `WithGuards` |
| `searchBy` | `array` of `string`, exploded, enum of the searchable names | `Searchable` |
| `filter.<field>` | `array` of `string`, exploded, one parameter **per filterable field** | `Filterable`, `FilterableMany`, `WithGuards` |
| `400` response | `application/problem+json` with `type` / `title` / `status` / `detail` / `code`, plus `traceId` where the app sends one | fixed |

Three conditions worth knowing:

- **Both search parameters are omitted** when the config declares no `Searchable` field at all. There is no
  free-text surface to document: `search` would advertise an input whose only possible answer is a `400`, and
  `searchBy` one with nothing to narrow.
- **`searchBy` alone is omitted** when the config calls
  [`IgnoreSearchByInQueryParam()`](/reference/configuration/#ignoresearchbyinqueryparam). It is ignored at
  run time, so advertising it would be a lie.
- **`filter.` parameters are ordered by field name** (ordinal), not by declaration order, so the document is
  stable across config edits that only move lines around.

All nine [guards](/reference/configuration/#withguards) reach the document. Five of them are expressible as
JSON Schema and are published that way — `MaxLimit` as `maximum`, `MaxSortFields` as `maxItems` and
`MaxSearchLength` as `maxLength`, alongside `DefaultLimit` and the `page` minimum. The rest have no keyword
that fits and are published as a sentence instead: `MaxOffset` on `page`, `MinSearchLength` on `search`, and
`MaxFilterValues` / `MaxFilterConditions` on every `filter.<field>`. A validating gateway therefore turns away
at the edge only what the engine already answers with a `400` — with one caveat: the engine measures the
**trimmed** search term, so a padded one can be inside `MaxSearchLength` for the engine and outside
`maxLength` for the validator.

The `400` schema documents **what that operation actually sends**, which is why it is not the same on both
legs. `type`, `title`, `status`, `detail` and `code` are always there — `code` names the cause as a stable
token, so a client branches on it rather than on the `detail` prose; see
[Errors as ProblemDetails](../#errors-as-problemdetails) for the member itself. `traceId` is added by the app's
`ProblemDetailsFactory` on a controller operation and by the problem-details writer on a Minimal API one, so
it is published for every controller operation and for a Minimal API operation only when the app registered
`AddProblemDetails()` — see [Errors as ProblemDetails](../#errors-as-problemdetails). `instance` is not
published at all: neither pipeline sets one and the framework synthesises none, so a generated model would
carry a property that is always `null`.

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
| `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong` | `integer` | `42` |
| `float`, `double`, `decimal` | `number` | `9.99` |
| `DateTime`, `DateTimeOffset` | `date-time` | `2025-01-01T00:00:00Z` |
| `DateOnly` | `date` | `2025-01-01` |
| `TimeOnly` | `time` | `09:00:00` |
| `TimeSpan` | `duration` | `PT2H30M` |
| `char` | `character` | `a` |
| an enum | its members, joined by a pipe | the first member |
| `Instant` ([NodaTime](../../nodatime/)) | `date-time (UTC)` | `2025-01-01T00:00:00Z` |
| `LocalDate` ([NodaTime](../../nodatime/)) | `date` | `2025-01-01` |
| `LocalDateTime` ([NodaTime](../../nodatime/)) | `date-time (local)` | `2025-01-01T00:00:00` |
| `LocalTime` ([NodaTime](../../nodatime/)) | `time` | `09:00:00` |
| `OffsetDateTime` ([NodaTime](../../nodatime/)) | `date-time (offset)` | `2025-01-01T00:00:00-05:00` |
| `Duration` ([NodaTime](../../nodatime/)) | `duration` | `PT2H30M` |
| `YearMonth` ([NodaTime](../../nodatime/)) | `year-month` | `2025-01` |
| anything else (a type you registered yourself) | the type's name | `value` |

Nullable types document as their underlying type. A duration is exemplified in its ISO-8601 spelling and an
offset date-time with a negative offset, because both forms survive being pasted into a URL unencoded: a
literal `+` decodes to a space.

The example's **operator** is `$eq` wherever the field grants it, and otherwise the lowest operator it does
grant, `$null` last of all — except that when a [LIKE strategy](../../postgresql/) advertises a preferred
operator and the field allows it, that one wins. So the same config documents `$eq:text` normally and
`$ilike:text` once `UsePostgreSql()` is registered: the example follows what the deployment can actually do.
The rule is deliberately independent of the order the operators were declared in, because this example lands
in a consumer's committed OpenAPI document and a regenerated one is diffed against it.

Two operators are not spelled `$operator:value`, because the engine does not accept them that way. `$null`
carries no value and is exemplified bare; `$btw` takes exactly two comma-separated bounds and is exemplified
with both.

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
