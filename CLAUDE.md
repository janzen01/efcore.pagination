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
- **`docs.yml` also runs build-only on `pull_request`.** That gate is the whole reason the verifiers below
  are worth having: `ci.yml` never touches `docs/` and `master` has no required checks, so without it a dead
  link merges green. It is also the only way to test the workflow at all, since `workflow_dispatch` needs the
  file to exist on the default branch.
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
  every site URL they advertise to exist too, because those are what the *next* release freezes. So repointing
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
- **Navigation lives in `config.mts`**, not in front matter. The pages carry no front matter at all; VitePress
  takes the title from the first `#` heading. A new page has to be added to the sidebar by hand, or it is
  reachable only by link and search.
- **`.gitignore` keeps `docs/*` deny-by-default** and re-includes the project by name (`!docs/src/`,
  `!docs/.vitepress/`, `!docs/scripts/`, `!docs/package.json`, `!docs/pnpm-lock.yaml`). A new directory that is
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
  `.WithGuards(…)`, `.Sortable(name, expr)`, `.DefaultSortBy(…)`, `.WithTieBreaker(expr)` (unique key appended as the
  final order → deterministic paging), `.Searchable(name, expr)`, `.IgnoreSearchByInQueryParam()`,
  `.Filterable(name, expr, ops…)`, `.FilterableMany(name, coll, expr, ops…)` (matches any element → `Any(...)`), plus
  `.ShowBadge(name, cssClass?)` / `.When(bool)` on the field declared immediately before. Often exposed via an
  `IPaginateConfigProvider<T>`.
- **`PaginateQuery`** — immutable request: `Page`, `Limit`, `SortBy` (`["field:DESC"]`), `Search`, `SearchBy`, `Filters`
  (`field → ["$op:value"]`), plus `.WithPage(n)` — the same request on another page, which is how a caller with no
  `PaginateLinkContext` (so a `null` `Links`) navigates off `Meta`. It is a `class`, not a `record`: value equality over
  the collection properties would compare by reference and lie, so there is no `with`. In ASP.NET Core it binds from
  `?page=&limit=&sortBy=&search=&filter.<field>=$op:value`.
- **`PaginatedResponse<T>`** — envelope: `Items`, `Meta` (totalItems / itemCount / itemsPerPage / totalPages /
  currentPage), `Links` (first / previous / next / last / **current**), which is `null` as a whole unless a
  `PaginateLinkContext` was supplied; within it, `previous` / `next` are `null` at the edges, while `current`
  never is — it echoes the requested page, past the end included. **Nulls are serialized, never dropped** —
  `"next": null` is the client's answer to "is there a next page", so no `JsonIgnore` on these; the payload
  shape is identical on every page. `current` is a non-positional init-only member, which is what keeps the
  ctor / `Deconstruct` / `with` shape (and the binary contract) untouched — the pattern for extending these
  records additively.
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
- **XML docs on every public member** — enforced by the build (`CS1591` is *not* suppressed for the packable
  projects). `GenerateDocumentationFile=true`, so the generated `.xml` ships inside the package and drives consumer
  IntelliSense: a wrong summary is worse than a missing one, because it cannot be recalled for that version.
  House style, sampled from the existing members: tabs + CRLF; single-line `<summary>` up to ~139 rendered columns,
  otherwise `///` + **five** spaces on the body lines. Tags in use: `<summary>`, `<remarks>`, `<param>`,
  `<typeparam>`, `<c>`, `<see cref>`, `<see langword>`, `<paramref>`, `<typeparamref>`, `<b>`, `<inheritdoc />`.
  **Do not** introduce `<returns>`, `<exception>`, `<example>` or `<seealso>`.
- **`<param>` is all-or-nothing per member** — document *every* parameter or none, because partial coverage raises
  CS1573 (and partial type-parameter coverage CS1712), which is an error here. **An optional parameter is not
  exempt** (verified: omitting `Badge = null` fails the build). Ordinary methods therefore name their parameters
  with `<paramref>` inside the summary and carry no `<param>` at all; positional records are the exception below.
