# Three Cards in Five MCP Calls

Executable evidence for the 0.2 roadmap exit test (an agent builds a three-column card
layout in five or fewer MCP calls):
three complete card frames, wrapped titles and bodies, helper alignment and equal
spacing, then lint with zero warnings. This is not 0.3 release qualification or
human visual approval.

## Call Budget

| Count | `tools/call` name | Effect |
| --- | --- | --- |
| 1 | `create_document` | Create a 960 x 400 canvas; retain returned `data.id`. |
| 2 | `apply_patch` | Atomically insert three complete frame subtrees; revision 1 -> 2. |
| 3 | `align_nodes` | Align their tops to the selection; revision 2 -> 3. |
| 4 | `distribute_nodes` | Derive equal horizontal gaps, holding endpoints fixed; revision 3 -> 4. |
| 5 | `lint_document` | Read-only default-profile lint: zero warnings and infos. |

Count every outgoing JSON-RPC request whose method is `tools/call`, including
lint, not individual patch operations or internal engine work. There are exactly
five, with no retries, discovery tools, `get_bounds`, or export/preview tool call.
`initialize` and `notifications/initialized` are transport setup, not tool calls
(six JSON-RPC requests plus one notification in total). This assumes a fresh
document and a client that already knows these schemas. Extra discovery, retries,
or protocol export calls would increase the count, not disappear from it.

## Run the Recipe

From the repository root, build Release first:

```sh
dotnet build tests/FTPlus.AgentGraphicDesign.Mcp.Tests --configuration Release
dotnet test tests/FTPlus.AgentGraphicDesign.Mcp.Tests --configuration Release --no-build --filter FullyQualifiedName~Roadmap02_ThreeWrappedCards --logger "console;verbosity=detailed"
```

