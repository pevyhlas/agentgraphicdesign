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
- **P1.B-16 Deterministic fonts**: raster text resolves font directories, then bundled Inter,
  then host fonts when allowed; an unresolved family is substituted with bundled Inter and
  reported as `FONT_SUBSTITUTED`, and strict font mode fails instead. Byte-stable raster text
  (Q-01) assumes host fonts are off or the same fonts are installed on every host.
  Test: `FontPolicyUnificationTests`, `TextShapingServiceTests`.
- **P1.B-17 Reviewed golden output**: the reviewed golden SVG reproduces byte for byte from the
  sources and from installed packages. Test: `SvgExportRegressionTests`; package smoke.

## Interface guarantees

- SVG export preserves supported semantics and documents lossy conversions (Q-02); token
  references resolve during export. Scene JSON is not embedded in SVG, so a round trip needs
  the original document.

## Failure semantics

- A missing font, an unsupported feature or a size past its limit is a structured diagnostic
  or error, never a silent substitute.

## Left open

- Node-scoped symbol fragments, ancestor-context export and multi-node selection (D-10).
