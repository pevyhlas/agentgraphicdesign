# Change: geometry fidelity

**Opened**: 2026-10-20 · **Parts**: P1, P4 · **Journeys**: J-01 · **Tier**: T3 · **Parent**: 013-brand-workflow-uptake

## Intent

Make path geometry a general capability with a stated, measured tolerance: stroking a centerline
into a few cubics instead of hundreds of segments, cutting clearances, reuniting pieces without
slivers, transforming, fitting and splitting paths, inspecting contours, converting shapes and text
to outlines, and writing results back in one revision-pinned patch, over CLI and MCP alike. Slice
S04 of change 013; implements D-21 without reopening it.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L0 | R-08 | extend with curve-preserving operations, transform, fit, split and outlines | |
| L1 | D-14 | Clipper2 for booleans only | with PR 2 |
| L2 | P1 | path-geometry layer behaviors; exact bounds; `r_eff` corners; the frozen identifier `agd-geometry/1`; a special-file gate at the Core document reader | |
| L2 | P1.B-19 | path operations keep curves and report the measured tolerance | |
| L2 | P4 | new `path stroke`, `path transform`, `path fit`, `path split`, `path inspect`, `path from-node`, `text outline` and MCP tools; structured MCP operands | parity; declared cutovers |

## Bounds

- Q-01: at risk: deterministic trigonometry and exact bounds move some outputs in the last bit;
  declared as cutovers with a drift note (committed curve rasters may report drift).
- Q-03: holds: magnitude, point, fit-operation, document-set and write-admission budgets before
  work, each tested at and one over.
- Q-04: holds: writes are one patch pinned to the revision read; no-op writes leave files alone.
- C-09: holds: format 1 unchanged.
- Others in scope: hold.

## Approach

Feature decisions (lead-level, inside D-21; IDs as in the pre-public plan):

- 011/D-1 typed request records, one engine method per operation, the string overloads removed;
  011/D-2 surfaces resolve files, Core gets literal or node operands, MCP operands become `{d}` or
  `{document, nodeId}` (legacy names refused with a hint); 011/D-3 sub-namespaces under
  `Core/PathOperations/`, one `Numerics.Affine`, MCP path tools in `PathTools.cs`.
- 011/D-4 Core evaluates full operand descriptors, surfaces expose only fill node operands (S07
  exposes descriptors); one render-style model, pinned to `SkiaRenderer`, decides what a node
  paints (`PATH_FILL_UNPAINTED`).
- 011/D-5 offset end types as D-21; 011/D-6 `path transform --op <kind>:<args>` repeatable, applied
  in order; 011/D-7 split and inspect classify non-crossing contours exactly at resolution `T/4`
  (touching allowed; crossing, shared-run and near-contact refused with non-looping hints).
- 011/D-8 write conveniences: a per-operation intended style decided at the destination, `--write`
  keeps the target's paint, a stroke result is filled with the source's stroke paint,
  `GRADIENT_BOX_CHANGED`, no-op writes, `INVALID_NODE_TYPE`, a default parent beside the source.
- 011/D-9 twelve pull requests with stated interim contracts (below); 011/D-10 optional ordered,
  sanitized error `details` and result `warnings`; 011/D-11 retired (σ rows stay acceptance;
  `PATH_OPERAND_MAGNIFIED`); 011/D-12 one load per canonical document per command, case aliases
  refused, the revision pinned on that handle; 011/D-13 ≤ 64 documents and 64 MiB per command, MCP
  operand-only documents ≤ 1 MiB; 011/D-14 write admission against every consuming budget and a
  store-wide rule that no save or create writes more than the store will read.
- 011/D-15 one Core special-file gate inside the bounded document reader: a path that is not a
  regular file after following links is `PATH_ESCAPE` where inspection is admitted; a zero-length
  file is `INVALID_FORMAT` everywhere; a missing file passes; the store enumeration and
  `list_documents` skip refused entries; the MCP and CLI import inputs call it directly; fail-closed
  IL inventory tests in Core and MCP.
- 011/D-16 the `agd-geometry/1-dev` identifier until the freeze pull request, then frozen goldens
  under an append-only manifest and a CI guard (D-21); 011/D-17 the miter-limit rule as D-21.
- Limits, each tested at and one over: tolerance [0.008, 100]; point budget 250 000 per command;
  union operands 256 on MCP and 10 000 in Core; `MaxFitOperations` 50 M; 64 documents and
  64 MiB per command; a written `d` of at most 256 000 characters and 10 000 commands; at most
  1 000 000 path characters, 10 000 nodes and 10 000 children per container after a patch; an
  `--op` list of 64; 4096 split parts returned. Cancellation is `CANCELLED` (CLI exit 130) with
  bytes and `revn` unchanged.

## Tasks

- [ ] PR 0, 011/T027: the special-file gate (implemented and reviewed pre-public; port it).
- [ ] PR 1a: T001 pin the Clipper2 assumptions (a failure returns to architecture review); T002
  `DeterministicTrig`; T003 trig tests and the IL allowlist scan; T004 SVG `rx = r_eff` and error
  details; T028 the golden guard.
