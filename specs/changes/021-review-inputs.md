# Change: Bound review references and inspected fonts for sheets, analysis and galleries

**Opened**: 2026-10-25 · **Parts**: P1, P4 · **Journeys**: J-01, J-02 · **Tier**: T2 · **Parent**: 012-review-analysis

<!-- Past its budget, a change splits into changes that each ship on their own (Parent: <id>). -->

## Intent

Continue the owner's roadmap after the shared transient builder merged in public PR #13.
Restate parent012/T004–T005 publicly: agents need bounded, consistently inspected document and
font inputs before review sheets, analysis or galleries can reuse the renderer. The owner inbox's
pre-public-port direction is followed; platform qualification remains with change008.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P1 rendering/export | reference reader, inspected font set, aggregate input budget and opaque review colours | D-26 input foundation |
| L2 | P1 asset pipeline | share font-directory inspection without changing its size/refusal policy | no new capability claim |

## Bounds

<!-- From `.prdkit/scripts/bash/bounds.sh <id>`: each constraint, non-goal or quality this change
could break, with how it holds; then "Others in scope: hold". A conflict goes to the owner inbox
with both sides, never resolved here. -->

- C-01/C-03: existing .NET libraries only; no new dependency, copied implementation or font asset.
- C-04: Core owns reference grammar, inspection, limits and colour normalization; surfaces retain
  confinement and their documented not-found mappings.
- C-09: additive internal foundation; vector document shape and existing export behavior hold.
- C-10/C-11 and Q-05: no host paths in new result paths; use the asset-inspection capability gate,
  without admitting another platform. Keep D-26's non-admitted-host deviation explicit.
- Q-01/Q-02: one byte capture supplies the parsed document and hash; ordered font rules and
  independent CSS-byte oracles preserve deterministic behavior and honest diagnostics.
- Q-03: 16 MiB per document, 64 MiB aggregate distinct documents plus counted fonts; at most16
  font directories,256 immediate entries on inspected paths, and8 MiB per counted font.
- Others in scope: hold.

## Approach

Approved by independent architecture/security/test design review at002b6a1 before implementation:

1. Core `AssetReference` has the document arm only. CLI splits at the last `#` only when the
   suffix is exactly a canonical GUID; future MCP references are closed
   `{document,nodeId?,label?}` objects. Native-pixel names, directories and content schema refuse
   with `REFERENCE_KIND_UNSUPPORTED` in that order, before rendering.
2. `IReferenceReader` resolves against an explicit base directory, inspects before opening where
   admitted, reads each distinct document once within16 MiB, hashes those exact bytes, parses and
   returns references plus the inspected font set. Repeated node selections share the document
   bytes; builder-copy ownership remains with child020's sequential rendering contract.
3. Extract `FontDirectoryInspection` from the pipeline's bounded immediate-entry walk. Share
   inspection, preserving caller policy: the pipeline refuses oversize fonts; review inputs skip
   them unopened and uncounted. Inspection is a separate pre-pass, so ordinary export keeps its
   existing linked-font skip behavior. Inspect non-font entries too on admitted platforms.
4. Core `FontDirectoryRules` produces an immutable `InspectedFontSet` containing ordered
   directories and `Load(path,length)` / `Skip(path,reason)` entries. Directory links refuse;
   extension filtering and ordinal candidate order match today's catalog; candidate links skip
   before length inspection on non-admitted hosts, and oversize files skip. Only this routine can
   construct a loadable set. Child PR-2c later makes both catalog loading paths consume it.
5. Count distinct document bytes and loadable font files together, including the non-admitted
   branch. Apply aggregate limits before a renderer/session opens. Keep test budget/gate seams
   internal to test assemblies. Output paths are relative POSIX to the explicit base, or null.
6. Parse review colours once with `SrgbColor`, use banker's byte rounding, require alpha byte255
   and emit six lowercase RGB hex digits. Document colours and legacy sheet backgrounds keep
   their existing semantics. `rgba(37,99,235,0.999)` normalizes to `#2563eb`;0.998 refuses.

