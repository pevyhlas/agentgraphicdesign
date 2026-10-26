# Change: Port the shared stage-S0 platform registry and capability gates

**Opened**: 2026-10-26 · **Parts**: P1, P4 · **Journeys**: J-03, J-04, J-05 · **Tier**: T2 · **Parent**: 008-macos-qualification

<!-- Past its budget, a change splits into changes that each ship on their own (Parent: <id>). -->

## Intent

Owner confirmed “go ahead” after the Product-Kit uptake and next-step assessment. Port approved
008/PR-1 (T001/T002) to public history, enabling child021's bounded review-input reader to use one
capability gate. The original definitions and implementation have been recovered. Existing
qualification remains Linux x64; later macOS backend/corpus/qualification work stays in008.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | D-28 | implement the approved S0 registry and capability report; document public evidence mapping | no S1/S2/S3 admission |
| L2 | P1.I-08/09, P4.B-02 | route guards through one registry and replace legacy report fields | explicit cutover |
| L2 | P1.I-02 | retain vector storage's unguarded, unqualified off-profile behavior | no canonical-path/backend port |

## Bounds

<!-- From `.prdkit/scripts/bash/bounds.sh <id>`: each constraint, non-goal or quality this change
could break, with how it holds; then "Others in scope: hold". A conflict goes to the owner inbox
with both sides, never resolved here. -->

- C-01: project-owned pre-public MIT source only; security/license review of provenance; no new dependencies/assets.
- C-03/C-04: Core owns the I/O-free registry; CLI/MCP report shared results; C# repository checks.
- C-09: direct cutover from `supportedPlatform`/`currentPlatformMatches` to `platform`; no aliases.
- C-10: no private-history identifiers or raw machine evidence in tracked records; public CI stays hosted/read-only.
- C-11/Q-05: S0 only, with Windows arm64/runtime narrowing declared; no Mac qualification claim.
- Q-01/Q-04: deterministic tuple goldens and no-write refusal checks; no file-backend changes.
- Others in scope: hold.

## Approach

Baseline public main `3e2d3533e6cb9f49e4b0bb7d61e4de480ffd32e5`. Binding design: D-28's
registry, staging table, gate map, capability JSON and P0 evidence; parent008/PR-1. Reapply only
the registry increment, integrating current special-file checks and tests rather than replacing
public runtime files with old versions. Original tasks/source remain ignored recovery evidence.

Public-port choices for independent design review before implementation:

1. Preserve D-28's six-capability S0 table and three qualification states. `Current` uses process
   architecture, runtime and OS versions, with no filesystem/probe/evidence-file access. The
   narrowing-only test seam remains internal. Availability vocabulary/interface is included;
   the live implementation remains008/T019.
2. Require an explicit capability at every asset inspection/inventory caller. Preserve the
   registry's refusal text through preflight, storage and lock-path wrappers. Windows x64 keeps
   asset/preflight unqualified admission; native pixels refuse at the capability gate. Windows
   arm64 and non-.NET10 guarded paths narrow as declared by D-28; vector/raster remain unqualified.
3. Keep the current public CI topology. Historical T000's self-hosted/artifact-quota premise is
   superseded by hosted CI; no collapse of the current suites/artifacts. Add the claim check and
   test-seam/gate rules to existing repository gates with full-history checkout.
4. Map six linux-x64 `pre-d10` entries to scrubbed human-written attestations in `specs/records.md`
   under the **public evidence equivalence** table below. These are fresh public observations,
   never reconstructed historical PASS. Raw logs remain on CI/PR/ignored cache. `d10` remains
   refused until008/T015; no squash-binding redesign is implemented here.
5. P0 means **original008/T002**, now mapped to023/T002–T004, with the complete inventory below.
   Run on the exact PR head in macOS `private-tmp` mode: zero failures, skips or not-executed
   results. All four design reviewers accept local execution on a hosted macOS26-arm64 runner
   as satisfying this bounded gate, without a separate owner-workstation requirement. Retain
   the actual process/runtime/OS/SDK identity, physical `/private/tmp` root, command lines and TRX.
   It is a merge-blocking acceptance obligation, not the broad advisory lane or Mac qualification.

Review concerns: architecture, security/license, tests and operations for the public-port design;
all five named concerns for implementation. Parent design approval is context, not fresh approval
for changed evidence plumbing or the resulting public source.

### Public evidence equivalence

All six pairs require independently inspected successful **source-suite and installed-package**
executions at one exact evaluated public commit on .NET10 Linux x64. Installed evidence identifies
both package versions, SHA-256 values, informational source identities and isolated installed
shims. A generic CI badge, installation-only smoke or unavailable0.3.0 packages are insufficient.

