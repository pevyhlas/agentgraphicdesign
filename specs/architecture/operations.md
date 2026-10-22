# Agent Graphic Design — operations

- **Delivery**: branch, pull request, green CI and recorded reviews, then merge (constitution).
  A release is a GitHub Release whose tag names an approved `main` commit; the owner cuts it.
  There is no deployment target and no public package feed (N-06).
- **CI** (`.github/workflows/ci.yml`): locked restore, build, the Core, CLI, MCP and Skia test
  suites, the combined coverage floor, the Product-Kit adoption and doctor checks, dependency,
  vulnerability, repository-checker self-test and format gates, package smoke and readiness
  artifacts, on GitHub-hosted runners with the SDK pinned in `global.json` and every action
  pinned by commit. `RepoChecks workflows` fails CI if a workflow leaves GitHub-hosted runners,
  uses `pull_request_target`, drops the top-level `permissions` block or uses an action that is
  not pinned by full commit SHA with a version comment.
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
- **Local tooling**: `scripts/install-local.sh` and `scripts/uninstall-local.sh` install or remove
  the tools from a local build; `scripts/validate-local.sh` runs the CI gates locally.
- **Contributor telemetry**: `scripts/claude-with-telemetry.sh` is an opt-in launcher for a
  maintainer's own loopback metrics collector, off by default and tested in CI by
  `scripts/test-claude-with-telemetry.sh`; the product itself sends nothing. Guide:
  `scripts/README.md`.
