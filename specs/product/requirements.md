# Agent Graphic Design — requirements

<!-- R- items by area, using the journey index's area names. Each says what must be true for
users, testable in principle, the journeys it serves and its source. Past the budget, split into
requirements/<area>.md and keep one line and a link per area here. -->

## Authoring

- **R-01 One engine, agent-primary surfaces** (validated): CLI and MCP drive the same
  authoritative engine operations; a later UI is a thin client over those same operations.
  Journeys: J-01, J-04, J-05. Source: owner direction (agents first, UI later).
- **R-02 Vector authoring** (validated): create, inspect, atomically patch, lay out (bounds,
  align, distribute, fit, text layout), validate, lint and export documents to SVG and raster,
  including symbol, sprite-sheet and single-node exports. Journeys: J-01. Source: owner
  direction (the 0.1 to 0.3 version plan).
- **R-03 Bounded agent ergonomics** (validated): capability discovery, paged and subtree
  inspection, small previews and actionable structured errors; no per-pixel or per-frame call
  loops. Journeys: J-01, J-04, J-05. Source: owner direction (agents first).
- **R-06 Native layered pixel stills** (validated): layers, opacity, visibility, coverage
  masks, region edits, preview, PNG export and read-only export checks, at any canvas size
  within published limits (no preset, square or power-of-two restriction); import of one
  provided PNG still as a new asset or an independent layer, keeping the original input.
  Journeys: J-04. Source: owner direction (the first native pixel scope, with 32x32 and 64x64
  as examples, not limits).
- **R-07 Provided-artifact checks** (validated): inventory, hash, PNG-structure and
  decoded-sample checks of supplied PNG bundles, always reporting nothing admitted and no
  provenance verified. Journeys: J-04. Source: owner direction (interoperability through
  provided artifacts).
- **R-08 Tokens, import and path operations** (validated): colour and typography tokens,
  palettes and DTCG interchange; best-effort SVG import with structured warnings; boolean and
  offset path operations. Journeys: J-01. Source: owner direction (the 0.1 to 0.3 version plan).
- **R-09 Animated sprite authoring** (validated, planned: 014-full-editor-domains): cels, exact
  time, frames and exposures, clips, directions and variants, pivots, and temporal previews
  and diffs. Journeys: J-06. Source: owner direction (full editor; sprites a first-class domain).
- **R-10 Tile authoring** (validated, planned: 014-full-editor-domains): connectivity intent and
  its pixel realization are validated separately, including corners, junctions, composition
  and animation. Journeys: J-07. Source: owner direction (tiles a separate first-class domain).
- **R-11 External artifact admission** (validated, planned: 014-full-editor-domains): admit
  exports of an external producer (such as the separate Blender producer,
  AgentBlender) with recorded
  provenance; the admission and provenance contract is co-designed with that producer's export
  contract. Journeys: J-04, J-06. Source: owner direction (Blender through an external producer).
- **R-12 Raster artwork editing** (validated, planned: 014-full-editor-domains): layered
  still-image editing beyond R-06 (selections, brushes, filters and non-destructive
  composition) over the shared pixel foundation, never requiring animation or tileset
  membership. Journeys: J-08. Source: owner direction (full editor).
- **R-13 Persisted reuse** (validated, planned: 013-brand-workflow-uptake): the first slice
  bakes reuse placements with a drift check and accepts asset kits from source builds whose
  build stamp truthfully says so; instances with token overrides need a format review first
  (C-09). Journeys: J-01, J-03. Source: owner direction (the reuse line and its first slice).

## Review and delivery

- **R-04 Reviewable sources** (validated): design sources are ordinary versioned files whose
  structural edits diff cleanly; binary pixel data is reviewed through previews and checks.
  Journeys: J-02, J-04. Source: owner direction (reviewers are a target audience; Git-first).
- **R-05 Manifest asset pipeline** (validated): AGD owns generic export, sprite-sheet and
  drift-check mechanics; consumers keep framework wiring, accessibility and visual approval.
  Journeys: J-03. Source: owner direction (0.3 before wider consumer adoption).

## Left open

- Default sizes, templates and palettes offered to agents, within the bounds of Q-03.
- Which unsupported SVG features import maps next, as long as every remaining loss is reported
  (Q-02).
