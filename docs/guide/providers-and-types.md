# Providers & custom types

The engine is provider-agnostic and knows only the BCL's value types. Two extension points widen that: a
**LIKE strategy** decides how pattern matching is emitted, and **`PaginateTypeSupport`** teaches it new value
types. The `.PostgreSql` and `.NodaTime` packages are nothing more than pre-built uses of those two.

---

## `LIKE` vs `ILIKE`

Three things emit a pattern match: `search`, the `$ilike` / `$sw` operators, and `$contains` on a string field.
Which SQL they become is a single **process-wide** strategy.

| Strategy | Registered by | Emits | Case-insensitive? |
|----------|---------------|-------|-------------------|
| Portable *(default)* | nothing — it is the fallback | `EF.Functions.Like` → SQL `LIKE` | follows the column collation |
| PostgreSQL | `.UsePostgreSql()` | `NpgsqlDbFunctionsExtensions.ILike` → SQL `ILIKE` | yes, always |

```csharp
builder.Services.AddPagination(pagination => pagination
    .AddAspNetCore()
    .UsePostgreSql());
```

Your `PaginateConfig<T>` definitions do not change — they stay provider-agnostic, and only the emitted SQL
differs. The switch is **global**: it applies to every config in the process, because the provider is a
property of the database, not of a resource.

Both strategies pass an explicit `ESCAPE '\'`, and the engine escapes `\`, `%` and `_` in the user's value, so
a search for `100%` matches the literal text rather than everything.

Registering the strategy also nudges the OpenAPI examples: with PostgreSQL active, a filterable string field
that allows `ILike` gets `$ilike:…` as its example instead of the field's first configured operator.

> `$ilike` without `.UsePostgreSql()` is a plain `LIKE`. The token name describes intent; the guarantee comes
> from the strategy. On SQL Server the common collations are already case-insensitive, so `LIKE` behaves the
> way callers expect — but that is the collation's doing, not the library's.

### A strategy of your own

```csharp
internal sealed class CitextLikeStrategy : IPaginateLikeStrategy {

    // Which operator best represents this strategy in generated docs; null = use the field's first operator.
    public PaginateFilterOperator? PreferredExampleOperator => PaginateFilterOperator.ILike;

    // `value` is the column expression, `pattern` the already-escaped and parameterised LIKE pattern.
    public Expression BuildLike(Expression value, Expression pattern) => /* your EF.Functions call */;

}

builder.Services.AddPagination(p => p.UseLikeStrategy(new CitextLikeStrategy()));
```

Call it once at startup, before serving requests. It sets a static, so the last call wins — do not switch it
per request.

---

## NodaTime

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

### Why `Instant → DateTimeOffset` is free

Both map to PostgreSQL `timestamptz`: the stored value is the same UTC instant, so `ToDateTimeOffset()` has no
SQL form — there is nothing for the database to compute. EF Core applies it in the **shaper**, client-side,
over the page rows only.

That is why it composes freely inside a selector, sub-collections included, and still executes as one query
with a narrow `SELECT`. It is not a fallback to client evaluation of the whole query, and it is **not** a
reason to reach for `PaginateMapAsync`. See
[Projections → sub-collections and NodaTime](projections.md#sub-collections-and-nodatime-in-one-query).

A nullable `Instant?` can only be projected onto a nullable `DateTimeOffset?`; targeting a non-nullable one
fails with the engine's usual projection error rather than silently substituting a default.

---

## Teaching the engine a new type

`PaginateTypeSupport` is an append-only, process-wide registry. Call it once at startup, before the first
query. It is exactly what the NodaTime package uses.

### `RegisterValueParser` — make a type filterable

```csharp
// Ulid columns are now usable in .Filterable(...) and accept "?filter.id=$eq:01JB…" from the query string.
PaginateTypeSupport.RegisterValueParser(typeof(Ulid), raw =>
    Ulid.TryParse(raw, out var ulid)
        ? ulid
        : throw new PaginateQueryException($"Value '{raw}' is not a valid ULID."));
```

Throw `PaginateQueryException` for bad input — that is what turns into a `400` rather than a `500`. Without a
parser, filtering on that type is `400 Filtering values of type 'Ulid' is not supported.`

### `RegisterSimpleType` — stop projection recursing into it

```csharp
PaginateTypeSupport.RegisterSimpleType(typeof(Ulid));
```

Automatic projection treats an unknown non-primitive target as a nested DTO to build. Marking a type as simple
says "copy it, do not look inside". Needed for any struct-like value type you project directly.

### `RegisterProjectionConversion` — convert during projection

```csharp
// Ulid (entity) -> string (DTO), applied by the automatic projection builder.
PaginateTypeSupport.RegisterProjectionConversion((source, targetType) =>
    source.Type == typeof(Ulid) && targetType == typeof(string)
        ? Expression.Call(source, nameof(Ulid.ToString), Type.EmptyTypes)
        : null);                                     // null = this conversion does not apply
```

The delegate receives the source member expression and the target type, and returns either the converted
expression or `null`. Conversions are tried in registration order and the first non-null wins.

Keep the produced expression translatable — or, like `Instant.ToDateTimeOffset()`, cheap enough that EF
evaluating it in the shaper costs nothing. An expression that forces client evaluation of the whole query is
the one thing to avoid here.

---

## Non-EF `IQueryable`

The engine checks whether the source's provider is an EF `IAsyncQueryProvider` and adapts:

| | EF provider | plain `IQueryable` (e.g. `List<T>.AsQueryable()`) |
|---|---|---|
| pattern matching | `EF.Functions.Like` / `ILike` | `string.IndexOf` / `StartsWith` with `OrdinalIgnoreCase` |
| filter values | wrapped in `EF.Parameter` for plan reuse | plain constants |
| count / materialise | `CountAsync` / `ToArrayAsync` | synchronous `Count` / `ToArray`, wrapped in a completed task |

So the whole pipeline — filters, search, sort, paging, projection — runs against an in-memory list with
case-insensitive search, which makes unit-testing a `PaginateConfig<T>` cheap. See
[Recipes → test a config without a database](recipes.md#test-a-config-without-a-database).
