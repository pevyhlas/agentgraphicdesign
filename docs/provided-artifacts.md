# Provided Artifacts

The current capability (shipped in 0.2.0-preview.2 and included in published 0.3.0) combines the **pure metadata
contract** with two separate read-only operations: `agd assets preflight` / MCP
`preflight_artifact` for encoded integrity and PNG structure, and `agd assets verify`
/ MCP `verify_artifact` for managed decoded RGBA8 sample verification. Public
preflight is unchanged and keeps IDAT opaque. These are the bounded P7 checks (R-07), not
external artifact admission (R-11), an image importer, Blender integration or a native sprite editor.
There is no producer registry, sealing, payload preservation or file admission path.

## Metadata Library API

Namespace: `FTPlus.AgentGraphicDesign.Core.Artifacts`.

```csharp
using System.Text;
using FTPlus.AgentGraphicDesign.Core.Artifacts;

var result = ArtifactDescriptorContract.Parse(Encoding.UTF8.GetBytes(json));
if (!result.Success)
    throw new InvalidOperationException($"{result.Error!.Code}: {result.Error.Message}");

var canonicalMetadata = ArtifactDescriptorContract.SerializeCanonical(result.Descriptor!);
var metadataDigest = ArtifactDescriptorContract.ComputeMetadataDigest(result.Descriptor!);
```

`Parse(ReadOnlyMemory<byte>)` copies input and succeeds only after all lexical,
structural, semantic, aggregate and canonical-size checks. `Validate` returns the
first bounded `ValidationError` or null. Canonicalization rejects invalid programmatic
models with `DocumentEngineException`. Use this contract's canonical writer, not a
general `JsonSerializer.Serialize` call, when computing its metadata identity.

Success proves **valid unverified claims only**. A claimed PNG need not exist. No
path is opened/resolved, no image is decoded, and no hash is checked against a file.
The metadata digest is neither a verified artifact-set identity nor admission,
quality, source-rights or selected-result evidence.

## Profile

The separate [schema](../schemas/agd-provided-artifact-v1.schema.json) has the identifier
`urn:agd:provided-artifact:1`; it is not fetched at runtime. The exact
[contract](contracts/provided-artifact-v1.md)
defines all fields, limits and canonical byte rules. Independent
[still](../tests/FTPlus.AgentGraphicDesign.Core.Tests/Artifacts/Fixtures/valid-still.json)
and [clip](../tests/FTPlus.AgentGraphicDesign.Core.Tests/Artifacts/Fixtures/valid-clip.json)
fixtures contain deliberately nonexistent payload claims.

- `schema`: `agd.provided-artifact`; integer `version`: `1`.
- `profile`: `png8-rgba-srgb-straight-fixed-canvas-v1`.
- Fixed untrimmed canvas; claimed PNG RGBA8/sRGB/straight alpha; zero crop origin.
- Either one untimed still or one clip/view/variant tuple with explicitly ordered
  frames and positive integer tick durations. `30000` ticks/second with `1001`-tick
  frames preserves `30000/1001` FPS exactly; no playback evaluator is included.
- Explicit decimal edge-space pivots, bounded to six fractional digits; no rounding
  of unsupported input. These profile limits are not the future native editor's ceiling.
- Unique file hash/path claims, complete references and distinct logical frame names.
  Repeated file references do not imply native cel links.
- Optional producer/recipe-snapshot provenance is inert, unverified data, not a live
  dependency or authority to run a program.

Input and canonical output are limited to 1 MiB. Objects are closed, nulls and decoded
duplicate keys are rejected, and raw-token/depth/count/arithmetic bounds apply.
Paths use a restricted portable lowercase ASCII grammar; this lexical validation
does not establish filesystem safety. Limits and still/clip requirements are independent
of the existing vector serializer and asset-manifest v1 grammar, which are unchanged.

## Canonical Identity

Canonical bytes are compact UTF-8 without BOM or final newline, with fixed property
order and literal validated ASCII strings. File claims sort ordinally by hash;
frame order is retained. Numbers use the contract's exact invariant representation.

