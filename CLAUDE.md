# Janzen.Pagination (EF Core + ASP.NET Core pagination library)

Dynamic, configuration-driven **pagination, filtering and sorting** for **Entity Framework Core** and **ASP.NET Core**,
shipped as four composable NuGet packages (`Janzen.Pagination.*`). **net10.0-only**, C# `latest`, nullable-enabled.
Not released yet — the first public version will be **10.x** (see *Versioning* below).

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
- **Entry points** (extension methods on `IQueryable<TEntity>`). One name per projection strategy — deliberately *not*
  overloads, so the choice is explicit at the call site and adding an optional parameter later stays non-breaking
  (`Select` = projected in SQL, `Map` = mapped in memory):
  - `PaginateAsync<TEntity, TResult>(request, config, …)` — SQL-side projection built automatically by reflection.
  - `PaginateSelectAsync<TEntity, TResult>(request, config, selector, …)` — SQL-side projection from a caller-supplied
    translatable `selector` (supports aggregates, sub-collection projections).
  - `PaginateSelectMapAsync<TEntity, TProjection, TResult>(request, config, selector, postMap, …)` — SQL-side
    projection, then `postMap` over the page **in memory** for the fields EF cannot translate.
  - `PaginateMapAsync<TEntity, TResult>(request, config, projector, …)` — paginate, then map **in memory** (computed
    fields / collections needing client-side logic); materializes the full entity.
- **`PaginateFilterOperator`** — `Eq`, `In`, `Null`, `StartsWith`, `Contains`, `ILike`, `GreaterThan(OrEqual)`,
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
- **Commits:** small and incremental (one logical change each).
- Each packable project ships its **own `README.md`** as the NuGet package readme — keep it in sync with behavior.
- **GitHub Actions are pinned to a full commit SHA**, with the version in a trailing comment
  (`uses: actions/checkout@3d3c42e… # v7.0.1`). Never replace a SHA with a tag — see *Intentional decisions*.
  Dependabot bumps the SHA and the comment together; minor and patch flow through, a major is a decision.

## Versioning
The package version's **first component tracks the .NET / EF Core major it targets** — a `10.x` package pairs with
.NET 10 and EF Core 10. This is lockstep versioning, as used by `Npgsql.EntityFrameworkCore.PostgreSQL` and
`Microsoft.Extensions.*`, so the pairing is visible without reading the dependency list.

- **Own breaking changes ride the framework major** whenever possible. Mid-cycle ones go into **minor** and are called
  out in the release notes — the major is not available for them.
- **A new .NET major means a new package line** (`11.x`). The engine touches expression trees, `EF.Parameter` and
  `EF.Functions`, so a rebuild against the new EF Core major is needed regardless of the version scheme: a `net10.0`
  assembly loaded against EF Core 11 can fail at runtime. Dependabot opens the `Microsoft.EntityFrameworkCore` major
  PR, which is the reminder; CI then says whether it is a plain retarget or a real port.
- **Older lines are not maintained in parallel.** `10.x` stays available on nuget.org as published; backport only on
  request.
- **No four-part versions.** NuGet drops a zero fourth component (`10.1.0.0` *is* `10.1.0`) and treats `1`, `1.0`,
  `1.0.0` and `1.0.0.0` as equal, so the component count would flicker per release. Three components only.
- Version lives in `<Version>` in [Directory.Build.props](Directory.Build.props) — there is **no MinVer** here.

## Testing
- **No test project currently in the repo.** Verify changes by building clean under `-warnaserror` and exercising the
  library from a consuming app — the public entry points (the `Paginate*Async` family) need a live EF Core provider to
  execute.

## Intentional decisions — do NOT "fix" these
- **net10.0-only** — net9 is EOL and net8 lacks the EF Core 9+ surface the engine relies on (e.g. `EF.Parameter`).
  Don't re-introduce multi-targeting.
- **`[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`** on every public `Paginate*Async` entry point — the engine
  builds expression trees and uses reflection, so it is **not** trim/AOT-safe. The annotations give consumers accurate
  analyzer warnings instead of silent runtime failures; keep them.
- **Auto-projection maps constructor parameters** (records / positional ctors), **not** settable properties — projection
  DTOs should be records. This is by design, not a bug.
- **`nuget.config` lists nuget.org only** and clears machine sources — deliberate, for reproducible restores. nuget.org
  is both the restore source and the publish target; publishing authenticates via Trusted Publishing (OIDC), so there is
  no API-key secret in the repo.
- **`.slnx` + lock files** — enabling `RestorePackagesWithLockFile` on the `.slnx` restore fails with
  `Invalid framework identifier ''`; lock files are intentionally not enabled at the solution level.
- **Unknown query parameters are ignored.** The binder reads exactly six inputs (`page`, `limit`, `sortBy`, `search`,
  `searchBy`, `filter.<field>`); anything else (`offset`, `utm_*`, …) is dropped and the request pages normally.
  API-audit tools report this as "invalid value silently accepted" — it is a false positive. Strict binding would
  reject consumers' own tracking parameters, so don't add it. `page` and `limit` themselves are validated → `400`.
- **Actions are SHA-pinned and `dependabot.yml` ignores nothing for them.** A tag is a moving pointer the upstream
  owner can repoint; `publish.yml` exchanges an OIDC token for a live nuget.org push key, so anything running in that
  job can publish under the maintainer's name. All three workflows are pinned so the convention has no exceptions to
  remember. Don't "tidy" a SHA back into `@v7`, and don't re-add an `ignore` for minor/patch — a SHA doesn't follow
  releases, so ignoring those updates freezes the pins permanently.
- **`publish.yml` triggers on `release: published` only**, and `TAG` reads `github.event.release.tag_name` with **no**
  `|| github.ref_name` fallback. Both are load-bearing: a run without a release would otherwise carry a branch name
  into the tag-vs-version guard and push whatever version happened to be committed. nuget.org unlists, never deletes.

## Verifying a change
1. Build clean (warnings = errors): `dotnet build Janzen.Pagination.slnx -c Release -warnaserror`.
2. Touched the public API? Update the affected package `README.md` and the XML docs — a public-API change is a
   versioning decision.
3. `graphify update .` to refresh the graph.
