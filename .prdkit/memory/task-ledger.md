# Optional execution ledger

## Configuration

- backend: none
- events: all
- mode: best-effort
- operator: none

This project uses file-only mode, and `operator: none` keeps contributor identities out of
events. Events emitted with `.prdkit/scripts/bash/ledger-emit.sh <event>` are appended to the
gitignored `.prdkit/ledger/events.jsonl`. `backend: command` would also pipe each event to one
executable, repository-contained path without arguments; adapters own credentials and network
access. `.prdkit/scripts/bash/ledger-spool-adapter.sh` mirrors events into a per-host spool that
survives worktree deletion. `events` filters delivery; `mode: strict` returns adapter failure to
the caller. Events are diagnostics, never approval.

## Vocabulary: prdkit.ledger.v1

The `feature` id is the change id. Emit, best-effort, at actual boundaries:

| Event | Boundary |
|-------|----------|
| feature.created | Change opened |
| review.recorded | Reviewer verdict recorded |
| evidence.recorded | Acceptance result recorded |
| blocker.opened / blocker.resolved | Blocker or stall lifecycle |
| feature.merged | Closing PR merged |

`tasks.generated`, `task.state_changed`, `phase.changed`, `readiness.changed` and
`exploration.converged` stay valid for 0.x spools; 1.0 workflows do not emit them.
