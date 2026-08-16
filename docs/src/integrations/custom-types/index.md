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

## Putting the three together

The three calls answer three different questions, and a type usually needs more than one:

| You want to | Register |
|-------------|----------|
| filter on it from the query string | `RegisterValueParser` |
| project it **as itself** onto a DTO | `RegisterSimpleType` |
| project it **as something else** — `Ulid` → `string` | `RegisterProjectionConversion` |

Registering only the parser leaves projection trying to build a nested DTO out of your value type;
registering only the simple type leaves `?filter.id=$eq:…` returning
`400 Filtering values of type 'Ulid' is not supported.` One startup block covers all of it:

```csharp
public static class UlidPaginationSupport {

    public static void Register() {

        PaginateTypeSupport.RegisterValueParser(typeof(Ulid), raw =>
            Ulid.TryParse(raw, out var ulid)
                ? ulid
                : throw new PaginateQueryException($"Value '{raw}' is not a valid ULID."));

        PaginateTypeSupport.RegisterSimpleType(typeof(Ulid));

        PaginateTypeSupport.RegisterProjectionConversion((source, targetType) =>
            source.Type == typeof(Ulid) && targetType == typeof(string)
                ? Expression.Call(source, nameof(Ulid.ToString), Type.EmptyTypes)
                : null);

    }

}

// Program.cs, before the first request.
UlidPaginationSupport.Register();
```

Three properties of the registry worth knowing before you call it:

- **Process-wide.** There is no per-config or per-request scope; a registration affects every query in the
  application, and nothing can be unregistered.
- **The three behave differently on a repeat call.** Parsers and simple types are keyed by type, so
  registering the same type again **replaces** the previous parser. Projection conversions are **appended**
  and tried in registration order, with the first non-`null` result winning — so with two conversions that
  could both apply, registration order decides.
- **Safe to call concurrently, but register at startup anyway.** The registry itself is thread-safe; what is
  not deterministic is a query that runs before the registration and therefore sees the old behaviour. Doing
  it lazily on first use is how that becomes an intermittent bug.

Because it is the same registry the [NodaTime](../nodatime/) package uses, anything that package does to
`Instant` and `LocalDate` is something you can do to a type of your own. There is no privileged path.

## What it cannot do

`PaginateTypeSupport` teaches the engine about **values**, not about operators or SQL. It cannot add a filter
operator, change how a pattern match reaches SQL — that is a
[LIKE strategy](../postgresql/#a-strategy-of-your-own) — or make an untranslatable expression translate. If
EF cannot turn your conversion into SQL, registering it here does not change that; it just moves where the
failure appears.
