# Agent Graphic Design constitution

- **Name**: Agent Graphic Design (AGD). Namespace root `FTPlus.AgentGraphicDesign`; repository `agentgraphicdesign`; CLI `agd`, MCP server `agd-mcp`.
- **Intent owner**: the project maintainer (FT Plus) — product purpose and direction, licensing,
  public or commercial distribution, privacy and security posture, spending, and irreversible or
  outward-facing actions. Never assign the intent owner implementation tasks.
- **Rigor**: full
- **Lifecycle**: see `specs/product/lifecycle.md`; the maintainer set the status to experimental.
- **Version**: 2.1.0 (Product-Kit 1.1 restructure, 2026-10-22)

`Rigor: full` also makes medium review findings block and requires independent review of every
substantive change; `light` follows the tiers in `.prdkit/workflows/protocol.md`.

Product intent lives in `specs/product.md` and `specs/product/` (L0), `specs/architecture.md` and
`specs/architecture/` (L1) and `specs/parts/` (L2); this file holds authority, commands, review
policy and principles. History questions go to `specs/archive.md`, read on demand only.

## Action policy

Standing maintainer policy:

- **Commit**: agents commit intended work on a feature branch, never directly on `main`.
- **Branch push and pull request**: agents push feature branches and open pull requests.
- **Merge**: agents merge a pull request once its exact head has green required CI, every
  required review verdict is recorded and passes, and `check-review.sh` passes on that head for
  change branches. Never push directly to `main`.
- **Release**: tags, GitHub Releases and asset uploads are maintainer actions or need explicit
  maintainer confirmation per release; scripts never publish (Q-06).
- **Deploy**: there is no deployment target; adding one needs maintainer approval and a
  threat-model review.
- **Spending, licensing, distribution, security posture**: maintainer only (C-01, C-02).

An agent never grants itself more attempts, lifts a hold, or widens scope, investment or limits.
Missing authority is not permission; a generated plan grants none.

## Commands

Run from the repository root. CI runs them on GitHub-hosted `ubuntu-24.04` runners with the SDK
pinned in `global.json` installed by a SHA-pinned `actions/setup-dotnet`; a non-required macOS
job runs only on schedule or manual dispatch and is informational.

```text
dotnet restore FTPlus.AgentGraphicDesign.slnx --locked-mode
dotnet build   FTPlus.AgentGraphicDesign.slnx --configuration Release --no-restore
dotnet test    FTPlus.AgentGraphicDesign.slnx --configuration Release --no-build
dotnet format  FTPlus.AgentGraphicDesign.slnx --verify-no-changes --no-restore
dotnet run --project scripts/RepoChecks/RepoChecks.csproj --configuration Release -- coverage coverage-results 35
dotnet run --project scripts/RepoChecks/RepoChecks.csproj --configuration Release -- self-test
dotnet run --project scripts/RepoChecks/RepoChecks.csproj --configuration Release -- workflows .
bash scripts/check-dependencies.sh          # exact approvals and notices (D-01)
bash scripts/check-vulnerabilities.sh       # structured NuGet audit
bash scripts/package-smoke.sh               # pack, inspect, isolated install, run
bash scripts/check-readiness.sh             # readiness artifacts
bash .prdkit/scripts/bash/check-adoption.sh # Product-Kit adoption contract
bash .prdkit/scripts/bash/doctor.sh         # spec, IDs, records, agents
bash scripts/validate-local.sh              # the CI gates locally
```

## Review and checks

Every substantive change gets independent review by a fresh context that did not implement it,
at the protocol tier or higher. Critical, high **and medium** findings block; low is advisory.
Up to three fix rounds after the first failing review, then an alternatives note and a
maintainer decision. Re-review only changed source, scoped to the fix.

Review lenses (reviewer agents in `.prdkit/memory/agents.md`); a lens may be marked not
applicable only with a one-line rationale in the change record:

| Lens | Required when | Reviewer |
|------|---------------|----------|
| Code | code, scripts, infrastructure or configuration change | agd-code-reviewer |
| Security and license | auth, dependencies, data, file or network boundaries, untrusted input, license graph | agd-security-reviewer |
| Tests and verification | every behavior change | agd-test-reviewer |
| Architecture | document or native formats, `*.schema.json`, Core engine API, export or producer seams, MCP tool surface | agd-architecture-reviewer |
| Release and operations | CI/CD, packaging, release bundle, platform support, deployment | agd-ops-reviewer |

High-risk persistence, input, license or format scopes need the high-tier owning reviewer in
addition to routine code and test review. Non-trivial capabilities are designed as general
features and get an independent design or architecture review before implementation, not ad
hoc plumbing around one consumer's need. Every question to the maintainer carries options,
trade-offs, a recommendation and, when reversible, a default.

Required before merge: all CI jobs (tests for Core, CLI, MCP and Skia; coverage floor; dependency,
vulnerability, format, adoption and doctor gates; package smoke; readiness artifacts) green on the
exact head, plus the recorded review verdicts. A dependency addition needs the security lens
and D-01 verification; a document-format change needs the architecture lens and C-09.
Exceptions are asked of the maintainer through `specs/inbox.md` with the risk and a
recommendation; only a maintainer `WAIVER` in `specs/records.md` grants one.

Always on, whatever the stage: deterministic, reproducible output (Q-01), honest export
fidelity (Q-02), permissive licensing (C-01), self-host (C-02), .NET-first (C-03), one engine
for all surfaces (C-04) and no secrets (C-10). Deferred until the maintainer selects the mode
that needs them (the stage they are due before is an open inbox question): multi-user locking
and authentication, server distribution and a public package feed, durable multi-file
operations (atomic publication, semantic undo, lost-response deduplication) and hosted
availability hardening. An unbuilt full-editor domain is planned scope, not a defect.

Evidence: raw machine output (build, test and validation logs, coverage dumps, review packages,
ledger traces) is never committed; it stays in CI, the pull request or the gitignored
`.prdkit/cache/evidence/`. Commit only scrubbed, human-written summaries without machine paths,
host names or environment details. Visual evidence may be committed only when free of
personal or device detail.

## Principles

- **One authoritative engine**: CLI, MCP and any future UI call the same Core operations;
  no surface implements authoritative edits of its own (C-04, R-01).
- **Determinism and stable identity**: canonical serialization, stable ids, versioned
  evaluation; reproducible output (Q-01).
- **Honest results**: exports preserve supported semantics or report loss (Q-02); checks never
  claim admission, provenance or qualification they did not perform.
- **Explicit contracts over compatibility code**: declared format versions and direct
  cutovers; no legacy migrations, aliases or parallel implementations by default (C-09).
- **Prefer reversible approaches** and mark experimental code clearly.
- Every product constraint (each C- item in `specs/product/constraints.md`) is non-negotiable
  for agents; changing one is a maintainer decision.
