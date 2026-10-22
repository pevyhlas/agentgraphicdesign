# Native Pixel API v1

**Status**: normative v1 contract, implemented and shipped since 0.2.0-preview.2 on .NET 10
Linux x64. Authority: [native pixels](../../specs/parts/p1-core-engine/native-pixels.md) in
the spec; this file holds the exact operation and format boundary.
**Scope**: flat, layered, native RGBA8 still assets with visibility, opacity, coverage masks,
immediately persisted rectangular edits, composite preview, single PNG export and read-only
decoded-sample output checking.

## Authority and Scope

Read these contracts together; do not implement a second storage/hash or color rule
in a surface adapter:

- [Native storage and writes](native-pixels-v1.md) owns native document shape/version,
  Unicode names, canonical serialization/source digest, framed payload bytes/hashes,
  source closure, writer locking and metadata-last publication.
- [Native evaluation and PNG](native-pixel-evaluation-v1.md) owns source channel/row
  conventions, exact color transfer, mask/opacity math, rounding, transparent-output
  canonicalization, evaluator/encoder settings and normative sample vectors.
- [Managed PNG verification](managed-png-verification-v1.md) owns strict complete
  managed decoding, including DEFLATE bounds, exact EOF and raw decoded RGBA hashing.
- [PNG structure policy](artifact-preflight-v1.md#png-structural-policy) owns
  `agd.png-rgba8-structure.v1`; structural validity alone is not output checking.
- The [spec component](../../specs/parts/p1-core-engine/native-pixels.md) records the
  owner-accepted scope; animation, transforms and indexed palettes are later work.

The jointly reviewed companion contracts are binding implementation dependencies,
not permission for CLI/MCP to select alternative interpretations.
Arbitrary positive, bounded dimensions are supported, including 32x32, 64x64,
37x23, 1xN and Nx1. Those are fixtures, not presets. Dimensions are fixed at create.
There are no sessions, dirty/unsaved state, save command, active layer or selection.
No vector `IDocumentEngine` extension, universal graphics engine, generic JSON
Pointer patching, PNG import, animation, transforms, resampling, groups, alternate
blend modes, manifest integration, jobs, receipt ledger, automatic Git, GUI or
cross-asset transaction is introduced.

## Shared Values

Wire names are case-sensitive camelCase. Core uses typed immutable records, closed
enums and an immutable ordered operation union, not `JsonElement`, dictionaries or
mutable document nodes as its model API. Unknown enum values are invalid even for
direct C# callers. Constructors/service entry guards enforce semantic invariants:
exact values, identities, names, counts, ownership and resource bounds. Wire lexical
checks belong to ingestion: typed integers, GUIDs and decimals do not retain JSON
number spelling, escaped property names or duplicate keys. Do not invent such
tokens by serializing a typed model and claim to have validated the original input.
Collections and sample buffers have actual ownership; merely
wrapping a caller array in `IReadOnlyList` or `ReadOnlyMemory` is not immutability.

| Value | Exact meaning |
|-------|---------------|
| `assetDirectory` | Directory containing `asset.agd-pixels.json` and `payloads/<sha256>.bin`, not a vector file, descriptor, asset ID lookup or payload URI |
| `profile` | `agd.pixels.rgba8-linear-over.v1`, with its exact source/evaluation meaning delegated to the companions |
| `limitsProfile` | `agd.pixels.runtime.v1`; runtime qualification limits, not permanent format capacity or a preset enum |
| `Snapshot` | Closed object `{assetId,revn,sourceDigest}`; all three fields required |
| `assetId`, layer `id` | Nonzero GUID, lowercase canonical D form (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`); asset/layer IDs pairwise distinct within an asset; reject braces, uppercase, short forms and `Guid.Empty`, rather than normalizing input; no UUID version/variant restriction |
| `revn` | Exact signed 64-bit integer in `1..9223372036854775807`; never parse through binary floating point; clients must preserve integer precision above 2^53 rather than round-trip through a JavaScript `Number` |
| `sourceDigest` | Lowercase 64-hex SHA-256 recomputed from validated canonical native source according to storage, excluding `revn` and including all source controls, raw-source payload identities, names, IDs and layer order |
| `name` | Exact Unicode name accepted by storage's bounded scalar/UTF-8 and explicit control/bidi rules; joined emoji are allowed, with no blanket `Cf` ban; no adapter trimming, normalization or uniqueness requirement beyond storage |
| `opacity` | Exact decimal value in `0..1` representable as integer millionths; wire spelling additionally obeys storage's grammar with at most six fractional digits; no float conversion or percent strings |
| `Rect` | Closed `{x,y,width,height}` of exact integers, top-left origin, x right/y down; positive width/height and nonnegative x/y; half-open `[x,x+width) x [y,y+height)` fully inside canvas |
| `target` | Exactly `pixels` or `mask`; never an implicit composite or default active surface |
| `LayerRef` | Canonical layer GUID or `@alias`, where the alias matches `[a-z][a-z0-9_-]{0,31}`; local aliases are edit-only |
| RGBA literal | Exactly `#[0-9a-f]{8}` in `#rrggbbaa` order; no named colors, shorthand, uppercase or automatic premultiplication |
| Mask sample | Integer byte `0..255`, linear coverage, not a color or percentage |

`sourceDigest` is not a composite hash or encoded PNG hash. Payload identity hashing
is the storage contract's hash of the complete 24-byte header and raw samples; do not
substitute the unframed region/PNG RGBA hash. Equal bytes may share addressed storage
but never link the logical editing of layers. Layers are always returned bottom to
top, with array index as the sole ordering authority.

Every wire input object is closed, including nested snapshots, rectangles and operation
variants. Reject duplicate keys, unknown fields, JSON comments/trailing commas,
wrong primitive types and explicit null. Optional means omitted, not null. The
explicit exception is native layer metadata `maskSha256: null` for an absent mask;
that field remains present in metadata responses, even though the existing envelope
omits its own absent optional fields. Do not change global serializer/envelope rules
to achieve this field-level requirement. No edit operation accepts a payload hash
or a caller-declared asset/layer ID. Identity is minted only by create/addLayer.

All integer wire lexemes contain digits only, with no sign, fraction, exponent or
leading zero except the value zero itself; field bounds apply independently. Core
uses exact integers and rejects out-of-range values, not float-based coercion.
For opacity, Core requires `opacity * 1_000_000` to be an exact integer in
`0..1_000_000`, with no rounding. A typed decimal value equal to `0.5` is valid
regardless of redundant internal scale; wire `0.5000000` is invalid excess precision.
Storage canonicalization emits the same exact value and the evaluator consumes
integer millionths. Neither direct Core calls nor adapters use binary floats.
All arithmetic is checked before allocation, offset calculation or conversion.
There is no clipping, wrapping, negative indexing, fractional rectangle, empty
rectangle, implicit resize or empty-region no-op. A geometrically valid assignment
of existing values can be a final-state no-op, but still must pass its guard.

## Core Service

Use `FTPlus.AgentGraphicDesign.Core.Pixels.NativePixelService`, separate from
`Core.Artifacts` and the vector document engine/store. Construction is I/O-free;
there is no native discovery/registration or hidden persistence initialization.
CLI/MCP are thin binding, confinement and envelope adapters over these reviewed
typed methods. There is no second `PixelAssetService` alias or compatibility layer.

```csharp
// Reviewed public signatures, now implemented and registered for the bounded still subset.
Task<CreatePixelAssetResult> CreateAsync(
    CreatePixelAssetRequest request, CancellationToken cancellationToken = default);
Task<PixelAssetInfo> InspectAsync(
    InspectPixelAssetRequest request, CancellationToken cancellationToken = default);
Task<PixelRegionResult> InspectRegionAsync(
    InspectPixelRegionRequest request, CancellationToken cancellationToken = default);
Task<EditPixelAssetResult> EditAsync(
    EditPixelAssetRequest request, CancellationToken cancellationToken = default);
Task<ValidatePixelAssetResult> ValidateAsync(
    ValidatePixelAssetRequest request, CancellationToken cancellationToken = default);
Task<PixelPreviewResult> PreviewAsync(
    PreviewPixelAssetRequest request, CancellationToken cancellationToken = default);
Task<PixelExportResult> ExportAsync(
    ExportPixelAssetRequest request, CancellationToken cancellationToken = default);
Task<PixelExportCheckResult> CheckExportAsync(
    CheckPixelExportRequest request, CancellationToken cancellationToken = default);
```

These methods are members of one service, not a new interface for arbitrary plugin
execution. The following tables define their request and result records. A field
listed as optional has no other default than the one stated. No caller can raise
limits, choose a decoder, alter color math or switch publication policy.

| Request | Required fields | Optional fields/defaults |
|---------|-----------------|--------------------------|
| `CreatePixelAssetRequest` | `assetDirectory`, `name`, `width`, `height` | None |
| `InspectPixelAssetRequest` | `assetDirectory` | None |
| `InspectPixelRegionRequest` | `assetDirectory`, `layerId` (GUID only), `target`, `rect` | None |
| `EditPixelAssetRequest` | `assetDirectory`, `expectedSnapshot`, `operations` | None |
| `ValidatePixelAssetRequest` | `assetDirectory` | None |
| `PreviewPixelAssetRequest` | `assetDirectory`, typed delivery (`Inline` or `File(outputPath,overwrite)`) | `rect` (whole canvas if within preview cap), `expectedReadSnapshot` (no precondition); file `overwrite=false` |
| `ExportPixelAssetRequest` | `assetDirectory`, `outputPath` | `overwrite=false`, `expectedReadSnapshot` (no precondition) |
| `CheckPixelExportRequest` | `assetDirectory`, `outputPath` (existing PNG input) | `expectedReadSnapshot` (no precondition) |

Core paths are absolute host-vetted local paths. CLI/MCP apply the path rules below;
Core still verifies file types, asset-local payload confinement, lengths/hashes,
collision rules and resource bounds. A direct Core caller has the same trusted,
quiescent filesystem obligations; a typed path is not a hostile-filesystem seal.

Every source read captures metadata once and validates its entire required payload
closure before returning a successful snapshot, including hidden layers/masks and
regions outside a query. Reuse only verified immutable bytes within that call; do
not trust a cached revision or lazily accept missing payloads on hidden layers.
Use direct lookups of the asset directory, fixed `asset.agd-pixels.json`, fixed
`payloads` root and at most 32 current logical payload references (deduplicate
verified captures by hash within the call). Do not enumerate unrelated root entries,
unreferenced payload history, sibling staging/lock entries or parent directories.
There is no retained-history count quota and no exact-inventory admission rule for
native assets. Unrelated root files and old payloads remain untouched; their presence
is not a validation failure. An old addressed object selected for reuse must be
fully verified then, not trusted because it was previously unreferenced. Read/query
success validates the current closure only, not all history or unrelated files.
Inspection is metadata-only in its result, not metadata-only validation. Read paths
create no source entries, payloads, lock markers, cache/repair files or reports.

### Create, Inspect and Validate

Create is exclusive creation at a nonexisting asset directory under an existing
parent, following storage's creation protocol. It never adopts/overwrites an
existing directory, including an empty one, and has no overwrite switch. The
absence precondition is the only exception to snapshot-guarded source writes:
there is no prior asset snapshot. Races cannot both publish the same directory.
Create mints a nonzero asset GUID and distinct initial layer GUID, sets `revn=1`,
uses the requested asset name, and creates one layer named `Layer 1`, visible,
opacity `1`, all RGBA bytes zero, `maskSha256: null`. It returns only after complete
source publication. The initial transparent surface is charged to the work budget.

`PixelAssetInfo` is exactly `{snapshot,profile,limitsProfile,name,width,height,
logicalSourceBytes,layers}`. Each layer is exactly `{id,name,visible,opacity,
pixelsSha256,maskSha256}`. `logicalSourceBytes` excludes framing and is charged as
defined below, not the number of distinct payload files. There are no payload bytes,
absolute paths, filesystem inventory, lock ownership or unvalidated source tokens
in this result. `CreatePixelAssetResult` is `{state,initialLayerId}` with `state` a
`PixelAssetInfo`; `state.snapshot.assetId` and `initialLayerId` are the minted IDs.

`PixelRegionResult` is exactly `{snapshot,profile,limitsProfile,layerId,target,rect,
samples}`. Samples are actual source samples in row-major rectangle order, no row
padding: RGBA literals for `pixels`, integer bytes for `mask`, exactly
`rect.width * rect.height` entries. The target layer must exist; `mask` requires an
attached mask. Visibility/opacity never modify returned source values. Neither the
composite nor a digest substitutes for these samples. At most 4,096 entries and
256 KiB serialized response are allowed; reject rather than truncate or paginate.

Validation is fail-first and read-only, not repair. `ValidatePixelAssetResult` is a
closed outcome union: `{valid:true,state}` for complete validated source, or
`{valid:false,errors:[diagnostic]}` for one intrinsic document/payload validation
failure. No state/snapshot is returned on failure. A diagnostic uses the existing
`code,message,path` vocabulary with the bounded rules below. Invalid metadata JSON,
invalid source fields, missing required payload root/reference, corrupt framing/
length/hash and supported-kind/dimension binding mismatches produce the invalid
report. Missing asset, inaccessible
input (including absent required metadata), unsafe path, unsupported version/profile,
busy, cancellation and resource exhaustion are execution failures, not an invalid
report or a successful validation.
Other methods fail on invalid source rather than returning partially validated data.

## Edit Request and Operations

On CLI the request file is UTF-8 JSON (no BOM); on MCP `request` is the same JSON
object, not a JSON string. Its exact top-level shape is:

```json
{
  "expectedSnapshot": {
    "assetId": "2a4b8fde-468e-4bbb-a11f-a2b20169060a",
    "revn": 1,
    "sourceDigest": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
  },
  "operations": [
    { "op": "addLayer", "name": "Highlights", "alias": "highlights" },
    { "op": "fill", "layer": "@highlights", "target": "pixels",
      "rect": { "x": 0, "y": 0, "width": 3, "height": 2 }, "rgba": "#ffffff80" },
    { "op": "attachMask", "layer": "@highlights" },
    { "op": "replace", "layer": "@highlights", "target": "mask",
      "rect": { "x": 1, "y": 0, "width": 2, "height": 1 },
      "inputPath": "inputs/highlight-mask.raw" }
  ]
}
```

The illustrative token is not valid evidence for an actual asset: clients use the
returned snapshot unchanged. `assetDirectory` is outside this request object, as a
CLI argument/MCP tool argument; it is combined into the typed Core request.

The object is at most 1 MiB and contains 1..64 operations. JSON nesting depth is at
most 16; numeric and string lengths must be checked before expensive conversion.
The wire parser checks original token spelling and enumerates object members to
reject duplicate decoded names, including escaped-key duplicates. Do not first bind
to floats, typed DTOs or dictionaries that erase the evidence. The MCP argument
capture boundary below preserves the original JSON before this shared parser
constructs immutable Core values. Direct typed calls enforce the semantic operation
and resource constraints, not fictional raw-token/depth/duplicate-key checks.
The typed Core operation union below has the same named fields; only `replace`
substitutes a typed `RawSampleInput` for the wire `inputPath` field. No arbitrary
JSON document or operation extensibility slot is accepted.

| `op` | Fields in addition to `op` | Ordered semantics |
|------|----------------------------|-------------------|
| `addLayer` | `name`; optional `alias` | Mint ID; append at top with all-zero RGBA, visible `true`, opacity `1`, no mask; optional alias declares that newly created layer |
| `removeLayer` | `layer` | Remove that layer and attached mask from logical source; reject removal of the current last layer; never delete addressed payload files |
| `reorderLayer` | `layer`, `index` | Remove the selected element and insert at its final zero-based index in `0..N-1`, where N is current layer count; retain ID, samples and all controls |
| `setVisibility` | `layer`, `visible` | Assign JSON boolean exactly; do not touch opacity or source bytes |
| `setOpacity` | `layer`, `opacity` | Assign the exact bounded decimal; do not bake opacity into RGBA or mask |
| `attachMask` | `layer` | Require no mask, then create canvas-sized all-255 coverage; already attached is an invalid operation, not replacement |
| `removeMask` | `layer` | Remove mask presence; already absent is a valid guarded no-op; leave RGBA unchanged |
| `fill` (pixels) | `layer`, `target:"pixels"`, `rect`, `rgba` | Assign the same exact RGBA sample to each rectangle pixel, not source-over painting |
| `fill` (mask) | `layer`, `target:"mask"`, `rect`, `value` | Assign integer coverage byte to every rectangle sample; `rgba` is forbidden |
| `clear` | `layer`, `target`, `rect` | Assign RGBA `(0,0,0,0)` for pixels or byte `0` for mask; no color/value/input field |
| `replace` | `layer`, `target`, `rect`, `inputPath` | Assign captured tightly packed raw samples with exact length and EOF; no blend, decode or color conversion |

`layer` resolves against the state immediately before that operation. A GUID must
identify a currently present layer. An alias may refer only to an earlier successful
`addLayer` declaration in this batch. Declarations omit `@`; references include it.
Aliases are unique for the entire batch, even after their layer is removed. Forward
references, unknown/removed references, GUID declaration, duplicate alias and
cross-batch alias reuse as a reference are invalid. IDs minted for rejected or
created-then-removed layers are not persisted and must not be reported as such.

Every intermediate state must satisfy 1..16 layers, at most one mask per layer,
logical source and work/reservation limits. A batch cannot temporarily create layer
17 and remove another later, remove the last layer and replace it later, or exceed
source limits and then shrink. All mask-targeted rectangle operations require a
currently attached mask. Clear-mask hides without erasing the underlying RGBA;
remove-mask restores implicit full coverage. Native hidden RGB under alpha zero is
preserved by assignment and all control/mask changes unless pixels are explicitly
cleared or replaced.

### Raw Input Boundary

Replacement files contain only top-left row-major samples, tightly packed,
unframed and with no padding: exactly `rect.width * rect.height * 4` bytes for
RGBA, or `rect.width * rect.height` bytes for a mask. Require that exact length and
EOF on the same capture, not only a pre-read file length. Empty, short, excess,
growing or truncated captures fail the entire batch. Native framed payload files,
PNG/header decoding, Git asset import, data URIs and remote fetch are not supported
input modes. Bytes that happen to spell a file signature are still raw samples;
there is no content sniffing or implicit importer.

`RawSampleInput` is a closed Core choice of a genuinely immutable owned sample
buffer or a host-vetted local file source to capture inside the operation. A direct
caller cannot retain mutable access to a transferred buffer; copy mutable input
before accepting it, under the reservation and cancellation checks. File input is
captured once per replacement operation into private bytes; validation, assignment
and hashing use that same capture, never preflight-and-reopen. Repeated use of one
path is still charged per operation. No streams, arbitrary callbacks, giant inline
JSON arrays/base64, or public ingestion/session service are added.

A small internal host-ingestion runner keeps request-file reading, confinement,
replacement capture and typed dispatch in the same admitted operation lifetime.
Direct typed entry points acquire that lifetime themselves; adapter dispatch does
not reacquire the guard recursively. This is private plumbing, not a public lease,
new general engine, or a way to perform unbudgeted I/O before Core begins.

### Guard, Atomicity and No-Op

All edits require `expectedSnapshot`; omission is never last-writer-wins. Acquire
the cooperative per-asset writer lock, freshly load and validate source, recompute
its digest, and compare asset ID, revision and digest exactly under that lock.
Any mismatch is `REVISION_CONFLICT`, including same revision/different content and
same content/different revision. A Git restore of the exact same tuple can make an
old token match again: this is state comparison, not a monotonic epoch or receipt.

The admitted, locked edit proceeds in this order:

1. Validate bounded request shape, then capture/validate current source and compare
   the required snapshot before replacement-file content reads or source staging.
2. Resolve operations sequentially, check every intermediate state and all budgets,
   capture raw inputs, and compute the proposed final state in private memory.
   Any invalid operation rejects the entire batch with no new payload write.
3. Compare final `canonicalSourceBytes` byte for byte with the validated starting
   source under the storage canonical rules, not digest-only or composite/rendered
   equality. The lock and snapshot guard still apply to a no-op, including an empty
   final diff after multiple operations. Perform the final guarded check before the
   no-op decision or any publication; do not bypass it using a cached hash or
   same-value shortcut.
4. If identical, return `changed=false`, identical before/after token and empty
   change/creation lists. Do not increment `revn`, serialize/replace metadata, stage
   payloads or publish anything. Writer arbitration may use its owned lock state;
   a no-op is not a source publication and must follow storage's lock cleanup rule.
5. Otherwise check `revn+1` for overflow, increment exactly once for the whole batch,
   and publish verified complete addressed payloads then metadata last according to
   storage. A real change at maximum `revn` fails `RESOURCE_LIMIT` before writes;
   a guarded no-op at maximum `revn` succeeds. Release the lock only after cleanup.

Payload/source publication details are exclusively the
[storage write protocol](native-pixels-v1.md). It must preserve a complete accepted
old or new asset, never a partial source. Validation failure writes no payload;
later I/O failure may leave unreachable complete payloads as storage allows. Never
delete old/unreachable payloads as implicit GC. Same-asset generation safety is not
coherent multi-output consumer publication, cross-file atomic visibility, power-loss
durability or an OS/hostile-filesystem guarantee.

Successful return means immediate persisted state, not an unsaved transaction.
Once publication is known to have succeeded, success wins over subsequent
cancellation or owned-temp cleanup failure, as storage specifies. Before that point
caller cancellation propagates; genuinely ambiguous visibility uses
`PIXEL_WRITE_OUTCOME_UNKNOWN` when a response is possible. A lost response is not
rollback evidence; inspect and reconcile instead of blindly replaying. No
idempotency key, retry receipt or undo history is provided.

### Edit Result

`EditPixelAssetResult` is exactly `{beforeSnapshot,state,changed,createdLayers,
affectedLayerIds,layerChanges,changedRegions}`; `state.snapshot` is the after token
and `state` has the complete `PixelAssetInfo` shape. All collections are bounded
and computed from the before/final difference, not the sequence of attempted writes.

- `createdLayers`: surviving new layers only, ordered by add operation index. Each
  record is `{operationIndex,layerId}` plus `alias` only if declared. The zero-based
  index is the creating `addLayer` index. Created-then-removed layers never appear.
- `affectedLayerIds`: each surviving layer whose ID/content/control/presence/final
  position changed, in final bottom-to-top order, followed by removed preexisting
  layer IDs in original bottom-to-top order. No duplicates or transient IDs.
- `layerChanges`: one `{layerId,changes}` per affected ID in that order. `changes`
  is a nonempty subset of the closed ordered list `created`, `removed`, `order`,
  `visibility`, `opacity`, `maskPresence`, `pixels`, `mask`. Created layers report
  `created` only; removed layers `removed` only. For retained layers compare final
  index/controls, RGBA bytes and mask presence/bytes independently. `mask` means
  changed bytes of a mask present before and after; presence transitions use
  `maskPresence`. Index shifts of other retained layers count as `order` changes.
- `changedRegions`: at most one `{layerId,target,rect}` per surviving changed
  surface, ordered by final layer order then pixels before mask. For a retained
  surface this is the tight axis-aligned bounding rectangle of actually different
  samples, which may enclose unchanged samples. A new layer's pixels and a newly
  attached mask report the whole canvas, even if initialized to zeros/255. Removed
  layers/masks have no fictitious final region; removal is in `layerChanges`.

Control/order-only changes can have no changed regions while still changing digest
and revision. Hidden RGBA/mask changes, mask presence versus absence, and names/IDs
are source-significant even if the composite is identical. Reverted changes produce
no final diff. Result construction/serialization capacity must be reserved before
source publication; a result is not a durable publication receipt.

## Evaluation, Preview, Export and Check

Each call captures one immutable, fully validated source snapshot. If supplied,
`expectedReadSnapshot` must match that capture exactly or fail `REVISION_CONFLICT`
before evaluation/output capture or publication. It is a read precondition, not a
writer lock, source-write authorization, freshness lease or promise that source
remains current at response time. Never reread source halfway through an image.

Composite/evaluation is defined only by
[native-pixel-evaluation-v1.md](native-pixel-evaluation-v1.md): straight RGBA8 sRGB
source, linear masks and decimal opacity, bottom-to-top normal linear-light
source-over with the specified fixed arithmetic/tables and rounding. No Skia/vector
renderer is the reference. Exact canonical composite RGBA has RGB zero whenever
its final quantized alpha is zero; native source inspection does not apply that
cleanup. Adapters must not silently choose color/math or codecs.

All three result types carry common evidence `{sourceToken,profile,limitsProfile,
evaluatorProfile,settings,rect,width,height,rgbaSha256}`. `sourceToken` is the
captured `Snapshot`. Both `profile` and `evaluatorProfile` are exactly
`agd.pixels.rgba8-linear-over.v1`; `limitsProfile` is `agd.pixels.runtime.v1`.
The API defines this closed evidence-only `settings` object from the evaluation
companion's [fixed-point rules](native-pixel-evaluation-v1.md#fixed-point-rules)
and [transfer-table identity](native-pixel-evaluation-v1.md#transfer-tables):

```json
{
  "fractionalBits": 24,
  "tableSha256": "353442b2a3b85e1030cc800349cc40d4d0b7e2bdf063a7a9766298f4dabaa7b8",
  "rounding": "nearest-ties-up"
}
```

These are fixed reported facts, not user-selectable math/settings. `rounding`
identifies the companion's `R(n,d)` at its explicitly prescribed steps and the
table threshold equality rule; it does not authorize reassociating operations or
rounding additional intermediates. `rect` is in source canvas coordinates;
output width/height equal its width/height. `rgbaSha256` is lowercase SHA-256 of
unframed row-major canonical composite RGBA bytes for that rectangle, excluding
headers, row filters and PNG compression. It is evidence under dimensions/settings,
not a new global source identity. No source token is encoded into PNG as a receipt.

Preview is composite only. Optional `rect` defaults to the whole canvas only if its
area is at most 1,048,576; otherwise fail `RESOURCE_LIMIT` and require an explicit
bounded region. An explicit region has that same cap. Output is native pixel
resolution, with no zoom, resize, resampling, checkerboard, guides, overlays, layer
solo or active selection. Preview PNG is at most 8 MiB. `PixelPreviewResult` adds
`delivery` (`inline` or `file`), `encoderProfile:"agd.pixels.png.v1"`, `encodedBytes`,
`fileSha256` and `mediaType:"image/png"` to the common evidence. Inline Core delivery
additionally owns immutable PNG bytes; file delivery publishes only the explicitly
requested path and returns no image bytes/path. MCP maps inline bytes to one PNG
image content block, not a giant JSON
field; CLI preview requires file delivery. No preview cache or source write occurs.

Export always evaluates the whole canvas at native resolution, with no rectangle,
format, scale, background or color options. `PixelExportResult` adds
`encoderProfile:"agd.pixels.png.v1",encodedBytes,fileSha256,mediaType:"image/png"`
to common evidence. Output area is at most 4,194,304 and encoded bytes at most
32 MiB. Use the reference thin C# PNG writer over existing .NET compression
specified by the evaluation companion: no
new deflater/dependency and no Skia encoder as reference. Compression bytes are not
the compatibility oracle. Preview/export use the companion's exact
[PNG writer profile](native-pixel-evaluation-v1.md#png-profiles): filter 0 rows,
noninterlaced RGBA8, one sRGB chunk with intent 0, bounded consecutive IDAT and IEND.
Publication is the explicit file operation below.

Check captures the existing output PNG exactly once into private bounded bytes and
uses that same buffer for SHA-256, PNG structure and strict complete managed decode.
Reuse the internal managed runner through a small native wrapper, not a fabricated
provided-artifact descriptor, changed public verifier or pixel-import API. The
wrapper uses the native bounds and the existing decoder's stream/work rules; it
does not recursively enter another public heavy-operation guard. It creates no
candidate PNG, report, marker, directory, native payload or repair write.

Extract and bound candidate dimensions from the captured PNG, then validate/decode
using those observed dimensions, not the expected native dimensions. Header
extraction alone is not validation. Decode/structure validation must complete even
if dimensions differ, before a comparison outcome can be returned. Compare
independently validated PNG dimensions and decoded RGBA SHA-256 against native
dimensions and `rgbaSha256`. Only exact dimensions and decoded sample hash equality
pass. No tolerances, alpha-zero RGB
cleanup of observed PNG samples, gamma conversion, re-encoding, perceptual equality
or compressed-byte equality is allowed. A PNG with nonzero hidden RGB where the
reference has alpha zero is drift, even if it looks identical.

`PixelExportCheckResult` adds `{matches,comparison:"dimensions-and-decoded-rgba8",
comparatorProfile:"agd.pixels.png-samples.v1",pngPolicy,decoderProfile,
execution:"managed-in-process",pixelsDecoded:true,observed,differences}` to common
evidence. No `encoderProfile` is asserted for the independently produced candidate.
`pngPolicy` is `agd.png-rgba8-structure.v1` and
`decoderProfile` is `agd.png-rgba8-managed.v1`. `observed` is exactly
`{width,height,encodedBytes,fileSha256,rgbaSha256}` from the captured PNG.
`differences` is an ordered subset of `dimensions`, `samples`; compare hashes even
for different dimensions, and require both comparisons to match for `matches=true`.
Different compression/IDAT splits/allowed tags with identical decoded dimensions and
samples can pass. Encoded `fileSha256` is reported, never used as the match criterion.
This is not admission, provenance verification, external artifact preservation or
perpetual freshness. Missing, unreadable, malformed, unsupported, oversized or
incompletely decoded PNG is an error without a check result, never a green result
and never a sample-mismatch classification.

### Paths and File Publication

All relative CLI paths resolve against the invocation working directory captured
at command start, including `--request`, `inputPath`, snapshot files and outputs.
They do not resolve against the request-file directory or native asset directory.
CLI accepts explicit absolute local paths. MCP resolves all relative paths against
its configured working root and confines absolute paths to that root. Reject
lexical root escape before filesystem probes, then complete confinement before
content reads. `inputPath` never resolves as `payloads/<hash>` metadata indirection.
No network/URI path syntax or current-directory changes within a call are supported.

Use existing trusted local file inspection/confinement with bounded paths (at most
4,096 characters); reject symbolic links/reparse points, special files, unsafe
traversal and case/path aliases as required by storage. The controller must pause
Git and external project/raw-input/output writers during capture/publication. Never
read raw inputs while another project writer is changing them. Cooperating native
writers can overlap a reader only under storage's immutable-payload/metadata-last
protocol. This is not hostile replacement-safe open, a sealed tree or a machine-wide
lock. Initial qualification targets .NET 10 Linux x64 and the companion's supported
local filesystem assumptions; do not claim Windows/network-filesystem qualification.

Path/type/identity checks concern actually addressed components and known inputs/
outputs. Use direct qualified-backend lookups and opened-file identities, not a
root/history/parent listing to discover aliases or prove exact inventory. Fail
closed if the required identity/type check is unsupported; do not impose an
unbounded history scan or a sibling-count quota as a path-lookup prerequisite.

Writers own only the fixed metadata name, addressed payload names and storage's
reserved lock/temp/staging namespaces, not unrelated asset-root or shared-parent
entries. Under the existing shared parent, creation staging is exactly
`.agd-pixels-stage-<nonce>` and the stable writer-lock name is
`.agd-pixels-lock-<lockKey>.lock`, where `lockKey` is lowercase SHA-256 of
strict UTF-8 encoding of the exact directory leaf, without case folding, Unicode
normalization, path/hash-domain prefix or appended newline. `nonce` is 32 privately
generated lowercase hex characters. Existing-asset owned root temporaries use
`.agd-pixels-tmp-<nonce>.tmp`. Validate parent/leaf binding, lock type and stable
opened-file identity with the qualified backend. The lock marker may remain after
release; never unlink it as cleanup or infer ownership from its presence. Readers
do not inspect/create/acquire it or discover/traverse creation staging. Writers
clean only recorded entries owned by their attempt, never scan or scavenge unrelated
entries, stale staging or retained payloads. The storage protocol owns publication.

Both CLI preview and CLI/MCP export require an explicit output file path, an existing
parent directory and `overwrite=false` by default. Reject source collisions before
creation of any output temporary: the destination cannot be the asset directory,
inside that directory (including unreferenced payload/temp names), in the reserved
sibling lock/creation-staging namespace, or an alias of a source metadata/payload/
lock file. Reject destination directories, symlinks, special
files and unsupported aliases. Do not overwrite other inputs of the same operation;
snapshot/request input files are protected as well. Check rejects a source-colliding
output path even though it does not write. No `.agd` store/output folder is created
implicitly. No project-wide ownership registry or output-generation inventory is implied.

An existing candidate/output file must have link count exactly one according to the
qualified opened-file/identity probe. Reject a multiply-linked PNG with `PATH_ESCAPE`,
even when no known source identity matches; this deliberately includes an otherwise
unrelated hard-linked PNG. Together with lexical reserved-namespace protection and
known-source identity checks, this rejects lock-file aliases without inspecting or
acquiring the sibling lock during a read-only operation. Unsupported link-count
inspection fails closed. Do not perform a global alias search. New output temporaries
are exclusive single-link files; recheck the applicable destination safety at publication.

Acceptance must cover both an output PNG name hard-linked to the writer lock and an
unrelated multiply-linked valid PNG: preview/export/check reject both with `PATH_ESCAPE`
without lock reads/creation or output changes. Ordinary single-link valid PNGs retain
their positive checks. This is an output-boundary rule, not a new source-history scan.

Generate/validate bounded PNG bytes before destination publication; stage a unique
owned sibling temporary, flush/complete it, then publish by the supported atomic
single-file operation. With overwrite false, publication must fail if a destination
already exists or appeared meanwhile; never emulate this with check-then-clobber.
With overwrite true, atomically replace only a permitted regular destination (or
create if absent), rechecking safety under the cooperative assumptions. Prepublish
failure leaves an existing output unchanged; clean up only this call's owned
temporary. Hold the operation guard through cleanup on all outcomes. No truncating
in-place output write, automatic overwrite, implicit output marker or multi-file
consumer atomicity/power-loss guarantee is added. Known publication success wins
over subsequent cancellation/cleanup failure; genuinely ambiguous publication or
reply loss has the same observation caveat as native source publication.

## Resource and Lifetime Contract

All caps are inclusive; MiB/KiB are binary units. Limits are reviewed runtime
qualification requirements named `agd.pixels.runtime.v1`, not evidence of completed
qualification or a hard process sandbox.
Independent maxima do not imply their Cartesian product fits.

| Resource | Limit and charge |
|----------|------------------|
| Canvas | Each axis `1..8192`; checked area at most `4,194,304` |
| Layers/masks | `1..16` layers, at most one canvas-sized mask per layer, at every intermediate state |
| Logical source samples | `64 MiB = 67,108,864` bytes; `area * (4 * layerCount + maskCount)`, charging hidden and duplicate logical surfaces; framing excluded here only |
| Working reservation | `256 MiB = 268,435,456` bytes across simultaneously live validated old source, dirty copies, captured replacements, composite rows/buffers, output/PNG capture, decode state, serialization, transfer copies, payload framing and bounded collection overhead |
| Native metadata | `1 MiB = 1,048,576` UTF-8 bytes, including whitespace on input; exact canonical/storage limits also apply |
| Source closure lookup | Fixed metadata/payload root and at most 32 current logical payload references; direct lookups only, no history/root/sibling enumeration or retained-history count quota |
| Owned source staging records | Storage's at most 64 owned payload-temporary paths and one metadata-temporary path, plus fixed owned creation-staging directories/lock handle; at most 32 final payload references to publish, never an on-disk inventory quota |
| Edit JSON | `1 MiB` UTF-8 bytes for the whole request object (CLI file bytes, MCP submitted object bytes), nesting depth at most 16, at most 64 operations and at least one |
| Replacement input | `16 MiB = 16,777,216` raw bytes summed over every replace in the batch, even reused paths/equal bytes |
| Cumulative edit work | `16,777,216` targeted pixel/mask sample positions per batch: charge full canvas for each added transparent layer or attached mask, and rectangle area for each fill/clear/replace; a pixel position is one charge, not four channel charges |
| Sample response | At most 4,096 samples and 256 KiB serialized region response, including surface envelope/protocol framing and duplicated MCP text/structured facts; no truncation |
| Other facts | At most 256 KiB per serialized facts envelope; bounded layers/change lists/errors, never unbounded source inventory or payload bytes |
| Preview | At most `1,048,576` output pixels and `8 MiB = 8,388,608` encoded PNG bytes |
| Full PNG export/check | At most `4,194,304` output/input pixels, each axis at most 8192, `32 MiB = 33,554,432` encoded bytes; decoder work bounds still apply |
| Managed PNG work | Existing 4,096 chunk / 65,536 DEFLATE-block limits per PNG and managed decoder bounded rows/history/tables, with native width/area/encoded caps additionally enforced |
| Lifetime | One admitted native operation per host, 60-second whole-operation cooperative deadline, caller cancellation preserved |

At 2048x2048, three masked layers charge 60 MiB; four charge 80 MiB and fail.
Two masked 64x64 layers charge 40 KiB before framing. Deduplication, hidden status,
render neutrality, final no-op, removing a layer later, or repeated rectangle overlap
does not refund cumulative input/work charges. Control, reorder and removal charge
no targeted samples but still incur bounded validation, comparison, serialization
and source-read work. Creation charges its initial full transparent canvas.

Reserve before allocation/copy/encoding and use checked remaining-capacity tests.
Old source and proposed new bytes coexist until safe to release; do not count only
the final asset or claim the logical-source cap bounds working memory. Row-wise
composition and dirty-surface copies are preferred, not an excuse to omit buffers.
Include MCP base64 image expansion, operation-owned protocol/text/structured buffers
and payload header/serialization overhead in the working reservation; the 8 MiB
preview cap measures binary PNG, not base64. The inline facts envelope is separately
bounded. Initial MCP SDK transport/envelope parsing and allocation precede the
native operation boundary and are outside the Core reservation; do not claim that
the 1 MiB edit cap retroactively bounds them. Charge retained/copied native argument
capture and subsequent operation-owned buffers inside admission. SDK/framework and
CLR/GC/native/OS overhead make this an algorithmic reservation, not a hard
process-RSS or transport-message allocation guarantee.

All eight functions are admitted as heavy native calls, including metadata inspect
because it validates source closure. Use one host-lifetime shared native Core guard
across service instances/assets/tools, not a new guard per request. After honoring
already-requested caller cancellation, competing calls fail immediately `PIXEL_BUSY`,
with no queue. Separately, storage requires a cancellation-aware five-second
monotonic contention budget, measured from the first attempt, for the per-asset
cross-process writer lock, returning
`PIXEL_BUSY` on expiry. Both cases use that same code, distinguished by the fixed
messages `Native operation slot is busy.` and `Asset writer lock wait expired.`
respectively, without new envelope fields. The per-asset five-second wait is within
the already admitted 60-second call, not a host work queue; other calls to this host
still fail immediately `PIXEL_BUSY`. Do not replace the per-asset cooperative wait
with immediate-only rejection or retry non-contention I/O/path failures as lock
contention. This is not machine-wide serialization of readers or a replacement for
storage's per-asset lock.

Admission precedes operation-specific I/O, including confinement probes, request/
snapshot-file reads, source/raw/PNG capture and output staging. Only bounded syntax
checks that perform no I/O may precede admission. The internal host runner and Core
share one budget/deadline/guard lifetime through validation, evaluation, serialization,
publication, result materialization and all cleanup. A completed inner Core task
must not release admission while a host still writes its file or captures input.
MCP network delivery after bounded response construction is not a background native
operation. No detached tasks or continued use of borrowed inputs/budgets is allowed.

The 60 seconds include all admitted I/O/work; this is cooperative, not a timer race
that abandons live work and releases the guard. Check caller cancellation/deadline
before I/O, before each allocation/publication, between operations, per composite
row and at most every 1,024 evaluated layer-pixels, emitted pixels or edited sample
positions during long loops. Capture, hashing, copying, CRC, serialization and
current-reference lookup loops check at most every 64 KiB or bounded reference/chunk;
decoder checkpoints remain at least as frequent as its managed contract. Bound
encoder writes and check between bounded writes; .NET
compression/filesystem/CLR calls are not forcibly preempted. Deadline exhaustion is
`RESOURCE_LIMIT`; caller cancellation propagates as cancellation with the caller's
token, not a deadline or rollback claim. If both are observed, caller cancellation
takes precedence before known publication; known publication success follows the
storage outcome rule above. Cleanup still unwinds, retaining both guard and the
applicable writer lock until complete; do not cancel cleanup by dropping ownership
of live work.

## CLI and MCP Binding

The following exact names/spellings are the reviewed implementation contract and
the registered native CLI/MCP surface delivered in 0.2.0-preview.2. Status
reconciliation changes no operation, schema, discovery or manifest semantics.
Every CLI command accepts `-f|--format text|json`, default `text`. Snapshot input
files contain exactly the closed `Snapshot` JSON object (UTF-8, no BOM, at most
1 KiB); they are read once under the same lifetime and use invocation path rules.

```text
agd pixels create <DIRECTORY> --name <NAME> --width <INT> --height <INT>
agd pixels inspect <DIRECTORY>
agd pixels region <DIRECTORY> --layer <GUID> --target pixels|mask --rect <X,Y,W,H>
agd pixels edit <DIRECTORY> --request <JSON_FILE>
agd pixels validate <DIRECTORY>
agd pixels preview <DIRECTORY> --output <PNG> [--rect <X,Y,W,H>] [--overwrite] [--expected-read-snapshot <JSON_FILE>]
agd pixels export <DIRECTORY> --output <PNG> [--overwrite] [--expected-read-snapshot <JSON_FILE>]
agd pixels check <DIRECTORY> --output <PNG> [--expected-read-snapshot <JSON_FILE>]
```

`--rect` is exactly four comma-separated base-10 integers without fractional or
exponent syntax, in x,y,width,height order; normal shell quoting is external to the
value. No stdin PNG, output default, save command, color/zoom option or optional
write guard is inferred. CLI JSON carries the result in the existing envelope;
text must identify captured source token and validation/check outcome without
claiming continued freshness. Preview is an explicit file write, not terminal
binary/base64 output.

| Reviewed MCP tool | Closed arguments | Source/output behavior |
|-------------------|------------------|------------------------|
| `create_pixel_asset` | `assetDirectory,name,width,height` | Creates source exclusively |
| `inspect_pixel_asset` | `assetDirectory` | Read-only metadata plus validated token |
| `inspect_pixel_region` | `assetDirectory,layerId,target,rect` | Read-only actual bounded samples |
| `edit_pixel_asset` | `assetDirectory,request` | Required guarded typed batch, immediate source persistence |
| `validate_pixel_asset` | `assetDirectory` | Read-only complete native validation |
| `preview_pixel_asset` | `assetDirectory`; optional `rect,expectedReadSnapshot` | Read-only native evaluation; inline PNG, no output path or source write |
| `export_pixel_asset` | `assetDirectory,outputPath`; optional `overwrite=false,expectedReadSnapshot` | Explicit confined file publication, no source write |
| `check_pixel_export` | `assetDirectory,outputPath`; optional `expectedReadSnapshot` | Read-only structure/decode/sample comparison, never repair |

At the native MCP tool-argument layer, preserve the original arguments object as
`JsonElement` backed by the original JSON capture, before typed/dictionary binding
can collapse duplicate members or normalize numeric tokens. This is adapter
ingestion, not a `JsonElement` Core model API or a public JSON-string replacement
for the structured `request`. Enumerate and validate outer argument names as well
as nested object names; `GetProperty`/last-value lookup alone is not duplicate
detection. Preserve raw `request`, `rect`, numeric and snapshot token spelling for
the shared wire parser. Do not deserialize and reserialize through doubles, a
dictionary or typed DTOs before checking lexical rules.

After admission, enforce the 1 MiB bound on the original UTF-8 `request` object
slice, including its whitespace, before an operation-owned clone, `GetRawText`
allocation or semantic-model construction. Use bounded original-byte capture/token
boundaries supplied at the argument layer rather than allocating an arbitrary raw
string merely to measure it. The shared parser consumes those preserved bytes,
rejects duplicate/unknown members and invalid numeric spellings, and produces the
immutable typed request. Other native argument values retain their field bounds;
an optional read snapshot uses the same at-most-1-KiB original JSON bound as CLI.
If SDK binding would discard the required evidence, intercept native arguments
before that binding; do not silently weaken validation or change the public object
shape. Initial SDK envelope allocation is outside Core's budget as stated above;
all native captures/copies, parsing and dispatch share the admitted lifetime.

MCP read-only annotations are true for inspect/region/validate/preview/check and
false for create/edit/export; no tool is generically declared retry-idempotent.
Use existing `ok`, `data`, `error` and MCP text/structured-content conventions.
Inline preview adds one `image/png` image content block beside bounded facts;
image bytes are not repeated inside `data` or the text block. Success uses
`ok:true`/`IsError=false`. A completed invalid validation report preserves the
existing validation convention: `ok:true`, `data.valid:false`, `IsError=false`, CLI
exit 2; callers must inspect `valid`, not confuse a completed assessment with valid
source. A completed PNG mismatch has `ok:false`, comparison result in `data`,
`error.code:"PIXEL_OUTPUT_DRIFT"`, MCP `IsError=true`, CLI exit 6. Execution errors
use `ok:false`, bounded `error`, no partial `data`, MCP `IsError=true`.

## Errors and Exits

Reuse the existing envelope definition in
[CLI output](../../src/FTPlus.AgentGraphicDesign.Cli/Output/OutputFormat.cs)
and [MCP ToolResult](../../src/FTPlus.AgentGraphicDesign.Mcp/Tools/ToolResult.cs).
Do not add global envelope fields or change other commands' behavior. Core uses
typed bounded outcomes for validation/check and the reviewed sealed
`NativePixelException : Exception` for native execution failures. It exposes
immutable `Code`, `Path` (optional schema location) and `OperationIndex` (optional
zero-based index), with its inherited `Message` subject to the bound below. Optional
Core fields are nullable; the wire omits absent error fields. Lower storage failure
codes are retained, not renamed as a generic native failure. This follows the
repository's structured-exception convention without modifying its sealed
`DocumentEngineException`, the vector model or global envelope. Native adapters
map these properties directly into the existing error fields. Cancellation remains
`OperationCanceledException`, not `NativePixelException`. Validation report
diagnostics use those same code/message/location facts; check mismatch is its
typed completed result, not an exception.

An operation-attributable diagnostic has a host-owned JSON location such as
`$.operations[3].rect` and zero-based `operationIndex:3` in the existing error
fields. Internally `opIndex` means that same index; do not introduce a competing
wire `opIndex` field. Request locations are relative to the edit object, not the MCP
transport wrapper. Source diagnostics use bounded locations such as
`$.layers[1].maskSha256`; non-operation errors omit `operationIndex`. Paths are
schema locations, never filesystem path echoes, and at most 512 ASCII characters.
Errors use the reviewed code spellings below and fixed/template messages of at
most 1,024 Unicode scalars and 4,096 UTF-8 bytes, matching storage, with no absolute
paths, untrusted names/values, payload bytes, arbitrary exception details or stack traces.
Truncate neither a success check nor a sample result to hide budget failure.

| Code/outcome | Meaning | CLI exit |
|--------------|---------|----------|
| Success | Valid completed operation, including guarded edit no-op and matching check | 0 |
| `INVALID_ARGUMENTS`, `INVALID_FORMAT` | Missing/unknown fields/options, malformed request JSON/token/rectangle/literal, wrong types, invalid alias/layer operation, empty batch/region, raw input length/EOF mismatch | 4 |
| `UNSUPPORTED_VERSION`, `PIXEL_PROFILE_UNSUPPORTED` | Unsupported native schema/version/profile or payload version/kind; no fallback | 4 |
| Invalid validation report | Intrinsic source failure with `valid:false`; bounded one-error report only for validate | 2 |
| `PARSE_ERROR`, `INVALID_ID`, `INVALID_PROPERTY`, `INVALID_DIMENSIONS`, `PIXEL_CONTENT_MISMATCH` | Preserve storage codes for invalid native source outside validate, including corrupt payloads; intrinsic failures become validate's invalid report instead | 1 |
| `PIXEL_INVENTORY_MISMATCH` | Storage's missing required payload directory/reference; intrinsic failure becomes validate's invalid report instead; the code does not require inventory enumeration or classify unrelated root/history entries as invalid | 1 |
| `REVISION_CONFLICT` | Any exact snapshot component mismatch, for edit or optional read precondition, even a proposed no-op | 1 |
| `PIXEL_BUSY` | Immediate host-slot rejection or expiry of the separate five-second cooperative per-asset writer-lock wait; fixed reason messages distinguish them | 1 |
| `RESOURCE_LIMIT` | Any cap/reservation/work/deadline/revision-increment overflow; valid-but-too-large data is not silently clipped | 1 |
| `PATH_ESCAPE`, `ASSET_PLATFORM_UNSUPPORTED` | Unsafe/unconfined path, protected input/source/output collision, or unsupported qualified filesystem/platform operations | 1 |
| `DOCUMENT_NOT_FOUND` | Absent asset target or required metadata; no implicit initialization | 1 |
| `PIXEL_INPUT_UNAVAILABLE` | Request/raw/snapshot/PNG input absent or unreadable; expected input I/O/access failure, not mismatch | 1 |
| `PIXEL_ALREADY_EXISTS`, `PIXEL_DESTINATION_EXISTS` | Storage's occupied Create target, or existing output without overwrite, respectively | 1 |
| `STORE_UNAVAILABLE` | Expected native source access/staging/flush/publication I/O failure with known prepublication outcome; preserve the primary failure over cleanup failures | 1 |
| `PIXEL_WRITE_FAILED` | Expected explicit PNG staging/flush/publication I/O failure with known prepublication outcome; known publication success is not replaced by cleanup failure | 1 |
| `PIXEL_WRITE_OUTCOME_UNKNOWN` | Publication visibility is genuinely ambiguous; inspect source or check output before retry, never claim rollback | 1 |
| `ARTIFACT_PNG_INVALID`, `ARTIFACT_PROFILE_UNSUPPORTED` | Existing managed structure/decode failure or unsupported PNG profile, mapped by the native wrapper without relaxing its policy | 1 |
| `PIXEL_OUTPUT_DRIFT` | Fully decoded supported PNG has different dimensions and/or raw samples; not compressed byte difference, not source snapshot staleness | 6 |
| `CANCELLED` | Caller cancellation; Core preserves cancellation rather than wrapping it | 130 |

Use `PIXEL_OUTPUT_DRIFT`, not manifest-specific `ASSET_DRIFT`, for this native
single-output mismatch. A malformed PNG whose dimensions happen to differ still
fails as a PNG error, not exit 6. Bounded PNG oversize/work exhaustion is resource
failure. No warning-only mode, repair flag, invalid-output success, or alternate
global CLI exit scheme is introduced.

Check basic no-I/O syntax first, then admission, path/input validity, validated
source and snapshot, indexed operation checks/capture, resource/evaluation and
publication stages as described above. Ordered operations fail at the first invalid
index encountered; do not promise all-error enumeration. Earlier whole-request
shape/limit failures can precede operation semantics. Unanticipated programmer/CLR
failures are not converted into a successful report; existing host fallback
sanitization applies and no new out-of-memory recovery guarantee is made.

## Acceptance Matrix

These are required implementation/qualification tests, not tests claimed to have
run in this docs-only slice. Exercise public CLI and real MCP independently with
the same Core outcomes. Harnesses may create raw fixtures and independently verify
bytes, but must not construct native assets privately to supply missing public edits.

| Area | Required evidence and exact pass/fail condition |
|------|------------------------------------------------|
| Public still workflow | At both 32x32 and 64x64 create, inspect, add/fill/clear/replace a second layer, add/remove a temporary third layer, reorder, attach/edit/remove mask, visibility/fractional opacity, preview/export/check; immediate reload shows final two layers, stable IDs/order and actual exact samples |
| Arbitrary geometry | 37x23 with distinct corners/rows and edge-touching rectangles; 1xN/Nx1 and 1x1; valid non-square/non-power-of-two sizes accepted, no preset restriction; checked positive/axis/area boundaries, no clipping or empty rectangle acceptance |
| Source assignment | Independent raw RGBA/mask fixtures prove row stride and exact EOF, untouched outside samples, zero-alpha hidden RGB preservation, clear RGBA zero versus clear mask zero with unchanged RGBA; replacement is raw, never PNG/native payload decoding |
| Controls/math | Visibility, opacity, reorder and mask coverage produce the independent exact reference samples from the evaluation companion, including partial alpha/coverage, rounding ties and final alpha-zero RGB zero; verify Q24 tableSha256/rounding evidence, native evaluator profile and encoder/comparator identities; no platform/Skia approximation accepted |
| Source tokens | Change RGBA, hidden RGB, visibility, decimal opacity, mask presence/raw mask bytes, order, IDs/names in independent storage fixtures; each semantic source difference changes sourceDigest, including render-neutral changes; changing only revn does not change digest |
| Stale branches | Equal-revn branches differing only in RGBA, visibility, opacity or mask reject old tokens; same digest/different revn and different asset ID reject; exact restored tuple may match again; every guarded no-op still rejects stale input |
| No-op/batch | Same-value fill/control, absent-mask removal, reorder back, fill then restore, attach then remove and add then remove yield stable revision/digest and no source publication when final source equals before; real multi-operation change increments once; result excludes transient IDs and reverted regions |
| References/results | Previous aliases and direct GUID targets resolve; forward/unknown/removed/duplicate alias fail at exact index; createdLayers contains only final survivors with creating index/alias; affected IDs/order and actual per-target bounding differences distinguish removal, controls, mask and RGBA |
| Atomic rejection | Invalid final op, last-layer removal, already-attached mask, mask edit without mask, short/extra raw input or invalid literal rejects all operations with no payload write; intermediate layer 17/logical overflow rejects even if later removed |
| Resource accounting | Inclusive axis/area/logical/working/input/work/JSON/response/PNG bounds and checked integer/revn overflow; charge hidden/duplicate surfaces, repeated/overlapping edits and initializations; 60 MiB versus 80 MiB masked example; no-op does not refund work; over-cap preview requires explicit region |
| Concurrent writes | Same-process overlapping native calls, including different assets/read calls, immediately PIXEL_BUSY; storage's separate five-second cross-process wait is bounded/cancellable and expires with the same PIXEL_BUSY code but its lock-wait reason message; two same-snapshot semantic writers cannot both commit, with REVISION_CONFLICT if loser acquires after winner publishes |
| Lifetime/cancellation | Admission before all operation I/O, deterministic deadline/caller checkpoints, input/copy/composite/decoder/encoder/output/cleanup failures; busy retained while cleanup unwinds and released on every exit; caller token preserved; no abandoned task or false rollback after publication |
| Source read-only | Inspect/region/validate/MCP preview/check and export evaluation change no native contents/mtimes/entries, markers or repair state on success/failure/cancellation; CLI preview/export write only explicitly authorized output/temp, never native source; access times are not guaranteed unchanged |
| Output sample check | Same dimensions/raw RGBA under different valid compression, IDAT split/filter strategy and allowed color tags pass despite file hash differences; changed sample or dimensions in an otherwise fully valid bounded PNG is PIXEL_OUTPUT_DRIFT/6, including nonzero alpha-zero RGB |
| Invalid output | Missing/access-denied/truncated/CRC-invalid/unsupported/bad-zlib/bad-Adler/trailing-member/oversized PNG never green and never mismatch/6; use existing malformed managed-decoder vectors and limits, with native cap enforcement |
| Explicit publication | Default no overwrite, explicit overwrite, destination race/collision/link/type, encode/flush/rename/cleanup failure; old output intact before publication, no partial file accepted, only owned temp cleaned; no claim of multi-output publication or power-loss guarantees |
| Source closure/Git | Metadata plus every required RGBA/mask payload restored using user-managed Git/Git LFS under paused writers preserves IDs/order/controls/raw bytes/digest; missing closure fails even for hidden/out-of-query data; no Git executable/worktree required and no cache overrides restored state |
| History and namespaces | More than 4,096 unreferenced payloads and unrelated root/shared-parent files do not invalidate a small current source or trigger enumeration; direct current-reference/reuse checks still reject missing/corrupt selected objects; fixed hashed-leaf lock and fixed-prefix staging bind to qualified identities; no history quota, directory scan, GC or unrelated-file modification |
| Storage fault injection | Companion protocol failures before/after each payload and metadata publication accept only complete old/new source; invalid-op path writes no payload, I/O failures may retain complete unreachable payloads, no automatic GC; lost reply resolved by inspect |
| Surface parity/safety | Closed JSON and semantic typed invariants, bounded non-echoing errors at $.operations[i] with operationIndex, paths rooted at invocation/MCP root not request/asset directory, existing envelopes/exits, cold-start no source initialization; later discovery tests cover exactly reviewed registrations |
| Wire fidelity | CLI and original MCP argument capture reject duplicate/escaped-duplicate keys, exponent/signed integer tokens and excess opacity fractional digits before lossy binding; exact revn above 2^53 survives; direct typed decimals are judged by exact value, not imagined JSON spelling; original request byte/depth bounds and SDK/Core allocation boundary are explicit |
| Unicode names | Storage-valid joined emoji, combining sequences and other allowed scalars round-trip without normalization; reject storage's explicit control/bidi ranges, malformed Unicode and scalar/UTF-8 bounds, not all Cf characters |

## Review Closure

Reviewed choices: separate `NativePixelService`; the eight CLI/MCP bindings;
typed batches and private raw-file ingestion; final-state guarded no-op; absent-mask
removal as no-op; `Layer 1` as initial layer name; optional snapshot-file/read-token
preconditions only on evaluated outputs; fail-first validation with exit 2; and
`PIXEL_OUTPUT_DRIFT`/exit 6 only for completed decoded comparisons. These choices
are approved implementation-contract decisions, not implemented or registered
native capabilities.

Joint API/storage review and independent evaluation review are complete for this
contract, including the exact source rules, API bindings and Q24/hash/sample
profile definitions. Implementation must still qualify the actual OS/filesystem
primitives and identity probes, MCP original-JSON lexical capture, measured memory
reservations, and runtime/public-surface fixtures. Contract approval is not passing
runtime evidence or a functional release claim. The broader native pixel and animation work, full artifact admission and
coherent multi-output publication remain open.
