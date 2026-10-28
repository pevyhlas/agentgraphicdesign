# Change: measurable macOS portability progress

**Opened**: 2026-10-28 · **Parts**: P1, P4 · **Journeys**: J-03, J-04, J-05 · **Tier**: T2 · **Parent**: 008-macos-qualification

## Intent

The owner asked “lets fix that and what are next steps?” after the macOS advisory failure was
reported. Execute the next accepted008 prerequisite, T003, so subsequent fixes demonstrably
reduce failures without hiding tests. Then continue the prescribed font/path/backend increments.
The latest retained native source run40671621597 has3016PASS/484FAIL/8NotExecuted. Most failures
are expected capability refusals at stageS0, with additional Linux-font, case-sensitive-fixture
and raster-rounding assumptions. Source fixes and qualification remain separate outcomes.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | D-28 | mark T003 progress/rule tooling delivered; hosted workflow adaptation | no admission change |
| L1 | operations | developer checks for portability progress | complete source proof, not qualification |
| Change | 008/T003 | restate recovered criteria and current sequencing | later tasks remain open |

## Bounds

- C-01, C-03: C# RepoChecks and existing .NET/Bash/Git tooling; no runtime or test dependency.
- C-10: read-only hosted CI, pinned actions, no secrets; evidence from repository tests only.
- C-11, Q-05: stageS0/refusals unchanged; no qualification or support claim from progress.
- Q-01, Q-02: no goldens, rendering tolerance, skip or runtime policy changes in this prerequisite.
- Q-03: bounded record parsing, subprocess deadlines, complete discovery/execution and explicit exits.
- Others in scope: hold.

## Approach

Baseline9b7150bbe8dc9a318c1f47e9f979ab20dac7232c. Consume accepted D-28 and recovered008/T003,
planD4/D7/D11. Earlier self-hosted/no-artifact premises were superseded by019/023; keep the public
hosted jobs, artifacts and coverage topology. Independent operations/test design confirmation
covers this adaptation before implementation.

- Add `macos-progress run` for full Core/Skia/CLI/MCP discovery and execution on a clean exact
  source SHA, in `default-tmpdir` and `private-tmp` modes. Capture OS/CPU/runtime/SDK, root, command
  exits and logs; only completed, discovery-matching TRX can produce a valid progress record.
  Nonzero test exits with complete failed results are recorded honestly; cancellation, timeout,
  absent/duplicate/incomplete suites or mismatched discovery produce no valid record.
- Deterministic TSV records carry a versioned header, source SHA, stage, mode, counts and body
  SHA-256; case rows include suite, identity and outcome, including skipped names. Record parser
  rejects malformed, duplicate, truncated, tampered or oversized inputs and unknown outcomes.
- `macos-progress compare` requires comparable modes/profiles; failing identities/counts may only
  shrink. Prior tests cannot disappear. New skips/OS-guarded returns fail without an explicit
  reviewed reason; intentional renames require a one-to-one mapping with a reason. It reports
  progress separately from zero-failure qualification. No automatic baseline reset.
- Add conservative source-rule inventories for raw full/relative-path APIs and non-enumerated
  FullName use, OS-guarded test returns, and Bash3.2-incompatible constructs. Existing occurrences
  are exact, reasoned migration entries; additions and stale/mutated entries fail. LaterT006,
  T013 andT014 remove entries as implementations land. This is static repository lint, not a
  general C#/Bash semantic verifier; multiline and compound OS guards have negative fixtures.
- Preserve existing hosted workflow-policy checks; require explicit head-SHA checkout, full source
  run, bounded execution, artifacts on failure, read-only permissions and no secret-bearing event
  for the progress job. A dispatch-only hosted progress run establishes both root-mode baselines;
  subsequent008 source PRs must compare head-bound records before merge (D-28/P0).

## Tasks

- [ ] T001 Capture baseline failure classification, recovered scope and design review.
- [ ] T002 Implement bounded progress records/run/compare with adversarial synthetic TRX and
  record tests: shrinking/new/growing failure, missing test/suite, new skip/return, rename,
  timeout, incomplete discovery, checksum/source mismatch.
- [ ] T003 Implement exact migration inventories and source/Bash/workflow checks with self-tests;
  preserve existing platform and qualification checks.
- [ ] T004 Required source checks; native two-mode progress run and completeness review. Existing
  failures remain failures. Publish branch/PR, owning implementation reviews and exact-head gates.

## Checks

Required code, tests, operations, security and architecture reviews: this is tooling that gates
later platform-sensitive changes. No profile is widened. Test-font portability follows in its
own T013a increment; native backend and final qualification remain008.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | D-28/P0, Q-05 | complete head-bound two-mode source progress records, honest existing failures | 0 | NOT_RUN |
| A2 | Q-01/Q-05 | adversarial comparison/completeness/source/hash tests reject hidden regressions | 0 | NOT_RUN |
| A3 | D-28/D4/D7 | path, return and Bash inventories plus hosted-workflow positive/negative tests | 0 | NOT_RUN |
| A4 | source delivery | Linux required CI, independent reviews and exact-head delivery gate | 0 | NOT_RUN |

## State

**Step**: design confirmation
**Branch**: `026-macos-progress`; no PR yet.
**Authority**: owner2026-10-28 request to fix macOS advisory; constitution commit/push/PR/green
reviewed merge. Earlier AGD macOS verification extension remains in scope; no AB native profiling.
**Attempts**: latest484-failure run is retained inherited evidence, not a new repair attempt.
No026 execution yet; parent qualification A1–A7 remain open.
**Next**: design review, T003 tooling and complete native baseline, then T013a portable fonts.
