# Change: Apple silicon macOS qualification

**Opened**: 2026-10-19 · **Parts**: P1, P4 · **Journeys**: J-03, J-04, J-05 · **Tier**: T3 · **Parent**: 013-brand-workflow-uptake

## Intent

Let an agent on Apple silicon with macOS 26 or later run `agd` and `agd-mcp` natively, with the
same fail-closed file-safety guarantees as on Linux x64: the asset pipeline, lint, preflight and
verify, still import, every pixel operation and vector document storage, in a project directory
or a temporary one.
Today two file-safety paths fail closed off Linux x64 and the store rejects paths under the
macOS `/var` alias. Slice S02 of change 013; S03, S06, S09 and S11 depend on it. Q-05 is
planned on this change.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L0 | Q-05 | qualified on osx-arm64, APFS, macOS 26 or later; remove the planned marker; state vector document storage per the inbox decision | per capability, recorded evidence |
| L0 | C-11 | name osx-arm64 as an owner-qualified profile for the guarded operations; restate vector document storage per the inbox decision | |
| L1 | environments | add the macOS qualification lane | runner choice: inbox |
| L2 | P1.I-08, P1.I-09 | widen to the exact verified profile tuple; add guards so every unqualified profile fails closed | |
| L2 | P1.I-02 | vector store: canonical paths and the `/var` alias; atomic replace and the cooperative lock (Q-04) qualified on osx-arm64 | guard or no guard: inbox |
| L2 | P4.B-02 | `get_capabilities.platform`: per-capability qualification (a direct cutover of the old fields) | |

## Bounds

- Q-05: the target of this change; nothing is admitted before its backend, tests and record.
- C-01: holds: no new dependency; the macOS native assets are already approved.
- C-11: holds: the owner chose this profile; every other profile still fails closed for the
  guarded operations; vector document storage follows the inbox decision.
- Others in scope: hold.

## Approach

Owner choices: Apple silicon osx-arm64, APFS and macOS 26 or later only; a strict symlink rule
with the three verified system aliases (`/var`, `/tmp`, `/etc`); the platform class of raster
outputs decided once a Linux-to-macOS output corpus comparison exists. Design: a Core platform
registry with three-valued per-capability qualification, Darwin backends behind a syscall seam
testable on every OS, one canonical-path entry point, a stored-name rule for pixel writer locks,
one platform-neutral error-path redactor, portable scripts and tests, and a scripted,
hash-bound qualification record. Excluded: Intel Macs, older macOS, non-APFS volumes, Linux
arm64 and Windows pixel backends.

## Tasks

- [ ] Port the first increment from the pre-public history, where it was implemented and
  reviewed: one CI job for the suites and coverage, the platform registry and
  `get_capabilities.platform`.
- [ ] Repository checks, OFL test fonts and the output corpus commands.
- [ ] Canonical paths for the vector store and the shared probe, the Darwin file probe and
  pixel backends with their fixtures, the stored-name gate for writer locks and the error-path
  redactor.
- [ ] Portable tests and scripts, the qualification command and record, the macOS CI job, the
  support matrix and the live availability probe.
- [ ] Choose the macOS CI runner for the public repository (inbox).
- [ ] Final qualification on osx-arm64 at the pull request head.

## Checks

Q-05 widens only with recorded qualification evidence. P1.I-08 and P1.I-09 gain guard tests,
so every unqualified profile fails closed; the capability report changes as a declared cutover.
P1.I-02 gains the store tests on osx-arm64, including paths under the `/var` alias.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | Q-05 | the full suite and the pipelines on Apple silicon macOS 26, with the qualification record | 0 | NOT_RUN |
| A2 | P1.I-08 | an Intel Mac or older macOS fails closed and changes nothing | 0 | NOT_RUN |
| A3 | P4.B-02 | capability report tests per profile | 0 | NOT_RUN |
| A4 | P1.I-02, Q-04 | vector document create, read and atomic replace under the lock on Apple silicon macOS 26, including a path under `/var`; behavior off-profile per the inbox decision | 0 | NOT_RUN |

## State

**Step**: tasks
**Waiting on**: the owner, on porting and on the macOS CI runner (inbox)
**Next**: port the first increment
