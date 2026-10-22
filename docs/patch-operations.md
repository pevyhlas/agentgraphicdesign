# Patch Operations

`agd patch` (CLI) and `apply_patch` (MCP) apply a list of operations to one document in a single atomic call: the document is loaded once, every operation is applied in order to the in-memory copy, the document is validated, and it is saved once with a single `revn` increment. If any operation fails, or validation reports an error, nothing is written and the error names the zero-based `operationIndex` that caused it.

Both surfaces accept the same JSON array. The CLI reads it from `--file <path>`, `--ops '<json>'` or `--stdin`; in 0.3 the MCP tool takes an actual `operations` array without encoding it as a JSON string. Both accept an optional `expectedRevn` checked like single-operation mutations and return `REVISION_CONFLICT` on mismatch. This is a direct MCP wire cutover; earlier string-encoded inputs are rejected.

## Limits

| Limit | Value |
|-------|-------|
| Operations per patch | 500 |
| JSON nesting depth | 64 (same as the document reader) |
| Operations JSON size | 16 MiB (same as the document input limit) |

Duplicate JSON properties, unknown properties on an operation, and unknown `op` values are rejected with the operation index.

## Operations

Every operation is an object with an `op` discriminator (any property order). `nodeId`, `parentId` and `afterId` are node GUIDs; `parentId` may be omitted or `null` for the root frame.

| `op` | Fields | Effect |
|------|--------|--------|
| `set` | `nodeId`, `path`, `value` | Set one property of a node. `path` is a JSON Pointer relative to the node's JSON form (`/style/fill/color`, `/style/fillRule`, `/transform/rotation`, `/width`, `/style/stroke/dashArray`, `/points/1/x`, `/src`, `/d`). `value` is any JSON, including `null`. Missing intermediate objects are created; arrays accept an existing index or `-` to append. `/id`, `/type` and anything under `/children` are rejected: address a child by its own `nodeId`, and change structure with the tree operations below. |
| `unset` | `nodeId`, `path` | Remove the property or array element at `path` so it reverts to its default. The target must exist; `/id`, `/type` and `/children` paths are rejected like for `set`. |
| `setNode` | `nodeId`, `node` | Replace the node in place with the complete node JSON. `node.id` must equal `nodeId`; the node type may change (the root must stay a `frame`). A container's `children` array is replaced too, so pass the existing children to keep them. A missing `order` keeps the current sibling order; nested children without an `id` receive a new GUID. |
| `addNode` | `node`, `parentId?`, `afterId?` | Insert node JSON under the parent. A missing `id` receives a new GUID (nested children too); a missing `order` places the node last, or directly after `afterId`. `order` and `afterId` are mutually exclusive. The new id is returned in `createdIds`. |
| `deleteNode` | `nodeId` | Delete the node and its subtree (never the root). |
| `moveNode` | `nodeId`, `parentId?`, `afterId?` | Move the node under the parent, last or directly after `afterId`. |
| `reorderNode` | `nodeId`, `afterId` | Reorder among siblings: directly after `afterId`, or first when `afterId` is `null`. |
| `setColorToken` | `name`, `value` | Add or update a color token (strict color or token reference). |
| `removeColorToken` | `name` | Remove a color token; `TOKEN_NOT_FOUND` when absent. |
| `setCanvas` | `width?`, `height?`, `background?` | Change canvas size and/or background. Omitted fields are unchanged; `"background": null` clears it. |

Node JSON in `setNode`/`addNode` uses the document format exactly (`type` discriminator, `children` required on `frame`/`group`; see [`format.md`](format.md)). Unknown properties are preserved as extension data, and the node type must be one of the canonical types. Tree operations reuse the engine's single-operation implementations, so ordering, index maintenance and error codes are identical to `add`/`delete`/`move`/`reorder`.

## Result

```json
{
  "revn": 4,
  "applied": 3,
  "createdIds": { "1": "9a3b8b8e-4d0c-4e4b-9a2f-4d7f7b3f1c2a" },
  "warnings": [ { "code": "PATH001", "message": "Path data is empty", "path": "root.children[2].d" } ]
}
```

Warnings come from the standard validator and never block the save. Validation errors do: a new error is attributed to the first operation after which it appears; an error that already existed before the patch and is not resolved by it fails without an `operationIndex`.

Errors use the shared envelope with an extra `operationIndex` when attributable:

```json
{ "ok": false, "error": { "code": "NODE_NOT_FOUND", "message": "Operation 2 (deleteNode): Node not found: …", "operationIndex": 2 } }
```

## Examples

```sh
cat > ops.json <<'JSON'
[
  { "op": "set", "nodeId": "3f2c…", "path": "/style/fill/color", "value": "#ff6600" },
  { "op": "set", "nodeId": "3f2c…", "path": "/transform/rotation", "value": 15 },
  { "op": "addNode", "parentId": "8d1e…", "node": { "type": "text", "text": "Sale", "fontSize": 32 } },
  { "op": "setCanvas", "background": null }
]
JSON
agd patch banner.agd.json --file ops.json --expected-revn 3 -f json
```

MCP: `apply_patch` with `idOrPath: "banner.agd.json"`, `operations` set to the same actual array, and `expectedRevn: 3`.
