# Change: call-scoped review render sessions and inspected font catalog loading

**Opened**: 2026-10-28 · **Parts**: P1 · **Journeys**: J-01, J-02 · **Tier**: T2 · **Parent**: 012-review-analysis

## Intent

Deliver012/PR-2c (T034–T035) after021's immutable inspected inputs and023's shared platform
registry. The owner asked to continue both projects after the render-session recommendation.
One session loads exactly the inspected font entries once, rather than multiplying font IO by
every review render. Public sheets, analysis and gallery integration remain later012 increments.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P1.B-16 | mark render-session/catalog foundation delivered; retain later surface markers | D-26,012/D32 |
| L1 | D-26 | restate accepted session ownership and per-render policy contract | recovered012 plan, no new semantics |

## Bounds

- C-01: no new dependency or external asset; tests reuse existing licensed fonts and an original
  generated test font where needed for a directory-only family.
- C-03, C-04: Core/Skia C# implementation, shared engine; no surface-only logic.
- C-09: additive renderer interface member with explicit fail-closed default; no stored-format change.
- C-10: raw evidence is ignored or on the PR; no private paths in tracked summaries.
- C-11: ordinary export keeps its current unguarded path; sessions consume already-inspected sets,
  preserving the platform-gate deviation and no new admission.
- Q-01, Q-02: same face order, matching, diagnostics and raster bytes under each per-render policy.
- Q-03: one catalog per session, ≤4096 bounded candidates; cancellation and deterministic ownership.
- Others in scope: hold.

## Approach

Baselinef18d3ba8a25266fa4f6dcfeb6d5ae6f9ccfa755a. Binding D-26/012/D32; T034/T035 and closure
IN-1/IN-4, RC-01–05 recovered from021's retained pre-public plan/tasks. Existing inputs and
registry prerequisites are merged. Independent review confirms this bounded design before code.

- Add `IRenderer.OpenSession(InspectedFontSet, CancellationToken)` and disposable `IRenderSession`
  with `Render(Document, RenderOptions, CancellationToken)`. Default accepts only zero entries,
  forwards to the renderer and rejects other sets with `NotSupportedException`.
- Skia owns one catalog per session; each render creates/disposes its own borrowing shaper, cache
  and diagnostics. Session dispose releases the catalog on caller success/failure/cancellation;
  partial construction cleans loaded native faces. Calls are sequential and session-local.
- `FromInspected` opens only Load entries in order, checks cancellation per file, rechecks final
  reparse state before load, emits Skip/unreadable diagnostics in entry order under today's cap,
  and never enumerates. Late additions are ignored. This is not same-UID TOCTOU protection.
- Ordinary `Load` uses shared `FontDirectoryRules` without the admitted-file pre-pass, then the
  same loading routine; export semantics and legacy sheet bytes stay unchanged. Internal Core
  friend access for Skia is restricted to the assembly, avoiding a public forgeable font set.
- Non-empty session `RenderOptions.FontDirectories` throws `ArgumentException`; SystemFonts and
  StrictFonts apply per render. Cancellation at open and render, disposal and later use are explicit.
- Test-only injected loaders/counters are internal instance seams, never global mutable switches.
  Public command integration and actual sheet/analysis/gallery work-bound fixtures stay later012.

## Tasks

- [x] T001 Confirm recovered contract with independent architecture/security design review.
- [ ] T002 Implement012/T034 Core session API, Skia session/catalog and borrowing shapers.
- [ ] T003 Implement012/T035: order/one-load counters, late file, cancellation/partial disposal,
  sequential reuse, default fallback, option refusal, forced non-admitted skips and diagnostics.
  Compare directory-only family and Inter directory face across three sizes and region-only input
  against ordinary rendering, including empty-set sensitivity; retain legacy sheet goldens.
- [ ] T004 Source/clean-archive/installed regression and required CI; four owning review lenses;
  reconcile012 State/Next in this PR and finalize after gates.

## Checks

Every edit implements the accepted D-26 contract. No new public operation, dependency, platform
qualification or native format. Release/operations lens not applicable: packaging, CI and supported
platforms are unchanged; ordinary installed rendering is regression evidence only.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | P1.B-16, Q-03 | T034/T035 session ownership, loading/order/cancellation and API defaults | 0 | NOT_RUN |
| A2 | Q-01, Q-02 | T035 differential font use/diagnostics, three sizes, region input, legacy goldens | 0 | NOT_RUN |
| A3 | C-11, Q-03 | admitted inputs plus forced non-admitted skip/boundary and reparse recheck tests | 0 | NOT_RUN |
| A4 | P1 rendering | source/archive and installed regression; required CI, adoption, doctor, reviews | 0 | NOT_RUN |

## State

**Step**: implementation
**Branch**: `025-render-session`; PR not opened yet.
**Authority**: owner continuation and constitution's standing commit/push/PR/green-reviewed-merge
policy. No release or platform-qualification authorization is inferred.
**Design review**: architecture/security PASS0 at4fd790a after supplying the missing revision-bound
  bounds capture; initial pending reports retained. No design/source correction was needed.
**Attempts**: baseline133 tests PASS. Broad macOS advisory failures and008 qualification stay open.
**Next**: implementation and evidence, then review/delivery.
