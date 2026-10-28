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
- [x] T002 Implement012/T034 Core session API, Skia session/catalog and borrowing shapers.
- [x] T003 Implement012/T035: order/one-load counters, late file, cancellation/partial disposal,
  sequential reuse, default fallback, option refusal, forced non-admitted skips and diagnostics.
  Compare directory-only family and Inter directory face across three sizes and region-only input
  against ordinary rendering, including empty-set sensitivity; retain legacy sheet goldens.
- [x] T004 Source/clean-archive/installed regression and required source CI; four owning review lenses;
  reconcile012 State/Next in this PR and finalize after gates.

## Checks

Every edit implements the accepted D-26 contract. No new public operation, dependency, platform
qualification or native format. Release/operations lens not applicable: packaging, CI and supported
platforms are unchanged; ordinary installed rendering is regression evidence only.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | P1.B-16, Q-03 | T034/T035 session ownership, loading/order/cancellation and API defaults; focused-04 below | 0 production corrections | PASS |
| A2 | Q-01, Q-02 | T035 differential font use/diagnostics, three sizes, region input; focused-04 and unchanged legacy goldens | 2 differential fixture corrections | PASS |
| A3 | C-11, Q-03 | admitted inputs plus forced non-admitted skip/boundary and reparse recheck; focused-04 | 0 | PASS |
| A4 | P1 rendering | source379323f: archive3510PASS, installed106PASS, requiredCI40669578222 and local gates PASS; final-head CI/review remain merge gates | 1 archive fixture correction | PASS |

## State

Evidence: `.prdkit/cache/evidence/025-render-session/verification/`; focused-04 has13PASS/0FAIL,
including original-font/Inter pixel sensitivity and strict-policy isolation. No usable host font
was exposed locally: SystemFonts toggles prove ordinary-render fallback parity here; actual system
resolution is asserted on hosts exposing a usable family. This is not platform qualification.

**Step**: source acceptance and all owning reviews PASS; exact final-head CI remains the merge gate
**Branch**: `025-render-session`; [PR18](https://github.com/pevyhlas/agentgraphicdesign/pull/18), draft.
**Authority**: owner continuation and constitution's standing commit/push/PR/green-reviewed-merge
policy. No release or platform-qualification authorization is inferred.
**Design review**: architecture/security PASS0 at4fd790a after supplying the missing revision-bound
  bounds capture; initial pending reports retained. No design/source correction was needed.
**Attempts**: baseline133PASS; initial focused142PASS/2fixtureFAIL (missing document ID); corrected
  full source and frozen0a0bfb1 archive each3507PASS/3fixtureFAIL (canvas still asks measurement for
  diagnostics; system font manager exposed no usable family). focused-03 then12PASS/1FAIL narrowed
  the host-font assumption; focused-04 all13PASS after making that limitation explicit while
  retaining policy parity and available-host assertions. No production correction. All logs retained.
  Corrected frozen archive at379323f:3510PASS/0FAIL/0SKIP; installed CLI66/MCP40PASS; exact-package
  smoke exit0. Required CI40669578222 PASS; broad macOS advisory FAIL retained. Package hashes,
  provenance, completed command and logs: verification/completion.md and PR evidence comment below.
  Broad macOS advisory failures and008 qualification stay open.
**Implementation review**: code/security PASS0 and architecture PASS (one low provenance typo)
  atf50bac5; scoped architecture PASS0 atef18d93 corrects it. Test review PASS0 after missing
  completed package evidence supplied same-round; initial pending report retained. Only parent013 changed
  since verified379323f; no runtime/test/package change.
**Next**: finalize this execution record, exact-head gates and authorized merge.

Evidence completion: https://github.com/pevyhlas/agentgraphicdesign/pull/18#issuecomment-6466405677

**Review**: round 2 · f18d3ba8a25266fa4f6dcfeb6d5ae6f9ccfa755a..f50bac5539747a3c5a324a0d54ddee9a8e3f09b6 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/18#issuecomment-6466297243
**Review**: round 2 · f18d3ba8a25266fa4f6dcfeb6d5ae6f9ccfa755a..f50bac5539747a3c5a324a0d54ddee9a8e3f09b6 · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/18#issuecomment-6466297775
**Review**: round 2 · f18d3ba8a25266fa4f6dcfeb6d5ae6f9ccfa755a..f50bac5539747a3c5a324a0d54ddee9a8e3f09b6 · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 1 low · https://github.com/pevyhlas/agentgraphicdesign/pull/18#issuecomment-6466307500
**Review**: round 3 · f50bac5539747a3c5a324a0d54ddee9a8e3f09b6..ef18d93ee4d41f388111ff8c0e38d538961bf67f · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/18#issuecomment-6466358375
**Review**: round 2 · f18d3ba8a25266fa4f6dcfeb6d5ae6f9ccfa755a..f50bac5539747a3c5a324a0d54ddee9a8e3f09b6 · agd-test-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/18#issuecomment-6466451850
