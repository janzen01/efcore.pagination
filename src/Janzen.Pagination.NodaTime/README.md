# Janzen.Pagination.NodaTime

NodaTime support for [Janzen.Pagination](https://github.com/janzen01/efcore.pagination): filter, sort
and project **`Instant`**, **`LocalDate`**, **`LocalDateTime`**, **`LocalTime`**, **`OffsetDateTime`**,
**`Duration`** and **`YearMonth`** values, including the projections onto their BCL counterparts.

The core engine is NodaTime-free; this package registers NodaTime value parsers and projection
conversions with the engine's extensibility registry (`PaginateTypeSupport`).

## Install

```bash
dotnet add package Janzen.Pagination.NodaTime
```

Requires [`Janzen.Pagination.EntityFrameworkCore`](https://www.nuget.org/packages/Janzen.Pagination.EntityFrameworkCore)
(referenced transitively).

## Usage

Register once at startup, before serving requests:

```csharp
services.AddPagination(pagination => pagination.UseNodaTime());

// non-DI hosts:
PaginateNodaTime.Register();
```

## `Instant → DateTimeOffset` projection

`Instant` and `DateTimeOffset` both map to PostgreSQL `timestamptz` — the stored value is the same UTC
instant. So `instant.ToDateTimeOffset()` has **no SQL form**: there is nothing for the database to compute,
it is a zero-cost CLR reinterpret. The conversion therefore runs in EF Core's **shaper** (client-side, over
the page rows only), never as a translated SQL function.

This is not a fallback and does **not** force full-entity materialization. The raw `Instant` columns and any
sub-collections stay in SQL, so the `SELECT` stays narrow; only the trivial date cast is lifted into the
shaper. Concretely, the conversion works:

- in the **auto** builder (`PaginateAsync<TEntity, TResult>(request, config)`), for top-level and single nested objects;
- in a caller **selector** (`PaginateSelectAsync<TEntity, TResult>(request, config, selector)`), anywhere in the terminal
  projection — **including inside one-to-many sub-collection items** — so a single selector can combine
  sub-collections with `Instant → DateTimeOffset` (and the nullable `Instant?` path) and still execute as one
  query. See the *Projection strategies* section of the
  [core package README](https://www.nuget.org/packages/Janzen.Pagination.EntityFrameworkCore).

Reach for `PaginateMapAsync` only when the response needs the fully loaded entity — not merely because a
projection mixes sub-collections with these date conversions.

## Supported types

Registration adds a value parser per type, so each works in `.Filterable(...)` and accepts ISO-8601 from the
query string:

```http
?filter.publishedAt=$btw:2026-01-01T00:00:00Z,2026-01-31T23:59:59Z
?filter.birthDate=$lte:2008-01-31
```

| Type | Accepted format | Projects onto |
|------|-----------------|---------------|
| `Instant` | `2026-01-31T23:59:59Z`, or an offset form such as `2026-02-01T00:59:59+01:00` | `DateTimeOffset` |
| `LocalDate` | `2026-01-31` | `DateOnly` |
| `LocalDateTime` | `2026-01-31T23:59:59` | `DateTime` |
| `LocalTime` | `23:59:59` | `TimeOnly` |
| `OffsetDateTime` | `2026-01-31T23:59:59+01:00` | `DateTimeOffset` |
| `YearMonth` | `2026-01` | — |
| `Duration` | `2:30:00` or ISO-8601 `PT2H30M` | — |

An unparseable value is a `400 Value 'x' is not a valid instant.` All of them are also registered as
projection leaf types, so the automatic projection copies them across instead of trying to recurse into them.
`ZonedDateTime`, `Period` and `Interval` are deliberately absent — see the integration page for why.

## Documentation

- [NodaTime integration](https://janzen01.github.io/efcore.pagination/integrations/nodatime/)
- [Custom types](https://janzen01.github.io/efcore.pagination/integrations/custom-types/)
- [Projections](https://janzen01.github.io/efcore.pagination/guide/projections/)
- [Full guide](https://janzen01.github.io/efcore.pagination/)

## Debugging

The package ships **embedded PDBs with Source Link**, so a debugger steps straight into these sources at the exact
commit the version was built from. Nothing to configure: no symbol server, no separate symbol download, and it works
offline.

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/master/LICENSE) © Lubos Jansky
