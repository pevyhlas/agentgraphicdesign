# Change: capability design for geometry, outlines, families, targets, kits and review

**Opened**: 2026-10-19 · **Parts**: P1, P4 · **Journeys**: J-01, J-02, J-03 · **Tier**: T3 · **Parent**: 013-brand-workflow-uptake

## Intent

Before implementing, settle one coherent, general architecture for the capabilities a real
brand job showed missing: curve-preserving geometry, stroke outlines and knockouts, variant
families with optical sizes, platform export targets, versioned kits, and review and
legibility tooling. Slice S00 of change 013; it unblocks S04 to S09. Design only: no code.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | new D- decisions | six decisions: curve-preserving geometry, derived geometry, asset families, manifest v2 target adapters, versioned kits, review and analysis boundaries | accepted by the closure design review |
| L2 | P1 | the format additions (`path.derivation`, `group.placement`) land with S07 and S08 | additive to format 1 |

## Bounds

- C-09: holds: derivations and baked placements are additive format 1 fields; anything
  render-affecting beyond them needs format 2.
- C-01: holds: no new dependency.
- Others in scope: hold.

## Approach

The approved design, condensed:

- **Geometry**: Clipper2 for booleans only, one final boolean per stage, exact reuse of shared
  segments, angle-aware intersection refinement and width-based cleanup; a tolerance measured
  on the emitted bytes and reported, not certified; an AGD stroker with SVG joins and one
  miter-limit rule (default 4, range 1 to 1000); magnitude bounds before the integer clipping
  domain; deterministic trigonometry.
- **Derived geometry**: baked derivations in format 1, evaluated in derived-local space, with
  two-level hashes and a work bound; every static rule in the validator.
- **Families**: a family manifest with token-override axes and confined references, and a baked
  placement with drift and edit detection (owner choice: baked placements first).
- **Targets**: manifest v2 with typed, pure target adapters.
- **Kits**: a scoped, no-replace publisher with pin-based consumer trust; source builds may
  publish kits when stamped truthfully (owner choice).
- **Review**: a Core analysis component with a counted work budget and a server-free static
  gallery with hash-checked owned files and a writer lock.

## Tasks

- [x] Design and decision records approved by the closure design review in all four lenses
  (recorded in the pre-public history; brought here with change 011).
- [ ] Record the six accepted decisions in `specs/architecture/decisions.md` when the design is
  brought here.
- [ ] Close the design's carried conditions and closure checks in the slice reviews (S04 in 011, S05 in
  012; S06 to S09 when opened).
- [ ] The glyph-outline platform class waits on the owner (inbox); only the text-outline
  increment of S04 waits on it.

## Checks

Design only. Consistent with D-12 and C-09: derivations and baked placements are additive
format 1 fields. Each decision lands with its alternatives and revisit condition.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | the six decisions | independent design review, closure pass 2 | 1 | PASS (pre-public history) |
| A2 | owner choices on reuse and kits | owner answers | 1 | PASS (pre-public history) |

## State

**Step**: delivery
**Waiting on**: the owner, on the glyph-outline platform class and on porting (inbox)
**Next**: bring the design record and decisions here with change 011
