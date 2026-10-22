# Agent Graphic Design — decisions

<!-- D- decisions, technology choices included, a `## D-NN <title>` section each. Past the budget,
split into decisions/<area>.md; one long decision gets specs/decisions/D-NN.md. Status words:
accepted, or proposed while an owner or review verdict is pending (see the inbox). -->

[D-01 Dependency policy and exact approvals](../decisions/D-01.md) (accepted): every package is
MIT-compatible permissive, approved by exact id and version, pinned, audited and noticed.

## D-02 Deployment model

**Decided by**: owner · **Date**: 0.1 line · **Cited by**: C-02, C-05, N-01, N-02
- **Decision** (accepted): local tools now. Later self-hosted HTTP with PostgreSQL, multi-user
  and an optional SaaS mode come from one codebase with full feature parity. All product
  function is MIT; only SaaS operations (billing, metering, analytics, multi-tenant routing,
  support) may be proprietary, behind an optional billing hook with a no-op default (none exists
  today). A SaaS tenant gets its own database. No live collaboration: multi-user mode uses leases
  and `revn` checks, an HTTP 409 on a stale write once a host exists, and manual refresh.
- **Alternatives**: open-core feature gating (self-hosters would lose parity); a shared-schema
  multi-tenant database (large, variable documents; per-tenant isolation is simpler to secure
  and back up); CRDT or OT live editing (N-01).
- **Revisit when**: the owner names a concrete deployment need; until then multi-user mode,
  passkeys, Nostr sign-in, an HTTP host, PostgreSQL and a web GUI stay deferred.

## D-03 Token format

**Decided by**: architecture review · **Date**: 0.1 line · **Cited by**: R-08
- **Decision** (accepted): simple flat internal tokens referenced by `tokenRef` (a bare name or
  `{name}`); DTCG (`$value`, `$type`) only for import and export, without keeping metadata such
  as `$description`; geometry flat on nodes.
- **Alternatives**: full DTCG everywhere (nested `$` keys are harder for agents to edit, and
  metadata rarely survives edits); simple tokens with no interchange (no interoperability with
  token tools).
- **Revisit when**: an interchange need requires round-tripping DTCG metadata.

## D-04 Full-editor domains

**Decided by**: owner (direction); architecture review per format · **Date**: 0.2 line · **Cited by**: R-09 to R-12, C-09
- **Decision** (proposed architecture over owner direction): Vector, Pixels, Raster artwork,
  Animation, Sprites and Tiles are peer domains over shared project, evidence and output
  services. Pixels and Animation are foundations; Sprites and Tiles are peers (tiles are not a
  sprite-sheet mode, vector frames are not time). Identity and sharing, exact time, pixel and
  compositing semantics, typed references and native write contracts are settled before each
  format freezes. Tiles get separate intent, coverage, pixel, composite and temporal validation.
  Every edit is a typed headless contract.
- **Alternatives**: port a consumer project's tile tooling; a pixel profile on vector export;
  every pixel a vector rectangle; one engine interface for every domain; a GUI first, automated
  by clicks; static sprites as the whole pixel scope; proving tiles by matching labels or a few
  screenshots. Each was rejected for the wrong model, coupled invariants or unverified checks.
- **Revisit when**: each domain's format review, in change 014.

## D-05 Optional external artifact producers

**Decided by**: owner (objective) · **Date**: 0.2 line · **Cited by**: R-07, R-11
- **Decision** (proposed): provided-artifact intake (descriptor metadata, preflight, decoded
  verification and still import) comes before any producer execution, which D-09 governs.
- **Alternatives**: an AGD-hosted prepared-scene Blender worker (retired by D-09); a general
  `bpy` bridge or embedding (C-03, C-07).
- **Revisit when**: D-09 is settled.

## D-06 Provided-artifact metadata v1

**Decided by**: architecture review · **Date**: 0.2 line · **Cited by**: R-07
- **Decision** (accepted): a pure, strict descriptor parser, validator, canonical serializer and
  domain-separated metadata digest for one closed PNG still or sequence profile; no file reads,
  decoding or admission. Contract: [provided-artifact v1](../../docs/contracts/provided-artifact-v1.md).
- **Alternatives**: extending the vector format or asset manifest v1 (would change shipped
  formats); admitting files first (unsafe before decoding and containment exist).
- **Revisit when**: admission (R-11) needs more than metadata.

## D-07 Typed in-process producers

**Decided by**: architecture review, with the owner's direct-cutover direction · **Date**: 0.2 line · **Cited by**: R-05
- **Decision** (accepted): explicit engine and producer construction with a generic typed
  request, per-run budgets charged before work, and output owned by the host. Contract:
  [in-process producers](../../docs/contracts/in-process-producers-v1.md).
