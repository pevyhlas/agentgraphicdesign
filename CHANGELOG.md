# Changelog

All notable changes to Agent Graphic Design are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

Source builds of this section still report version `0.3.0` followed by `+<commit>`; quote the
full `agd --version` string when reporting a problem.

### Added

- Internal straight RGBA rendering and a bounded `RgbaImage` for upcoming review sheets and
  analysis. Raw buffers are never accepted as CLI, MCP or asset-manifest file formats.
- Pre-refactor contact-sheet baselines across Skia, CLI and MCP, including directory fonts,
  and exhaustive characterization of Skia's unpremultiplication against managed PNG decode.
- A concise public README, retained complete usage reference, and release-readiness checklist.
- A developer-only, standard-library Python CI guard for common credential signatures,
  non-example home paths, tracked runtime/credential files and public entry-page links.
  Matching values are withheld; this is limited regression protection, not a complete PII audit.
- **Preserved still import** (development source; not in the 0.3.0 packages): CLI
  `agd pixels import` and `agd pixels import-layer`, and MCP `import_pixel_asset` and
  `import_pixel_layer`, import one untimed provided-artifact v1 PNG still into a new native
  asset or append it as an independent top layer of a same-size asset under an expected
  snapshot. The original input and its mapping are preserved with the asset; decoding is the
  managed in-process verifier on the same captured bytes; results always report
  `admitted: false` and `provenanceVerified: false`. Bounds: one file, 1 MiB metadata, 32 MiB
  encoded PNG, axes up to 8192 and 4,194,304 pixels, a 256 MiB cumulative reservation and a
  60-second cooperative deadline, sharing the native `PIXEL_BUSY` slot. Qualified on .NET 10
  Linux x64 with a trusted, quiescent local filesystem. `get_capabilities` now reports
  `nativeImageImport: true` and a `nativeStillImport` block. See
  [still import](docs/native-still-import.md) and its
  [contract](docs/contracts/native-still-import-v1.md).

### Fixed

- Sub-node raster crops now use the requested font directories and system-font policy.
  SVG/raster exports report non-fatal bounds diagnostics through CLI and MCP. Hidden text
  does not acquire a strict-font failure during measurement. The shared private-copy builder
  preserves existing SVG/PNG geometry crops, including legacy centered-stroke clipping.
  Transient text measurement now refuses more than 64 million cumulative measured characters
  with `RESOURCE_LIMIT`, including repeated line-wrap probes.
- Raster encoder and export maps now use explicit format cases; existing PNG/JPEG/WebP
  exports and legacy contact-sheet bytes are unchanged.
- The exact SDK and restore-audit pins move together from 10.0.112 to the 10.0.113 patch.
  Install that SDK before running repository commands; no runtime package or format changes.
- Vendored Product-Kit moves to public 1.1.1 with manifest integrity, helper fixes, ignored
  retrospective data and complete upstream/Spec Kit MIT notices. No kit submodule is required.
- Sub-node export (`agd export --node`, MCP `export_document` with `nodeId`) now carries
  the document's design tokens, so token-referenced paint and typography resolve as in the whole
  document instead of exporting unresolved.
- Documents and patch subtrees load with a node's `type` discriminator in any property
  position, not only first.
- `agd validate` on a file no longer creates a `.agd` store in the invoking directory, so it
  also works in a read-only directory or inside an MCP store.
- Export and preview size options (`--scale`, `--width`, `--height` and the MCP `scale`,
  `width`, `height` arguments) now describe what they do: a uniform scale, and a logical
  viewport size before scale that crops or pads rather than fitting the geometry.
- MCP `import_pixel_layer` declared its `request` parameter as a boolean JSON Schema
  (`true`), which TypeScript MCP SDK clients (e.g. OpenCode) reject, hiding every AGD tool.
  It is now a described schema object; a protocol test rejects boolean subschemas on all tools.
- `agd path offset|bool|union` no longer resolve the store-backed engine: they run on plain
  input without creating `./.agd` or failing inside an existing store tree.

### Changed

