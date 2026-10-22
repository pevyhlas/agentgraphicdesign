# Change: review sheets, static gallery and legibility analysis

**Opened**: 2026-10-20 · **Parts**: P1, P4 · **Journeys**: J-01, J-02 · **Tier**: T3 · **Parent**: 013-brand-workflow-uptake

## Intent

Let agents and maintainers review small artwork under controlled conditions and with numbers:
comparison sheets of assets by conditions (size, background, integer nearest zoom, labels), a
static gallery a command regenerates and a browser reloads from `file://` (no server, no
watching), and legibility analysis (coverage, anti-aliasing-robust contrast, topology against a
large reference, minimum stroke and gap widths in output pixels) with configurable findings.
Slice S05 of change 013; implements the change 007 review design and depends on 007 and 010,
whose token copy its shared transient-document builder takes over.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P1 | new rendering and analysis behaviors; a shared transient-document builder | |
| L2 | P4 | `contact-sheet` conditions, `agd analyze`, `agd gallery write` and MCP tools | parity |

## Bounds

- Q-01: holds per platform: raster outputs and analysis numbers are reproducible per platform
  for the recorded font policy.
- Q-03: holds: a counted analysis budget; the gallery refuses unowned files.
- Others in scope: hold.

## Approach

Working assumption, to be confirmed in the design review (not an owner decision): on hosts where reference inspection is
not admitted, sheets and analysis keep today's read paths as a tracked deviation, and
`gallery write` refuses.

## Tasks

- [ ] Port the first increment from the pre-public history, where it was implemented and
  reviewed: raw RGBA rendering and a characterization of the current grid.
- [ ] S05a: reference union, render sessions, sheet conditions and compositor, CLI and MCP
  surfaces, contrast, `agd analyze`, the gallery model and writer, browser evidence,
  documentation and smoke.
- [ ] S05b: an operation counter, supersampled widths and topology, budgets, documentation and
  macOS runs.

## Checks

Raster and analysis numbers are reproducible per platform (Q-01); a counted budget bounds the
analysis (Q-03); new commands and tools are registered in the surface manifest (P4.B-01).

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | J-02 | labelled light and dark, native and zoomed comparison sheets | 0 | NOT_RUN |
| A2 | J-02 | a regenerable static gallery | 0 | NOT_RUN |
| A3 | Q-03 | legibility measurements within their budget | 0 | NOT_RUN |

## State

**Step**: tasks
**Waiting on**: 010; the owner, on porting (inbox)
**Next**: port the first increment
