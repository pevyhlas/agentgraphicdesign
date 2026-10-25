# Change: Run observable Apple silicon macOS CI on every pull request

**Opened**: 2026-10-25 · **Parts**: P1, P4 · **Journeys**: J-05 · **Tier**: T2 · **Parent**: 008-macos-qualification

## Intent

The owner asked to set up GitHub macOS CI while continuing the roadmap. Fold the runner-choice
inbox item into a standard hosted Apple silicon macOS 26 lane on every PR, main push, schedule
and manual run. Expose the current platform failures with durable diagnostics for change 008.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | operations, environments | macOS 26 arm64 advisory PR lane with logs and TRX | Q-05 qualification remains open |
| L1 | D-28 runner choice | standard hosted runner; diagnostic stage before qualification tooling | inactive account-provisioning alternative retained |
| Policy | constitution Commands | describe the expanded trigger coverage | no merge-gate change |

## Bounds

- C-10: read-only token, no secrets, pinned existing actions, ephemeral hosted runner; uploaded
  output is generated from repository fixtures only.
- C-11, Q-05: advisory CI is not qualification; no platform registry or support claim changes.
- Q-01, Q-02: run the full source suite without filtering or regenerating goldens; report failures.
- Q-03: bounded job/test timeouts and seven-day artifact retention.
- Q-06, N-06: no release or feed publishing; Linux package gates remain required.
- Others in scope: hold.

## Approach

Choose the standard `macos-26` arm64 image, rather than a drifting `macos-latest` or a managed
machine. Record and assert OS major/architecture and retain image/SDK identity. Use the runner's
physical temporary directory, run all four source suites, and preserve console/TRX results even
on failure. Keep the existing job advisory until change 008 qualifies the platform. Show the
actual test-step outcome in the job summary so advisory success cannot imply passing tests.

## Tasks

- [ ] Wire the lane and reconcile the runner choice.
- [ ] Validate locally; publish a draft PR and observe its macOS run and downloadable artifact.
- [ ] Independent code, tests and operations review; exact-head gates; merge.

## Checks

CI-only configuration: no L0 or L2 behavior changes. Existing workflow-policy self-tests,
Product-Kit adoption/doctor, public-repository hygiene, whitespace and the real hosted run
verify the change. Security lens not applicable: existing hosted trust model, actions,
permissions and fixture-only test data are unchanged. Architecture lens not applicable:
no engine, document, producer or MCP interface changes.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | L1 CI, C-10 | local workflow-policy/self-test, adoption, doctor, public hygiene and whitespace | 1 | PASS: Linux local checks, `.prdkit/cache/evidence/019-macos-ci/local-checks.log`; hosted verification pending |
| A2 | L1 CI, Q-05 | PR run asserts macOS 26 arm64, builds and attempts all four suites; summary and downloadable profile/console/TRX artifact preserve actual outcomes | 0 | NOT_RUN |
| A3 | Delivery policy | required Linux checks and independent reviews pass on exact head | 0 | NOT_RUN |

## State

**Step**: implement
**Waiting on**: nothing
**Authority**: owner request “go ahead - we should setup github CI for mac os”; constitution
permits feature commit/push/PR and exact-head reviewed green merge. Releases remain owner-gated.
**Branch**: 019-macos-ci; base 55c0efab887ea924c14019f0d98cc5e5eec1f5c7.
**Attempts**: acceptance 0; review fix rounds 0. Branch-protection API read returned 403 with
app credentials; workflow delivery does not require administration access.
**Next**: publish the CI checkpoint and inspect hosted macOS results before final review.
