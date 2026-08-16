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

1. **Filter parsing** for `Instant` (extended ISO-8601, e.g. `2026-01-31T23:59:59Z`) and `LocalDate`
   (`2026-01-31`), so those types work in `.Filterable(...)`:
   ```http
   ?filter.publishedAt=$btw:2026-01-01T00:00:00Z,2026-01-31T23:59:59Z
   ?filter.birthDate=$lte:2008-01-31
   ```
   An unparseable value is a `400 Value 'x' is not a valid instant.`
2. **Leaf-type classification** for `Instant`, `LocalDate` and `LocalDateTime`, so automatic projection copies
   them straight across instead of trying to recurse into them as nested DTOs.
3. **`Instant` → `DateTimeOffset` projection**, including the `Instant?` → `DateTimeOffset?` path.

The package registers all three through the same public registry any application can use — see
[Custom types](../custom-types/).

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
