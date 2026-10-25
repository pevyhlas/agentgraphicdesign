# Change: agent tool usability defects

**Opened**: 2026-10-19 · **Parts**: P1, P4 · **Journeys**: J-01, J-05 · **Tier**: T2 · **Parent**: 013-brand-workflow-uptake

## Intent

Two defects made AGD unusable for an agent in a consumer project's session: `agd-mcp`
advertised a boolean JSON Schema for one tool parameter, which the TypeScript MCP SDK rejects,
hiding all tools; and the pure path geometry commands created and failed on a document store.
After this change any spec-conformant MCP client gets every tool, path geometry runs anywhere,
and a real store conflict names the other store. Slice S01 of change 013.

Inbox: this change ports one in-flight branch as its own pull request, the recommended option
of "How in-flight pre-public work lands here"; that item stays open for the other changes.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P4.B-07 | add: every MCP tool input schema and nested schema is an object (boolean `additionalProperties` allowed) | test over all tools |
| L2 | P1.B-26 | add: path geometry commands are store-free | |
| L2 | P1.B-27 | add: overlapping stores fail with `STORE_CONFLICT`, naming the other store, creating nothing | declared error-code cutover from `INVALID_PROPERTY` |
| L2 | P4 failure semantics | add: a CLI command that fails while it is constructed reports its own code with exit 1 | declared exit-code cutover from 4 |
| L2 | P4 left open | add: whether the other non-persisting file commands move to the store-free engine | review finding, not in scope |
| L0 | J-05 | remove the variation "An MCP client rejects one tool's schema" | lands with the schema fix |
| L1 | interfaces | MCP clients row: drop the `import_pixel_layer` case; keep the general failure mode | lands with the schema fix |

## Bounds

- C-04: holds: fixes stay in the adapters and shared engine composition.
- C-09: holds: the error-code and exit-code changes are declared cutovers in the changelog.
- Q-05: holds: no platform claim changes; macOS store paths stay with change 008.
- Others in scope: hold.

## Approach

- **Schema**: `[Description]` on both `import_pixel_layer` parameters, so the C# SDK emits
  `{"description": ...}` instead of `true` for the `JsonElement` request (as `edit_pixel_asset`
  already did). Closed request objects stay validated by Core; typing tool arguments beyond
  schema objects was deferred.
- **Schema test**: a standalone protocol test walks every tool's `properties`, `$defs`,
  `definitions`, `patternProperties`, `anyOf`, `oneOf`, `allOf`, `prefixItems`, `items`, `not`,
  `if`/`then`/`else`, `contains`, `propertyNames` and object-valued `additionalProperties`; a
  boolean `additionalProperties` stays legal. It asserts the tool count against
  `docs/surface-manifest.json` and pins `import_pixel_layer.request` as an object, so it cannot
  pass vacuously; a walker self-test proves the detector fires. The installed-package
  verification (`scripts/verify-packages.sh`) also runs it.
- **Path commands**: inject the keyed file-only engine (`Program.FileEngineServiceKey`), the
  composition `validate` and the asset pipeline already use. MCP `path_*` tools keep the
  server's store-backed engine, which does no store access in the operation; the parity table
  says so.
- **Store conflict**: a distinct `STORE_CONFLICT` code instead of `INVALID_PROPERTY`. The
  enclosing case names the enclosing root at the end of the message; the contained case names
  the inner store's directory relative to the directory holding the new store, so it survives
  surface path sanitization. The ancestor check runs before the store directory is created, so
  a refused store leaves no `./.agd`.
- **CLI construction errors**: `HandleException` unwraps a `DocumentEngineException`,
  `UnauthorizedAccessException` or `IOException` from Spectre's `CommandRuntimeException`, so
  such failures report their own code with exit 1, not `INVALID_ARGUMENTS: Could not resolve
  type` with exit 4.
- **Deferred**: the unanchored Core path pattern in `ErrorSanitizer` garbles `/private/...`
  roots and relative locations containing `tmp`, `var` and similar segments; it moves to change
  008's error-path redactor. Moving the other non-persisting file commands to the store-free
  engine is left open in P4.

