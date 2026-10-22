# Artifact Input Preflight v1

**Status**: normative v1 contract, implemented and shipped. Authority:
[provided artifacts](../../specs/parts/p1-core-engine/provided-artifacts.md).
**Scope**: input integrity and PNG structure, not decoding or admission.

## Boundary

Add one synchronous-lifetime, read-only workflow over existing supplied-artifact
metadata and actual input files. Asynchronous reads do not create jobs. No SQLite,
receipt ledger, selection format, Git operations, source rewriting, payload storage,
native import, producer execution or consumer publication is included.

Full artifact admission still requires qualified complete decoding and containment.
Existing in-process Skia rendering does not establish that boundary. Preflight
never calls a codec or inflater and must not claim that a PNG can be decoded.
Corrupt compressed data can pass structural preflight if its outer checksums match.

## API and Files

`Core.Artifacts.ArtifactPreflight.CheckAsync(string descriptorPath, CancellationToken)`
returns an immutable bounded `ArtifactPreflightResult` or throws a structured
`DocumentEngineException`; cancellation propagates. Construction/registration causes
no I/O and no document store is used. Existing metadata APIs remain pure and unchanged.

The descriptor resides in a dedicated bundle directory. Its file name is the one
allowed descriptor entry; payload paths resolve relative to that directory. Parse
with `ArtifactDescriptorContract` and retain its owned metadata. No requirement for
a Git worktree or a particular descriptor filename/extension is added.

Require the exact file inventory: the descriptor, every declared PNG, and only
their necessary parent directories. Reject extra files/directories (including hidden
entries), missing entries, case aliases, wrong types, symlinks/reparse points and
special files. Reject a descriptor path that conflicts with a declared payload or
parent directory. Metadata bounds imply at most 32,769 entries, including directories
and descriptor; enumerate lazily with cancellation and reject unexpected entries
without materializing unbounded listings.

Use existing Core asset path inspection on the trusted, quiescent local filesystem.
Initial qualification is .NET 10 Linux x64; the existing Windows implementation is
not newly qualified here and other Unix configurations fail closed. Checks/open are
separate operations, not hostile replacement-safe confinement. Source hard links
are not ownership: only freshly read private bytes are used for subsequent checks.
Pause producers/Git writers. No atomic source-tree snapshot or continued validity
of supplier paths after the call is promised.

Read descriptor bytes once within 1 MiB, then each unique payload once into fresh
private memory, bounded before allocation and while reading. Require the claimed
length and EOF. Hash and inspect exactly that owned payload buffer, never reopen
the source for a later check. Do not expose buffers. Per-file encoded bytes <=64 MiB,
aggregate <=256 MiB, and all existing canvas/file/frame metadata limits apply.
Process unique files sequentially; repeated logical frames do not repeat reads.
No successful partial report is returned. Reads may update filesystem access times,
but create/change no file contents, mtimes, directory entries, markers or reports.

## PNG Structural Policy

Policy identifier: `agd.png-rgba8-structure.v1`. This explicitly narrow preflight
policy is separate from the metadata profile and from any future decoder identity.
Legal PNG variants outside it are unsupported, not silently converted.

- Exact PNG signature; checked chunk lengths/offsets; at most 4,096 chunks per file.
- Verify IEEE CRC-32 over each accepted chunk's type and data, with cancellation
  during long chunks. No native decoding/decompression or new dependency is needed.
- Exactly one first `IHDR`, length 13. Actual dimensions equal the descriptor canvas;
  bit depth 8, color type 6, compression/filter methods 0, interlace 0.
- Allow only `IHDR`, optional unique `sRGB`, optional unique `gAMA`, optional unique
  `cHRM`, a consecutive nonempty aggregate `IDAT` run, and one final empty `IEND`.
  Color chunks precede IDAT. No bytes may follow IEND. Zero-length IDAT chunks are
  allowed within the chunk count; at least one IDAT byte is required overall.
- `sRGB` has length 1 and rendering intent 0..3. `gAMA` has length 4 and value 45455.
  `cHRM` has length 32 and values 31270,32900,64000,33000,30000,60000,15000,6000.
  Unsupported color values/chunks (including ICC/HDR), APNG, text/EXIF and suggested
  palettes are rejected by this policy. Untagged samples retain the descriptor's
  unverified sRGB interpretation; no color conversion or producer-intent proof occurs.
- IDAT remains opaque. No zlib/Adler checksum, DEFLATE termination, row count/filter,
  pixel samples, hidden RGB or decoder behavior is verified. Do not hash pixels,
  generate a preview, normalize or re-encode inputs.

## Result and Errors

Success reports `metadataDigest`, `profile`, `assetId`, `width`, `height`, `fileCount`,
`frameCount` (one for a still) and actual `totalBytes`. Always include:

```json
{
  "verification": "encoded-integrity-and-png-structure",
  "pngPolicy": "agd.png-rgba8-structure.v1",
  "pixelsDecoded": false,
  "admitted": false,
  "provenanceVerified": false
}
```

The metadata digest keeps its original definition and name. Matching bytes does
not transform it into a verified artifact-set identity, preservation guarantee,
quality/rights certificate or selection authority. Results describe this call's
captured bytes, not a durable receipt or later path contents.

Use existing metadata error codes and `RESOURCE_LIMIT`, `PATH_ESCAPE`,
`DOCUMENT_NOT_FOUND`, `ASSET_PLATFORM_UNSUPPORTED` as applicable; add only
`ARTIFACT_INVENTORY_MISMATCH`, `ARTIFACT_CONTENT_MISMATCH`, `ARTIFACT_PNG_INVALID`
and existing `ARTIFACT_PROFILE_UNSUPPORTED` for the new checks. Normalize expected
I/O/access failures to `ARTIFACT_INPUT_UNAVAILABLE`. Error messages are bounded to
1024 characters, do not echo untrusted input/path data, and include the metadata
location when available. No arbitrary exception details are exposed by this service.

## Surfaces and Evidence

CLI: `agd assets preflight <DESCRIPTOR> [-f text|json]`. MCP:
`preflight_artifact(descriptorPath)` marked read-only, in `ArtifactTools` with only
`PathConfinement` as a dependency. MCP rejects lexical root escapes before path
probes and completes confinement before Core inventory/content reads; confinement
itself can inspect attributes. Core confines all payloads beneath the descriptor
directory. Both call the same service and
return the shared envelope. Text output explicitly says pixels are not decoded and
artifacts are not admitted. Invalid arguments/format/version use CLI exit 4, other
preflight failures exit 1, cancellation 130. No asset drift exit or repair mode.

Independent .NET fixtures cover still/clip/repeated references, literal PNG/CRC
vectors, header/profile/dimension/order/CRC/truncation/trailing-data failures,
allowed color tags and unsupported chunks, actual inventory/length/hash failures,
resource/cancellation limits, unsafe paths/types/case aliases and unchanged input
snapshots on success/failure. A deliberately invalid compressed stream with valid
outer CRC/hash must pass preflight while reporting `pixelsDecoded=false` and
`admitted=false`; this is a required regression against overstating its guarantee.
CLI/MCP and real stdio tests prove discovery, envelopes, confinement and no incidental
document-store initialization. Full vector/schema/manifest regressions remain.
