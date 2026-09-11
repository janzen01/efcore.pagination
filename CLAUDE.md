# Janzen.Pagination (EF Core + ASP.NET Core pagination library)

Dynamic, configuration-driven **pagination, filtering and sorting** for **Entity Framework Core** and **ASP.NET Core**,
shipped as four composable NuGet packages (`Janzen.Pagination.*`). **net10.0-only**, C# `latest`, nullable-enabled.
Published on nuget.org as the **10.x** line; prereleases carry an `-rc.N` suffix (see *Versioning* below).

> **Machine setup** (prerequisites, restore, build, graphify) lives in **[SETUP.md](SETUP.md)** — not repeated here.
> **Consumer documentation** — the query-string contract, every builder method, the projection strategies and the
> `400` catalogue — lives in **[docs/src/guide/](docs/src/guide/)** and is published at
> **<https://janzen01.github.io/efcore.pagination/>**. Behaviour described there is the published contract:
> change the behaviour, change the guide in the same commit.
> This file is for *working in the code*.

## The documentation site
`docs/` is a **VitePress** project published to GitHub Pages by [.github/workflows/docs.yml](.github/workflows/docs.yml).
Sources live in `docs/src`, the build lands in `docs/.dist`, config is
[docs/.vitepress/config.mts](docs/.vitepress/config.mts), package manager is **pnpm** pinned through
`packageManager` in [docs/package.json](docs/package.json).
- **Settings → Pages → Source must be "GitHub Actions"**, not "deploy from a branch". That is repository
  state no file here can set, `actions/configure-pages` will not flip it (its `enablement` input defaults to
  `false`), and `actions/deploy-pages` fails while the source is still the legacy Jekyll one:
  `gh api --method PUT repos/janzen01/efcore.pagination/pages -f build_type=workflow`. **Do it before merging
  a change that removes the Jekyll tree from `master:/docs`,** not after — the wrong order rebuilds Jekyll
  against a tree with no site root and takes the frozen URLs down with it.
- **The build lives in [docs-build.yml](.github/workflows/docs-build.yml) and is called twice.** `ci.yml`
  calls it on every pull request (`upload: false`) and `docs.yml` calls it from `master` to produce the
  artifact it deploys (`upload: true`) — one copy, so a PR verifies exactly what gets published. It is part
  of `ci-ok`, which means the two verifiers below are a **required** gate: without them a dead link merges
  green and then renders inside a released package forever.
  Two things follow. **Guard the Pages steps on `inputs.upload`, never `github.event_name`** — inside a
  called workflow that expression is the *caller's* event, so `!= 'pull_request'` would fire on a fork PR and
  redden a required check on a Pages API call it should never make. And **`docs.yml` is now deploy-only**, so
  its artifact hand-off and environment wiring are no longer exercised before a merge — dispatch it once
  after changing them.
- **`pnpm/action-setup` is passed `package_json_file: docs/package.json`.** Its default is `package.json`
  resolved against the *repository root*, which has none, and `defaults.run.working-directory` does not apply
  to a `uses:` step's inputs. Remove that input and the job dies before it reaches the build.
- **`pnpm docs:build` is the local build**, and it ends by running `scripts/verify-frozen-urls.mjs` and then
  `scripts/verify-anchors.mjs`. Keep all three chained: the first is the only thing standing between a rename
  and a dead link inside a released package, the second catches what `ignoreDeadLinks` structurally cannot —
  a link to a heading that no longer exists on a page that does. It reads ids out of `.dist` rather than
  deriving them from the markdown, because **VitePress slugify is not GitHub slugify**: an apostrophe becomes
  a dash (`keep-a-big-table-s-page-count-cheap`) and an em dash survives into the id verbatim
  (`paginateselectmapasync-—-sql-then-finish-in-memory`). Guessing the slug is how the two dead anchors that
  motivated the script got written.
- **The published URLs must keep answering, and every one of them ends with a slash** (`/guide/query-string/`).
  They ship inside the four package READMEs, which nuget.org renders per version forever. This is *not* a
  freeze on the site's structure: a page may move, as long as the old path still publishes something — the
  page, or a **redirect stub** (a markdown file whose `head` sets `http-equiv: refresh`, the same trick the
  MDS Dynamics docs use at their root). Later versions' READMEs can then point at the new location.
  What the rule really guards against is the accident: pages are authored as **`<name>/index.md`** →
  `<name>/index.html`, and renaming one to `<name>.md` builds `<name>.html`, which GitHub Pages serves at
  `/guide/query-string` but **not** at `/guide/query-string/`. `docs/scripts/verify-frozen-urls.mjs` fails the
  build when one of those paths has nothing behind it, stub or page. Its hardcoded list is *history* — what
  `10.0.0` published, never removable — and it additionally **reads the root and package READMEs** and requires
  every site URL they advertise to exist too. Only the **package** READMEs are what the *next* release
  freezes — packaging is per project and the root README ships in no package at all, so its URLs are checked
  and never promoted, and the script's closing summary counts the two sources separately. So repointing
  a README is safe: forget to publish the target and the build says so, naming the README. **A `#fragment` in a
  README URL is checked the same way**, against the ids in the built page — a README deep link is frozen exactly
  like the page it points into, and `verify-anchors.mjs` cannot see it (that one walks the markdown sources;
  these are absolute URLs in files VitePress never builds).
- **A dead link fails the build** (`ignoreDeadLinks: false`). Links are **relative within a section**
  (`../configuration/`, `../query-string/#guards`) and **root-absolute across sections**
  (`/guide/projections/`), always ending in a slash. Both halves matter: a relative link that crosses a
  section boundary resolves inside the *current* one, which is how two guide pages ended up pointing at the
  `/guide/aspnetcore/` redirect stub and silently losing their `#fragment` on the way through.
