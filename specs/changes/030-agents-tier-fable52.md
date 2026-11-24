# Change: agents tier fable52

**Opened**: 2026-11-24 · **Parts**: none (developer tooling) · **Journeys**: none · **Tier**: T1 · **Parent**: none

## Intent

Maintainer direction (2026-11-24): move the planning tier to Fable 5.2 and refresh every
reviewer model pin from the shared tier baseline. Developer tooling only; no product behavior.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|

Spec impact: none — reviewer model pins are developer configuration, not product behavior.

## Bounds

- C-01 (permissive licensing): holds: no dependency or license change.
- C-10 (no secrets): holds: only public model identifiers and hashes change.
- Others in scope: hold.

## Approach

Vendor the refreshed shared `tiers.json` with `render-agents.sh --refresh-baseline`, keep the
project-owned provenance convention (baseline token = first 12 hex of the snapshot sha256), update
the `Tier baseline` line in `.prdkit/memory/agents.md`, then re-render the five OpenCode projections.
Tier map (local `high` = `deep`, `standard` = `standard`) and the absence of overrides are unchanged.

## Tasks

- [x] T001 Refresh `.prdkit/agent-library/tiers.json` + `.ref` and the agents.md baseline line; re-render `.opencode/agents/`; `render-agents.sh --check`, `check-adoption.sh`, `doctor.sh`.

## Checks

`render-agents.sh --check` (no drift), `check-adoption.sh`, `doctor.sh`, required CI on the exact head.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | agents.md tier map | `render-agents.sh --check` and `--resolve`: deep → openai/gpt-6.2-astra high, standard → openai/gpt-6.2-sol high; snapshot sha256 matches `.ref` | 1 | PASS |
| A2 | constitution (Commands) | `check-adoption.sh` and `doctor.sh` pass locally; required CI green on the exact head | 0 | NOT_RUN |

## State

**Step**: review
**Waiting on**: independent code review and exact-head CI
**Next**: dispatch agd-code-reviewer on the committed range
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->
