# P4 CLI and MCP server

**What it is**: two .NET global tools that load P1 in process: the `agd` command line and the
`agd-mcp` stdio MCP server · **Maturity**: validated · **Code**:
`src/FTPlus.AgentGraphicDesign.Cli/`, `src/FTPlus.AgentGraphicDesign.Mcp/`

## Why it exists

Agents and maintainers reach P1 only through these tools (J-01, J-03 to J-05, and the planned
J-06 to J-08). They parse
and bound arguments, call the shared service and map its result, owning no business logic
(R-01, C-04); they let an agent discover what this build can do and inspect large documents in
bounded steps (R-03, Q-03).

## Components

### Shared by both tools

- **P4.B-01 Registered surface matches the manifest**: every CLI command, MCP tool and MCP
  resource is listed in the surface manifest, and the manifest lists nothing unregistered.
  Test: `CliMcpParityTests`.
- **P4.B-06 Read-only checks signal through results**: drift and verification outcomes are
  completed results with a distinct exit code or error code, never a write. Test:
  `PixelsCommandTests`, `AssetsCommandTests`.
- **P4.I-02 No hidden state**: no command depends on state from an earlier call other than the
  files it names; there are no sessions or unsaved edits. Enforced by: owner review.

### agd CLI

Reads and writes caller-chosen paths without a sandbox
([security](../architecture/security.md)); ID-based lookups use the store under
the current directory, and validation, asset batches and path geometry create no store (P1.B-08,
P1.B-26). The asset pipeline is CLI-only batch work.

### agd-mcp server

Speaks MCP over stdio, with one working directory as both confinement root and store root.

- **P4.B-02 Discovery**: `get_capabilities` reports the executable version, root, platform
  profile, enabled domains, limits and CLI-only operations from the running build, not a
  static list. Its I/O-free `platform` member embeds Core's D-28 projection unchanged:
  architecture, capabilities, OS/version, profile, qualified profiles and runtime version,
  ordinal keys and enum/table array order. `supportedPlatform` and `currentPlatformMatches`
  are removed, without aliases; there is no top-level qualified boolean or live probe.
  S1 reports macOS26+ arm64 .NET10 pipeline/artifact admission as unqualified, never qualified.
  Test: `ProtocolTests` (including the three explicit platform methods).
- **P4.B-03 Small previews**: vector previews default to a thumbnail no larger than 512 px
  without upscaling, explicit sizes are bounded, and every inline image is a valid base64 PNG.
  Test: `ProtocolTests`.
- **P4.B-04 Raw argument capture**: native pixel tool arguments are validated from the
  original UTF-8 request bytes before typed binding, so duplicate members, exact numeric
  spellings and the request size bound are enforced as sent; every message is bounded at
  ingress. Test: `RawCaptureIngressTests`.
- **P4.B-05 Served references match the repository**: the schema, format reference and agent
  guide resources are the embedded repository files. Test: `ProtocolTests`, `DocumentResourcesTests`.
- **P4.B-07 Object tool schemas**: every tool input schema, and every nested property, item and
  composition schema in it, is a JSON object, never a boolean schema (a boolean
  `additionalProperties` is allowed), because TypeScript MCP SDK clients reject a boolean
  subschema and drop the whole tool list. Test:
  `ProtocolTests.ToolsList_AllInputSchemas_HaveNoBooleanSubschemas`.
- **P4.I-01 MCP confinement**: every MCP path resolves beneath the working directory; parent
  traversal, outside-root paths and symbolic-link or reparse-point traversal are rejected.
  D-28's verified system aliases use their canonical spelling for roots, arguments and reported
  `workingDirectory`; other links, including ancestors above the root, stay refused. CLI display
  paths use canonical operands too. Enforced by: `PathConfinementTests`, `CanonicalConfinementTests`,
  `ProtocolTests.WorkingDirectory_ConfinesPathsCorrectly`, `CanonicalPathCommandTests`.

## Interface guarantees

- Sub-node vector and raster exports report Core bounds notes in their existing `diagnostics`
  arrays; CLI human output also prints them. Render previews retain combined bounds/render notes.
  No new command or tool; SVG font-option restrictions are unchanged.
  Tests: `ExportCommandSubNodeTests`, `ExportSubNodeToolTests`.
- Raw RGBA buffers are internal to rendering. `export` and `export_document` reject raw
  formats with `EXPORT_UNSUPPORTED`; CLI contact sheets reject non-PNG output extensions with
  `INVALID_ARGUMENTS`; MCP previews and sheets always return PNG. Rejections write no output.
  Tests: `RawRgbaRefusalCommandTests`, `RawRgbaRefusalToolsTests`.
- Command, tool and resource names: changed only by a declared cutover recorded in the
  changelog, with the manifest updated in the same change. Schema: `docs/surface-manifest.json`.
- CLI and MCP share engine operations but not argument, output, persistence or concurrency
  shapes; behavioral parity is not promised beyond the documented parity table
  (`docs/CLI_MCP_PARITY.md`).

## Failure semantics

- Failures return the shared structured error envelope (code, message, optional location);
  busy guards (P1.B-14, P1.B-25) surface as their own codes, never as a queue.
- A CLI command that fails while it is constructed (a store conflict, denied access, an I/O
  error) reports that failure's own code with exit 1, never a type-resolution error.

## Left open

- The layout of human-readable CLI output; the JSON output form is the machine contract.
- Whether the other file-path commands that never persist (`inspect`, `export`, `list`,
  `bounds`, `lint`, `sprite`, `import`) move to the store-free engine, or store resolution
  becomes lazy; today they fail with `STORE_CONFLICT` inside a store tree. Either needs its own
  architecture review.
