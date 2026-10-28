# Change: macOS asset file-type inspection

**Opened**: 2026-10-28 · **Parts**: P1, P4 · **Journeys**: J-03, J-04, J-05 · **Tier**: T2 · **Parent**: 008-macos-qualification

## Intent

Continue the owner's macOS advisory repair after merged028 verified paths. Implement accepted
008/T007 (D-28/D2, stage S1): admit asset pipeline and artifact verification as unqualified on
.NET10 osx-arm64, macOS26 or later, using the existing runtime PAL regular-file probe. Both temp
modes now agree at480 failures/8 skips; the next bounded improvement is real asset inspection.
Native pixels/still import remain refused until S2; qualification and release remain separate.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | D-28 S1 and progress evidence | implement accepted admission; bounded explicit S0→S1 comparison | no qualified pair added |
| L2 | P1.I-09, P4.B-02 | report/enforce unqualified assetPipeline/artifactVerify on the accepted profile | other profile/capability policies unchanged |
| Change | 008/T007 | record delivery after native proof and final gates | AC-05 still-import success remains with S2 |

## Bounds

- C-01/C-03: reuse the existing .NET10 PAL layout and C# tools; no new dependency/license intake.
- C-04: pure Core registry remains authoritative; every entry-point guard and MCP report agrees.
- C-09: no file format, API or command/tool cutover. Progress tooling explicitly adds one accepted
  stage transition; old durable S0 evidence remains readable, not rewritten or relabelled.
- C-11/Q-05 **conflict, owner decision required**: D-28 specifies S1 unqualified admission, but
  current C-11 requires every unqualified guarded profile to fail closed and explicitly says no
  S1–S3 admission is implied. The historical Apple-silicon qualification direction does not resolve
  these contradictory current statements. No runtime admission is authorized by this plan.
  Proposed S1 would add no qualified pair; Windows deviation stays in its separate inbox item.
- Q-01/Q-02: no output/sample tolerance changes or broader support/qualification claims.
- Q-03/Q-04: existing inspection-before-open, fixed bounds, cancellation, no partial writes and
  verified aliases hold. FIFO/device/socket/link leaves must be refused without opening them.
- C-10: no personal credentials, runtime network access or user-controlled native metadata targets.
- Others in scope: hold.

## Approach

Base `bb035efd9fe69013904af612fa73cd3cff04b2ce`, merged028/PR21. Canonical-path dependency is
delivered, with accepted indexed sourcee62b4dc, complete3562-case records per mode and final-head
equivalence. Retain028's failed first comparison and earlier026/027 evidence; no attempt reset.

### Proposed implementation of D-28 S1, blocked on C-11 reconciliation

Move only the macOS26+ arm64 .NET10 registry row from `[U,U,R,R,R,R]` to `[U,U,U,U,R,R]`.
Regenerate and review the corresponding synthetic fixture. `AssetPaths.Inspect` already consumes
`UnixFileMetadata.SystemNative_LStat`; its platform-neutral Mode distinguishes regular files
from FIFO/socket/device leaves without opening them. Preserve all gates, exact refusal text,
canonical path flow and link checks. Do not add an OS-specific stat layout or general link bypass.

The newly admitted reference/font prepasses follow the same registry, so their existing explicit
inspection skips should become executed coverage. A failed newly executed case is a real blocker,
not grounds to retain the skip or loosen the assertion. Asset output publication stays on its
existing managed path; native pixel storage and import target admission remain refused.

### Truthful progress stage — integration design for review

The current progress parser and writer hardcode S0, and comparison requires identical stages.
Do not mislabel S1 runs or disable non-regression. Keep progress formatVersion1, admit exactly S0
and S1 stage values, and classify the exact canonical macOS26 arm64 fixture's six capability
policies: S0=`[U,U,R,R,R,R]`, S1=`[U,U,U,U,R,R]`. Reject unknown, duplicate/missing capabilities,
nonempty requires or any qualified macOS capability. This classifier is tool-only, outside the
I/O-free Core registry, and is not a second admission decision.

For new runs, require the existing `platform-fixtures ROOT --check` against the built Core
assembly, with bounded command/stdout/stderr/exit capture. Capture the tracked macOS26 fixture
from the checked source and derive the record's Stage from it. `check` validates the captured
fixture and successful fixture check, then demands header-stage agreement. Historic S0 records
used as predecessors remain parseable; they do not acquire fabricated capture files. New-run
verification requires the capture irrespective of stage. Exact clean source checks still bracket
every run, and all four discovery/TRX reconciliations remain mandatory.

