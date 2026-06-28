# Janzen.Pagination.NodaTime

NodaTime support for [Janzen.Pagination](https://github.com/janzen01/efcore.pagination): filter, sort
and project **`Instant`** and **`LocalDate`** values, including `Instant` → `DateTimeOffset` projection.

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

- in the **auto** builder (`PaginateAsync<TResult>(request, config)`), for top-level and single nested objects;
- in a caller **selector** (`PaginateAsync<TResult>(request, config, selector)`), anywhere in the terminal
  projection — **including inside one-to-many sub-collection items** — so a single selector can combine
  sub-collections with `Instant → DateTimeOffset` (and the nullable `Instant?` path) and still execute as one
  query. See the *Projection strategies* section of the
  [core package README](https://www.nuget.org/packages/Janzen.Pagination.EntityFrameworkCore).

Reach for `PaginateMapAsync` only when the response needs the fully loaded entity — not merely because a
projection mixes sub-collections with these date conversions.

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/main/LICENSE) © Lubos Jansky
