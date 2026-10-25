# Change: Adopt Product-Kit 1.2.0 and refresh roadmap execution readiness

**Opened**: 2026-10-26 · **Parts**: P1, P4 · **Journeys**: J-02, J-05 · **Tier**: T2 · **Parent**: none

<!-- Past its budget, a change splits into changes that each ship on their own (Parent: <id>). -->

## Intent

Owner direction: “please uptake the latest product kit and then see what are the next steps”.
Adopt public Product-Kit 1.2.0, then reassess AGD's next roadmap outcome and enabling dependency.
Existing product bounds, platform qualification, reviews and attempt history retain their force;
the new PR-scoped finalization guidance does not close an unfinished acceptance item.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | operations | record Product-Kit 1.2.0 vendored source and manifest | tooling update, no new architecture decision |
| Change | 006, 007, 010 acceptance | move historical evidence from Result to Check; retain bare PASS tokens | status-helper compatibility; evidence and attempts preserved |

## Bounds

<!-- From `.prdkit/scripts/bash/bounds.sh <id>`: each constraint, non-goal or quality this change
could break, with how it holds; then "Others in scope: hold". A conflict goes to the owner inbox
with both sides, never resolved here. -->

- C-01: public MIT source pinned by commit; verbatim Product-Kit/Spec Kit notices retained.
- C-03/C-04: developer workflow files only; no production dependency, engine or surface change.
- C-10/C-11: no credentials, machine-path evidence or platform-admission change.
- Q-04/Q-06: preserve customized project policy/reviewers, evidence, attempts and owner gates;
  verify installer idempotence, helper regressions, adoption and exact-head CI before merge.
- Others in scope: hold.

## Approach

Baseline: public main `90b98fa04455678fc928ceb6f6a451c41d3400fd`. Observed upstream release
[v1.2.0](https://github.com/pevyhlas/product-kit/releases/tag/v1.2.0), source
dff688c51d1cf2a09d456be7c5135404f2390eef. Use the pinned installer check/upgrade in existing copy
mode; no kit-owned local edits or customized templates were found. Preserve customized memory
and the project-owned agent library/reviewers. Refresh version, manifest hash and provenance;
adopt the upstream planning/execution playbooks and optional templates without adding another
specification layer or changing project acceptance. Retain the existing vendored-only choice.

Review lenses: code, security/license, tests/verification and operations. Architecture is not
applicable: no engine/API, document/native-format or export/producer-seam change.

## Tasks

- [x] T001 Install pinned upstream 1.2.0 and update manifest/version/notices.
- [x] T002 Verify source equality, idempotence, preservation and helper/project checks.
- [ ] T003 Independent code/security/test/operations review and exact-head CI/merge.
- [x] T004 Assess and publish the registry prerequisite and downstream order on the uptake PR. Updating draft #14 to the merged kit follows this PR.

| Work | Outcome / canonical IDs | Design / parts | Stage / disposition | Depends on + required outcome | Completion checks |
|------|-------------------------|----------------|---------------------|-------------------------------|-------------------|
| 008/PR-1 registry port | one platform-capability authority, P1.I-08/09, P4.B-02 | D-28, P1/P4 | required enabling work; next | recover approved T001/T002 definitions and public evidence mapping | tuple goldens, gate agreement, Linux CI and T002 on macOS at the PR head in private-tmp mode (D-28 P0) |
| 021 review inputs, draft #14 | bounded document/font reads,012/T004/T005 | D-26, P1/P4 | required; waits for registry | merged registry gate and independent input design PASS | grammar, special-file refusal, byte/font bounds and colour oracles |
| 012 render sessions | one font load per call,T034/T035 | D-26, P1/Skia | required; follows inputs | inspected font-set contract merged | reuse/disposal and differential raster checks |
| 012 comparison sheets | controlled size/background review | D-26, P1/P4 | required; follows sessions | shared inputs/render session and legacy goldens | matrix/cell-map correctness and CLI/MCP parity |
| 008 full qualification | supported Mac file/raster behavior | D-28, P1/P4 | required gate; later registry stages | backends, corpus and hash-bound qualification evidence | full qualified-profile checks; advisory CI is not qualification |

Main's 012 record still contains stale 020 waiting text; public PR #13 actually merged the builder.
Draft #14 already reconciles that record. Its shared-parent edits stay in that draft rather than
being independently rewritten here. Change006's public-port review evidence remains open.

## Checks

Verify all installed managed bytes against the pinned source and keep customized project files
byte-identical except the reviewed adoption metadata. Use upstream installer/adoption/review/
finalization/status/workflow/safety tests, then local adoption/doctor/status/public hygiene and
required CI. Runtime source did not change, so no new product tests or qualification claim.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | C-01, operations | `source-preservation.json`, `post-upgrade.log`, `adoption.log`: 72 managed files equal pinned source, both MIT notices verbatim | baseline | PASS |
| A2 | Q-04/Q-06 | `idempotence-upgrade.log`, pinned-source regression evidence on companion AgentBlender uptake PR, `doctor.log`, `status.log`, `public-hygiene.log`; policy/reviewers unchanged | baseline | PASS |
| A3 | J-02/J-05 | next-work table checked against main and draft #14; independent reviews and exact-head CI remain required delivery gates | baseline | PASS |

## State

**Step**: uptake verification
**Waiting on**: independent reviews and exact-head CI.
**Authority**: owner's 2026-10-26 uptake request; standing constitution feature commit/push/PR
and reviewed exact-head green merge policy. Releases and platform qualification retain their gates.
**Attempts**: baseline only; repair 0. The same pinned source passed eight upstream regression suites.
**Next**: verify/publish, complete reviews and merge, then refresh draft #14 and report registry
port readiness. Raw evidence: `.prdkit/cache/evidence/022-product-kit-120/`.
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->
