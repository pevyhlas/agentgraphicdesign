# Change: render fidelity and exact package evidence

**Opened**: 2026-10-24 · **Parts**: P1, P4 · **Journeys**: J-01, J-02, J-05 · **Tier**: T2 · **Parent**: 013-brand-workflow-uptake

## Intent

Owner asks to fix public-project issues, continue roadmap and generate logos through AGD.
Fix the known CSS alpha-channel defect recorded in 012 before authoring real artwork. Fix
package smoke's last-line source identity, observed to read a moving HEAD after packaging.

## Spec edits

- P1 rendering: raster literals use Core's CSS RGBA parser, consistent with SVG/validation.
- Operations: package-smoke identity is the captured archive revision, never later HEAD.

## Bounds

- Q-01, Q-02: pin expected pixels for hex shorthand/full and equivalent rgba(), fill/stroke/
  gradients/background; existing supported opaque rendering remains.
- C-03, C-04: use shared .NET parser, no duplicate surface semantics or dependency.
- Q-06: exact source/hash evidence; no tag/release/upload or new platform claims.
- Others in scope: hold.

## Approach

Decode valid document colors through ColorLiteral; retain existing fallback for renderer-only
legacy named colors. Test rendered pixels and equivalence rather than parser implementation.
Read and validate bundle provenance at completion rather than rereading HEAD. A fixture advances
HEAD during the wrapped invocation and verifies the message still identifies the captured source.

## Tasks

- [x] Reproduce and fix RGBA raster mismatch; targeted and full Skia tests.
- [x] Bind package-smoke message to archive provenance; regression self-check.
- [x] Reconcile delivered roadmap slices 006/010 with public history; native artwork in 017.
- [x] Independent code/security/tests/ops review; authorized publication and CI.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | Q-02 | raster colors match CSS RGBA across paint/background paths | 1 | PASS: 139 Skia tests, including four new cross-paint/background cases |
| A2 | Q-06 | package source identity remains fixed if branch advances | 1 | PASS: moving-HEAD wrapper fixture; package qualification recorded below |
| A3 | J-01 | native AGD logo create/validate/preview/export journey | 1 | PASS locally in 017-brand-artwork; three treatments and byte-identical reexports |
| A4 | C-04 | independent reviews and exact-head CI | 1 local; 1 hosted | PASS: all lenses and [run 40114812534](https://github.com/pevyhlas/agentgraphicdesign/actions/runs/40114812534) on 67d748983f08a88273461ccf29e1dbb3997a1dee |

## State

**Step**: close
**Waiting on**: closure-head CI; PR #8 must merge first.
**Next**: close the record, retarget [PR #9](https://github.com/pevyhlas/agentgraphicdesign/pull/9)
to main after #8, verify exact-head CI and the review gate, then merge.
**Authority**: standing commit/feature push/PR/green-reviewed merge; no release/tag/assets upload.
**Base**: 14e0b1d (stacked on 015-public-readiness, public ancestry only).
**Local qualification**: `scripts/validate-local.sh` completed successfully, zero-warning build,
3,316 source tests and 73.84% coverage, dependency/audit/workflow/readiness/adoption/doctor,
public hygiene, identity fixture and formatting gates. Captured archive 3296b8e passed all
3,316 tests again plus 56 installed CLI and 37 installed MCP tests. Package provenance pins
3296b8ea6c74779bd7cb2de88bdec0310463bb32 and SDK 10.0.113; the corrected wrapper reports that
same SHA despite subsequent documentation commits. Evidence: `.prdkit/cache/evidence/016-render-fidelity/validate.log`;
copied package provenance/checksums accompany it. Expected negative installer diagnostics are
in the successful package smoke transcript. No package was published.
**Review policy**: initial independent general sessions applied the named rubrics; native
AGD reviewers independently completed publication delta/evidence reviews.
Architecture lens not applicable: shared parser repair adds no format, schema or engine API.
One implementation review fix round used. Both this and 015's former qualification findings
were independently closed with hosted evidence, without waivers. Initial publication-review
invocations failed on checkout access; the same reviewers resumed in the assigned checkout.
**Hosted evidence**: 3316 source/archive tests, 56 installed CLI and 37 installed MCP tests;
73.85% coverage. All eight required jobs passed; informational macOS skipped. Hosted packages
identify synthetic merge 9c6426ac3e5524110f50504e6400dc0b1a47c5e9, with the same tree as
67d748983f08a88273461ccf29e1dbb3997a1dee. Local hashes above remain revision-scoped.
**Review**: round 1 · 14e0b1df2d9d5406146231ef64dca28e036eb124..4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 1 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 1 · 14e0b1df2d9d5406146231ef64dca28e036eb124..4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8 · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 1 · 14e0b1df2d9d5406146231ef64dca28e036eb124..4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8 · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 1 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 1 · 14e0b1df2d9d5406146231ef64dca28e036eb124..4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8 · agd-test-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 2 · 4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8..3296b8ea6c74779bd7cb2de88bdec0310463bb32 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 2 · 4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8..3296b8ea6c74779bd7cb2de88bdec0310463bb32 · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 2 · 4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8..46e4d11d2e07cd778e06207a53d87fc6c6c61b7b · agd-test-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Historical blocker**: the earlier tests review retained only missing hosted CI; the
publication evidence review below resolves it.
**Review**: round 3 · 3296b8ea6c74779bd7cb2de88bdec0310463bb32..3ddb281451a5deb6e13fb17fcebc67133238a021 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 4 · 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e..67d748983f08a88273461ccf29e1dbb3997a1dee · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/9#issuecomment-6387232738
**Review**: round 4 · 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e..67d748983f08a88273461ccf29e1dbb3997a1dee · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/9#issuecomment-6387232738
**Review**: round 4 · 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e..67d748983f08a88273461ccf29e1dbb3997a1dee · agd-test-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/9#issuecomment-6387232738
**Review**: round 4 · 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e..67d748983f08a88273461ccf29e1dbb3997a1dee · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/9#issuecomment-6387232738
**Review**: round 5 · 67d748983f08a88273461ccf29e1dbb3997a1dee..825b7ab75e1c94895ddd343a8b9151085c31ba1c · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/9#issuecomment-6387255670
