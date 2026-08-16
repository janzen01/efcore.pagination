# Custom types

`PaginateTypeSupport` is an append-only, process-wide registry. Call it once at startup, before the first
query. It is exactly what the [NodaTime](../nodatime/) package uses — nothing there is privileged.

## `RegisterValueParser` — make a type filterable

```csharp
// Ulid columns are now usable in .Filterable(...) and accept "?filter.id=$eq:01JB…" from the query string.
PaginateTypeSupport.RegisterValueParser(typeof(Ulid), raw =>
    Ulid.TryParse(raw, out var ulid)
        ? ulid
        : throw new PaginateQueryException($"Value '{raw}' is not a valid ULID."));
```

Throw `PaginateQueryException` for bad input — that is what turns into a `400` rather than a `500`. Without a
parser, filtering on that type is `400 Filtering values of type 'Ulid' is not supported.`

## `RegisterSimpleType` — stop projection recursing into it

```csharp
PaginateTypeSupport.RegisterSimpleType(typeof(Ulid));
```

Automatic projection treats an unknown non-primitive target as a nested DTO to build. Marking a type as simple
says "copy it, do not look inside". Needed for any struct-like value type you project directly.

## `RegisterProjectionConversion` — convert during projection

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