Dependency satisfied: child023 merged as e50b1a15cc054359d654a8f692c781d36325d13c ([PR #16](https://github.com/pevyhlas/agentgraphicdesign/pull/16)),
with all required CI, complete native P0 and five owning reviews. This branch incorporates that
public baseline. Bind inspection to `PlatformProfile.Current.GateAdmits(PlatformCapability.AssetPipeline)` and the capability
argument required by `AssetPaths.Inspect`; never duplicate an OS predicate. The tracked Windows
pass-through remains the owner-inbox question; hosted P0 does not qualify macOS inspection.

Alternatives: duplicating current surface readers would repeat limits and safety rules; embedding
new OS tests here would drift from D-28. Prefer the shared reader after its registry prerequisite.

## Tasks

- [x] Incorporate merged change008/PR-1's platform registry and public capability gate.
- [x] Independent architecture/security design review against recovered T004/T005 and D-26.
- [x] T004: references, bounded capture/hash, inspection, font rules and aggregate budget, colours.
- [x] T005: independent grammar/order/boundary/special-file and forced-gate tests.
- [ ] Code/security/test/architecture review, required checks, installed regression evidence and
  exact-head delivery. Render sessions follow in parent012/PR-2c.

## Checks

Preserve existing pipeline and export font tests. FIFO tests use a bounded out-of-process check
or a write-end watchdog; no hanging test can count as refusal. Tests pin handwritten entry order
and byte values, not a comparison with the same rule routine. No public command/tool cutover is
claimed until parent012's surface increment.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | D-26 references; parent AC-03/AC-23 input scope | ReviewInputTests: grammar/native precedence/exact BOM hash/shared capture and original duplicate-key rejection | 1 completed focused run | PASS |
| A2 | Q-05; parent AC-04 input scope | focused reference/font link/FIFO/device refusal, bounded FIFO watchdog, non-admitted skips; AssetPipelineTests; full confinement regressions pending A5 | 1 completed focused run | PASS |
| A3 | Q-03; T005 | exact16MiB and real64MiB plus injected aggregate boundaries, actual growth recheck,256/257 entries, ordered Load/Skip; non-admitted fonts counted | 2 completed focused runs | PASS |
| A4 | Q-01; D-26 colours | handwritten CSS/alpha-byte/ties-to-even vectors in ReviewInputTests | 1 completed focused run | PASS |
| A5 | Delivery | source/installed regression checks, independent lenses and exact-head required CI | 0 | NOT_RUN |

## State

**Step**: source implemented; full verification and independent implementation review
**Waiting on**: A5 source/installed regressions, owning reviews and exact-head required CI.
**Authority**: owner public-roadmap continuation and “and then continue with next steps”; the
constitution permits feature commits, branch publication and reviewed exact-head green merges.
**Attempts**: initial design architecture/security/test PASS, zero findings. Focused build01 stopped
on two test compilation mistakes (long/int count and namespace resolution); corrected build02 and
focused136 tests PASS, zero skips. Run03 adds actual64MiB and mid-capture cancellation/disposal
proof:138 PASS, zero skips. All logs retained; no acceptance failure relabelled.
**Review applicability**: code/security/test/architecture own the implementation. Ops not applicable:
no CI, packaging, dependency or platform-admission change; installed regressions remain required.
Product-Kit 1.2.0 is incorporated from public main `3e2d353` ([uptake #15](https://github.com/pevyhlas/agentgraphicdesign/pull/15));
its required CI/reviews passed. The advisory macOS run retained exactly the prior main's 485
failing test names/outcomes (2,900 passed); that is not qualification or a new acceptance attempt.
The full approved 008/T001–T002 definitions, design/acceptance and original `Core/Platform`
implementation are now recovered in ignored evidence at
`.prdkit/cache/evidence/021-review-inputs/recovered/008-platform-registry/` with source hashes.
Child023 completed source/installed proof at2293234 and full final-head CI/nativeP0 at981bc80;
its selected evidence and reviews remain on PR16. Mac advisory484 failures and earlier transient
cancellation failure remain parent008 observations. This change inherits no qualification claim.
**Next**: finish source/installed regressions and required implementation reviews. Owner2026-10-26 “sounds good, continue with those” confirms this
priority alongside AgentBlender startup reliability; no broader platform/release scope is inferred.
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->

**Review**: round 1 · e50b1a15cc054359d654a8f692c781d36325d13c..002b6a1f74f5b15c91d57fcff8dbaffff861c1c8 · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/14#issuecomment-6428411034
**Review**: round 1 · e50b1a15cc054359d654a8f692c781d36325d13c..002b6a1f74f5b15c91d57fcff8dbaffff861c1c8 · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/14#issuecomment-6428411427
**Review**: round 1 · e50b1a15cc054359d654a8f692c781d36325d13c..002b6a1f74f5b15c91d57fcff8dbaffff861c1c8 · agd-test-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/14#issuecomment-6428411821
