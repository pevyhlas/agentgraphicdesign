# Change: Polish public documentation, adopt Product-Kit 1.1.1, and enforce public repository hygiene

**Opened**: 2026-10-23 · **Parts**: P4 · **Journeys**: J-05 · **Tier**: T2 · **Parent**: none

<!-- Past its budget, a change splits into changes that each ship on their own (Parent: <id>). -->

## Intent

Polish public documentation, verify hosted CI/public-data hygiene and adopt public kit 1.1.1.
Check stale PRs afterwards. Resolve the SDK-patch recommendation through bounded maintenance;
release authority, platforms and future editor domains remain separate.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | operations | kit provenance and SDK patch | no product/API/format change |

## Bounds

<!-- From `.prdkit/scripts/bash/bounds.sh <id>`: each constraint, non-goal or quality this change
could break, with how it holds; then "Others in scope: hold". A conflict goes to the owner inbox
with both sides, never resolved here. -->

- C-01: holds: preserve full upstream MIT notices, with independent licence review.
- C-03: holds: no production runtime, authoring or recipe dependency added.
- C-09: holds: no document-format change.
- C-10: holds: public-ancestry audit and redacted findings.
- C-11, Q-05: hold: no added platform is enabled or claimed.
- Q-06: holds: SDK/audit pin move together, bundle checks remain intact.
- Others in scope: hold.

## Approach

Preserve advanced guidance in REFERENCE.md and make README a concise starting page. Keep the
no-submodule vendored installation; upgrade from public v1.1.1, preserve policy, record the
source/manifest hashes and full notices. Adopt the recommended 10.0.113 SDK patch with audit
checker and fixtures. No runtime package change or claim of comprehensive PII detection.

## Tasks

- [x] Inspect public remote, CI/rulesets, PRs and release availability.
- [x] Polish README and retain advanced reference.
- [x] Upgrade kit/notices and SDK/audit pin.
- [ ] Finish public-data audit and repeatable regression protection.
- [ ] Verify and obtain independent code/security/test/ops reviews plus exact-head CI.
- [ ] Integrate; recheck stale PRs and report release blockers.

## Checks

Spec impact: no new R/Q/B/I. No format/Core/MCP contract change: architecture lens not
applicable. Code, security/license, tests and ops apply. Verify links/examples, audit checker
self-tests, package smoke, adoption/doctor and exact-head hosted CI.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | J-05 | documentation links/examples and independent read | 0 | NOT_RUN |
| A2 | C-01 | adoption, doctor and licence review | 0 | NOT_RUN |
| A3 | C-10 | public audit and regression checks | 0 | NOT_RUN |
| A4 | Q-06 | local validation, bundle --check and exact-head CI | 0 | NOT_RUN |

## State

**Step**: build
**Waiting on**: nothing
**Next**: finish hygiene checks, verify and review.
**Authority**: constitution permits commits, branch push/PR and green-reviewed merge;
release/tag/assets remain owner-gated. No history rewrite or remote branch deletion.
**Branch**: 015-public-readiness; base 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e.
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->
