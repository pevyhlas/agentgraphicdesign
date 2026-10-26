# P1 Core engine — vector documents

**Serves**: J-01, J-02; R-01, R-02, R-04, R-13, Q-01, Q-02, Q-03, Q-04 · **Code**: `src/FTPlus.AgentGraphicDesign.Core/` (engine, documents, serialization, validation, lint, layout, patching, ordering, persistence) · Part: [P1 Core engine](../p1-core-engine.md)

## What it does

- Is the single authority for vector document reads, mutations, validation and lint; every
  surface reaches documents only through `IDocumentEngine` and its shared services (R-01).
- Keeps vector format 1 additive (C-09) and its serialization canonical (Q-01).
- Makes concurrent or failed writes visible instead of silently losing data (Q-04).

## Behaviors

- **P1.B-01 Canonical serialization**: serializing a document twice, or after a load/save
  round trip with no edit, yields identical bytes: typed properties in a fixed order,
  dictionary keys ordinally sorted, fixed numeric precision, GUID node ids. Test:
  `DocumentSerializerTests`.
- **P1.B-02 Tolerant, strict reading**: a `type` discriminator is accepted in any property
  position, duplicate keys and excessive depth are rejected, and unknown node types are kept
  with their payload for round trips. Test: `DiscriminatorOrderTests`, `DocumentSerializerTests`.
- **P1.B-03 Format version gate**: a document newer than format 1 validates with a warning and
  is refused for mutation. Test: `SchemaCompatibilityTests`, `DocumentValidatorTests`.
- **P1.B-04 Atomic patch**: a patch of up to the published operation limit is validated as a
  whole and saved once with one new revision; any failing operation names its index and
  nothing is written. Test: `PatchTests`, `PatchCommandTests`.
- **P1.B-05 Revision check**: every mutation increments `revn`; a caller that passes an expected
  revision that is stale gets `REVISION_CONFLICT` and the document is unchanged. Callers that
  omit it accept last-writer-wins. Test: `FileDocumentStoreTests`, `DocumentToolsTests`.
- **P1.B-06 Layout helpers move only what they say**: align and distribute change only
  `transform.x/y` of the named nodes; fit changes the frame's size and translation and offsets
  its direct children so they stay visually in place, and nothing else. Test: `LayoutToolsTests`, `LayoutCommandTests`.
- **P1.B-07 Lint is advisory**: lint findings never block validation or export. Test:
  `LintServiceTests`.
- **P1.B-08 Store-free validation**: file validation and asset batches do not create a `.agd`
  store in the invoking directory. Test: `ValidateCommandTests`.
- **P1.B-26 Store-free path geometry**: the path geometry commands (`agd path offset`, `bool`,
  `union`) run on the store-free engine, so they create no `.agd` store and work inside an
  existing store tree. Test: `PathCommandTests.PathCommands_AreStoreFree_InsideAnExistingStoreRoot`.
- **P1.B-27 Store conflicts are named**: a store that would sit inside another store, or would
  contain one, fails with `STORE_CONFLICT` before creating anything; the message names the
  enclosing store root, or the contained store's location relative to the directory holding the
  new store. Test: `FileDocumentStoreHardeningTests`, `PathCommandTests`.

## Invariants

- **P1.I-01 Reorder is local**: moving or reordering a node changes only that node's
  fractional `order` value, never its siblings'. Enforced by: `FractionalIndexTests`.
- **P1.I-02 No partial documents**: a document file is replaced atomically under a cooperative
  lock; readers see the previous or the new content, never a mix. Power-loss durability is not
  claimed. Enforced by: `FileDocumentStoreHardeningTests`.
  Vector storage remains unguarded on every profile and unqualified off .NET 10 Linux x64;
  the S0 platform registry does not add a vector file gate (D-28).
- **P1.I-03 Bounded queries**: node listing and inspection page their results and stop at
  published scan and depth limits with `RESOURCE_LIMIT`. Enforced by: `NodePageTests`, `HardeningTests`.

## Interface guarantees

- Vector document format 1: optional fields may be added in 0.x; removing or redefining a field
  needs format 2 and architecture review. The `$schema` value is an identifier only; the
  repository schema is authoritative. Schema: `schemas/agd-document.schema.json`.

## Budgets

- Patch size, listing page size, scan count and tree depth are fixed published limits
  (`DocumentLimits`, `PatchLimits`); raising one is a reviewed change because it widens Q-03.

## Left open

- Layout constraints and auto-layout.
