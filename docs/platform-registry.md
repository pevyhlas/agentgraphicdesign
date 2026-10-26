# Platform registry (development source)

Core owns one I/O-free D-28 registry. `get_capabilities.platform` embeds it unchanged;
`supportedPlatform` and `currentPlatformMatches` are removed, without aliases.
The member has ordinal object keys: `architecture`, `capabilities`, `os`, `osVersion`,
`profile`, `qualifiedProfiles`, `runtimeVersion`. Each capability has `capability`,
`qualification`, `requires`; each qualified profile has `capabilities`, `minOsVersion`
(including explicit null), `profile`, `requires`. Arrays keep enum/table order.
Architecture is the **process** architecture; Rosetta x64 is not arm64. Linux OS version
is the kernel version. There is no aggregate `qualified` flag or live availability probe.

## Stage S0

| Profile | vector / rasterExport | assetPipeline / artifactVerify | stillImport / nativePixels |
|---|---|---|---|
| .NET 10 Linux x64 | qualified | qualified | qualified |
| .NET 10 Windows x64 | unqualified | unqualified | refused |
| All other tuples | unqualified | refused | refused |

Unqualified means admitted best-effort, not supported. Refused means
`ASSET_PLATFORM_UNSUPPORTED` before any write, with the path-free registry text preserved
through CLI/MCP and storage wrappers. Windows native **reads** now refuse as well as writes;
Windows arm64 and runtime majors other than 10 no longer pass the guarded probe. Vector
storage and raster export remain unguarded. No macOS backend, APFS admission, live probe,
S1–S3 admission or new qualification is included.

## Developer checks

After a locked Release build, use RepoChecks (no added dependencies):

```sh
dotnet run --project scripts/RepoChecks --configuration Release -- platform-fixtures . --check
dotnet run --project scripts/RepoChecks --configuration Release -- platform-rules .
dotnet run --project scripts/RepoChecks --configuration Release -- self-test
dotnet run --project scripts/RepoChecks --configuration Release -- qualification-evidence .
```

Omit `--check` from `platform-fixtures` only for intentional fixture regeneration; tests
never regenerate their own expected bytes. The literal policy table is a separate oracle.

`.config/platform-p0-tests.json` inventories exact methods and theory-case counts for
Core, CLI, partial MCP `ProtocolTests`, and the four repository commands. Dedicated
platform classes are selected whole; shared classes use exact method filters.
`platform-p0-run ROOT FRESH_OUTPUT EXPECTED_SHA --macos` and `platform-p0-check` with the
same arguments enforce runtime 10, macOS 26 arm64, the exact checked-out PR head and an
inherited physical private directory under `/private/tmp`. The runner retains process/SDK/OS
identity, argv, exit codes, stdout/stderr, discovered identities and TRX. All expected cases
must execute with no failures/skips/not-executed outcomes; cancelled, timed-out and partial
runs never pass. Without `--macos`, this is local diagnostic verification, not native P0.
The separate P0 job does not replace the broad advisory macOS lane. The existing required
repository-gates job waits for P0 and explicitly fails unless its outcome is success, including
failure/cancellation/skips; this avoids relying on an unconfigured new remote check name.

## Public evidence bookkeeping

The six `.config/qualification-evidence.json` entries intentionally start with pending
source revisions and unique future ids in `specs/records.md`. **The real claim gate remains
blocked until fresh proof is recorded.** Do not replace pending values with a convenient
existing SHA or reconstruct historical results.

The driver records evidence only after inspecting change 023's source/installed equivalence
tables and final independent reviews. Each selected single-line `ATTEST` record binds these
fields together (no cross-record matching):

| Field | Binding |
|---|---|
| id | Entry's unique `attestationId` |
| by / what | `agent` or `owner` / `platform-qualification` |
| profile / capability | Entry's exact pair; pre-d10 is Linux x64 only |
| sourceCommit / version | Same exact evaluated public revision as the entry; all six agree |
| env | `dotnet10-linux-x64` |
| evaluator / date | Named evaluator and ISO date |
| result | Explicit whole-record result, not PASS text in explanatory prose |
| sourceEvidence / installedEvidence / reviewEvidence | GitHub HTTPS links or existing ignored `.prdkit/cache/evidence/` files |
| ac01 / ac02 / ac03 | Separate observed still-import runtime outcomes, each `PASS` |
| ac04 | Proposed023 correction: `PREMERGE_PROOF_PASS` for observed implementation proof, never future CI |

Field values are space-free; explanatory prose follows ` — `. Duplicate fields or selected
ids fail. Whole-line anchored patterns are evaluated only against that selected record.
Unrelated historical failures do not poison a later valid selected attestation. A selected
FAIL, failed-test line, missing proof, swapped pair or borrowed SHA cannot satisfy a claim.
`d10` records remain refused until 008/T015. The checker validates an index, not the truth
of linked execution evidence; independent reviewers must inspect the actual runs.

The proposed023 sequencing correction separates already-observed proof from final integration:
`PREMERGE_PROOF_PASS` means the approved design, full exact-source/installed suites, coverage,
dependency/audit/self-test/format/adoption/doctor checks and all five implementation reviews passed.
The evidence-index check and final integration cannot attest themselves in advance. Full original
005/AC-04 and merge eligibility remain incomplete until the final attestation-bearing head passes
complete native P0, every required CI job and the review gate. The always-run P0 dependency stays
required. Generic `ac04=PASS` is refused rather than silently redefined. No entry changes until this
bounded design/checker correction is reviewed; no d10 binding or qualification scope is added.
