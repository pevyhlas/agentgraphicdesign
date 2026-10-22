---
name: agd-authoring
description: Use when authoring or editing .agd.json documents, icons, or sprites with the agd CLI or the agd-mcp server (create, add/update nodes, validate, export SVG/PNG, symbols, tokens).
---

# AGD authoring

AGD provides headless vector scene documents and native layered RGBA8 still-pixel assets. You author shapes or bounded pixel edits, validate, preview and export; animation and tiles remain planned. Full guide: `docs/agent-guide.md` (including the guarded native-pixel recipe); vector format: `docs/format.md`; vector schema: `schemas/agd-document.schema.json`. MCP serves these as `agd://agent-guide`, `agd://format`, `agd://schema`. Discover the executable version/domains with `get_capabilities`; CLI uses `agd --version` and command help. The latest release is 0.3.0, published before this repository became public and not available here; build and install from source. Source builds report `0.3.0+<commit>` and add still import and other changes listed under *Unreleased* in `CHANGELOG.md`.

## The loop

```sh
agd create <name> --template web-icon-32 -o <path>.agd.json   # web-icon-32 | web-icon-24 | social-1200x630
agd add <doc> path -n <node-name> --fill "#000000" --path-data "M... Z"
agd validate <doc> --profile web-icon --strict
agd export <doc> -t png -s 4 -o preview@4x.png                   # look before you ship
agd export <doc> --profile web-icon --strict --current-color "#000000" -o <name>.svg
```

Over MCP: `create_document` (`template`), `add_node`/`update_node`, `validate_document`, `render_preview`, `export_document`. Use `-f json` on the CLI for the `{ ok, data | error }` envelope the MCP tools always return.

## Conventions (keep documents reviewable)

- One document per asset; `name` becomes the sprite symbol id, so use a valid id (`icon-power`, `ic-power`).
- Readable GUID families for hand-authored files: `11111111-0001-0002-0001-000000000001` (document), `...0002` (root), `...0010`, `...0011` (nodes). Ids must be unique and document id != root id.
- Every sibling needs a distinct base-62 `order` (`a0`, `a1`, ...). Omitted orders default to `a0`; a duplicate fails validation (`NODE010`) and blocks mutations.
- Name every node. Root frame `width`/`height` equal the canvas, `clipContent: true`, no transform.
- Colors are hex or `rgb()`/`rgba()` only; named colors are rejected.

## Icon recipe

- 24 or 32 px canvas, no `canvas.background`, literal `#000000` fills; export with `--current-color "#000000"` (also on `agd sprite`; MCP `export_document` `currentColor`) so the SVG inherits the host's CSS `color`.
- Geometry nodes only (path, rectangle, ellipse, line, polygon, group, frame); the `web-icon` profile rejects text, image, and unknown nodes.
- `agd export <doc> --symbol <id> -o <id>.symbol.svg` for a `<symbol>` fragment; `agd sprite a.agd.json b.agd.json -o sprite.svg --strict --profile web-icon --current-color "#000000"` for a sheet.
- Cutouts: draw the hole as a second contour and set `style.fillRule: "evenodd"` (`agd add|update ... --fill-rule evenodd`, MCP `fillRule`); `agd path bool difference <outer> <hole>` also works.
- Check the silhouette at 1x and 4x.

## Paths and tokens

- Absolute commands (`M L H V C S Q T A Z`), integer-friendly coordinates, close with `Z`.
- `agd path bool <union|intersect|difference|xor> <a> <b>`, `agd path union <paths...>`, `agd path offset <path> <distance> --join round`, each with `--fill-rule nonzero|evenodd`; elliptical arcs (`A`/`a`) are supported with bounded deterministic flattening (the first release, 0.1.0, rejected arcs in geometry operations).
- `agd tokens set-color <doc> <name> <color>`, `--fill-token <name>` on add/update, `agd tokens apply-palette <doc> tailwind`, `agd tokens export|import` for DTCG. Icons keep literal black; tokens are for multi-color art.

## MCP session

- Paths are confined to `--working-directory`; escaping gives `PATH_ESCAPE`.
- `create_document` accepts a confined `outputPath` such as `icons/power.agd.json`; its result carries `locator`, `rootId` and `revn`.
- In 0.3, `apply_patch.operations` and align/distribute `nodeIds` are actual JSON arrays. Batch complete subtrees rather than one call per shape.
- `list_nodes` accepts subtree `nodeId`, `offset`, `limit` and relative `maxDepth`; `get_document_json` accepts `nodeId` for one canonical subtree.
- Pass `expectedRevn` on mutations to get `REVISION_CONFLICT` instead of silent overwrites.
- `render_preview` defaults to a <=512px thumbnail without upscaling; explicit scale is 0.1-4 and effective output stays <=2048px per side.

## Before you finish

`agd validate --strict` (with `--profile web-icon` for icons) is green, the render looks right at target size, and the `.agd.json` source is committed next to any exported output. Troubleshooting codes: `docs/agent-guide.md#troubleshooting`.
