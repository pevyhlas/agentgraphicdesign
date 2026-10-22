# Agent Graphic Design — security

- **Trust boundaries**: file contents and MCP arguments are untrusted; every parser and decoder
  bounds input and work (Q-03). The local filesystem is trusted and quiescent during an
  operation; path checks and atomic replacement cannot stop a concurrent rewriting process.
- **MCP confinement**: `agd-mcp` resolves every path beneath its working directory and rejects
  parent traversal, outside-root paths and symbolic-link or reparse-point traversal (P4.I-01).
  The working directory should not be writable by mutually untrusted local users.
- **CLI**: unrestricted; it reads and writes caller-chosen paths and should run in an OS sandbox
  when paths are untrusted.
- **Network**: no listener; MCP uses stdio. Runtime never fetches URLs; raster output decodes only
  bounded embedded data and draws a placeholder for paths and URLs.
- **Identity and access**: none; there are no accounts, sessions or roles today.
- **Encryption**: none needed; AGD stores no secrets or credentials (C-10).
- **Personal data**: none processed; errors reduce absolute host paths to file names.
- **Claims**: results never claim admission, provenance, rights or qualification they did not
  perform (Q-02).
- **Future review**: an HTTP host, database, authentication, tenancy, a billing hook or a
  deployment manifest needs its own architecture and threat-model review (D-02).
