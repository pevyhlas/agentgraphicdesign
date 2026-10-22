# Change: full-editor domains

**Opened**: 2026-10-22 · **Parts**: P1, P4 · **Journeys**: J-06, J-07, J-08 · **Tier**: T3 · **Parent**: none

<!-- Opened at the 1.1 restructure to hold owner-decided, unscheduled full-editor work that the
planned markers of R-09 to R-12 name. Each domain becomes its own child change when scheduled. -->

## Intent

The owner set AGD's direction as a full agent-native graphics editor: animated sprites (R-09),
tiles (R-10), external artifact admission (R-11) and raster artwork (R-12) are first-class
domains, delivered in tested increments without shrinking the ambition. None is scheduled yet.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L0 | J-06, J-07, J-08 | confirm the outlined variations and success with the owner | inbox |
| L0 | R-09 to R-12 | remove the planned marker as each domain ships | |
| L2 | P1 | each domain's component, from the planned-domains outline | |

## Bounds

- C-09: each domain gets its own reviewed format, never vector nodes.
- C-07: admission consumes producer exports; AGD never runs Blender.
- C-03: no Python, also in recipes.
- Others in scope: hold.

## Approach

Former gates, to settle per domain before its format freezes (D-04):

- Identity and sharing, payload, colour and alpha, time and topology decisions, with
  independent failure fixtures.
- Pixels and raster foundations with editable animation: linked and unique cels, frames and
  exposures, exact evaluation and revision-checked writes.
- Rich sprite authoring: clips, directions and variants, retiming and loops, pivots and sockets,
  attachments, onion skins and temporal and pixel diffs.
- Tiles: an independent intent universe, coverage, corners and junctions, pixel, composite and
  phase validation, reproducible counterexamples.
- Cross-domain projects: typed references, dependency and quality evidence, coherent sheets and
  atlases (atlas packing waits for a named consumer use case; target adapters are intended
  scope under change 013 slice S06), target
  reconstruction and platform qualification.
- Depth and an optional UI: brushes and effects, vector and raster composition, property
  animation and interchange, a UI over the same contracts.
- Admission: provided-artifact intake and native sequence import through the shared producer
  boundary, co-designed with the external producer's export contract (D-09).

Open per domain: cel identity and payload deduplication, timebase and exposure authority,
palettes and indexed blending, tile topology, native write contracts, and coherent multi-output
publication. Durable retries, semantic undo and restore or fork reconciliation stay
workflow-specific.

## Tasks

- [ ] The owner picks the first domain and its stage (inbox).
- [ ] Open a child change per domain with its format review.

## Checks

Each domain's format review checks C-09 and D-04; its validators never certify output when a
required check is incomplete (J-07).

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | R-09 to R-12 | each child change's acceptance | 0 | NOT_RUN |

## State

**Step**: intent
**Waiting on**: the owner, on the first domain and its stage (inbox)
**Next**: owner direction
