# Agent Graphic Design — constraints

<!-- C- items: what must hold. Who may act on a limit is the constitution's Action policy. Past
the budget, split into constraints/<area>.md and keep one line and a link per area here. -->

- **C-01 Permissive licensing**: AGD code is MIT; every dependency is MIT or MIT-compatible
  permissive (no GPL, LGPL, AGPL or MPL) and license-verified before adoption (D-01).
  - Why: users must be free to self-host and embed AGD.
  - Scope: all (runtime, build and test dependency graphs).
  - Source: owner (constitution); owner clarification: permissive, not MIT-only, and the
    proposed copyleft host-utility exception was withdrawn.
  - Revisit when: never by an agent.
  - Exception: the owner, through the inbox.
- **C-02 Self-host always**: every feature runs self-hosted; any later hosted mode is optional
  and keeps feature parity.
  - Why: no lock-in.
  - Scope: all distributions.
  - Source: owner (constitution); D-02.
  - Revisit when: never by an agent.
  - Exception: the owner, through the inbox.
- **C-03 .NET-first, no Python**: production authoring, generation, validation, import and
  export, and supported recipes are .NET/C#; no Python or Node.js runtime or subprocess
  dependency.
  - Why: one reviewable, self-hostable toolchain; a script is no substitute for a native agent
    operation.
  - Scope: all shipped tools and supported recipes (P1, P4).
  - Source: owner direction (full-editor direction: no Python).
  - Revisit when: the owner approves a justified exception.
  - Exception: the owner, through the inbox.
- **C-04 Agents first**: CLI and MCP are the primary surfaces; a GUI, if built, holds no
  business logic.
  - Why: agents are the primary users.
  - Scope: P4 and every future client; J-01, J-04, J-05.
  - Source: owner direction (agents first, UI later).
  - Revisit when: never by an agent.
  - Exception: the owner, through the inbox.
- **C-05 No live collaboration**: no CRDT or OT, presence or live sync; a future multi-user
  mode uses leases plus revision checks.
  - Why: the workflow is solo authors, agents and pull-request review.
  - Scope: all modes.
  - Source: D-02.
  - Revisit when: the owner reopens D-02.
  - Exception: the owner, through the inbox.
- **C-06 Git-first project state**: authoritative sources, settings and chosen asset
  references live in versioned project files with required payloads preserved; AGD does not
  require or operate Git and adds no standalone selection database or generic receipt ledger.
  - Why: user-managed history restores the whole project.
  - Scope: P1 and all project state; J-02, J-03.
  - Source: owner direction (D-08).
  - Revisit when: a workflow needs durable replay or semantic undo.
  - Exception: the owner, with a reviewed workflow contract.
- **C-07 No Blender execution in AGD**: Blender interoperability consumes artifacts exported
  by a separate producer; AGD never launches, embeds, bundles or installs Blender and has no
  Python bridge.
  - Why: keeps GPL execution and Python outside AGD.
  - Scope: all AGD code (P1, P4); J-04, J-06.
  - Source: owner direction (D-09).
  - Revisit when: a fresh, separate owner decision.
  - Exception: the owner, through the inbox.
- **C-08 Consumers are evidence, not code**: consumer projects supply requirements and failure
  examples, never implementations, artwork, constants or validation baselines to port.
  - Why: their checks are unverified.
  - Scope: all.
  - Source: owner direction (full-editor direction).
  - Revisit when: never by an agent.
  - Exception: the owner, through the inbox.
- **C-09 Explicit format evolution**: vector format 1 changes stay additive; an incompatible
  change needs a new format version and architecture review. Pixel, sprite and tile semantics
  get their own reviewed formats, never vector nodes. Declared direct API and format cutovers
  are allowed, without legacy migrations or compatibility aliases by default.
  - Why: stable agent contracts, no dead compatibility code.
  - Scope: all formats and public APIs (P1, P4); J-01, J-04.
  - Source: owner (constitution); owner direction (direct cutovers allowed).
  - Revisit when: a format leaves 0.x.
  - Exception: the owner, through architecture review.
- **C-10 No secrets**: no secrets in the repository, logs or docs; raw machine output stays
  out of the repository.
  - Why: the repository is public.
  - Scope: all (everything committed).
  - Source: owner (constitution).
  - Revisit when: never.
  - Exception: none.
- **C-11 Owner-gated platforms**: qualifying a platform profile beyond .NET 10 Linux x64 is the
  owner's decision, made per profile with recorded qualification evidence. The guarded
  operations are the asset pipeline, artifact preflight, still import and native pixel storage
  (P1.I-09); unqualified profiles fail closed except for the owner-approved S1 scope below. The existing deviation remains
  an owner inbox question: D-28 S0 retains unqualified Windows x64 .NET 10 pipeline/artifact
  admission only; all Windows native operations now refuse.
  Planned S1 (change 029): `assetPipeline` and `artifactVerify` may run as **unqualified** only on
  .NET 10 Apple-silicon macOS 26+, after native regular/special-file safety proof, complete two-mode
  non-regression and all existing review/CI gates. This permission does not qualify that profile
  or authorize native pixels, still import, S2/S3 admission, release or deployment.
  Vector document reads and writes (P1.I-02) and raster export are not guarded: they run on
  every profile, unqualified off .NET 10 Linux x64 (also an inbox question).
  - Why: file safety rests on OS primitives verified per profile; an unverified profile could
    lose or corrupt sources while claiming success.
  - Scope: P1, P4; J-03, J-04, J-05.
  - Source: owner direction (platform support is maintainer-gated; other platforms fail
    closed); owner, 2026-10-28: "Allow S1 (Recommended)" in response to the bounded macOS asset
    access question recorded for [PR #22](https://github.com/pevyhlas/agentgraphicdesign/pull/22).
  - Revisit when: a profile has recorded qualification evidence (change 008 for Apple silicon
    macOS).
  - Exception: the owner, through the inbox.
