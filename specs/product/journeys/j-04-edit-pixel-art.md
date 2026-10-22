# J-04 Edit pixel art (validated)

**Who**: Agent · **Area**: Authoring · **Surfaces**: MCP server, CLI

## Intent

Create or change a small layered pixel still (an icon, a sprite frame) exactly, and export it
with proof that the output matches the source.

## Situation

The agent works headless in a repository. Canvases are small and any size within published
limits (such as 32x32, 64x64 or 37x23); pixel payloads are binary, so reviewers depend on the
previews and checks it produces.

## Scenario

1. It creates a layered still, or imports one provided PNG as a new asset or as a new top layer
   of a same-size asset, keeping the original input.
2. It edits regions, layers, visibility, opacity and coverage masks as one guarded batch.
3. It previews the composite, exports a PNG and checks that export for drift.

## Variations

- **The source changed since it was read**: the edit is refused with a revision conflict.
- **A batch that changes nothing**: reported as unchanged; the revision stays.
- **Another native operation is running**: refused at once as busy, never queued.
- **An unknown or damaged source**: an error; nothing is migrated or repaired.
- **An output path inside the source, or an existing file**: inside the source is always
  refused; an existing file is replaced only on explicit request.
- **A PNG bundle from an external producer**: preflight and decoded verification report
  inventory, hash, structure and sample findings before any import; nothing is admitted.
- **On an unqualified platform**: work fails closed at the shared file probe with a platform
  error, except for the Windows pass-through (Q-05, P1.I-09).

## Success

The composite is exactly reproducible, the export check passes, and the reviewer sees a
preview. For the project: pixel assets need no scripts of their own.
