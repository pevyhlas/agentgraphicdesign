# Change: review sheets, static gallery and legibility analysis

**Opened**: 2026-10-20 · **Parts**: P1, P4 · **Journeys**: J-01, J-02 · **Tier**: T3 · **Parent**: 013-brand-workflow-uptake

## Intent

Let agents and maintainers review small artwork under controlled conditions and with numbers:
comparison sheets of assets by conditions (size, background, integer nearest zoom, labels), a
static gallery a command regenerates and a browser reloads from `file://` (no server, no
watching), and legibility analysis (coverage, anti-aliasing-robust contrast, topology against a
large reference, minimum stroke and gap widths in output pixels) with configurable findings. A
consumer project did this with image scripts, a restarted local web server and judgement by eye.
Slice S05 of change 013; implements D-26 and depends on 007 and 010, whose token copy its shared
transient-document builder takes over.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P1 | `Rgba8` and `RgbaImage`; the transient-document builder; sheet, analysis and gallery behaviors | |
| L2 | P1.B-15 | sub-node export goes through the builder (behavior unchanged, pinned by a pre-refactor characterization) | |
| L2 | P4 | `contact-sheet` conditions, `agd analyze`, `agd gallery write`; MCP `render_contact_sheet` takes `refs` (declared cutover from `paths`), `analyze_legibility`, `write_gallery` | parity |

## Bounds

- Q-01: holds per platform: raster outputs and analysis numbers are reproducible per platform for
  the recorded font policy; layouts, cell maps and the gallery HTML without analysis are identical
  across platforms.
- Q-03: holds: a counted analysis budget, a 64 MiB aggregate input bound, one font load per call;
  the gallery refuses unowned files.
- Q-02: holds: legacy contact sheets keep their bytes; their inputs narrow only where a security
  bound needs it, declared in the changelog.
- C-03: holds: the review boards and preview server become .NET commands.
- Others in scope: hold.

## Approach

