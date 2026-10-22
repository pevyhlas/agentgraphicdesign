# In-Process Asset Producer Contract

**Status**: normative contract, implemented and shipped. Authority:
[asset pipeline](../../specs/parts/p1-core-engine/asset-pipeline.md).
**Scope**: the typed in-process producer boundary only; no producer discovery, external
execution, metadata admission, durable jobs or manifest changes.

## Interfaces and Scope

Add the following small Core surface under `Core.Assets` (names are the proposed
implementation contract, not new CLI/MCP tools):

```csharp
public interface IAssetProducer<in TRequest>
{
    Task<AssetProducerResult> ProduceAsync(
        TRequest request, IAssetProductionBudget budget,
        CancellationToken cancellationToken = default);
}

public interface IAssetProductionBudget
{
    void ChargeNodes(int count);
    void ChargeRasterPixels(long count);
}

public sealed record AssetProducerResult(
    ReadOnlyMemory<byte> Data,
    IReadOnlyList<RenderDiagnostic> Diagnostics);
```

The existing `Core.Rendering.RenderDiagnostic` is a code/message data record and
is reused without introducing a duplicate diagnostic model or eager mapping. It
does not require a Skia dependency. These are in-process transfer types, not immutable
artifact receipts: `ReadOnlyMemory` and `IReadOnlyList` alone do not establish ownership.

`VectorAssetRequest` carries a freshly loaded `Document`, a diagnostic `SourceName`,
a closed `VectorAssetFormat` (`Svg`, `Png`, `Jpeg`, `Webp`), `AsSymbol`, `Strict`,
`ValidationProfile`, optional `CurrentColor`, `Scale`, optional `Width`/`Height`,
an immutable ordered font-directory sequence, `SystemFonts` and `StrictFonts`.
It contains no output path, store, publication handle or pipeline reference.

This is an effective-request boundary, not another manifest/admission API. Defaults
are strict validation, scale 1, null size/currentColor overrides, empty immutable
font directories, default validation profile, system fonts false and strict fonts true.
Only the declared format enum and Default/WebIcon validation profiles are accepted;
undefined enum values fail rather than falling through to PNG. Scale is finite and
0.1-4; supplied width/height are 1-8192. SVG uses scale 1 with no size/font-directory
overrides; raster rejects currentColor. CurrentColor, when present for SVG, must be
a supported color literal. Raster font booleans are ignored for SVG; do not infer
manifest option presence from effective defaults or alter its stricter presence rules.

Direct callers must provide a loader-equivalent structurally bounded, acyclic document
graph and vetted font directories (at most 16, no null/blank entries), and keep the
inputs stable. This does not mean semantic validation already passed: node charging
still precedes semantic document validation inside the adapter. Cheap request/option
guards reject invalid programmatic requests, but do not add a general graph-admission
layer or a serialization clone. The adapter is not a path-confinement entry point.

`VectorAssetProducer(IDocumentEngine)` implements this request specialization.
The sole pipeline constructor is
`AssetPipeline(IDocumentEngine, IAssetProducer<VectorAssetRequest>)`. Both dependencies
are explicit and required. Repository callers cut over directly, including the CLI's
storeless composition. The old engine-only constructor is removed, with no forwarding
overload, alias or migration layer. The maintainer explicitly permitted direct cutovers.

A generic request parameter prevents future non-vector producers from inheriting
vector fields. The v1 pipeline deliberately remains vector-source-specific; this
slice does not pretend it dispatches arbitrary artifact types. Introduce a future
request type only with its actual producer/profile, not speculative registry entries.

## Host Responsibilities

The pipeline retains manifest parsing, all source/font/output path checks and byte
limits, occurrence order, output inventory, source loading, sprite composition,
bounded result handling, complete-batch comparison and per-file atomic publication.

Preserve the current sequence:

1. Plan all manifest entries and inspect source/font sizes and output ownership.
2. Inventory the output root without writing.
3. For each plan and each source in listed order: re-inspect, call the existing
   `IDocumentEngine.TryLoadDocumentFromFile`, convert failed loads to existing
   source-qualified `PARSE_ERROR`, and invoke one producer request.
4. Consume diagnostics under the current bounded policy, check cancellation, enforce
   actual returned bytes, and copy accepted bytes into host-owned storage.
5. Compose each SVG sprite from its frozen fragments and apply current final limits.
6. Compare the complete batch before any publication. Build replaces only changed
   declared files; check never writes. Orphans remain reported and never pruned.

Do not preload, cache/deduplicate or parallelize source loads. Repeated references
remain repeated occurrences; a source modified by an earlier trusted test producer
is loaded later as before. This is not a sealed batch filesystem snapshot or artifact
admission. Do not replace engine loading with a differently shared byte reader or
serialize/deserialize the document as a defensive copy: both can change error or
numeric behavior. Source labels preserve existing error qualification.

