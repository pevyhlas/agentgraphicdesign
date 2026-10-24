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
| A3 | C-01 | independent original-artwork/provenance review | 1 | PASS: independent security/licence review |

## State

**Step**: close
**Waiting on**: closure-head CI; prerequisite PR #9 must merge first.
**Next**: close this proposal-authoring record, retarget [PR #10](https://github.com/pevyhlas/agentgraphicdesign/pull/10)
after #9, and merge once exact-head CI/review gates pass. Permanent identity selection remains
an owner decision outside this completed proposal-authoring scope.
**Hosted evidence**: [run 40114877993](https://github.com/pevyhlas/agentgraphicdesign/actions/runs/40114877993)
passed all eight required Linux jobs on 16daf2acfce1986269594b123243a4a5164ac3f7;
informational macOS skipped. Subsequent changes close parent records and record evidence only.
**Authority**: owner artwork request and standing feature commit/push/PR policy; no release.
**Base**: 4c08bd3, stacked on 016-render-fidelity.
**Evidence**: `.prdkit/cache/evidence/017-brand-artwork/`; public artwork and provenance in
`docs/brand/`. Native authoring and export pass; full S11 macOS brand-kit qualification remains open.
**Review policy**: fresh independent sessions applied named code/tests and security rubrics.
Architecture and operations lenses are not applicable: artwork adds no engine API, format,
platform, packaging or CI capability. Execution summary retains command/version/exit evidence;
all six SVG/PNG re-exports match committed bytes. No substantive review fix round was needed.
**Review**: round 1 · 3296b8ea6c74779bd7cb2de88bdec0310463bb32..d552510e585f5eaa32c1871d6ff693d98a660e48 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/017-brand-artwork/reviews-summary.md
**Review**: round 1 · 3296b8ea6c74779bd7cb2de88bdec0310463bb32..d552510e585f5eaa32c1871d6ff693d98a660e48 · agd-test-reviewer · PASS · 0 critical, 0 high, 0 medium, 2 low · .prdkit/cache/evidence/017-brand-artwork/reviews-summary.md
**Review**: round 1 · 3296b8ea6c74779bd7cb2de88bdec0310463bb32..d552510e585f5eaa32c1871d6ff693d98a660e48 · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/017-brand-artwork/reviews-summary.md
**Review**: round 2 · 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e..e4457e2936b940bbad3ad0a67e8c09219495dd9d · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/10#issuecomment-6387281531
