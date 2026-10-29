# Change: macOS asset file-type inspection

**Opened**: 2026-10-28 · **Parts**: P1, P4 · **Journeys**: J-03, J-04, J-05 · **Tier**: T3 · **Parent**: 008-macos-qualification

## Intent

Continue the owner's macOS advisory repair after merged028 verified paths. Implement accepted
008/T007 (D-28/D2, stage S1): admit asset pipeline and artifact verification as unqualified on
.NET10 osx-arm64, macOS26 or later, using the existing runtime PAL regular-file probe. Both temp
modes now agree at480 failures/8 skips; the next bounded improvement is real asset inspection.
Native pixels/still import remain refused until S2; qualification and release remain separate.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L0 | C-11, Q-05 | owner-approved bounded S1 admission after proof | unqualified labels and all other gates retained |
| L1 | D-28 S1 and progress evidence | implement accepted admission; bounded explicit S0→S1 comparison | no qualified pair added |
| L2 | P1.I-09, P4.B-02 | report/enforce unqualified assetPipeline/artifactVerify on the accepted profile | other profile/capability policies unchanged |
| Change | 008/T007 | record delivery after native proof and final gates | AC-05 still-import success remains with S2 |

## Bounds

- C-01/C-03: reuse the existing .NET10 PAL layout and C# tools; no new dependency/license intake.
- C-04: pure Core registry remains authoritative; every entry-point guard and MCP report agrees.
- C-09: no file format, API or command/tool cutover. Progress tooling explicitly adds one accepted
  stage transition; old durable S0 evidence remains readable, not rewritten or relabelled.
- C-11/Q-05: owner resolved the S1 conflict on2026-10-28 by selecting "Allow S1 (Recommended)":
  only these two capabilities/profile may be admitted after native safety tests and all review/CI
  gates. Labels stay unqualified; no qualification or S2/S3/release/deployment permission follows.
  This is an owner-directed rule amendment, not a waiver of any required check or an agent exception.
- Q-01/Q-02: no output/sample tolerance changes or broader support/qualification claims.
- Q-03/Q-04: existing inspection-before-open, fixed bounds, cancellation, no partial writes and
  verified aliases hold. FIFO/device/socket/link leaves must be refused without opening them.
- C-10: no personal credentials, runtime network access or user-controlled native metadata targets.
- Others in scope: hold.

## Approach

Base `bb035efd9fe69013904af612fa73cd3cff04b2ce`, merged028/PR21. Canonical-path dependency is
delivered, with accepted indexed sourcee62b4dc, complete3562-case records per mode and final-head
equivalence. Retain028's failed first comparison and earlier026/027 evidence; no attempt reset.

### Owner-approved bounded D-28 S1 implementation

Move only the macOS26+ arm64 .NET10 registry row from `[U,U,R,R,R,R]` to `[U,U,U,U,R,R]`.
Regenerate and review the corresponding synthetic fixture. `AssetPaths.Inspect` already consumes
`UnixFileMetadata.SystemNative_LStat`; its platform-neutral Mode distinguishes regular files
from FIFO/socket/device leaves without opening them. Preserve all gates, exact refusal text,
canonical path flow and link checks. Do not add an OS-specific stat layout or general link bypass.
S1 also requires managed-output normalization-alias protection: macOS FormD comparison keys for
planned collisions/overlap, bounded exact stored-component checks for existing paths, and native
NFC/NFD source-preservation tests before publication. Actual IO retains original canonical spelling.

The newly admitted reference/font prepasses follow the same registry, so their existing explicit
inspection skips become executed coverage, including portable device/FIFO fixtures and a stable
bare FIFO wrapper retaining its original three scenarios and argument-qualified Linux identities.
A failed newly executed case is a real blocker,
not grounds to retain the skip or loosen the assertion. Asset output publication stays on its
existing managed path; native pixel storage and import target admission remain refused.

### Truthful progress stage — integration design for review

