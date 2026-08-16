# Integrations

The engine is provider-agnostic and knows only the BCL's value types. Everything else is an extension point,
and every package in this section is nothing more than a pre-built use of one:

| Package | Extension point it uses | Page |
|---------|-------------------------|------|
| `Janzen.Pagination.AspNetCore` | model binding, exception filter, OpenAPI transformer | [ASP.NET Core](./aspnetcore/) |
| `Janzen.Pagination.PostgreSql` | the **LIKE strategy** — how a pattern match is emitted | [PostgreSQL](./postgresql/) |
| `Janzen.Pagination.NodaTime` | **`PaginateTypeSupport`** — parsing, classification, projection | [NodaTime](./nodatime/) |
| *(your own code)* | the same registry the NodaTime package uses | [Custom types](./custom-types/) |

None of them changes what a `PaginateConfig<T>` looks like. A config written against SQL Server works
unchanged on PostgreSQL; only the emitted SQL differs.

## Non-EF `IQueryable`

Before any of that, there is one adaptation the engine makes on its own. It checks whether the source's
provider is an EF `IAsyncQueryProvider` and takes a different path when it is not:

| | EF provider | plain `IQueryable` (e.g. `List<T>.AsQueryable()`) |
|---|---|---|
| pattern matching | `EF.Functions.Like` / `ILike` | `string.IndexOf` / `StartsWith` with `OrdinalIgnoreCase` |
| filter values | wrapped in `EF.Parameter` for plan reuse | plain constants |
| count / materialise | `CountAsync` / `ToArrayAsync` | synchronous `Count` / `ToArray`, wrapped in a completed task |

So the whole pipeline — filters, search, sort, paging, projection — runs against an in-memory list, with
case-insensitive search, which makes unit-testing a `PaginateConfig<T>` cheap. See
[Recipes → test a config without a database](/recipes/#test-a-config-without-a-database).
