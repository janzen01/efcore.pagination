# Upgrading from 10.x to 11.0

The public API of the `11.x` packages is the one `10.1.1` shipped: no member was removed, renamed or re-signed.
What changes is the platform underneath. The major version tracks the framework, so `11.x` pairs with .NET 11 and
EF Core 11, and moving to it is a retarget of your own project more than a change to your pagination code. The
`10.x` line stays serviced for .NET 10 until three months after `11.0.0`, and its documentation is its own copy of
this site, one pick away in the version picker.

## What stays the same

- **The public API.** `PaginateConfig<T>`, `PaginateQuery`, `PaginatedResponse<T>`, the `Paginate*Async` entry
  points and the composers compile as they did.
- **The wire contract.** The [query string](/reference/query-string/), the [response envelope](/reference/response/)
  and the [`400` catalog](/reference/errors/) are unchanged.
- **Your configs.** A `PaginateConfig<T>` that builds on `10.1.1` builds here, including the required tie-breaker
  `10.1.0` introduced.

## What you change

1. **Retarget to `net11.0`.** The packages have no `net10.0` asset, so a project that stays on .NET 10 fails
   restore with NU1202. Stay on the `10.x` line if you cannot move yet.
2. **Move to EF Core 11.** The engine builds expression trees against it, and a `net10.0` assembly loaded against
   EF Core 11 can fail at run time, which is why this is a new line rather than a retarget in place. The
   dependency range stops below the next major: a restore that resolves EF Core 12.0.0 or later reports NU1608.
3. **`Janzen.Pagination.PostgreSql`: take the Npgsql EF Core provider's 11 line** along with EF Core.
4. **`Janzen.Pagination.AspNetCore`: ASP.NET Core 11 brings `Microsoft.OpenApi` 3.x.**
   - The pagination transformer needs nothing from you. It still adds the same parameters and the same `400` to
     the operations that carry `[PaginatedQuery<TProvider>]` or `WithPagination<TProvider>()`.
   - Your *own* transformers, committed OpenAPI documents and generated clients are yours to migrate. That is the
     framework's change, not this package's, and its migration notes are the place to read it.
   - Keep the direct reference to `Microsoft.AspNetCore.OpenApi` in your app, or `AddOpenApi()` fails to build
     with CS9137. [OpenAPI](/integrations/aspnetcore/openapi/) says why.

## What the framework changed under you

EF Core 11 makes a split query (`AsSplitQuery()`) throw `DbQueryConcurrencyException` when its statements read
rows that changed in between, where it used to return an empty child collection. If a
[`PaginateSelectAsync`](/guide/projections/) selector with sub-collections runs as a split query, that now
surfaces as a `500`. [What is not a 400](/reference/errors/#what-is-not-a-400) has the options.

## Unchanged on purpose

- **Still not trim-safe or Native-AOT-safe.** The annotations and the analyzer warnings they produce are listed
  under [Requirements](../getting-started/#requirements).
- **Still not strong-named.** The only cost is CS8002 in a project that strong-names its own assemblies.

## Checking the upgrade

Build with warnings as errors, run your tests, and, if you commit your OpenAPI document, regenerate it and read
the diff. A change in the pagination parameters or the `400` is worth reporting; a change in everything around
them is the framework's.
