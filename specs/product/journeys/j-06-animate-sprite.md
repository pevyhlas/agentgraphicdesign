# J-06 Animate a sprite (hypothesis, planned: 014-full-editor-domains)

**Who**: Agent · **Area**: Authoring · **Surfaces**: MCP server, CLI

One intent each with [J-07 Author a tile set](j-07-author-tile-set.md) and
[J-08 Edit raster artwork](j-08-edit-raster-artwork.md).

## Intent

Author an animated sprite as a native source: cels, exact timing, frames and exposures, clips,
directions and variants, and pivots, then review it over time.

## Situation

As in [J-04](j-04-edit-pixel-art.md), on shared pixel foundations; the agent must not need one
call per frame.

## Scenario

1. It creates a sprite and adds or links cels on an exact timeline.
2. It defines clips, directions, variants and pivots.
3. It previews and diffs the animation over time, then exports it.

## Variations

- **The sprite changed since it was read**: the edit is refused with a revision conflict.
- **A timing the timebase cannot hold exactly**: refused, never rounded silently (Q-02).
- **A clip past the published limits**: a structured limit error, never one call per frame.
- **Frames from an external producer**: checked and imported as data; only admission records
  their provenance (R-11).

## Success

A reviewable animated sprite built in bounded calls, with temporal previews and diffs.
