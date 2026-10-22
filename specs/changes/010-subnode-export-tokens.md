# Change: sub-node export keeps document design tokens

**Opened**: 2026-10-20 · **Parts**: P1 · **Journeys**: J-01 · **Tier**: T1 · **Parent**: 013-brand-workflow-uptake

## Intent

Sub-node export (D-10) built its transient document without the source document's tokens, so a
node whose paint referenced a token exported with unresolved paint: silently wrong colours for
an agent exporting one icon from a shared brand document (Q-02). Found in the change 007
design review. It stands alone and must land before S05 (change 012, whose shared
transient-document builder takes over this token copy), S06 and S08 of change 013.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P1.B-15 | extend: sub-node exports resolve token references exactly as the whole document does | |

## Bounds

- Q-02: holds: the fix removes a silent loss.
- Others in scope: hold.

## Approach

Copy the document's tokens into both transient documents (the bounds probe and the export); the
raster path shares it.

Out of scope: two pre-existing gaps in the same builder, found in review, move to change 012's
shared transient-document builder: the bounds probe ignores the render font policy, and its
bounds diagnostics are not surfaced on sub-node export.

## Tasks

- [x] Port the fix and its regression tests from the pre-public history, where they were
  reviewed.
- [ ] Verify on Linux CI, and record the acceptance result.
- [x] Verify on macOS: the three token tests pass with the fix and fail without it; the full
  suite's failing set equals the `origin/main` baseline (known macOS platform failures).

## Checks

P1.B-15 keeps its tests and gains token tests for SVG, typography and raster output; no
format or interface change.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | P1.B-15 | `SubNodeExportTests.ExportNode_ResolvesDocumentTokens`, `SubNodeRenderTests.RenderNode_ResolvesColorTokens`, `SubNodeRenderTests.RenderNode_TypographyTokenSizesTheCrop` | 1 | PENDING: macOS arm64 passes, each fails on the base; Linux CI on the PR is authoritative |

## State

**Step**: review
**Branch**: `fix/010-subnode-export-tokens`, PR against `main`
**Waiting on**: Linux CI on the PR, then the lead's merge
**Authority**: the lead merges; this change does not
**Next**: record the Linux CI result in Acceptance A1, then close