- **Which section a page belongs to is decided by how it is read**, not by what it is about: the guide is
  prose you follow once (one example per idea, link out for the full list), the reference is looked up
  mid-task and is exhaustive (one subject per page, signature + what it refuses + captured SQL), integrations
  are per package, the cookbook is task-shaped. A corollary that is easy to violate: **every fact has exactly
  one home** and the other pages link to it. A per-method enumeration inside the guide, or a second copy of
  the guards table, is the thing this rule exists to prevent.
- **Navigation lives in `config.mts`**, not in front matter. **Content** pages carry no front matter at all;
  VitePress takes the title from the first `#` heading. The exceptions are structural and each has to be one:
  `docs/src/index.md` is `layout: home` and is front matter almost end to end, the four redirect stubs carry
  the `head` refresh plus `search: false` / `robots: noindex` described above, and `docs/src/cs/index.md` is
  the excluded draft. A new page has to be added to the sidebar by hand, or it is reachable only by link and
  search.
- **`.gitignore` keeps `docs/*` deny-by-default** and re-includes the project by name (`!docs/src/`,
  `!docs/.vitepress/`, `!docs/scripts/`, `!docs/package.json`, `!docs/.npmrc`, `!docs/pnpm-lock.yaml`).
  `.npmrc` is load-bearing — it holds the `shamefully-hoist=true` without which `pnpm docs:dev` renders a
  blank page — so it is not a stale entry to tidy away. A new directory that is
  not re-included is invisible to git and therefore to the build, which then publishes a site missing that page
  without failing. Everything else under `docs/` is local planning and stays untracked.
- **English is the root locale** (`/`) and has to stay there: the frozen URLs have no locale prefix.
  **There is currently no second locale.** The Czech draft lives at `docs/src/cs/index.md`, is kept out of the
  build by `srcExclude: ['cs/**']`, and its `locales.cs` block is commented out of `config.mts`. Registering a
  locale advertises a translation, and VitePress rewrites the current path into it *unconditionally* — with one
  Czech page against 25 English ones the language switcher pointed at `/cs/<path>/` on every page but the home,
  48 dead links in the built output. Restore the locale and the `srcExclude` line together, once there is
  Czech content to switch to.
- **A new file under `.vitepress/theme/` needs the dev server restarted.** HMR picks up edits to a theme that
  already existed, but not the theme appearing for the first time, and the symptom is that the stylesheet
  simply has no effect while the build output has it. Cost an evening once: the mermaid CSS below looked
  broken when it was only unloaded.
- **Client-rendered output cannot be checked with `curl`** — in dev VitePress serves a 552-byte shell and
  mounts everything in the browser, and mermaid draws its SVG there too. To see the real DOM without asking
  someone to look: `& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --headless=new
  --virtual-time-budget=8000 --dump-dom <url>`.
- **The reading-preferences menu is `@nolebase/vitepress-plugin-enhanced-readabilities`**, mounted into the
  `nav-bar-content-after` and `nav-screen-content-after` slots in `.vitepress/theme/index.ts` (Layout Switch,
  which widens the content for the wide SQL blocks and tables, plus Spotlight). It needs **both** halves of
  the `vite` block in `config.mts`: it ships raw `.vue` in its dist, so `optimizeDeps.exclude` keeps the dev
  server from pre-bundling it (otherwise the menu silently never mounts) and `ssr.noExternal` makes Vite
  bundle it for SSR instead of letting Node `require` a `.vue` file (otherwise the production build fails
  while rendering). Same pairing as the MDS Dynamics docs. Extending the theme this way does not disturb
  mermaid, which registers through a Vite alias rather than through the theme — verified in the rendered DOM,
  not assumed.
- Mermaid comes from `vitepress-plugin-mermaid` via `withMermaid()`. It declares a peer on VitePress 1.x and we
  run the 2.0 alpha, so pnpm prints an unmet-peer warning; the diagrams render regardless (same pairing as the
  MDS Dynamics docs). If they ever stop rendering, that warning is the first place to look.

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
| Test               | `dotnet test Janzen.Pagination.slnx -c Release`               |
| Pack               | `dotnet pack Janzen.Pagination.slnx -c Release -o ./artifacts` |
| Refresh code graph | `graphify update .`                                           |

- The build entry point is the **`.slnx`** solution (`Janzen.Pagination.slnx`) — four packable library projects plus
  `test/Janzen.Pagination.Tests` (`IsPackable=false`, so it is excluded from packing *and* from the public-API
  analyzers).
- `TreatWarningsAsErrors=true` — warnings fail the build, with **no exceptions** for the packable projects. Missing XML
  docs (`CS1591`) included: a new public member without a doc comment is a build error. Only
  `test/Janzen.Pagination.Tests` suppresses `CS1591`, scoped in its own `.csproj` — that assembly has no consumers.

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
  `.WithGuards(…)`, `.WithMinSearchLength(n)`, `.WithMaxOffset(n)`, `.AllowUnlimited(maxRows)`, `.Sortable(name, expr)`, `.DefaultSortBy(…)`, `.WithTieBreaker(expr)` (unique key appended as the
  final order → deterministic paging), `.Searchable(name, expr)`, `.IgnoreSearchByInQueryParam()`,
  `.Filterable(name, expr, ops…)`, `.FilterableMany(name, coll, expr, ops…)` (matches any element → `Any(...)`), plus
  `.ShowBadge(name, cssClass?)` / `.When(bool)` on the field declared immediately before. Often exposed via an
  `IPaginateConfigProvider<T>`.
  **`PaginateConfigDefaults`** is the shared-limits object: passed to `Create(defaults, b => …)`, or assigned once
  to the static `PaginateConfigDefaults.Shared`. Resolution is `WithX` > the passed object > `Shared` > the engine
  constant, read at `Build()` time — so a shared value is a default, never a ceiling a config cannot raise, and a
  config never observes a later assignment. `AllowUnlimited` is deliberately **not** on it: an unbounded read is a
  claim about one resource's size. Its arrival is why `WithGuards`' four parameters became `int?` — with the old
  `int` defaults, naming one guard silently reset the other three to the constants, discarding shared values the
  caller never mentioned. Source-compatible, binary-breaking, and the one entry in
  `CompatibilitySuppressions.xml`.
  Both `Filterable` overloads have an **operator-less sibling** (`.Filterable(name, expr)`) whitelisting
  `PaginateFilterOperators.For<TValue>()` — the public derivation, and the single place a later release widens a
  row (which then widens every shorthand field on rebuild: release-note it). Ranges are deliberately withheld
  from `string` / `Guid` / `char` / enum, `Null` joins only where the value can be null, and an underivable type
  throws at `Build()` rather than guessing. The empty-array error stays on the *explicit* signature: "derive" is
  a signature the caller picks, never a fallback for a list that came out empty. Note the overload resolution —
  a pre-existing zero-operator call binds to the new overload in normal form and now *configures* instead of
  throwing; only already-compiled consumers keep the old behaviour, until they rebuild.