The lowercase digest is SHA-256 of the ASCII domain prefix
`agd.provided-artifact.canonical-metadata.v1` followed by one LF byte and the canonical
JSON. Whitespace, object/inventory order and equivalent permitted pivot spellings
do not change it. Logical order, names/paths, timing, claims and provenance do.
Changing real payload bytes without updating their claims is invisible to this
metadata API; preflight and verification separately check actual bytes against
those claims.

JSON Schema only expresses part of the contract. For example, mathematical integer
values `1.0`/`1e0` may satisfy schema shape but violate the parser's integer-token
grammar. Duplicate keys, reference integrity, aggregates and canonical identity need
the contract implementation. Test-only schema evaluation is deliberately bounded
and rejects unsupported keywords/forms; it is not a production general schema engine.

Errors use `PARSE_ERROR`, `INVALID_ARGUMENTS`, `UNSUPPORTED_VERSION`,
`ARTIFACT_PROFILE_UNSUPPORTED`, or `RESOURCE_LIMIT`. Messages and locations are bounded
and do not echo complete input. Semantic failures identify the metadata location;
syntax/shape failures may point to `$`.

## Input Preflight

```text
agd assets preflight <DESCRIPTOR> [-f text|json]
preflight_artifact(descriptorPath)
```

Both surfaces call `ArtifactPreflight.CheckAsync(string descriptorPath,
CancellationToken)` in the same Core namespace. It returns an immutable
`ArtifactPreflightResult` or throws a structured `DocumentEngineException`;
cancellation propagates. Existing `Parse`, `Validate`, `SerializeCanonical` and
`ComputeMetadataDigest` APIs remain pure and unchanged. This is a single-call
workflow, not a background job, and it uses no document store.

### Bundle Layout and Example

Use a dedicated directory containing **exactly the descriptor, every declared PNG,
and only their necessary parent directories**. Payload paths are relative to that
directory, not the current shell directory or the MCP root. For example:

```text
artwork/
  hero/
    descriptor.json
    frames/
      idle-000.png
```

Here the descriptor declares `frames/idle-000.png`. The descriptor's actual filename
is the only allowed descriptor entry; no particular name or extension is required.
Reject extra files or directories, including hidden files, empty unused directories,
sidecars and reports; also reject missing entries, case aliases, wrong entry types,
symlinks/reparse points, special files, and descriptor/payload/parent conflicts.
Keep source recipes and other project files outside the dedicated bundle.

Use a project-managed, tracked location such as `artwork/hero/`, **not this
repository's ignored `artifacts/` directory**. Preserve required and nonreproducible
payloads, even generated artwork, with the descriptor in Git, Git LFS or another
durable pinned store. A digest or recipe cannot recover missing bytes. Tracking is
the user's responsibility: AGD neither requires a Git worktree nor runs Git or
edits `.gitignore`. See the [Git workflow](git-workflow.md).

For a concrete small payload, the Core fixture
[`preflight-rgba.png.base64`](../tests/FTPlus.AgentGraphicDesign.Core.Tests/Artifacts/Fixtures/preflight-rgba.png.base64)
contains literal Base64 for a 73-byte, 1x1 RGBA8 PNG; the matching
[`preflight-bundle.json`](../tests/FTPlus.AgentGraphicDesign.Core.Tests/Artifacts/Fixtures/preflight-bundle.json)
has its actual length and SHA-256 claim. That fixture declares `image.png`; the
layout above instead requires `files[0].path` to be `frames/idle-000.png`.
The [.NET fixture setup](../tests/FTPlus.AgentGraphicDesign.Core.Tests/Artifacts/ArtifactPreflightTests.cs)
materializes the binary payload in a dedicated directory. Base64 is test-fixture
storage, not a production handoff format or a requirement for external Base64 tools.
Do not preflight the shared fixture directory itself: its unrelated files violate
the exact-inventory rule.

