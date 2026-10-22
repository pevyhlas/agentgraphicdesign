# Change: brand workflow uptake

**Opened**: 2026-10-19 · **Parts**: P1, P4 · **Journeys**: J-01, J-02, J-03, J-04, J-05 · **Tier**: T3 · **Parent**: none

<!-- Carried over at the 1.1 restructure from an open 0.x target. Its slices are changes 006 to
012 (Parent: 013-brand-workflow-uptake) and the pending slices below. Detailed target, evidence
and design records are kept in the pre-public history. -->

## Intent

Make AGD usable for a real logo and icon job end to end: a mark with optical small sizes,
colour and mono variants, an outlined wordmark, lockups, app and platform icons and a versioned
brand kit, using only `agd` and `agd-mcp`, including on Apple silicon macOS (change 008).
Evidence: a pilot project's logo and icon work needed custom scripts for authoring, review
sheets, legibility checks and icon packaging, and was blocked by an MCP schema defect. The
maintainer approved closing these gaps and qualifying Apple silicon macOS; motion stays
deferred. Each capability is designed as a general feature with an
independent design review before implementation (constitution, Review and checks).

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L0 | R-13 | planned: 013-brand-workflow-uptake | baked placements with drift check; kits from truthfully stamped source builds |
| L0 | R-02, R-03, R-08, Q-05 | extend per slice | applied in each slice's PR |

## Bounds

- C-01: holds: no new dependency is assumed; any addition goes through D-01.
- C-03: holds: script-based steps are replaced by .NET operations, not ported.
- C-08: holds: the pilot is evidence only; nothing is ported from it.
- C-09: at risk in S07 and S08 (format changes): each needs architecture review and a declared
  format change.
- Others in scope: hold.

## Approach

Slices, each its own change and independently mergeable:

| Slice | Change | Delivers | Depends on |
|-------|--------|----------|------------|
| S00 | 007-capability-design | one reviewed design for geometry, outlines, families, targets, kits and review | none |
| S01 | 006-agent-tool-usability | MCP schema client compatibility, store-free geometry commands | none |
| S02 | 008-macos-qualification | Apple silicon macOS qualification (Q-05) | none |
| S03 | 009-agent-onboarding | `agd doctor`, `agd mcp install`, discovery and guidance | S01, S02 (its registry first; its probe before B5) |
| S04 | 011-geometry-fidelity | curve-preserving path operations, transform, fit, split, text outlines | S00 |
| S05 | 012-review-analysis | comparison sheets, static gallery, legibility analysis | S00, 010 |
| S06 | pending | export targets: mixed SVG and PNG, ICO, ICNS, iOS catalog, web icons, hicolor (D-24) | S00, S02, S05a, 010 |
| S07 | pending | stroke-to-outline with knockout (format change, D-22) | S00, S04 |
| S08 | pending | variant families with optical sizes and baked placements (format change, R-13, D-23) | S00, S05, S06, S07 for derivation-aware checks, 010, the SVG presentation fix |
| S09 | pending | kit build, check and pack (R-13, D-25) | S00, S02, S03, S06, S08 |
| S11 | pending | integrated run on Apple silicon macOS | S01 to S09 |

The sub-node token fix (010) lands before S05, whose shared transient-document builder takes over
its token copy, and so before S06 and S08.

## Tasks

- [ ] Land S00 to S05 from changes 006 to 012 (see the inbox on porting their branches).
- [ ] Open S06 to S09 and S11 as changes. Their accepted designs and verification lists are D-22
  to D-25 under the rules of D-27; each slice plan cites them, and S07 and S08 still pass the
  document-format review before building.
- [ ] S11: a fresh agent on Apple silicon macOS reproduces a logo family and platform kit from
  committed sources using only `agd` and `agd-mcp`.

## Checks

Each slice runs its own checks and review. S07 and S08 change the format and need
architecture review (C-09).

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | R-02, R-03, R-08, Q-05 | the acceptance of each child change, 006 to 012 | 0 | NOT_RUN |
| A2 | R-05, R-13, C-09 | the acceptance of the S06 to S09 changes, each architecture-reviewed | 0 | NOT_RUN |
| A3 | J-01, J-03 | integrated logo job on Apple silicon macOS (S11 integrated trial) | 0 | NOT_RUN |

## State

**Step**: approach
**Waiting on**: the owner, on how the in-flight branches land here (inbox)
**Next**: land 006 and 010, the smallest standalone slices
