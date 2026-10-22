# Contributing to Agent Graphic Design

Agent Graphic Design (AGD) is experimental and has one maintainer. Issues and draft pull
requests are welcome; the maintainer reviews them as time allows and is the only person who
merges.

AGD is released as 0.x under SemVer, so formats and APIs can still change between minor
versions; vector document format 1 changes stay additive. v0.3.0 (2026-10-01) is the latest
release. It was published before this repository became public, and its assets are not
republished here yet, so build from source (see [README](README.md#quickstart)).

## Before you start

- **Open an issue first** for anything beyond a small fix such as a typo, a broken link or an
  obvious one-line bug. Use the bug report or proposal template, and agree on the problem and
  the smallest useful change before you build it.
- **Never report a vulnerability in an issue or pull request.** Report it privately as
  [SECURITY.md](SECURITY.md) describes.
- Everyone taking part follows the [code of conduct](CODE_OF_CONDUCT.md).

## Pull requests

- **Open the pull request as a draft** and link its issue. Mark it ready for review when CI
  passes and you consider it done. Only the maintainer merges, and may close a pull request
  that does not fit the product's direction, with a reason.
- **One concern per pull request.** Don't stack unrelated work onto an open branch.
- Note user-visible changes under `## Unreleased` in [CHANGELOG.md](CHANGELOG.md), including
  what callers must update for a breaking one. Leave versions alone (`Directory.Build.props`,
  release notes, tags): the maintainer decides when a change is released, and as which version.
- No secrets, credentials, private paths or private project data in code, docs, examples,
  fixtures or tests.
- [CLAUDE.md](CLAUDE.md) and [AGENTS.md](AGENTS.md) carry the same guidance for coding agents.
  You are responsible for an agent-written contribution as for any other.

## What to keep in mind

- **One engine, two surfaces.** The `agd` CLI and the `agd-mcp` server both call
  `IDocumentEngine`; put behavior in Core, not in a surface.
- **Formats are contracts.** Changes to the document format, `schemas/*.schema.json`, the
  native pixel format or the MCP tool surface need a design discussion in an issue first.
  Vector format 1 changes must stay additive.
- **The spec moves with the code.** Product intent lives in [`specs/`](specs/product.md). A
  change that alters what AGD guarantees updates the affected spec file in the same pull
  request.
- Follow the existing code style and patterns, and add tests for new behavior.

## Dependencies

All dependencies must be MIT or an MIT-compatible permissive license; copyleft (GPL, LGPL,
AGPL, MPL) is not accepted ([D-01](specs/decisions/D-01.md)). A new or updated NuGet package
needs:

- the exact version in `Directory.Packages.props` and refreshed `packages.lock.json` files;
- an approval entry with its reviewed license and scopes in
  [`.config/dependencies.json`](.config/dependencies.json), and any required notice in
  [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt) (a package whose own notice file is
  redistributed verbatim, like the SkiaSharp native assets, is declared under `upstreamNotices`
  with its SHA-256);
- a clean `dotnet list FTPlus.AgentGraphicDesign.slnx package --vulnerable --include-transitive`.

Dependabot updates only the SHA-pinned GitHub Actions. NuGet updates stay manual because each
one needs this license approval and a locked-restore refresh.

## Validation

Install the .NET SDK pinned in [`global.json`](global.json); roll-forward is disabled, so no other
SDK version builds this tree. From the repository root:

```sh
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --jsonfile global.json --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
```

The README [Requirements](README.md#requirements) cover Windows and the *compatible .NET SDK was
not found* error. Changing the pin is a pull request of its own that reruns CI, the locked
restore and `scripts/release-bundle.sh --check`. The everyday loop:

```sh
dotnet restore FTPlus.AgentGraphicDesign.slnx --locked-mode
dotnet build   FTPlus.AgentGraphicDesign.slnx --configuration Release --no-restore
dotnet test    FTPlus.AgentGraphicDesign.slnx --configuration Release --no-build
dotnet format  FTPlus.AgentGraphicDesign.slnx --verify-no-changes --no-restore
```

`bash scripts/validate-local.sh` runs every CI gate: the four test suites with coverage, the
combined coverage floor, the Product-Kit checks, the repository-checker self-tests and CI
workflow policy, dependency and vulnerability gates, readiness artifacts, package smoke and
formatting. Package smoke refuses a dirty worktree, so commit first.

The qualified authoring profile is .NET 10 on Linux x64. On other platforms native pixel
storage and the asset-pipeline file probe fail closed, so many tests fail there by design.

**CI.** [`ci.yml`](.github/workflows/ci.yml) runs these gates on GitHub-hosted Ubuntu 24.04
runners for every pull request and every push to main, plus a weekly scheduled run that also
picks up new vulnerability advisories. All of its Ubuntu jobs must pass before a merge. An
informational macOS job runs only on the schedule or a manual dispatch and gates nothing.
Pull-request runs get a read-only token and no secrets, and a run from an outside contributor
waits for the maintainer's approval. Workflows stay on GitHub-hosted runners, pin
every action by full commit SHA with a version comment, and never use `pull_request_target`;
`RepoChecks workflows` enforces this.

## License

AGD is released under the [MIT License](LICENSE). By contributing, you agree that your
contribution is licensed under the same license (inbound = outbound). Keep third-party license
texts and notices intact.

## Releases

Releases are GitHub Release assets built by `scripts/release-bundle.sh` from one clean commit;
there is no public package feed. The maintainer cuts releases. Do not create tags or GitHub
releases, and do not publish packages.