With the matching descriptor and binary PNG already present, pause all producers
and Git writers before running:

```sh
agd assets preflight artwork/hero/descriptor.json
agd assets preflight artwork/hero/descriptor.json -f json
```

The MCP equivalent is `preflight_artifact` with
`{"descriptorPath":"artwork/hero/descriptor.json"}` under the configured working
directory. It is marked read-only. MCP rejects lexical root escapes before path
probes and completes confinement before Core inventory/content reads; confinement
itself can inspect attributes. Core confines payloads beneath the bundle directory.
`assets build` and `assets check` remain CLI-only manifest batch workflows; preflight
does not build, check drift, repair or publish their outputs.

### Checks and Limits

The shared bounded read path inspects file types and lengths before allocation,
reads the descriptor once within 1 MiB, and captures each unique payload once into
fresh private memory. Bounds are enforced before allocation and while reading,
with cancellation checks throughout. It requires the claimed length and EOF, then
computes actual SHA-256 and checks PNG structure on **the same captured bytes**,
never reopening a payload for a later check. Unique files are processed sequentially;
repeated logical frame references do not repeat reads. No buffers or successful
partial report are exposed.

- Encoded payload limits: 64 MiB per unique file, 256 MiB aggregate.
- At most 4,096 unique files, 4,096 logical clip frames and 4,096 PNG chunks per file.
- At most 32,769 inventory entries, including the descriptor and necessary directories;
  enumeration is lazy and rejects unexpected entries without walking their subtrees.
- Metadata profile `png8-rgba-srgb-straight-fixed-canvas-v1`: width/height 1..8192,
  canvas area at most 16,777,216; all existing lexical, timing, pivot and aggregate
  declared-pixel-work limits still apply. These bounds do not imply pixel decoding.

PNG policy **`agd.png-rgba8-structure.v1`** checks the signature, bounded chunk framing,
IEEE CRC-32 over each accepted chunk's type and data, and an initial unique `IHDR`
whose dimensions match the descriptor: RGBA8, compression/filter methods 0, no
interlace. The allowlist is `IHDR`, optional unique `sRGB`/`gAMA`/`cHRM` before IDAT,
a consecutive IDAT run with nonempty aggregate data, and a final empty `IEND` with
no trailing bytes. Only the contract's sRGB-compatible color tag values are accepted.
ICC/HDR, APNG, text/EXIF, suggested palettes and other unsupported chunks or legal
PNG variants are rejected, not converted. Untagged data retains the descriptor's
unverified sRGB interpretation, not proof of producer intent. Exact tag values and
remaining constants are in the reviewed [preflight contract](contracts/artifact-preflight-v1.md).

