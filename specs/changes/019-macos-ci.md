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

- [x] Wire the lane and reconcile the runner choice.
- [x] Validate locally; publish PR #12 and observe its macOS run and downloaded artifact.
- [x] Independent code, tests and operations review; required source-head gates.
- [ ] Close the record and merge after final-head gates.

## Checks

CI-only configuration: no L0 or L2 behavior changes. Existing workflow-policy self-tests,
Product-Kit adoption/doctor, public-repository hygiene, whitespace and the real hosted run
verify the change. Security lens not applicable: existing hosted trust model, actions,
permissions and fixture-only test data are unchanged. Architecture lens not applicable:
no engine, document, producer or MCP interface changes.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | L1 CI, C-10 | workflow-policy/self-test, adoption, doctor, public hygiene and whitespace | 1 | PASS: local log and required hosted gates on `9538351`, [run](https://github.com/pevyhlas/agentgraphicdesign/actions/runs/40219615008) |
| A2 | L1 CI, Q-05 | macOS PR run, all four suites and retained diagnostics | 1 | PASS for diagnostic infrastructure on `9538351`: macOS 26.6.2 arm64, 3363 executed, 2878 passed/485 failed, four TRX files downloaded; [independent evidence](https://github.com/pevyhlas/agentgraphicdesign/pull/12#issuecomment-6401308496). Not platform qualification |
| A3 | Delivery policy | required Linux checks and independent reviews pass on exact source head | 1 | PASS: all eight Linux jobs green on `9538351`, three independent PASS verdicts below; final metadata-only head gated again before merge |

## State

**Step**: close
**Waiting on**: nothing
**Authority**: owner request “go ahead - we should setup github CI for mac os”; constitution
permits feature commit/push/PR and exact-head reviewed green merge. Releases remain owner-gated.
**Branch / PR**: 019-macos-ci; base 55c0efab887ea924c14019f0d98cc5e5eec1f5c7; [PR #12](https://github.com/pevyhlas/agentgraphicdesign/pull/12).
**Attempts**: A1/A2/A3 one each; review fix rounds 0. Branch-protection API read returned 403 with
app credentials; workflow delivery does not require administration access.
**Next**: commit review record, close, publish and enforce final-head gates before merge.
**Review**: round 1 · 55c0efab887ea924c14019f0d98cc5e5eec1f5c7..95383519630275bbaab104df2c2137addb20bff6 · agd-code-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/12#issuecomment-6401260870
**Review**: round 1 · 55c0efab887ea924c14019f0d98cc5e5eec1f5c7..95383519630275bbaab104df2c2137addb20bff6 · agd-ops-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/12#issuecomment-6401261081
**Review**: round 1 · 55c0efab887ea924c14019f0d98cc5e5eec1f5c7..95383519630275bbaab104df2c2137addb20bff6 · agd-test-reviewer · PASS · 0 critical, 0 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/12#issuecomment-6401308496
