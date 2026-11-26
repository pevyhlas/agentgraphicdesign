# Change: Product-Kit 3.1.5 and agent-library 1.1.0 cutover

**Opened**: 2026-11-26

**Parts**: none (workflow tooling)

**Journeys**: none

**Tags**:

**Tier**: T1

**Parent**: none

## Intent

Move the vendored Product-Kit from 1.2.1 to 3.1.5 in copy mode and sync the five reviewer
agents from agent-library 1.1.0, so reviews get the library's canonical bodies, tier settings
and lens skills on both Claude Code and OpenCode. Maintainers and agents keep one estate workflow.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|

## Bounds

- C-01..: hold: no product code, package, CLI or MCP surface changes.
- Others in scope: hold.

## Approach

Fix the 1.x manifest (`options: agent-tiers`), run the 3.1.5 installer with `--upgrade --copy`,
migrate project records to the 2.2 history layout, then sync agents from a detached
agent-library v1.1.0 checkout and re-render projections.

## Tasks

## Checks

- PK doctor and check-adoption.sh pass; agent-library `ci/sync.py --check` reports no drift.
- Required CI green at the exact head.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | kit 3.1.5 | doctor.sh + check-adoption.sh in CI repository-gates | 0 | NOT_RUN |
| A2 | agents synced | ci/sync.py --check at v1.1.0 | 0 | NOT_RUN |

## State

**Step**: build
**Waiting on**: nothing
**Next**: agent sync, CI, review
