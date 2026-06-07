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

> **Pre-release (0.3.0).** Published to a private GitHub Packages feed while the public API
> stabilises. A public NuGet.org release is planned. See [CHANGELOG](CHANGELOG.md).

See each package's README (linked above) for installation and usage.

## License

[MIT](LICENSE) © Lubos Jansky