- **Alternatives**: a fixed vector request (couples future domains to vectors); eager batch
  preparation (changes load and error order); producer-owned publication (loses confinement and
  limits); a serialization clone (rounds numeric inputs); post-work cost reporting (too late).
- **Revisit when**: a non-vector producer or external execution is designed.

## D-08 Git-first project state

**Decided by**: owner · **Date**: 0.2 line · **Cited by**: C-06
- **Decision** (accepted): versioned project-managed files hold sources, settings and chosen
  asset references, with required payloads preserved; users manage Git; AGD needs no Git and
  automates none. Previews, caches and locks are local runtime data that never hold sole intent.
  Git is not semantic undo, deduplication, restartable jobs or atomic multi-file publication.
- **Alternatives**: an SQLite commit, receipt and selection store (built, unreleased, withdrawn);
  Git as the transaction log (restores revisions but is not identity, undo or deduplication).
- **Revisit when**: a workflow needs durable replay, semantic undo or coherent publication.

## D-09 Blender via an external producer

**Decided by**: owner (direction) · **Date**: 0.2 line · **Cited by**: C-07, R-11
- **Decision** (recorded as proposed; inbox): a separate producer project,
  AgentBlender, runs headless Blender and owns its
  GPL boundary; AGD admits its PNG stills, sidecars and reproducibility evidence as data and
  never launches Blender or links `bpy`. The admission and provenance contract is
  co-designed once the producer's export contract exists.
- **Alternatives**: an AGD-hosted Blender worker (needs a fresh owner decision on GPL tool use,
  isolation and distribution); a general `bpy` bridge, embedding or bundling (C-03, C-07).
- **Revisit when**: the owner makes a fresh decision on hosted execution.

## D-10 Bounded sub-node export

**Decided by**: architecture review · **Date**: 0.3.0 · **Cited by**: R-02
- **Decision** (implemented in 0.3.0; recorded as proposed, inbox): export one node's subtree in
  isolation, cropped to its own bounds; ancestor transforms and clipping are not applied; a node
  with no visible bounds fails with `NODE_NO_BOUNDS`; exclusive with symbol and explicit size
  options; no format, adapter or renderer change.
- **Alternatives**: keeping ancestor context or clipping, cropping to the canvas, node-scoped
  symbols or multi-node selection: each deferred to its own contract.
- **Revisit when**: a use case needs ancestor context or node-scoped symbols.

## D-11 Bounded SVG reference import

**Decided by**: architecture review · **Date**: 0.3.0 · **Cited by**: R-08
- **Decision** (implemented in 0.3.0; recorded as proposed, inbox): expand local `<use>` of a
  `<symbol>`, `<svg>` or element, bounded in depth and count, and map an axis-aligned rectangular
  `clip-path` to a clipping frame, using only existing node types; other clips, masks, filters,
  patterns, markers and external references keep their warnings.
- **Alternatives**: persisted instances sharing a master (a format change, under R-13).
- **Revisit when**: non-rectangular clips or masks are needed.

## D-12 Custom JSON scene graph

**Decided by**: research validation and the owner · **Date**: 0.1 line · **Cited by**: R-02, R-04, C-09
- **Decision** (accepted): a custom JSON scene graph is the canonical vector format, with SVG and
  PNG as import and export targets; pixel, sprite and tile sources do not inherit vector nodes.
- **Alternatives**: SVG as the native format (path mini-language and indirection are hard for
  agents, re-serialization makes noisy diffs, editor semantics capped); a zipped JSON bundle (not
  diffable).
- **Revisit when**: never for format 1; a new format version needs architecture review.

## D-13 SkiaSharp and HarfBuzzSharp

**Decided by**: research validation · **Date**: 0.1 line · **Cited by**: R-02, R-06
- **Decision** (accepted): SkiaSharp and HarfBuzzSharp (MIT) for rendering and text shaping,
  with their native notices retained.
- **Alternatives**: System.Drawing (Windows-only at runtime on modern .NET).
- **Revisit when**: a rendering need SkiaSharp cannot meet.

## D-14 Clipper2

