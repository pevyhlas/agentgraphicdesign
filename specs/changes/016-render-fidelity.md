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

- [ ] Reproduce and fix RGBA raster mismatch; targeted and full Skia tests.
- [ ] Bind package-smoke message to archive provenance; regression self-check.
- [ ] Reconcile delivered roadmap slices 006/010 with public history; advance review workflow.
- [ ] Independent code/security/tests/ops review; authorized publication and CI.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | Q-02 | raster colors match CSS RGBA across paint/background paths | 0 | NOT_RUN |
| A2 | Q-06 | package source identity remains fixed if branch advances | 0 | NOT_RUN |
| A3 | J-01 | native AGD logo create/validate/preview/export journey | 0 | NOT_RUN |
| A4 | C-04 | independent reviews and exact-head CI | 0 | NOT_RUN |

## State

**Step**: build
**Waiting on**: app token refresh for public branch pushes (403).
**Next**: fix confirmed defects, then produce artwork with the tool.
**Authority**: standing commit/feature push/PR/green-reviewed merge; no release/tag/assets upload.
**Base**: 14e0b1d (stacked on 015-public-readiness, public ancestry only).