- **`PaginateQuery`** — immutable request: `Page`, `Limit`, `SortBy` (`["field:DESC"]`), `Search`, `SearchBy`, `Filters`
  (`field → ["$op:value"]`), plus `.WithPage(n)` — the same request on another page, which is how a caller with no
  `PaginateLinkContext` (so a `null` `Links`) navigates off `Meta`. It is a `class`, not a `record`: value equality over
  the collection properties would compare by reference and lie, so there is no `with`. In ASP.NET Core it binds from
  `?page=&limit=&sortBy=&search=&filter.<field>=$op:value`.
- **`PaginatedResponse<T>`** — envelope: `Items`, `Meta` (totalItems / itemCount / itemsPerPage / totalPages /
  currentPage, plus the non-positional **`SortBy` / `Search` / `SearchBy` / `Filter` / `HasPreviousPage` /
  `HasNextPage`** — the *effective* request echoed back, so a client sees where `DefaultSortBy` and the
  searchable-field defaults landed; the tie-breaker is deliberately absent from `SortBy`, and field names are
  canonical rather than as-typed), `Links` (first / previous / next / last / **current**), which is `null` as a whole unless a
  `PaginateLinkContext` was supplied; within it, `previous` / `next` are `null` at the edges, while `current`
  never is — it echoes the requested page, past the end included. **Nulls are serialized, never dropped** —
  `"next": null` is the client's answer to "is there a next page", so no `JsonIgnore` on these; the payload
  shape is identical on every page. `current` is a non-positional init-only member, which is what keeps the
  ctor / `Deconstruct` / `with` shape (and the binary contract) untouched — the pattern for extending these
  records additively.
  **`PaginatedMeta` and `PaginatedResponse<T>` hand-write `Equals`/`GetHashCode`**, because a record's
  synthesized equality runs every field through `EqualityComparer<T>.Default` — reference equality for the
  three collection members and for `Items`, which made two envelopes describing the same page unequal. Two
  consequences when touching them: **a new member has to be added to both methods by hand** (the compiler will
  not warn), and the two rules the implementation rests on must survive — `Filter` keys are matched
  **ordinally**, not through either dictionary's own comparer (the binder's is `OrdinalIgnoreCase`,
  `PaginateQuery.EmptyFilters` is `Ordinal`, and deferring to one of them makes equality *asymmetric*), and
  `FilterHash` combines entries **commutatively**, because a dictionary has no order.
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
- **Composers** (same `extension<TEntity>(IQueryable<TEntity>)` block) — build the query and stop before executing.
  Both return `PaginateComposedQuery<TEntity>`: the composed `Query` plus the effective `Page` / `Limit` /
  `SortBy` / `Search` / `SearchBy` / `Filter`, the same values `PaginatedMeta` carries, from the same resolution.
  - `ApplyPaginateFilters(request, config)` — filters + search only (`Query` is the **match set**, unordered and
    unpaged), for facets / sums / exports. Resolves and validates everything, `sortBy` included: it reports the
    ordering that *would* apply without applying it.
  - `ApplyPagination(request, config)` — the full page query. Same validation; **no** count and **no**
    past-the-end short-circuit, so it describes what would run rather than optimizing it away.
  - **`SortBy` went back to non-nullable in `10.1.0`, and that is F16's doing.** It was nullable because the
    filtered composer skipped sort resolution — `ResolveSorts` could *refuse* a config with nothing to order
    by, which would have rejected a facet count over a request that never wanted an order. Making the
    tie-breaker required at `Build()` deleted that refusal, so both composers now validate identically and
    `[]` unambiguously means "resolved, nothing requested". Don't re-introduce the null.
  - Both composers now also call `ResolveSorts`; only `ApplyPagination` applies the result.
  - All three paths (both composers and `PaginateCoreAsync`) go through one private `Compose`, which is what makes
    "the composed SQL is the executed SQL" true — do not give a composer its own copy of a stage. Read the
    assertion behind that claim precisely before quoting it anywhere: `ComposerTests` compares the composed
    query against the command `PaginateMapAsync` executes, **modulo whitespace**, for one request shape.
    `PaginateMapAsync` is the one entry point that adds no SQL-side projection, which is exactly why it is
    the one the comparison can be made against; the other three replace the `SELECT` list, so their executed
    statement is by construction not what `ApplyPagination(...).Query.ToQueryString()` prints.
- **`PaginateFilterOperator`** — `Eq`, `In`, `Null`, `StartsWith`, `Contains`, `ILike`, `GreaterThan(OrEqual)`,
  `LessThan(OrEqual)`, `Between`. Each field whitelists its allowed operators.
- **DI:** `services.AddPagination(b => { b.AddAspNetCore(); b.UsePostgreSql(); b.UseNodaTime(); });` — add only the
  extensions in play. `AddAspNetCore()` wires query-string binding, the `ProblemDetails` exception filter, and OpenAPI
  metadata.