The current progress parser and writer hardcode S0, and comparison requires identical stages.
Do not mislabel S1 runs or disable non-regression. Keep progress formatVersion1, admit exactly S0
and S1 stage values, and classify the exact canonical macOS26 arm64 fixture's six capability
policies: S0=`[U,U,R,R,R,R]`, S1=`[U,U,U,U,R,R]`. Reject unknown, duplicate/missing capabilities,
nonempty requires or any qualified macOS capability. This classifier is tool-only, outside the
I/O-free Core registry, and is not a second admission decision.

New runs capture exact-head locked builds, actual executed checker/Core/test-assembly identities,
stable hashes and `platform-fixtures --check`; header stage must match the captured source fixture.
Historic S0 predecessors remain readable without fabricated captures. Four-suite discovery/TRX,
clean-source brackets, bounded logs/exits and source/fixture/binary mismatch negatives are mandatory.
Comparison binds stages to committed live-base/head fixtures; only explicit `--stage-transition
S0:S1` permits that forward transition. Reverse/unknown/same-stage-with-flag/header-only changes
fail closed. All original identity/failure/skip/toolchain/current-base checks, manual durable
replacement and final-head equivalence remain. No generic ignore-stage or automatic reset.

### Proof scope

Restated008/AC-05 portion: regular-file asset build/check, lint and artifact preflight/verify
complete on macOS with Linux-equivalent envelopes. RestatedAC-06: FIFO, socket, device, symlink
and dangling components fail PATH_ESCAPE before leaf open, and FIFO cannot block. Cover input,
manifest, output and inventory paths through Core and representative CLI/real MCP wire.
No privileged device creation: fixed devices are metadata-only. Call-local Core IO trap seams
prove zero unsafe-leaf opens, with sentinel controls. Killable workers with deadlines/tree cleanup
bound real Core/CLI/MCP special-file cases; snapshot ordinary bytes/mtimes/entries without reading
special fixtures. Detailed corrective design and finding map are retained on PR22 and in ignored
evidence `029-asset-inspection/design/integration-02.md` (S-01, T-01/T-02, P-01).

Keep every accepted case identity and outcome unless it improves; no new skip/rename exception.
Check unsupported OS/runtime/process-architecture tuples still refuse before writes and agree
with reported capabilities. Staged still-import/native-pixel success and zero-failure macOS
qualification remain parent008 obligations; do not count their refusal as editing success.

## Tasks

- [x] T001 Record owner reconciliation of C-11 versus D-28 S1; complete independent design review.
- [x] T002 Registry/fixture admission, file-probe native cases and bounded progress-stage migration.
- [x] T003 Complete Linux and two-mode native comparison; source/archive/installed proof and durable baseline.
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
| A1 | D-28 S1, P1.I-09/P4.B-02 | registry tuples/fixtures, host agreement and unchanged refusals; native40757367407 at8e75a1c | 3 | PASS |
| A2 | AC-05 asset portion, AC-06 | real native safety and source/archive3625PASS; installedCLI66/MCP40 plus8special-file cases at8e75a1c | 3 | PASS |
| A3 | Q-01/Q-03/Q-05 progress integrity | stage negatives; two-mode current-base comparison and durable retrieval in PR22comment6479083236 | 3 | PASS |
| A4 | source delivery | closing40848620385 comparison FAIL on existing timing-sensitive cancellation case; deterministic correction/review pending | 4 | FAIL |

## State

