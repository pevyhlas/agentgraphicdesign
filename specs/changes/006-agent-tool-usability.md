# Change: agent tool usability defects

**Opened**: 2026-10-19 · **Parts**: P1, P4 · **Journeys**: J-01, J-05 · **Tier**: T2 · **Parent**: 013-brand-workflow-uptake

## Intent

Two defects made AGD unusable for an agent in a consumer project's session: `agd-mcp`
advertised a boolean JSON Schema for one tool parameter, which the TypeScript MCP SDK rejects,
hiding all tools; and the pure path geometry commands created and failed on a document store.
After this change any spec-conformant MCP client gets every tool, path geometry runs anywhere,
and a real store conflict names the other store. Slice S01 of change 013.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P4 (new B-) | add: every MCP tool input schema and nested schema is an object (boolean `additionalProperties` allowed) | test over all tools |
| L2 | P1 (new B-) | add: path geometry commands are store-free | |
| L2 | P1 (new B-) | add: overlapping stores fail with `STORE_CONFLICT`, naming the other store, creating nothing | declared error-code cutover from `INVALID_PROPERTY` |
| L0 | J-05 | remove the variation "An MCP client rejects one tool's schema" | lands with the schema fix |
| L1 | interfaces | MCP clients row: drop the `import_pixel_layer` case; keep the general failure mode | lands with the schema fix |

## Bounds

- C-04: holds: fixes stay in the adapters and shared engine composition.
- C-09: holds: the error-code change is a declared cutover in the changelog.
- Others in scope: hold.

## Approach

Describe both `import_pixel_layer` parameters so the SDK emits object schemas; a protocol test
walks every tool's schemas; the three path commands use the file-only engine; a distinct
`STORE_CONFLICT` code; the CLI unwraps engine, access and I/O errors raised while a command is
constructed.

## Tasks

- [ ] Port the schema, store-free and store-conflict fixes with their tests from the pre-public
  history, where they were reviewed.
- [ ] Verify on Linux CI and macOS, and record the acceptance results.

## Checks

Each new behavior passes the line test: a real client hid every tool over one boolean schema,
and a pure command that created a store broke path geometry. The new P4 and P1 behaviors
get the tests in Acceptance; the error-code change is declared in the changelog (C-09).

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | P4: object tool schemas (new) | `ProtocolTests.ToolsList_AllInputSchemas_HaveNoBooleanSubschemas` and the walker self-test | 0 | NOT_RUN |
| A2 | P1: store-free path commands (new) | CLI test: path commands inside an existing store root create no store | 0 | NOT_RUN |
| A3 | P1: `STORE_CONFLICT` (new) | CLI and Core store-conflict tests | 0 | NOT_RUN |

## State

**Step**: tasks
**Waiting on**: the owner, on how the in-flight branches land here (inbox)
**Next**: port the reviewed fixes, then verify
