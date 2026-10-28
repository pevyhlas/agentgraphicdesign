# Change: agent onboarding

**Opened**: 2026-10-19 · **Parts**: P1, P4 · **Journeys**: J-05, J-01 · **Tier**: T3 · **Parent**: 013-brand-workflow-uptake

## Intent

In consumer use, getting AGD running cost an agent far more time than the work itself: it
hand-wrote client configuration and a smoke test, missed the path commands and mislabelled an
unreleased build as a release. After this change the agent finds commands by task in its MCP
context or `agd capabilities --find`, follows recipes the test suite executes, gets a truthful
producer label, runs `agd doctor` for one structured environment report, and runs
`agd mcp install` to register the server safely. Slice S03 of change 013; depends on 006 and 008.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | D-29 | added: the owner's onboarding decisions and the contracts below | accepted |
| L2 | P4 | new behaviors: capabilities by task, guide sections, server instructions, doctor, `diagnose_environment`, MCP config and install, broad-root startup refusal | |
| L2 | P4.B-02 | report v2: `cliOnly` removed; `report`, `producer`, `runtime`, `tasks`, `filter`, catalog `tools` added; the D-28 platform member embedded unchanged | declared cutover |
| L2 | P1 | a shared store layout inspector; the producer record | |
| L1 | interfaces | surface manifest v2; MCP clients row for install | declared cutover |

## Bounds

- C-02, C-10: hold: no network, no secrets; install writes the project scope only; outputs show
  only the named entry and never `env` values.
- Q-02: holds: the producer label never claims a release for a source build; only a consumer's
  pin proves bundle or publication.
- P4.I-01: holds: the broad-root refusal narrows confinement roots and changes no path.
- C-03: holds: probes, installer and tests are .NET; recipes never run a shell.
- Others in scope: hold.

## Approach

The owner accepted every recommendation, recorded with their trade-offs in D-29: 009/D-1 narrow
client setup in AGD; 009/D-2 never edit client-owned state; 009/D-3 absolute, verified paths;
009/D-4 `bundle|source` stamps with a dirty flag and label; 009/D-5 two executables with a full
producer comparison; 009/D-6 no skills in consumer repositories; 009/D-7 the startup broad-root
refusal with `--allow-broad-root` only at the launch site. The lead chose 009/X-1 option (b):
refuse link components on the canonical path, never resolve them. Two phases, one change: both
serve the one slice criterion and share a review context; each PR group is reviewed, merged and
package-qualified on its own. Phase A text names only phase A surfaces.

External prerequisites, no fallback or stub: 009/E1 change 006 merged; 009/E2 008/T001 merged
(registry, gates, the probe interface and vocabulary) before any task; 009/E3 008/T006 before
phase B; 009/E4 008/T012 before B3; 009/E5 008/T019 before B5. From 008/T018 on, PR groups that
touch platform-sensitive paths carry a zero-failure qualification record (D-28).
009/E3's canonical-path implementation is in child028 ([PR #21](https://github.com/pevyhlas/agentgraphicdesign/pull/21));
consume it after that PR's final exact-head gates and source merge. E4/E5 and platform qualification
remain separate prerequisites; this does not complete phase B.

## Tasks

- [ ] A0 identity and bundle stamping (default first PR): 009/T002 producer v1; T003 identity
  tests; T004 bundle stamp in `release-bundle.sh` and the package check.
- [ ] A discovery and identity: T001 spikes (help footer, client surfacing of instructions); T049
  no-Git export build spike; T005 `install-local.sh`; T006 its harness; T007 embedded references;
  T008 surface manifest v2; T009 description terms; T010 catalog coverage; T011 report v2 builder;
  T012 `agd capabilities`; T013 parity and embedding tests; T014 guide content; T015 `agd guide`;
  T016 server instructions; T017 instruction wire test; T018 recipe harness; T019 guide and
  doc-lint tests; T020 docs and delta; T043 package qualification; T022 phase A verification.
- [ ] B1 store inspector: T024 the legacy-check oracle copy (after 008/T006); T025 the inspector;
  T026 differential and no-write tests.
- [ ] B2 broad-root rule and startup refusal: T027 `BroadRootRule`; T028 the startup refusal;
  T029 its tests; T044 the breaking-change declaration and package filters.
- [ ] B3 doctor: T023 spikes; T030 the stdio probe components; T031 probe tests; T032 doctor and
  the client-file reader; T033 doctor tests; T034 `diagnose_environment`; T035 its wire tests;
  T039 error hints; T045 docs and filters (the reader and `client.*` checks may split off).
- [ ] B4 client setup: T036 adapters, project root and `mcp config`; T037 splicer and
  `mcp install`; T038 their tests; T040 docs; T041 package filters.
- [ ] B5 `platform.support`: T046 the check over the D-28 probe; T047 its tests; T048 docs.
- [ ] Close: T042 acceptance and a real-client run with OpenCode and Claude Code on Apple silicon
  (not qualified until 008/T018).

Design closure checks for implementation review: 009/RC-1 skip the `identity.mcp` pre-check when
the root gate refuses the root (T032, T033); RC-2 project-relative remedies resolve against the
cwd, so emit cwd-relative or absolute paths, with a cwd ≠ project fixture (T032, T033, T037,
T038); RC-3 compute the root gate before any root-dependent check, also under `--check` filters
(T032, T033, T046); RC-4 the one remedy argv taken from a client file is lexically normalised,
refuses remaining `..` and passes the reject list (T032, T033); RC-5 implement the full bidi set
(T032, T033); RC-6 build the link-component fixture with an explicit linked spelling or the
`lstat` seam (T033); RC-7 hash only regular ignored files, compare nested repositories by name
(T006); RC-8 guarded `rm -rf` and no stdin hashing (T005, T006); RC-9 a free-space floor and
recorded peak disk (T006); RC-10 cite change 008's final design revision (lead, before T001).

## Checks

Install writes the project scope only and needs no network (C-02, C-10). Each new P4 behavior gets
a test; report v2 and manifest v2 are declared cutovers (C-09). An entry is never written unless it
passed a probe in exactly its written form.

## Acceptance

Design acceptance criteria 009/AC-01 to 009/AC-31 group as below. Only the group labels are public:
each criterion's full text, and each task's acceptance text, stay in the pre-public history. D-29
holds the binding contract; the pull request that ports or implements an increment restates, in its
body and in this record, the criteria and tasks it closes.

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | Q-02 | AC-01 to AC-03: producer stamps on packed and installed tools; `install-local.sh` harness invariants | 0 | NOT_RUN |
| A2 | P4.B-02 | AC-04 to AC-12: parity of report v2 on both installed executables, catalog coverage, instructions, guide bytes, executed recipes, doc-lint, platform embedding | 0 | NOT_RUN |
| A3 | P4 | AC-13 to AC-22: inspector differential, doctor exit codes and no-execution rules, root classification, probe lifecycle, read-only `diagnose_environment` | 0 | NOT_RUN |
| A4 | J-05 | AC-23 to AC-29: config goldens, install safety, byte splice, secret sentinels; an agent installs, verifies and diagnoses with one command | 0 | NOT_RUN |
| A5 | P4.I-01 | AC-30, AC-31: the startup broad-root refusal and its doctor warning | 0 | NOT_RUN |

## State

**Step**: tasks
**Pre-public review**: design approved at closure pass 1 (architecture, security, operations) in
the pre-public history, whose review reports and criterion and task text are not public; no
implementation yet
**Waiting on**: 006 and 008 (009/E1, 009/E2); the owner, on porting (inbox)
**Next**: A0, once 006 and 008's first increment land, restating the criteria and tasks it closes
