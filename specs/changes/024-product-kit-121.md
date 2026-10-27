# Change: Adopt Product-Kit 1.2.1 and reconcile resolved inbox direction

**Opened**: 2026-10-27 · **Parts**: P1, P4 · **Journeys**: J-05 · **Tier**: T2 · **Parent**: none

## Intent

Owner request2026-10-27: "please uptake product kit 1.2.1 for both AGD and AB".
Upgrade AGD's existing vendored copy; AB has its own change021. Reconcile answered inbox entries
into canonical destinations with provenance and remaining work/holds, leaving unresolved questions open.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | operations | vendored1.2.1, source/notice references and open-only inbox adoption | authority/qualification unchanged |

## Bounds

- C-01: pinned MIT developer-tooling source, Product-Kit/Spec Kit attribution retained; no NuGet addition.
- C-03/C-04: Bash workflow maintenance; no application/runtime changes or supported Python recipe.
- C-10: no committed private paths/credentials; backend:none retained, evidence remains ignored.
- C-11/Q-05/Q-06: qualification/release gates remain, including required P0 and advisory broad macOS CI.
- Others in scope: hold.

## Approach

Baselinec900ca90186402b5afadfb70a85da63c22b627f0. Upstream annotated tagv1.2.1 peels to
589589aeae2791d7e509214b9fb82b014bd59f82. Use pinned init --check, then --upgrade --copy;
preserve customized constitution/agents/ledger/projections. Update manifest hash and stale notice
version. Review the automatic template updates; no forced memory overwrite. Follow release guidance
to reconcile resolved inbox items only after preserving their canonical meaning, provenance and holds.

Maintenance uses the upstream approved implementation, not a new product capability. Code,
security/license and tests/verification lenses apply; architecture is not applicable (no format,
engine API, producer seam or MCP surface change). Operations reviews the shipped notice/provenance
wording and gate applicability; product CI/CD, packaging behavior and platform policy do not change.

## Tasks

- [x] T001 Upgrade pinned copy/provenance/notices and prove policy/projections preserved.
- [x] T002 Reconcile answered inbox items with a destination map; preserve remaining domain/platform gates.
- [ ] T003 Run changed-helper regressions and consumer gates, independent required reviews and exact-head
  required CI; finalize on this PR.

## Checks

Verify upstream review-package/ledger/inbox/worktree-numbering regressions and actual consumer
adoption/doctor/status plus revision-bound review dispatch preflight. Reuse upstream tests rather
than adding mirrors. Application source remains unchanged; full required CI remains the merge gate.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | C-01; operations | source589589ae, manifestc26db47e;72 installed files byte-identical, both MIT notices verbatim; install/adoption8groups/policy hashes PASS | 1 | PASS |
| A2 | owner authority; C-10 | destination map, upstream804 assertions, actual round1 package inventory/preflight and7 synchronized race scenarios PASS; fixture failure retained | 1 | PASS |
| A3 | constitution; Q-06 | source1f4cdb1 required9CI jobs PASS; code/security/ops PASS, test review1medium FAIL pending new evidence re-review; final-headCI still required | 1 | FAIL |

## State

**Step**: review preparation
**Waiting on**: scoped verification re-review and final-head required CI
**Review**: round 1 · c900ca90186402b5afadfb70a85da63c22b627f0..1f4cdb13eaa72508d6e9f416d035b998d6393ed2 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/17#issuecomment-6451863652
**Review**: round 1 · c900ca90186402b5afadfb70a85da63c22b627f0..1f4cdb13eaa72508d6e9f416d035b998d6393ed2 · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/17#issuecomment-6451864364
**Review**: round 1 · c900ca90186402b5afadfb70a85da63c22b627f0..1f4cdb13eaa72508d6e9f416d035b998d6393ed2 · agd-test-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/17#issuecomment-6451865335
**Review**: round 1 · c900ca90186402b5afadfb70a85da63c22b627f0..1f4cdb13eaa72508d6e9f416d035b998d6393ed2 · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/17#issuecomment-6451866314
**Authority**: explicit owner uptake request; existing branch/PR/reviewed-green merge policy.
Full macOS qualification remains open with prior failures retained; Windows deviation, native/backend
capability limits and release gates are unchanged. No new capability or qualification allocation.
**Inbox reconciliation**: baseline `c900ca9:specs/inbox.md` preserves source words/dates. The three
review-foundation/builder/input continuation answers map to parent012 Intent/Tasks; registry-first
continuation, hosted macOS, public workflow and kit uptake map to operations and PRs8–16, with008
retaining platform failures/remaining work. Existing deferred-mode/atlas/adapter wording is already
canonical in D-02 and changes013/014. All13 unanswered questions remain; the copy-vs-submodule
question stays open, with its obsolete release reference refreshed rather than answered by an agent.
**Evidence**: `.prdkit/cache/evidence/024-product-kit-121/`: expected pre-upgrade drift, successful
upgrade/check-after, adoption8groups, doctor/status and policy hashes. The shared upstream9suites
pass804 assertions at589589ae; copied completed results identify their AB driving checkout and
byte-identical installed helpers in both consumers. App source/CI/projections/ledger backend remain
unchanged. Doctor retains pre-existing budget/archive/open-question advisories. No qualification is
inferred. Actual round1 dispatch preflight passed byte/hash recomputation and accessible input checks.
**Verification correction1**: test review found absent during-copy/during-observation race proof.
Shared AB021 private verification now synchronizes at real Git hash boundaries with FIFO barriers:
evidence changes before copy and after copy cause refusal with retained incomplete round; changed
manifest yields unknown version/blob with attribution intact. Seven scenarios pass including two
guard-bypass negative controls. Driver/helper hashes and environment are in copied completed
`race-guards/`; `race-evidence.md` retains the first driver's stdin-EOF fixture error (five passed,
barrier timeout) and its bounded repair. No installed helper changed; no application run allocated.
**CI**: source1f4cdb1 required nine jobs PASS in run40570677521, including native P0; broad macOS
advisory FAIL remains disclosed. This does not close008 or retry its outcome. Final metadata head
will still require fresh exact-head CI. Unaffected code/security/ops approvals retain applicability.
**Next**: review the completed race evidence and record delta, then finalize this PR and await exact-head CI.
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->
