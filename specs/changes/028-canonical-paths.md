# Change: verified macOS canonical paths

**Opened**: 2026-10-28 · **Parts**: P1, P4 · **Journeys**: J-01, J-03, J-04, J-05 · **Tier**: T2 · **Parent**: 008-macos-qualification

## Intent

Continue the owner's 2026-10-28 request to fix the macOS advisory after merged027 portable fonts.
Implement accepted008/T006 and D-28/D4: three verified system aliases become one canonical spelling
across Core, CLI and MCP. The accepted baseline has742 default/480 private failures;262 additional
default-root failures make this path repair measurable. S1 asset inspection remains the dependent
008/T007 increment; S2 native backend and final zero-failure qualification remain parent obligations.

## Spec edits

| Level | ID | Edit | Note |
|-------|----|------|------|
| L1 | D-28/D4 | implementation of accepted canonical entry point | no registry stage change |
| L1 | D-29 root contract | restate three-alias canonical form before009 phaseB | accepted008/T006 precondition |
| L2 | P1.I-02, P4.I-01 | canonical root/arguments/display and unchanged strict-link refusal | implementation markers after proof |
| Change | 008/T006,009/E3 | record delivered dependency after proof | T007 and qualification remain open |

## Bounds

- C-01/C-03: existing .NET10 runtime PAL only, C# implementation; no dependency or license addition.
- C-04/P4.I-01: Core owns canonicalization; MCP confinement compares checked canonical strings.
- C-09: no file-format, schema or command/tool change; reported macOS root follows accepted D-28.
- C-11/Q-05: S0 registry and evidence stay unchanged; no new capability admission or support claim.
- Q-01/Q-04: every subsequent IO/lock/comparison uses the same canonical string; existing walks and
  no-partial-write behavior remain. No general realpath or symlink exception outside verified aliases.
- Q-03/C-10: verifier touches only three fixed root aliases/targets once per process, not user-controlled
  target paths; native input/resource budgets and original-byte MCP validation stay intact.
- Others in scope: hold.

## Approach

Base08523525cfff8770ebefaf56fb30da7acf9bbcf2. Consume accepted D-28/D4 and recovered008/AC-18..22.
Fresh design review resolves their integration with021's stricter lexical reference/font inspection
and023's I/O-free registry architecture before implementation.

### Entry point and verifier

`Core/Paths/PlatformPaths.cs` exposes `FullPath(path, basePath = null)` and `RelativePath(base,path)`.
The latter canonicalizes both operands before computing relative spelling. These helpers live outside
the pure registry's directory/namespace; its existing no-I/O tests remain intact. Update the exact
RepoChecks exemption to this one entry point and remove migrated file-hash inventory entries.

FullPath first computes a full lexical path. On macOS (any architecture), an exact leading component
`/var`, `/tmp` or `/etc` rewrites to `/private/<name>` only after process-cached verification:
uid0 symlink, exact relative LinkTarget `private/<name>`, and root-owned real destination directory.
Use the existing runtime `SystemNative_LStat` layout through an internal shared Unix metadata helper;
AssetPaths consumes the same unchanged ABI. Missing/inaccessible metadata, wrong mode/owner/target,
an unavailable PAL or unsupported runtime ABI never grants an alias. Failed verification leaves the
path unchanged for the caller's strict walk. `/home`, `/VAR`, prefix lookalikes and user links get no
special treatment. No path supplied by users is included in the verifier's fixed metadata requests.

An internal instance seam supplies platform choice/metadata for deterministic positive/negative
tests; it cannot override public production policy. Cache positive and negative alias results per process
under the existing trusted, quiescent filesystem model. This is not handle-based TOCTOU protection.

### Preserve newer lexical checks

021's ReferenceReader/FontDirectoryRules reject `link/..` before normalization. A blind GetFullPath
substitution would hide the link. Preserve that check through an internal FullPath overload, with
this invariant: **whenever it succeeds, its result equals public FullPath for the same original
input and base**. Both use only the public normalization-then-alias algorithm for final identity.

The internal overload computes that final result from the original input/base, without opening it.
Before final inspection, a component precheck retains the original absolute spelling and visits
each accumulated prefix in order. Resolve each prefix through the same FullPath algorithm for its
metadata check; never feed the rewritten prefix back into the original prefix accumulator. Check
each component before processing the next `..`, rejecting a non-verified link (including dangling
links) before it can be hidden. Preserve the existing intermediate-directory and missing-component
checks. This precheck establishes lexical traversal safety, not permission to open its final prefix.

Then invoke the existing strict inspector on the **exact final canonical path**, and return only
that inspected string for all later IO. The callback cannot authorize a differently spelled path.
Use this extra precheck only where inspection already applies; preserve the existing non-admitted
reference/font behavior. Native-reference lexical refusals and aggregate pre-open budgets retain
their ordering. No capability override or public bypass is introduced.

