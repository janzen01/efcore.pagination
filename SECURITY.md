# Security Policy

## Supported versions

The supported line is `10.x` (.NET 10 / EF Core 10).

Security fixes go to the **newest version line only**. A version's first component tracks the .NET / EF Core
major it targets, so when `11.x` ships, `10.x` stops receiving fixes unless a backport is agreed for a
specific report. A prerelease (`-rc.N`) is superseded by its stable release rather than fixed separately.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Use GitHub's private reporting instead:
[**Report a vulnerability**](https://github.com/janzen01/efcore.pagination/security/advisories/new)
(repository **Security → Advisories**). That opens a private thread visible only to the maintainers.

On that form **only the title and description are required** — everything else can be left blank, so
don't let the metadata fields hold up a report. If you do want to fill them in: the ecosystem is
**NuGet** and the packages are `Janzen.Pagination.EntityFrameworkCore`, `.AspNetCore`, `.PostgreSql`
and `.NodaTime`. A minimal reproduction (the `PaginateConfig`, the request, and what happened) is
worth far more than complete metadata.

Expect an initial response within a few days. If a fix is warranted it ships as a patch
release and is credited in the accompanying GitHub release notes.