- **Error-code cutover:** nested/overlapping document stores now fail with `STORE_CONFLICT`
  (was `INVALID_PROPERTY`), naming the enclosing store root or the contained store's relative
  location; a refused store no longer leaves an empty `./.agd` behind.
- **Exit-code cutover:** failures while a CLI command is constructed (store conflict, access
  denied, I/O) report their own code with exit 1 instead of `INVALID_ARGUMENTS: Could not
  resolve type '...Command'` with exit 4.
- **First public release (fresh history).** The repository is published with a single
  initial commit; earlier development history, pull requests and CI runs belong to the
  pre-public history and are not linked here.
- CI moved to GitHub-hosted runners with the SDK installed from `global.json`, and runs for
  pull requests against any base branch, forks included. `RepoChecks workflows` (in CI and
  `scripts/validate-local.sh`) fails when a workflow leaves GitHub-hosted runners, uses
  `pull_request_target`, drops its top-level `permissions` block or uses an action not pinned
  by full commit SHA with a version comment.
- Contribution and security process for the public repository: an "Experimental" note in the
  README, CONTRIBUTING for outside contributors (issue first, draft pull requests,
  maintainer-only merges), SECURITY (private vulnerability reporting or agd@petrvyhlas.com,
  acknowledgement within 7 days), the Contributor Covenant 2.1 code of conduct, issue forms,
  a pull request template, CODEOWNERS and weekly Dependabot updates for the pinned actions.
- **Lifecycle: experimental.** The project's status label is now *experimental* (formerly
  "pre-release"). Version numbering is unchanged: 0.x releases follow SemVer, so formats and
  APIs can still change between minor versions, and vector document format 1 changes stay
  additive. Package descriptions say the same.
- Product specification moved to the Product-Kit 1.1 layout: an executive `specs/product.md`
  with journeys, requirements, qualities, constraints, non-goals and the lifecycle under
  `specs/product/`; an architecture overview `specs/architecture.md` with topics and decisions
  under `specs/architecture/`; two parts, P1 Core engine and P4 CLI and MCP server, in
  `specs/parts/`; open work as change records in `specs/changes/`; and a condensed history in
  `specs/archive.md`. Former ADR-001…ADR-011 are decisions D-01…D-11; former parts P2, P3 and
  P5 to P12 became P1 components or the operations topic, and native pixel IDs P6.B and P6.I
  moved to P1 (listed in `specs/architecture/retired.md`). The shipped v1 contracts for native
  pixels, still import, provided artifacts and in-process producers moved to `docs/contracts/`;
  the 0.x canon and feature records left the tree. The lifecycle stage is Public preview.
- Product-Kit 1.1.0 workflow files are vendored as copies under `.prdkit/`; the repository
  does not track the kit source, and the adoption gate checks the manifest hash and the
  installed command surface. Building and testing AGD never needs the kit source.

## 0.3.0 - 2026-10-01

### Agent-readiness continuation — 2026-09-30

- Readable confined MCP creation paths with locator/rootId/revn, bounded subtree/paged
  inspection, canonical subtree JSON and compact get_capabilities discovery.
- **MCP wire cutover:** patch operations and align/distribute nodeIds are actual arrays,
  replacing string-encoded inputs. Existing CLI contracts/document formats are unchanged.
- Vector previews default to <=512px thumbnails without upscaling; explicit scaled
  dimensions are bounded correctly. Fixed all three inline image tools to use the SDK
  decoded-byte factory, producing valid base64 PNG data verified over real stdio.
- Approved-bundle isolated CLI/MCP bootstrap, exact installed-package manifest/wire
  qualification, consumer uptake guidance and an external-producer interoperability
  analysis. No consumer uptake or admission is claimed.

First stable release of the 0.3 "reuse and sheets" line. It was released before this
repository became public, and its packages are not available here; build from source instead.

**Scope.** This release delivers a *subset* of the 0.3 roadmap line: the reusable asset pipeline (shipped in 0.2), plus the additions below — unified font policy across the read paths, bounded sub-node export, and bounded local SVG `<use>`/`<symbol>`/rectangular `clip-path` import. It does **not** complete the 0.3 milestone: persisted reuse **instances with token overrides** (a document-format change) remain deferred to a separate architecture review, and atlas packing, target adapters, animated-sprite and tile authoring are future work. The document format is unchanged (version `1`).

