# Agent Graphic Design — interfaces

| Interface | Owner part | Consumers | Promise | Schema in code |
|-----------|------------|-----------|---------|----------------|
| Vector document format 1 | P1 | agents, consumers | Additive within format 1; newer versions warn on validate and are refused for mutation (C-09) | `schemas/agd-document.schema.json`; reference `docs/format.md` |
| Native pixel source format v1 | P1 | agents, consumers | Versioned; payload layout and evaluation id change only with a new version | `src/FTPlus.AgentGraphicDesign.Core/Pixels/`; contract `docs/contracts/native-pixels-v1.md` |
| Provided-artifact descriptor v1 | P1 | external producers | Strict, versioned metadata; unknown or duplicate members are errors | `schemas/agd-provided-artifact-v1.schema.json` |
| Asset manifest version 1 | P1 | consumer projects | Closed members; another version is refused; build writes only declared outputs, check never writes | `src/FTPlus.AgentGraphicDesign.Core/Assets/AssetManifest.cs`; guide `docs/asset-pipeline.md` |
| `IDocumentEngine` and Core services | P1 | P4, tests | In-repository API, no standalone package; a direct cutover updates every caller | `src/FTPlus.AgentGraphicDesign.Core/IDocumentEngine.cs` |
| CLI commands, MCP tools and resources | P4 | agents, scripts | Registered names match the tested manifest; a breaking wire change is a declared cutover in the changelog | `docs/surface-manifest.json` |
| Release bundle | operations | installers, consumers | Two tool packages plus `SHA256SUMS` and `provenance.json` from one source commit | `scripts/release-bundle.sh` |

| External system | Used for | By | When it fails |
|-----------------|----------|----|---------------|
| Project filesystem | every source, output, lock and cache | P1 | operations fail closed with structured errors; nothing partial is published |
| Git, run by the user | project history, review and restore | users | AGD does not depend on it; after a restore, sources are reloaded and validated |
| MCP clients | starting `agd-mcp` over stdio and calling tools | agents | a client that rejects one tool schema hides every tool; every tool schema is therefore a JSON object with no boolean subschema, which the TypeScript MCP SDK accepts (P4.B-07, J-05) |
| GitHub Releases | distributing release bundles | users installing | install from source instead; checksums guard every download |
| The external Blender producer (AgentBlender) | PNG stills, sidecars and reproducibility evidence | P1 preflight, verify and import | its outputs are untrusted input; checks report findings, never admission |
| NuGet | restoring pinned packages at build time only | CI, contributors | locked restore fails the build; runtime never contacts it |
