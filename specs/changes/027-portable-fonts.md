# Change: portable font test fixtures

**Opened**: 2026-10-28 · **Parts**: P1, P4 · **Journeys**: J-01, J-03, J-05 · **Tier**: T2 · **Parent**: 008-macos-qualification

## Intent

Continue the owner's2026-10-28 request to fix the macOS advisory. Implement accepted008/T013a,
D-28/D7 RC5 after merged026: four existing tests require Linux-installed DejaVu despite testing
explicit font-directory resolution and Arabic shaping. Pin portable original Noto fixtures and
measure the reduction against026's accepted complete two-mode baseline.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| Change |008/T013a|mark portable test-font task delivered|qualification remains open|
| L1 |D-01 approvals|test-only Noto binaries/license hashes and notice|accepted OFL fixture scope|
| L2 |P1.B-16|fixture coverage only|production behavior unchanged|

## Bounds

- C-01/D-01: original pinned OFL-1.1 fonts, verbatim license/notices and test-only approvals.
- C-03/C-04: C# fixture helper, no new NuGet/runtime dependency or production implementation.
- Q-01/Q-02: original assertions, case names, pixel tolerances and runtime Inter policy retained.
- Q-05/C-11: full discovery, unchanged skips and source-bound non-regression in both modes;
  capability admission remains S0; no support/qualification claim.
- Others in scope: hold.

## Approach

Base82c4d9d5b2877660abf70e7dd84fca03050cd180. Accepted008/D7 RC5 explicitly selects Noto Sans for
width tests and Noto Sans Arabic for RTL/concurrency. Use unmodified hinted/ttf binaries from
notofonts/noto-fonts@ffebf8c1ee449e544955a7e813c54f9b73848eac, licensed OFL-1.1. Copy fonts/license
to both test outputs; a shared test-only helper validates expected hashes before returning the
directory. No runtime download or host font lookup. Concurrent English/Arabic calls select the
explicit corresponding family with strict resolution. Preserve all assertions and case names.
The original width probe `Hello world` differs by only0.5469 units for approved Noto versus Inter;
append16narrow glyphs at the original font size for a measured6.03125unit difference, keeping
the >1.0 assertion. Repeating words alone was insufficient because inter-word advances offset.
Arabic text and64concurrent English/Arabic inputs remain unchanged.

Compare fresh complete head records to the index at the verified current PRbase; no skip/rename
exceptions. Only after comparison/review replace the baseline index with durable PR records of
the improved outcome. Final metadata-only head must reproduce the accepted case/outcome sets.
The inherited baseline has3508cases each: default746fail/private484fail,8skips each. Expected four
fewer failures is a hypothesis until measured, not an acceptance shortcut.

## Tasks

- [x] T001 Adopt pinned original fonts, test-only hash helper, approvals/notices and four fixture edits.
- [ ] T002 Verify Linux, exact native two-mode progress against acceptedbase, durable replacement.
- [ ] T003 Owning reviews, final exact-head gates, source delivery and same-PR closure.

## Checks

Required code, security/license, test and operations reviews. Architecture not applicable: no
production API, format, backend or surface change. Verify font bytes and license against pinned
upstream, no Noto fonts in tool packages, original width/RTL/64-call concurrency assertions,
complete discovery/non-regression with no new skips, and required CI at exact final head.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 |C-01/D-01|pinned original bytes, OFL license, test scopes/notices and package exclusion|0|NOT_RUN|
| A2 |P1.B-16/Q-01|two retained Skia153PASS/1FAIL runs; measured glyph-sensitive probe pending at unchanged threshold|2|FAIL|
| A3 |D-28/P0/Q-05|two-mode complete records, latest-base non-regression and four fixed identities|0|NOT_RUN|
| A4 |source delivery|required CI, owning reviews, final native equivalence and review gate|0|NOT_RUN|

## State

**Step**: implementation of accepted test-portability increment
**Branch**:027-portable-fonts; no PR yet.
**Authority**: same owner request and standing source delivery policy; earlier AGD nativeverification
extension remains scoped, AB019 native hold remains untouched. No release/deployment.
**Evidence**: pinned candidates prepared under026/followup-fonts;027 evidence stays in ignored cache.
Fresh pinned-upstream fetch matches all809664font bytes and4377license bytes exactly; approval,
dependency,45-entry portability,15script syntax, adoption/doctor/hygiene checks passed.
Initial Linux Skia run retains genuine width-probe failure (128.6719 versus128.125); repeated words
also fail (398.4844 versus397.875). Actual C#shaping diagnostic measured narrow-glyph probes before
the third test attempt: appended16`i` yields233.90625 versus227.875. Keep font size/policy/threshold;
this diagnostic is input selection, not acceptance. Both failures retained in linux-01/02.
**Next**: fixture/notice edits and Linux proof, then full native comparison and reviews.
