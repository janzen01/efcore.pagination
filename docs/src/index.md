---
layout: home

hero:
  name: Janzen.Pagination
  text: Pagination that stays a contract
  tagline: Declare once per entity what clients may sort, search and filter. The engine turns an opinionated query string into a translated EF Core query, and rejects everything it cannot honour.
  image:
    src: /icon.svg
    alt: Janzen.Pagination
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started/
    - theme: alt
      text: Query-string contract
      link: /reference/query-string/
    - theme: alt
      text: View on GitHub
      link: https://github.com/janzen01/efcore.pagination

features:
  - title: An allow-list, not a query language
    icon: 🔒
    details: A field that is not declared is not addressable, and an operator not granted for a field is rejected for that field. No accidental ORDER BY on an unindexed column.
    link: /guide/configuration/
    linkText: Configure a resource
  - title: Eleven filter operators
    icon: 🎛️
    details: $eq $in $null $sw $ilike $contains $lt $lte $gt $gte $btw, with $not negation and $and / $or between criteria on the same field.
    link: /reference/query-string/#operator-reference
    linkText: See the emitted SQL
  - title: Deterministic paging
    icon: 🧭
    details: A tie-breaker key is appended to every sort, so rows cannot drift between pages. Without one, the engine refuses rather than paging an unordered set.
    link: /reference/query-string/#sortby
    linkText: How sorting works
  - title: Four projection strategies
    icon: 🪄
    details: From a DTO built for you by reflection to a hand-written selector with sub-collections and aggregates, each keeping the SELECT narrow.
    link: /guide/projections/
    linkText: Pick a strategy
  - title: ASP.NET Core, wired
    icon: 🔌
    details: Query-string binding, ProblemDetails on bad input, first / prev / next / last links, and OpenAPI parameters generated from the same config the engine enforces.
    link: /integrations/aspnetcore/
    linkText: Wire up an endpoint
  - title: Guards by default
    icon: 🛡️
    details: Ceilings on page size, filter values, filter conditions, sort fields and search length, so one request cannot cost the database an afternoon.
    link: /reference/query-string/#guards
    linkText: See the defaults
---

## What it looks like

```http
GET /products?page=2&limit=25&sortBy=price:DESC&search=widget&filter.status=$in:Active,Draft&filter.price=$btw:10,500
```

That contract is borrowed from [nestjs-paginate](https://github.com/ppetzold/nestjs-paginate) (MIT) — the same
query parameters, operator names and response envelope.

> Versions track the framework: the first component is the .NET / EF Core major the package targets, so this is
> the **10.x** line, pairing with .NET 10 and EF Core 10. Older lines are not maintained in parallel, and
> release notes live on the [Releases](https://github.com/janzen01/efcore.pagination/releases) page.

## Packages

Four packages. The engine works on its own against any `IQueryable<T>`; the three add-ons build on it and are
independent of each other, so take only the ones you need.

| Package | Purpose |
|---------|---------|
| `Janzen.Pagination.EntityFrameworkCore` | Provider-agnostic query engine — fluent `PaginateConfig<T>`, filtering / sorting / search, projection, `PaginateAsync`. |
| `Janzen.Pagination.PostgreSql` | PostgreSQL provider — case-insensitive search via native `ILIKE`. |
| `Janzen.Pagination.AspNetCore` | ASP.NET Core integration — query-string model binding, `ProblemDetails`, links, OpenAPI metadata. |
| `Janzen.Pagination.NodaTime` | NodaTime support — filter / sort / project `Instant` and `LocalDate` (incl. `Instant` → `DateTimeOffset`). |

```mermaid
graph BT
    Core["<b>Janzen.Pagination.EntityFrameworkCore</b><br/>query engine: config, filtering, sorting, paging, projection"]

    Pg["<b>.PostgreSql</b><br/>native ILIKE"]
    Web["<b>.AspNetCore</b><br/>binding · ProblemDetails · links · OpenAPI"]
    Noda["<b>.NodaTime</b><br/>Instant · LocalDate"]

    Pg --> Core
    Web --> Core
    Noda --> Core

    classDef core fill:#512BD4,stroke:#512BD4,color:#fff
    classDef addon fill:none,stroke:#512BD4,stroke-width:2px
    class Core core
    class Pg,Web,Noda addon
```

## Install

```bash
dotnet add package Janzen.Pagination.EntityFrameworkCore
dotnet add package Janzen.Pagination.AspNetCore
```

## Where to go next

The guide is meant to be read in order, but each page stands on its own.
**[Getting started](./guide/getting-started/)** takes it from install to a paginated endpoint returning JSON.
**[Query-string contract](/reference/query-string/)** is the exact specification of every parameter, every
operator and every `400`, with the SQL each one produces.
