# Janzen.Pagination — Development Environment Setup

One-time machine setup needed before `dotnet build` works. This is onboarding for working **on** the library —
usage docs for *consuming* the packages live in [README.md](README.md) and each package's own README.

## Prerequisites

- **.NET 10 SDK** (the repo is `net10.0`-only)
- **git**

No database or extra services are required — the four `Janzen.Pagination.*` projects restore from nuget.org and build
on their own.

## 1. Clone & restore

```powershell
git clone https://github.com/janzen01/efcore.pagination.git
cd efcore.pagination
dotnet restore Janzen.Pagination.slnx
```

Restore pulls from **nuget.org only** ([nuget.config](nuget.config) clears machine-level sources for reproducible
builds), so **no credentials are needed to build the repo**.

## 2. Build

```powershell
dotnet build Janzen.Pagination.slnx -c Release -warnaserror
```

`TreatWarningsAsErrors=true` ([Directory.Build.props](Directory.Build.props)) — warnings fail the build. Keep the tree
warning-clean. Missing XML doc comments (`CS1591`) are the one intentionally-allowed exception. The repo has **no test
project**, so a clean build is the build-time gate.

## 3. Graphify — code knowledge graph (for AI agents)

The repo uses a `graphify` knowledge graph; [CLAUDE.md](CLAUDE.md) routes codebase questions through it first. The graph in [graphify-out/](graphify-out/) is **not committed** (reproducible from source, no API
cost) — generate it yourself after cloning.

### 3.1 Install the CLI

```powershell
winget install astral-sh.uv      # uv (skip if already installed)
uv tool install graphifyy        # graphify CLI
graphify install                 # finish setup — deps, skill, etc.
```

Install reference: <https://github.com/safishamsi/graphify#install>. Verify with `graphify --version`.

### 3.2 Wire up the agent hooks

The hooks make the agent run `graphify query` before grepping/reading raw source:

```powershell
graphify claude install   # Claude Code hooks → .claude/settings.json
```

### 3.3 Keeping the graph current

After modifying code, refresh the graph (AST-only, **no LLM/API cost**):

```powershell
graphify update .
```

The semantic layer needs an AI-provider token, but inside an interactive Claude Code session the host model performs
extraction and no token is required.

## Optional — publishing (maintainers)

Releases are tag-driven. The version's first component tracks the **.NET / EF Core major** the package targets, so
tags look like `v10.1.0` — see *Versioning* in [CLAUDE.md](CLAUDE.md) for the policy.

1. Move the entries from each `src/*/PublicAPI.Unshipped.txt` into the matching `PublicAPI.Shipped.txt`. This is what
   marks the surface as released; `RS0017` then fails the build if a member is later removed.
2. Bump `Version` in [Directory.Build.props](Directory.Build.props) and commit.
3. Tag and publish the release:

```powershell
git tag v10.1.0
gh release create v10.1.0 --generate-notes
```

Publishing a GitHub Release runs [.github/workflows/publish.yml](.github/workflows/publish.yml), which builds, packs,
and pushes the four `Janzen.Pagination.*` packages (+ `snupkg` symbols) to **nuget.org**. Authentication uses
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): the workflow exchanges a GitHub
OIDC token for a short-lived API key, so there is no long-lived secret to store or rotate. Release notes live on the
tag/release — there is no `CHANGELOG` file in the repo.

## Troubleshooting

| Symptom                                                      | Cause                                               | Fix                                                                                    |
|--------------------------------------------------------------|-----------------------------------------------------|----------------------------------------------------------------------------------------|
| `error NETSDK1045: ... does not support targeting .NET 10.0` | .NET 10 SDK not installed (older SDK on PATH)       | install the .NET 10 SDK; confirm with `dotnet --list-sdks`                             |
| build fails with `warning ... treated as error`              | `TreatWarningsAsErrors=true`                        | fix the warning; only missing XML docs (`CS1591`) are exempt                           |
| `restore` → `Invalid framework identifier ''`                | `.slnx` + `RestorePackagesWithLockFile` interaction | don't enable lock files at the `.slnx` level (see CLAUDE.md → *Intentional decisions*) |
