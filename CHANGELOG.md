# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). This project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html); while pre-1.0, minor versions may
include breaking changes.

## [0.3.0] - 2026-06-08

### Added
- **Deterministic ordering** — `PaginateConfigBuilder.WithTieBreaker(...)` appends a unique key as the
  final sort key so offset paging is stable even when the primary sort is absent or non-unique.
- **`MaxSearchLength` guard** (default 256) on `WithGuards(...)`, enforced before the query is built.
- **`PaginateConfigBuilder.UseLikeStrategy(...)`** plus a config-level **`UsePostgreSql()`** (in the
  PostgreSql package) so the pattern-match strategy is chosen per resource.
- **Integration test suite** against a real EF Core SQLite provider — exercises the database
  translation path (operators, search, sort, auto-projection incl. nested, `LIKE`/`ILIKE` escaping,
  paging metadata) — plus value-converter unit tests.
- **CI** — `ci.yml` builds with `-warnaserror` and runs tests on push/PR; `publish.yml` now builds and
  tests before packing; `ContinuousIntegrationBuild` enabled under CI.

### Changed
- The LIKE/ILIKE strategy is now carried per `PaginateConfig` instead of a process-wide global; the
  OpenAPI operation transformer reads the per-config strategy.
- `PaginateQueryException` now preserves the inner exception for value parse failures.
- Query-string filter keys are matched case-insensitively, matching config field lookup.
- README samples show the OpenAPI operation transformer registration and the tie-breaker.

### Removed / Breaking
- Removed the global mutable `PaginateLike.Strategy` static.
- `UsePostgreSql()` moved from `IPaginationBuilder` (DI callback) to `PaginateConfigBuilder` (per resource):
  `PaginateConfig<T>.Create(b => b.UsePostgreSql()...)`.
- A configuration that resolves to **no ordering** (no `sortBy`, no `DefaultSortBy`, no tie-breaker) now
  throws `PaginateQueryException` instead of returning a non-deterministic page.

### Security
- Enum filter values must be supplied by name; numeric forms are rejected.
- Cancellation is honored on the synchronous (non-async-provider) execution path.

[0.3.0]: https://github.com/janzen01/efcore.pagination/releases/tag/v0.3.0
