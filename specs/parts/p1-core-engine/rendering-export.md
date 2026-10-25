# P1 Core engine — rendering and export

**Serves**: J-01, J-02, J-03; R-02, Q-01, Q-02 · **Code**: `src/FTPlus.AgentGraphicDesign.Core/Export/`, `src/FTPlus.AgentGraphicDesign.Skia/` · Part: [P1 Core engine](../p1-core-engine.md)

## What it does

- Exports vector documents to SVG through `IExportAdapter`, and to PNG, JPEG and WebP
  through `IRenderer` (Skia); a raster format is not an export adapter. The SVG root carries
  no `version` attribute, and image references use the SVG 2 `href` form, not `xlink:href`.
- Renders raster text with HarfBuzz shaping, resolving each family from explicit font
  directories, then the bundled Inter faces, then host-installed fonts. Host fonts are on by
  default for render, export and contact sheets and off by default in the asset pipeline;
  `--no-system-fonts` or manifest `systemFonts: false` makes text independent of the host.
- Composes SVG symbol sprite sheets, exports one node's subtree (D-10) and draws contact sheets.
- Emits image nodes as `<image href>` references; raster output decodes only bounded embedded
  data and draws a placeholder for paths and URLs, never fetching them.

Format reference: [docs/format.md](../../../docs/format.md).

## Behaviors

- **P1.B-15 Isolated sub-node export**: exporting one node draws its subtree alone, cropped to
  its own bounds, without ancestor transforms or clipping, and resolves token references in
  paint and typography exactly as the whole document does; a node with no visible bounds fails
   with `NODE_NO_BOUNDS`. Test: `SubNodeExportTests`, `SubNodeRenderTests`, `ExportCommandSubNodeTests`.
- The shared internal transient builder takes one synchronized private snapshot; masks retain
  selected subtrees and ancestor chains without enabling authored-hidden nodes. Clearing a mask
  restores visibility; source/none backgrounds change only the snapshot, without another copy.
  Node crops use a fresh non-clipping probe and translated wrapper; ancestors are excluded.
  The legacy D-10 “ink” box is geometry-only: centered stroke overflow stays clipped at the
  raster viewport. SVG bytes and Linux x64 PNG hashes pin that behavior before the refactor.
  Tests: `TransientDocumentBuilderTests`, `SubNodeCharacterizationTests`.
- Raster sub-node bounds honor the caller's font directories and system-font policy. Measurement
  is non-strict because legacy crop geometry can include hidden text; the actual renderer retains
  strict-font enforcement for rendered text. A separate diagnostic walk skips hidden ancestor
  chains, preserves document order and deduplicates messages, capped at 64 plus a truncation note.
  Bounds notes use `BOUNDS_DIAGNOSTIC` on render and vector export results. SVG measurement uses
  the default font policy. Visible text is checked against existing export limits before measuring;
  transient measurement caps aggregate measured characters (including repeated wrapping probes)
  at 64,000,000 and refuses excess with `RESOURCE_LIMIT`. Hidden chains bypass the visible-text
  check. Tests: `SubNodeFontPolicyTests`, `TransientDocumentBuilderTests`.
- **P1.B-16 Deterministic fonts**: raster text resolves font directories, then bundled Inter,
  then host fonts when allowed; an unresolved family is substituted with bundled Inter and
  reported as `FONT_SUBSTITUTED`, and strict font mode fails instead. Byte-stable raster text
  (Q-01) assumes host fonts are off or the same fonts are installed on every host.
  Test: `FontPolicyUnificationTests`, `TextShapingServiceTests`.
- **P1.B-17 Reviewed golden output**: the reviewed golden SVG reproduces byte for byte from the
  sources and from installed packages. Test: `SvgExportRegressionTests`; package smoke.

## Interface guarantees

- `RenderFormat.Rgba8` is an in-process buffer, never an export format: straight sRGB RGBA,
  8 bits per channel, top-down row-major without padding, `width*height*4` bytes and MIME marker
  `application/vnd.agd.rgba8`. Core `RgbaImage` checks 1–8192 pixels per side, at most
  64,000,000 pixels and exact byte length; it wraps caller-owned memory without copying.
  Skia readback defines unpremultiplication; exhaustive alpha/channel characterization pins it
  against the managed PNG decoder. Encoders reject the raw member and undefined enum values.
  Tests: `Rgba8RenderTests`, `Rgba8UnpremultiplyCharacterizationTests`, `RenderFormatContractTests`.
- Legacy contact sheets retain their pre-refactor grid, cell-map and surface-output baselines;
  PNG hashes are Linux x64-specific, while cell maps and refusal messages are platform-neutral.
  Tests: `LegacyContactSheetCharacterizationTests` on Skia, CLI and MCP.

- Raster document colors use Core's CSS RGBA parser, including `#rgba` and `#rrggbbaa`;
  Skia's ARGB hex interpretation never overrides document semantics. Regression:
  `SkiaRendererTests.CssAlphaHexMatchesRgbaAcrossPaintAndBackground`.

- SVG export preserves supported semantics and documents lossy conversions (Q-02); token
  references resolve during export. Scene JSON is not embedded in SVG, so a round trip needs
  the original document.

## Failure semantics

- A missing font, an unsupported feature or a size past its limit is a structured diagnostic
  or error, never a silent substitute.

## Left open

- Node-scoped symbol fragments, ancestor-context export and multi-node selection (D-10).
