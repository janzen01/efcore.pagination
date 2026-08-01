# Janzen.Pagination.PostgreSql

PostgreSQL provider for [Janzen.Pagination](https://github.com/janzen01/efcore.pagination) —
case-insensitive search via PostgreSQL's native **`ILIKE`** operator.

Without a provider package the engine falls back to a portable `LIKE` whose case-sensitivity
depends on the column collation. This package registers a strategy that emits true `ILIKE`,
giving correct case-insensitive search on PostgreSQL.

## Install

```bash
dotnet add package Janzen.Pagination.PostgreSql
```

Prereleases carry an `-rc.N` suffix — add `--prerelease` to install one.

Requires [`Janzen.Pagination.EntityFrameworkCore`](https://www.nuget.org/packages/Janzen.Pagination.EntityFrameworkCore)
(referenced transitively).

## Usage

Register once at startup. It makes **every** `PaginateConfig` emit native `ILIKE` in place of the portable
`LIKE` — there is no per-resource configuration:

```csharp
// Program.cs
builder.Services.AddPagination(pagination => pagination
    .AddAspNetCore()   // optional: ASP.NET Core integration
    .UsePostgreSql()); // emit native ILIKE for case-insensitive search / pattern filtering
```

Your `PaginateConfig<T>` definitions stay provider-agnostic:

```csharp
PaginateConfig<Product>.Create(b => b
    .WithLimits(25, 100)
    .Sortable("name", p => p.Name)
    .Searchable("name", p => p.Name)
    .WithTieBreaker(p => p.Id));
```

## What it changes

Three things emit a pattern match: free-text `search`, the `$ilike` and `$sw` filter operators, and
`$contains` on a string field. This package makes all of them use `ILIKE` instead of `LIKE`. Both forms pass an
explicit `ESCAPE '\'`, and the engine escapes `\`, `%` and `_` in the user's value, so a search for `100%`
matches that literal text.

It also nudges the generated OpenAPI examples: a filterable string field that allows `ILike` gets `$ilike:…`
as its example operator.

## Documentation

- [Providers & custom types](https://janzen01.github.io/efcore.pagination/guide/providers-and-types/)
- [Full guide](https://janzen01.github.io/efcore.pagination/)

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/master/LICENSE) © Lubos Jansky
