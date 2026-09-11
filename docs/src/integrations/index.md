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
provider is Entity Framework Core's own `EntityQueryProvider` and takes a different path when it is not:

| | EF provider | plain `IQueryable` (e.g. `List<T>.AsQueryable()`) |
|---|---|---|
| pattern matching (`search`, `$ilike`, `$sw`, string `$contains`) | `EF.Functions.Like` / `ILike` | `string.IndexOf` / `StartsWith` with `OrdinalIgnoreCase` |
| `$eq` and `$in` on a string | the column's collation decides | `Expression.Equal` / `Enumerable.Contains` — **ordinal, case-sensitive** |
| `$lt` / `$gt` / `$btw` on a string | the column's collation decides | `StringComparison.InvariantCulture` |
| filter values | wrapped in `EF.Parameter` for plan reuse | plain constants |
| count / materialise | `CountAsync` / `ToArrayAsync` | synchronous `Count` / `ToArray`, wrapped in a completed task |

So the whole pipeline — filters, search, sort, paging, projection — runs against an in-memory list, which
makes unit-testing a `PaginateConfig<T>` cheap. See [Testing your pagination](/recipes/testing/).

Read the first three rows together before relying on the leg for case behaviour: **in memory the operators
disagree with each other.** `?filter.name=$ilike:APPLE` matches `apple pie` while `?filter.name=$eq:APPLE`
does not, where SQL Server's usual collation matches both and PostgreSQL without the `.PostgreSql` package
matches neither. That is not a defect being reported here — a plain list has no collation to consult, so
each operator has to pick something — but it is the reason a case-sensitivity expectation formed against this
leg does not survive the move to a database.

There is a third case, and it is refused rather than adapted. A provider that is **asynchronous without being
Entity Framework Core's** — what a queryable-shaped mocking library produces — is neither leg, and the engine
answers it with a `PaginateQueryException` that says so. Sending it down the in-memory leg would have been the
friendlier answer and the wrong one: the two legs disagree on a substantial share of requests, so a test that
passed that way would prove nothing about the database. [Testing your pagination](/recipes/testing/) shows the
SQLite in-memory setup to use instead — a real provider, and it costs no more.
