# macOS portability progress

`RepoChecks macos-progress` implements the interim D-28/P0 check for change008. A complete
record can contain failed and skipped tests. It is **not platform qualification**.

## Run and inspect

Dispatch **CI** on the exact feature branch. The two `macos-progress` jobs use macOS26 arm64,
the pinned SDK and all four source suites. `default-tmpdir` inherits
`getconf DARWIN_USER_TEMP_DIR` verbatim; `private-tmp` uses a new0700 physical directory beneath
`/private/tmp`. The job checks out the PR head, builds it and discovers every case before running.

For an equivalent local native run, build the solution and `scripts/RepoChecks` in Release,
select one of those roots as `TMPDIR`, then run from a clean checkout:

```sh
dotnet scripts/RepoChecks/bin/Release/net10.0/RepoChecks.dll \
  macos-progress run . "$OUTPUT" "$HEAD_SHA" "$MODE"
dotnet scripts/RepoChecks/bin/Release/net10.0/RepoChecks.dll \
  macos-progress check "$OUTPUT" "$HEAD_SHA"
```

`OUTPUT` must be fresh and outside tracked source. Each invocation preserves command arguments,
stdout, stderr, exits, discovery, TRX and environment identity. A complete failed suite returns a
valid progress record; a crash, timeout, incomplete discovery, missing suite or inconsistent result
does not. Each command has a five-minute deadline, each input/log a32MiB limit and records a30000
case limit. Unknown outcomes and duplicate cases fail closed. The pinned VSTest/xUnit combination
reports skipped rows as `NotExecuted` even when its aggregate skip counter is zero.
An owned POSIX helper keeps its process-group identity until the direct exit is recorded, then
the parent terminates the group, including descendants retaining output pipes. Log setup happens
before launch; timeout and overflow retain failure exits. This is cleanup on a trusted host, not
a sandbox against commands deliberately escaping their session.

Download both artifacts and run `check` on each mode directory. Retain original failed attempts.
The TSV is sorted by ordinal suite/case identity, carries explicit skips and binds counts, mode,
source commit, stage, profile, SDK/runtime and a SHA-256 of its body.

## Compare against the accepted predecessor

`.config/macos-progress-baseline.json` identifies the accepted source, record digest and durable
PR evidence for each mode. Retrieve both exact TSVs from those PR comments and verify their
digests; never choose an older run because it has more failures. Put each pair of TSVs in its own
directory as `default-tmpdir.tsv` and `private-tmp.tsv`, then run:

```sh
dotnet scripts/RepoChecks/bin/Release/net10.0/RepoChecks.dll macos-progress compare \
  . "$ACTUAL_PR_BASE_SHA" "$PREVIOUS_RECORDS" "$CURRENT_RECORDS" "$HEAD_SHA"
```

The comparator reads the **committed target-base index**, not the proposed working-tree index.
It verifies the base against the public remote `main` and requires a clean exact head descended
from that base. A moved base requires fetch/rebase and recomparison. Before merge also verify
the actual PR targets that base and the tested head. SDK/runtime, profile, stage and mode must
match; a toolchain change needs explicitly reviewed baseline replacement.

Failures may only shrink; a previously fixed failure cannot return. Old cases may not disappear.
`.config/macos-progress-exceptions.json` can explicitly name a reasoned new skip or a one-to-one
rename, reviewed on the same PR. Duplicate, stale or unreasoned exceptions fail. Exceptions do
not authorize a new failing case.
Skip entries require `Mode`, `Suite`, `Name` and `Reason`; each mode can have different outcomes.
Rename entries require `Suite`, `From`, `To` and `Reason` and apply to both modes.

## Accept and retain a baseline

Bootstrap is explicit in change026. Later baseline replacements require a successful comparison
to the latest accepted predecessor and review. Post each full record as a durable PR comment
(gzip/base64 is suitable when needed), retain the raw artifacts in durable local evidence, and
update the index with the uncompressed TSV SHA-256 and the exact comment URL. Actions artifacts
expire after30days; they are not the baseline's sole location. A retrieval must decode the record
and reproduce its indexed digest. No command automatically resets the baseline.

An index-only finalization still requires native runs at the final head. They must reproduce the
accepted case identities/outcomes, including failure and skip sets. Review the intervening diff
to establish that only baseline/execution metadata changed; keep both source identities in the
PR. This avoids a record trying to contain its own commit SHA.

## Migration lint

`RepoChecks portability-rules .` checks the hosted progress/package-head workflow and the exact,
reasoned `.config/portability-migrations.json` inventory. `RepoChecks bash32-lint .` checks just
the Bash/BSD subset. Both run locally; the complete gate runs in CI.

The scanner is intentionally conservative: every matching file is hash-bound. C# files containing
OS-selection tokens and `return` require review even when returns are helper values rather than
early exits. Raw full/relative-path calls and `FullName` use are inventoried, including currently
safe enumerations. The Bash scanner covers common Bash4+/GNU-only constructs; it is not a shell
parser or portability proof. Multiline/compound guards, aliases in the same file and changes
within an inventoried file cannot silently inherit an older allowance. Changes must migrate the
site or update its reason under review; removed sites must remove stale entries. Tasks008/T006,
T013 andT014 tighten this inventory as the corresponding implementations land.

For diagnostics on any host, `macos-progress check-trx SUITE TRX DISCOVERY_LOG EXIT_CODE`
validates one suite's completion/identity without issuing a native record. Existing theories
use stable serializable keys where complex objects or truncated display names would hide cases;
all original input construction and assertions remain inside the tests.