**Step**: verification repair — closing cancellation race needs deterministic coverage
**Authority**: owner's2026-10-28 macOS repair request and standing feature commit/push/PR/reviewed-
green source-merge policy; AGD extended verification budget inherited. AB019 profiling remains
held/exhausted2/2; Dossier015 stays in its other session. No release/deployment authorization.
**Baseline**: merged028 atbb035ef, acceptedS0 sourcee62b4dc:3074PASS/480FAIL/8SKIP per mode.
Attempt1 at5443648/run40754002135 completed both3624-case modes:3403PASS/221FAIL/0SKIP default,
3408PASS/216FAIL/0SKIP private. All8former skips execute/pass; default has1new host-gate failure
(direct probe got raw/var), plus4old canonical-expectation failures private already fixes. Full
comparison FAIL; original records/artifacts retained. Core-onlyCI lacks CLI/MCP build prerequisites.
**Owner decision**: 2026-10-28 conversation, question "May S1 enable assetPipeline and artifactVerify
on .NET 10 Apple-silicon macOS 26+ after native safety tests and all review/CI gates pass, while still
reporting them as unqualified?" Answer: "Allow S1 (Recommended)". This resolves the conflict raised
atac25d14/PR22, for exactly that scope. C-11 and D-28 now incorporate it; the answered inbox item
is removed. No review, runtime proof or later-stage authorization is implied by the answer.
**Design round1**: architecture/ops PASS0; security1HIGH (APFS normalization aliases), tests2MEDIUM
(eight stable identities and observable no-open proof). Reports retained verbatim onPR22. Corrective
design adds alias protection, Darwin fixtures/stable wrapper, IO traps/killable workers and explicit
build/hash captures. All four scoped round2 design reviews PASS0; source/native acceptance remains open.
**Checkpoint**: S1 registry/fixture, bounded alias policy, observable leaf IO, stable FIFO identity,
killable Core/CLI/MCP special-file workers and source/binary-bound stage tooling implemented. Full
Linux3624PASS/0FAIL/0SKIP (Core2753, Skia154, CLI357, MCP360); stage/capture/CLI/self-tests PASS.
Later deadline/oracle refinements receive focused rechecks. Original failed build/fixture attempts
are retained. Bare FIFO discovery plus three original argument cases are preserved; raw-format
refusal now follows S1's gate with independent literal registry expectations. No tolerance weakened.
At754d639, code/security/architecture scoped fixes PASS0; fresh Core-only archive2754PASS and
native2/run40755937608 passes both complete3625-case comparisons:3409PASS/216FAIL/0SKIP, identical
bodies,264oldfailuresfixed/mode andall8former skips executed. Current-base comparisonPASS; no index
reset. Package02fails only the new synthetic-mac budget fixture: isolated environment uses shared
/tmp with>4096entries. Pinning unrelated ancestors in its injected enumeration retains realowned
4096/4097entries,256outputs/allassertions; cleared-TMPDIR regressionPASS. Production bounds unchanged.
Q-05's staleS0description now reflects the alreadyapprovedC11scope. Native3/run40757367407 at8e75a1c
reproduces all native2 outcomes/non-source headers and passes actual-base comparison. Package03
passes clean-archive3625tests, installedCLI66/MCP40 and8extra installed special-file worker cases.
Full native records published/retrieved with matching digests atPR22comment6479083236; replacement
index proposed for owning review. No new skip/rename exceptions or tolerance changes. Three remaining
artifact failures are case-insensitive fixture construction, retained with the other216failures in
parent008; none is a newly failing case. Next: all five final reviews and closing-head native/CI gates.
**Final approval**: all five round5 implementation/evidence verdicts PASS0 at75d06a1. On2026-10-29
the owner answered "Confirm S1 (Recommended)" to the final specification at that exact head;
the full question/answer is retained inPR22comment6492788816. Required CI at75d06a1 is green;
native equality on the final metadata head and exact-head review/delivery gates remain pending.
**Closing attempt4**: run40848620385 at45664cf has green required CI but default-mode comparison
FAIL: `NodePageTests.Cancellation_DuringBroadTraversalIsObserved` finished before its1ms timer fired.
Private mode retains indexed outcomes. Complete captures and failed comparison remain preserved;
the setup invocation with an incorrect full SHA refused before inspection and was corrected.
The original100000-node reverse-order scenario and exception assertion remain; an internal,
call-local visit observer coordinates cancellation after128visited nodes, with bounded waits and
an exact stop-count assertion. Public traversal/cancellation checks remain the same. No timer,
skip or tolerance relaxation; this bounded verification repair needs owning re-review and fresh proof.
**Review**: round 1 · bb035efd9fe69013904af612fa73cd3cff04b2ce..22ea474db5ccc96258536f2c0b8a726ad5432aeb · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6477604826
**Review**: round 1 · bb035efd9fe69013904af612fa73cd3cff04b2ce..22ea474db5ccc96258536f2c0b8a726ad5432aeb · agd-security-reviewer · FAIL · 0 critical, 1 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6477605294
**Review**: round 1 · bb035efd9fe69013904af612fa73cd3cff04b2ce..22ea474db5ccc96258536f2c0b8a726ad5432aeb · agd-test-reviewer · FAIL · 0 critical, 0 high, 2 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6477556368
**Review**: round 1 · bb035efd9fe69013904af612fa73cd3cff04b2ce..22ea474db5ccc96258536f2c0b8a726ad5432aeb · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6477538551
**Review**: round 2 · 22ea474db5ccc96258536f2c0b8a726ad5432aeb..ce5462caf220fbaee222443577c92381a5cf7898 · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6477731138
**Review**: round 2 · 22ea474db5ccc96258536f2c0b8a726ad5432aeb..ce5462caf220fbaee222443577c92381a5cf7898 · agd-test-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6477713018
**Review**: round 2 · 22ea474db5ccc96258536f2c0b8a726ad5432aeb..ce5462caf220fbaee222443577c92381a5cf7898 · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6477722702
**Review**: round 2 · 22ea474db5ccc96258536f2c0b8a726ad5432aeb..ce5462caf220fbaee222443577c92381a5cf7898 · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6477751084
**Review**: round 3 · bb035efd9fe69013904af612fa73cd3cff04b2ce..5443648ef836dc1f0a4b239f68c5dfd96c5bd642 · agd-code-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6478592547
**Review**: round 3 · bb035efd9fe69013904af612fa73cd3cff04b2ce..5443648ef836dc1f0a4b239f68c5dfd96c5bd642 · agd-security-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6478592969
**Review**: round 3 · bb035efd9fe69013904af612fa73cd3cff04b2ce..5443648ef836dc1f0a4b239f68c5dfd96c5bd642 · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6478593390
**Review**: round 4 · 5443648ef836dc1f0a4b239f68c5dfd96c5bd642..754d63949e70f683c0a55b59b0885de06231a436 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6478817729
**Review**: round 4 · 5443648ef836dc1f0a4b239f68c5dfd96c5bd642..754d63949e70f683c0a55b59b0885de06231a436 · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6478832517
**Review**: round 4 · 5443648ef836dc1f0a4b239f68c5dfd96c5bd642..754d63949e70f683c0a55b59b0885de06231a436 · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6478832939
**Review**: round 5 · 754d63949e70f683c0a55b59b0885de06231a436..75d06a1bf1d93199a598c99ff79708aa7be39588 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6492786227
**Review**: round 5 · bb035efd9fe69013904af612fa73cd3cff04b2ce..75d06a1bf1d93199a598c99ff79708aa7be39588 · agd-test-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6492786659
**Review**: round 5 · bb035efd9fe69013904af612fa73cd3cff04b2ce..75d06a1bf1d93199a598c99ff79708aa7be39588 · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6492787186
**Review**: round 5 · 754d63949e70f683c0a55b59b0885de06231a436..75d06a1bf1d93199a598c99ff79708aa7be39588 · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6492787761
**Review**: round 5 · 754d63949e70f683c0a55b59b0885de06231a436..75d06a1bf1d93199a598c99ff79708aa7be39588 · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6492788264
**Review**: round 5 · bb035efd9fe69013904af612fa73cd3cff04b2ce..75d06a1bf1d93199a598c99ff79708aa7be39588 · owner · PASS · https://github.com/pevyhlas/agentgraphicdesign/pull/22#issuecomment-6492788816
