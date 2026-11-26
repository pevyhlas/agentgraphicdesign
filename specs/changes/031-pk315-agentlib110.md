# Change: Product-Kit 3.1.5 cutover

**Opened**: 2026-11-26

**Parts**: none (workflow tooling)

**Journeys**: none

**Tags**:

**Tier**: T1

**Parent**: none

## Intent

Move the vendored Product-Kit from 1.2.1 to 3.1.5 in copy mode so maintainers and agents keep
one estate workflow. The planned agent-library 1.1.0 sync of the five reviewers (the reason for
this record's ID) is held: the library's license does not yet cover redistribution in this public
MIT repository, so vendoring its agent bodies and skill scripts here waits on the maintainer
(inbox).

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|

## Bounds

- C-01: holds: no product code, package, CLI or MCP surface changes; kit files stay MIT (D-01).
- Others in scope: hold.

## Approach

Fix the 1.x manifest (`options: agent-tiers`), run the 3.1.5 installer with `--upgrade --copy`,
and migrate project records to the 2.2 history layout. Reviewer definitions and projections
are unchanged (3.1.5 `render-agents.sh` re-renders them byte-identical).

## Tasks

## Checks

- PK doctor and check-adoption.sh pass; `render-agents.sh` leaves all five projections unchanged.
- Required CI green at the exact head.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | kit 3.1.5 | doctor.sh + check-adoption.sh in CI repository-gates | 0 | NOT_RUN |
| A2 | agents unchanged | render-agents.sh: 0 created, 0 updated, 5 unchanged | 1 | PASS |

## State

**Step**: build
**Waiting on**: nothing
**Next**: review
<!-- Outcome checkpoint format: .prdkit/scripts/bash/outcome-checkpoint.md -->
<!-- Verdicts live in publisher-authored PR reviews, never in this record (workflows/review.md). -->
