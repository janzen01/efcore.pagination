<img src="assets/icon.svg" width="96" alt="">

# Janzen.Pagination

Dynamic, configuration-driven pagination, filtering and sorting for **Entity Framework Core**
and **ASP.NET Core**.

An opinionated query-string contract — `?page=&limit=&sortBy=&search=&filter.<field>=$op:value` —
a fluent per-entity configuration, strict validation, and a ready-made paginated response with
metadata and links.

## Packages

| Package                                   | Docs                                                          | Purpose                                                                                                                 |
|-------------------------------------------|---------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------|
| **Janzen.Pagination.EntityFrameworkCore** | [README](src/Janzen.Pagination.EntityFrameworkCore/README.md) | Provider-agnostic query engine — fluent `PaginateConfig<T>`, filtering / sorting / search, projection, `PaginateAsync`. |
| **Janzen.Pagination.PostgreSql**          | [README](src/Janzen.Pagination.PostgreSql/README.md)          | PostgreSQL provider — case-insensitive search via native `ILIKE`.                                                       |
| **Janzen.Pagination.AspNetCore**          | [README](src/Janzen.Pagination.AspNetCore/README.md)          | ASP.NET Core integration — query-string model binding, `ProblemDetails`, links, OpenAPI metadata.                       |
| **Janzen.Pagination.NodaTime**            | [README](src/Janzen.Pagination.NodaTime/README.md)            | NodaTime support — filter / sort / project `Instant` and `LocalDate` (incl. `Instant` → `DateTimeOffset`).              |

## Quick start

```bash
dotnet add package Janzen.Pagination.EntityFrameworkCore
```

```csharp
// 1. Describe what is sortable / searchable / filterable for an entity.
var config = PaginateConfig<Product>.Create(b => b
    .WithLimits(defaultLimit: 25, maxLimit: 100)
    .Sortable("name", p => p.Name)
    .DefaultSortBy("name")
    .WithTieBreaker(p => p.Id)          // unique key appended → deterministic paging
    .Searchable("name", p => p.Name)
    .Filterable("status", p => p.Status, PaginateFilterOperator.Eq));

// 2. Build a request (bound from the query string in ASP.NET Core, or directly).
var request = new PaginateQuery { Page = 1, Limit = 25, SortBy = ["name:DESC"] };

// 3. Execute against an IQueryable, projecting to a DTO.
PaginatedResponse<ProductDto> response = await dbContext.Products
    .PaginateAsync<Product, ProductDto>(request, config);
```

For the full query-string contract, ASP.NET Core model binding and PostgreSQL `ILIKE`, see the per-package READMEs
linked above.

## Architecture

`EntityFrameworkCore` is the core engine. `PostgreSql` and `AspNetCore` build **on top of it** and
are independent of each other — pick the extensions you need:

```
            Janzen.Pagination.EntityFrameworkCore   (core engine)
                      ▲                     ▲
                      │                     │
   Janzen.Pagination.PostgreSql   Janzen.Pagination.AspNetCore
        (ILIKE provider)            (web pipeline integration)
```

The engine is usable on its own; the provider package upgrades search to true `ILIKE`, and the
ASP.NET Core package wires pagination into the request/response pipeline.

## Status

> **Not released yet.** The first public release to **NuGet.org** is still to come.
>
> Versions track the framework: the first component is the **.NET / EF Core major** the package targets, so the first
> release will be **10.x** (pairing with .NET 10 and EF Core 10) rather than 1.0.0. Release notes will live on the
> [Releases](https://github.com/janzen01/efcore.pagination/releases) page.

## Documentation

- **[SETUP.md](SETUP.md)** — development environment setup (restore, build, graphify) for working on the library.
- **[CLAUDE.md](CLAUDE.md)** — architecture, public API surface, versioning, conventions, and intentional decisions (guide for humans and AI agents).
- **Per-package usage** — each package's README, linked in [Packages](#packages) above.

## License

[MIT](LICENSE) © Lubos Jansky
