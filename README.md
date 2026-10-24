<div align="center">

# Agent Graphic Design

### Give your agents a graphics editor.

[![CI](https://github.com/pevyhlas/agentgraphicdesign/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/pevyhlas/agentgraphicdesign/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Status: experimental](https://img.shields.io/badge/status-experimental-orange.svg)](#status)

[Quickstart](#quickstart) · [MCP setup](#mcp-setup) · [Agent guide](docs/agent-guide.md) · [Reference](REFERENCE.md) · [Roadmap](docs/public-readiness.md)

</div>

Create, edit and export graphics through **MCP or the `agd` CLI**. AGD keeps vector documents
and layered pixel stills in reviewable project files, with one shared .NET engine behind both tools.

- **Vectors:** shapes, paths, text, layout, tokens and atomic edits.
- **Pixels:** layered RGBA8 stills, masks, region edits and PNG import.
- **Delivery:** SVG, PNG, JPEG and WebP; previews, contact sheets and asset drift checks.
- **Local first:** no account, database or product telemetry. Keep your sources in Git.

For 3D assets and renders, use [AgentBlender](https://github.com/pevyhlas/agentblender).

## Quickstart

### Requirements

Install **.NET SDK 10.0.113** ([SDK setup](REFERENCE.md#requirements)).

```sh
git clone https://github.com/pevyhlas/agentgraphicdesign.git
cd agentgraphicdesign
./scripts/install-local.sh       # installs agd and agd-mcp as global .NET tools

agd create my-design
agd add my-design.agd.json rectangle --width 100 --height 60 --fill "#336699"
agd validate my-design.agd.json
agd export my-design.agd.json --type svg --output output.svg
```

Add `~/.dotnet/tools` to `PATH` if needed. Follow the [agent guide](docs/agent-guide.md)
for the create → inspect → preview → export loop.

## MCP setup

Create a working directory for your designs, then give your MCP client a stdio entry:

```json
{
  "mcpServers": {
    "agd": {
      "command": "agd-mcp",
      "args": ["--working-directory", "/absolute/path/to/designs"]
    }
  }
}
```

Call `get_capabilities`, then read `agd://agent-guide`. MCP confines files to the chosen directory;
CLI has normal local-user access. See [client setup](REFERENCE.md#mcp-server) for PATH and SDK settings.

## Status

**Experimental; build from source.** Only .NET 10 Linux x64 is qualified. No public release
bundle yet; APIs and formats can change in 0.x. Animation, tiles, full raster editing and
complete artifact admission remain planned. See [roadmap](docs/public-readiness.md),
[surface parity](docs/CLI_MCP_PARITY.md) and [changelog](CHANGELOG.md).

## Contributing and license

[Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) · [Product](specs/product.md) · [MIT](LICENSE)
· [Third-party notices](THIRD-PARTY-NOTICES.txt). Fork-safe GitHub-hosted CI;
[Product-Kit 1.1.1](https://github.com/pevyhlas/product-kit) vendored, no submodule required.