| Capability | Required observations / public check equivalents |
|---|---|
| vector | source Core/CLI/MCP vector regressions plus installed strict validation and byte comparison of `basic-shapes.svg` to its reviewed golden in `verify-packages.sh` |
| rasterExport | source Skia/Core render/export regressions plus installed manifest PNG export, repeated build/check and clean byte drift result |
| assetPipeline | source pipeline inventory/budget/no-write/fault tests plus installed SVG/PNG/sprite manifest build and read-only check |
| artifactVerify | source preflight/decoded verification invalid-input and no-write suites plus installed `AssetsPreflightCommandTests`, `AssetsVerifyCommandTests`, real MCP preflight/verify cold-start/read-only/storeless tests |
| nativePixels | source independent fixed-point pixel/mask/export/storage regressions plus installed guarded edit/advance and export/check tests and MCP export/check/drift-after-edit journey |
| stillImport | all four original005 criteria below, separately evidenced; it cannot inherit only the five-capability package-smoke result |

Still-import equivalence is explicit:

| Original criterion | Public equivalent required before its attestation passes |
|---|---|
| 005/AC-01 | `NativeStillImportTests` independent literal samples and two-layer mask/native composition; CLI `PixelsCommandTests.StillImport_*` and MCP `ProtocolTests.StillImport_*` import/inspect/edit/preview/export/check, non-square/thin/hidden RGB, sibling preservation and native Q24 expected differences, in source and installed tools |
| 005/AC-02 | the same suites' exact descriptor/PNG/native frame/record bytes, supplier mutation/deletion, caller-operated Git/file restoration, restarted-server restoration and restored snapshot eligibility |
| 005/AC-03 | Core import/decoder boundary suites, `NativeStillImportConcurrencyTests`, and CLI/MCP negative cases: original-byte duplicate/unknown/type/token/limits, hash/CRC/DEFLATE/inventory/path/link/FIFO/profile/canvas/conflict/capacity/busy, actual access failures, staging ownership, separate-process create/append and real-held-lock cancellation/recovery |
| 005/AC-04 | this public port's approved pre-runtime design; locked build/full source and exact installed suites; coverage, dependency/audit/self-test/format/adoption/doctor; five independent final owning-domain reviews explicitly covering these fresh observations and preservation of the import boundary; exact-head required CI |

An attestation is an index, not independent proof. Each has a unique id binding **in one selected
record** profile, capability, exact evaluated commit, result, evaluator and source/installed/review
evidence links. Still import separately names AC-01–04 outcomes. The checker validates the selected
record's fields and whole-line result; another record's SHA/PASS cannot satisfy it. Unrelated
historical FAIL records do not invalidate a later passing selected record. Required negatives:
swapped pair, SHA from another record, missing/duplicate/extra pair, malformed/path/unknown kind,
unanchored or unmatched claim, FAIL result with PASS prose, `d10` or non-Linux `pre-d10`. Before A3
passes, reviewers inspect linked executions' actual revision/environment/selection/results against
the table. Missing equivalent evidence requires fresh execution, never an invented PASS or weakened
qualification claim. The final record-only commit may cite its evaluated implementation parent;
review verifies only attestation bookkeeping changed and CI reruns on the final head.

### Original008/T002 P0 inventory

Commit an explicit `.config/platform-p0-tests.json` inventory and project/filter selection, with
method identities and expected theory-case counts; retain discovered and executed identities with
TRX. Dedicated new platform classes are selected whole; partial shared classes select exact methods.
The minimum inventory is:

| Project | Selected classes / methods |
|---|---|
| Core.Tests | all `PlatformRegistryTests` (tuple goldens, literal table, host identity/OS floor, invariants/evidence pairs, vocabulary and narrowing), `PlatformArchitectureTests` (no-I/O/source-set/no-PInvoke), `PlatformGateAgreementTests.HostGates_AgreeWithTheRegistry`, all `PlatformEntryPointRefusalTests` (build/check, preflight/verify, create/append import, create/edit/export native), `AssetPipelineTests.PlatformGuard_FollowsTheRegistryGate` |
| Cli.Tests | all `PlatformGateCommandTests` (real assets preflight and assets check subprocesses) |
| Mcp.Tests | `ProtocolTests.GetCapabilities_PlatformMemberEqualsTheRegistryAndReplacesTheOldFields`, `ProtocolTests.PreflightArtifact_HostGateAndRefusalTextAgreeWithTheRegistry`, `ProtocolTests.ToolResultFail_PassesEveryRegistryRefusalTextThroughUnchanged` |
| RepoChecks | `self-test` including qualification-evidence negative/positive fixtures; `qualification-evidence .`, `platform-rules .` and fixture check command on the same checked-out head |

