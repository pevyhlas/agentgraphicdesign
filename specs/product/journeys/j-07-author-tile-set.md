# J-07 Author a tile set (hypothesis, planned: 014-full-editor-domains)

**Who**: Agent · **Area**: Authoring · **Surfaces**: MCP server, CLI

One intent each with [J-06 Animate a sprite](j-06-animate-sprite.md) and
[J-08 Edit raster artwork](j-08-edit-raster-artwork.md).

## Intent

Author a tile set whose connectivity intent and pixel realization are each validated, so tiles
join correctly at edges, corners and junctions.

## Situation

Tiles share pixel and animation foundations with sprites but are their own domain, not a
sprite-sheet mode.

## Scenario

1. It declares the tile set's connectivity intent.
2. It draws each tile's pixels.
3. It validates intent and pixels separately, including corners, junctions, composition and
   animation, and gets reproducible counterexamples for failures.

## Variations

- **A seam does not join**: a reproducible counterexample names the tiles and the edge, corner
  or junction.
- **Intent and pixels disagree**: each is reported on its own, so the agent knows which to fix.
- **A required check cannot complete** (a limit, a cancelled run): the set is not certified,
  and the report names what was not checked.

## Success

A tile set certified only when every required check passed; an incomplete check never certifies.