## Providers — LIKE vs ILIKE
- Default search / `Contains` / `StartsWith` emit **portable `LIKE`**, and its case behaviour is the engine's,
  **not the column collation's**. That shorthand was wrong in the one direction that matters here: a
  deterministic PostgreSQL collation never case-folds `LIKE` and, before 18, a nondeterministic one is
  rejected by it, so the portable path is case-**sensitive** on PostgreSQL whatever collation is configured
  (measured on 15.19: `$ilike:widget` matched neither `Widget` nor `WIDGET`). SQL Server folds by collation,
  SQLite folds ASCII only, and the plain-`IQueryable` leg is `OrdinalIgnoreCase` for the pattern operators
  while `$eq` / `$in` stay ordinal. The single home for the per-leg table is
  `docs/src/reference/query-string/` under `$ilike`; don't restate it elsewhere.
- `.UsePostgreSql()` registers `NpgsqlLikeStrategy` (`src/Janzen.Pagination.PostgreSql/Like/NpgsqlLikeStrategy.cs`)
  **globally**, upgrading those to native **`ILIKE`** (true case-insensitive). The provider-agnostic `PaginateConfig` is
  unchanged — only the emitted SQL differs. The strategy resolves `NpgsqlDbFunctionsExtensions.ILike` through a
  `MethodInfo` lookup, which is what `Expression.Call` needs — **not** a way of avoiding a dependency. The
  project carries a `PackageReference` to the Npgsql provider and the packed nuspec declares it, so that
  coupling is paid in full either way.
- **A per-resource override exists**: `WithLikeStrategy(...)` on a config wins over the process-wide default
  and is resolved per query, which is what makes one process talking to two providers workable.

## Conventions
- **net10.0-only**, `Nullable=enable`, `ImplicitUsings=enable`, C# `latest` ([Directory.Build.props](Directory.Build.props)).
- **CPM** — every package version lives in [Directory.Packages.props](Directory.Packages.props); don't pin versions in a `.csproj`.
- **The tree is LF, pinned by [.gitattributes](.gitattributes)** (`* text=auto eol=lf`; `.bat`/`.cmd` carved out).
  Not cosmetic here: the parameter descriptions this library generates land in a *consumer's* committed OpenAPI
  artefact, and the transformer builds them from raw string literals, which the compiler copies **verbatim** —
  a CRLF checkout ships a package whose documentation is CRLF, and the consumer's artefact then rewrites itself
  on every build. The joins in `PaginatedQueryOperationTransformer` are pinned to `'\n'` for the same reason:
  **don't put `Environment.NewLine` back.** `OpenApiTests.No_description_carries_a_platform_line_ending` guards
  both paths at once, and needs the documented config to keep **two** sortable fields and **two** operators on
  `filter.status` — a one-element `string.Join` emits no separator and the test would pass without testing.
- **XML docs on every public member** — enforced by the build (`CS1591` is *not* suppressed for the packable
  projects). `GenerateDocumentationFile=true`, so the generated `.xml` ships inside the package and drives consumer
  IntelliSense: a wrong summary is worse than a missing one, because it cannot be recalled for that version.
  House style, sampled from the existing members: tabs + LF; single-line `<summary>` up to ~139 rendered columns,
  otherwise `///` + **five** spaces on the body lines. Tags in use: `<summary>`, `<remarks>`, `<param>`,
  `<typeparam>`, `<c>`, `<see cref>`, `<see langword>`, `<paramref>`, `<typeparamref>`, `<b>`, `<inheritdoc />`.
  **Do not** introduce `<returns>`, `<exception>`, `<example>` or `<seealso>`.
- **`<param>` is all-or-nothing per member** — document *every* parameter or none, because partial coverage raises
  CS1573 (and partial type-parameter coverage CS1712), which is an error here. **An optional parameter is not
  exempt** (verified: omitting `Badge = null` fails the build). Ordinary methods normally name their parameters
  with `<paramref>` inside the summary and carry no `<param>`; where a parameter earns its own description one
  is permitted, provided **every** parameter of that member gets one — `Create`, `PaginateFilterOperators.For`
  and `PaginateQuery.WithPage` are the three that do. Positional records are the separate rule below.
- **`<inheritdoc />` is for one thing only:** the `PaginateConfig<TEntity>` members implementing
  `IPaginateConfig`, and nowhere else — an invariant rather than a count, because the number grows with the
  interface and a stale one stops the check working. Their prose lives once on the interface — `docs/src/reference/configuration/` teaches the metadata
  read-back path as `IPaginateConfig meta = provider.GetConfig()`, so the interface is the type a consumer holds for
  them. Don't spread the tag elsewhere, and don't "fix" those members into duplicated prose. Note the compiler copies
  the tag into the `.xml` verbatim rather than expanding it (Roslyn resolves it in quick info), so it only works while
  both declarations stay in the same assembly.
- A **positional record** takes a `<summary>` on the declaration **plus a `<param>` for every positional
  parameter**. The summary alone silences `CS1591` for the record, its constructor and all its properties at
  once — but it emits **no `<member name="P:…">` entry**, so a consumer hovering `meta.TotalPages` sees nothing.
  `<param>` is what fixes that: Roslyn re-emits each one as the synthesized property's own `<summary>`
  (`DocumentationCommentCompiler`, an LDM decision shipped in VS 17.2), and the same edit serves both the packed
  `.xml` and IDE hover. Nested `<c>`, `<see cref>`, `<see langword>` and `<paramref>` survive the copy verbatim.
  Keep the summary's cross-member prose where it is — only `<param>` content reaches a property tooltip, and
  `<remarks>` stays on the type. Two consequences worth knowing: a `cref` to a positional property only resolves
  in the shipped `.xml` once that property has a `<param>`, and once a record carries `<param>` tags, `CS1573`
  turns a later undocumented positional parameter into a build error. **Do not** re-declare a positional property
  in the record body to document it — that suppresses the copy and doubles the declaration.
