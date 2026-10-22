# Agent Graphic Design — Development Guide

<!-- prdkit:orientation-only -->

Agent Graphic Design targets a full agent-native graphics editor: vector, raster/
pixel, animated sprites and tile authoring. **CLI/MCP-first**, with a later UI using
the same headless contracts. Vector authoring and the native layered-still pixel subset
are implemented; animation, tiles, wider raster artwork editing and external-artifact
admission are planned. Lifecycle: **experimental**, stage Public preview (0.x; formats and APIs
may still change between minor versions; `specs/product/lifecycle.md`). Product intent is in
`specs/product.md` and `specs/product/` (L0) and `specs/architecture.md` and
`specs/architecture/` (L1, parts in `specs/parts/`); open changes are in `specs/changes/` and
open maintainer questions in `specs/inbox.md`. History questions go to `specs/archive.md`, read
on demand only.

**Namespace:** `FTPlus.AgentGraphicDesign`
**CLI command:** `agd`

## Install CLI Locally

```sh
# Install agd as a global .NET tool (from local build)
./scripts/install-local.sh

# Verify
agd --help

# Uninstall
./scripts/uninstall-local.sh
```

## Build, Test, Run

```sh
dotnet restore FTPlus.AgentGraphicDesign.slnx
dotnet build   FTPlus.AgentGraphicDesign.slnx --configuration Release
dotnet test    FTPlus.AgentGraphicDesign.slnx --configuration Release
dotnet list    FTPlus.AgentGraphicDesign.slnx package --vulnerable --include-transitive

# CLI via installed tool
agd create my-design
agd inspect my-design.agd.json
agd validate my-design.agd.json
agd export my-design.agd.json -o output.svg

# Or via dotnet run (without install)
dotnet run --project src/FTPlus.AgentGraphicDesign.Cli -- create my-design

# MCP server (stdio transport)
dotnet run --project src/FTPlus.AgentGraphicDesign.Mcp -- --working-directory /path/to/docs
```

## Architecture Principles

- **MIT license** — all dependencies must be MIT or MIT-compatible permissive. No GPL/LGPL/AGPL/MPL.
- **License clarification** - MIT-compatible permissive dependencies are allowed; AGD's own code stays MIT and each component retains its license/notices. Do not add copyleft exceptions for new runtime host tools. The proposed bubblewrap/systemd intake requirement was withdrawn, not approved for implementation.
- **Agentic-first, CLI/MCP primary** — agents are first-class clients; any future GUI reuses the same API.
- **Custom JSON scene-graph** — the existing vector format; new pixel/sprite/tile formats require separate architecture review, not reinterpretation of vector nodes.
- **Self-host always** — SaaS is optional later convenience; self-hosting is always supported.
- **.NET-first** — no Node.js runtime dependency in app/runtime code unless justified.
- **No Python** — production authoring, generation, validation, import/export and supported automation recipes use .NET/C#.
- **Requirements, not consumer code** — consumer projects are requirements evidence, not an implementation, schema, artwork or validation baseline to port into AGD.
- **Distinct authoring domains** — tiles and sprites share pixel/animation foundations but have different invariants; full-editor functionality is the goal, agents remain primary.
- **Blender via an external producer** — AGD admits artifacts exported by a separate Blender producer (AgentBlender); it never installs, runs, embeds or bundles Blender and adds no Python bridge (C-07 and D-09 in `specs/`).
- **Direct cutover** — the maintainer permits breaking API/format cutovers when needed. Do not add legacy migrations, compatibility aliases or parallel implementations by default; update callers and declare format/version changes explicitly.
- **Git-first project state** - authoritative sources, settings and chosen asset references belong in versioned project-managed files, with required payloads preserved. Users manage Git history; AGD does not require or automatically operate Git. Do not add a standalone selection database or generic receipt ledger as an editor prerequisite. Durable jobs/retries and semantic undo need a concrete workflow and separate qualification. See C-06 in [constraints](specs/product/constraints.md), D-08 in [decisions](specs/architecture/decisions.md#d-08-git-first-project-state) and the [Git workflow](docs/git-workflow.md).

## Development Rules

- Branch → PR → green CI → merge. Never push directly to `main`. Outside contributors open draft
  PRs and only the maintainer merges ([CONTRIBUTING.md](CONTRIBUTING.md)); report vulnerabilities
  privately ([SECURITY.md](SECURITY.md)).
- CI runs on GitHub-hosted runners only, with a read-only token, no secrets, no
  `pull_request_target`, and every action pinned by full commit SHA with a version comment
  (`RepoChecks workflows` enforces it).
- Dependency additions require license verification per [D-01](specs/decisions/D-01.md).
- Run `dotnet list package --vulnerable --include-transitive` before adding dependencies.
- Document format schema changes require architecture review.

## Project Layout

```
FTPlus.AgentGraphicDesign.slnx                Solution file (.NET 10 XML format)
Directory.Build.props                         Shared MSBuild properties
src/FTPlus.AgentGraphicDesign.Core/           Headless core engine
  ├─ IDocumentEngine.cs / DocumentEngine.cs   Single shared engine API + implementation (all surfaces call this)
  ├─ IDocumentStore.cs                        Storage abstraction
  ├─ DocumentIndex.cs                         id→node index with parent links
  ├─ Documents/                               Document model, Canvas
  │   └─ Nodes/                               Node hierarchy (Frame, Rectangle, Ellipse, Path, Text, Group)
  ├─ Export/                                  IExportAdapter, ExportOptions, ExportResult
  ├─ Import/                                  SVG import adapter
  ├─ Ordering/                                FractionalIndex (sibling ordering)
  ├─ PathOperations/                          Clipper2 boolean ops, stroke offset
  ├─ Persistence/                             FileDocumentStore
  ├─ Rendering/                               SkiaSharp rendering helpers
  ├─ Serialization/                           DocumentSerializer (deterministic JSON)
  ├─ Tokens/                                  Design tokens (DTCG), palettes
  └─ Validation/                              DocumentValidator
src/FTPlus.AgentGraphicDesign.Skia/           SkiaSharp rendering implementation
src/FTPlus.AgentGraphicDesign.Cli/            CLI surface (agd command)
src/FTPlus.AgentGraphicDesign.Mcp/            MCP server (stdio transport)
tests/                                        xUnit tests
examples/                                     Sample .agd.json documents
specs/                                        Product (L0), architecture (L1), parts (L2), changes, inbox, records, archive.md
docs/contracts/                               Normative v1 contracts for native pixels, still import and provided artifacts
```

## Project Phase

**Lifecycle: experimental (Public preview).** Stable `0.3.0` was published on 2026-10-01, before
this repository became public, qualified for .NET 10 Linux x64 only (see `specs/records.md`).
Native layered still pixels are delivered, not the full animation/tile editor; see
`specs/product.md` for what is true now and what is planned.
Vector format v1 changes must stay additive; an incompatible document-shape change needs a
new format version and architecture review.
