# Agent Graphic Design — operations

- **Delivery**: branch, pull request, green CI and recorded reviews, then merge (constitution).
  A release is a GitHub Release whose tag names an approved `main` commit; the owner cuts it.
  There is no deployment target and no public package feed (N-06).
- **CI** (`.github/workflows/ci.yml`): locked restore, build, the Core, CLI, MCP and Skia test
  suites, the combined coverage floor, the Product-Kit adoption and doctor checks, dependency,
  vulnerability, repository-checker self-test and format gates, the redacting developer-only
  `scripts/check-public-repo.py` text/file/link guard, package smoke and readiness
  artifacts, on GitHub-hosted runners with the SDK pinned in `global.json` and every action
  pinned by commit. `RepoChecks workflows` fails CI if a workflow leaves GitHub-hosted runners,
  uses `pull_request_target`, drops the top-level `permissions` block or uses an action that is
   not pinned by full commit SHA with a version comment.
- **macOS diagnostics**: every CI event also runs all four source suites on standard hosted
  `macos-26` arm64 with OS/architecture assertions, physical temporary paths and bounded tests.
  The advisory job retains runner/image/SDK identity, console output and TRX for seven days;
  its summary reports the real test outcome. These artifacts are excluded from Linux coverage.
  The hosted runner choice implements the public-CI direction; qualification remains change 008.
- **Release bundle**: `scripts/release-bundle.sh` builds from an isolated `git archive` of one
  clean commit with locked restore and a non-incremental Release build, tests and packs without
  rebuilding, installs and checks the staged packages from a cleared local feed, reproduces the
  golden SVG, and writes both packages, `SHA256SUMS` and a timestamp-free `provenance.json`. It
  never publishes, tags or creates a release (Q-06). `--check` validates its inputs only.
- **Release identity**: `Directory.Build.props` holds the one package version for both tools;
  `global.json` pins the SDK exactly; `--version` reports the package version plus the source
  commit when the build supplies it.
- **Package smoke**: `scripts/package-smoke.sh` packs, inspects the legal files, installs both
  tools from a local feed and runs them; it refuses a dirty worktree.
- **Dependency and notice gates**: `scripts/check-dependencies.sh` and
  `scripts/check-vulnerabilities.sh` enforce D-01 against `.config/dependencies.json`,
  `THIRD-PARTY-NOTICES.txt` and the verbatim upstream `THIRD-PARTY-NOTICES-SKIASHARP.txt`. Code:
  `scripts/RepoChecks/`.
- **Readiness**: `scripts/check-readiness.sh` regenerates and checks the readiness artifacts.
- **Product-Kit**: public v1.2.1 vendored in copy mode, with the upstream revision and manifest
  hash in `.prdkit/memory/config.md`; no kit checkout is needed to build or run CI. Owner request
  2026-10-27: "please uptake product kit 1.2.1 for both AGD and AB"; supersedes the2026-10-26
  "please uptake the latest product kit and then see what are the next steps" request
  (`c900ca9:specs/inbox.md`, Latest Product-Kit and next steps; implemented in PR15).
- **Public-workflow direction**: on2026-10-24 the owner selected "work on the public repos",
  "very succint and to the point readme", public Product-Kit, fixes/roadmap continuation and
  "generate logos for AGD and AB via their respective workflows". Full source:
  `c900ca9:specs/inbox.md`, Public projects, fixes and logos. README, constitution and
  [PRs8–10](../log.md) enact the delivered portions; continuing domains remain changes013/014,
  and artwork adoption/release gates are not inferred. On2026-10-25, "go ahead - we should setup
  github CI for mac os" selected hosted CI (same source, GitHub macOS CI); PR12 implemented it,
  with full platform qualification retained in change008.
  On2026-10-26, "go ahead" selected the registry-first roadmap (same source, Continue after
  Product-Kit uptake); PR16 delivered that increment, with remaining work/failures retained in008.
- **Local tooling**: `scripts/install-local.sh` and `scripts/uninstall-local.sh` install or remove
  the tools from a local build; `scripts/validate-local.sh` runs the CI gates locally.
- **Contributor telemetry**: `scripts/claude-with-telemetry.sh` is an opt-in launcher for a
  maintainer's own loopback metrics collector, off by default and tested in CI by
  `scripts/test-claude-with-telemetry.sh`; the product itself sends nothing. Guide:
  `scripts/README.md`.
