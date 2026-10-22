# Preserved managed-local native still import

**Status**: normative v1 contract, implemented on 2026-10-10 after the 0.3.0 release; not
in the published 0.3.0 packages. Authority:
[native pixels](../../specs/parts/p1-core-engine/native-pixels.md).

AGD imports one provided-artifact v1 PNG still into a new native asset or an independent
top layer of an existing same-size asset. Shared Core, CLI and MCP preserve one capture
for hashing, structure checks, decoding, native framing and historical input retention.
Source integration and package qualification are distinct from publication and consumer
adoption.

## Qualified profile and mapping

`agd.pixel-still-import.managed-local.v1` supports .NET10 Linux x64, trusted/quiescent
source and target filesystems, untrusted bounded PNG/metadata content, a pure managed
decoder and one whole-operation cooperative60s native lifetime. It is the explicit local
interpretation lane permitted by the interoperability qualification clause, not full artifact
admission, an OS sandbox, hostile-writer revocation, hard deadline, process-RSS or crash/
power-loss containment, provenance, rights, quality or source-freshness approval.
Discovery reports nativeImageImport true for this profile, providedArtifactAdmission
false; results always include admitted false and provenanceVerified false.

Exactly one untimed still/file under `png8-rgba-srgb-straight-fixed-canvas-v1` is supported.
Metadata<=1MiB, encodedPNG<=32MiB, axes1..8192, area<=4,194,304; native16layers/64MiB
logical-source limits and256MiB conservative cumulative reservation apply. Shared native
host slot rejects PIXEL_BUSY immediately without queuing. Original request1MiB/snapshot1KiB
limits are separate from8MiB MCP transport ingress. Reservations precede capture/parser,
decoder history/rows/Huffman/output, framing, verification and bounded results.

The same codec yields exact sRGB-encoded straight RGBA8, including hidden RGB atalpha0,
only after complete zlib/DEFLATE/Adler/filter/EOF validation. No resampling, trimming,
quantization, premultiplication or implicit linked layers. Mint independent native asset/
layer GUIDs; append is topmost, visible, opacity1, no mask, full canvas. Supplied pivots
are historical claims, not native placement. Native metadata/payloadv1 and Q24 linear-over/
mask/opacity evaluation stay unchanged. Flattened transparent output clears hidden RGB;
editable sources preserve it. Opaque red below green alpha128 evaluates to `#bbbc00ff`;
arbitrary encoded-byte compositors may differ.

## Shared API and original-byte surfaces

- `NativePixelService.ImportAsync(ImportPixelAssetRequest)`: absolute assetDirectory,
  descriptorPath, name, layerName; exclusive new asset, revision1.
- `ImportLayerAsync(ImportPixelLayerRequest)`: assetDirectory, descriptorPath,
  expectedSnapshot, layerName. Under the native lock, reload/validate the complete current
  closure and compare assetId/revn/sourceDigest before supplier reads; append advances
  once. Any mismatch rejects. Exact restored tokens can become eligible again; no
  anti-rollback, monotonic runtime history or durable retry identity exists.
- File/wire entry points preserve original UTF8 inside one admitted lifetime before
  typed parsing, probes, reads, publication and cleanup. Optional Core pathRoot is
  host-supplied, never a caller-selectable MCP argument or bypass.
- CLI `pixels import <ASSET> --descriptor <FILE> --name <NAME> --layer-name <NAME>`;
  `pixels import-layer <ASSET> --request <FILE>` with closed
  `{descriptorPath,expectedSnapshot,layerName}`; paths resolve from invocation root.
- MCP `import_pixel_asset(assetDirectory,descriptorPath,name,layerName)` and
  `import_pixel_layer(assetDirectory,request)`; configured-root confinement, mandatory
  original capture, fail closed if absent. Import/edit have separate closed shapes.
- Create result `{state,initialLayerId,import}`; append
  `{previousSnapshot,state,layerId,import}`. Bounded import metadata: recordSha256,
  asset-relative recordPath, metadataDigest, descriptorSha256, encodedSha256, rgbaSha256,
  pixelsSha256, decoderProfile, interpretationProfile, execution and false qualifiers.
  No source PNG/Base64 or supplier locator enters normal metadata.

CLI file and direct Core wire ingestion enforce the original byte-size bound and capture/
copy request bytes under the single native operation scope, then reject unknown/duplicate/
escaped-duplicate/null fields and invalid original GUID/hash/integer tokens before
constructing the typed import request or reading supplier content. MCP additionally
validates captured original arguments before SDK binding and operation-owned request
copying. Typed Core calls enforce semantic invariants; they do not reconstruct JSON to
claim validation of original raw-token spelling.
Lexical confinement precedes probes. Artifact codes remain lossless/bounded through
NativePixelException. Shared CLI mapping stays0success,4INVALID_ARGUMENTS/INVALID_FORMAT/
UNSUPPORTED_VERSION/PIXEL_PROFILE_UNSUPPORTED,130caller cancellation,1others including
RESOURCE_LIMIT, ARTIFACT_PROFILE_UNSUPPORTED and source/storage STORE_UNAVAILABLE.
Output-only PIXEL_WRITE_FAILED is not used for native source failures.

