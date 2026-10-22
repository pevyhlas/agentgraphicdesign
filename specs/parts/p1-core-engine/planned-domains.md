# P1 Core engine — planned domains

**Serves**: J-06, J-07, J-08; R-09, R-10, R-11, R-12 · **Maturity**: hypothesis, planned: 014-full-editor-domains · Part: [P1 Core engine](../p1-core-engine.md)

Each domain gets its own reviewed source format over shared pixel, evaluation and pipeline
services, never vector nodes (C-09, D-04). Change 014 holds the open design questions.

## Animation and sprites (R-09)

Exact time, cels, frames and exposures, clips, directions and variants, pivots, and temporal
previews and diffs. Pixels and animation are shared foundations; brushes, filters and effects
belong to raster artwork, not here.

## Tiles (R-10)

Connectivity intent and its pixel realization, validated independently, including corners,
junctions, composition and animation. Tiles share foundations with sprites but keep separate
invariants: they are not a sprite subclass. Each starts with a reviewed format, an independent
fixture corpus and validators that never certify output when a required check is incomplete.

## Artifact admission (R-11)

Sealed admission and provenance of external producer exports. It starts once the external
producer's export contract exists; until then still import and verification are the only
intake, always reporting `admitted=false`.

## Raster artwork (R-12)

Layered still-image editing beyond native pixels: selections, brushes, filters and
non-destructive composition, with its own reviewed source format over the native pixel
surfaces and evaluation; ordinary artwork never needs a timeline or tileset membership.
