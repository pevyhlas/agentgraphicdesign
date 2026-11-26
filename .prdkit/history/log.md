# Closed-change history

History only; never cited by the spec. One short row per change, no task lists or
acceptance tables. Find the original close commit with
`git log --diff-filter=A -- .prdkit/history/log/<id>.md`.

<!-- The 0.x features below closed before the 1.0 cutover; .prdkit/history/archive.md
condenses them, and their records and pull requests belong to the pre-public history.
Rows closed before Product-Kit 2.2 were shortened to the 200-character limit; their full
text is in Git at specs/log.md before the 3.1.5 cutover. -->

- 2026-09-02 001-v0-1-stabilization: stabilize and release v0.1.0 with a reproducible bundle and provenance — R-02, Q-01, Q-06 — pre-public history
- 2026-09-12 003-agent-native-graphics-editor: full-editor design, artifact metadata, typed producers, preflight and layered stills (0.2.0-preview.2) — R-06, R-07, R-09–R-11 — pre-public history
- 2026-10-01 002-v0-3-reusable-assets: manifest asset pipeline, arcs, sub-node export, bounded SVG reference import and stable 0.3.0 — R-05, R-08, D-10, D-11 — pre-public history
- 2026-10-08 004-authoring-reliability: current-tool loading and validation reliability repairs — R-02, R-03 — pre-public history
- 2026-10-10 005-still-image-import: preserved managed-local PNG still import into native assets and independent layers — R-06, R-07 — pre-public history
- 2026-10-24 015-public-readiness: public onboarding, Product-Kit 1.1.1, SDK 10.0.113, hygiene — J-05, C-01, C-10, Q-06 — delivered — [PR #8](https://github.com/pevyhlas/agentgraphicdesign/pull/8)
- 2026-10-24 016-render-fidelity: CSS alpha-color rendering, immutable package source identity — P1, Q-01, Q-02, Q-06 — delivered — [PR #9](https://github.com/pevyhlas/agentgraphicdesign/pull/9)
- 2026-10-24 017-brand-artwork: editable identity proposals authored via AGD; adoption is owner choice — J-01–J-03, C-01 — delivered — [PR #10](https://github.com/pevyhlas/agentgraphicdesign/pull/10)
- 2026-10-25 018-review-foundation: raw RGBA rendering, verified contact-sheet baselines — J-02, Q-01–Q-03, 012/T001–T003 — delivered — [PR #11](https://github.com/pevyhlas/agentgraphicdesign/pull/11)
- 2026-10-25 019-macos-ci: advisory macOS 26 arm64 CI on every PR — J-05, C-10, C-11, Q-05, D-28 — qualification open — [PR #12](https://github.com/pevyhlas/agentgraphicdesign/pull/12)
- 2026-10-25 020-transient-builder: copy/mask/crop builder — P1.B-15, P4, Q-01–Q-04, D-26, 012/T029/T032/T033 — closed — [PR #13](https://github.com/pevyhlas/agentgraphicdesign/pull/13)
- 2026-10-26 022-product-kit-120: adopt Product-Kit 1.2.0 — operations, C-01, J-02/J-05, Q-06 — registry, input, macOS open — [PR #15](https://github.com/pevyhlas/agentgraphicdesign/pull/15)
- 2026-10-26 023-platform-registry: S0 registry, MCP cutover — D-28, P1.I-08/09, P4.B-02, 008/T001–T002 — macOS, Windows open — [PR #16](https://github.com/pevyhlas/agentgraphicdesign/pull/16)
- 2026-10-26 021-review-inputs: bounded review inputs — D-26, Q-01/Q-03/Q-05, 012/T004–T005 — sessions, catalog, surfaces open — [PR #14](https://github.com/pevyhlas/agentgraphicdesign/pull/14)
- 2026-10-27 024-product-kit-121: adopt Product-Kit 1.2.1 — operations, C-01/C-10, Q-06 — platform, release gates open — [PR #17](https://github.com/pevyhlas/agentgraphicdesign/pull/17)
- 2026-10-28 025-render-session: per-render font catalog — P1.B-16, D-26, Q-01–Q-03, 012/T034–T035 — public surfaces, platform open — [PR #18](https://github.com/pevyhlas/agentgraphicdesign/pull/18)
- 2026-10-28 026-macos-progress: two-mode macOS records, migration gates — D-28/P0, Q-01/Q-03/Q-05, 008/T003 — qualification open — [PR #19](https://github.com/pevyhlas/agentgraphicdesign/pull/19)
- 2026-10-28 027-portable-fonts: Noto test fixtures fix four font failures — D-01, D-28, P1.B-16, 008/T013a — qualification open — [PR #20](https://github.com/pevyhlas/agentgraphicdesign/pull/20)
- 2026-10-28 028-canonical-paths: verified system-alias paths — D-28, P1.I-02, P4.I-01, 008/T006, 009/E3 — 262 fixed, 008 open — [PR #21](https://github.com/pevyhlas/agentgraphicdesign/pull/21)
- 2026-10-29 029-asset-inspection: macOS 26+ inspection — C-11/Q-05, D-28, P1.I-09, P4.B-02, 008/T007 — 264 fixed, 216 open — [PR #22](https://github.com/pevyhlas/agentgraphicdesign/pull/22)
- 2026-11-24 030-agents-tier-fable52: reviewer pins, 2026-11-24 baseline — agents tier map, constitution Commands — tooling only — [PR #24](https://github.com/pevyhlas/agentgraphicdesign/pull/24)
