# Change: Apple silicon macOS qualification

**Opened**: 2026-10-19 · **Parts**: P1, P4 · **Journeys**: J-03, J-04, J-05 · **Tier**: T3 · **Parent**: 013-brand-workflow-uptake

## Intent

Let an agent on Apple silicon with macOS 26 or later run `agd` and `agd-mcp` natively, with the
same fail-closed file-safety guarantees as on Linux x64: the asset pipeline, lint, preflight and
verify, still import, every pixel operation and vector document storage, in a project directory
or a temporary one. A consumer project's brand work ran AGD only through Docker emulation,
because two file-safety paths fail closed off Linux x64 and the store rejects paths under the
macOS `/var` alias. The owner lifted the platform gate for this profile. Slice S02 of change 013;
S03, S06, S09 and S11 depend on it. Q-05 is planned on this change.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L0 | Q-05 | qualified on osx-arm64, APFS, macOS 26 or later; remove the planned marker; state vector document storage per the inbox decision | per capability, recorded evidence |
| L0 | C-11 | name osx-arm64 as an owner-qualified profile for the guarded operations; restate vector document storage per the inbox decision | |
| L1 | D-28 | added: the registry, Darwin backends, canonical paths, evidence and CI rules | accepted at design closure |
| L1 | environments | add the macOS qualification lane | runner choice: inbox |
| L2 | P1.I-08, P1.I-09 | widen to the exact verified profile tuple; every unqualified profile fails closed | |
| L2 | P1.I-02 | vector store: canonical paths and the `/var` alias; atomic replace and the lock (Q-04) qualified on osx-arm64 | guard or no guard: inbox |
| L2 | P4.B-02 | `get_capabilities.platform` replaces `supportedPlatform` and `currentPlatformMatches` | declared cutover |

## Bounds

- Q-05: the target of this change; nothing is admitted before its backend, tests and record.
- C-01: holds: no new runtime dependency; the macOS native assets are already approved; the
  OFL-1.1 test fonts get approval entries and notices.
- C-11: holds: the owner chose this profile; every other profile still fails closed for the
  guarded operations; vector document storage follows the inbox decision.
- C-10: holds: the runner token never reaches argv, files or logs; CI holds no secrets.
- Q-01: holds per D-28: SVG, JSON and native outputs must match across hosts or the claim blocks.
- Others in scope: hold.

## Approach

Owner choices: Apple silicon osx-arm64, APFS, macOS 26 or later only; the strict symlink rule with
the three verified aliases `/var`, `/tmp`, `/etc`; the raster platform class decided once the
Linux-to-macOS corpus exists (recommended outcome B, D-28). The pre-public runner choice, a
self-hosted runner under a separate unprivileged account, is reopened for the public repository
(inbox). Design (D-28; plan sections 008/D1 to 008/D12): a Core registry with three-valued
per-capability qualification and staged values; Darwin backends behind a syscall seam testable on
every OS; one canonical-path entry point; a stored-name gate for pixel writer locks; one redactor;
portable tests and scripts; a corpus comparison; a scripted, hash-bound qualification record with a
claim check and merge evidence. Excluded: Intel Macs, older macOS, non-APFS volumes, Linux arm64 and
Windows pixel backends, handle-based no-follow reads, ephemeral runner registration, and cutting a
release (owner). The registry and the capabilities member are owned here; change 009 consumes them
unchanged and its phase B waits for 008/T006, 008/T012 and 008/T019.

## Tasks

Pull request increments, in order (task IDs as in the pre-public plan):

- [ ] PR-0, 008/T000: one CI job for the four suites and the coverage gate, no inter-job artifacts
  (implemented and reviewed pre-public; its artifact-quota motive may not apply here, so decide
  while porting).
- [ ] PR-1, 008/T001 and 008/T002: `Core/Platform` exactly as D-28, stage S0 values,
  `get_capabilities.platform`, golden fixtures per tuple, gate-agreement and entry-point refusal
  tests, the evidence file with the claim check's linux-x64 entries (implemented and reviewed
  pre-public: port it). Merge evidence (D-28 phase P0): Linux CI, plus a local macOS run of the
  008/T002 test classes on the pull request head in `private-tmp` mode, and those classes pass; no
  `macos-progress` record, which 008/T003 builds later.
- [ ] 008/T003 repository checks (path-API ban, OS-guarded-return ban, workflow lint, bash 3.2
  lint, the `macos-progress` record); 008/T016a runner script and runbook (only if the owner picks
  a self-hosted runner).
- [ ] 008/T012 `PathRedactor`, which also fixes the unanchored Core path pattern that garbles
  `/private/...` roots and relative store locations with a `tmp`, `var` or similar segment in
  `STORE_CONFLICT` messages (deferred here from change 006); 008/T013a OFL-1.1 test fonts;
  008/T004 corpus commands and the dispatch-only Linux corpus job.
- [ ] 008/T006 `PlatformPaths` and the alias canonicalizer; 008/T007 admit asset inspection (S1).
- [ ] 008/T008 backend split and the Linux locking matrix; 008/T009 Darwin backend with seam tables;
  008/T010 real-seam APFS fixtures; 008/T011 stored-name gate and the macOS product-path tests (S2).