- **Argument errors eager, request errors faulted.** The four `Paginate*Async` entry points are non-`async`
  `Task`-returning wrappers around one `async` body, which is the BCL's own split: a usage error (`source`,
  `request`, `config`, `selector`, `postMap`, `projector` being `null`) throws at the call, while everything
  the *caller of the API* sent is validated inside and arrives as a faulted task. Don't make an entry point
  `async` — that moves every argument throw into the task and silently breaks a fan-out that builds its tasks
  before awaiting them.
- **Every `await` in the engine carries `ConfigureAwait(false)`**, with the one deliberate exception of
  `PaginateExceptionEndpointFilter.cs`, which is application-level code inside the host's own pipeline. Keep
  the split: a library await never captures a context, so a consumer's `postMap` and `projector` continue on
  a thread-pool thread — which the projections guide now promises.
- Build must stay clean under `-warnaserror` before any commit.
- **Commits:** small and incremental (one logical change each).
- **`master` takes no direct pushes.** A ruleset requires a pull request with **`ci-ok`** green, signed
  commits and linear history, and forbids force-pushing or deleting the branch. So work lands as
  branch → PR → **squash** merge (the only merge method the repo allows), and a mistake already on `master`
  is fixed with a follow-up commit, never with a rewrite. No approving review is required — a solo
  maintainer cannot approve their own PR, so demanding one would wedge the repo. Tags are a separate
  ruleset: `v*` can be created but never moved or deleted.
- **`ci-ok` is the required check, and it aggregates rather than tests anything itself.** `build-test` runs
  as a three-OS matrix, which turns its context into `build-test (ubuntu-latest)` and friends — a ruleset
  pinned to job names would need editing on every matrix change, so it is pinned to this one name instead.
  Consequences worth knowing before touching it: a **`skipped`** dependency counts as *passing* for a
  required check, so never let `ci-ok` itself be skipped by an `if:`; and the ruleset has **no bypass
  actors**, so a red gate for a reason outside the PR (an npm or nuget.org outage) blocks every merge
  including the maintainer's — the only escape is editing the ruleset.
  **A cancelled run is safe, and this was measured rather than assumed:** cancelling a run makes `ci-ok`
  conclude **`cancelled`**, not `skipped`, and `cancelled` is not in the set a required check accepts, so
  `mergeStateStatus` stays `BLOCKED`. That says nothing about an `if:` that skips the job on a *completed*
  run — the rule above is unchanged.
- **Never give the matrix job a static `name:`** to keep its context stable. GitHub does not append matrix
  values to an explicit name, so all three legs would report one context and the last to finish would win —
  a red Windows leg hidden behind a green Linux one.
- **The matrix is ubuntu + windows + macOS on purpose.** The engine's wire contract is invariant-culture
  parsing and its SQL is provider-translated; a Linux-only suite cannot be trusted to have exercised either,
  and the library is consumed overwhelmingly from Windows. Actions minutes are free for public repositories
  on standard runners, so the extra legs cost wall-clock and nothing else.
- Each packable project ships its **own `README.md`** as the NuGet package readme — keep it in sync with behavior.
- **GitHub Actions are pinned to a full commit SHA**, with the version in a trailing comment
  (`uses: actions/checkout@3d3c42e… # v7.0.1`). Never replace a SHA with a tag — see *Intentional decisions*.
  Dependabot bumps the SHA and the comment together; minor and patch flow through, a major is a decision.

## Versioning
The package version's **first component tracks the .NET / EF Core major it targets** — a `10.x` package pairs with
.NET 10 and EF Core 10. This is lockstep versioning, as used by `Npgsql.EntityFrameworkCore.PostgreSQL` and
`Microsoft.Extensions.*`, so the pairing is visible without reading the dependency list.

- Within a line the scheme is **`<.net>.<breaking>.<additive+fixes>`**: the middle component is reserved for the
  library's **own breaking changes** — they ride the framework major whenever possible, and a mid-cycle one bumps
  the middle component with a release-note callout. Everything else — new API surface and bug fixes alike — bumps
  the **third** component (so a release adding builder methods is `10.0.1`, not `10.1.0`).
- **A new .NET major means a new package line** (`11.x`). The engine touches expression trees, `EF.Parameter` and
  `EF.Functions`, so a rebuild against the new EF Core major is needed regardless of the version scheme: a `net10.0`
  assembly loaded against EF Core 11 can fail at runtime. Dependabot opens the `Microsoft.EntityFrameworkCore` major
  PR, which is the reminder; CI then says whether it is a plain retarget or a real port.
- **Older lines are not maintained in parallel.** `10.x` stays available on nuget.org as published; backport only on
  request.
- **No four-part versions.** NuGet drops a zero fourth component (`10.1.0.0` *is* `10.1.0`) and treats `1`, `1.0`,
  `1.0.0` and `1.0.0.0` as equal, so the component count would flicker per release. Three components only.
- Version lives in `<Version>` in [Directory.Build.props](Directory.Build.props) — there is **no MinVer** here.
- **Prereleases** use an `-rc.N` suffix (`10.0.0-rc.1`), dotted like .NET's own. `dotnet add package` skips
  prereleases, so **while an rc is the newest release** the install snippets in the six reader-facing
  surfaces (root `README.md`, `docs/src/index.md`, the four package READMEs) carry a `--prerelease` note. It is
  worded without a version number, so no release inside the rc series has to touch it — but the stable
  release **removes** it from all six, where it would only send readers looking for a prerelease that is
  now older than the default.