D-26 holds the contract (its refinements 1 to 16 confirmed by this change's design review).
Choices made here (IDs as in the pre-public plan): 012/D1 S05a introduces the builder; 012/D2 one
compositor with a legacy and a matrix tile recipe (012/D3), so the legacy grid keeps its bytes;
012/D4 ground semantics; 012/D5 MCP `refs`; 012/D6 a 2048 px inline cap; 012/D7 shorthand or an
explicit condition list; 012/D8 ownership, paint order, numbers, percentile, rounding and opt-in pair
contrast; 012/D9 opaque analysis grounds; 012/D10 opt-in safe zone; 012/D11 the literal contrast
table; 012/D12 `--refs-per-sheet`; 012/D13 one shared gallery budget; 012/D14 the two-phase index;
012/D15 leaf-only directory creation; 012/D16 the asset-pipeline platform capability; 012/D17 no
link-count probe yet; 012/D18 ink crop for node references; 012/D19 families arrive with S08;
012/D20 reference size; 012/D21 vector references only; 012/D22 inspection before opening;
012/D23 the gallery lock; 012/D24 no system fonts by default in analysis and galleries; 012/D25
relative paths in outputs; 012/D26 one copy per reference and the aggregate bound; 012/D27 region
colour on exclusive pixels; 012/D28 identity `(algorithm, checks)`; 012/D29 the legacy 256-reference
cap; 012/D30 font-directory inspection; 012/D31 an opaque matrix sheet background; 012/D32 one
render session per call; 012/D33 colour inputs normalised.

On hosts where inspection is not admitted (macOS before change 008), sheets and analysis keep
today's read paths as a tracked deviation and `gallery write` refuses: the lead confirmed this with
the owner (option A of 012/OD-S05-1), removed by the change 008 increment that admits inspection.

## Tasks

- [x] PR-1: 012/T001 legacy grid characterization goldens; T002 `Rgba8` and `RgbaImage`; T003 the
  unpremultiply characterization (child 018-review-foundation, [public PR #11](https://github.com/pevyhlas/agentgraphicdesign/pull/11)).
- [ ] PR-2a (change 010 already merged): T029 pre-refactor sub-node characterization; T032 the builder
  replacing the sub-node copy; T033 its tests. PR-2b: T004 references, inspection, the inspected
  font set, the aggregate bound and the colour parser; T005 their tests. PR-2c: T034 the render
  session; T035 its tests.
- [ ] Shared transient-document builder (with T032): thread the render font policy (`--font-dir`,
  `--no-system-fonts`) into the sub-node bounds probe so the crop matches the shaped text, and
  surface the probe's bounds diagnostics (such as an unresolved typography token) on sub-node
  export. Both are pre-existing gaps found in the change 010 review and handed to this change.
- [ ] PR-3: T006 matrix layout and limits; T007 one compositor; T008 sheet tests. PR-4: T009 CLI
  flags and the MCP cutover; T010 surface tests.
- [ ] PR-5: T011 contrast table and the analysis frame; T012 analysis fixtures and the work-bound
  fixture. PR-6: T013 `agd analyze` and `analyze_legibility`; T014 their tests.
- [ ] PR-7: T015 gallery model and HTML; T016 strict-parse corpus. PR-8a: T017 `GalleryWriter`;
  T018 its tests. PR-8b: T030 surfaces; T031 surface tests and the gallery work-bound fixture.
  PR-8c: T020 docs; T021 installed smoke. Follow-up: T019 browser evidence.
- [ ] S05b: PR-9 T022 topology and correspondence, T023 tests; PR-10 T024 widths and gaps, T025
  brute-force references; PR-11 T026 budget prefix tests, T027 docs, T028 installed smoke and macOS
  runs (removing the deviation).

Work-bound evidence (a tagged evidence job, a child process measured): the worst legal S05a
analysis plan within 60 s and a 64-reference gallery at the 64 MiB aggregate with analysis within
180 s, each at most 2 GiB peak, also with the worst legal font input; if one misses, the aggregate
(then the gallery reference count, or for fonts the counted font files) is halved and recorded.

Implementation-review checks: 012/IN-1 font paths and the reparse re-check (PR-2c); IN-2 the
anti-aliased badge margin is measured and recorded (PR-5); IN-3 colour rounding pinned (PR-2b);
IN-4 test-only seams (PR-2b, PR-2c); 007/C-08 and 007/R-06 at PR-8a; the minimal test font
accepted by Skia, else a recorded committed OFL fallback (PR-5). Closure checks: 012/RC-01 font
entry inspection as a pre-pass so `export --font-dir` keeps today's behavior; RC-02 skip
diagnostics in order under today's cap; RC-03 no bypass of the rule routine; RC-04 the forced-gate
assertions split across PR-2b and PR-2c; RC-05 weight 400 in the font anchors; RC-06 badge fixture
geometry; RC-07 AC-10 wording; RC-08 the guide keeps stating the deviation's residual risk.

Recorded follow-ups outside this change: route `export`, `export_document` and `render_preview`
reads through the same reference reader; one render session per asset-pipeline build. The renderer's
CSS alpha-hex defect was fixed and characterized by change 016 (public PR #9).

## Checks

Raster and analysis numbers are reproducible per platform (Q-01); a counted budget bounds the
analysis (Q-03); new commands and tools are registered in the surface manifest (P4.B-01).

## Acceptance

Design acceptance criteria 012/AC-01 to 012/AC-25 group as below. D-26 holds the binding contract;
each increment restates the criteria and tasks it closes publicly in its change record and PR.
Child 018 restates T001–T003, AC-01/AC-02 and the AC-09/AC-25 baseline scope. Remaining full
criterion and task text is recovered from pre-public history before its increment starts.

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | J-02 | AC-01 to AC-09, AC-23, AC-25: raw RGBA, references, labelled light and dark, native and zoomed matrix sheets, legacy bytes unchanged, one font load per call | 0 | NOT_RUN |
| A2 | J-02 | AC-16 to AC-20, AC-22, AC-24: a regenerable static gallery, hostile-index and concurrency safety, browser evidence | 0 | NOT_RUN |
| A3 | Q-03 | AC-10 to AC-15: contrast, safe zone, topology and widths within their budget, deterministic `incomplete` | 0 | NOT_RUN |
| A4 | P4.B-01 | AC-21: parity, the object-schema gate and capabilities | 0 | NOT_RUN |

## State

**Step**: tasks
**Pre-public review**: design approved at closure pass 1 (architecture, security, test); its
remaining criterion/task text is recovered before each increment. PR-1's public criteria,
source/installed verification and five independent review verdicts are recorded in PR #11.
**Waiting on**: nothing for PR-2a once PR #11 merges; change 010 is already merged
**Next**: PR-2a, shared transient-document builder and sub-node characterization