**In preflight, IDAT is opaque: no codec or inflater is called.** Zlib/Adler checksums, DEFLATE
termination, row counts/filters, pixel samples, hidden RGB and full PNG decodability
are not validated. Invalid compressed data with matching outer CRCs, length and
SHA-256 can pass. Preflight returns no pixel hash, preview, color conversion or
re-encoding. Use the separate [decoded-sample verification](#decoded-sample-verification)
operation when compressed-stream and raw sample checks are required.

### Result and Errors

CLI JSON and MCP use the shared `{ "ok": true, "data": ... }` envelope. Minimal
success excerpt (the remaining result fields are omitted here, not by the API):

```json
{
  "ok": true,
  "data": {
    "verification": "encoded-integrity-and-png-structure",
    "pngPolicy": "agd.png-rgba8-structure.v1",
    "pixelsDecoded": false,
    "admitted": false,
    "provenanceVerified": false
  }
}
```

The full result also includes `metadataDigest`, `profile`, `assetId`, `width`,
`height`, `fileCount`, `frameCount` (one for a still), and actual `totalBytes` across
unique PNG files, excluding the descriptor. Text output explicitly says pixels
are not decoded, artifacts are not admitted and provenance is not verified.
The descriptor's digest is still named `metadataDigest`, with its original
canonical-metadata definition. It is **not a verified artifact-set identity**, a
preservation receipt, a quality/rights certificate or selection authority.

Failures return `{ "ok": false, "error": { "code": "...", "message": "..." } }`.
MCP supplies the envelope in both `structuredContent` and a text block, with `isError`
set for failures. Core messages are bounded to 1,024 characters, do not echo
untrusted paths/input or arbitrary exception details, and include the metadata
location in the message when available; a separate `error.path` is not guaranteed.

| Code | Meaning |
|------|---------|
| `PARSE_ERROR`, `INVALID_ARGUMENTS`, `UNSUPPORTED_VERSION` | Existing metadata syntax/shape, semantic or version errors; `INVALID_ARGUMENTS` also covers invalid Core/CLI input paths |
| `INVALID_FORMAT` | CLI output format is not `text` or `json` |
| `ARTIFACT_INVENTORY_MISMATCH` | Missing, extra, case-aliased or conflicting bundle entries |
| `ARTIFACT_CONTENT_MISMATCH` | Actual length/SHA-256 differs from the claim, or length changes while reading |
| `ARTIFACT_PNG_INVALID` | Invalid PNG framing, CRC, dimensions or required chunk structure |
| `ARTIFACT_PROFILE_UNSUPPORTED` | Unsupported metadata profile, PNG variant, chunk or color value |
| `RESOURCE_LIMIT` | A metadata, encoded-byte, inventory, chunk or path bound is exceeded |
| `PATH_ESCAPE` | MCP path/confinement rejection, links/special files or wrong required entry type; invalid MCP paths can fail here before Core |
| `DOCUMENT_NOT_FOUND` | Required input is absent at path inspection, including a missing descriptor |
| `ASSET_PLATFORM_UNSUPPORTED` | File-type inspection is unsupported on this platform |
| `ARTIFACT_INPUT_UNAVAILABLE` | Expected I/O or access failure during MCP confinement probes or Core inspection/read |
| `CANCELLED` | Operation cancelled |

CLI exits: `0` on success; `4` for `INVALID_ARGUMENTS`, `INVALID_FORMAT` or
`UNSUPPORTED_VERSION`; `1` for other preflight failures (including `PARSE_ERROR` and
unsupported profiles); `130` for cancellation. There is no asset-drift exit `6` or
repair mode. MCP path/confinement rejections use `PATH_ESCAPE`; probe I/O/access
failures use `ARTIFACT_INPUT_UNAVAILABLE`. The adapter uses
`ARTIFACT_PREFLIGHT_FAILED` for other expected failures without a more specific code.

## Decoded-Sample Verification

```text
agd assets verify <DESCRIPTOR> [-f text|json]
verify_artifact(descriptorPath)
```

Use the same [dedicated bundle layout](#bundle-layout-and-example), with existing
descriptor and PNG files and all producers/Git writers paused:

```sh
agd assets verify artwork/hero/descriptor.json -f json
agd assets verify --help
```

Text is the default; `-f` / `--format` accepts `text` or `json`. There is no output-file
or repair option. MCP `verify_artifact` accepts
`{"descriptorPath":"artwork/hero/descriptor.json"}`, is read-only, and uses the same
confinement as `preflight_artifact`, without initializing a document store. Both
verification surfaces call the separate Core API:

```csharp
using FTPlus.AgentGraphicDesign.Core.Artifacts;

var result = await ArtifactVerification.CheckAsync("artwork/hero/descriptor.json");
foreach (var file in result.Files)
    Console.WriteLine($"{file.FileSha256}: {file.RgbaSha256}");
```

`CheckAsync(string descriptorPath, CancellationToken cancellationToken = default)`
returns immutable `ArtifactVerificationResult` / `ArtifactPixelDigest` facts or
throws a structured `DocumentEngineException`; caller cancellation propagates.
It runs inventory, capture, length/SHA-256, CRC/structure and decoding in one owned
call. Each unique PNG is captured once, and **hash, CRC and decoding consume that
same private buffer**. It does not preflight a path and reopen it for decoding.
Files are processed sequentially; repeated frames do not repeat reads or decoding.

### Stream and Sample Checks

The original managed C# decoder keeps the `agd.png-rgba8-structure.v1`
RGBA8/noninterlaced chunk/color allowlist. Consecutive IDAT payloads form one stream,
including empty chunks and arbitrary splits. Strict RFC 1950/1951 validation covers
zlib headers/FCHECK, no preset dictionary, advertised windows (including smaller
windows), valid history/overlap, and stored/fixed/dynamic DEFLATE with Huffman checks.
The strict profile requires length 258 to use code 285; code 284 with extra value 31
is rejected, even though some permissive decoders accept that representation.
Success requires actual final-block termination, the full Adler-32 checksum over
filtered bytes, exactly `height * (1 + 4 * width)` output bytes, and no trailing IDAT
bytes or second zlib member. Filling the output alone is not success.

All PNG row filters 0..4 (None, Sub, Up, Average, Paeth) are reversed. Plain SHA-256
covers reconstructed RGBA8 rows in order, excluding filter bytes, with **no alpha
premultiplication, hidden-RGB clearing, color conversion, resizing or re-encoding**.
RGB under zero alpha contributes to the hash; the descriptor's sRGB/straight-alpha
interpretation remains a claim, not proof of producer intent. The reviewed
[managed verification contract](contracts/managed-png-verification-v1.md)
defines the detailed stream rules and required qualification evidence.

For example, an otherwise allowed PNG whose IDAT contains `78 01 07 00 00 00 00`
(a reserved DEFLATE block type), with correct chunk CRCs and matching descriptor
length/SHA-256, passes `assets preflight` but fails `assets verify` with
`ARTIFACT_PNG_INVALID` (CLI exit `1`). Neither a matching file hash nor successful
structural preflight proves decodability; verification returns no partial success
even if an earlier file decoded successfully.

### Verification Result

The shared `{ "ok": true, "data": ... }` envelope contains the same eight flat
metadata facts as preflight: `metadataDigest`, `profile`, `assetId`, `width`, `height`,
`fileCount`, `frameCount`, `totalBytes`. Alongside them, `files` contains at most
4,096 unique-file records, each with only `fileSha256` (verified encoded-byte hash)
and `rgbaSha256` (raw sample hash), sorted ordinally by `fileSha256`. No absolute
paths, original metadata blob or pixel buffers are returned. The result always
includes these six qualifiers (excerpt, not the complete result):

```json
{
  "verification": "decoded-rgba8",
  "decoderProfile": "agd.png-rgba8-managed.v1",
  "execution": "managed-in-process",
  "pixelsDecoded": true,
  "admitted": false,
  "provenanceVerified": false
}
```

Text output prints both hashes per file and the managed-execution/non-admission
qualifiers. The unchanged `metadataDigest` and raw RGBA hashes are bounded observations
under explicit dimensions/profile, **not a new global artifact identity**, preservation
certificate, retained payload ownership, source-rights/quality approval or project selection.

### Verification Bounds and Errors

All existing [input bounds](#checks-and-limits) remain: 1 MiB metadata, 64 MiB per
encoded PNG, 256 MiB aggregate, and the inventory, chunk, canvas, frame and declared
pixel-work limits. Verification additionally allows only one active call per host,
using a shared Core guard acquired before capture and held until work unwinds.
Competing calls fail immediately with `ARTIFACT_VERIFY_BUSY`; there is no queue or
cross-process filesystem lock. The guard is released on every exit.

A **60-second cooperative deadline covers the whole Core verification call**, not
just decoding, with checks throughout I/O, capture/hash/CRC and decompression/filtering.
Each unique PNG may contain at most 65,536 DEFLATE blocks, including empty blocks.
This is a resource policy, not a PNG validity rule; deadline or block-budget exhaustion
returns `RESOURCE_LIMIT`. Exact check intervals are in the reviewed contract.

Decoder working storage consists of one captured encoded buffer, a fixed 32 KiB
history, two sample rows of at most 32 KiB each, and bounded reusable Huffman arrays.
There is no full-image inflated/RGBA or concatenated IDAT buffer. The filtered-output
bound is 67,117,056 bytes, not exactly 64 MiB. These are algorithmic limits, **not a
process-RSS bound or hard deadline**: filesystem calls, CLR pauses and hashing are
not hard-preempted. Execution trusts reviewed code, the CLR and host; it is not an
OS sandbox, credential boundary or process-crash guarantee.

Existing [preflight errors and envelopes](#result-and-errors) apply.
`ARTIFACT_PNG_INVALID` additionally covers malformed compressed streams, invalid
filters and short/excess decoded output; per-file failures retain original
`$.files[index]` locations in bounded, non-echoing messages. MCP uses
`ARTIFACT_VERIFY_FAILED` for other expected failures without a more specific code.
CLI exits remain `0` on success, `4` for invalid arguments/format/version, `1` for
other failures (including busy/resource limits), and `130` for cancellation, never
asset-drift exit `6`.

## Filesystem and Non-Guarantees

Both operations require a **trusted, quiescent local filesystem**. They reuse the
existing .NET 10 Linux x64-qualified file inspection; the existing Windows implementation
is not newly qualified by this slice, and other Unix configurations fail closed.
Inspection and opening are separate operations, not hostile replacement-safe
confinement. Source hard links do not confer ownership: only freshly captured
private bytes are used for checks.

Pause producers and Git checkout/restore/merge writers throughout the call. Git
does not provide an atomic source-tree snapshot or participate in AGD's locks.
Results describe this call's captured bytes, not durable validity or later contents
at the supplier's paths. Rerun after restoring/changing inputs; retain the required
payloads independently of any saved report.

Neither operation creates or changes file contents, mtimes, directory entries, lock
markers or report files; reads may update filesystem access times. They add no jobs,
selection format, SQLite store, Git operations, cache, receipt ledger, payload
preservation, sealing, admission, native import, producer execution or publication.
No new dependencies or formats are added; document/manifest and artifact metadata
formats remain unchanged, as does the MCP resource inventory.

The verifier is original AGD C# code under MIT, with no external package, native
codec/inflater or fallback added. MIT-compatible permissive dependencies are already
allowed by the current policy, subject to review and their own licenses/notices;
this is not a new exception. No copyleft host tools or OS sandbox requirement are
introduced, and supported workflows remain .NET/C#, not Python.

## Package Qualification

`scripts/verify-packages.sh` selects artifact preflight/verification and native CLI
cases against exact installed shims (pre-cancelled command cases remain in-process).
MCP filters exercise discovery, schemas, read-only/storeless artifact calls, native
guards and readiness vector/native guide journeys. It also verifies approved-bundle
bootstrap and mixed SVG/PNG/sprite manifest build/check. Coverage includes raw sample
hashes, invalid-IDAT contrast and decoded inline PNGs, not artifact admission.
`AGD_TEST_CLI_EXECUTABLE` and `AGD_TEST_MCP_EXECUTABLE` are test-only absolute existing
executable paths, not production settings. Actual candidate results and source/package
identities belong to the feature verification record; selecting a filter is not
passing evidence or a new platform qualification.

## Next Boundary

External production, receiver-owned sealed artifacts, admission-lane decoder
containment/qualification, durable workflow-specific jobs/retries, coherent publication
and native sequence import remain later work (R-11 in [requirements](../specs/product/requirements.md)).
Chosen asset references belong in tracked project declarations with preserved
payloads; a standalone selection database or generic receipt ledger is not a
prerequisite. Neither preflight nor managed verification delivers external artifact admission (R-11)
or preserves payloads.
Blender production belongs to a separate external producer,
AgentBlender; the shared descriptor/profile conformance
and eventual AGD admission contract remain open (R-11 and D-09 in `specs/product/requirements.md` and `specs/architecture/decisions.md`). This changes no dependency approvals
and is not a 0.3 release.