Verify every inventory method/class is discovered, every expected theory case executes, each test
project has nonzero results, no expected identity is missing and no selected result fails/skips/is
not-executed. Repository checks are separately required nonzero-on-failure commands, not TRX tests.
Reject missing results, cancellation or timeout. Verify checked-out SHA equals PR head, runtime10,
macOS26/arm64 and the actual inherited physical temp root beneath `/private/tmp`. Inventory changes
are reviewed with source; they cannot silently reduce the retained scope. The same explicit CLI/MCP
method filters are added to installed-package verification; source-only surface tests are insufficient.

## Tasks

- [ ] T000 Public design review settles evidence mapping and P0 execution without relaxing its outcome.
- [ ] T001 Port `Core/Platform`: capability/qualification/policy types, profile factory/current,
  S0 registry, ordinal JSON projection, qualified profiles, narrowing-only seam and availability
  interface/vocabulary. Require capability arguments across pipeline/lint/preflight/import/pixel
  inspection and lock gates; preserve refusal messages; replace legacy MCP fields. No live probe.
- [ ] T002 Port synthetic tuple goldens (Linux x64/arm64; macOS arm64 above/below26 and Rosetta;
  Windows x64/arm64; runtime9), literal S0 table and invariants. Test host-current identity,
  OS-floor argument, stable vocabulary, qualified-pair evidence equality and no-I/O architecture.
- [ ] T003 Port explicit fixture regeneration/check command, plain unskipped host-gate agreement
  fact and source-seam rule. Add forced per-capability refusal checks at pipeline build/check,
  preflight/verify, still import and pixel create/edit/export, using ordinary-file fixtures so
  tests execute on refused platforms. Pin MCP projection and CLI refusal text.
- [ ] T004 Public pre-d10 evidence/claim checker and independent negative self-tests: missing
  record, source mismatch, unanchored or unmatched pattern, FAIL result column, failed test line,
  unknown/d10 kind, malformed/duplicate pair and invalid path. Keep all raw evidence outside specs.
- [ ] T005 Update current contracts and cutover docs; complete Linux/installed checks and exact-head
  macOS P0 proof, then all implementation reviews and exact-head CI/review gate before merge.

<!-- Tiny fixes stay inline. Optional detail: .prdkit/cache/implementation/<id>.md;
useful context/progress on the same PR. Product/programme discovery uses workflows/planning.md. -->

## Checks

Use handwritten S0 policies and committed synthetic byte fixtures, not only projection against
itself. Forced refusal snapshots must prove no writes. Preserve current special-file guards and
native tests; no OS-guarded early-return or new skip may substitute for P0 proof. Claim self-tests
must reject false positive PASS matches. Installed CLI/MCP verify the declared report cutover.

## Acceptance

<!-- Required PR obligations. Distinguish later non-gating runtime observations on the PR before
execution; existing required checks cannot be demoted to finish. Live-only claims need live proof.
Keep these five columns for status helpers, including on a parent programme. Result is exactly
NOT_RUN/PASS/FAIL/BLOCKED/NOT_APPLICABLE; evidence/revision/environment go in Check or below the table. -->

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | D-28, P4.B-02; 008/AC-01/02/03 | synthetic tuple bytes/policies, host agreement, MCP cutover and stable vocabulary | 0 | NOT_RUN |
| A2 | P1.I-08/09, Q-04 | all forced capability refusals preserve messages and write nothing; existing guarded paths agree | 0 | NOT_RUN |
| A3 | D-28 evidence; 008/AC-32 | six public pre-d10 pairs, no-I/O registry and negative claim-check self-tests | 0 | NOT_RUN |
| A4 | D-28 P0 | complete original008/T002 inventory above (023/T002–T004) at exact PR head on macOS26-arm64 private-tmp; all expected cases, zero failures/skips/not-executed | 0 | NOT_RUN |
| A5 | Q-06, delivery | required Linux/installed checks, all independent reviews and exact-head required CI/review gate | 0 | NOT_RUN |

## State

**Step**: public port design
**Waiting on**: scoped design re-review of evidence equivalence and complete original008/T002 inventory.
**Authority**: owner's2026-10-26 “go ahead”; constitution permits feature commit/push/PR and
reviewed exact-head green merges. No platform expansion, release or spending authority inferred.
**Attempts**: implementation0, acceptance0, design initial review FAIL (security2 medium, tests1
medium; architecture/operations PASS). Fix round1 defines the evidence equivalents and P0 inventory;
scoped security/test re-review pending. Parent008's existing failures/gates remain;
the unchanged 485 advisory macOS failures are baseline context, not cleared by this child.
**Next**: review public adaptation, then reapply the recovered increment and verify P0.
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->
