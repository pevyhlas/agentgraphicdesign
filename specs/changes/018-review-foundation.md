# Change: Port raw RGBA rendering and legacy contact-sheet characterization for review analysis

**Opened**: 2026-10-25 · **Parts**: P1, P4 · **Journeys**: J-01, J-02 · **Tier**: T2 · **Parent**: 012-review-analysis

<!-- Past its budget, a change splits into changes that each ship on their own (Parent: <id>). -->

## Intent

Port the independently reviewed first increment of review analysis onto public main. Agents
need raw render pixels for upcoming comparison sheets and legibility metrics; maintainers need
a pre-refactor byte baseline for existing sheets. The pre-public-port inbox item is folded here
for 012/T001–T003 only. Change 010 already landed publicly; it is no longer a dependency blocker.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P1 rendering/export | Internal straight RGBA buffer contract and bounded access | D-26 |
| L2 | P4 | Existing export formats still exclude raw buffers | No new command or MCP tool |

## Bounds

<!-- From `.prdkit/scripts/bash/bounds.sh <id>`: each constraint, non-goal or quality this change
could break, with how it holds; then "Others in scope: hold". A conflict goes to the owner inbox
with both sides, never resolved here. -->

- C-01, C-03, C-04: holds: existing permissive .NET/Skia dependencies and one renderer.
- C-09: holds: no document-format change; raw RGBA is an in-process buffer, not a file format.
- Q-01, Q-02: holds: pre-change sheet bytes are pinned; unpremultiplication is exhaustively
  characterized rather than inferred from an arithmetic approximation.
- Q-03: holds: existing 8192-side / 64-million-pixel render limits also constrain the buffer.
- Q-05: holds: Linux x64 evidence is rerun; historical macOS evidence does not qualify macOS.
- Others in scope: hold.

## Approach

Reapply the existing D-26 design and reviewed implementation rather than invent another pixel
API. Recover the archived review/evidence under the ignored evidence cache; publish new review
reports and current CI evidence on this PR. Readback uses Skia's authoritative unpremultiply.
Encoded PNG/JPEG/WebP paths use explicit cases and cannot silently encode the raw member.

## Tasks

- [x] Recover pre-public design, task text and four implementation reviews.
- [x] T001: pin PNG hashes/cell maps, CLI JSON and MCP results for labels on/off, 10×3 in
  128-pixel cells and 100×30 in 64-pixel cells, translucent edges, custom gap/background,
  257-reference refusals, and directory-font sheets with a sensitivity control.
- [x] T002: port `RenderFormat.Rgba8`, bounded Core `RgbaImage`, straight readback and explicit
  format maps; no user-facing raw export.
- [x] T003: characterize all 32,896 valid premultiplied alpha/channel pairs against PNG decode
  and the closed-form candidate; record the matching conversion rule and apply D-26's fallback
  if they differ. Test hand-derived pixels, format dispatch and refusals with no output writes.
- [ ] Rerun baseline/current checks, independent reviews, hosted CI and authorized delivery.

## Checks

Run the recovered characterization against public-main sources before the renderer edit and
against the port. Run full source tests, format, policy gates and installed-package smoke through
current CI. Code, security, tests, architecture and operations reviews are required; round-1
test finding M1 adds the new CLI sheet/refusal cases to exact installed-package verification.

Restated criteria: **012/AC-01** requires `width*height*4` straight RGBA bytes for opaque and
half-transparent rectangles, exact at alpha 0/255 and channels fixed by the characterized rule,
within one otherwise, and equal to managed PNG decode on identically converting pairs.
**012/AC-02** refuses raw files on all existing export/manifest/sheet surfaces and never encodes
`Rgba8` as PNG. Future gallery refusals remain in 012. **012/AC-09** and **012/AC-25** receive
their pre-change legacy-grid and directory-font baselines here; their compositor/session
acceptance remains open in 012.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | Q-01, Q-02; 012/AC-09, AC-25 baseline | Public-main versus port legacy sheets, CLI and MCP fixtures | 1 | PASS on Linux x64: `baseline-tests.log`, `focused-tests.log` in evidence cache |
| A2 | P1, Q-03; 012/AC-01 | Raw buffer contract, hand-derived pixels, exhaustive conversion characterization | 1 | PASS on Linux x64: 32,896 pairs, zero rule/PNG mismatches; `characterization.log` and `current/rgba8-unpremultiply.json` |
| A3 | P4; 012/AC-02 | Explicit encoder cases and CLI/MCP/manifest raw refusals | 1 | PASS: 42 focused cases across four assemblies, `focused-tests.log` |
| A4 | Q-01, Q-02 | Full suite, format/policy/package gates and exact-head CI | 2 | RUNNING; build, adoption, doctor and dependencies pass |

## State

**Step**: implementation
**Waiting on**: nothing
**Next**: independent reviews and exact-head CI; full local suite still running.
**Branch**: `018-review-foundation`; PR pending.
**Authority**: owner requested “please continue on next capabilities” (2026-10-25); constitution
authorizes feature commits, push/PR and merge after exact-head CI/reviews. Release remains owner-only.
**Evidence**: `.prdkit/cache/evidence/018-review-foundation/`; pre-public design closure and four
implementation verdicts recovered from archived revision `95c678808019fb30b61858fbfbc957da8397fdcf`.
**Attempts**: A1–A3 1 PASS; A4 attempt 1 stopped before tests on a CLR startup fault (exit 134),
attempt 2 PASS with worker reuse disabled and serial project scheduling: 3,357 tests, zero
failures/skips; format also PASS. Failed log retained as `tests.log`; retry output
`tests-attempt-2.log`. Review fix round 1 adds installed sheet/refusal coverage (M1), recording
without bypassing golden comparison (L1/L01), and odd/thin/boundary renders (L2). No holds.
**Review**: round 1 · 8d5cf0e91c1bde74e9c560a65b8f749a696213cc..e32d096782cf54bc85bc6e94b958a99b5ce8530d · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/018-review-foundation/reviews/round-1/agd-code-reviewer.md
**Review**: round 1 · 8d5cf0e91c1bde74e9c560a65b8f749a696213cc..e32d096782cf54bc85bc6e94b958a99b5ce8530d · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/018-review-foundation/reviews/round-1/agd-security-reviewer.md
**Review**: round 1 · 8d5cf0e91c1bde74e9c560a65b8f749a696213cc..e32d096782cf54bc85bc6e94b958a99b5ce8530d · agd-test-reviewer · FAIL · 0 critical, 0 high, 1 medium, 2 low · .prdkit/cache/evidence/018-review-foundation/reviews/round-1/agd-test-reviewer.md
**Review**: round 1 · 8d5cf0e91c1bde74e9c560a65b8f749a696213cc..e32d096782cf54bc85bc6e94b958a99b5ce8530d · agd-architecture-reviewer · PASS · 0 critical, 0 high, 0 medium, 1 low · .prdkit/cache/evidence/018-review-foundation/reviews/round-1/agd-architecture-reviewer.md
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->
