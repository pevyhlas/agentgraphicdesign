# Agent Graphic Design

Agent Graphic Design (AGD) is a headless graphics engine for agents and automation: deterministic vector scene-graph documents plus native layered pixel stills. The same core API backs the `agd` command-line tool and the `agd-mcp` stdio server. AGD is MIT licensed and .NET-first. **Lifecycle: experimental** — released as 0.x under SemVer; formats and APIs can still change between minor versions.

> **Experimental.** One maintainer builds AGD and uses it on a handful of projects. It may not
> fit your project, platform or agent harness, and only .NET 10 on Linux x64 is qualified.
> Interfaces and formats can still change between releases; the [CHANGELOG](CHANGELOG.md)
> says what changed and what callers must update. Issues and draft pull requests are welcome: see
> [CONTRIBUTING](CONTRIBUTING.md), [SECURITY](SECURITY.md) and the
> [code of conduct](CODE_OF_CONDUCT.md).

## Quickstart

Build from source. `global.json` pins one exact .NET SDK with roll-forward disabled, so install
that SDK first ([Requirements](#requirements) gives the command):

```sh
git clone https://github.com/pevyhlas/agentgraphicdesign.git
cd agentgraphicdesign
dotnet restore FTPlus.AgentGraphicDesign.slnx --locked-mode
dotnet build FTPlus.AgentGraphicDesign.slnx --configuration Release --no-restore
./scripts/install-local.sh          # installs agd and agd-mcp as global .NET tools
```

The installed `agd` and `agd-mcp` commands find the .NET runtime through `DOTNET_ROOT` unless
.NET 10 is installed system-wide, so keep `DOTNET_ROOT` and `PATH` (including
`$HOME/.dotnet/tools`) set in your shell profile, as [Requirements](#requirements) shows.

Create, check and export a design with the CLI:

```sh
agd create my-design
agd add my-design.agd.json rectangle --width 100 --height 60 --fill "#336699"
agd validate my-design.agd.json
agd export my-design.agd.json --type svg --output output.svg
```

Or serve the same engine to an agent over MCP (stdio):

```sh
agd-mcp --working-directory /path/to/designs
```

See [MCP Server](#mcp-server) for client configuration. The [installation
guide](docs/installation.md) for verified release bundles applies once a release is published
in this repository; until then, build from source as above.

## Direction

The confirmed product direction is a **full agent-native graphics editor**,
with vector, raster/pixel, animated-sprite and tile-authoring domains, shared project
services, and a later UI over the same contracts. These expanded capabilities are
partly delivered: native layered still-pixel editing first shipped in `0.2.0-preview.2` and is in `0.3.0`;
animation, tiles and the broader [architecture](specs/architecture.md)
remain future work. Production workflows and supported recipes use .NET/C#,
without Python; consumer implementations are requirements evidence, not code to port.

[Blender interoperability](specs/architecture/why-it-works.md#external-producers-instead-of-hosted-blender-r-11-c-07)
uses a separate external Blender producer project,
AgentBlender, not AGD's native engine. Provided-artifact
verification exists; admission/preservation and the producer handoff remain planned.
See the planned requirements in the [product spec](specs/product.md).

The implemented interoperability foundation includes a [pure Core metadata contract
and read-only artifact checks](docs/provided-artifacts.md) for supplied PNG
still/sequence claims. Metadata APIs remain pure; `agd assets preflight`
and MCP `preflight_artifact` check exact bundle inventory, captured-byte length/SHA-256
and narrow CRC/PNG structure, with IDAT still opaque. The separate `agd assets verify`
/ MCP `verify_artifact` adds strict managed DEFLATE, all row filters and raw RGBA8
SHA-256 on the same owned input bytes. Invalid compressed data with matching outer
CRCs/hash can pass preflight but fails verification. These are the bounded P7
provided-artifact checks (R-07), not sealing, payload preservation, admission, verified
provenance or Blender execution;
metadata and sample hashes are observations, not global artifact identities or rights approval.

The asset pipeline also has a [typed in-process producer boundary](docs/asset-producers.md)
with explicit vector-producer composition. This is a direct API cutover, not a
compatibility wrapper or permission for external execution.

Project files are authoritative and intended for user-managed Git history. Track
sources, settings and required artwork; exclude rebuildable caches and runtime
locks, not project intent. AGD does not require or automatically operate Git, and
no database is needed for project history. See the [Git workflow](docs/git-workflow.md)
for what to track and how to restore safely.

## Status

**Lifecycle: experimental (Public preview).** 0.3.0 (2026-10-01) was released before this
repository became public and is not available here; until a public release is published, build
and install from source (see [Local Tools](#local-tools)). 0.x versions follow SemVer, so later
minor versions may still break. The supported authoring profile is .NET 10 Linux x64 only, and
the delivered scope is a subset of the full-editor roadmap. Document format version `1` remains
the vector contract. See the [product spec](specs/product.md) and
[`CHANGELOG.md`](CHANGELOG.md).

The source in this repository is ahead of 0.3.0: it adds preserved still import
(`agd pixels import` / `import-layer`, MCP `import_pixel_asset` / `import_pixel_layer`) and
authoring reliability fixes, listed under *Unreleased* in the changelog. Source builds still
report version `0.3.0`, followed by `+<commit>`; quote that full string from `agd --version`
in bug reports.

The 0.3.0 release includes a [manifest-driven asset pipeline](docs/asset-pipeline.md), arc geometry, sub-node export, bounded local SVG `<use>`/`<symbol>`/rectangular-clip import, and a unified font policy across the read paths. Downstream projects perform their own pin/install verification using the [installation guide](docs/installation.md); no consumer adoption is claimed here.

Current capabilities include:

- Deterministic `.agd.json` documents with stable IDs and fractional sibling ordering
- Rectangle, ellipse, path, text, line, polygon, image-reference, group, and frame nodes
- Text layout: `textAlign`, `verticalAlign`, wrapping at `maxWidth`, `lineHeight`, and `letterSpacing` on text nodes, shared by bounds, SVG, and raster output
- SVG export (including symbol fragments and `currentColor` substitution) and best-effort SVG import
- PNG, JPEG, and WebP rendering through SkiaSharp with a bundled default text family (Inter) so raster text is reproducible across hosts
- `nonzero`/`evenodd` fill rules on path and polygon nodes, honored by SVG export, raster rendering, SVG import, and path operations
- Design lint (`agd lint`): seven advisory rules (WCAG text contrast, hairline strokes, off-grid icon geometry, content outside the canvas, unused tokens, invisible content, symbol readiness) that never block export, plus `agd contact-sheet` to compose documents into one PNG grid
- Path boolean and offset operations through Clipper2
- Embedded color/typography tokens, bundled palettes, and DTCG import/export
- CLI and MCP adapters over the same `IDocumentEngine`, with shared canvas templates for creation
- Read-only MCP resources that serve the schema, format reference, and agent authoring guide
- `get_capabilities`, readable MCP creation paths, typed patch/layout arrays, bounded subtree/paged inspection, and small default previews for agent authoring
- `agd assets build` / `agd assets check`: portable export/sprite batches and read-only byte/inventory drift checks from one manifest
- `agd assets preflight <DESCRIPTOR> [-f text|json]` / MCP `preflight_artifact(descriptorPath)`: shared read-only encoded-integrity and PNG-structure checks, not decoding or admission
- `agd assets verify <DESCRIPTOR> [-f text|json]` / MCP `verify_artifact(descriptorPath)`: shared read-only exact RGBA8 sample verification, managed in-process without an OS sandbox, admission or preservation
- `agd pixels` and eight native MCP operations: layered RGBA8 still assets, visibility/opacity/masks, region edits, inspection/validation, preview, PNG export and read-only output checks; shipped in `0.2.0-preview.2` on the qualified Linux x64 profile
- Development source only (unreleased): `agd pixels import` / `agd pixels import-layer` and MCP `import_pixel_asset` / `import_pixel_layer` import one provided PNG still into a new native asset or an independent top layer, preserving the original input; nothing is admitted. See [still import](docs/native-still-import.md)

Important limitations:

- Distribution is a GitHub Release asset: the checksummed bundle from `scripts/release-bundle.sh` (both `.nupkg` files, `SHA256SUMS`, `provenance.json`). There is no public package feed, and the scripts never publish packages, create a tag, or create a release themselves.
- `ImageNode.src` is emitted as an SVG `<image href>` reference. Raster rendering decodes bounded base64 data URIs for PNG/JPEG/GIF/WebP (maximum 1 MB decoded); relative paths, remote URLs, invalid data, and oversized payloads render an `[image]` placeholder and are never fetched.
- Path nodes preserve SVG `d` strings. Geometry operations support absolute/relative `M`, `L`, `H`, `V`, `C`, `S`, `Q`, `T`, `A`, and `Z`. Curves and arcs are flattened deterministically; bounds are approximate, not analytic extrema.
- `fill.tokenRef`, `stroke.tokenRef`, and typography references are resolved by SVG/raster exporters. Missing or cyclic references produce diagnostics/fallback behavior rather than silently fetching values.
- CLI and MCP share engine behavior but are not fully surface-equivalent. See [the tested surface matrix](docs/CLI_MCP_PARITY.md).
- Raster text resolves fonts through the policy in [Fonts](#fonts). The Linux native SkiaSharp build used here has no fontconfig, so host fonts on Linux are only reachable through explicit font directories.
- The asset pipeline's Unix file-safety probe, reused by artifact preflight and verification, is currently qualified only on .NET 10 Linux x64; other Unix configurations fail closed. The existing Windows implementation is not newly qualified by either artifact operation; Windows qualification and portable file inspection remain release gates. See [pipeline limits](docs/asset-pipeline.md#limits-and-portability).

## Requirements

The repository pins .NET SDK `10.0.112` in `global.json` with roll-forward disabled, so every
`dotnet` command in this tree uses exactly that SDK. Install it beside any SDKs you already have
with Microsoft's install script, which reads the version from `global.json` (run from the
repository root):

```sh
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --jsonfile global.json --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
```

Add that `export` line to your shell profile (for example `~/.zshrc` or `~/.bashrc`) so it
holds in every new terminal. The `export` alone lasts only for the current shell, and the
global tools that `install-local.sh` installs (`agd`, `agd-mcp`) need `DOTNET_ROOT` to find the
runtime when .NET 10 is not installed system-wide; without it they fail to start in a new
terminal or when an MCP client launches them.

On Windows, download `https://dot.net/v1/dotnet-install.ps1` and run
`./dotnet-install.ps1 -JsonFile global.json`. If `dotnet` stops with *A compatible .NET SDK was
not found. Requested SDK version: 10.0.112*, the `dotnet` first on your `PATH` does not have the
pinned SDK: install it as above and put its directory first on `PATH`. The exact pin is
deliberate, for reproducible locked restores and release bundles; SDK updates, security patches
included, land through a pull request that reruns CI, the locked restore and
`scripts/release-bundle.sh --check`.

```sh
dotnet restore FTPlus.AgentGraphicDesign.slnx --locked-mode
dotnet build FTPlus.AgentGraphicDesign.slnx --configuration Release --no-restore
dotnet test FTPlus.AgentGraphicDesign.slnx --configuration Release --no-build
```

Run all authoritative local gates, including dependency/audit checks, readiness generation, package smoke tests, coverage, and formatting:

```sh
bash scripts/validate-local.sh
```

## Local Tools

Install both tools globally from local packages:

```sh
./scripts/install-local.sh
agd --help
agd-mcp --help
```

For an isolated pack/install/legal-content smoke test that does not modify global tools:

```sh
bash scripts/package-smoke.sh
```

Uninstall global tools with `./scripts/uninstall-local.sh`.

`scripts/verify-packages.sh` exercises exact installed artifact/native CLI cases,
MCP discovery/schema/verification/raw-wire cases and complete readiness vector/native
journeys. It also checks approved-bundle bootstrap and mixed SVG/PNG/sprite manifest
build/check. Revision-scoped passing evidence is recorded with qualification;
filter configuration alone is not a passing claim.

For installation from an approved released bundle with trusted package/source pins,
use [`scripts/restore-approved-tools.sh`](scripts/restore-approved-tools.sh) from the
approved AGD source checkout. It installs both tools into a fresh local directory,
checks hashes/versions and clears implicit feeds/caches. See
[installing a released AGD](docs/installation.md).

## Release Bundle

From a clean Git worktree, create the CLI/MCP release packages, checksums, and deterministic provenance manifest:

```sh
bash scripts/release-bundle.sh
```

The script builds from an isolated `git archive` of the exact source commit, uses locked restore and a non-incremental Release build, then tests and packs without rebuilding. It installs and checks the exact staged packages from a cleared local-only feed and reproduces the golden SVG before writing `artifacts/release-bundle/` with both `.nupkg` files, `SHA256SUMS`, and `provenance.json`. It never publishes, tags, or creates a hosted release; a maintainer attaches this bundle to the GitHub Release for the tag. `bash scripts/release-bundle.sh --check` validates the SDK, package-version, and source-revision inputs without creating a bundle.

## CLI

```sh
agd create my-design
agd create ic-power --template web-icon-32 -o ic-power.agd.json
agd add my-design.agd.json rectangle --width 100 --height 60 --fill "#336699"
agd inspect my-design.agd.json --tree
agd validate my-design.agd.json
agd validate icon.agd.json --profile web-icon
agd export my-design.agd.json --type svg --output output.svg
agd export icon.agd.json --profile web-icon --output icon.svg
agd sprite icon-a.agd.json icon-b.agd.json --output sprite.svg --strict --profile web-icon
agd export my-design.agd.json --type png --output output.png
agd export icon.agd.json --type svg --current-color "#000000" --output icon.svg
agd sprite icon-a.agd.json icon-b.agd.json --current-color "#000000" --output sprite.svg
agd export my-design.agd.json --type png --font-dir ./fonts --no-system-fonts --strict-fonts --output output.png
agd import input.svg --output imported.agd.json
agd update my-design.agd.json <NODE-ID> --fill-rule evenodd
agd patch my-design.agd.json --file ops.json --expected-revn 3
agd path bool union "M0,0L100,0L100,100Z" "M50,50L150,50L150,150Z"
agd path bool intersect "M0,0L100,0L100,100L0,100Z M25,25L75,25L75,75L25,75Z" "M0,0L100,0L100,100L0,100Z" --fill-rule evenodd
agd tokens list-palettes
agd add card.agd.json text --text "A wrapped paragraph" --max-width 320 --line-height 1.4 --text-align center -x 200 -y 24
agd bounds my-design.agd.json --node <node-id> -f json
agd align my-design.agd.json --nodes <id-a>,<id-b> --mode centerX
agd distribute my-design.agd.json --nodes <id-a>,<id-b>,<id-c> --axis horizontal
agd fit my-design.agd.json --frame <frame-id> --padding 8
agd assets build examples/assets.json
agd assets check examples/assets.json -f json
agd assets preflight artwork/hero/descriptor.json -f json
agd assets verify artwork/hero/descriptor.json -f json
```

`assets preflight` and `assets verify` expect an existing dedicated bundle containing
only its descriptor, declared PNGs (for example `frames/idle-000.png`) and necessary
directories. Use a tracked project location such as `artwork/hero/`, not this
repository's ignored `artifacts/` directory; preserve required/nonreproducible
payloads, not just hashes or recipes. AGD does not edit `.gitignore` or operate Git.
Pause producers and Git writers: both assume a trusted, quiescent filesystem,
not an atomic snapshot, and report this call's captured bytes, not durable validity.
Preflight's unchanged result includes `verification: "encoded-integrity-and-png-structure"`,
`pngPolicy: "agd.png-rgba8-structure.v1"`, `pixelsDecoded: false`, `admitted: false`
and `provenanceVerified: false`.

Verification returns the same eight flat metadata facts plus unique `files` records
(`fileSha256`, `rgbaSha256`) sorted ordinally by encoded hash, with
`verification: "decoded-rgba8"`, `decoderProfile: "agd.png-rgba8-managed.v1"`,
`execution: "managed-in-process"`, `pixelsDecoded: true`, `admitted: false`, and
`provenanceVerified: false`. It checks zlib headers, advertised window/history,
checksum, exact output, final termination and absence of trailing bytes/members.
The hash covers raw RGBA8 samples without premultiplication, hidden-RGB clearing or
color conversion; it is not preservation or rights/quality approval.

One verification is active per host; competing calls fail immediately with
`ARTIFACT_VERIFY_BUSY`. The whole Core call has a 60-second cooperative deadline and
a 65,536-DEFLATE-block limit per unique PNG. Existing 1 MiB metadata, 64 MiB file,
256 MiB aggregate and canvas/inventory bounds remain; row/history working storage
is bounded, not process RSS. There is no hard termination or OS sandbox guarantee.
Neither operation creates jobs, selection/SQLite state, cache, receipts, sealed or
preserved payloads, native imports or source writes. No external package, native
codec, document/metadata format change or MCP resource is added; metadata APIs remain
pure. See the [bundle example, limits and errors](docs/provided-artifacts.md#input-preflight)
and [verification reference](docs/provided-artifacts.md#decoded-sample-verification).
`assets build` and `assets check` remain CLI-only manifest batches; neither artifact
operation is their drift check.

`bounds` reports each node's `local` box (own geometry) and `world` box (after its own and every ancestor transform, clipped by clipping frames). Text extents come from the Skia shaping engine and cover the whole wrapped block with its alignment offset; a document opened without a text measurer reports `measured: false` and an estimate. `align`, `distribute`, and `fit` change only `transform.x`/`transform.y` (and the frame size for `fit`) and accept `--expected-revn`.

Run `agd --help`, `agd assets --help`, `agd assets verify --help`, `agd path --help`, or `agd tokens --help` for the executable command contract. The tested command inventory is also recorded in [`docs/surface-manifest.json`](docs/surface-manifest.json).

`agd create --template <id>` seeds the canvas from a shared catalog: `web-icon-32` (32x32, no background), `web-icon-24` (24x24, no background), and `social-1200x630` (1200x630, white background). Explicit `--width`, `--height`, and `--background` override the template; an unknown id fails with `INVALID_ARGUMENTS` and lists the valid ids. `agd create --help` documents the catalog, and MCP `create_document` accepts the same ids through its optional `template` argument.
`agd patch` (and the MCP `apply_patch` tool) applies a JSON array of up to 500 operations atomically: any node property by JSON Pointer (`set`/`unset`), whole-node replacement, node add/delete/move/reorder, color tokens and canvas changes are validated together and saved with one revision increment, or rejected with the failing operation index. See [`docs/patch-operations.md`](docs/patch-operations.md).

The optional `web-icon` validation profile accepts only self-contained geometric nodes and a null or fully transparent hex/RGBA canvas background. It rejects text, images, unknown nodes, and opaque backgrounds. Profile exports always validate before writing; `--strict` additionally blocks ordinary validation warnings. MCP `ValidateDocument` and `ExportDocument` expose the same optional `profile` value.

`agd export --current-color <COLOR>` and `agd sprite --current-color <COLOR>` write `currentColor` in place of every node fill or stroke that resolves (after token lookup) to exactly that color, so a monochrome icon inherits the CSS `color` of its host element. The option is svg-only; raster formats reject it, gradient stops and the canvas background are never rewritten, and the color must be a valid hex, `rgb()`, or `rgba()` literal.

`agd add`/`agd update --fill-rule nonzero|evenodd` set `style.fillRule` on path and polygon nodes (`--fill-rule __clear__` resets it on update). `agd path bool|offset|union --fill-rule nonzero|evenodd` (default `nonzero`) chooses how overlapping contours are resolved by the geometry operations.

Raster exports (`png`, `jpeg`, `webp`) accept three font options: `--font-dir <DIR>` (repeatable) names directories whose `.ttf`/`.otf` files are searched first, `--no-system-fonts` disables host-installed fonts, and `--strict-fonts` fails the export with `FONT_NOT_FOUND` instead of substituting the bundled family. Font substitutions are reported as `FONT_SUBSTITUTED` entries in the `diagnostics` array of the JSON result (and as `!` lines in text mode). These options are rejected for `svg` export, which references families by name and resolves nothing.

## Fonts

Text in raster output resolves each `fontFamily`/`fontWeight` pair in a fixed order:

1. Explicit font directories (`--font-dir` / `fontDirs`), matched by family name (case-insensitive) and nearest weight. A directory is read once, non-recursively, without following symbolic links; at most 16 directories, 256 font files per directory, and 8 MB per file.
2. The bundled **Inter** family (Regular for weights below 600, Bold at 600 and above), embedded in `FTPlus.AgentGraphicDesign.Skia`.
3. Host-installed fonts through the platform font manager, only while system fonts are allowed (the default) and only under exactly the requested family name; generic aliases such as `sans-serif` are never taken from the host.

A text node with no `fontFamily` always renders with bundled Inter. A named family that none of the tiers provides renders with bundled Inter and emits a `FONT_SUBSTITUTED` diagnostic naming the requested family and the substitute; with strict fonts the export fails with `FONT_NOT_FOUND` instead. With system fonts disabled, PNG output for the same document and options is byte-identical on every host.

Inter is distributed under the SIL Open Font License 1.1; the release tag, file hashes, and licence text are recorded in [`.config/dependencies.json`](.config/dependencies.json) and [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).

## MCP Server

The MCP server uses stdio transport and confines file paths to its working directory.

```sh
agd-mcp --working-directory /path/to/designs
```

Example client configuration:

```json
{
  "mcpServers": {
    "agd": {
      "command": "agd-mcp",
      "args": ["--working-directory", "/path/to/designs"],
      "env": { "DOTNET_ROOT": "/path/to/.dotnet" }
    }
  }
}
```

MCP clients start the server without your shell profile. When .NET 10 is not installed
system-wide, set `env.DOTNET_ROOT` to the directory that holds the runtime (for example the
`$HOME/.dotnet` from [Requirements](#requirements), written as an absolute path); if `agd-mcp`
is not on the client's `PATH`, give `command` the absolute path under `$HOME/.dotnet/tools`.
With a system-wide .NET 10 install, omit `env`.

The current protocol names, backing methods, and human-readable titles are:

| Tool name | Method | Title |
|-----------|--------|-------|
| `get_capabilities` | `GetCapabilities` | Get Capabilities |
| `list_documents` | `ListDocuments` | List Documents |
| `create_document` | `CreateDocument` | Create Document |
| `load_document` | `LoadDocument` | Load Document |
| `validate_document` | `ValidateDocument` | Validate Document |
| `list_nodes` | `ListNodes` | List Nodes |
| `add_node` | `AddNode` | Add Node |
| `update_node` | `UpdateNode` | Update Node |
| `delete_node` | `DeleteNode` | Delete Node |
| `move_node` | `MoveNode` | Move Node |
| `reorder_node` | `ReorderNode` | Reorder Node |
| `apply_patch` | `ApplyPatch` | Apply Patch |
| `export_document` | `ExportDocument` | Export Document |
| `get_document_json` | `GetDocumentJson` | Get Document JSON |
| `render_preview` | `RenderPreview` | Render Preview |
| `import_svg` | `ImportSvg` | Import SVG |
| `path_boolean` | `PathBoolean` | Path Boolean Operation |
| `path_offset` | `PathOffset` | Path Stroke Offset |
| `path_union_all` | `PathUnionAll` | Path Union All |
| `set_color_token` | `SetColorToken` | Set Color Token |
| `remove_color_token` | `RemoveColorToken` | Remove Color Token |
| `list_color_tokens` | `ListColorTokens` | List Color Tokens |
| `apply_palette` | `ApplyPalette` | Apply Palette |
| `list_palettes` | `ListPalettes` | List Palettes |
| `import_dtcg_tokens` | `ImportDtcgTokens` | Import DTCG Tokens |
| `export_dtcg_tokens` | `ExportDtcgTokens` | Export DTCG Tokens |
| `get_bounds` | `GetBounds` | Get Bounds |
| `align_nodes` | `AlignNodes` | Align Nodes |
| `distribute_nodes` | `DistributeNodes` | Distribute Nodes |
| `fit_frame` | `FitFrame` | Fit Frame |
| `preflight_artifact` | `PreflightArtifact` | Preflight Artifact |
| `verify_artifact` | `VerifyArtifact` | Verify Artifact |
| `lint_document` | `LintDocument` | Lint Document |
| `render_contact_sheet` | `RenderContactSheet` | Render Contact Sheet |
| `import_pixel_asset` | `ImportPixelAsset` | Import Pixel Asset |
| `import_pixel_layer` | `ImportPixelLayer` | Import Pixel Layer |
| `create_pixel_asset` | `CreatePixelAsset` | Create Pixel Asset |
| `inspect_pixel_asset` | `InspectPixelAsset` | Inspect Pixel Asset |
| `inspect_pixel_region` | `InspectPixelRegion` | Inspect Pixel Region |
| `edit_pixel_asset` | `EditPixelAsset` | Edit Pixel Asset |
| `validate_pixel_asset` | `ValidatePixelAsset` | Validate Pixel Asset |
| `preview_pixel_asset` | `PreviewPixelAsset` | Preview Pixel Asset |
| `export_pixel_asset` | `ExportPixelAsset` | Export Pixel Asset |
| `check_pixel_export` | `CheckPixelExport` | Check Pixel Export |

`import_pixel_asset` and `import_pixel_layer` are in the development source only; the 0.3.0
release does not have them. Clients should still discover tools during MCP initialization. `agd-mcp --help` generates the protocol-name list from registered tool and resource attributes.

The 0.3 MCP wire cutover takes actual JSON arrays for `apply_patch.operations` and
align/distribute `nodeIds`; earlier string-encoded inputs are rejected. `create_document`
accepts confined `outputPath` and returns locator/rootId/revn. `list_nodes` supports
subtree `nodeId`, offset/limit (100 default, 1000 maximum) and relative maxDepth;
`get_document_json` can return one subtree. `render_preview` defaults to a <=512px
thumbnail without upscaling; explicit sizing keeps logical viewport semantics and
the effective 2048px limit. Vector/native/contact-sheet images contain valid base64 PNGs.

`preflight_artifact` and `verify_artifact` are read-only and accept `descriptorPath`, for example
`{"descriptorPath":"artwork/hero/descriptor.json"}`. MCP rejects lexical root
escapes before path probes, then completes confinement before the shared Core
inventory/content reads; confinement itself can inspect attributes. No document
store is initialized by either tool. Results use the shared envelope with the
respective preflight or managed-verification qualifiers above.

The server also advertises the `resources` capability and serves the following read-only reference resources, embedded into the tool assembly from the repository files so an installed `agd-mcp` answers `resources/read` without a checkout. Neither artifact operation adds a resource:

| URI | MIME type | Source file |
|-----|-----------|-------------|
| `agd://schema` | `application/schema+json` | [`schemas/agd-document.schema.json`](schemas/agd-document.schema.json) |
| `agd://format` | `text/markdown` | [`docs/format.md`](docs/format.md) |
| `agd://agent-guide` | `text/markdown` | [`docs/agent-guide.md`](docs/agent-guide.md) |

## For Agents

- [`docs/agent-guide.md`](docs/agent-guide.md) is the authoring guide: what AGD is and is not, the create/validate/render/export loop, the document-model cheat-sheet, conventions that keep hand-authored documents reviewable, the web-icon recipe, tokens, path tips, MCP session tips, and a troubleshooting table.
- [`.claude/skills/agd-authoring/SKILL.md`](.claude/skills/agd-authoring/SKILL.md) is the repository skill for Claude Code agents; it states the conventions in one page and points at the guide and the format reference.
- Over MCP, read `agd://agent-guide`, `agd://format`, and `agd://schema` (table above) instead of opening the repository.

`export_document` accepts the optional `symbolId` and `currentColor` parameters (svg only) with the same semantics as the CLI `--symbol` and `--current-color` options. `add_node` and `update_node` accept an optional `fillRule` (`nonzero` or `evenodd`; `update_node` also accepts `__clear__`), and `path_boolean`, `path_offset`, and `path_union_all` accept an optional `fillRule` that defaults to `nonzero`.

`export_document` (raster formats) and `render_preview` accept `fontDirs` (comma-separated directories, each confined to the working directory), `systemFonts` (default `true`), and `strictFonts` (default `false`) with the semantics described in [Fonts](#fonts); their results carry a `diagnostics` array.

## Concurrency

`revn` increases during engine edits, but Git can restore older values and different branches can reuse a revision number. It is not a globally unique content identity. The core save methods perform an optimistic check only when the caller supplies `expectedRevn`; a mismatched value returns `REVISION_CONFLICT`. There is no HTTP `409` surface or lease implementation in the current local-only release.

Current document mutations on both surfaces expose an expected revision: add, update, delete, move, reorder, patch, align, distribute, fit, set/remove color token, apply palette, and DTCG token import. Creation uses create-if-absent semantics instead of a stale revision check. File writes and store operations use locks plus atomic replacement, but callers that omit the optional expected revision are choosing last-writer-wins behavior.

Pause writers before Git checkout/restore/merge, then reload and validate. Git does
not participate in AGD's locks, and expected revisions do not protect concurrent
Git replacement. A lost mutation response requires inspection, not blind replay;
Git history does not supply durable request receipts or native semantic undo/redo.

## Document Format

The schema is [`schemas/agd-document.schema.json`](schemas/agd-document.schema.json), and the format-v1 compatibility contract is in [`docs/format.md`](docs/format.md). The schema URL is the format-v1 schema identifier: a stable `$id` string, not a promise that the URL resolves. This public repository was started from fresh history and has no `v0.1.0` tag, so the URL does not currently resolve; validate against the repository copy instead. Contract tests reflect model JSON fields/discriminators and validate serialized all-fields fixtures plus all supported examples against the repository's schema subset.

## Dependency Policy

AGD's own code, including the original managed PNG verifier, remains MIT. The current
policy already allows reviewed MIT-compatible permissive dependencies with their
own licenses/notices; no new exception is needed. Verification adds no external
package, native codec/inflater, copyleft host tool or OS sandbox requirement.

Runtime and test package IDs, exact resolved versions, reviewed licenses, and allowed scopes are recorded in [`.config/dependencies.json`](.config/dependencies.json). `bash scripts/check-dependencies.sh` consumes structured `dotnet package list --format json` output, verifies central pins and approvals, checks available package license metadata, and detects required notice-marker drift without `jq` or other OS parsing tools. License and notice completeness remains part of dependency review. `bash scripts/check-vulnerabilities.sh` parses the structured NuGet vulnerability result.

## Contributing

Open an issue before a pull request for anything beyond a small fix, and open pull requests as
drafts; only the maintainer merges. [CONTRIBUTING.md](CONTRIBUTING.md) has the validation
commands and the dependency policy. Report vulnerabilities privately as [SECURITY.md](SECURITY.md)
describes. Everyone taking part follows the [code of conduct](CODE_OF_CONDUCT.md).

## License

Agent Graphic Design is MIT licensed. Tool packages include [`LICENSE`](LICENSE), [`README.md`](README.md), [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt), and [`THIRD-PARTY-NOTICES-SKIASHARP.txt`](THIRD-PARTY-NOTICES-SKIASHARP.txt), the unmodified notice file of the SkiaSharp and HarfBuzzSharp native-assets packages. The bundled Inter font family is redistributed under the SIL Open Font License 1.1, reproduced in the notices file.
