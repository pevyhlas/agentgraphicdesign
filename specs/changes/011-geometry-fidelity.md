# Change: geometry fidelity

**Opened**: 2026-10-20 · **Parts**: P1, P4 · **Journeys**: J-01 · **Tier**: T3 · **Parent**: 013-brand-workflow-uptake

## Intent

Make path geometry a general capability with a stated, measured tolerance: stroking a
centerline into a few cubics instead of hundreds of segments, cutting clearances, reuniting
pieces without slivers, transforming, fitting and splitting paths, inspecting contours,
converting shapes and text to outlines, and writing results back in one revision-pinned patch,
over CLI and MCP alike. Slice S04 of change 013; implements the change 007 geometry design.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L0 | R-08 | extend with curve-preserving operations, transform, fit, split and outlines | |
| L2 | P1 | new behaviors in import, paths and tokens; a frozen geometry algorithm identifier | |
| L2 | P4 | new `path stroke`, `path fit`, `path split`, `path inspect`, `path from-node`, `text outline` and MCP tools | parity |

## Bounds

- Q-01: at risk: deterministic trigonometry changes the last bit of some outputs; declared as a
  cutover with a drift note.
- Q-03: holds: magnitude, point and fit-operation budgets before allocation.
- Others in scope: hold.

## Approach

As designed in change 007: stage planner and reference allowance, the dense independent
oracle, coincidence matching, the curve fitter, the stroker and offset builder, store-free
node-addressed operands, and write-back through the patch path with an expected revision. Text
outline waits on the owner's glyph-outline platform class decision.

## Tasks

- [ ] Port the regular-file gate inside bounded document reads, implemented and reviewed in
  the pre-public history.
- [ ] Pinned Clipper2 assumptions, deterministic trigonometry and SVG corner-radius export.
- [ ] The affine model and parsing, the dense oracle, the stage planner, the stroker,
  coincidence matching and the curve fitter.
- [ ] Operand arguments, write-back, transform, fit and split, then text outlines once the
  owner decides the glyph-outline platform class.
- [ ] Documentation, the integration reproduction and the geometry algorithm identifier freeze.

## Checks

The tolerance is a measured band on the emitted bytes, never a tuning number (Q-01, Q-03). New
behaviors and the algorithm identifier get tests; CLI and MCP stay registered in the surface
manifest (P4.B-01).

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | R-08 | measured deviation within tolerance on the emitted bytes, by the dense oracle | 0 | NOT_RUN |
| A2 | R-08 | repeated booleans leave no slivers (chained round-trip fixtures) | 0 | NOT_RUN |
| A3 | Q-01 | trigonometry goldens and the golden drift check | 0 | NOT_RUN |

## State

**Step**: tasks
**Waiting on**: the owner, on porting and the glyph-outline platform class (inbox)
**Next**: port the regular-file gate
