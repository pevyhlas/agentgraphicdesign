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

## Tasks

- [ ] Port the fix and its regression tests from the pre-public history, where they were
  reviewed.
- [ ] Verify on Linux CI and macOS, and record the acceptance results.

## Checks

P1.B-15 keeps its tests and gains token tests for SVG, typography and raster output; no
format or interface change.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | P1.B-15 | `SubNodeExportTests.ExportNode_ResolvesDocumentTokens`, typography and raster token tests | 0 | NOT_RUN |

## State

**Step**: tasks
**Waiting on**: the owner, on how the in-flight branches land here (inbox)
**Next**: port the reviewed fix
