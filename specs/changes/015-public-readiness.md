# Change: Polish public documentation, adopt Product-Kit 1.1.1, and enforce public repository hygiene

**Opened**: 2026-10-23 · **Parts**: P4 · **Journeys**: J-05 · **Tier**: T2 · **Parent**: none

<!-- Past its budget, a change splits into changes that each ship on their own (Parent: <id>). -->

## Intent

Owner follow-up (2026-10-24): succinct README, public Product-Kit, defect fixes, roadmap
continuation and logos through the respective tools. This change further shortens README;
the remaining work gets bounded changes, with existing stage/platform gates preserved.

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
- [x] Finish bounded public-text/history audit and repeatable regression protection.
- [ ] Verify and obtain independent code/security/test/ops reviews plus exact-head CI.
- [x] Recheck stale PRs (none open) and report release blockers.
- [ ] Integrate only after repository access and exact-head CI are restored.

## Checks

Spec impact: no new R/Q/B/I. No format/Core/MCP contract change: architecture lens not
applicable. Code, security/license, tests and ops apply. Verify links/examples, audit checker
self-tests, package smoke, adoption/doctor and exact-head hosted CI.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | J-05 | documentation links/examples and independent code read | 1 | PASS locally |
| A2 | C-01 | adoption, doctor (existing advisories) and licence review | 1 | PASS |
| A3 | C-10 | bounded public audit and extended guard regression checks | 1 | PASS within stated limits |
| A4 | Q-06 | local validation, package smoke, bundle --check and exact-head CI | 2 | LOCAL PASS at recorded revisions; hosted CI BLOCKED |

## State

**Step**: verify
**Resume**: 2026-10-24 owner reports app access added, but current credential still returns
push 403. No remote branch/PR exists. Product-Kit v1.1.1 remains the latest public release.
**Waiting on**: repository write access for the automation credential (push 403); then exact-head CI.
**Next**: restore repository write access, publish this branch and run all required exact-head
hosted checks; resolve the tests review's remaining qualification finding before any merge.
**Authority**: constitution permits commits, branch push/PR and green-reviewed merge;
release/tag/assets remain owner-gated. No history rewrite or remote branch deletion.
**Branch**: 015-public-readiness; base 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e.
**Delivery**: LOCAL_ONLY; remote main is the base above; no remote feature branch or PR exists.
**Attempts**: first review found guard-test/evidence gaps and overstated PR evidence; fix round 1
adds isolated negative tests, bytecode ignores and accurate blocked status. The claimed broken
Contributing anchor is not reproduced: both the exact committed README and the guard use
CONTRIBUTING.md, not a fragment (reviewer withdrew that finding). Full initial validation
passes build, all 3312 tests, 73.78% coverage, adoption/doctor, repository checker self-tests,
workflow, telemetry, dependency/security audit and readiness generation. It then fails package
smoke because source edits/bytecode were not committed at its cleanliness check. The clean
eeb427e archive subsequently passes all tests, both package contracts, isolated installs and
exact installed-package verification, including 56 CLI and 37 MCP package-proof tests. Final fixture canonicalization
(fix round 2, one line) passes self-tests even with a symlinked temporary-directory ancestor.
**Evidence**: `.prdkit/cache/evidence/015-public-readiness/` contains the original validation,
package/format/bundle checks, eight-commit/531-blob bounded public scan, upstream proof/CI
metadata and all review transcripts. Baseline review is not exact-head hosted CI.
**Final local evidence**: package provenance names eeb427e435f51de9cb4d4d55254b150151cdecac
and SDK 10.0.113. CLI hash fbd898c30c5589231de750e1ee567d471b6e5312916f1ba0bbcea43f3cf46a3e;
MCP hash 37c8edb172d8f7952ef76b4b3cec39baee49fccab500c56236b3263ff4efe67a.
The wrapper's final line rereads a later HEAD because the branch advanced during its isolated
archive run; use provenance, not that line, for package identity. Format check passes; bundle
input check passes at 7c34776230360c553b066fede86de7a4936ac1a4. This is not packaged proof
of the final metadata head. Later source delta is the reviewed developer-only fixture root.
**Remaining review blocker**: the tests review closed the implementation/guard and upstream-kit
behavior gaps, but retained qualification as medium. Its earlier incomplete local logs are
now complete; required hosted CI is still absent. No waiver or independent final acceptance
is inferred. The review gate correctly fails until that review is resolved.
**Review**: round 1 · 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e..ce60737c938294226f93675eeb2a92f8ece5250d · agd-code-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · .prdkit/cache/evidence/015-public-readiness/code-review.jsonl
**Review**: round 1 · 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e..ce60737c938294226f93675eeb2a92f8ece5250d · agd-security-reviewer · FAIL · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/015-public-readiness/security-review.jsonl (upstream access only)
**Review**: round 1 · 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e..ce60737c938294226f93675eeb2a92f8ece5250d · agd-test-reviewer · FAIL · 0 critical, 0 high, 3 medium, 0 low · .prdkit/cache/evidence/015-public-readiness/test-review.jsonl
**Review**: round 1 · 83c86f6f360ca4cc2a3dfa7dbf53f19a446e158e..ce60737c938294226f93675eeb2a92f8ece5250d · agd-ops-reviewer · FAIL · 0 critical, 0 high, 1 medium, 1 low · .prdkit/cache/evidence/015-public-readiness/ops-review.jsonl
**Review**: round 2 · ce60737c938294226f93675eeb2a92f8ece5250d..eeb427e435f51de9cb4d4d55254b150151cdecac · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/015-public-readiness/code-review-round-2.jsonl
**Review**: round 2 · ce60737c938294226f93675eeb2a92f8ece5250d..eeb427e435f51de9cb4d4d55254b150151cdecac · agd-security-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/015-public-readiness/security-review-round-2.jsonl
**Review**: round 3 · eeb427e435f51de9cb4d4d55254b150151cdecac..d511465192c6e0499d22f75ff97c5a5acb4437b5 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/015-public-readiness/code-review-round-3.jsonl
**Review**: round 3 · ce60737c938294226f93675eeb2a92f8ece5250d..d511465192c6e0499d22f75ff97c5a5acb4437b5 · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/015-public-readiness/ops-review-final.jsonl
**Review**: round 3 · ce60737c938294226f93675eeb2a92f8ece5250d..d511465192c6e0499d22f75ff97c5a5acb4437b5 · agd-test-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · .prdkit/cache/evidence/015-public-readiness/test-review-final.jsonl (qualification only, changed-source findings closed)
**Review**: round 4 · d511465192c6e0499d22f75ff97c5a5acb4437b5..14e0b1df2d9d5406146231ef64dca28e036eb124 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · .prdkit/cache/evidence/015-public-readiness/concise-readme-review.md
**README follow-up**: owner-requested T0 shortening reviewed independently; source install,
SDK, MCP discovery, links and limits remain accurate. No additional implementation fix round.
The existing tests qualification and exact-head hosted CI blocker remain unwaived.
<!-- One line per verdict (workflows/change.md, Review):
**Review**: round <n> · <base sha>..<reviewed head sha> · <reviewer> · PASS|FAIL · <n> critical, <n> high, <n> medium, <n> low · <link> -->
