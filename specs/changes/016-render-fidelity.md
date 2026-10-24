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
- [ ] Independent code/security/tests/ops review; authorized publication and CI.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | Q-02 | raster colors match CSS RGBA across paint/background paths | 1 | PASS: 139 Skia tests, including four new cross-paint/background cases |
| A2 | Q-06 | package source identity remains fixed if branch advances | 1 | PASS: moving-HEAD wrapper fixture; package qualification recorded below |
| A3 | J-01 | native AGD logo create/validate/preview/export journey | 1 | PASS locally in 017-brand-artwork; three treatments and byte-identical reexports |
| A4 | C-04 | independent reviews and exact-head CI | 1 | implementation lenses PASS; tests qualification retains MEDIUM while hosted CI is blocked |

## State

**Step**: verify
**Waiting on**: app token refresh for public branch pushes (403).
**Next**: finish local qualification record, refresh app credential, publish and run exact-head CI.
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
**Review policy**: native AGD reviewers are unavailable in this harness; fresh independent
general sessions applied the named `.prdkit/agents/agd-*-reviewer.md` rubrics, read-only.
Architecture lens not applicable: shared parser repair adds no format, schema or engine API.
One review fix round used; previous change 015's qualification finding remains unwaived.
**Review**: round 1 · 14e0b1df2d9d5406146231ef64dca28e036eb124..4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 1 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 1 · 14e0b1df2d9d5406146231ef64dca28e036eb124..4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8 · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 1 · 14e0b1df2d9d5406146231ef64dca28e036eb124..4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8 · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 1 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 1 · 14e0b1df2d9d5406146231ef64dca28e036eb124..4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8 · agd-test-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 2 · 4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8..3296b8ea6c74779bd7cb2de88bdec0310463bb32 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 2 · 4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8..3296b8ea6c74779bd7cb2de88bdec0310463bb32 · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Review**: round 2 · 4c08bd3f6fe0685fbe306ca5755a94d12a7e75d8..46e4d11d2e07cd778e06207a53d87fc6c6c61b7b · agd-test-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
**Retained blocker**: final independent tests review confirms all local/package/artwork gaps
resolved and no implementation/test gaps; only exact-head hosted CI remains its MEDIUM.
**Review**: round 3 · 3296b8ea6c74779bd7cb2de88bdec0310463bb32..3ddb281451a5deb6e13fb17fcebc67133238a021 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/016-render-fidelity/reviews-summary.md