## Releasing
`publish.yml` does the publishing, triggered by **`release: published`** and nothing else. The steps a release
needs, in order — most of them are guarded, and the guard fires *after* the tag exists, so get them right first:
1. Bump `<Version>` in `Directory.Build.props` and land it **through a PR** — `master` takes no direct
   pushes, so the tag is cut from the squash-merge commit. The tag must be exactly `v$(Version)`
   (`v10.0.0-rc.1`); `publish.yml` compares them and refuses the publish otherwise, because nuget.org unlists
   but never deletes.
2. **At a stable release only**, move each `PublicAPI.Unshipped.txt` into its `PublicAPI.Shipped.txt`. That is
   what makes a later removal an RS0017 build error. Do **not** do it for an `-rc.N`: an rc-only member promoted
   to *shipped* cannot then be dropped before stable without fighting the analyzer.
3. **`PackageValidationBaselineVersion` is bumped *after* the publish, never in the release PR.** It resolves
   through a `PackageDownload`, so pointing it at a version nuget.org does not serve yet fails **restore** with
   `NU1102: Unable to find package … with version (= x.y.z)` — the release PR's own CI, before any tag exists.
   So the release ships with the baseline still naming the *previous* version, which is also what makes the
   validation meaningful for that build. Once the packages are live, a follow-up PR raises the baseline to the
   version just published and deletes the `CompatibilitySuppressions.xml` entries that existed only against the
   superseded one (regenerate with `dotnet pack -p:ApiCompatGenerateSuppressionFile=true` rather than
   hand-editing). Skip that follow-up and the guard keeps validating against an ever-older surface, and the
   stale suppressions hide the next accidental break behind the same target. An `-rc.N` is not a baseline.
4. Release notes go **on the GitHub release** — there is no changelog file, and `PackageReleaseNotes` points at
   the Releases page.
5. Publishing authenticates by **Trusted Publishing (OIDC)**, so there is no API key anywhere. The policy lives
   on nuget.org under the *owner* (not per package), keyed to repository owner + repo + `publish.yml` + the
   **`nuget` environment**. That last field is optional on nuget.org's side, but it is filled in here on purpose:
   left empty, the policy would trust any run of that workflow, gated or not. Its scope is narrowed to
   `Janzen.Pagination.*`, "push only new package versions" — so a *fifth* package needs the policy widened before
   its first publish. The "pending full activation for 7 days" wait applies to **private** repositories; for a
   public one like this the policy is active immediately.
6. `publish.yml` runs as two jobs. `build` holds no credential and does everything that executes project code —
   the tag-vs-version guard, restore, build, test, the README pin and pack — and hands the packages on as an
   artefact. `publish` declares `environment: nuget`, so the run **stops for a manual approval** (required
   reviewer, and only a `v*` tag may deploy) before it reaches the OIDC exchange; by then the suite is already
   green, which is what the approval is confirming. Approve it under *Review deployments* in the run. Nothing
   reaches nuget.org until then, which is also why a mismatched policy fails at `NuGet login` rather than
   half-way through a push. **The artefact hand-off cannot be dry-run** — `release: published` is the only
   trigger — so the first release after any change to it is its own test; cut that one as an `-rc.N`.
7. The `publish` job records a **build provenance attestation** for every packed file, and that is where it ends:
   **nothing is attached to the GitHub release.** Releases here are *immutable*, so a `gh release upload` step
   fails with `HTTP 422: Cannot upload assets to an immutable release` — learned by trying it during the
   `10.0.0` publish. Don't re-add one. Note what that costs: `gh attestation verify` compares a file digest,
   and the copy nuget.org serves has a different one, because nuget.org adds its own repository signature
   (`.signature.p7s`) to every package it accepts, which rewrites the archive. So the attestation is a
   standing public record that this repo produced those exact bytes, not something a consumer can check
   against a download.
8. A **draft** release publishes nothing. `gh release edit <tag> --draft=false` is what fires the workflow.
   Pushing a tag on its own is inert here — no workflow watches tags.

## Testing
`test/Janzen.Pagination.Tests` (xunit v3) — `dotnet test Janzen.Pagination.slnx -c Release`. Two legs, both in-process,
neither needing Docker:

> **[global.json](global.json) is load-bearing**: it selects the **Microsoft.Testing.Platform** runner for `dotnet test`.
> MTP v2 dropped the VSTest bridge on the .NET 10 SDK, so without that file *every* `dotnet test` here — yours, `ci.yml`
> and the guard inside `publish.yml` — fails with `Testing with VSTest target is no longer supported`. It pins no SDK
> version and is not meant to.

- **SQLite in-memory** — most tests. Real SQL translation, so it is what catches "the expression cannot be translated",
  and it exercises the engine's `UseDatabaseFunctions` path (`EF.Functions.Like`, `EF.Parameter`).
- **Plain `IQueryable`** (`List<T>.AsQueryable()`) — the engine's other branch (`string.IndexOf`, synchronous terminal
  operators). Also the only place date filters can be asserted, see below.
  It is additionally the only leg where `PaginateNullSafeRewriter` runs: a selector crossing a navigation
  (`p => p.Category!.Name`) compiles to a plain dereference here and threw an NRE for a row with no parent,
  where every relational provider LEFT JOINs and answers normally. The rewrite makes the two legs agree,
  **including for `$null`, which matches such a row on both** — a predicate-level guard would have answered
  "no" and quietly disagreed with the database. A value-typed member is lifted to `Nullable<T>` on the way, so
  a caller reads the rewritten expression's type rather than the member's. `NestedPathTests` is the leg's
  parity suite; seven of its nine cases fail if the rewriter is short-circuited.

Two SQLite limits shape what may be asserted there, and **neither is the library's doing** — both reproduce with a
plain `Where` and no engine involved:
- `DateTimeOffset` comparisons do not translate at all, so every date filter lives in `InMemoryTests`.
- Decimals are stored as TEXT and the collation parses them with the **current culture**, so ordering a decimal throws
  outright on a machine whose decimal separator is not a dot. Order and range over `Rank` (an `int`) instead; `Price`
  is only ever tested for equality.

