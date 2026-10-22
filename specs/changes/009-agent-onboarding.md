# Change: agent onboarding

**Opened**: 2026-10-19 · **Parts**: P1, P4 · **Journeys**: J-05, J-01 · **Tier**: T3 · **Parent**: 013-brand-workflow-uptake

## Intent

In consumer use, getting AGD running cost an agent far more time than the work itself: it
hand-wrote client configuration and a smoke test, missed the path commands and mislabelled an
unreleased build as a release. After this change the agent finds commands by
task in its MCP context or `agd capabilities --find`, follows recipes the test suite executes,
gets a truthful producer label, runs `agd doctor` for one structured environment report, and
runs `agd mcp install` to register the server safely. Slice S03 of change 013; depends on 006
and 008.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L2 | P4 | new behaviors: capabilities by task, guide sections, server instructions, doctor, MCP install, broad-root refusal | |
| L2 | P4.B-02 | extend discovery with the platform member from 008 | |
| L1 | interfaces | surface manifest v2 | declared cutover |

## Bounds

- C-02, C-10: hold: no network, no secrets; install writes only the project scope.
- Q-02: holds: the producer label never claims a release for a source build.
- Others in scope: hold.

## Approach

The owner accepted every recommended option: client setup lives in AGD and is narrow (install
writes project scope only, everything else is printed); client-owned state is never edited;
absolute, verified paths; builds are stamped `bundle` or `source` with a dirty flag and a
self-describing label; two executables stay; no skills are written into consumer repositories;
`agd-mcp` refuses broad roots at startup unless explicitly allowed at the launch site. Link
components in the written root are refused, never resolved.

## Tasks

- [ ] Phase A: producer identity and build stamping, `install-local.sh`, embedded references,
  surface manifest v2, tool descriptions, `agd capabilities`, `agd guide`, server instructions,
  the recipe harness, documentation and package qualification.
- [ ] Phase B: store layout rules, the broad-root rule and refusal, environment probes,
  `agd doctor` and `diagnose_environment`, error hints, client adapters and `agd mcp install`,
  the `platform.support` check, documentation and verification.

## Checks

Install writes the project scope only and needs no network (C-02, C-10). Each new P4 behavior
gets a test; surface manifest v2 is a declared cutover (C-09).

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | J-05 | an agent installs, verifies and diagnoses the connection with one command | 0 | NOT_RUN |
| A2 | Q-02 | producer-label tests for bundle and source builds | 0 | NOT_RUN |

## State

**Step**: tasks
**Waiting on**: 006 and 008; the owner, on porting (inbox)
**Next**: Phase A, after 006 lands