Comparison reads each committed fixture through Git at the authenticated current target base and
exact head; both record stages must match their respective source-bound fixture classification.
Default comparison still requires equal stages. Add explicit CLI option `--stage-transition S0:S1`
for this reviewed transition only: require base fixture S0, head fixture S1 and both mode records
in that direction. Reject reverse, unknown, same-stage-with-flag and header-only transitions.
The option cannot alter case identity, failure, skip, profile, SDK/runtime or current-base checks.
No persistent exception or automatic baseline reset is introduced. Manual durable replacement
and actual closing-head equivalence remain mandatory; once the accepted target base is S1,
ordinary same-stage comparison resumes without a transition flag.

Reject alternatives: S0-labelled S1 evidence, a general ignore-stage switch, an automatic baseline
reset, embedding file IO in PlatformProfile, or admitting native pixels before their backend.
Architecture/security review must approve this tooling integration before implementation.

### Proof scope

Restated008/AC-05 portion: regular-file asset build/check, lint and artifact preflight/verify
complete on macOS with Linux-equivalent envelopes. RestatedAC-06: FIFO, socket, device, symlink
and dangling components fail PATH_ESCAPE before leaf open, and FIFO cannot block. Cover input,
manifest, output and inventory paths through Core and representative CLI/real MCP wire.
No privileged device creation is required: use an existing fixed device read-only for type
inspection and owned temporary paths for all writes. Preserve worker deadlines and cleanup.

Keep every accepted case identity and outcome unless it improves; no new skip/rename exception.
Check unsupported OS/runtime/process-architecture tuples still refuse before writes and agree
with reported capabilities. Staged still-import/native-pixel success and zero-failure macOS
qualification remain parent008 obligations; do not count their refusal as editing success.

## Tasks

- [ ] T001 Owner reconciliation of C-11 versus D-28 S1; then independent design integration review.
- [ ] T002 Registry/fixture admission, file-probe native cases and bounded progress-stage migration.
- [ ] T003 Complete Linux and two-mode native comparison; source/archive/installed proof and durable baseline.
- [ ] T004 All five owning reviews, final exact-head gates and same-PR source closure.

## Checks

All five implementation review lenses apply. Architecture/security review precedes implementation;
test/operations also assess stage provenance, transition negatives and real native no-open proof.
Run complete source suites, guard/fixture/self-tests, clean-archive/installed checks, all required
CI and both native modes. Compare against the actual committed current-base index, explicitly
authorizing only the reviewed S0→S1 transition. Keep failed attempts. Final metadata-only head
must reproduce accepted case outcomes and non-source headers before source merge.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | D-28 S1, P1.I-09/P4.B-02 | C-11 conflict requires owner disposition before runtime admission | 0 | BLOCKED |
| A2 | AC-05 asset portion, AC-06 | real native regular-file success and special-file no-open refusal across Core/CLI/MCP; source and installed Linux proof | 0 | NOT_RUN |
| A3 | Q-01/Q-03/Q-05 progress integrity | stage classifier/capture/transition negatives; complete two-mode current-base non-regression and durable retrieval | 0 | NOT_RUN |
| A4 | source delivery | owning reviews, final-head native equivalence, required CI and review/delivery gates | 0 | NOT_RUN |

## State

**Step**: NEEDS_DECISION — current C-11 conflicts with D-28 staged admission
**Authority**: owner's2026-10-28 macOS repair request and standing feature commit/push/PR/reviewed-
green source-merge policy; AGD extended verification budget inherited. AB019 profiling remains
held/exhausted2/2; Dossier015 stays in its other session. No release/deployment authorization.
**Baseline**: merged028 atbb035ef, acceptedS0 sourcee62b4dc:3074PASS/480FAIL/8SKIP per mode.
No029 implementation or native attempt yet. Failed predecessor attempts remain on their PRs.
**Conflict**: C-11 (constraints.md:83–99) says guarded unqualified profiles must refuse and no
S1–S3 admission is implied; D-28:69–76 says S1 admits the two asset capabilities as unqualified.
008's historical qualification intent and the broad repair request do not select between these
current rules. No constraint is changed, exception granted or new owner HOLD invented here.
**Next**: publish blocked design checkpoint and resolve the inbox's S1 question with the owner.
Recommended: explicitly permit only D-28 S1 after native file-safety proof and existing review/CI
gates, retaining unqualified labels and separate S2/qualification/release gates. Alternatively keep
runtime refusal until full qualification, developing proof through test-only probes in the meantime.