**The whole suite runs serially**, via `parallelizeTestCollections: false` in
`test/Janzen.Pagination.Tests/xunit.runner.json` (copied to the output by an explicit `Content` item). The
engine has three process-wide mutable statics — `PaginateLikeDefaults.Strategy`, `PaginateTypeSupport`'s
registries and `PaginateConfigDefaults.Shared` — and a `[Collection]` **cannot** isolate a test that assigns
one: xunit serialises *within* a collection but runs different collections in parallel, so a class holding a
mutated static still overlaps every other collection. `Shared` is what made that concrete rather than
theoretical, because every `Build()` in the assembly reads it. Verified by a throwaway probe: with
`parallelizeTestCollections: true` a pair of two-collection tests asserting "only one of us is live" fails,
with `false` it passes. The suite is ~2–3s either way. The `[Collection("LikeDefaults")]` /
`[Collection("ConfigDefaults")]` attributes stay as documentation of the hazard, and such tests still restore
the static in `Dispose`. Note `[assembly: CollectionBehavior(DisableTestParallelization = true)]` is **not**
the way to do this here: it is `[Obsolete]` in xunit v3 and `-warnaserror` rejects it, while its replacement
`ParallelizationAttribute` does not exist in 4.0.0.

`PaginateTypeSupport` is process-wide **and append-only** — a registration cannot be undone. Tests that
register anything therefore key it to a type declared in the test file itself, so it can never be reached by
another test.

The test project is named in the core project's `InternalsVisibleTo` list, alongside the two add-on packages.
Use it sparingly — the point is behaviour, not internals — but two invariants have no behaviour to assert
against and are tested directly: `PaginateValueConverter`'s UTC `DateTimeKind` (a `DateTime` compares by
ticks, so the wrong `Kind` changes nothing in memory and nothing in the SQL SQLite emits; it shifts the
instant only on a provider that converts, on a server off UTC — a behavioural test would pass in CI either
way), and `PaginateExpressionUtils.EscapeLikePattern`'s `[` (only SQL Server reads it as a range).

- **PostgreSQL**, in CI. Native `ILIKE` and its `ESCAPE` behaviour need a real server, so they run in a
  dedicated `ci.yml` job against a `postgres:18.6` service container, gated on `JANZEN_TEST_POSTGRES` and
  skipped when it is unset — which is why `dotnet test` is still green locally without one. See
  [SETUP.md](SETUP.md) for running that leg yourself. The job is a `needs:` of `ci-ok` and carries no `if:`
  of its own, for the reason under *Conventions*.

## Intentional decisions — do NOT "fix" these
- **net10.0-only** — net9 is EOL and net8 lacks the EF Core 9+ surface the engine relies on (e.g. `EF.Parameter`).
  Don't re-introduce multi-targeting.
- **Value resolution order is registry → built-ins → `IParsable<TSelf>` → 400**, and the registry going *first* is
  the load-bearing part: consulted last (as it was before 10.0.3) a registration for an already-built-in type was a
  silent no-op, so everyone it affected was someone who tried to override and never found out. Don't move it back
  below the built-in table. The `IParsable` fallback has **no opt-out knob** on purpose — parsing only ever runs for
  a field the consumer declared filterable, so whitelisting a field of type `T` *is* the opt-in.
- **`DateOnly`/`TimeOnly` parse with `TryParseExact` against pinned ISO formats, not `Parse`.** The BCL's `Parse` is
  lossy in opposite directions — it reads `2026-01-03T10:00:00` as a `DateOnly` and drops the time, and reads the
  same string as a `TimeOnly` and drops the date — so a caller asking about one moment would silently match a whole
  day. Same reasoning as the NodaTime `Instant` parser refusing a bare date. Don't "simplify" either back to `Parse`.
- **NodaTime has no ISO-8601 duration pattern** — `DurationPattern.JsonRoundtrip` is the colon form (`2:30:00`)
  despite the name, verified against NodaTime 3.3.3. The ISO leg (`PT2H30M`) therefore goes through `XmlConvert`,
  mirroring how the engine reads a `TimeSpan`. Don't replace it with a NodaTime pattern that does not exist.
- **`[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`** on **every public member that reaches the
  reflective surface** — the four `Paginate*Async` entry points and the two composers, but also the
  configuration builder, `WithPagination<T>()`, the OpenAPI transformer and the NodaTime registration. The
  engine builds expression trees and uses reflection, so it is **not** trim/AOT-safe, and silence on an
  unannotated member is indistinguishable from a declaration that it is safe. `EnableTrimAnalyzer`,
  `EnableAotAnalyzer` and `EnableSingleFileAnalyzer` are on repo-wide in
  [Directory.Build.props](Directory.Build.props) under `-warnaserror`, so a reflective path added without an
  annotation **fails the build** — that is the mechanism, not a side effect. `IsTrimmable` / `IsAotCompatible`
  stay absent: annotating declares the library is not trim-safe, those two would claim it is.
- **Auto-projection maps constructor parameters** (records / positional ctors), **not** settable properties — projection
  DTOs should be records. This is by design, not a bug.
- **`nuget.config` lists nuget.org only** and clears machine sources — deliberate, for reproducible restores. nuget.org
  is both the restore source and the publish target; publishing authenticates via Trusted Publishing (OIDC), so there is
  no API-key secret in the repo.
- **Embedded PDBs, not a `.snupkg`.** `DebugType=embedded` in [Directory.Build.props](Directory.Build.props);
  `IncludeSymbols` / `SymbolPackageFormat` are deliberately gone, and the two cannot coexist anyway — an embedded
  PDB leaves no `.pdb` file for a symbols package to hold. What it buys: stepping into these sources needs nothing
  configured on the consumer's side, no symbol server and no separate download, and it works offline. That is worth
  the ~50–200 KB per package. SourceLink itself is in-box in the .NET SDK — there is no `Microsoft.SourceLink.*`
  reference to add, only `PublishRepositoryUrl` / `EmbedUntrackedSources` / `ContinuousIntegrationBuild`, which are
  already set.
