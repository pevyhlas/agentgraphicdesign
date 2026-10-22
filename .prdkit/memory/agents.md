# Agent Registry

> The project's agent roster for automated review and implementation. OpenCode
> reviewers are native files in `.opencode/agents/`; launch from the intended
> checkout/worktree and restart after changing configuration or definitions.

## Registry

| Domain / Role | Agent | Tier | Purpose |
|---------------|-------|------|---------|
| Code Review | agd-code-reviewer | standard | Correctness, shared Core semantics, CLI/MCP parity |
| Security Review | agd-security-reviewer | high | License compliance, input/path bounds, secrets, auth |
| Tests & Verification | agd-test-reviewer | standard | Independent unit, real-wire, installed-package and visual evidence |
| Architecture | agd-architecture-reviewer | high | Document/native formats, engine design, persistence and API surface |
| Release / Operations | agd-ops-reviewer | standard | CI, clean-archive packages, release provenance, supported platforms |
| Implementation | build / workflow agents | high | Routine implementation; use planning tier for design decisions |

## Tier → model / effort map

Models resolve from the project-owned vendored tier map at
`.prdkit/agent-library/tiers.json`: `high` = `deep`, `standard` = `standard`. The
`frontier` alias (library `verydeep`) is reserved for deliberate planning and is
never a reviewer tier; `fast` resolves to `light`. Reviewers must not inherit the parent
session's model/effort: every projection carries an explicit model and variant. A
deviation is an override row with a Reason, never a hand-edited projection.

Tier baseline: agent-library tiers.json@0856aa639778
(the snapshot is project-owned with no public upstream; the baseline token is the first
12 hex of the snapshot sha256, not an upstream commit; see `tiers.json.ref`)
Banned models: claude-opus-5

| Local tier | Library tier |
|------------|--------------|
| high | deep |
| standard | standard |

The review lenses in the constitution (Review and checks) for code, security, tests,
architecture and release map respectively to the five named agents above. High-risk persistence, input,
license or format scopes require the high-tier owning reviewer in addition to
routine code/test review. Deterministic gates run directly, without model dispatch.

`mode: all` supports named primary and subagent use. Reviewers deny edits/delegation
and use a bounded read/search/web/Git/GitHub inspection tool set; build/test gates
run in the driving session or CI. They may tag their own sessions when a
session-tagging tool is available.
Neutral definitions live in `.prdkit/agents/` (`harnesses: [opencode]`); the
`.opencode/agents/` files are their projections. Edit the definition, then run
`.prdkit/scripts/bash/render-agents.sh`: it owns `model`, `variant` and the body, while
`mode` and the permission block stay project-owned in the projection. Claude projections
are not rendered, because the bodies are tuned for OpenCode (including optional
session tagging).

## Notes

- **License compliance** is part of Security Review — every dependency must be verified MIT-compatible before adoption.
- **Document format design** is a critical architecture domain — JSON schema changes require architecture review.
