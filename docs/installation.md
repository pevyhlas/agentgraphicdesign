# Installing a released AGD

AGD releases are published as GitHub Release assets: the CLI and MCP .NET tool
packages, `SHA256SUMS` and `provenance.json`. There is no public package feed. A
source version or a green pull request is not a release.

> The 0.3.0 release was published before this repository became public, and its
> assets are not available from this repository. Until a release is published here,
> build and install from source (`./scripts/install-local.sh`); the verification steps
> below apply once a public release exists.

## Verify the release

Download both packages, `SHA256SUMS` and `provenance.json` from the release, then
record the approved version, source commit and both package hashes in your own
project-managed pin. Do not infer approval from a global executable or an unchecked
downloaded descriptor.

The supported authoring/build/check platform is .NET 10 Linux x64. Consumer
applications can target other platforms: committed SVG/PNG/JPEG assets do not require
AGD at runtime. Additional authoring platforms need their own qualification.

## Install into an isolated tool directory

From an AGD checkout at the release source revision, use the shared bootstrap with
trusted pin values, an existing parent directory and a fresh local tool directory:

```sh
bash /path/to/agd-checkout/scripts/restore-approved-tools.sh \
  /path/to/downloaded-bundle /path/to/local-tools/agd-0.3 \
  0.3.0 <SOURCE_COMMIT> <CLI_PACKAGE_SHA256> <MCP_PACKAGE_SHA256>
/path/to/local-tools/agd-0.3/cli/agd --version
/path/to/local-tools/agd-0.3/mcp/agd-mcp --version
```

The helper verifies both hashes, clears package sources/caches for installation,
checks informational versions, and preserves an existing installation by refusing
to overwrite its directory. A hash, source or version mismatch is a failing gate.
Runtime tool/cache paths are disposable; project asset intent and required payloads
remain versioned under each project's Git policy.

## Register the MCP server

Register the exact verified MCP executable with an explicit working directory:

```json
{
  "mcpServers": {
    "agd": {
      "command": "/path/to/local-tools/agd-0.3/mcp/agd-mcp",
      "args": ["--working-directory", "/path/to/project"]
    }
  }
}
```

That is the Claude-compatible shape; other harnesses use their own local-server
configuration schema. After registration, initialize, discover tools, call
`get_capabilities`, and read `agd://agent-guide`. Check version, root and domain
limits. The CLI is the fallback; manifest `assets build/check` and sprite batches are
intentionally CLI-only.

## External producers

A producer self-check or scene replay PASS is not AGD admission or visual approval.
An external producer must emit the exact AGD provided-artifact v1 descriptor and PNG
profile, verified by both `agd assets preflight` and decoded verification; producer
metadata and replay sources belong outside the strict payload directory. See
[provided artifacts](provided-artifacts.md).

## Completion evidence

Record the actual pin/install identity, source/output inventories, build/check
outcome, intentional framework-only wrapper changes, target-size visuals and
accessibility. A missing required tool, unsupported platform or unavailable release
is a blocked install, never a passing skipped gate.
