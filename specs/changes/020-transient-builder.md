# Change: Share transient document construction and preserve sub-node export fidelity

**Opened**: 2026-10-25 · **Parts**: P1, P4 · **Journeys**: J-01, J-02 · **Tier**: T2 · **Parent**: 012-review-analysis

<!-- Past its budget, a change splits into changes that each ship on their own (Parent: <id>). -->

## Intent

Continue the owner's next-capabilities direction with parent012's PR-2a: a shared transient
document builder for sub-node export and upcoming review/analysis. Reconcile parent012 with
merged child018; resolve the font-policy and dropped-bounds-diagnostic carry-overs from010.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P1.B-15, rendering/export | shared private-copy builder; same isolated-node crop | D-10, D-26 |
| L2 | P4 export/preview | report bounds diagnostics and honor render font policy | additive diagnostic result |

## Bounds

<!-- From `.prdkit/scripts/bash/bounds.sh <id>`: each constraint, non-goal or quality this change
could break, with how it holds; then "Others in scope: hold". A conflict goes to the owner inbox
with both sides, never resolved here. -->

- C-04: Core owns copy/mask/crop and diagnostic composition; surfaces only pass/report.
- C-09: vector document v1 unchanged; additive Core result diagnostics, no migration.
- Q-01, Q-02: pre-refactor bytes and independent ink boxes pin current behavior before replacement.
- Q-03: one synchronized private copy per build, no mask/background recopy, cancellation and
  existing node/depth bounds. Caller-scoped reference deduplication arrives in parent012 PR-2b.
- Q-04: source document and tokens never mutate, including failed/cancelled probes.
- C-11, Q-05: native-platform qualification stays with008; Linux raster hashes are platform-scoped.
- Others in scope: hold.

## Approach

Recovered criteria from018's retained pre-public012 tasks/plan, restated here:

- **T029** precedes implementation: exact SVG bytes, Linux-x64 PNG SHA-256 and hand-derived ink
  boxes for a nested/transformed/clipped/half-opaque node, a token-filled node and a stroked node
  whose ink extends beyond geometry. Keep existing sub-node and010 token tests.
- **T032**: pure synchronous `Rendering/TransientDocumentBuilder` makes one locked
  serialize/deserialize snapshot and returns a private `TransientDocument`. Inputs are document,
  optional node, canvas/ink crop and font policy. Node ink crop uses today's fresh non-clipping
  probe and translated wrapper, preserving isolated-node semantics and NODE_NOT_FOUND/NO_BOUNDS.
  Background source/none changes only the copy; explicit colors continue through render options.
- **T033**: `ApplyMask(ids)` retains only selected subtrees plus ancestor chains, preserving
  authored visibility, transforms/clips/opacity; null restores all authored visibility. Repeated
  masks/background changes never serialize again. Source background restores via SetBackground.

Proposed implementation choices for design review:

1. Builder receives a bounds-measure delegate (Core types only) and an internal counting copy seam
   for tests. It invokes the engine's policy-bound bounds path; no renderer or font IO in builder.
2. An owned transient snapshot can go directly to the export adapter; public whole-document
   ExportAsync retains its synchronized copy. This removes the existing second serialization in
   ExportNodeAsync without giving callers access to another document's mutable state.
3. Probe bounds for the subtree and descendants; collect their existing diagnostics deterministically
   in document order, deduplicate equal messages and expose code BOUNDS_DIAGNOSTIC. Add
   ExportResult.Diagnostics (default empty), combine bounds/render diagnostics in RenderNode, and
   route vector result diagnostics through the existing CLI/MCP diagnostics output. No new tool.
4. Map font directory/system/strict flags into FontPolicy before the crop probe. SVG uses the
   existing default policy (surface SVG font-option restrictions stay). Add portable fake-measurer
   tests and a real bundled-font fixture to avoid Linux-only host-font paths.
5. This builder call creates one private reference/crop result; future reader caching owns distinct
   references per call. No global cache or live document aliases. Path derivations do not yet exist;
   their future introduction must strip them in this shared copy as D-26 requires.

## Tasks

- [ ] Independent architecture design review of the recovered contract and integration choices.
- [ ] T029 baseline on unmodified engine, committed before T032.
- [ ] Builder, engine/surface diagnostic and font-policy integration; T033 and carry-over tests.
- [ ] Source/installed verification, applicable independent reviews and exact-head delivery.

## Checks

Existing engine, export, CLI/MCP and native format contracts stay aligned; new diagnostic behavior
gets Core and surface tests. Design and final architecture review required; final code, tests and
security lenses cover snapshot ownership, input bounds, font-policy and source-preservation.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | P1.B-15, Q-01/Q-02 | pre-refactor SVG/PNG goldens plus hand-derived crop boxes remain equal | 0 | NOT_RUN |
| A2 | D-26, Q-03/Q-04 | builder masks/background restore, copy-count/cancellation, tokens and source-preservation tests | 0 | NOT_RUN |
| A3 | P1.B-15, P4, Q-02 | font policy affects actual crop; bounds diagnostics reach render/vector export and both surfaces | 0 | NOT_RUN |
| A4 | Delivery | source/installed checks, exact-head CI and independent review | 0 | NOT_RUN |

## State

**Step**: design
**Waiting on**: independent design review before implementation.
**Authority**: owner continuation (“please continue on next capabilities”, then “go ahead - we
should setup github CI for mac os”); constitution authorizes feature commit/push/PR and reviewed
exact-head green merge. Release and platform qualification gates hold.
**Branch**: 020-transient-builder; base55c0efab887ea924c14019f0d98cc5e5eec1f5c7. Incorporate
the independently delivered019 CI increment before implementation verification.
**Attempts**: 0 acceptance attempts; 0 design fix rounds.
**Next**: review design, then characterize existing exports before replacing their builder.
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->