- **`<inheritdoc />` is for one thing only:** the eleven `PaginateConfig<TEntity>` members implementing
  `IPaginateConfig`, whose prose lives once on the interface — `docs/src/reference/configuration/` teaches the metadata
  read-back path as `IPaginateConfig meta = provider.GetConfig()`, so the interface is the type a consumer holds for
  them. Don't spread the tag elsewhere, and don't "fix" those eleven into duplicated prose. Note the compiler copies
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
- Build must stay clean under `-warnaserror` before any commit.
- **Commits:** small and incremental (one logical change each).
- **`master` takes no direct pushes.** A ruleset requires a pull request with `build-test` green, signed
  commits and linear history, and forbids force-pushing or deleting the branch. So work lands as
  branch → PR → **squash** merge (the only merge method the repo allows), and a mistake already on `master`
  is fixed with a follow-up commit, never with a rewrite. No approving review is required — a solo
  maintainer cannot approve their own PR, so demanding one would wedge the repo. Tags are a separate
  ruleset: `v*` can be created but never moved or deleted.
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
3. Release notes go **on the GitHub release** — there is no changelog file, and `PackageReleaseNotes` points at
   the Releases page.
4. Publishing authenticates by **Trusted Publishing (OIDC)**, so there is no API key anywhere. The policy lives
   on nuget.org under the *owner* (not per package), keyed to repository owner + repo + `publish.yml` + the
   **`nuget` environment**. That last field is optional on nuget.org's side, but it is filled in here on purpose:
   left empty, the policy would trust any run of that workflow, gated or not. A fresh policy is "pending full
   activation" for 7 days and goes inactive if nothing is published in that window; the first successful publish
   makes it permanent.
5. The job declares `environment: nuget`, so the run **stops for a manual approval** (required reviewer, and only
   a `v*` tag may deploy) before it reaches the OIDC exchange. Approve it under *Review deployments* in the run.
   Nothing reaches nuget.org until then, which is also why a mismatched policy fails at `NuGet login` rather than
   half-way through a push.
6. The same job records a **build provenance attestation** for every packed file, and that is where it ends:
   **nothing is attached to the GitHub release.** Releases here are *immutable*, so a `gh release upload` step
   fails with `HTTP 422: Cannot upload assets to an immutable release` — learned by trying it during the
   `10.0.0` publish. Don't re-add one. Note what that costs: `gh attestation verify` compares a file digest,
   and the copy nuget.org serves has a different one, because nuget.org adds its own repository signature
   (`.signature.p7s`) to every package it accepts, which rewrites the archive. So the attestation is a
   standing public record that this repo produced those exact bytes, not something a consumer can check
   against a download.
7. A **draft** release publishes nothing. `gh release edit <tag> --draft=false` is what fires the workflow.
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

Two SQLite limits shape what may be asserted there, and **neither is the library's doing** — both reproduce with a
plain `Where` and no engine involved:
- `DateTimeOffset` comparisons do not translate at all, so every date filter lives in `InMemoryTests`.
- Decimals are stored as TEXT and the collation parses them with the **current culture**, so ordering a decimal throws
  outright on a machine whose decimal separator is not a dot. Order and range over `Rank` (an `int`) instead; `Price`
  is only ever tested for equality.

`PaginateLikeDefaults.Strategy` is a process-wide mutable static, so tests that swap it sit in the
`[Collection("LikeDefaults")]` non-parallel collection and restore it in `Dispose`.

`PaginateTypeSupport` is process-wide **and append-only** — a registration cannot be undone. Tests that
register anything therefore key it to a type declared in the test file itself, so it can never be reached by
another test.

The test project is named in the core project's `InternalsVisibleTo` list, alongside the two add-on packages.
Use it sparingly — the point is behaviour, not internals — but two invariants have no behaviour to assert
against and are tested directly: `PaginateValueConverter`'s UTC `DateTimeKind` (a `DateTime` compares by
ticks, so the wrong `Kind` changes nothing in memory and nothing in the SQL SQLite emits; it shifts the
instant only on a provider that converts, on a server off UTC — a behavioural test would pass in CI either
way), and `PaginateExpressionUtils.EscapeLikePattern`'s `[` (only SQL Server reads it as a range).

Not covered: native PostgreSQL `ILIKE` and its `ESCAPE` behaviour — that needs a real PostgreSQL server.

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
   change is a versioning decision.
4. `graphify update .` to refresh the graph.
