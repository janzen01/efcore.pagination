# NodaTime

```bash
dotnet add package Janzen.Pagination.NodaTime
```

```csharp
builder.Services.AddPagination(pagination => pagination.UseNodaTime());

// Non-DI hosts:
PaginateNodaTime.Register();
```

Registration is idempotent and process-wide. It adds three things:

1. **Filter parsing** for the types in the table below, so they work in `.Filterable(...)`:
   ```http
   ?filter.publishedAt=$btw:2026-01-01T00:00:00Z,2026-01-31T23:59:59Z
   ?filter.birthDate=$lte:2008-01-31
   ```
   An unparseable value is a `400 Value 'x' is not a valid instant.`
2. **Leaf-type classification** for all of them, so automatic projection copies them straight across instead
   of trying to recurse into them as nested DTOs.
3. **Projection conversions** onto the BCL type a DTO holds — see the table.

The package registers all three through the same public registry any application can use — see
[Custom types](../custom-types/).

## Supported types

| Type | Accepted format | Projects onto |
|------|-----------------|---------------|
| `Instant` | `2026-01-31T23:59:59Z`, **or** an offset form such as `2026-02-01T00:59:59+01:00` | `DateTimeOffset` |
| `LocalDate` | `2026-01-31` | `DateOnly` |
| `LocalDateTime` | `2026-01-31T23:59:59` | `DateTime` (unspecified kind) |
| `LocalTime` | `23:59:59` | `TimeOnly` |
| `OffsetDateTime` | `2026-01-31T23:59:59+01:00` | `DateTimeOffset` |
| `YearMonth` | `2026-01` | — |
| `Duration` | `2:30:00` **or** ISO-8601 `PT2H30M`. `P1M` / `P1Y` are a `400`: a month has no fixed length, and 30 days is an approximation, not an answer. | — |

Every conversion also carries the nullable pair (`Instant?` → `DateTimeOffset?`). There are no reverse
conversions: entities hold NodaTime, DTOs hold BCL types.

`Instant` accepts an offset form because `2026-02-01T00:59:59+01:00` names exactly one instant — there is
nothing ambiguous to refuse. A **bare date** is still a `400`: it would silently mean midnight, and which
midnight is a question the caller has not answered.

::: tip Not in the table, on purpose
`ZonedDateTime` has no canonical text form without first deciding on a zone provider — store an `Instant` and
present it zoned. `Period` is calendar arithmetic rather than a comparable value, and `Interval` is
two-valued, which `$btw` over `Instant` already expresses.
:::

## Why `Instant → DateTimeOffset` is free

Both map to PostgreSQL `timestamptz`: the stored value is the same UTC instant, so `ToDateTimeOffset()` has no
SQL form — there is nothing for the database to compute. EF Core applies it in the **shaper**, client-side,
over the page rows only.

That is why it composes freely inside a selector, sub-collections included, and still executes as one query
with a narrow `SELECT`. It is not a fallback to client evaluation of the whole query, and it is **not** a
reason to reach for `PaginateMapAsync`. See
[Projections → sub-collections and NodaTime](/guide/projections/#sub-collections-and-nodatime-in-one-query).

A nullable `Instant?` can only be projected onto a nullable `DateTimeOffset?`; targeting a non-nullable one
fails with the engine's usual projection error rather than silently substituting a default.

## End to end

An entity storing `Instant`, a DTO exposing `DateTimeOffset`, and one config in between:

```csharp
public sealed class Article {
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public Instant PublishedAt { get; set; }
    public LocalDate? EmbargoUntil { get; set; }
}

// The DTO speaks DateTimeOffset, so JSON clients need no NodaTime serializer.
public sealed record ArticleDto(Guid Id, string Title, DateTimeOffset PublishedAt);

var config = PaginateConfig<Article>.Create(b => b
    .WithLimits(25, 100)
    .Sortable("publishedAt", a => a.PublishedAt)
    .DefaultSortBy("publishedAt", PaginateSortDirection.Desc)
    .WithTieBreaker(a => a.Id)
    .Filterable("publishedAt", a => a.PublishedAt,
        PaginateFilterOperator.GreaterThanOrEqual,
        PaginateFilterOperator.LessThanOrEqual,
        PaginateFilterOperator.Between)
    .Filterable("embargoUntil", a => a.EmbargoUntil,
        PaginateFilterOperator.Null, PaginateFilterOperator.LessThanOrEqual));
```

```http
GET /articles?filter.publishedAt=$btw:2026-01-01T00:00:00Z,2026-01-31T23:59:59Z&filter.embargoUntil=$null
```

Both filter values are parsed by the registered NodaTime parsers, compared as `Instant` and `LocalDate` in
SQL, and the page comes back with `publishedAt` already converted:

```json
{ "id": "7f3c…", "title": "Widget shipped", "publishedAt": "2026-01-14T09:15:00+00:00" }
```

Sorting works on `Instant` directly — it is the column, so nothing is converted before the `ORDER BY`. The
conversion happens after paging, on the page's rows only.

In OpenAPI, an `Instant` field documents its type as `date-time (UTC)`, a `LocalDate` as `date`, a
`LocalDateTime` as `date-time (local)`, an `OffsetDateTime` as `date-time (offset)`, a `LocalTime` as `time`,
a `Duration` as `duration` and a `YearMonth` as `year-month`, with matching example values — see
[OpenAPI](../aspnetcore/openapi/#types-and-examples).

::: warning Keep NodaTime out of the DTO
Filter *parameters* are documented by this library, but the **response schema** is generated by ASP.NET
Core's own OpenAPI pipeline, which knows nothing about NodaTime. A DTO exposing an `Instant` directly is
therefore described structurally — an object with the type's members — rather than as an ISO-8601 string,
and clients generated from that document come out wrong.

Projecting to `DateTimeOffset` in the DTO, as above, avoids it: the conversion is free (see below), the JSON
is an ISO-8601 string either way, and the generated schema is correct without a schema transformer of your
own. Reach for a custom transformer only if the type genuinely has to survive into the DTO.
:::