**Supported platform.** Qualified on **.NET 10 Linux x64** only, as with 0.2. The native regular-file inspection used by the asset pipeline and native-pixel storage is pinned to that profile (Windows passes the probe unqualified using public file attributes; native-pixel writes and output checks also need Linux primitives and fail closed without them); every other OS/architecture/runtime combination **fails closed** (`ASSET_PLATFORM_UNSUPPORTED`) and is **UNSUPPORTED**. Qualifying additional platforms is a tracked release gate (now change `008-macos-qualification` for Apple silicon macOS).

### Added

- Sub-node export (feature 002 T010, [D-10](specs/architecture/decisions.md#d-10-bounded-sub-node-export), formerly ADR-010): `agd export --node <ID>` and MCP `export_document` `nodeId` emit the subtree rooted at one node as a standalone SVG or raster image cropped to that subtree's own bounds and drawn **in isolation** (ancestor transforms and clipping are not applied). Additive `IDocumentEngine.ExportNodeAsync`/`RenderNode` extract the subtree into a transient document sized to its `NodeBounds.World` and reuse the existing SVG adapter / raster renderer unchanged — no export-interface, renderer, or document-format change. `--node` is mutually exclusive with `--symbol`/`--width`/`--height`; it honours `--scale`, `--background` and the raster font policy; a node with no visible bounds fails with `NODE_NO_BOUNDS`. The persisted-instance/token-override half of T010 is a document-format change and is not included.
- Bounded local SVG reference/clip import (feature 002 T011, [D-11](specs/architecture/decisions.md#d-11-bounded-svg-reference-import), formerly ADR-011): `SvgImportService` now expands local `<use href="#id">` (a referenced `<symbol>`/`<svg>` becomes a clipped frame viewport; any other element is reimported and wrapped in a group carrying the use's transform and `x`/`y`; reference cycles, nesting depth and total expansion count are bounded), captures `<symbol>`/`<clipPath>` definitions by id instead of warning, and maps an axis-aligned rectangular `clip-path="url(#id)"` to a `FrameNode { ClipContent = true }`. Non-rectangular/transformed/rounded/`objectBoundingBox` clips, and `mask`/`filter`/`pattern`/`marker`, keep their unsupported-feature warnings. Purely additive to Core: no import-API, document-format, or CLI/MCP surface change; only existing node types are produced.

### Changed

- Unified font policy across the read paths (feature 002 T008): `agd bounds`, `agd lint`, and `agd contact-sheet` — CLI and MCP (`get_bounds`, `lint_document`, `render_contact_sheet`) — now accept the same `--font-dir`/`--no-system-fonts`/`--strict-fonts` (`fontDirs`/`systemFonts`/`strictFonts`) options as `export`/`preview`, so measured, linted, and composited text resolves fonts exactly as a render would. A `FontPolicy` is threaded through `IDocumentEngine`'s bounds/align/distribute/fit/lint and into `ContactSheetOptions`; MCP confines each font directory to the working directory. SVG export wrapping stays out of scope and continues to reject the font flags.
- Packaged `.nupkg` size roughly halved by dropping native debug symbols. The SkiaSharp/HarfBuzzSharp native-asset packages ship Windows `libSkiaSharp.pdb` and `libHarfBuzzSharp.pdb` files (~335 MB uncompressed) under `runtimes/<rid>/native/` that are never loaded at runtime. A shared `Directory.Build.targets` now removes every `runtimes/**/*.pdb` from the packed tools (`agd`, `agd-mcp`); the native libraries themselves and all runtime identifiers are retained, so behaviour is unchanged. CLI `185,105,204 → 101,564,523` bytes and MCP `185,592,627 → 102,074,914` bytes (−45%). Verified by an installed-tool PNG render on .NET 10 Linux x64.

## 0.2.0-preview.2 - 2026-09-12

First prerelease of the 0.2 line, built by `scripts/release-bundle.sh` (the `FTPlus.AgentGraphicDesign.Cli` and `FTPlus.AgentGraphicDesign.Mcp` `.nupkg` files, `SHA256SUMS`, and `provenance.json`) and published as a GitHub **prerelease** from a clean `main` SHA — not latest stable and not on any public feed. `0.2.0-preview.1` was only an in-development version string and was never released. Native pixel qualification targets .NET 10 Linux x64 on the reviewed trusted local-filesystem profile; other platforms are not yet qualified. All other 0.2 additions below keep their stated support and limitations.

### Added

- Native pixel foundation (feature 003 P04-A), the first raster domain: content-addressed `<asset>.pixels/` source directories (`asset.agd-pixels.json` plus `payloads/<sha256>.bin`) holding flat RGBA8 layered stills with per-layer visibility, opacity and A8 coverage masks, evaluated by a versioned Q24 linear-light source-over reference (`agd.pixels.rgba8-linear-over.v1`) whose tables, math and thresholds have independent fixtures. Eight guarded operations over the shared Core `NativePixelService`: create, inspect, region read, batch edit (add/remove/reorder layers; visibility; opacity; attach/remove/fill/clear mask; fill/clear/replace pixels; `@alias` vs GUID resolution against the pre-op state; every intermediate state bounded; atomic metadata-last publish with `REVISION_CONFLICT` on a stale snapshot and byte-for-byte no-op detection), validate, composite preview, PNG export (`agd.pixels.png.v1`) and read-only decoded-sample export checking (`agd.pixels.png-samples.v1`; drift is a completed result, exit 6 / `PIXEL_OUTPUT_DRIFT`, never a write). Exposed as `agd pixels create|inspect|region|edit|validate|preview|export|check` and eight MCP tools (`create_pixel_asset`, `inspect_pixel_asset`, `inspect_pixel_region`, `edit_pixel_asset`, `validate_pixel_asset`, `preview_pixel_asset`, `export_pixel_asset`, `check_pixel_export`; 41 tools total). MCP tool arguments are validated from their original captured UTF-8 bytes before typed binding (a custom raw-capturing stdio transport), preserving duplicate-member detection, exact numeric spelling (revisions above 2^53, opacity millionths) and a 1 MiB request bound. Preview/export/check writes are confined and no-replace by default and reject symlinked, aliased or multiply-linked destinations. Native qualification is .NET 10 Linux x64 on a trusted local filesystem; this preview does not add animation, transforms, indexed palettes, groups, brushes, image import/admission, atlases or a GUI. See the [storage](docs/contracts/native-pixels-v1.md), [evaluation](docs/contracts/native-pixel-evaluation-v1.md) and [API](docs/contracts/native-pixel-api-v1.md) contracts.
- Typed in-process asset producers: `IAssetProducer<TRequest>`, `VectorAssetRequest`, `VectorAssetProducer` and cooperative per-run work budgets. The host keeps loading, confined I/O, output byte limits, diagnostic handling, sprite composition and publication, and freezes accepted result slices before further production. No registry, external execution or artifact admission is added. See [`docs/asset-producers.md`](docs/asset-producers.md).
- Provided-artifact metadata v1: immutable Core records plus a pure bounded parser, semantic validator, canonical writer and domain-separated SHA-256 for one fixed-canvas PNG still/sequence claim profile. Exact integer timing/decimal pivots, closed JSON, lexical paths and graph/aggregate limits have independent fixtures. These metadata APIs do not read/decode/admit files or expose CLI/MCP/Blender operations; the separate preflight and verification below leave them unchanged. A separate structural schema and fail-closed test-only evaluator distinguish schema acceptance from parser rules. See [`docs/provided-artifacts.md`](docs/provided-artifacts.md).
- Artifact input preflight, the first P03 encoded-integrity/PNG-structure subset: `agd assets preflight <DESCRIPTOR> [-f text|json]` and read-only MCP `preflight_artifact(descriptorPath)` share `ArtifactPreflight.CheckAsync`. Exact dedicated-bundle inventory, bounded single captures, actual length/SHA-256 and chunk CRC/structure checks use the same owned bytes without inflation. Results report `verification: "encoded-integrity-and-png-structure"`, `pngPolicy: "agd.png-rgba8-structure.v1"`, `pixelsDecoded: false`, `admitted: false`, `provenanceVerified: false`; invalid compressed data with matching outer CRCs/hash can pass. `metadataDigest` remains a metadata identity, not a verified artifact identity. Existing Linux x64 file inspection is reused on a trusted, quiescent filesystem, not newly qualified Windows behavior or an atomic Git/source snapshot. No jobs, selection/SQLite, source writes, Git operations, cache, receipts, payload preservation, native import, new dependencies/formats or MCP resources are added. Use a tracked bundle such as `artwork/hero/`, not ignored `artifacts/`, and preserve required/nonreproducible bytes independently. See [examples, limits and errors](docs/provided-artifacts.md#input-preflight).
- Read-only managed artifact verification: `agd assets verify <DESCRIPTOR> [-f text|json]` and MCP `verify_artifact(descriptorPath)` share `ArtifactVerification.CheckAsync`. Original MIT C# validates complete zlib/DEFLATE streams and filters 0..4 on the same owned bytes used for hash/CRC checks; sorted per-file raw RGBA8 SHA-256 observations retain hidden RGB without premultiplication or color conversion. Results identify `decoded-rgba8`, `agd.png-rgba8-managed.v1` and `managed-in-process`, with `pixelsDecoded: true`, `admitted: false`, `provenanceVerified: false`. One active verification per host immediately rejects competitors with `ARTIFACT_VERIFY_BUSY`; a 60-second whole-call cooperative deadline and 65,536 blocks per unique PNG supplement unchanged input bounds. Row/history storage limits are not process-RSS limits, hard termination or an OS sandbox. Preflight remains IDAT-opaque and can pass invalid compressed data that verification rejects. No external package/native codec, writes, sealing, preservation, admission, selection/database, Git operations or Python is added; metadata/document formats and pure APIs are unchanged. Existing Linux x64 input-probe qualification is reused, not new Windows qualification. Hashes do not confer artifact identity or rights approval, and this is not full P03. See [verification details](docs/provided-artifacts.md#decoded-sample-verification).
- Package verification filters now include artifact preflight and verification: 50 CLI cases (48 exact installed-shim cases plus two in-process pre-cancellation cases) and four MCP stdio discovery/schema/cold-start tests are configured. These filters have not yet been executed for this slice; no passing-test or managed-decoder qualification claim is made. The existing MIT-compatible permissive dependency policy remains unchanged; all new AGD code is MIT and no external package is added. See [package qualification](docs/provided-artifacts.md#package-qualification).
- Reusable asset pipeline toward 0.3: `agd assets build <MANIFEST>` and `agd assets check <MANIFEST>`, explicit version-1 manifests, standalone SVG/PNG/JPEG/WebP exports and shared SVG sprite composition, inherited export options, native `currentColor`, strict validation/fonts by default, bounded source/output diagnostics, confined paths, atomic per-file publication after full generation, and read-only byte/inventory drift detection (exit 6). Orphans are reported, never deleted. The initial Unix file-type probe is qualified on .NET 10 Linux x64 only; portability and package qualification remain release gates. See [`docs/asset-pipeline.md`](docs/asset-pipeline.md).
- Elliptical arc (`A`/`a`) support in the shared path-operation/bounds parser, including rotated ellipses, radius correction, compact flags, relative endpoints and degenerate arcs. Arcs become bounded cubic segments using existing deterministic flattening, limits and cancellation. This does not change stored path data.
- Counted real-stdio MCP exit exercise: three card subtrees with wrapped text, aligned/distributed through helpers, and zero lint findings in five tool calls. The harness verifies geometry, SVG line wrapping, and strict bundled-font PNG output. See [`docs/mcp-card-recipe.md`](docs/mcp-card-recipe.md); this is automated evidence, not human visual approval.
- Fill rule: `style.fillRule` (`nonzero` default, `evenodd`) on path and polygon nodes, validated by `STYLE012`. SVG export writes `fill-rule="evenodd"` (standard and symbol output), raster rendering uses the matching Skia fill type, SVG import maps the `fill-rule` attribute or inline style back, and the Clipper2 operations accept the same rule: `agd path bool|offset|union --fill-rule nonzero|evenodd` and MCP `path_boolean`/`path_offset`/`path_union_all` `fillRule` (default `nonzero`). Set it with `agd add|update --fill-rule` or `add_node`/`update_node` `fillRule` (`update` accepts `__clear__`).
- `currentColor` SVG export: `ExportOptions.CurrentColor`, exposed as `agd export --current-color <COLOR>`, `agd sprite --current-color <COLOR>`, and MCP `export_document` `currentColor`. Every node fill or stroke that resolves (after token lookup) to exactly that color is written as `currentColor`; gradient stops and the canvas background are untouched; raster formats and invalid color literals are rejected.
- Deterministic raster fonts (0.2): the Inter Regular and Bold static faces (rsms/inter v4.1, SIL Open Font License 1.1) are embedded in `FTPlus.AgentGraphicDesign.Skia` as the default text family. Fonts resolve in a fixed order — explicit font directories, then bundled Inter, then host fonts — with `agd export --font-dir <DIR>` (repeatable), `--no-system-fonts`, and `--strict-fonts`, and MCP `export_document`/`render_preview` `fontDirs` (confined to the working directory), `systemFonts`, and `strictFonts`. Unresolved families substitute bundled Inter and report a `FONT_SUBSTITUTED` diagnostic on the result (`RenderResult.Diagnostics`), or fail with `FONT_NOT_FOUND` under strict fonts. Font directories are bounded (16 directories, 256 files each, 8 MB per file, no symbolic links, no recursion).
- Agent authoring kit:
  - [`docs/agent-guide.md`](docs/agent-guide.md): the authoring guide for agents (what AGD is and is not, the create/validate/render/export loop, document-model cheat-sheet, reviewability conventions, the web-icon recipe, tokens, path tips, MCP session tips, troubleshooting codes).
  - MCP read-only resources served by `agd-mcp` from embedded copies of the repository files: `agd://schema` (`application/schema+json`, `schemas/agd-document.schema.json`), `agd://format` (`text/markdown`, `docs/format.md`), and `agd://agent-guide` (`text/markdown`, `docs/agent-guide.md`); the server now advertises the `resources` capability and `agd-mcp --help` lists the URIs.
  - Canvas templates shared by `agd create --template <id>` and MCP `create_document` (`template`): `web-icon-32`, `web-icon-24`, `social-1200x630`; explicit size/background override the template and unknown ids fail with `INVALID_ARGUMENTS` listing the valid ids.
  - `.claude/skills/agd-authoring/SKILL.md`, a repository skill for Claude Code agents, and a README "For agents" section.
  - `docs/surface-manifest.json` gains `mcpResources`, checked by the parity test together with the `EmbeddedResource` links.
- Bounds query: `IDocumentEngine.GetBounds`, `agd bounds <DOCUMENT> [--node ID]`, and MCP `get_bounds(idOrPath, nodeId?)` report each node's `local` box and its `world` axis-aligned box after its own and every ancestor transform (scale, then rotate, then translate, matching the renderer). Clipping frames clip descendants' world boxes; path extents reuse the Clipper2 parser and report a diagnostic for unsupported commands; text extents come from the new `ITextMeasurer` seam (Skia shaping in the CLI and MCP hosts, an estimate flagged `measured: false` when no measurer is registered).
- Text layout: optional `textAlign` (`start`|`center`|`end`), `verticalAlign` (`top`|`middle`|`bottom`|`baseline`), `maxWidth`, `lineHeight` (multiple of the font size; node, then typography token, then `1.2`), and `letterSpacing` fields on text nodes, plus `\n` line breaks. Words wrap greedily at `maxWidth` using the registered `ITextMeasurer` (no intra-word breaks); SVG export emits one `<tspan x="0" dy="…">` per line with `text-anchor` and `letter-spacing`; the Skia renderer offsets each line and adds letter spacing to glyph advances; `bounds` reports the whole block so `align`/`distribute` work on wrapped text. `agd add text`/`agd update` and MCP `add_node`/`update_node` accept `--text-align`, `--vertical-align`, `--max-width`, `--line-height`, `--letter-spacing` (`__clear__` restores the default alignment). SVG import maps `text-anchor`, `letter-spacing`, and `<tspan dy>` lines. Validation codes `TEXT004`–`TEXT007`. Documents that do not use the fields export byte-identical SVG and identical raster output (the `top` default keeps the first baseline one font size below y).
- Alignment helpers: `agd align --nodes … --mode left|centerX|right|top|centerY|bottom [--relative-to parent|selection|canvas]`, `agd distribute --nodes … --axis horizontal|vertical [--mode gap|centers] [--gap N]`, and `agd fit --frame ID [--padding N]`, with MCP `align_nodes`, `distribute_nodes`, and `fit_frame`. They change only `transform.x`/`transform.y` (plus the frame size for `fit`), keep children visually in place, accept `expectedRevn`, and fail with `MIXED_PARENTS` when nodes under different parents are aligned to anything but the canvas.
- Patch-style atomic mutations (roadmap 0.2): `agd patch <DOC> (--file|--ops|--stdin) [--expected-revn]` and the MCP `apply_patch(idOrPath, operations, expectedRevn?)` tool apply one JSON array of operations over `IDocumentEngine.ApplyPatch`, validate the result, and save once with a single `revn` increment; any failure leaves the stored document untouched and reports the failing `operationIndex`. Operations: `set` and `unset` (any node property by RFC 6901 JSON Pointer relative to the node, for example `/style/fill/color`, `/transform/rotation`, `/style/stroke/dashArray`, `/points/1/x`; intermediate objects are created, arrays take an existing index or `-` to append; `/id`, `/type` and `/children` paths are rejected, children are addressed by their own `nodeId`), `setNode`, `addNode` (missing `id`/`order` generated, new ids returned in `createdIds`), `deleteNode`, `moveNode`, `reorderNode`, `setColorToken`, `removeColorToken`, and `setCanvas` (`null` background clears). Limits: 500 operations per patch, JSON depth 64, 16 MiB. The MCP tool confines image `src` values inside operations like `add_node`. Reference: [`docs/patch-operations.md`](docs/patch-operations.md).
- Design lint (roadmap 0.2): `agd lint <DOC> [--profile web-icon] [--target-size <PX>] [--strict-grid] [--fail-on info|warning] [-f json]` and MCP `lint_document(idOrPath, profile?, targetSize?, strictGrid?)` run seven advisory rules over `IDocumentEngine.Lint` and never block export — `LINT001` text contrast below WCAG 2.x AA against the resolved background, `LINT002` strokes that render below one pixel at the target size, `LINT003` off-grid icon geometry (half-pixel info, whole-pixel warning under `--strict-grid`), `LINT004` content outside the canvas, `LINT005` color/typography tokens no node references, `LINT006` invisible content (zero opacity, hidden containers, zero-size shapes, empty text), and `LINT007` web-icon fills a uniform `currentColor` export would recolor differently. Findings are `info`/`warning`; the `web-icon` profile forces the grid rule and enables symbol readiness. `agd lint` exits `0` unless `--fail-on` names a reported severity (exit `5`).
- Contact sheet (roadmap 0.2, EXPERIMENTAL): `agd contact-sheet <FILES…> -o <PNG> [--columns N] [--cell PX] [--gap PX] [--background COLOR] [--labels]` and MCP `render_contact_sheet(paths, columns?, cell?, labels?)` compose several documents into one PNG grid, each aspect-fitted into a square cell and optionally labelled with its name; grid dimensions are bounded.

### Changed

- Adopted Git-first project authority and user-managed history in the full-editor plan: sources/settings and chosen asset references stay in versioned files with preserved payloads. Standalone SQLite selection/receipt work is deferred; current document/manifest formats and commands are unchanged. Added [Git workflow guidance](docs/git-workflow.md), narrow runtime-lock ignore rules and a file-restoration regression. Git is not a replacement for semantic undo or durable retry guarantees.
- Dependency gates now require isolated, forced locked restore audits with actual project coverage and suppression checks before accepting structured package listings; incomplete advisory retrieval cannot pass as an empty vulnerability result. No new dependency is introduced.
- `AssetPipeline` now requires explicit `(IDocumentEngine, IAssetProducer<VectorAssetRequest>)` composition. The engine-only constructor was removed and callers cut over directly; no legacy wrapper, migration or alias is provided, as requested by the maintainer. CLI and manifest behavior are unchanged.
- Contrast lint no longer treats frame fills as painted backgrounds; frames remain non-painting containers. The wrapped-text example uses an explicit background rectangle. Shared translucent-ancestor contrast is conservatively skipped, like unsupported gradient backgrounds, rather than calculated with incorrect independent opacity; clean lint is not a visual/accessibility approval.
- The approved delivery order is AGD 0.3 first, then downstream consumer adoption. Generic consumer asset plumbing moves into AGD; framework wiring stays in consumers.
- Text nodes with no `fontFamily` always render with bundled Inter in raster output instead of a host-dependent face. Host fonts are matched only under exactly the requested family name; on Linux the bundled SkiaSharp native build exposes no host fonts, so families that previously rendered silently as DejaVu now render as Inter with a diagnostic (or fail under `--strict-fonts`) unless supplied through `--font-dir`. `TextShapingService.GetFallbackFontFamilies`/`FindAvailableFont` and the hard-coded platform font-path fallback were removed.
- CI: the locked `dotnet restore` steps retry up to three times, and the pinned actions moved to Node 24 releases.

## 0.1.0 - 2026-09-02

First release. Distribution is a GitHub Release asset built by `scripts/release-bundle.sh`: the `FTPlus.AgentGraphicDesign.Cli` and `FTPlus.AgentGraphicDesign.Mcp` `.nupkg` files, `SHA256SUMS`, and `provenance.json`. There is no public package feed. Document format version `1` is the v0.1.0 contract.

### Added

- Document model: deterministic `.agd.json` documents (format version `1`) with stable IDs, a monotonic `revn` revision, fractional sibling ordering, canonical git-diffable JSON, and preservation of unknown fields and node types on round trip.
- Node types: frame, group, rectangle, ellipse, path, text, line, polygon, and image reference.
- Style: solid fill and stroke, opacity, blend modes, linear and radial gradients, stroke cap/join/miter/dash, and translate/rotate/scale transforms.
- Tokens: embedded color and typography tokens with bare and braced aliases, fill/stroke/typography token references resolved at export, bundled palettes (Tailwind, Material, ColorBrewer), and DTCG import/export of the supported subset.
- Path operations through Clipper2: union, intersect, difference, XOR, union-all, and stroke offset over absolute and relative `M`, `L`, `H`, `V`, `C`, `S`, `Q`, `T`, and `Z`.
- Export: SVG; PNG, JPEG, and WebP rendering through SkiaSharp with HarfBuzz text shaping; SVG `<symbol>` fragments (`agd export --symbol`, MCP `export_document` with `symbolId`) and `agd sprite` icon sheets.
- Import: best-effort SVG import with structured warnings for unsupported features.
- Validation: structured diagnostic codes, the restricted `web-icon` profile, and `--strict` export that rejects warnings before writing.
- Surfaces: the `agd` CLI with 22 command registrations (including the `path` and `tokens` branches) and the `agd-mcp` stdio server with 24 tools, both over the shared `IDocumentEngine`; mutations accept an optional expected revision and return `REVISION_CONFLICT` on stale writes.
- Safety: MCP path confinement to one working directory (traversal and symlink rejection), and resource bounds on input size, token counts, alias depth, image payloads (1 MB decoded data URIs), and sprite batches.
- Release engineering: pinned SDK `10.0.111`, locked restore, deterministic builds with source provenance, and `scripts/release-bundle.sh` producing checksummed packages plus `provenance.json` from a clean `git archive`.

### Known limitations

- No fill rule (`style.fillRule`); scheduled for the 0.1 close-out.
- Boolean and offset operations reject elliptical arcs (`A`).
- Raster rendering decodes images only from bounded data URIs; relative paths and remote URLs render a placeholder and are never fetched.
- SVG import is lossy for unsupported features.
- CLI and MCP share the engine but are not fully surface-equivalent; see [`docs/CLI_MCP_PARITY.md`](docs/CLI_MCP_PARITY.md).