- [ ] 008/T013 remaining test portability; 008/T014 script portability; 008/T015 `platform-qualify`
  and the `d10` claim check; 008/T019 the live availability probe (needed by change 009 B5).
- [ ] 008/T016b the macOS workflow; 008/T016c the owner's one-time runner setup (self-hosted only).
- [ ] 008/T005 the corpus at one commit with T006, T007 and T011, then the raster class wording;
  008/T017 the support matrix as the single source in the release contract and the linked docs.
- [ ] 008/T018 final qualification (S3). The pre-public plan qualified the pull request head and
  merged it with a merge commit; this repository's main ruleset is squash-only with linear history,
  so how the record's source commit stays reachable is open (D-28, inbox). Recommended: merge the
  code, qualify the resulting squash commit on main, then add the record and the `d10` entry in a
  results-only pull request.
- [ ] Choose the macOS CI runner for the public repository (inbox).
- [ ] Choose how a `d10` record's source commit stays reachable under squash-only main (inbox);
  008/T015 builds the claim check for the chosen flow.

Implementation-review checks from design closure, each settled in its task's review:
008/RC-01 anchored, column-pinned pass patterns plus positive and `FAIL`-row self-tests (T002);
RC-02 the first post-T018 Linux log confirms the full-history claim check (T018); RC-03
`verify-merge` detects a non-PR head from explicit input, never a parent count (T015); RC-04 how a
schema tightening treats a standing record (T015); RC-05 40-hex source commits passed safely to Git
(T015); RC-06 completeness self-tests for a missing quarantine result and mode-rule violations
(T015); RC-07, pre-public a merge-commit reminder and squash recovery, is redesigned for the
squash-only main ruleset per the inbox choice (T015, T018); RC-08 the claim check on the flip commit
is expected red (T018), and under the two-step flow the redesign settles where the registry flip
lands so main is not left red between the two merges; RC-09 "not release evidence" wording for local builds; RC-10
Linux `OsVersion` is the kernel version; RC-11 teardown removes crontab, user jobs and the account
record first (T016a); RC-12 the token rule covers teardown (T016a); RC-13 the installed audit digest
matches the checkout (T016a); RC-14 results-only commits wait for the head run (T016b); RC-15
Markdown inside platform-sensitive paths (T016b, T015); RC-16 shorten macOS runs by triggering less
often, never by shrinking the record (T016b, T018).

## Checks

Q-05 widens only with recorded qualification evidence; the registry values change only with the
enforcement and evidence behind them. P1.I-08 and P1.I-09 gain guard tests, so every unqualified
profile fails closed; the capability report changes as a declared cutover. P1.I-02 gains store tests
on osx-arm64, including paths under the `/var` alias.

## Acceptance

Design acceptance criteria 008/AC-01 to 008/AC-34 group as below. Only the group labels are public:
each criterion's full text, and each task's acceptance text, stay in the pre-public history. D-28
holds the binding contract; the pull request that ports or implements an increment restates, in its
body and in this record, the criteria and tasks it closes.

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | Q-05 | AC-26 to AC-30: the four suites in three root modes and the installed tools on Apple silicon macOS 26, in a complete zero-failure qualification record | 0 | NOT_RUN |
| A2 | P1.I-08 | AC-02, AC-03, AC-14: Intel, older macOS, non-APFS and disabled locking fail closed and change nothing | 0 | NOT_RUN |
| A3 | P4.B-02 | AC-01, AC-04, AC-32: the platform member per profile tuple, the probe, the claim check | 0 | NOT_RUN |
| A4 | P1.I-02, Q-04 | AC-10 to AC-13, AC-15 to AC-23: create, publish, replace and lock on APFS, aliases and stored names, including a path under `/var` | 0 | NOT_RUN |
| A5 | Q-01 | AC-07 to AC-09: the corpus comparison and the raster class wording | 0 | NOT_RUN |
| A6 | Q-02 | AC-24, AC-25, AC-31, AC-33, AC-34: redaction, the single support matrix, runner and workflow rules | 0 | NOT_RUN |
| A7 | P1.I-08, P1.I-09 | AC-05, AC-06: asset build and check, lint, artifact preflight and verify, and still import admitted on osx-arm64 with regular files, same exit codes and reports as on linux-x64; FIFO, socket, device, symlink and dangling inputs and outputs fail closed without opening the leaf, and a FIFO never blocks | 0 | NOT_RUN |

## State

**Step**: tasks
**Pre-public review**: design approved at closure pass 1 (architecture, security, operations,
test), and the first increment (PR-0 and PR-1) passed implementation review in all four lenses
with low notes only, both in the pre-public history; the ranges, review reports and the criterion
and task text are not public
**Waiting on**: the owner, on porting (inbox), on the macOS CI runner (inbox) and on how `d10`
evidence merges under squash-only main (inbox)
**Next**: port PR-0 and PR-1 as their own pull request, restating the criteria and tasks they
close, then 008/T003
