# Janzen.Pagination (EF Core + ASP.NET Core pagination library)

Dynamic, configuration-driven **pagination, filtering and sorting** for **Entity Framework Core** and **ASP.NET Core**,
shipped as four composable NuGet packages (`Janzen.Pagination.*`). **net10.0-only**, C# `latest`, nullable-enabled.
Pre-1.0 — the public API is stabilizing.

> **Machine setup** (prerequisites, restore, build, graphify) lives in **[SETUP.md](SETUP.md)** — not repeated here.
> This file is for *working in the code*.

## graphify — read the graph before the source
The knowledge graph at `graphify-out/` (god nodes, communities, cross-file edges) is **not committed** — it is
reproducible from source (AST + local clustering, no API cost). **Generate it yourself first**: run `graphify update .`
once after cloning; the git hooks then keep it current. Hooks enforce graphify-first; follow it:
- **No `graphify-out/` yet?** (fresh clone) → run `graphify update .` before relying on graph queries.
- Codebase questions → `graphify query "<question>"` first (scoped subgraph, far smaller than grep). Use
  `graphify path "<A>" "<B>"` for relationships, `graphify explain "<concept>"` for one concept.
- Read raw source only after graphify orients you, or to edit/debug specific lines.
- **After changing code** → `graphify update .` (AST-only, no API cost) to keep the graph current.
- `/graphify` → invoke the `graphify` skill.

## Commands
| Action             | Command                                                       |
|--------------------|---------------------------------------------------------------|
| Restore            | `dotnet restore Janzen.Pagination.slnx`                       |
| Build              | `dotnet build Janzen.Pagination.slnx -c Release -warnaserror` |
| Pack               | `dotnet pack Janzen.Pagination.slnx -c Release -o ./artifacts` |
| Refresh code graph | `graphify update .`                                           |

- The build entry point is the **`.slnx`** solution (`Janzen.Pagination.slnx`) — four library projects, all packable.
- `TreatWarningsAsErrors=true` — warnings fail the build. Missing XML docs (`CS1591`) are the one allowed exception.
- There is **no test project** in the repo — see *Testing* below.

## Architecture
```
src/
  Janzen.Pagination.EntityFrameworkCore/   core engine — PaginateConfig<T>, query building, PaginateAsync
  Janzen.Pagination.PostgreSql/            native ILIKE provider (NpgsqlLikeStrategy)
  Janzen.Pagination.AspNetCore/            query-string binding, ProblemDetails, links, OpenAPI metadata
  Janzen.Pagination.NodaTime/              Instant / LocalDate filter · sort · project
```
`EntityFrameworkCore` is the core engine; `PostgreSql`, `AspNetCore` and `NodaTime` build **on top of it** and are
independent of each other — consumers pick the extensions they need:
```
        Janzen.Pagination.EntityFrameworkCore   (core engine)
                          ▲
         ┌────────────────┼────────────────┐
    .PostgreSql       .AspNetCore       .NodaTime
  (ILIKE provider)   (web pipeline)   (NodaTime types)
```

## Public API surface
- **`PaginateConfig<T>`** — fluent, per-entity contract (`PaginateConfig<T>.Create(b => …)`): `.WithLimits(default, max)`,
  `.Sortable(name, expr)`, `.Searchable(name, expr)`, `.Filterable(name, expr, ops…)`, `.DefaultSortBy(…)`,
  `.WithTieBreaker(expr)` (unique key appended as the final order → deterministic paging), `.IgnoreSearchBy()`. Often
  exposed via an `IPaginateConfigProvider<T>`.
- **`PaginateQuery`** — immutable request: `Page`, `Limit`, `SortBy` (`["field:DESC"]`), `Search`, `SearchBy`, `Filters`
  (`field → ["$op:value"]`). In ASP.NET Core it binds from `?page=&limit=&sortBy=&search=&filter.<field>=$op:value`.
- **`PaginatedResponse<T>`** — envelope: `Items`, `Meta` (totalItems / itemCount / itemsPerPage / totalPages /
  currentPage), `Links` (first / prev / next / last / self — individual links are nullable, the record is not).
