# AGD Agent Authoring Guide

The latest release is **0.3.0** (2026-10-01, released before this repository became public),
qualified for .NET 10 Linux x64: vector authoring, native layered RGBA8 still-pixel editing,
and read-only supplied-PNG verification. The development source adds preserved PNG-still
import and authoring reliability fixes that the 0.3.0 packages do not contain; source builds
still report `0.3.0+<commit>`. See *Unreleased* in the [CHANGELOG](../CHANGELOG.md) and
[still import](native-still-import.md).
Start with `agd --version` and command help, or MCP initialization,
`tools/list` and `get_capabilities`, so recipes match the executable you are using.
[`format.md`](format.md) is the vector format reference.

The MCP server serves this guide as `agd://agent-guide`, the format reference as `agd://format`, and the JSON Schema as `agd://schema`, so an MCP client can read them without a checkout.

## Current Tools and Target

AGD is a headless vector scene-graph engine. A design is a `.agd.json` file: a canvas, a root frame, a tree of typed nodes (rectangle, ellipse, path, text, line, polygon, image, group, frame), optional design tokens. You author or edit that JSON, then validate and export it to SVG or to PNG/JPEG/WebP through SkiaSharp. The CLI `agd` and the MCP server `agd-mcp` share `IDocumentEngine` for document operations; provided-artifact preflight and verification use separate shared Core services without a document store.

AGD targets a full agent-native graphics editor, with proposed vector, raster/pixel,
sprite-animation and tile domains. CLI/MCP are primary; a later UI shares their
contracts. Production workflows and supported recipes use .NET/C#, not Python.
See the [architecture](../specs/architecture.md) and planned requirements in the
[product spec](../specs/product.md).
That target is not an executable capability claim.