## Historical format and publication

Reserve `imports/<recordSha256>/record.json` and `bundle/descriptor.json` plus the PNG at
its original portable relative path under bundle. Descriptor locator does not define
identity. PNG paths beginning `descriptor.json/` are an import-only unsupported subset,
rejected before PNG allocation/staging; provided metadata generally still allows them.
Original descriptor preserves inert provenance claims.

Closed record schema `agd.pixel-still-import`, digit-only version1, <=16KiB, depth<=4;
no BOM/comments/null/unknown/duplicate keys/trailing commas/extra documents. Canonical order:

| Object/order | Required field | Grammar/value |
|---|---|---|
| root1..7 | schema, version, inputProfile, decoderProfile, interpretationProfile, execution, limitsProfile | `agd.pixel-still-import`,1,`png8-rgba-srgb-straight-fixed-canvas-v1`,`agd.png-rgba8-managed.v1`,`agd.pixel-still-import.srgb-straight-source.v1`,`managed-in-process`,`agd.pixel-still-import.managed-local.v1` |
| root8/source1..6 | source.assetId, stillName, width, height, pngPath, pivot | supplied IDs `[a-z][a-z0-9_-]{0,63}`; verified axes/area above; original provided portable path (<=240chars,<=8segments) plus prefix restriction; closed pivot x,y exact provided decimals [-1000000,1000000],<=6fractional digits, unverified placement claims |
| source7..11 | descriptorSha256, metadataDigest, encodedSha256, encodedByteLength, rgbaSha256 | exact original descriptor SHA; existing canonical metadata-domain identity; observed encoded SHA matching claim; observed digit-only length1..33554432 matching claim; observed raw row-major RGBA SHA |
| root9/mapping1..3 | mapping.assetId, layerId, pixelsSha256 | minted nonzero distinct lowercaseD GUIDs; observed full framed payload SHA including24byte header |
| root10..11 | admitted, provenanceVerified | booleanfalse |

All hashes lowercase64hex; raw string tokens<=1536bytes, numeric<=32bytes; logical strings/
path/GUIDs validated ASCII. Unsupported schema/version/profile fails explicitly. Compact
UTF8, no BOM/finalnewline, fixed order, literal permitted ASCII, invariant digit-only
integers; pivots have no exponent/trailingzeros, normalize negativezero without rounding.
No omitted/optional/null fields, absolute locator, self-hash recursion, verified artifact-
set identity or quality-evidence key. Address is lowercaseSHA256 of ASCII
`agd.pixel-still-import.record.v1\n` followed by canonical bytes. Independent
[literal vector](../../tests/FTPlus.AgentGraphicDesign.Core.Tests/Pixels/Fixtures/import-record-v1.json):
1020canonical bytes excluding storage-finalLF, identity
`aa4104fdea22ccef905a57a44228b7206f693602e4a4384f180307c6eb6185b5`.
Synthetic hashes do not claim artifact verification. The archived original technical
contract/review history records qualification; this canonical document owns accepted behavior.

Lock paths reject static links/special/wrong-type entries before open and qualify opened/
named regular single-link inode/device binding. Only actual contention errors retry under
the existing5s budget. Atomic exclusive mkdir establishes ownership; cleanup requires
successful ownership acquisition. No preexisting occupied tree is adopted/scavenged/deleted.

Under that lock, stage the complete record/original bundle in reserved
`.agd-pixels-import-stage-<nonce>`, flush/close and stream-verify bounded exact inventory/
intended bytes. Publish the complete addressed directory atomically without replacement;
occupied addresses reuse only exact inventory/bytes, never equivalent parsed metadata or
decoded pixels. No history-wide scan/repair/GC. Publish framed payloads before metadata
last; new assets publish one complete staged directory no-replace. Preconstruct bounded
results before native visibility; known commit success wins, unknown outcomes are not
falsely reported rolled back.

Pre-commit failures preserve prior authoritative references. Complete unreferenced history
may remain; orphan records describe intended mapping, not proof of commit. Native validation
certifies current raw closure, not all history. Version metadata/current required payloads
and original imports in user-managed project files; restoration uses those bytes, not
supplier paths, a cache, selection DB or automatic Git. No generic ledger/job, animation,
tile or Blender runtime is introduced.
