# P1 Core engine — import, paths and tokens

**Serves**: J-01; R-08, Q-02, Q-03 · **Code**: `src/FTPlus.AgentGraphicDesign.Core/Import/`, `PathOperations/`, `Tokens/` · Part: [P1 Core engine](../p1-core-engine.md)

## What it does

- Imports SVG best effort with the in-repository XML importer (D-20), mapping supported
  elements to existing node types, expanding local `<use>` and `<symbol>` and rectangular
  clips (D-11).
- Runs boolean and offset path operations through Clipper2 (D-14) over `M L H V C S Q T A Z`,
  with bounded arc-to-cubic conversion and deterministic flattening.
- Holds flat internal colour and typography tokens and palettes, resolves `tokenRef`, and
  imports and exports DTCG (D-03).

## Behaviors

- **P1.B-18 Import reports loss**: an unsupported SVG feature yields a structured warning, never
  a silent drop; reference cycles are broken with a warning; `<use>` nesting past its depth
  bound is skipped with a warning, and past the expansion count bound import fails with
  `SVG_RESOURCE_LIMIT`. Test: `SvgImportServiceTests`, `SvgImportUseSymbolClipTests`.
- **P1.B-19 Bounded path operations**: path operations accept the full path command set and
  fail, rather than run unbounded, past the published command, point, coordinate and output
  limits. Test: `PathOperationsServiceTests`, `PathCommandTests`.
- **P1.B-20 DTCG is interchange only**: DTCG import maps `$value` and `$type` into flat tokens
  and drops metadata such as `$description`; export writes DTCG from the flat tokens. Test:
  `TokenServiceTests`.

## Interface guarantees

- Import creates only node types of format 1; no import changes the document format (C-09).

## Failure semantics

- Malformed input is a structured parse error; a resource limit is a structured limit error.

## Left open

- Non-rectangular clips, masks, filters, patterns, markers and external references (D-11).
