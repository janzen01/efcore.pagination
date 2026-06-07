# Janzen.Pagination.AspNetCore

ASP.NET Core integration for [Janzen.Pagination](https://github.com/janzen01/efcore.pagination).

Wires the pagination engine into the web pipeline:

- **Query-string model binding** — bind `PaginateQuery` straight from the request
  (`?page=&limit=&sortBy=&search=&filter.<field>=$op:value`).
- **`ProblemDetails` error handling** — invalid queries surface as consistent `400` responses.
- **Pagination links** — `first` / `previous` / `next` / `last` built from the current request.
- **OpenAPI metadata** — documents the pagination query parameters on annotated endpoints.

## Install

```bash
dotnet add package Janzen.Pagination.AspNetCore
```

Requires [`Janzen.Pagination.EntityFrameworkCore`](https://www.nuget.org/packages/Janzen.Pagination.EntityFrameworkCore)
(referenced transitively).

## Usage

```csharp
// Program.cs — register the query-string model binder and the 400 ProblemDetails filter.
services.AddPagination(pagination => pagination.AddAspNetCore());

// Register the OpenAPI operation transformer on your document so the pagination query
// parameters are documented on endpoints annotated with [PaginatedQuery<TConfigProvider>].
// (The transformer is a normal IOpenApiOperationTransformer — your app owns the document name.)
using Janzen.Pagination.AspNetCore.OpenApi;
services.AddOpenApi(options => options.AddOperationTransformer<PaginatedQueryOperationTransformer>());

// Controller — PaginateQuery is bound from the query string.
[HttpGet]
[PaginatedQuery<JudgeConfigProvider>]
public Task<PaginatedResponse<JudgeDto>> Get([FromQuery] PaginateQuery request) =>
    _dbContext.Judges.PaginateAsync<JudgeDto>(request, _config, HttpContext.Request);
```

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/main/LICENSE) © Lubos Jansky