Optional [Blender interoperability](../specs/architecture/why-it-works.md#external-producers-instead-of-hosted-blender-r-11-c-07)
is also a design target, not an installed tool. Provided artifacts and native editing
remain separate from gated external production; do not invent Blender commands or
assume permission to launch an executable or generate a Python bridge.

The current tools have these limits:

- **No autonomous generation.** Nothing draws for you. You decide every shape and coordinate; the engine validates, composes, and renders deterministically.
- **Native layered still-pixel authoring is implemented.** The separate `agd pixels`
  commands and ten native MCP tools in development source edit flat RGBA8 layers and coverage masks with
  guarded snapshots. See the complete recipe below. Brushes, transforms, animation
  remain separate follow-ups. Development source adds [preserved still import](native-still-import.md); vector `image` nodes retain
  their bounded embedded-image/path-placeholder rendering contract.
- **Sprite animation is not implemented yet.** Current documents are still scenes; spatial frames are not animation frames. Timelines/cels/clips and separate tile-connectivity authoring are first-class planned domains.

## The loop

Author in small, checkable steps: create, add or update, validate, look at a render, export.

```sh
agd create ic-power --template web-icon-32 -o ic-power.agd.json
agd add ic-power.agd.json path -n bolt --fill "#000000" \
    --path-data "M18 2 L8 16 L14 16 L10 30 L24 14 L17 14 L22 2 Z"
agd validate ic-power.agd.json --profile web-icon --strict
agd export ic-power.agd.json -t png -s 4 -o ic-power@4x.png    # look at it
agd inspect ic-power.agd.json --tree
agd export ic-power.agd.json --profile web-icon --strict -o ic-power.svg
```

The same loop over MCP is `create_document` (optional `template`), `add_node` / `update_node`, `validate_document`, `render_preview` (inline PNG), `list_nodes` or `get_document_json`, then `export_document`.

For layouts, prefer one `apply_patch` containing complete subtrees, then shared
alignment/distribution helpers rather than one tool call per shape. The tested
[three-column card recipe](mcp-card-recipe.md) uses exactly five tool calls:
create, patch, align, distribute, lint. Preview/export are subsequent review and
delivery steps; the counted exercise assumes known schemas and no retries.

Useful habits:

- `-f json` on any CLI command returns a machine-readable envelope (`{ "ok": true, "data": ... }` or `{ "ok": false, "error": { "code", "message" } }`); the MCP tools always return that envelope.
- `agd validate` first, `--strict` before export. Profile exports (`--profile web-icon`) validate before writing and refuse on errors; `--strict` also refuses on warnings.
- Render early. A 4x PNG (`-s 4`) of a 32 px icon is 128 px, large enough to judge the silhouette and small enough to keep in context.
- Lint before you ship. `agd lint <doc>` (MCP `lint_document`) reports advisory findings — low text contrast (`LINT001`), hairline strokes (`LINT002`), off-grid icon geometry (`LINT003`), content outside the canvas (`LINT004`), unused tokens (`LINT005`), invisible content (`LINT006`), and symbol-readiness for `currentColor` icons (`LINT007`). It never blocks export; add `--profile web-icon` for icons, and `--fail-on warning` to make a script stop. `agd contact-sheet <files…> -o sheet.png` tiles several documents into one PNG for a quick side-by-side.
- Export one node on its own with `agd export <doc> --node <ID>` (MCP `export_document` with `nodeId`). It emits the subtree rooted at that node as a standalone SVG or raster image cropped to the node's own bounds and drawn in isolation — ancestor transforms and clipping are not applied — so an icon or component authored inside a larger scene comes out as its own file without editing the tree. It honours `--scale`, `--background` and the raster font policy, but not `--symbol`/`--width`/`--height` (the size is intrinsic); a node with no visible bounds fails with `NODE_NO_BOUNDS`.
- Hand-editing the JSON is fine and often faster for geometry. Run `agd validate` after every hand edit; the loader rejects malformed trees before any command runs.
- For committed asset sets, declare one [asset manifest](asset-pipeline.md), then use `agd assets build` and `agd assets check` instead of writing per-project export, recolouring, sprite, and drift-check loops. These commands are included in published 0.3.0 and remain CLI-only batch workflows; the separate `assets preflight` and `assets verify` commands have MCP counterparts. It supports .NET 10 Linux x64 only; see the [CHANGELOG](../CHANGELOG.md).
- Frames do not paint backgrounds: add a rectangle behind the frame's content. Contrast checks skip unsupported backgrounds, including gradients and shared translucent compositing groups; no findings is not proof of visual quality or accessibility.

## Provided-artifact preflight

Published 0.3.0 includes the read-only provided-artifact preflight and managed
verification checks (R-07 in `specs/product/requirements.md`). Full admission remains future scope;
development source adds a separately qualified [managed-local native still import](native-still-import.md):

```sh
agd assets preflight artwork/hero/descriptor.json -f json
agd assets verify artwork/hero/descriptor.json -f json
agd assets verify --help
```

MCP: `preflight_artifact` or `verify_artifact`, each with
`{"descriptorPath":"artwork/hero/descriptor.json"}` and marked read-only.
The descriptor is not a vector document or an asset-build manifest. Use a dedicated
tracked bundle such as `artwork/hero/descriptor.json` plus declared
`artwork/hero/frames/*.png`, not this repository's ignored `artifacts/` directory.
Only that descriptor, declared PNGs and necessary directories are allowed; extra
files, hidden sidecars or unused directories fail. The [bundle example and literal
payload fixture](provided-artifacts.md#bundle-layout-and-example) show the layout
and a real small PNG claim without inventing a production descriptor.

Preflight checks exact inventory, bounded reads, claimed length and actual SHA-256,
then CRCs and narrow PNG structure on the same captured bytes. Success reports
`verification: "encoded-integrity-and-png-structure"` and
`pngPolicy: "agd.png-rgba8-structure.v1"`, always with `pixelsDecoded: false`,
`admitted: false`, `provenanceVerified: false`. IDAT is opaque: invalid compressed
data with valid outer CRCs/hash can pass. Unsupported legal PNG variants are
rejected, not converted. This is not full PNG decodability validation or admission;
the unchanged `metadataDigest` is not a verified artifact identity.

Choose `assets verify <DESCRIPTOR> [-f text|json]` / `verify_artifact(descriptorPath)`
when exact decoded samples are needed. The separate `ArtifactVerification.CheckAsync`
Core service checks hash, CRC/structure and decoding on the same private captured
bytes, once per unique file. Original managed C# validates the complete zlib/DEFLATE
stream (headers, advertised window/history, checksum, final termination, no trailing
bytes or second member) and all row filters 0..4. SHA-256 covers raw RGBA8 rows,
without premultiplication, hidden-RGB clearing or color conversion. For example,
matching-CRC/hash IDAT containing reserved DEFLATE block type bytes
`78 01 07 00 00 00 00` passes preflight but fails verification with
`ARTIFACT_PNG_INVALID`; no partial success is returned.

Verification reports the eight flat metadata facts from preflight plus unique
`files` records (`fileSha256`, `rgbaSha256`), sorted ordinally by encoded hash.
It always includes `verification: "decoded-rgba8"`,
`decoderProfile: "agd.png-rgba8-managed.v1"`, `execution: "managed-in-process"`,
`pixelsDecoded: true`, `admitted: false`, `provenanceVerified: false`. These are raw
sample observations under explicit dimensions/profile, not artifact-set identity,
payload preservation, rights/quality approval or chosen-result authority.

Verification permits one active call per host and immediately rejects competitors
with `ARTIFACT_VERIFY_BUSY`, without queuing. The whole Core call has a 60-second
**cooperative** deadline and a 65,536-DEFLATE-block limit per unique PNG; exhaustion
uses `RESOURCE_LIMIT`. Existing 1 MiB metadata, 64 MiB encoded-file, 256 MiB aggregate
and canvas/inventory bounds remain. A 32 KiB history, two at-most-32-KiB sample rows
and bounded Huffman storage avoid full-image decode buffers. These are work/storage
bounds, not process-RSS limits, hard termination or an OS sandbox. See the
[verification reference](provided-artifacts.md#decoded-sample-verification).

Pause producers and Git writers for the whole call. The trusted, quiescent local
filesystem boundary reuses existing .NET 10 Linux x64 inspection qualification;
Windows is not newly qualified and other Unix configurations fail closed.
Checks/open are not an atomic snapshot or hostile replacement-safe confinement.
The report describes captured bytes, not continued path validity or a durable
receipt. Preserve required/nonreproducible payloads yourself, even if generated;
a hash or recipe is insufficient. AGD does not operate Git or edit `.gitignore`.
See the [Git workflow](git-workflow.md).

Neither operation adds jobs, selection/SQLite store, cache, receipts, source writes,
sealing, payload preservation, native import, new dependencies/formats or new MCP
resources; metadata APIs remain pure. New AGD code is MIT, with no external package,
native codec or copyleft host tool added. The existing MIT-compatible permissive
dependency policy is unchanged. Reads can update access times, but not contents or mtimes.
Limits and [error codes](provided-artifacts.md#result-and-errors) are separate from
document validation: CLI exits `0` on success, `4` for invalid arguments/format/version,
`1` for other preflight/verification failures, `130` for cancellation, never asset-drift exit `6`.

## Document model cheat-sheet

The full reference is [`format.md`](format.md). What matters day to day:

**Top level**

| Field | Notes |
|-------|-------|
| `version` | Always `1`. |
| `id` | Document GUID; must differ from the root node id (`DOC006`). |
| `name` | Non-empty. Used as the SVG symbol id by `agd sprite`, so keep it a valid id (letter first; letters, digits, `-`, `_`). |
| `revn` | Revision, starts at `1`; the engine increments it on every mutation. |
| `canvas` | `width`, `height` (> 0), optional `background` color. Omit `background` for transparency. |
| `root` | A `frame` whose `width`/`height` should equal the canvas. `clipContent: true` clips children to the frame. |
| `tokens` | Optional `colors` and `typography` maps. |

**Coordinates.** Origin is the top-left corner, y grows downward, units are canvas pixels. A node's `transform.x`/`transform.y` translate it relative to its parent; `rotation` is degrees; `scaleX`/`scaleY` default to `1`.

**Node types and the fields that matter**

| `type` | Geometry fields | Notes |
|--------|-----------------|-------|
| `frame` | `width`, `height`, `clipContent`, `children` | Container with bounds; the root is always a frame. |
| `group` | `children` | Container without bounds; use it to move or name a cluster. |
| `rectangle` | `width`, `height`, `cornerRadius` | Positioned by `transform.x`/`y` (top-left corner). |
| `ellipse` | `width`, `height` | Bounding box, so a circle is `width == height`. |
| `path` | `d` | SVG path data; overlapping subpaths follow `style.fillRule`. See the path tips below. |
| `line` | `x1`, `y1`, `x2`, `y2` | Needs a stroke to be visible. |
| `polygon` | `points` (`[{x,y}, ...]`), `closed` | `closed: false` renders a polyline; self-intersections follow `style.fillRule`. |
| `text` | `text`, `fontFamily`, `fontSize`, `fontWeight`, `typographyTokenRef` | Raster output shapes with fonts available on the rendering host; SVG output depends on the viewer's fonts. |
| `image` | `src`, `width`, `height` | Raster export only decodes bounded base64 data URIs; anything else renders a placeholder. |

Every node also has `id` (GUID, unique in the document), `order` (sibling sort key, see below), optional `name`, `visible`, `locked`, `transform`, and `style`.

**Style**

```json
"style": {
  "fill":   { "color": "#336699" },
  "stroke": { "color": "#000000", "width": 2, "cap": "round", "join": "round" },
  "opacity": 1
}
```

- Colors are `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, `rgb()`, or `rgba()`. Named colors (`black`, `currentColor`) are rejected (`STYLE002`/`STYLE003`).
- `fill.tokenRef` / `stroke.tokenRef` reference a color token instead of a literal (`--fill-token` / `--stroke-token` on the CLI, `fillToken` / `strokeToken` over MCP).
- `fill.gradient` (linear or radial) takes precedence over `fill.color`.
- `fillRule` (`nonzero` by default, or `evenodd`) decides which overlapping regions of a `path` or `polygon` are inside the fill; other node types ignore it. Set it with `agd add|update --fill-rule evenodd` (`fillRule` over MCP; `update --fill-rule __clear__` resets it).
- A node with no `fill` and no `stroke` renders nothing.

**Ordering.** Siblings render in ascending `order` (ordinal string compare), not array order. `order` uses the base-62 alphabet `0-9A-Za-z`, so `a0 < a1 < a2 < b0`. `agd add` and `agd reorder` maintain it for you; when you hand-write nodes, give every sibling a distinct value.

## Conventions that make documents reviewable

These conventions make diffs readable and reviews fast.

**One document per asset.** `icon-power.agd.json` contains exactly the power icon. Sprites and sheets are built at export time (`agd sprite`), not authored as one giant scene.

**Readable GUID families.** Hand-authored documents do not need random GUIDs. Choose a family per asset and number the nodes:

```
document 11111111-0001-0002-0001-000000000001
root     11111111-0001-0002-0001-000000000002
nodes    11111111-0001-0002-0001-000000000010, ...-000000000011, ...
```

Any well-formed GUID is accepted; the only rules are uniqueness within the document (`NODE002`) and document id != root id (`DOC006`). A reviewer can then tell at a glance which asset a node belongs to. `agd create --seed <N>` is the CLI's own deterministic alternative when you do not care about readability.

**Distinct, short `order` strings.** Number siblings `a0`, `a1`, `a2`, ... in the array order you wrote them. A container whose children share an `order` (including the case where you omitted it, since the default is `a0`) fails validation with `NODE010` and blocks every mutation with `INVALID_PROPERTY: ... duplicate sibling order`.

**Name every node** (`"name": "bolt"`, `"name": "outer-ring"`). Names show up in `agd inspect --tree`, `agd list --tree`, and `list_nodes`, and a reviewer reads names, not GUIDs.

**Keep the root honest.** Root `width`/`height` equal the canvas; `clipContent: true`; no transform on the root.

**Explicit over implicit** in committed files: write `"visible": true`, `"locked": false`, `"opacity": 1` if the consumer's export pipeline compares serialized output, because `agd` writes them on save.

**Commit source and output together** when a downstream project consumes the SVG; the `.agd.json` is the source of truth and the SVG is a build artifact you can regenerate with `agd export`.

## The icon recipe

Small, single-color UI icons are the best-tested use of AGD. The recipe:

1. **Canvas 24 or 32 px, no background.** `agd create <name> --template web-icon-32` (or `web-icon-24`). Omit `canvas.background`; the web-icon profile rejects an opaque one (`WEBICON001`).
2. **Black fills, `currentColor` at export.** Use literal `#000000` for fills and strokes, then export with `agd export icon.agd.json --current-color "#000000" -o icon.svg` (or `agd sprite ... --current-color "#000000"`; MCP `export_document` takes `currentColor`). Every fill or stroke that resolves to exactly that color is written as `currentColor`, so the icon inherits the CSS `color` of its host element; gradient stops and the canvas background are left alone, and the option is svg-only. Internal color differences do not survive a monochrome icon, so shape carries the meaning.
3. **Geometry only.** No `text` (`WEBICON002`), no `image` (`WEBICON003`), no unknown node types (`WEBICON004`). Paths, rectangles, ellipses, lines, polygons, groups, and frames are allowed.
4. **Validate and export with the profile.**
   ```sh
   agd validate icon.agd.json --profile web-icon --strict
   agd export icon.agd.json --profile web-icon --strict --current-color "#000000" -o icon.svg
   ```
5. **Symbols and sprites.** `agd export icon.agd.json --symbol ic-power -o ic-power.symbol.svg` emits a bare `<symbol id="ic-power" viewBox="0 0 32 32">` fragment (svg only; `--scale`, `--width`, and `--height` are rejected in symbol mode). `agd sprite a.agd.json b.agd.json -o sprite.svg --strict --profile web-icon --current-color "#000000"` wraps one symbol per document in a single `<svg><defs>` sheet, using each document's `name` as the symbol id; names must therefore be unique and valid ids. Reference them with `<use href="#ic-power">`.
6. **Cutouts.** Draw the hole as a second contour and set `style.fillRule` to `evenodd` (`agd add ... --fill-rule evenodd`, or `agd update <doc> <node-id> --fill-rule evenodd`); SVG export writes `fill-rule="evenodd"` and raster rendering matches, so you never have to reason about winding direction. The default `nonzero` fills an inner contour wound the same way as its outer one. `agd path bool difference <outer> <hole>` also works (its output keeps holes in the opposite winding), and the geometry operations accept `--fill-rule evenodd` when their inputs already rely on it.
7. **Check legibility at target size.** Render at 1x (`agd export -t png`) and 4x, and look at both. Stroke width 2 on a 32 px canvas is a good default; below 1.5 px the mask disappears at 16 px.

Over MCP, the same recipe uses `create_document` with `template: "web-icon-32"`, `add_node` with `type: "path"` and `pathData`, `validate_document` / `export_document` with `profile: "web-icon"` and `strict: true`, `export_document` with `currentColor: "#000000"` for the monochrome SVG, and `export_document` with `symbolId` for the fragment.

## Tokens and palettes

Tokens let several nodes share a color and let a downstream design system supply values.

```sh
agd tokens set-color doc.agd.json primary "#0066cc"
agd tokens set-color doc.agd.json accent "{primary}"          # alias
agd add doc.agd.json rectangle -w 100 -h 40 --fill-token primary
agd tokens list-colors doc.agd.json
agd tokens remove-color doc.agd.json accent
```

Token values are strict colors or aliases (`primary` or `{primary}`); missing or cyclic references are validation errors (`TOKEN003`, `TOKEN006`). Exporters resolve `tokenRef` to the literal value when writing SVG or raster output.

Bundled palettes populate the color map in one step: `agd tokens list-palettes` shows them (`tailwind`, `material`, `colorbrewer-qualitative`); `agd tokens apply-palette doc.agd.json tailwind` adds the palette's entries to the document.

DTCG (Design Tokens Community Group) files round-trip: `agd tokens export doc.agd.json -o doc.tokens.json` (add `--resolve-refs` to flatten aliases) and `agd tokens import doc.agd.json tokens.json`. `$description` and other DTCG metadata are not preserved.

MCP equivalents: `set_color_token`, `remove_color_token`, `list_color_tokens`, `apply_palette`, `list_palettes`, `import_dtcg_tokens`, `export_dtcg_tokens`.

Icons that will inherit their color from CSS should keep literal `#000000` (exported with `--current-color "#000000"`) rather than token references; tokens are for multi-color art and layouts.

## Path authoring tips

- **Use absolute commands** (`M`, `L`, `H`, `V`, `C`, `S`, `Q`, `T`, `A`, `Z`). Relative commands are accepted, but absolute coordinates are easier to review against the canvas and to edit by hand.
- **Prefer integer-friendly coordinates** on the pixel grid (`M18 2 L8 16`). On a 32 px canvas, half-pixel coordinates blur edges at 1x. Reserve decimals for curves.
- **Close shapes with `Z`.** An unclosed path with a fill still fills, but strokes and boolean operations behave better with explicit closure.
- **Elliptical arcs work in geometry operations.** `agd path bool`, `agd path offset`, `agd path union`, their MCP twins, and bounds accept `A`/`a`, including compact SVG flags. Geometry converts arcs to cubics and flattens them; SVG export preserves the original path data. The first release, 0.1.0, rejected arcs in geometry operations; 0.2.0-preview.2 and later accept them.
- **Build shapes with booleans instead of hand-solving intersections.**
  ```sh
  agd path bool union "M0 0 L20 0 L20 20 L0 20 Z" "M10 10 L30 10 L30 30 L10 30 Z"
  agd path bool difference "M0 0 L32 0 L32 32 L0 32 Z" "M8 8 L24 8 L24 24 L8 24 Z"
  agd path union "M0 0 L8 0 L8 8 L0 8 Z" "M8 0 L16 0 L16 8 L8 8 Z"
  agd path offset "M4 4 L28 4 L28 28 L4 28 Z" 2 --join round
  agd path union "M0 0 L32 0 L32 32 L0 32 Z" "M8 8 L24 8 L24 24 L8 24 Z" --fill-rule evenodd
  ```
  `--fill-rule nonzero|evenodd` (`fillRule` over MCP, default `nonzero`) tells the operation how to read overlapping contours in its inputs; use `evenodd` when the input path is drawn for `evenodd` rendering so the hole survives. Each prints a new `d` string (`-f json` for the envelope) that you paste into `--path-data` or the node's `d`. Curves are flattened deterministically; output contours are polygons.
- **Offsets make outlines.** A positive `path offset` grows the shape, a negative one shrinks it; subtracting the shrunken shape from the original (`path bool difference`) yields a ring-shaped outline that renders as a fill, which masks better than a stroke.
- **Keep the whole shape inside the canvas.** Geometry outside the root frame is clipped when `clipContent` is `true`, which is the default.

## Raster export sizing

`scale` uniformly scales geometry and the logical viewport. `width`/`height` override
that viewport **before scaling**; they crop or pad rather than fitting artwork.
For example, a 128×128 canvas at 32×32 pixels uses:

```sh
agd export mark.agd.json -t png --scale 0.25 -o mark-32.png
```

MCP: `export_document(idOrPath="mark.agd.json", outputPath="mark-32.png",
format="png", scale=0.25)`. A 128×64 canvas at the same scale produces 32×16,
preserving aspect ratio. Adding `width=64` crops the logical viewport to its left
64 units, producing 16×16 at that scale; it does not squeeze the whole artwork.
Frame clipping still applies. For `--node` / `nodeId`, scale applies to the subtree's
own bounds; width/height overrides are rejected. See
[asset-manifest sizing](asset-pipeline.md#export-options).

## MCP session tips

- **Working-directory confinement.** Start the server with `agd-mcp --working-directory /path/to/designs`. Every `idOrPath`, `outputPath`, `svgPath`, `descriptorPath`, and token path is resolved inside that directory; anything that escapes (`../`, absolute paths elsewhere) fails with `PATH_ESCAPE`. `list_documents` enumerates only that directory. `preflight_artifact` and `verify_artifact` reject lexical root escapes before path probes and complete confinement (which can inspect attributes) before Core inventory/content reads; payloads are confined beneath the descriptor's bundle directory.
- **Ids versus paths.** `create_document` accepts `outputPath`, for example
  `icons/ic-power.agd.json`, and returns its `locator`, `rootId` and `revn`.
  Address readable-path documents by that locator; mutations save back to the same
  file. Omit `outputPath` for GUID-store creation. Existing files require `force:true`.
- **Typed batches (0.3 wire cutover).** `apply_patch.operations` is an actual array
  of operation objects; align/distribute `nodeIds` is an array of GUID strings.
  Earlier string-encoded inputs are rejected. The CLI still reads patch JSON from
  a file/stdin/inline text and accepts its documented node-selection flags.
- **`expectedRevn`.** Document mutations (`add_node`, `update_node`, `delete_node`,
  `move_node`, `reorder_node`, `apply_patch`, `align_nodes`, `distribute_nodes`,
  `fit_frame`, `set_color_token`, `remove_color_token`, `apply_palette`,
  `import_dtcg_tokens`) accept it. Use the last observed revn; mismatch is
  REVISION_CONFLICT, omission chooses last-writer-wins. Creation uses exclusive/force
  semantics; native edits require the separate three-field snapshot token.
- **Git history.** Track project sources, manifests/settings and required artwork, not local lock markers or rebuildable caches. AGD does not operate Git. Pause writers before checkout/restore/merge, then reload and validate: `revn` can move backwards or repeat across branches, and Git does not take AGD's locks. Discard cached revisions and pre-restore plans. Git history is not semantic undo or request deduplication; inspect the document after a lost response rather than blindly repeating a mutation.
- **`render_preview`** defaults to an aspect-preserving <=512px thumbnail without
  upscaling. Explicit `scale` is 0.1..4; effective output remains <=2048px per side.
  Explicit width/height retain logical viewport semantics rather than fitting geometry.
- **Bounded inspection.** `list_nodes` accepts subtree `nodeId`, `offset`, `limit`
  (default 100, max 1000) and relative `maxDepth`; inspect `total`/`hasMore` before
  assuming a complete listing. `get_document_json(nodeId)` returns only that canonical
  subtree, while omission explicitly requests the whole document.
  Core scan budget is 100000 nodes/depth128 with cooperative cancellation; oversized
  trees return RESOURCE_LIMIT rather than unreachable pages. Stdio messages are
  bounded to 8 MiB; Core/CLI patch arrays remain 16 MiB, so transport can reject earlier.
  Returned summary depth is relative to the selected root; absolute document paths
  remain available. Unrelated later subtrees and pruned relative depths are not scanned.
- **`export_document`** writes files (`format`: `svg`, `png`, `jpeg`, `webp`) and accepts the same `profile`, `strict`, and `symbolId` options as the CLI. Raster width/height override the logical viewport; use `scale` to resize geometry. Out-of-range dimensions/scale are rejected, not clamped.
- **`get_document_json`** returns the canonical JSON when you want to reason about the whole tree; `list_nodes` is the cheaper summary.
- **Read the reference resources** (`agd://agent-guide`, `agd://format`, `agd://schema`) with `resources/read` when you need the document model; they are the same files as this repository's `docs/` and `schemas/`.
- Tool results carry `isError` at the protocol level and the `{ ok, data | error }` envelope in both `structuredContent` and the text block; parse the envelope and branch on `error.code`.

## Native layered still-pixel recipe

Native still assets are source directories (`asset.agd-pixels.json` plus immutable
`payloads/`), separate from vector `.agd.json` files and supplied-artifact descriptors.
The supported release profile is .NET 10 Linux x64. Create uses a nonexisting asset
directory beneath an existing parent; edits persist immediately.

CLI journey:

```sh
agd pixels create artwork/icon.pixels --name icon --width 64 --height 64 -f json
agd pixels inspect artwork/icon.pixels -f json
agd pixels edit artwork/icon.pixels --request edit.json -f json
agd pixels validate artwork/icon.pixels -f json
agd pixels preview artwork/icon.pixels --output artwork/icon-preview.png -f json
agd pixels export artwork/icon.pixels --output artwork/icon.png -f json
agd pixels check artwork/icon.pixels --output artwork/icon.png -f json
```

The MCP journey is create_pixel_asset → inspect_pixel_asset → edit_pixel_asset →
validate_pixel_asset → preview_pixel_asset → export_pixel_asset → check_pixel_export.
`create_pixel_asset` takes `assetDirectory`, `name`, `width`, `height` and returns
`data.state.snapshot` plus `data.initialLayerId`. Inspection returns `data.snapshot`
and `data.layers` ordered bottom to top; layer IDs are `layers[i].id`.

Replace the three placeholder strings below with the exact returned snapshot/layer
values, preserving integer revision spelling. Save this object as edit.json for CLI,
or pass it as the actual `request` object to edit_pixel_asset alongside `assetDirectory`.

```json
{
  "expectedSnapshot": {
    "assetId": "<ASSET_ID>",
    "revn": 1,
    "sourceDigest": "<SOURCE_DIGEST>"
  },
  "operations": [
    {
      "op": "fill", "layer": "<LAYER_ID>", "target": "pixels",
      "rect": { "x": 16, "y": 16, "width": 32, "height": 32 },
      "rgba": "#336699ff"
    },
    { "op": "addLayer", "name": "Highlight", "alias": "highlight" },
    {
      "op": "fill", "layer": "@highlight", "target": "pixels",
      "rect": { "x": 20, "y": 20, "width": 8, "height": 8 },
      "rgba": "#ffffff80"
    },
    { "op": "attachMask", "layer": "@highlight" },
    {
      "op": "fill", "layer": "@highlight", "target": "mask",
      "rect": { "x": 20, "y": 20, "width": 4, "height": 8 }, "value": 0
    },
    { "op": "setOpacity", "layer": "@highlight", "opacity": 0.75 }
  ]
}
```

An edit contains 1..64 operations within 1 MiB and increments revision once when
the final state changes. Layer aliases are local to that batch. Colors are exact
lowercase `#rrggbbaa`; rectangles are nonnegative integers entirely inside the canvas.
An attached mask starts fully covered; mask fill uses `target:"mask"` and a byte
`value` field. Use `inspect_pixel_region` with `layerId`, `target` and a `rect`
to read actual samples (at most 4096 entries), rather than guessing from a preview.

Keep the whole `{assetId,revn,sourceDigest}` token unchanged. A stale token returns
REVISION_CONFLICT; inspect and reconsider the edit instead of blindly replaying it.
Optional read guards for preview/export/check use `expectedReadSnapshot` with the
same three fields. Re-read after Git restore or any successful edit. Native objects
are closed: omit optional fields rather than sending null; duplicates, unknown
fields and unsupported numeric spelling are rejected before typed binding.

Validation can complete with `ok:true` and `data.valid:false`; inspect validity.
Check reports PIXEL_OUTPUT_DRIFT (CLI exit 6) when an existing export differs.
Existing export destinations require `overwrite:true` / CLI `--overwrite`.
MCP preview returns one inline PNG; exported bytes and sources remain on disk.
This create/edit workflow does not animate frames or build an asset manifest. To start
from a supplied PNG, use the development source's [preserved still import](native-still-import.md).

## Troubleshooting

The codes agents hit most, and what to do:

| Code | Meaning | Fix |
|------|---------|-----|
| `NODE010`, or `INVALID_PROPERTY` "... duplicate sibling order" on a mutation | Two siblings share an `order` (often because it was omitted and defaulted to `a0`). | Give each sibling a distinct base-62 `order` (`a0`, `a1`, ...). |
| `NODE002` | Duplicate node id. | Renumber within your GUID family. |
| `NODE003` / `NODE009` | `order` empty or outside `0-9A-Za-z`. | Use short base-62 strings. |
| `DOC006` | Document `id` equals root `id`. | Give the root its own GUID. |
| `STYLE002` / `STYLE003` | Fill/stroke color is not hex or `rgb()`/`rgba()`. | Replace named colors (`black`, `currentColor`) with `#000000`. |
| `STYLE012` | `fillRule` is not `nonzero` or `evenodd`. | Fix the value or clear it (`update --fill-rule __clear__`). |
| `CANVAS009` | Invalid `canvas.background`. | Use a hex color or omit the field. |
| `WEBICON001` | Web-icon profile requires a fully transparent background. | Omit `canvas.background` (or use `#00000000`). |
| `WEBICON002` / `WEBICON003` / `WEBICON004` | Text, image, or unknown node in a web icon. | Convert text to paths outside AGD, drop images, fix the `type`. |
| `PATH001` (warning) | Empty path `d`. | Fill in the geometry; `--strict` treats warnings as failures. |
| `TEXT002` (warning) | Font weight outside 100-900. | Use a standard weight (400, 700). |
| `TOKEN003` / `TOKEN006` | Cyclic or missing color token reference. | `agd tokens list-colors` and fix the alias or `tokenRef`. |
| `INVALID_DIMENSIONS` | Canvas or override size is not finite/positive (or over 100000). | Check `-w`/`-h`, template, and export overrides. |
| `INVALID_ARGUMENTS` "Unknown template" / "Unknown validation profile" / "--current-color only applies to svg export" | Bad `--template` or `--profile` id, or `--current-color` on a raster export. | Valid templates: `web-icon-32`, `web-icon-24`, `social-1200x630`; the only profile is `web-icon`; use `--current-color` with `-t svg` only. |
| `PATH_OP_FAILED` | Invalid or out-of-bounds geometry input. | Check path syntax, arc flags (0 or 1), finite coordinates, and resource limits. The first release, 0.1.0, also required arcs to be converted to cubics by hand. |
| `NODE_NOT_FOUND` / `PARENT_NOT_FOUND` / `PARENT_NOT_CONTAINER` | Bad node or parent id in add/update/move. | Copy ids from `agd list -f json` or `list_nodes`; parents must be `root`, a frame, or a group. |
| `REVISION_CONFLICT` | `expectedRevn` is stale. | Reload, read the current `revn`, and retry the mutation. |
| `PATH_ESCAPE` | A path left the MCP working directory. | Use relative paths inside the working directory. |
| `STORE_CONFLICT` | A store-backed command's store (`./.agd` for the CLI, the working directory for MCP) would sit inside another store, or would contain one (another `.agd-store.lock` below it). | Inside another store: run from outside the reported store root, or use file-only commands (`agd path ...`, `validate`, `assets`). Containing one: move or remove the reported inner store, or start from a directory that does not contain it. |
| `DOCUMENT_NOT_FOUND` | Unknown id or path. | `list_documents` / `agd list-documents` to see what exists. |
| `ARTIFACT_VERIFY_BUSY` | Another verification is active in this host. | Wait for it to finish before making another call; there is no server queue. |
| `ARTIFACT_PNG_INVALID` | Invalid PNG structure or, for verification, compressed stream/samples. | Correct the supplied PNG and matching descriptor claims; preflight alone cannot establish decodability. |
| `RESOURCE_LIMIT` during verification | An inherited input bound, DEFLATE block limit or cooperative time budget was exceeded. | Check the [bounds](provided-artifacts.md#verification-bounds-and-errors); do not treat this as admission or a hard-sandbox stop. |

Document validation codes not listed here are documented in [`format.md`](format.md#validation-error-codes); validation diagnostics carry a `path` (for example `root.children[2].style.fill`) that points at the offending field. Artifact preflight and verification use the [separate error reference](provided-artifacts.md#result-and-errors) with [verification additions](provided-artifacts.md#verification-bounds-and-errors); their bounded messages include a metadata location when available, but a separate `error.path` is not guaranteed.
