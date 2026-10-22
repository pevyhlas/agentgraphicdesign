# AGD Document Format Reference

This document describes the `.agd.json` file format used by Agent Graphic Design.

## Overview

AGD documents are JSON files with the `.agd.json` extension. Each document describes a vector graphics scene with a hierarchical node tree. The format is designed to be:

- **LLM-friendly**: Clear structure with predictable field names
- **Round-trip safe**: Unknown fields are preserved through load/save cycles
- **Versioned**: Format version enables forward compatibility

## Schema

The repository JSON Schema is available at:
```
schemas/agd-document.schema.json
```

The public schema URL is the format-v1 schema identifier: a stable `$id` string, not a promise that the URL resolves. This public repository was started from fresh history and has no `v0.1.0` tag, so the URL does not currently resolve; validate against the repository copy instead (for example by mapping the identifier to the local file in your editor). Documents carry it in `$schema`:
```json
{
  "$schema": "https://raw.githubusercontent.com/pevyhlas/agentgraphicdesign/v0.1.0/schemas/agd-document.schema.json",
  "version": 1,
  ...
}
```

## Format-v1 Compatibility

- `0.1.0` reads, writes, and mutates document format version `1`. Package version and document format version are independent.
- Versions below `1` are invalid. Newer format versions produce a compatibility warning and cannot be mutated by this implementation.
- Unknown properties and unknown node payloads are preserved on supported round trips where possible, but this is not a semantic compatibility guarantee for future format versions.
- Format `1` is the v0.1.0 contract (released 2026-09-02). Later 0.x releases may add optional fields to format `1` without changing the format version; an incompatible change to the document shape requires a new format version.

## Document Structure

```json
{
  "$schema": "...",
  "version": 1,
  "id": "uuid",
  "name": "document-name",
  "revn": 1,
  "canvas": { ... },
  "root": { ... },
  "tokens": { ... }
}
```

### Top-Level Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `$schema` | string | No | JSON Schema URL for validation |
| `version` | integer | Yes | Format version (currently `1`) |
| `id` | string (UUID) | Yes | Unique document identifier |
| `name` | string | Yes | Document name (non-empty) |
| `revn` | integer | Yes | Revision number, incremented by document mutations |
| `canvas` | object | Yes | Canvas dimensions and background |
| `root` | object | Yes | Root frame node containing the scene tree |
| `tokens` | object | No | Design tokens (colors, typography) |

## Canvas

```json
{
  "canvas": {
    "width": 1920,
    "height": 1080,
    "background": "#ffffff"
  }
}
```

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `width` | number | Yes | Canvas width in pixels (must be > 0) |
| `height` | number | Yes | Canvas height in pixels (must be > 0) |
| `background` | string | No | Background color in hex format |

## Node Types

All nodes share a common base structure and are distinguished by the `type` discriminator property.

### Base Node Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `type` | string | Yes | Node type discriminator |
| `id` | string (UUID) | Yes | Unique node identifier |
| `order` | string | No | Fractional index for sibling ordering (base-62) |
| `name` | string | No | Node name for identification |
| `visible` | boolean | No | Visibility (default: `true`) |
| `locked` | boolean | No | Lock state (default: `false`) |
| `transform` | object | No | Position and transformation |
| `style` | object | No | Fill, stroke, and opacity |

### Node Type: `frame`

Container node with explicit dimensions. The root node is always a frame.

```json
{
  "type": "frame",
  "id": "...",
  "width": 800,
  "height": 600,
  "clipContent": true,
  "children": [ ... ]
}
```

| Property | Type | Description |
|----------|------|-------------|
| `width` | number | Frame width |
| `height` | number | Frame height |
| `clipContent` | boolean | Clip children to frame bounds (default: `true`) |
| `children` | array | Required child-node collection; use `[]` for an empty frame |

### Node Type: `group`

Container node without explicit dimensions. Groups children together.

```json
{
  "type": "group",
  "id": "...",
  "children": [ ... ]
}
```

| Property | Type | Description |
|----------|------|-------------|
| `children` | array | Required child-node collection; use `[]` for an empty group |

### Node Type: `rectangle`

Rectangle shape with optional rounded corners.

```json
{
  "type": "rectangle",
  "id": "...",
  "width": 100,
  "height": 50,
  "cornerRadius": 8
}
```

| Property | Type | Description |
|----------|------|-------------|
| `width` | number | Rectangle width |
| `height` | number | Rectangle height |
| `cornerRadius` | number | Corner radius (default: `0`) |

### Node Type: `ellipse`

Ellipse shape defined by bounding box dimensions.

```json
{
  "type": "ellipse",
  "id": "...",
  "width": 100,
  "height": 100
}
```

