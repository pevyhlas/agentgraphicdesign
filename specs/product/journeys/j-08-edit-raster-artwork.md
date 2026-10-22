# J-08 Edit raster artwork (hypothesis, planned: 014-full-editor-domains)

**Who**: Agent · **Area**: Authoring · **Surfaces**: MCP server, CLI

One intent each with [J-06 Animate a sprite](j-06-animate-sprite.md) and
[J-07 Author a tile set](j-07-author-tile-set.md).

## Intent

Edit layered still raster artwork beyond J-04: selections, brushes, filters and
non-destructive composition.

## Situation

Ordinary artwork, not a sprite or tile: it never needs a timeline or tileset membership.

## Scenario

1. It selects regions and paints, filters or composes non-destructively.
2. It previews and exports the result.

## Variations

- **The artwork changed since it was read**: the edit is refused with a revision conflict.
- **A step to undo**: non-destructive composition removes or changes that step; the layers
  below are untouched.
- **An edit past the published limits**: a structured limit error, never unbounded work.

## Success

Raster artwork edited in bounded calls with reproducible results.
