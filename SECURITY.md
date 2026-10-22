# Security policy

Agent Graphic Design (AGD) is experimental and has one maintainer. Security reports are welcome
and are handled on a best-effort basis.

## Supported versions

Only the latest release and the `main` branch receive security fixes. A fix ships on `main` and
in a new release; earlier versions are not patched. The latest release, 0.3.0, was published
before this repository became public, and its assets are not republished here yet, so a fix for
it lands on `main` and in the next release published here.

## Reporting a vulnerability

Report vulnerabilities privately. Do not open a public issue, discussion or pull request for one.

- **Preferred:** GitHub's private vulnerability reporting. Open the repository's **Security**
  tab and choose **Report a vulnerability**
  (<https://github.com/pevyhlas/agentgraphicdesign/security/advisories/new>).
- **Or email** agd@petrvyhlas.com.

Include the affected version or commit, the platform, what an attacker could do, how to
reproduce it (a minimal document, SVG, request or manifest helps), and a suggested fix if you
have one. Share only what the report needs: no real secrets or private project data.

For non-sensitive hardening ideas, a public issue or pull request is fine.

## What to expect

- An acknowledgement within 7 days.
- An assessment, and for a confirmed vulnerability a fix on `main` and in a new release as soon
  as the maintainer can make one. The advisory is published with or after the fix, and credits
  you unless you prefer otherwise.

This is a solo maintainer's best effort, not a guaranteed service level. If you have heard
nothing after 7 days, send a reminder through the other channel.

## Scope

In scope:

- The `agd` CLI and the `agd-mcp` MCP server, and the Core and Skia libraries behind them:
  parsing and processing of untrusted input (`.agd.json` documents, SVG import, native pixel
  assets and still-import requests, asset manifests, provided-artifact descriptors and PNG
  data, font files), resource limits, and file-system handling.
- The MCP server's working-directory boundary: path traversal, symbolic-link or reparse-point
  escapes, and anything that lets an MCP client read or write outside `--working-directory`.
- The release and packaging scripts (`scripts/release-bundle.sh`, `scripts/package-smoke.sh`,
  `scripts/install-local.sh`) and this repository's GitHub Actions workflows
  (`.github/workflows/`).
- Agent guidance shipped with the product (MCP resources, `docs/agent-guide.md`), for example an
  instruction that leads an agent to leak data or execute untrusted input.

Out of scope:

- Vulnerabilities in the .NET runtime, SkiaSharp, HarfBuzz, the MCP SDK, agent harnesses,
  models or other third-party components; report those to their maintainers. If AGD's use makes
  one exploitable, that part is in scope.
- The CLI reading or writing a path its caller chose. Unlike MCP, the CLI intentionally accepts
  any caller-selected path.
- Attacks that already need write access to the MCP working directory from another local
  process (check-to-use races), or a shell on the machine.
- Network exposure: the MCP transport is stdio only, with no network listener, authentication
  layer or multi-tenant isolation.

## Security considerations

- The CLI and MCP server process untrusted SVG and JSON. Input and output limits reduce resource
  risk but do not replace operating-system isolation.
- Only MCP enforces a path boundary. `agd-mcp --working-directory <PATH>` rejects traversal and
  symbolic-link/reparse-point paths beneath that root.
- Filesystem checks cannot eliminate check-to-use races against another process that can
  rewrite the MCP working tree. Do not share the working directory with mutually untrusted local
  writers.
- Font directories (`--font-dir`, `fontDirs`) are read non-recursively without following
  symbolic links, bounded to 16 directories, 256 font files per directory, and 8 MB per file.
  MCP confines each directory to its working directory; the CLI accepts any caller-selected
  directory.
- `scripts/check-dependencies.sh` verifies resolved graph approvals and available package
  license metadata; `scripts/check-vulnerabilities.sh` runs the structured NuGet advisory audit
  in CI.