**Decided by**: research validation · **Date**: 0.1 line · **Cited by**: R-08, D-21
- **Decision** (accepted): Clipper2 (BSL-1.0, pure C#) for boolean and offset path operations;
  its triangulation module is not used. When change 011 lands, Clipper2 serves booleans only and
  offsets move to the AGD stroker (D-21).
- **Alternatives**: in-house polygon clipping.
- **Revisit when**: a curve-native boolean engine becomes qualifiable (D-21).

## D-15 Spectre.Console.Cli

**Decided by**: research validation · **Date**: 0.1 line · **Cited by**: R-01
- **Decision** (accepted): Spectre.Console.Cli for the `agd` command line.
- **Alternatives**: hand-rolled argument parsing.
- **Revisit when**: the CLI needs what the library cannot express.

## D-16 PostgreSQL for multi-user persistence

**Decided by**: owner · **Date**: 0.1 line · **Cited by**: D-02
- **Decision** (accepted): PostgreSQL if multi-user persistence is built; no adapter today;
  adopting Npgsql needs its own dependency review.
- **Alternatives**: Cosmos DB (only with owner approval).
- **Revisit when**: a multi-user mode is selected.

## D-17 Fido2NetLib for passkeys

**Decided by**: research validation · **Date**: 0.1 line · **Cited by**: D-02
- **Decision** (accepted): Fido2NetLib (MIT) is the target if full passkey support is built; it
  is not an approval to add it now.
- **Alternatives**: ASP.NET Core Identity's built-in passkeys alone (enough for simpler flows;
  the two interoperate).
- **Revisit when**: authentication is designed.

## D-18 MCP SDK placement

**Decided by**: dependency review · **Date**: 0.1 line · **Cited by**: R-01, D-01
- **Decision** (accepted): the ModelContextProtocol SDK (Apache-2.0) is referenced only by the
  MCP project, with its exact version pinned centrally and its notice retained.
- **Alternatives**: a separate MCP abstraction layer (no second host exists).
- **Revisit when**: a second MCP host or a breaking SDK line arrives.

## D-19 Original managed PNG and DEFLATE decoder

**Decided by**: architecture review · **Date**: 0.2.0-preview.2 · **Cited by**: R-06, R-07, Q-03, C-01
- **Decision** (accepted): an in-repository, bounded managed RFC 1950/1951 and PNG decoder for
  strict provided-artifact verification and native still import; codecs only encode, and decode
  exact RGBA8, never define sample semantics.
- **Alternatives**: Skia or native zlib inflaters and SharpZipLib (they accept invalid streams,
  their semantics are codec-defined and their work cannot be bounded); SixLabors.ImageSharp
  (split commercial terms, D-01).
- **Revisit when**: a new image format or profile needs decoding.

## D-20 In-repository SVG importer

**Decided by**: research validation · **Date**: 0.1 line · **Cited by**: R-08, D-11
- **Decision** (accepted): SVG import parses with the in-box .NET XML APIs and maps elements to
  existing node types itself.
- **Alternatives**: Svg.Skia (not needed: a dependency for parsing the in-box XML APIs already
  cover).
- **Revisit when**: import needs SVG semantics the in-box parser cannot reach.

## Capability and onboarding decisions

Accepted in the design reviews of changes 007 to 012 (pre-public history); none is built yet.
Each file holds the binding contract its implementing change cites.

[D-21 Curve-preserving geometry with a measured, reported tolerance](../decisions/D-21.md)
(accepted; the glyph platform class waits on the owner): Clipper2 for booleans only, normative
sliver prevention, a libm-free stroker, magnitude bounds and path transform, fit, split and
outlines; change 011.

[D-22 Stroke outlines and knockouts as baked derivations in format 1](../decisions/D-22.md)
(accepted): an additive `path.derivation` with pure staleness hashes; slice S07 of change 013.

[D-23 Asset families: optical masters, variant axes and baked placements](../decisions/D-23.md)
(accepted): a family manifest and an additive `group.placement` with drift detection; slice S08.

[D-24 Asset manifest v2 and platform target adapters](../decisions/D-24.md) (accepted): new
spellings only, a shared transient-document builder and closed pure target adapters; slice S06.

[D-25 Versioned, immutable, producer-stamped asset kits](../decisions/D-25.md) (accepted):
no-replace publication of write-once versions with pin-based consumer trust; slice S09.

[D-26 Review sheets, static gallery and legibility analysis](../decisions/D-26.md) (accepted):
a condition matrix, raw RGBA renders, Core analysis with a counted budget and a server-free
gallery; change 012.

[D-27 Cross-cutting rules for the capability design](../decisions/D-27.md) (accepted): format
versions, references, severities, determinism classes, limits, security controls, parity,
verification layers and exit codes for D-21 to D-26.

[D-28 Platform registry and per-capability qualification](../decisions/D-28.md) (accepted): a
three-valued registry, Darwin file backends, one canonical-path entry point and hash-bound
qualification evidence; change 008.

[D-29 Agent onboarding: truthful identity, discovery and narrow client setup](../decisions/D-29.md)
(accepted, owner decisions): producer stamps, a task catalog, executable recipes, doctor, a
broad-root refusal and project-only client install; change 009.