- **Entry points** (extension methods on `IQueryable<TEntity>`):
  - `PaginateAsync<TEntity, TResult>(request, config, …)` — SQL-side projection; auto (reflection-built) or a custom
    translatable `selector` (supports aggregates, sub-collection projections).
  - `PaginateMapAsync<TEntity, TResult>(request, config, projector, …)` — paginate, then map **in memory** (computed
    fields / collections needing client-side logic).
- **`PaginateFilterOperator`** — `Eq`, `In`, `StartsWith`, `EndsWith`, `Contains`, `ILike`, `GreaterThan(OrEqual)`,
  `LessThan(OrEqual)`, `Between`. Each field whitelists its allowed operators.
- **DI:** `services.AddPagination(b => { b.AddAspNetCore(); b.UsePostgreSql(); b.AddNodaTime(); });` — add only the
  extensions in play. `AddAspNetCore()` wires query-string binding, the `ProblemDetails` exception filter, and OpenAPI
  metadata.

## Providers — LIKE vs ILIKE
- Default search / `Contains` / `StartsWith` emit **portable `LIKE`** — case-sensitivity follows the column collation.
- `.UsePostgreSql()` registers `NpgsqlLikeStrategy` (`src/Janzen.Pagination.PostgreSql/Like/NpgsqlLikeStrategy.cs`)
  **globally**, upgrading those to native **`ILIKE`** (true case-insensitive). The provider-agnostic `PaginateConfig` is
  unchanged — only the emitted SQL differs. The strategy resolves `NpgsqlDbFunctionsExtensions.ILike` via reflection.

## Conventions
- **net10.0-only**, `Nullable=enable`, `ImplicitUsings=enable`, C# `latest` ([Directory.Build.props](Directory.Build.props)).
- **CPM** — every package version lives in [Directory.Packages.props](Directory.Packages.props); don't pin versions in a `.csproj`.
- **XML docs** on the public API (the `CS1591` *warning* is suppressed for now — the docs themselves are not).
- Build must stay clean under `-warnaserror` before any commit.
- **Commits:** small and incremental (one logical change each); end the message with
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Each packable project ships its **own `README.md`** as the NuGet package readme — keep it in sync with behavior.

## Testing
- **No test project currently in the repo.** Verify changes by building clean under `-warnaserror` and exercising the
  library from a consuming app — the public entry points (`PaginateAsync` / `PaginateMapAsync`) need a live EF Core
  provider to execute.

## Intentional decisions — do NOT "fix" these
- **net10.0-only** — net9 is EOL and net8 lacks the EF Core 9+ surface the engine relies on (e.g. `EF.Parameter`).
  Don't re-introduce multi-targeting.
- **`[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`** on the public `PaginateAsync` / `PaginateMapAsync` entry
  points — the engine builds expression trees and uses reflection, so it is **not** trim/AOT-safe. The annotations give
  consumers accurate analyzer warnings instead of silent runtime failures; keep them.
- **Auto-projection maps constructor parameters** (records / positional ctors), **not** settable properties — projection
  DTOs should be records. This is by design, not a bug.
- **`nuget.config` lists nuget.org only** and clears machine sources — deliberate, for reproducible restores. The GitHub
  Packages feed is a publish target, not a build dependency.
- **`.slnx` + lock files** — enabling `RestorePackagesWithLockFile` on the `.slnx` restore fails with
  `Invalid framework identifier ''`; lock files are intentionally not enabled at the solution level.
- **Unknown query parameters are ignored.** The binder reads exactly six inputs (`page`, `limit`, `sortBy`, `search`,
  `searchBy`, `filter.<field>`); anything else (`offset`, `utm_*`, …) is dropped and the request pages normally.
  API-audit tools report this as "invalid value silently accepted" — it is a false positive. Strict binding would
  reject consumers' own tracking parameters, so don't add it. `page` and `limit` themselves are validated → `400`.

## Verifying a change
1. Build clean (warnings = errors): `dotnet build Janzen.Pagination.slnx -c Release -warnaserror`.
2. Touched the public API? Update the affected package `README.md` and the XML docs — a public-API change is a
   versioning decision.
3. `graphify update .` to refresh the graph.