### Pre-public review

The work was implemented and reviewed in the pre-public history before this repository
started, at full rigor:

- Round 1: code FAIL (one medium: a contained store was reported by its lock-file name) and
  tests FAIL (two medium: the schema check sat behind platform-gated assertions and could pass
  vacuously; the conflict location and the contain direction were untested). All were fixed,
  with the low findings on residue, exit-code pinning, walker coverage and the read-only
  directory.
- Round 2: code, tests and architecture PASS, low advisories only; the advisories on the
  changelog section, the parity rows, the schema rule's wording and the package verification
  filter were applied, and the rest are listed under accepted advisories.
- Round 3, a delta review of those advisory commits (code, tests, operations): PASS, one low
  wording advisory, applied in P4.B-07.
- Accepted advisories: no MCP-level `STORE_CONFLICT` test (the code path is the shared
  `ToolResult` mapping); the read-only directory test returns early under a root runner, so its
  behavioral evidence is a non-root run; the `IOException` unwrap branch has no dedicated test;
  the contained-store message carries no MCP-specific anchor wording; the guide does not name
  the MCP remedy for a store conflict; the contained case does not assert that no lock file is
  created.

This port applies the reviewed source and tests unchanged; only the changelog placement and the
spec text differ.

## Tasks

- [x] Port the schema, store-free and store-conflict fixes with their tests from the pre-public
  history, where they were reviewed.
- [x] Fold the spec text into P1, P4, interfaces and J-05; carry the sanitizer deferral to 008.
- [x] Verify on Linux CI at the pull request head, and record the acceptance results. macOS
  verification is the local non-root arm64 run, which is also the behavioral evidence for the
  read-only directory test. The live `tools/list` probe of the source plan is replaced by the
  protocol test, which asserts the tool count against the surface manifest.
- [x] Fix the Coverage Gate download race found on the first CI run: parallel test jobs
  uploaded identically named collector copies (same runner host name and start second), and
  `merge-multiple` wrote them into one path concurrently, corrupting one XML file. Each
  artifact now downloads into its own subdirectory; the gate's merge is a union over source
  lines, so the duplicate copies do not change the result.
- [ ] Independent review of the port in this repository.

## Checks

Each new behavior passes the line test: a real client hid every tool over one boolean schema,
and a pure command that created a store broke path geometry. The new P4 and P1 behaviors
get the tests in Acceptance; the error-code and exit-code changes are declared in the
changelog (C-09).

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | P4.B-07 | `ProtocolTests.ToolsList_AllInputSchemas_HaveNoBooleanSubschemas` and `BooleanSubschemaWalker_RejectsNestedBooleanSchemas`; public PR #5 Linux CI; earlier macOS arm64 evidence retained | 1 | PASS |
| A2 | P1.B-26 | CLI `PathCommandTests.PathCommands_AreStoreFree_InsideAnExistingStoreRoot`; public PR #5 Linux CI | 1 | PASS |
| A3 | P1.B-27, P4 failure semantics | CLI `PathCommandTests.StoreBackedCommand_*` and Core `FileDocumentStoreHardeningTests.Constructor_{RejectsNestedStoreRoot, RejectsStoreContainingAnExistingStore, RejectedNestedStore_LeavesNoDirectory}`; public PR #5 Linux CI | 1 | PASS |

## State

**Public-state reconciliation (2026-10-24)**: PR [#5](https://github.com/pevyhlas/agentgraphicdesign/pull/5)
merged at b73240715ebbf72a0682ea423fbcb2035898d659 on 2026-10-22. All eight required Linux
checks passed on eb53414fbf349af97561db8daf1ca91eb094d761
([run](https://github.com/pevyhlas/agentgraphicdesign/actions/runs/39866542757)); informational
macOS was skipped. Code is already delivered; do not port it again. Locate the independent
public-port review evidence before closing the remaining review task.

**Step**: verify
**Waiting on**: the public-port independent review record, not CI or code implementation.
**Delivered**: public PR #5; its source branch has already merged.
**Next**: recover the remaining review evidence, then close with the log line.
