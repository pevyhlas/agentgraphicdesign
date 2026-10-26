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

- **P1.I-09 Capability-specific file gate**: every shared regular-file inspection/inventory
  names its capability from Core's I/O-free D-28 registry: pipeline/lint inputs use
  `assetPipeline`, preflight/verify use `artifactVerify`, import input uses `stillImport`,
  and native storage/lock inspection uses `nativePixels`. At S0, all six capabilities retain
  Linux x64 .NET 10 qualification; Windows x64 .NET 10 admits only pipeline and artifact
  verification unqualified (including managed pipeline output writes). Every other guarded
  profile refuses with `ASSET_PLATFORM_UNSUPPORTED`, preserving the registry text before any
  write. Windows arm64, other runtime majors and Windows native reads no longer pass through.
  Vector/raster remain unguarded and unqualified off Linux x64. Tests: `PlatformRegistryTests`,
  `PlatformGateAgreementTests`, all three platform entry-point refusal classes and
  `AssetPipelineTests.PlatformGuard_FollowsTheRegistryGate`. No live availability probe is built.

## Interface guarantees

- Asset manifest version 1: closed members, and another version is refused; a breaking change
  is a declared cutover (C-09). Schema: `src/FTPlus.AgentGraphicDesign.Core/Assets/AssetManifest.cs`.
- The pipeline is CLI-only batch work; preflight and verify also have MCP tools.

## Failure semantics

- A malformed manifest or an unowned output path is a structured error before any write.

## Left open

- Asset manifest integration of native pixel sources.