The [C# stdio regression test](../tests/FTPlus.AgentGraphicDesign.Mcp.Tests/ProtocolTests.cs),
`Roadmap02_ThreeWrappedCards_AlignDistributeAndLint_InFiveToolCalls_ViaStdio`, is
the complete executable recipe and assertion-backed client. It launches the actual
.NET server, reads the complete JSON inputs below, counts outgoing requests and retains
render artifacts. No Python script or external image-processing runtime is required.

For agent use, reproduce its five requests with these contracts:

1. `create_document`: name `mcp-card-recipe`, width 960, height 400, background
   `#0f172a`; retain the returned document ID.
2. `apply_patch`: three `addNode` operations, each with a 260x280 frame and ordered
   background rectangle, title and body children. Omit `parentId` to target root;
   the string `root` is not a valid patch GUID. Pass expected revision 1.
3. `align_nodes`: the three client-known frame IDs, mode `top`, relative to
   `selection`, expected revision 2.
4. `distribute_nodes`: the same IDs, horizontal axis, gap mode, expected revision 3.
5. `lint_document`: the document ID; require empty diagnostics and no truncation,
   not merely a successful protocol envelope.

## Complete Request Inputs

Use each tool name from the table with the arguments below. Replace `<DOCUMENT_ID>`
with `data.id` from call 1, not the root ID. These calls use current implemented
schemas, not the proposed full-editor protocol. No test-source reconstruction or
external scripting runtime is needed.

Call 1, `create_document`:

```json
{"name":"mcp-card-recipe","width":960,"height":400,"background":"#0f172a"}
```

Call 2, `apply_patch`, uses these arguments:

```json
{"idOrPath":"<DOCUMENT_ID>","expectedRevn":1,"operations":[]}
```

Replace the empty `operations` array above with the complete array below. The 0.3
protocol takes an actual JSON array; no second JSON-string encoding is needed.
All 12 node IDs are fixed before the request. Omitted frame orders and parents are
assigned by `addNode`. The C# test executes these exact documented payloads and
independently asserts their results, preventing the recipe from drifting from the tool contract.

```json
[
  {
    "op":"addNode",
    "node":{
      "type":"frame","id":"00000000-0000-0000-0000-000000000100","name":"card-1",
      "width":260,"height":280,"clipContent":false,"transform":{"x":32,"y":72},
      "children":[
        {
          "type":"rectangle","id":"00000000-0000-0000-0000-000000000101","order":"a0","name":"background-1",
          "width":260,"height":280,"style":{"fill":{"color":"#ffffff"}}
        },
        {
          "type":"text","id":"00000000-0000-0000-0000-000000000102","order":"a1","name":"title-1",
          "text":"Build a clear visual language","fontFamily":"Inter","fontSize":22,"fontWeight":700,
          "maxWidth":212,"lineHeight":1.25,"transform":{"x":24,"y":24},"style":{"fill":{"color":"#0f172a"}}
        },
        {
          "type":"text","id":"00000000-0000-0000-0000-000000000103","order":"a2","name":"body-1",
          "text":"Give every idea room to breathe with readable type, generous spacing, and a consistent rhythm across the page.",
          "fontFamily":"Inter","fontSize":16,"fontWeight":400,"maxWidth":212,"lineHeight":1.5,
          "transform":{"x":24,"y":112},"style":{"fill":{"color":"#0f172a"}}
        }
      ]
    }
  },
  {
    "op":"addNode",
    "node":{
      "type":"frame","id":"00000000-0000-0000-0000-000000000200","name":"card-2",
      "width":260,"height":280,"clipContent":false,"transform":{"x":330,"y":32},
      "children":[
        {
          "type":"rectangle","id":"00000000-0000-0000-0000-000000000201","order":"a0","name":"background-2",
          "width":260,"height":280,"style":{"fill":{"color":"#eef2ff"}}
        },
        {
          "type":"text","id":"00000000-0000-0000-0000-000000000202","order":"a1","name":"title-2",
          "text":"Keep every card on the same grid","fontFamily":"Inter","fontSize":22,"fontWeight":700,
          "maxWidth":212,"lineHeight":1.25,"transform":{"x":24,"y":24},"style":{"fill":{"color":"#0f172a"}}
        },
        {
          "type":"text","id":"00000000-0000-0000-0000-000000000203","order":"a2","name":"body-2",
          "text":"Align the cards by their visible bounds, then distribute the columns so the spaces between them stay equal.",
          "fontFamily":"Inter","fontSize":16,"fontWeight":400,"maxWidth":212,"lineHeight":1.5,
          "transform":{"x":24,"y":112},"style":{"fill":{"color":"#0f172a"}}
        }
      ]
    }
  },
  {
    "op":"addNode",
    "node":{
      "type":"frame","id":"00000000-0000-0000-0000-000000000300","name":"card-3",
      "width":260,"height":280,"clipContent":false,"transform":{"x":668,"y":96},
      "children":[
        {
          "type":"rectangle","id":"00000000-0000-0000-0000-000000000301","order":"a0","name":"background-3",
          "width":260,"height":280,"style":{"fill":{"color":"#ecfdf5"}}
        },
        {
          "type":"text","id":"00000000-0000-0000-0000-000000000302","order":"a1","name":"title-3",
          "text":"Ship assets with useful feedback","fontFamily":"Inter","fontSize":22,"fontWeight":700,
          "maxWidth":212,"lineHeight":1.25,"transform":{"x":24,"y":24},"style":{"fill":{"color":"#0f172a"}}
        },
        {
          "type":"text","id":"00000000-0000-0000-0000-000000000303","order":"a2","name":"body-3",
          "text":"Check contrast and canvas bounds before handing off the layout. Keep the source editable for the next revision.",
          "fontFamily":"Inter","fontSize":16,"fontWeight":400,"maxWidth":212,"lineHeight":1.5,
          "transform":{"x":24,"y":112},"style":{"fill":{"color":"#0f172a"}}
        }
      ]
    }
  }
]
```

Call 3, `align_nodes`:

```json
{
  "idOrPath":"<DOCUMENT_ID>","expectedRevn":2,"mode":"top","relativeTo":"selection",
  "nodeIds":["00000000-0000-0000-0000-000000000100","00000000-0000-0000-0000-000000000200","00000000-0000-0000-0000-000000000300"]
}
```

Call 4, `distribute_nodes`:

```json
{
  "idOrPath":"<DOCUMENT_ID>","expectedRevn":3,"axis":"horizontal","mode":"gap",
  "nodeIds":["00000000-0000-0000-0000-000000000100","00000000-0000-0000-0000-000000000200","00000000-0000-0000-0000-000000000300"]
}
```

Call 5, `lint_document`:

```json
{"idOrPath":"<DOCUMENT_ID>"}
```

The `nodeIds` values are JSON arrays of GUID strings in this protocol. The expected
final revision is 4, horizontal gap is 58, and lint has zero warning/info findings
without truncation. Change content through native agent calls and recheck fit/lint;
do not turn this recipe into a second authoring engine in a scripting language.

## Evidence and Limits

- Initial positions are `(32,72)`, `(330,32)`, `(668,96)`, with horizontal gaps
  38 and 78 px. The helpers must actually move nodes: final world bounds are
  `(32,32,260,280)`, `(350,32,260,280)`, `(668,32,260,280)`, with two 58 px gaps.
- Frames are unpainted containers. Opaque rectangle children paint the cards
  before the text. The dark canvas matches the text color, so omitting those
  backgrounds would not be a valid contrast solution. No gradients or translucent
  groups are used; this is not evidence for unsupported contrast analysis of those.
- Titles and bodies contain no explicit newlines. All six exceed their 212 px
  wrap width and export as multiple SVG `tspan` lines with preserved words and
  the specified line pitch. The test checks measured world bounds, containment,
  non-overlapping title/body blocks, and equal gaps, not just tool success flags.
- Inter is the bundled default family, selected explicitly at weights 700/400.
  The harness verifies bundled resolution, no substitution or estimated bounds,
  and PNG rendering with `SystemFonts=false`, `StrictFonts=true`. SVG retains
  `font-family="Inter"` but does not embed font bytes; viewing it elsewhere still
  requires the font. The PNG is the host-font-independent render artifact.
- After the fifth call, the test reads the saved document and uses the engine's
  bounds API, SVG adapter, and Skia renderer locally. These are **harness-only
  verification**, not agent authoring or a hidden sixth protocol call. It checks
  the saved source is unchanged and the outgoing tool count is still five.
- The test writes `cards.agd.json`, `cards.svg`, and `cards.png` under
  `tests/FTPlus.AgentGraphicDesign.Mcp.Tests/bin/Release/net10.0/artifacts/mcp-card-recipe/`
  and prints the absolute artifact path. Build outputs are ignored, not tracked.
  Successful decoding and sampled card/background pixels are automated render
  checks only. No human visual approval, accessibility audit, or consumer approval
  is claimed; default lint does not run the icon-grid or symbol-readiness profile.
