# Optional Claude telemetry profile

This is an opt-in convenience for maintainers (or any contributor) who run their own
local OTLP collector on `127.0.0.1:4318`. It is off by default: nothing runs it
implicitly, it is not part of the project infrastructure, and the repository's
`.claude/settings.json` (empty) does not enable telemetry, hooks or permissions. The
profile lives in `scripts/claude-telemetry.env.json`.

To explicitly launch Claude Code with this cumulative, metrics-only profile:

```sh
bash scripts/claude-with-telemetry.sh
```

The Bash 4+/jq launcher reads the profile environment and passes it to `claude`
without eval; remaining arguments are forwarded exactly. It targets a loopback OTLP
collector and disables logs, traces and high-cardinality session/account attributes.
It accepts only the ten declared telemetry keys and clears inherited `OTEL_*`,
Claude OTel content and beta-tracing variables before applying that profile. Other
Claude/model authentication settings remain outside this telemetry profile; managed
administrator settings still have Claude's documented precedence.
Alternatively configure the same variables in supported user/managed settings or
your launch environment. Restart Claude Code after changing startup configuration.

`bash scripts/test-claude-with-telemetry.sh` verifies the effective launch environment
and failure behavior with a fake CLI, without model calls or metric export. It does
not prove backend delivery: after a real session starts, check the backend for
`claude_code.session.count` and cumulative counters. Existing processes retain their
startup configuration. Reference: https://code.claude.com/docs/en/monitoring-usage.
