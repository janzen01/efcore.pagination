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

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/main/LICENSE) © Lubos Jansky