The host does not mutate a loaded document during its production call. The mutable
graph belongs to that occurrence; producers must not retain or mutate it after the
call. Copy font-directory sequences into immutable storage. Rendering still reads
host-vetted explicit fonts and optionally system fonts; this is not an I/O-free
producer contract or a replacement for future sealed input/decoder policies.
Input and budget borrowing ends when the producer task completes, including failure
or cancellation; producers must not retain those objects or charge after that point.

## Work and Output Limits

Create one private budget per `RunAsync`, shared by its sequential producer calls,
never stored on the reusable pipeline instance. Reject negative charges and overflow
using remaining-capacity checks. Provide no reset, refund or limit-changing methods.

The vector adapter counts and charges nodes before document validation. For raster
output it preserves finite/0.5-8192 effective-dimension checks and AwayFromZero integer
rounding, and charges the computed pixels before invoking the renderer. Parsing and
counting necessarily happen before those costs are known. Preserve existing cancellation
checkpoints and do not use returned raster dimensions as the cost authority.

Limits remain 100,000 nodes and 64,000,000 raster pixels per run, 32 MiB per result
fragment/final output, and 64 MiB aggregate generated output. The host meters actual
byte lengths regardless of producer cooperation. Source/font/inventory limits remain
unchanged. For sprites, charge/check each fragment, compose in explicit order, then
add only wrapper overhead before checking the final result; do not double-charge or
move predicted final-size rejection ahead of existing XML/ID validation.

Budgets are cooperative for reviewed in-process code, not a security sandbox against
arbitrary .NET implementations. No manifest can select a class or authorize arbitrary
code, and no external tool can be added through these interfaces without its own gates.

## Ownership, Diagnostics and Errors

The host checks actual byte limits before copying, then retains only its private
copy before the next production call. Copy exactly the returned memory slice. A later
call reusing a producer/exporter buffer cannot change an accepted earlier fragment.
Producers must keep returned memory/diagnostics stable while the host consumes them;
copying does not establish an atomic snapshot against concurrent mutation. External
producer sealing remains a later, stronger contract.

"Next call" here means the same serialized production-and-consumption sequence,
not the next call anywhere on a shared producer. Task completion is not consumption
completion. Overlapping runs require thread-safe engine/producer dependencies and
nonconflicting returned storage, normally a per-call snapshot. Per-run budget tests
do not certify arbitrary dependency thread safety. This slice adds no result lease,
consumption acknowledgment or pipeline-wide lock.

Preserve diagnostic order, host-owned manifest-relative source/output labels, 128-
character codes, 4096-character messages and the current 256-slot truncation behavior.
Check saturation before reading omitted diagnostic fields; truncation retains the
replaced slot's source/output identity. Do not eagerly enumerate/map all renderer
diagnostics before the host applies its bound. Accepted diagnostics become new immutable
pipeline records, not retained provider collections. Opaque bytes remain opaque:
do not introduce MIME/content/decode admission checks for standalone outputs.

Keep error stages meaningful. Loading and path failures use the host's outer mappings.
The vector adapter counts/validates outside its export/render catch; engine
exceptions, cancellation and out-of-memory pass through. Unexpected export/render
failures become source-qualified `ASSET_EXPORT_FAILED`; host diagnostic processing
uses the same mapping. Do not wrap every new producer call in a broader catch that
changes count/validation errors. Sprite composer failures retain their codes.
New injected producers use `DocumentEngineException` for handled failures and honor
cancellation; they are trusted code, not another untrusted protocol surface.

## Acceptance

- Run the asset/vector/schema/CLI/MCP correctness and safety regressions. This slice
  does not need a manifest-format change; existing bytes/defaults remain useful
  baselines, not a requirement to implement compatibility wrappers or migrations.
- Verify effective typed mapping, ordered source occurrences, symbol flags and font
  immutability. Requests have no destination or publication authority.
- Reject node cost before validation and pixel cost before raster work; prove budgets
  reset per run and are isolated between controlled overlapping runs.
- Reuse/mutate a returned array on a later call and prove earlier standalone and
  sprite output stays unchanged; include sliced memory and diagnostic-list mutation.
- Enforce host byte limits even when a fake producer reports no work.
- Preserve cancellation, late load/export failure, diagnostic truncation/precedence,
  sprite composition and no-publication-on-production-failure behavior.
- Exercise a source growing beyond the engine limit after planning: it remains a
  load-time `PARSE_ERROR`, not a newly remapped planning/reader error.
- Keep check storeless/write-free and preserve the documented trusted-filesystem and
  per-file atomicity limitations. No schema, dependency, command or package-version change.

Direct cutover supersedes the initial proposal to preserve the old constructor.
Do not maintain parallel implementations solely for compatibility. Keep safety,
determinism and explicit format/version semantics; revise behavior and callers together
when an intentional design change needs it instead of adding a migration path.