Required counterexamples for every verified alias, with absolute and relative inputs/bases:
`/tmp/../probe` resolves to `/probe` in both overloads, while `/./tmp/probe` resolves to
`/private/tmp/probe`. The raw prefix accumulator must not turn the first into `/private/probe`.
`/tmp/user-link/../probe` still refuses even if its normalized result is safe. Assert cross-overload
equality, idempotence, relative-display equality, and that the actual later IO path equals the final
strictly inspected path. Equivalent spelling is not assumed when `..` crosses an alias boundary;
the accepted lexical normalization order defines identity.

Font enumeration remains bounded and retains per-file skips. Enumerated filenames originate from
the inspected canonical directory; retained FullName usage must be explicitly proven enumeration-only.
The non-enumerated FileDocumentStore ancestor traversal moves to string parents. Inspect=false font
loading keeps its existing leaf-link check and receives canonical directories before enumeration.

### Surface migration and coverage

Migrate all16 current canonical-path inventory files plus both FullName provenances, including newer
ReferenceReader/InspectedFontSet. Audit every subsequent File/Directory/lock/open use: replacing only
an output-directory calculation while writing the original argument is insufficient. Canonicalize MCP
root/arguments/reported workingDirectory and both CLI display operands. Preserve error codes, original
path-length limits, strict walks, containment/overlap comparisons and caller-specific argument validation.

AC-18's eventual full four-suite PASS also depends on S1/S2 and is not closed here. This child proves
the canonical-path portion for store, manifest, asset root, MCP, pixel asset/export, still import,
artifact, CLI input/output and font directory; guarded operations still refuse under S0. AC-19 other
links refuse, AC-20 verifier negatives fail closed, AC-21 mixed spelling/display agreement and AC-22
path API migration are required here. Full native progress must retain all previous identities and
non-regressing outcomes, and improve measured alias failures; new focused cases are allowed,
disappearing cases/new skips are not.

Rejected: broad realpath, merely skipping aliases during the walk, process-global mutable test overrides,
silently weakening021's lexical inspection, moving IO into PlatformProfile, or flipping S1 without T007 proof.

## Tasks

- [ ] T001 Architecture/security design review of current-tree integration and recovered acceptance.
- [ ] T002 Core canonicalizer/verifier seam and all surface migrations; tighten exact inventory.
- [ ] T003 Negative/alias/mixed-spelling/lexical-inspection tests, native full comparison and package proof.
- [ ] T004 Owning reviews, reviewed baseline replacement, final native/CI gates and same-PR source closure.

## Checks

All five owning implementation review lenses apply; architecture/security design review precedes code.
Tests cover all three aliases, empty/root/prefix/dot/mixed-spelling inputs, every verifier failure and
cached failure, non-Mac identity, strict-link/`link/..` rejection, relative CLI output, Core store locks,
MCP real-wire capabilities and original path bounds. Actual macOS metadata verifies the three fixed
aliases; write tests use owned temp directories only. Four full suites in both modes compare against
the index at the verified current PR base; no new skip/rename exceptions. Clean-archive/installed
checks and required CI remain mandatory. Future qualification and S1/S2 integration stay with008.

## Acceptance

| Item | Proves | Check | Attempts | Result |
|------|--------|-------|----------|--------|
| A1 | D-28/D4, AC-20 | alias-verifier tables, cache/metadata failures and actual macOS alias facts | 0 | NOT_RUN |
| A2 | AC-18 path portion, AC-19/21, P1.I-02/P4.I-01 | cross-surface canonical paths and strict-link/lexical negatives; S0 preserved | 0 | NOT_RUN |
| A3 | AC-22, Q-01/Q-03/Q-04 | complete path inventory migration, source/archive/installed checks and default/private non-regression | 0 | NOT_RUN |
| A4 | source delivery | all owning reviews, reviewed baseline index, final native equivalence and exact-head CI/review gate | 0 | NOT_RUN |

## State

**Step**: pre-implementation design integration review
**Authority**: owner's2026-10-28 fix request and standing feature commit/push/PR/green-reviewed-merge
policy. AGD verification extension inherited; AB019 profiling hold and Dossier015 other-session work unchanged.
**Baseline**: merged027 at0852352; accepted221e14a progress source default742/private480 failures,
3508 cases and8 skips each. Failed026 parser attempts and027 width-probe attempts remain on their PRs.
**Design review**: initial architecture1HIGH/security1MEDIUM identify the same ordering defect:
rewriting an alias before normalization changes `/tmp/../probe` identity. Fix round1 specifies one
final algorithm, original-prefix lexical prechecks and strict inspection of the exact final IO path.
A1–A4 remain NOT_RUN; this is a design correction, not an implementation or native attempt.
**Review**: round 1 · 08523525cfff8770ebefaf56fb30da7acf9bbcf2..89cc3fa560fa1bd99c62345ff0ce10526150a27a · agd-security-reviewer · FAIL · 0 critical, 0 high, 1 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/21#issuecomment-6474584185
**Review**: round 1 · 08523525cfff8770ebefaf56fb30da7acf9bbcf2..89cc3fa560fa1bd99c62345ff0ce10526150a27a · agd-architecture-reviewer · FAIL · 0 critical, 1 high, 0 medium, 0 low · https://github.com/pevyhlas/agentgraphicdesign/pull/21#issuecomment-6474584666
**Next**: scoped design re-review of the identity correction; no production edits yet.
