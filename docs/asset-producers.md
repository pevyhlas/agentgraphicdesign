# Typed Asset Producers

The Core asset pipeline now delegates vector production through a typed in-process
interface. This is part of the asset pipeline (the asset-pipeline component of the Core engine; D-07 in `specs/architecture/decisions.md`), not an external plugin system,
Blender worker or provided-artifact admission implementation.

## Composition and Cutover

The pipeline requires explicit loader/producer composition:

```csharp
// engine is the application's configured, storeless IDocumentEngine.
var pipeline = new AssetPipeline(engine, new VectorAssetProducer(engine));
var result = await pipeline.RunAsync(manifestPath, check: true, cancellationToken);
```

Types are in `FTPlus.AgentGraphicDesign.Core.Assets`. The engine-only constructor
was removed and repository callers were updated directly. There is no compatibility
forwarder, legacy alias or migration layer. CLI commands and manifest grammar do
not change in this slice; their output/safety tests remain correctness baselines.

The CLI's factory constructs one storeless engine and passes that same engine to
the producer. It does not resolve the document-store-backed engine just to check
files, so `agd assets check` still creates no `.agd` directory.

## Contracts

`IAssetProducer<TRequest>.ProduceAsync(request, budget, cancellationToken)` produces
one result per source occurrence. The generic request parameter keeps future domains
from inheriting vector fields; the current manifest pipeline deliberately remains
specialized to `VectorAssetRequest`. There is no manifest-controlled producer discovery.

The request carries the freshly loaded document, diagnostic source name, typed SVG/
PNG/JPEG/WebP format, symbol mode, effective validation/color/size/font settings and
an immutable ordered font-directory sequence. No output path, store or publication
handle is supplied. The producer validates semantic document rules and renders/exports
through the engine; source loading remains host-owned.

`IAssetProductionBudget` supplies node and raster-pixel charging. A fresh budget is
created per run; nodes are charged before validation and raster pixels before rendering.
Negative charges, capacity exhaustion and overflow cannot refund or bypass limits.
These are cooperative limits for reviewed code, not a sandbox against arbitrary .NET.

`AssetProducerResult` carries `ReadOnlyMemory<byte>` and the existing code/message
`RenderDiagnostic` list. Those are transfer views, not immutable admitted artifacts.
The host consumes bounded diagnostics, checks actual byte sizes, and copies the exact
accepted memory slice before retaining it or invoking the next occurrence. Later
buffer/list reuse cannot alter accepted outputs within that serialized sequence.

Inputs and budget must not be retained beyond task completion. Returned storage must
remain stable through host consumption; task completion is not a consumption signal.
Overlapping runs require thread-safe engine/producer dependencies and nonconflicting
result storage. Per-run budget isolation does not prove arbitrary dependency thread safety.

## Host Responsibilities

The pipeline retains:

- Manifest parsing, path/font vetting, input limits and output ownership.
- Just-in-time source loading, including repeated sources, in explicit order.
- Actual fragment/aggregate byte limits independent of producer work reporting.
- Diagnostic qualification, truncation and cancellation handling.
- SVG symbol composition and its XML/ID/size checks.
- Full-batch comparison before any file publication.
- Read-only check and atomic per-file build replacement; no automatic orphan deletion.

Production errors leave prior outputs untouched. Filesystem failures during publication
can still leave a partially updated batch; whole-generation atomic publication is
separate planned work. Host copies are not the stronger sealed external-artifact
receipt boundary. No payload MIME/decode/admission check is added to opaque output.

## Direct Calls and Limits

Direct producer callers supply loader-equivalent bounded acyclic documents and vetted
font directories; the producer is not a path-confinement or arbitrary graph-admission
API. Cheap effective-option guards reject invalid enum/profile/color/sizing combinations.
SVG ignores raster font booleans, but does not accept raster sizing/font-directory
overrides. Defaults are documented in the [exact contract](contracts/in-process-producers-v1.md).

Current batch bounds remain 100,000 nodes, 64 million raster pixels, 32 MiB per
result/final asset and 64 MiB aggregate generated output. The host checks byte limits
before accessing/copying rejected memory. Diagnostics remain capped at 256 with
128-character codes and 4096-character messages, qualified by host source/output paths.

The [contract](contracts/in-process-producers-v1.md)
and [D-07](../specs/architecture/decisions.md#d-07-typed-in-process-producers)
record sequencing, ownership, cancellation and error-stage requirements. Durable jobs,
artifact selection, sealed admission, native import and external execution remain
separate gates. No schema, dependency, command registration or package version changed.
