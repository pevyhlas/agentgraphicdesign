# CLI/MCP Surface Matrix

This matrix describes stable `v0.3.0`, published on 2026-10-01 before this repository became
public (see the [CHANGELOG](../CHANGELOG.md)).
Bounded artifact checks/native stills first shipped in `0.2.0-preview.2` and are
included in this stable release. Document operations use
IDocumentEngine; artifact checks use separate shared Core services. Argument,
persistence, confinement, output and revision contracts differ; full parity is not claimed.

[`surface-manifest.json`](surface-manifest.json) records the complete CLI command, MCP tool, and MCP resource inventory. `CliMcpParityTests` extracts executable registrations and embedded resource links and fails if the manifest drifts. Inventory parity does not establish behavior parity.

## Capability Matrix

| Capability | CLI | MCP tool (method) | Relationship |
|------------|-----|------------|--------------|
| Capability discovery | `--version`, command `--help` | `get_capabilities` | MCP adds compact version/root/platform/domain/limit/CLI-only discovery over executable registrations |
| Create document | `create [--template <id>]` | `create_document` (`CreateDocument`) with optional `template` and `outputPath` | Shared engine/catalog; both select a file path, MCP confines it; omitted MCP outputPath uses the GUID store. MCP returns locator/rootId/revn |
| Inspect summary/tree | `inspect` | `load_document`, `get_document_json` | Different output contracts; MCP has no rendered tree |
| Validate | `validate [--profile web-icon]` | `validate_document` (`ValidateDocument`) | Shared default/profile validator; response envelopes differ |
| List documents | `list-documents` | `list_documents` (`ListDocuments`) | Similar capability; MCP confines enumeration to its working directory |
| List nodes | `list` | `list_nodes` (`ListNodes`) | Shared engine listing; MCP adds subtree nodeId, relative maxDepth and offset/limit pagination (default 100, max 1000); CLI also renders a tree |
| Add node | `add [--fill-rule <RULE>]` | `add_node` (`AddNode`) with `fillRule` | Shared mutation; both expose an expected revision and an optional `nonzero`/`evenodd` fill rule for path and polygon nodes |
| Update node | `update [--fill-rule <RULE>]` | `update_node` (`UpdateNode`) with `fillRule` | Shared mutation; both expose an expected revision and an optional fill rule (`__clear__` resets it) |
| Delete node | `delete` | `delete_node` (`DeleteNode`) | Shared mutation; both expose an expected revision |
| Move node | `move` | `move_node` (`MoveNode`) | Shared mutation; both expose an expected revision |
| Reorder node | `reorder` | `reorder_node` (`ReorderNode`) | Shared mutation; both expose an expected revision |
| Query bounds | `bounds [--node ID] [--font-dir <DIR>]… [--no-system-fonts] [--strict-fonts]` | `get_bounds` (`GetBounds`) with `fontDirs`, `systemFonts`, `strictFonts` | Shared engine query; local and world boxes, clipping and text-measurement diagnostics; the font options resolve fonts for measurement exactly as a render would (directories → bundled Inter → system fonts), so measured text matches rendered text; MCP confines each font directory |
| Align nodes | `align --nodes … --mode … [--relative-to …]` | `align_nodes` (`AlignNodes`) | Shared mutation of `transform.x/y`; both expose an expected revision |
| Distribute nodes | `distribute --nodes … --axis … [--mode …] [--gap N]` | `distribute_nodes` (`DistributeNodes`) | Shared mutation of `transform.x/y`; both expose an expected revision |
| Fit frame | `fit --frame ID [--padding N]` | `fit_frame` (`FitFrame`) | Shared mutation of frame size/position and child offsets; both expose an expected revision |
| Atomic multi-operation patch | `patch` | `apply_patch` (`ApplyPatch`) | Shared engine patch with one save/revision; both expose an expected revision; CLI reads a JSON array from file/stdin/inline, MCP takes an actual operations array and additionally confines image sources ([operation reference](patch-operations.md)). Align/distribute nodeIds also use actual arrays in 0.3 |
| Export SVG/raster | `export [--profile web-icon]` | `export_document` (`ExportDocument`) | Same profile preflight; MCP confines paths and clamps raster dimensions/scale |
| Raster font policy | `export -t png\|jpeg\|webp [--font-dir <DIR>]… [--no-system-fonts] [--strict-fonts]` | `export_document` with `fontDirs`, `systemFonts`, `strictFonts` | Same renderer policy (directories → bundled Inter → system fonts) and `FONT_SUBSTITUTED`/`FONT_NOT_FOUND` outcomes; MCP confines each font directory to its working directory; both reject the options for `svg` |
| Export SVG symbol fragment | `export --symbol <ID>` | `export_document` with `symbolId` | Same shared-engine symbol mode (svg only; rejects scale/width/height) |
| Export a single sub-node | `export --node <ID>` | `export_document` with `nodeId` | Same shared-engine sub-node export ([D-10](../specs/architecture/decisions.md#d-10-bounded-sub-node-export)): emits the subtree rooted at one node as a standalone svg/raster image cropped to its own bounds, drawn in isolation (ancestor transforms/clipping not applied); honours scale, background and raster font policy; rejects `--symbol`/`--width`/`--height`; `NODE_NOT_FOUND`/`NODE_NO_BOUNDS` outcomes |
| Assemble symbol sprite sheet | `sprite <FILES…> -o <PATH>` | No dedicated tool; call `export_document` with `symbolId` per document and wrap the fragments | CLI-only batch convenience over the shared symbol mode; a multi-file batch is a script-level concern in MCP |
| Asset pipeline | `assets build <MANIFEST>`, `assets check <MANIFEST>` | No build/check batch tool; these remain CLI-only | Shared engine exports and sprite composer; manifest ownership, strict validation, and read-only drift checks are CLI build concerns ([contract](asset-pipeline.md)) |
| Provided-artifact preflight (0.2 preview) | `assets preflight <DESCRIPTOR> [-f text\|json]` | `preflight_artifact` | Shared exact inventory/encoded hash/CRC structure checks; confined descriptor; not decoding/admission ([contract](provided-artifacts.md#input-preflight)) |
| Provided-artifact verification (0.2 preview) | `assets verify <DESCRIPTOR> [-f text\|json]` | `verify_artifact` | Shared owned-byte/managed DEFLATE/filter/RGBA sample checks; not preservation/admission ([reference](provided-artifacts.md#decoded-sample-verification)) |
| Export SVG with `currentColor` | `export --current-color <COLOR>`, `sprite --current-color <COLOR>` | `export_document` with `currentColor` | Same shared-engine substitution (svg only; exact RGBA match after token resolution; gradient stops untouched); raster formats reject the option on both surfaces |
| Inline PNG preview | No dedicated command; use `export -t png` | `render_preview` (`RenderPreview`) | MCP defaults to an aspect-preserving <=512px thumbnail without upscaling; explicit scale/viewports remain supported and effective output is capped at 2048. All inline images use the SDK decoded-byte factory to emit valid base64 PNG content |
| Import SVG | `import` | `import_svg` (`ImportSvg`) | Shared importer; MCP confines input/output paths |
| Boolean path operation | `path bool [--fill-rule <RULE>]` | `path_boolean` (`PathBoolean`) with `fillRule` | Shared engine operation; both default to `nonzero` and accept `evenodd` |
| Stroke offset | `path offset [--fill-rule <RULE>]` | `path_offset` (`PathOffset`) with `fillRule` | Shared engine operation; both default to `nonzero` and accept `evenodd` |
| Union paths | `path union [--fill-rule <RULE>]` | `path_union_all` (`PathUnionAll`) with `fillRule` | Shared engine operation; both default to `nonzero` and accept `evenodd` |
| Set color token | `tokens set-color` | `set_color_token` (`SetColorToken`) | Shared mutation; both expose an expected revision |
| Remove color token | `tokens remove-color` | `remove_color_token` (`RemoveColorToken`) | Shared mutation; both expose an expected revision |
| List color tokens | `tokens list-colors` | `list_color_tokens` (`ListColorTokens`) | Shared engine query |
| Apply palette | `tokens apply-palette` | `apply_palette` (`ApplyPalette`) | Shared mutation; both expose an expected revision |
| List palettes | `tokens list-palettes` | `list_palettes` (`ListPalettes`) | Shared engine query |
| Import DTCG tokens | `tokens import` | `import_dtcg_tokens` (`ImportDtcgTokens`) | Shared mutation with expected revision; MCP confines the token path |
| Export DTCG tokens | `tokens export` | `export_dtcg_tokens` (`ExportDtcgTokens`) | Shared engine operation; MCP confines the output path |
| Design lint | `lint [--profile web-icon] [--target-size PX] [--strict-grid] [--fail-on info\|warning] [--font-dir <DIR>]… [--no-system-fonts] [--strict-fonts]` | `lint_document` (`LintDocument`) with `fontDirs`, `systemFonts`, `strictFonts` | Shared advisory rules over `IDocumentEngine.Lint`; seven codes `LINT001`–`LINT007`; never blocks export; CLI adds a `--fail-on` exit code (`5`); the font options measure text under the same policy a render would, so contrast/geometry findings match the rendered result; MCP confines each font directory |
| Contact sheet | `contact-sheet <FILES…> -o <PNG> [--columns N] [--cell PX] [--gap PX] [--background COLOR] [--labels] [--font-dir <DIR>]… [--no-system-fonts] [--strict-fonts]` | `render_contact_sheet` (`RenderContactSheet`) with `fontDirs`, `systemFonts`, `strictFonts` | Shared grid compositor; CLI writes a PNG file, MCP returns an inline image block (EXPERIMENTAL); the font options apply to every tile render and every label, so a sheet matches a normal export of each document; MCP confines each font directory |

## MCP-Only Operations

- `load_document` (`LoadDocument`) accepts an ID or confined path and returns document metadata.
- `get_document_json` (`GetDocumentJson`) returns canonical document JSON, or the subtree selected by optional nodeId, plus revn inside the MCP result envelope.
- `render_preview` (`RenderPreview`) returns an inline MCP image content block plus metadata, including font `diagnostics`.
- Read-only resources (`resources/list`, `resources/read`), registered through `[McpServerResource]` in `Resources/DocumentResources.cs` and embedded into the tool assembly from the repository files:
  - `agd://schema` (`application/schema+json`) mirrors `schemas/agd-document.schema.json`.
  - `agd://format` (`text/markdown`) mirrors `docs/format.md`.
  - `agd://agent-guide` (`text/markdown`) mirrors `docs/agent-guide.md`.

  The CLI has no equivalent; the same files are read from the repository. `agd-mcp --help` lists the URIs, and the MCP tests compare each served resource with its source file.

The CLI loads file paths as part of each command instead of maintaining MCP-style document context.

## Artifact Preflight Boundary

```sh
agd assets preflight artwork/hero/descriptor.json -f json
agd assets verify artwork/hero/descriptor.json -f json
agd assets verify --help
```

MCP: `preflight_artifact` or `verify_artifact` with
`{"descriptorPath":"artwork/hero/descriptor.json"}`.
Use a dedicated tracked bundle containing only the descriptor, declared PNGs and
necessary directories, not this repository's ignored `artifacts/` directory. Preflight
on either surface returns the shared envelope with `verification: "encoded-integrity-and-png-structure"`,
`pngPolicy: "agd.png-rgba8-structure.v1"`, `pixelsDecoded: false`, `admitted: false`
and `provenanceVerified: false`. The unchanged `metadataDigest` is not a verified
artifact identity. Preflight never inflates IDAT; invalid compressed data with matching
outer CRCs and claimed length/hash can pass.

The separate verification operation checks hash, CRC/structure and decoded samples
on the same once-captured private bytes. It validates the complete zlib/DEFLATE
stream, including advertised window/history, headers, checksum, termination and
absence of trailing bytes/members, then reverses filters 0..4 without premultiplying,
clearing hidden RGB or converting color. Matching-CRC/hash invalid IDAT that passes
preflight fails verification with `ARTIFACT_PNG_INVALID`.

Verification returns the same eight flat metadata facts plus `files` records
(`fileSha256`, `rgbaSha256`), sorted ordinally by encoded hash. Its qualifiers are
`verification: "decoded-rgba8"`, `decoderProfile: "agd.png-rgba8-managed.v1"`,
`execution: "managed-in-process"`, `pixelsDecoded: true`, `admitted: false`, and
`provenanceVerified: false`. Raw sample hashes describe observations under the
dimensions/profile, not global artifact identity, preservation or rights approval.
One verification is active per host; competitors fail immediately with
`ARTIFACT_VERIFY_BUSY`. A 60-second whole-Core-call cooperative deadline and 65,536
DEFLATE blocks per unique file bound work, not process RSS or hard termination.
Original MIT C# code adds no package/native codec, copyleft host tool or OS sandbox.

MCP confinement rejects lexical root escapes before path probes and finishes before
Core inventory/content reads; it can itself inspect attributes. Both surfaces
require a trusted, quiescent filesystem: pause producers and Git writers. Captured
bytes are not an atomic source-tree snapshot, durable receipt or preservation.
The existing Linux x64 inspection qualification is reused, not new Windows
qualification. Neither operation adds jobs, selection, sealing, preservation,
document-store initialization, writes or new MCP resources; pure metadata APIs and
document/metadata formats remain unchanged. See [preflight details](provided-artifacts.md#input-preflight)
and [verification bounds](provided-artifacts.md#verification-bounds-and-errors)
for limits, errors and filesystem qualifications. Both use CLI exits `0` for success,
`4` for invalid arguments/output format/version, `1` for other failures,
and `130` for cancellation; neither uses asset-drift exit `6`.

The [package filters](provided-artifacts.md#package-qualification) select artifact
and native CLI tests, real MCP discovery/verification/raw-wire tests, and readiness
vector/native guide journeys. Package verification also exercises approved-bundle
bootstrap and a mixed SVG/PNG/sprite manifest. Actual revision-scoped results belong
to the feature verification record; filter configuration is not passing evidence.

## Native Pixel Assets (P6)

The native pixel surface authors and evaluates flat RGBA8 assets held as a source
directory (`asset.agd-pixels.json` plus content-addressed `payloads/`), separate from
the vector document format and its `IDocumentEngine`. Both surfaces call the shared Core
`NativePixelService`; a `<DIRECTORY>`/`assetDirectory` argument names the asset. Every CLI
command accepts `-f|--format text|json` (default `text`); MCP carries the native result in
the shared tool envelope. CLI relative paths resolve against the invocation working
directory; MCP confines every path to the server working directory. On MCP the object-valued
arguments (`request`, `rect`, `expectedReadSnapshot`) are consumed from the original captured
tool-call bytes before typed binding, so duplicate members, the 1 MiB request byte bound and
exact numeric spelling (for example `revn` above 2^53) survive to the shared wire parser.

| Capability | CLI | MCP tool (method) | Relationship |
|------------|-----|-------------------|--------------|
| Create pixel asset | `pixels create <DIR> --name <NAME> --width <INT> --height <INT>` | `create_pixel_asset` (`CreatePixelAsset`) | Atomic no-replace directory publication with one initial layer; writes source exclusively |
| Import provided still | `pixels import <DIR> --descriptor <FILE> --name <NAME> --layer-name <NAME>` | `import_pixel_asset` (`ImportPixelAsset`) | Development source; one captured PNG still, preserved original inputs/mapping and independent native raw layer; managed local, not admission |
| Import independent layer | `pixels import-layer <DIR> --request <FILE>` | `import_pixel_layer` (`ImportPixelLayer`) | Development source; exact original-byte closed request, complete snapshot CAS, same canvas, metadata-last publication |
| Inspect pixel asset | `pixels inspect <DIR>` | `inspect_pixel_asset` (`InspectPixelAsset`) | Read-only metadata, layers and the captured `{assetId,revn,sourceDigest}` token |
| Read region samples | `pixels region <DIR> --layer <GUID> --target pixels\|mask --rect <X,Y,W,H>` | `inspect_pixel_region` (`InspectPixelRegion`) | Read-only actual bounded RGBA8/A8 samples; no clipping, wrapping or empty rectangles |
| Guarded edit batch | `pixels edit <DIR> --request <JSON_FILE>` | `edit_pixel_asset` (`EditPixelAsset`) | Typed 1..64-op batch with `expectedSnapshot`; metadata-last atomic write; `REVISION_CONFLICT` on a stale token |
| Validate | `pixels validate <DIR>` | `validate_pixel_asset` (`ValidatePixelAsset`) | A completed invalid report is `ok:true`, `data.valid:false`, `IsError=false`, CLI exit `2` (not an execution error) |
| Preview PNG | `pixels preview <DIR> --output <PNG> [--rect <X,Y,W,H>] [--overwrite] [--expected-read-snapshot <JSON_FILE>]` | `preview_pixel_asset` (`PreviewPixelAsset`) | Read-only bounded evaluation, no source write; the CLI writes a confined file, MCP returns one inline `image/png` block (bytes not repeated in `data`) |
| Export PNG | `pixels export <DIR> --output <PNG> [--overwrite] [--expected-read-snapshot <JSON_FILE>]` | `export_pixel_asset` (`ExportPixelAsset`) | Whole-canvas evaluation to a confined output path; `PIXEL_DESTINATION_EXISTS` unless `overwrite`; no source write |
| Check output | `pixels check <DIR> --output <PNG> [--expected-read-snapshot <JSON_FILE>]` | `check_pixel_export` (`CheckPixelExport`) | Compares an existing PNG against a fresh evaluation; a completed mismatch is `ok:false` with the comparison in `data`, `error.code:"PIXEL_OUTPUT_DRIFT"`, MCP `IsError=true`, CLI exit `6` |

CLI exit codes: `0` success; `2` a completed invalid validation report; `6` output drift;
`4` invalid arguments, output format or unsupported version/profile; `1` every other native
execution failure; `130` cancellation. MCP read-only annotations are true for inspect, region,
validate, preview and check, and false for create, edit and export. Inspect, region, validate,
preview and check never mutate the source on success, failure or cancellation; preview and
export write only their explicitly authorized output/temp paths. Registering the eight MCP
tools activates the original-byte admission gate, which rejects duplicate/escaped-duplicate
argument keys and exponent/signed/over-precise numeric tokens before typed binding.

## Concurrency

An incremented `revn` does not itself guarantee optimistic concurrency. The save layer compares revisions only when the surface supplies `expectedRevn`.

- CLI add/update/delete/move/reorder/align/distribute/fit/patch and token writes: expected revision supported.
- MCP add/update/delete/move/reorder/align/distribute/fit/patch and token writes: expected revision supported.
- Create/import/export/query operations have different persistence semantics and are not classified as mutation-parity assertions.

## Contract Boundary

The registration test proves that the inventory matches executable registrations. It does not prove argument-level or output-level equivalence. Command/tool unit and integration tests remain responsible for each adapter's behavior.
