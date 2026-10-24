# Change: original AGD brand artwork

**Opened**: 2026-10-24 · **Parts**: P1, P4 · **Journeys**: J-01, J-02, J-03 · **Tier**: T2 · **Parent**: 013-brand-workflow-uptake

## Intent

Create a proposed identity through AGD's native create/edit/validate/export/contact-sheet
workflow, as the owner requested. Exercise the delivered brand workflow with real artwork.

## Bounds

- C-01: original geometric art, no external assets or fonts added; independent licence review.
- C-03, C-04: author and export through AGD CLI; no bespoke image renderer.
- Q-01, Q-02: retain editable sources and native exports; review target-size previews.
- Others in scope: hold. This is artwork, not a new product capability or a release.

## Approach

Two chamfered opposing brackets around a diamond aperture, mint on deep ink. A coordinated
mono mark and light treatment test the silhouette independently of color. Owner chooses
whether the proposal becomes the permanent identity.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | J-01, Q-02 | CLI create/add/validate/export; no system fonts | 1 | PASS on source 4c08bd3; older installed tool lacked font-policy flag, corrected invocation retained |
| A2 | J-02 | contact sheet plus small-size visual review | 1 | PASS: three treatments, dark silhouette inspected at 32 px; owner choice remains |
| A3 | C-01 | independent original-artwork/provenance review | 0 | NOT_RUN |

## State

**Step**: review
**Waiting on**: public pushes blocked by existing App credential; identity selection is owner acceptance.
**Next**: author native marks, inspect previews, record evidence and checkpoint.
**Authority**: owner artwork request and standing feature commit/push/PR policy; no release.
**Base**: 4c08bd3, stacked on 016-render-fidelity.
**Evidence**: `.prdkit/cache/evidence/017-brand-artwork/`; public artwork and provenance in
`docs/brand/`. Native authoring and export pass; full S11 macOS brand-kit qualification remains open.
