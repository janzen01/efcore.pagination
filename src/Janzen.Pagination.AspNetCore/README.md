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
// Program.cs — register binder, error filter and OpenAPI metadata.
services.AddPagination(pagination => pagination.AddAspNetCore());

// Controller — PaginateQuery is bound from the query string.
[HttpGet]
[PaginatedQuery<JudgeConfigProvider>]
public Task<PaginatedResponse<JudgeDto>> Get([FromQuery] PaginateQuery request) =>
    _dbContext.Judges.PaginateAsync<JudgeDto>(request, _config, HttpContext.Request);
```

## License

[MIT](https://github.com/janzen01/efcore.pagination/blob/main/LICENSE) © Lubos Jansky