- **`.slnx` + lock files** — enabling `RestorePackagesWithLockFile` on the `.slnx` restore fails with
  `Invalid framework identifier ''`; lock files are intentionally not enabled at the solution level.
- **The assemblies are not strong-named**, and this was decided at `10.0.0` rather than left open. Adding a
  strong name later changes assembly identity, which is a breaking change for every consumer, so it is a
  one-way door that has to be walked through before the first stable release or not at all. Against it:
  `net10.0`-only means no GAC and no binding redirects, and the .NET runtime does not verify strong-name
  signatures. The only cost is `CS8002` on consumers who strong-name their own assemblies. Don't add
  `SignAssembly` to a `10.x` build; a new framework major is the earliest place the question can reopen.
- **The binder carries `limit=-1` through; the *config* decides.** `PaginateQueryParser` parses `limit` with
  `AllowLeadingSign` and accepts `-1` specifically, because it has no configuration and cannot know whether
  this resource opted in. Dropping it there — which is what the original positive-only parser did — makes
  `AllowUnlimited` unreachable over HTTP while OpenAPI advertises it, and the tests miss it because they build
  `PaginateQuery` directly. The gate is unchanged and still `ParseLimit`: without `AllowUnlimited` the value
  gets the ordinary `must be between 1 and N` 400. Everything else negative stays a binder-level 400.
- **`$null` is decided from the field's *declared* type, never the expression's.** The in-memory rewriter
  lifts a value-typed nested member to `Nullable<T>` so it has somewhere to put "absent"; reading that lifted
  type in `BuildNullExpression` would make `$null` match a row with a missing parent in memory and match
  nothing on any relational provider, which answers from the declared type. So a non-nullable field reports
  "no row is null" on both legs, nested or not — and "has no category" is expressed by filtering the nullable
  FK, not the joined key.
- **Navigation stops where the offset guard does.** `meta.totalPages` stays the honest page count, but
  `links.next` / `links.last` / `meta.hasNextPage` are drawn from `NavigablePages`, which clamps to what
  `WithMaxOffset` allows. Otherwise a config hands out a `next` link to a page it then answers with a 400, and
  a client that pages by following links walks into a hard error instead of the end of the collection.
- **`WithTieBreaker` is required at `Build()`, and required outright** — not "a `DefaultSortBy` *or* a
  tie-breaker". The weaker form does not hold: a default-sort field is filtered through `When(...)` while the
  tie-breaker is not, so a config whose only default is disabled for a caller would pass that check and still
  have nothing to order by. Shipped in `10.1.0` as the library's own breaking change, deliberately not held
  for `11.0`. What it deleted: the runtime `400 Pagination requires a deterministic sort order …`, its row on
  `reference/errors/`, and the `keys.Count == 0` guard in `ResolveSorts` — a configuration defect reported as
  a client error, which stayed invisible for as long as every caller happened to send `sortBy`. Note the
  break is **behavioural, not binary**: `dotnet pack` stays silent because the `SortBy` nullability change is
  an annotation, not a signature, so `PublicAPI.Shipped.txt` (RS0017) is the only guard that fires. Don't add
  an opt-out; a knob that disables a correctness guarantee is a knob someone will turn.
- **`limit=-1` is opt-in per resource and its row ceiling is mandatory.** `AllowUnlimited(maxRows)` has no
  argument-less form, and `PaginateConfigDefaults` deliberately cannot carry it: a global "unlimited is fine"
  is a promise about table sizes nobody can make. The engine fetches `maxRows + 1` so "exactly at the ceiling"
  and "over it" are distinguishable, skips the count entirely (the fetched set *is* the count — one query, not
  two), and echoes `itemsPerPage` as the actual row count rather than the requested `-1`. `page` must be 1;
  `-2` and `0` stay `400` with or without the opt-in. Don't add a bare `AllowUnlimited()`.
- **Unknown query parameters are ignored.** The binder reads exactly six inputs (`page`, `limit`, `sortBy`, `search`,
  `searchBy`, `filter.<field>`); anything else (`offset`, `utm_*`, …) is dropped and the request pages normally.
  API-audit tools report this as "invalid value silently accepted" — it is a false positive. Strict binding would
  reject consumers' own tracking parameters, so don't add it. `page` and `limit` themselves are validated → `400`.
- **`null` links are serialized, not omitted.** No `JsonIgnore` on `PaginatedResponse<T>.Links` or on the four
  `PaginatedLinks` members. `"next": null` is a value the client needs — it is how it learns this is the last page —
  and keeping the keys means `links` has the same shape on every page, so a client never has to distinguish "no next
  page" from "this API does not send a next link". Payload-size linters suggest dropping nulls; don't. Nothing here is
  sensitive enough to justify stripping it from a response.
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
2. `dotnet test Janzen.Pagination.slnx -c Release` — green, and **add a case for what you changed**. Behaviour with no
   test is behaviour nothing will notice losing.
3. Touched the public API? Update the affected package `README.md`, the XML docs and `docs/src/guide/` — a public-API
   change is a versioning decision. Run `dotnet pack Janzen.Pagination.slnx -c Release --no-build`: package
   validation compares the packed assembly against the released baseline, so an accidental break surfaces here
   rather than in `publish.yml` after the tag exists. A **deliberate** break is recorded, not silenced by hand —
   `dotnet pack -p:ApiCompatGenerateSuppressionFile=true` writes the project's `CompatibilitySuppressions.xml`,
   and that file then reads as the release's breaking-change inventory.
4. `graphify update .` to refresh the graph.
