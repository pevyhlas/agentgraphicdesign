# J-05 Set up the tools (validated)

**Who**: Project maintainer · **Area**: Setup · **Surfaces**: CLI, MCP client configuration

## Intent

Install `agd` and `agd-mcp` and connect an agent to them, without a hosted service, an account
or another runtime.

## Situation

On a developer machine or a CI runner, possibly on a platform AGD has not qualified. The agent
has only what its MCP client or shell gives it.

## Scenario

1. They install both tools from a pinned release bundle, checking its checksums, or build and
   install them from source.
2. They register `agd-mcp` with the agent's MCP client, naming the working directory it may use.
3. The agent asks for capabilities and starts working.

## Variations

- **No release bundle to install**: they build and install from source.
- **A checksum mismatch**: installation stops.
- **An unqualified platform**: the capability report says the platform does not match the
  qualified profile, and the guarded operations (asset pipeline, artifact preflight, still
  import and native pixel storage) fail closed, except on Windows, which the shared file probe
  lets through unqualified (Q-05, P1.I-09); vector document editing still runs, unqualified
  (C-11, Q-05).
- **An MCP client rejects one tool's schema**: the client shows no AGD tools; today the
  TypeScript MCP SDK does so over `import_pixel_layer`, so the agent uses the CLI until change
  006 lands.

## Success

An agent is productive soon after installation, with no network service, account or Python.
