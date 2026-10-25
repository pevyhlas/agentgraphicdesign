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

- **T029** precedes implementation: exact SVG bytes, Linux-x64 PNG SHA-256 and hand-derived
  legacy D-10 crop boxes for a nested/transformed/clipped/half-opaque node, a token-filled node
  and a stroked node. Derive actual stroke-expanded ink separately and document retained overflow
  clipping: a20×20 rectangle with4-unit centered stroke retains today's20×20 crop, although ink
  occupies24×24. The recovered `ink` crop name refers to legacy D-10 measurement, not a promise
  to correct its geometry-only stroke treatment here. Keep existing sub-node and010 token tests.
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
3. Crop computation retains the legacy selected-subtree measurement. A separate diagnostic walk
   visits only effectively visible nodes (never descendants of a hidden node); it must not turn
   ignored content into an export failure. Collect existing diagnostics deterministically in
   document order, deduplicate equal messages, cap at64 plus a truncation note, and expose code
   BOUNDS_DIAGNOSTIC. Add
   ExportResult.Diagnostics (default empty), combine bounds/render diagnostics in RenderNode, and
   route vector result diagnostics through the existing CLI/MCP diagnostics output. No new tool.
4. Measurement honors font directories and system-font policy, but uses non-strict resolution:
   bounds computation can include legacy hidden geometry, and diagnostics must not create a new
   strict-font rejection for content the renderer skips. The actual render retains the original
   StrictFonts flag and is authoritative for fatal missing rendered fonts. SVG uses the existing
   default policy (surface SVG font-option restrictions stay). Test visible missing strict text
   still fails, hidden text and hidden ancestor chains do not introduce a failure, and legacy crop
   extents remain pinned. Add portable fake-measurer and real bundled-font fixtures.
5. This builder call creates one private reference/crop result; future reader caching owns distinct
   references per call. No global cache or live document aliases. Path derivations do not yet exist;
   their future introduction must strip them in this shared copy as D-26 requires.

## Tasks

- [x] Independent architecture design review of the recovered contract and integration choices.
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
| A1 | P1.B-15, Q-01/Q-02 | pre-refactor SVG/PNG goldens plus hand-derived legacy crop boxes remain equal; stroke ink overflow/clipping documented separately | 0 | NOT_RUN |
| A2 | D-26, Q-03/Q-04 | builder masks/background restore, copy-count/cancellation, tokens and source-preservation tests | 0 | NOT_RUN |
| A3 | P1.B-15, P4, Q-02 | font policy affects actual crop; bounds diagnostics reach render/vector export and both surfaces | 0 | NOT_RUN |
| A4 | Delivery | source/installed checks, exact-head CI and independent review | 0 | NOT_RUN |

## State

**Step**: ready to implement
**Waiting on**: nothing for T029; implementation and acceptance remain open.
**Authority**: owner continuation (“please continue on next capabilities”, then “go ahead - we
should setup github CI for mac os”); constitution authorizes feature commit/push/PR and reviewed
exact-head green merge. Release and platform qualification gates hold.
**Branch / PR**: 020-transient-builder; base55c0efab887ea924c14019f0d98cc5e5eec1f5c7;
[PR #13](https://github.com/pevyhlas/agentgraphicdesign/pull/13). Merged019 hosted CI incorporated;
integration base8bad0a76a8ec3d663f734f172b6223fa6ee588d3. No production implementation yet.
**Attempts**: 0 acceptance attempts; design fix round1 addresses crop-oracle and hidden-font
findings. Initial external-directory reviewer access failure was tooling-only and is preserved.
**Next**: T029 pre-refactor characterization, committed before T032 builder replacement.
**Design review**: round1 · 55c0efab887ea924c14019f0d98cc5e5eec1f5c7..16005b84c333be97dbe7615d1d0d005f5ed8ab4d · agd-architecture-reviewer · FAIL · 0 critical, 0 high, 2 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/13#issuecomment-6401464438
**Design review**: round2 · 16005b84c333be97dbe7615d1d0d005f5ed8ab4d..41493d89a0c0904ca8747c06b420a14a8070e2bc · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/13#issuecomment-6401482295
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->