| Property | Type | Description |
|----------|------|-------------|
| `width` | number | Horizontal diameter |
| `height` | number | Vertical diameter |

### Node Type: `path`

Arbitrary vector path using SVG path syntax.

```json
{
  "type": "path",
  "id": "...",
  "d": "M 0 0 L 100 100 Z"
}
```

| Property | Type | Description |
|----------|------|-------------|
| `d` | string | SVG path data |

Path nodes preserve their SVG `d` string. Rendering and geometry operations are separate contracts: `agd path bool/offset/union` supports absolute and relative `M`, `L`, `H`, `V`, `C`, `S`, `Q`, `T`, `A`, and `Z`. Geometry operations convert elliptical arcs to bounded cubic segments, then flatten curves deterministically; resulting bounds are approximate. Arc support is an unreleased addition toward 0.3, not a format change.

Overlapping subpaths are filled according to `style.fillRule` (`nonzero` by default, or `evenodd`); see [Fill Rule](#fill-rule).

### Node Type: `text`

Text content with typography and paragraph layout properties.

```json
{
  "type": "text",
  "id": "...",
  "text": "Hello World",
  "fontFamily": "Arial",
  "fontSize": 24,
  "fontWeight": 700,
  "textAlign": "center",
  "maxWidth": 320,
  "lineHeight": 1.4,
  "letterSpacing": 0.5
}
```

| Property | Type | Description |
|----------|------|-------------|
| `text` | string | Text content; `\n` always starts a new line |
| `fontFamily` | string | Font family name |
| `fontSize` | number | Font size in pixels (default: `16`) |
| `fontWeight` | integer | Font weight 100-900 (default: `400`) |
| `typographyTokenRef` | string | Reference to typography token |
| `textAlign` | string | `start` (default), `center`, or `end`: where each line sits relative to the node's x |
| `verticalAlign` | string | `top` (default), `middle`, `bottom`, or `baseline`: which edge of the block sits at the node's y |
| `maxWidth` | number | Wrap width in pixels (> 0); omitted means no wrapping |
| `lineHeight` | number | Line pitch as a multiple of `fontSize` (> 0); see precedence below |
| `letterSpacing` | number | Extra advance after each glyph in pixels (default: `0`) |

All five layout fields are optional and additive to format `1`; canonical serialization omits them when unset, and a document that does not use them exports byte-identical SVG and identical raster output to a document written before they existed.

**Anchor.** The node's transform places the anchor. Horizontally, `start` puts the first glyph at x, `center` centres each line on x, and `end` finishes each line at x (SVG `text-anchor="middle"`/`"end"`). Vertically, the default `top` keeps today's behaviour: the first baseline sits one font size below y (SVG `y="{fontSize}"` inside the node transform; the renderer draws the baseline at local `y = fontSize`). The block is `lines × lineHeight × fontSize` tall with its top at y; `middle` centres that block on y, `bottom` ends it at y, and `baseline` puts the first baseline exactly at y.

**Wrapping.** With `maxWidth`, each paragraph wraps greedily at spaces so that no line measures wider than `maxWidth`; a single word wider than the limit stays on its own line and is never broken inside. Runs of spaces between wrapped words collapse to one space. Lines are measured with the registered text measurer (Skia shaping in the `agd` and `agd-mcp` hosts); the same measurer is used by `bounds`, SVG export, and raster export, so all three agree. A host without a measurer falls back to the glyph-count estimate and reports `measured: false` in `bounds`.

**Line height precedence.** The node's `lineHeight` wins, then a positive `lineHeight` on the referenced typography token, then the default `1.2`. It only affects output when the text has more than one line. (Font family, size, and weight keep their existing rule: the token value wins over the node value.)

**SVG output.** A single line exports as `<text y="…">…</text>` exactly as before. Multiple lines export as one `<tspan x="0" dy="…">` per line inside the `<text>`: the first with `dy="0"`, the rest with `dy` equal to the line pitch (an empty line carries its pitch into the next tspan). `textAlign` becomes `text-anchor`, `letterSpacing` becomes `letter-spacing`, and `verticalAlign` shifts the `y` attribute.

Raster export resolves `fontFamily` through the font policy described in the README (font directories, then the bundled Inter family, then optional host fonts); a missing `fontFamily` always renders with bundled Inter. SVG export writes the family name unchanged.

### Node Type: `line`

```json
{
  "type": "line",
  "id": "...",
  "x1": 0,
  "y1": 0,
  "x2": 100,
  "y2": 100
}
```

All four endpoint fields are numbers. A visible line normally needs a stroke color.

### Node Type: `polygon`

```json
{
  "type": "polygon",
  "id": "...",
  "points": [{ "x": 0, "y": 0 }, { "x": 100, "y": 0 }, { "x": 50, "y": 80 }],
  "closed": true
}
```

`closed: false` exports/renders a polyline; the default is `true`. Self-intersecting polygons honor `style.fillRule` (`nonzero` by default, or `evenodd`); see [Fill Rule](#fill-rule).

### Node Type: `image`

```json
{
  "type": "image",
  "id": "...",
  "src": "assets/photo.png",
  "width": 320,
  "height": 200
}
```

SVG export writes `src` to `<image href="...">`. Raster export decodes base64 data URIs for PNG, JPEG, GIF, and WebP when decoded content is at most 1 MB. Relative paths, remote URLs, invalid data, unsupported MIME types, and oversized payloads draw an `[image]` placeholder; the renderer performs no file or network fetch.

### Node Type: `unknown`

Fallback type for unrecognized node types. Preserves original data.

```json
{
  "type": "unknown",
  "id": "...",
  "originalType": "future-node-type"
}
```

## Transform

```json
{
  "transform": {
    "x": 100,
    "y": 50,
    "rotation": 45,
    "scaleX": 1.5,
    "scaleY": 1.5
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `x` | number | `0` | X translation |
| `y` | number | `0` | Y translation |
| `rotation` | number | `0` | Rotation in degrees |
| `scaleX` | number | `1` | X scale factor |
| `scaleY` | number | `1` | Y scale factor |

## Style

```json
{
  "style": {
    "fill": { "color": "#ff0000" },
    "stroke": { "color": "#000000", "width": 2 },
    "opacity": 0.8,
    "blendMode": "multiply",
    "fillRule": "evenodd"
  }
}
```

### Fill

| Property | Type | Description |
|----------|------|-------------|
| `color` | string | Fill color (hex or `rgb()`/`rgba()`) |
| `tokenRef` | string | Reference to a color token |
| `gradient` | object | Linear or radial gradient; takes precedence over `color` |

Gradients have a `type` (`linear` or `radial`) and `stops`. Each stop contains `offset` (0-1), `color`, and `opacity` (0-1). Linear gradients use `x1`, `y1`, `x2`, `y2`; radial gradients use `cx`, `cy`, `r`, and optional `fx`, `fy`.

### Stroke

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `color` | string | - | Stroke color |
| `tokenRef` | string | - | Reference to a color token |
| `width` | number | `1` | Stroke width in pixels |
| `cap` | string | `butt` | `butt`, `round`, or `square` |
| `join` | string | `miter` | `miter`, `round`, or `bevel` |
| `miterLimit` | number | `4` | Miter limit |
| `dashArray` | number[] | - | Alternating dash and gap lengths |
| `dashOffset` | number | `0` | Dash phase offset |

### Opacity and Blend Mode

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `opacity` | number | `1` | Opacity (0-1) |
| `blendMode` | string | - | CSS blend mode |

### Fill Rule

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `fillRule` | string | `nonzero` | `nonzero` or `evenodd`; decides which overlapping regions of a `path` or `polygon` are inside the fill |

`fillRule` is meaningful only for `path` and `polygon` nodes and is omitted from canonical JSON when unset. Under `nonzero` (the SVG default) a subpath wound in the same direction as an enclosing subpath stays filled; under `evenodd` it becomes a hole. SVG export writes `fill-rule="evenodd"` only when the value is `evenodd`, raster rendering uses the matching Skia fill type, SVG import maps the `fill-rule` attribute or inline style back to this property, and `agd path bool/offset/union` accept the same rule through `--fill-rule` (`fillRule` in MCP) so geometry resolves overlaps consistently with rendering.

## Design Tokens

Design tokens allow reusable style values.

```json
{
  "tokens": {
    "colors": {
      "primary": "#007bff",
      "secondary": "#6c757d"
    },
    "typography": {
      "heading": {
        "fontFamily": "Inter",
        "fontSize": 32,
        "fontWeight": 700,
        "lineHeight": 1.25
      }
    }
  }
}
```

A typography token's `lineHeight` is a multiple of the font size and applies to text nodes that reference the token and do not set their own `lineHeight`.

Color token values are strict hex/`rgb()`/`rgba()` colors or aliases written as either a bare token name such as `primary` or a braced name such as `{primary}`. Arbitrary CSS color strings are not valid token values. Color and typography maps each contain at most 50,000 entries; aliases resolve through at most 64 levels, and validation bounds diagnostics per token category.

Persist token references in styles:
```json
{
  "style": {
    "fill": { "tokenRef": "primary" }
  }
}
```

`tokenRef` is the canonical reference field. The core resolver accepts a bare token name or `{tokenName}`, and SVG/raster exporters resolve fill, stroke, and typography references. DTCG metadata such as `$description` is not preserved by the simplified internal token representation.

## Schema Contract

The repository contract suite reflects serializer fields and polymorphic discriminators, validates an all-fields serialized fixture, and checks every supported example. It implements the schema keywords used by this repository, not the complete JSON Schema 2020-12 vocabulary. External consumers should still use a standards-compliant Draft 2020-12 validator.

## Color Format

Colors are specified in hex format:
- 3-digit: `#rgb`
- 4-digit: `#rgba`
- 6-digit: `#rrggbb`
- 8-digit: `#rrggbbaa`

Or functional notation:
- `rgb(r, g, b)`
- `rgba(r, g, b, a)`

## Sibling Ordering

The `order` property uses fractional indexing with a base-62 alphabet (`0-9`, `A-Z`, `a-z`). This allows inserting nodes between existing siblings without renumbering.

Example ordering: `a0` < `a1` < `a2` < `b0`

## Validation Error Codes

The validator returns structured errors with codes for programmatic handling.

### Document Errors (DOC)

| Code | Description |
|------|-------------|
| `DOC001` | Document ID is empty |
| `DOC002` | Document name is empty or whitespace |
| `DOC003` | Document version must be >= 1 |
| `DOC004` | Document revision must be >= 1 |
| `DOC005` | Document version newer than supported (warning) |

### Canvas Errors (CANVAS)

| Code | Description |
|------|-------------|
| `CANVAS001` | Canvas width must be > 0 |
| `CANVAS002` | Canvas height must be > 0 |
| `CANVAS003` | Canvas dimensions exceed 16384 (warning) |

### Node Errors (NODE)

| Code | Description |
|------|-------------|
| `NODE001` | Node ID is empty |
| `NODE002` | Duplicate node ID |
| `NODE003` | Node order is empty |

### Style Errors (STYLE)

| Code | Description |
|------|-------------|
| `STYLE001` | Opacity must be between 0 and 1 |
| `STYLE002` | Invalid fill color format |
| `STYLE003` | Invalid stroke color format |
| `STYLE004` | Stroke width cannot be negative |
| `STYLE005` | Stroke miter limit must be >= 1 |
| `STYLE006` | Dash array values cannot be negative |
| `STYLE007` | Unsupported blend mode (warning) |
| `STYLE008` | Stroke width is not a finite number |
| `STYLE009` | Stroke miter limit is not a finite number |
| `STYLE010` | Dash array value is not a finite number |
| `STYLE011` | Dash offset is not a finite number |
| `STYLE012` | fillRule must be nonzero or evenodd |

### Shape Errors

| Code | Description |
|------|-------------|
| `FRAME001` | Frame width cannot be negative |
| `FRAME002` | Frame height cannot be negative |
| `RECT001` | Rectangle width cannot be negative |
| `RECT002` | Rectangle height cannot be negative |
| `RECT003` | Corner radius cannot be negative |
| `ELLIPSE001` | Ellipse width cannot be negative |
| `ELLIPSE002` | Ellipse height cannot be negative |
| `PATH001` | Path data is empty (warning) |
| `TEXT001` | Font size must be > 0 |
| `TEXT002` | Font weight should be 100-900 (warning) |
| `TEXT003` | Font size is not a finite number |
| `TEXT004` | `maxWidth` must be > 0 |
| `TEXT005` | `lineHeight` must be > 0 |
| `TEXT006` | `textAlign` must be `start`, `center`, or `end`; `verticalAlign` must be `top`, `middle`, `bottom`, or `baseline` |
| `TEXT007` | `letterSpacing` must be a finite number |

## Example Document

```json
{
  "$schema": "https://raw.githubusercontent.com/pevyhlas/agentgraphicdesign/v0.1.0/schemas/agd-document.schema.json",
  "version": 1,
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "example-design",
  "revn": 1,
  "canvas": {
    "width": 800,
    "height": 600,
    "background": "#ffffff"
  },
  "root": {
    "type": "frame",
    "id": "550e8400-e29b-41d4-a716-446655440001",
    "name": "root",
    "width": 800,
    "height": 600,
    "children": [
      {
        "type": "rectangle",
        "id": "550e8400-e29b-41d4-a716-446655440002",
        "name": "background-rect",
        "order": "a0",
        "width": 200,
        "height": 100,
        "transform": { "x": 50, "y": 50 },
        "style": {
          "fill": { "color": "#3498db" },
          "stroke": { "color": "#2980b9", "width": 2 }
        }
      },
      {
        "type": "text",
        "id": "550e8400-e29b-41d4-a716-446655440003",
        "name": "title",
        "order": "a1",
        "text": "Hello AGD",
        "fontSize": 24,
        "fontWeight": 700,
        "transform": { "x": 50, "y": 200 },
        "style": {
          "fill": { "color": "#2c3e50" }
        }
      }
    ]
  }
}
```