- [ ] PR 1b: T005 model, affine, parser, writer, arcs, shape-to-path, flattener, exact bounds;
  T025 the dense oracle, ideal regions and the honesty seam.
- [ ] PR 1c: T006 planner, allowance, evaluator, polyline verifier, typed bool and union, the
  polygon cutover, `-dev` identifier; T007 planner table; T008 migration and limits.
- [ ] PR 2: T009 stroker and offsets, `ClipperOffset` removed; T010 `path stroke`; T011 stroke and
  offset oracles in polygon mode.
- [ ] PR 3a: T012 matcher, refiner, reuser; T013 their property tests. PR 3b: T014 fitter, cleanup,
  `curves` default; T015 chained round trips and the curves-mode oracles.
- [ ] PR 4a: T016 node operands, document sets, structured MCP operands, render-style model; T017
  read-row tests. PR 4b: T029 revision-pinned writes, style mapper, write admission, document
  limits; T030 write tests and cancellation smokes.
- [ ] PR 4c: T018 transform, fit, split, inspect, from-node; T019 their tests; T020 capabilities
  and docs. PR 4d: T021 crossing-strand reproduction and installed smoke; T026 identifier freeze.
- [ ] PR 5 (gated on the owner's glyph-class answer, inbox): T022 glyph provider and
  `text outline`; T023 its tests; T024 docs.

Carried checks (from change 007, closed in the owning task's review): 011/K-01 dense oracle as
defined, honesty check, "measured" wording (T025, T006, T014, T020); K-02 allowance split and
feasibility from exact expressions (T006, T007, T015); K-03 refinement acceptance (T012, T013,
T009); K-04 cusp rule from measured Skia (T011); K-05 miter limit and end types (T009, T011); K-06
magnitude checks in every space (T006, T008, T009, T011, T018, T019); K-07 sector cost (T011); K-08
oracle classes ≤ 120 s each or a recorded test-plan change; K-09 map-scale sizing via σ rows; K-10
the special-file residuals for the security reviewer (T027).

Closure review checks: 011/RC-01 capabilities match D-27 (T020); RC-02, RC-03 the gate's import
and `Try*` outcomes (T027); RC-04 fail-closed Core read inventory (T027); RC-05 typed `stat`
outcomes and bounded FIFO tests (T027); RC-06 both read-code cutovers in the changelog (T027);
RC-07 gradient box in Core doubles (T029); RC-08 the pre-refiner crossing exclusion (T008); RC-09
predicted sample counts per class (T008, T011, T015); RC-10 the measured share leaves
`q_out + T/64` at every T (T006, T007); RC-11 the reverse measurement target (T025, T006, T014).

## Checks

The tolerance is a measured band on the emitted bytes, never a tuning number (Q-01, Q-03). New
behaviors and the identifier get tests; CLI and MCP stay registered in the surface manifest
(P4.B-01).

## Acceptance

Design acceptance criteria 011/AC-01 to 011/AC-34 group as below.

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | R-08 | AC-01 to AC-04, AC-32: measured deviation within tolerance by the dense oracle; feasibility fails fast; polygon mode | 0 | NOT_RUN |
| A2 | R-08 | AC-05 to AC-07: chained round trips leave no slivers; matcher and refinement properties | 0 | NOT_RUN |
| A3 | R-08 | AC-08 to AC-10, AC-28: stroke and offset against the SVG ideal, live-stroke raster equivalence, refusals | 0 | NOT_RUN |
| A4 | Q-01 | AC-11, AC-12: double runs, goldens and the guard, the IL allowlist scan, trig sweep | 0 | NOT_RUN |
| A5 | R-08 | AC-13 to AC-18: shape conversion with `r_eff`, exact bounds, transform, fit, split, inspect, from-node | 0 | NOT_RUN |
| A6 | Q-04 | AC-19 to AC-22, AC-27, AC-29 to AC-31, AC-33, AC-34: operands, evaluation space, pinned writes, special files, write styles, admission, magnitude bounds | 0 | NOT_RUN |
| A7 | P4.B-01 | AC-23 to AC-25: limits and cancellation, parity and capabilities, exit codes | 0 | NOT_RUN |
| A8 | R-08 | AC-26: text outlines render like the text (after the owner's answer) | 0 | NOT_RUN |

## State

**Step**: tasks
**Review**: design approved at closure pass 1 (architecture, code, test) in the pre-public
history; PR 0 (011/T027) passed implementation review round 2 in all four lenses (architecture,
code, security, test) after one fix round, with low notes only; the security reviewer accepted the
special-file residuals (check-then-open race, macOS before change 008, font directories, the
store's reparse-point asymmetry); ranges are not reproducible here
**Waiting on**: the owner, on porting (inbox) and the glyph-outline platform class (inbox, PR 5 only)
**Next**: port PR 0 as its own pull request, then PR 1a
