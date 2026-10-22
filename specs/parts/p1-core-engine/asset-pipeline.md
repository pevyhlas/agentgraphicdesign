# P1 Core engine — asset pipeline

**Serves**: J-03; R-05, R-13, Q-01, Q-05 · **Code**: `src/FTPlus.AgentGraphicDesign.Core/Assets/` · Part: [P1 Core engine](../p1-core-engine.md)

## What it does

- Reads one asset manifest of exports and SVG sprite sheets, builds the declared outputs and
  checks them read-only for byte and inventory drift.
- Produces outputs through typed in-process producers (D-07), charging each run's budget before
  work; the host owns output copies and I/O.

Detail: the [in-process producer contract](../../../docs/contracts/in-process-producers-v1.md)
and the [asset pipeline guide](../../../docs/asset-pipeline.md).

## Behaviors

- **P1.B-21 Declared outputs only**: build writes only the outputs the manifest declares, each
  published atomically per file; check never writes; outputs no longer declared are reported,
  never pruned. Test: `AssetPipelineTests`, `AssetsCommandTests`.
- **P1.B-22 Budgets before work**: a producer run is charged for nodes and raster pixels before
  validation or raster work, the host meters output bytes, and an over-budget run is refused
  with a structured limit error. Test:
  `AssetProducerPipelineTests`, `VectorAssetProducerTests`.

## Invariants

- **P1.I-09 Platform probe fails closed**: the shared regular-file probe guards pipeline
  inputs and outputs, artifact preflight, still import and native pixel storage. It passes
  .NET 10 Linux x64 and fails with `ASSET_PLATFORM_UNSUPPORTED` on every other profile except
  Windows, which it lets through unqualified, a deviation from C-11 (Q-05, inbox). This is the
  one statement of the Windows pass-through; other specs cite it. On Windows, unqualified: the
  asset pipeline build and check run, and the build writes its outputs by a managed
  temporary-file write and replacing move; artifact preflight runs; native pixel reads
  (inspect, validate, region read) run. Native pixel writes (create, edit, still import) pass
  the probe and then fail closed at the writer lock or the no-replace publication, and preview
  and export fail closed at their output checks, because the Linux statx and renameat2
  primitives are missing. Enforced by: `AssetPipelineTests` (probe on the qualified profile);
  the Windows pass-through is unenforced and CI runs only the qualified profile (inbox).

## Interface guarantees

- Asset manifest version 1: closed members, and another version is refused; a breaking change
  is a declared cutover (C-09). Schema: `src/FTPlus.AgentGraphicDesign.Core/Assets/AssetManifest.cs`.
- The pipeline is CLI-only batch work; preflight and verify also have MCP tools.

## Failure semantics

- A malformed manifest or an unowned output path is a structured error before any write.

## Left open

- Asset manifest integration of native pixel sources.
