# Managed PNG Decoded-Sample Verification v1

**Status**: normative v1 contract, qualified on 2026-09-09 only for the bounded
managed-in-process profile on .NET 10 Linux x64 with unchanged file-probe and
trusted-filesystem limits; not an OS sandbox, admission or preservation qualification.
Authority: [provided artifacts](../../specs/parts/p1-core-engine/provided-artifacts.md).
**Scope**: read-only decoded-sample verification, not admission or preservation.

## Scope and Licensing

Keep AGD MIT with existing permissive dependencies and notices. Do not add SharpZipLib,
a native inflater, bubblewrap/systemd requirements, a sandbox framework, jobs, SQLite,
selection state or automatic Git operations. Implement one original managed C# decoder
for the already bounded RGBA8/noninterlaced PNG policy, not a general compression API.

The native zlib probe did not enforce all advertised smaller windows; inspected
SharpZipLib 1.4.2 source accepts invalid history references and has incomplete Huffman
validation. Neither is adopted for strict verification. Correctness is independently
qualified against RFC 1950/1951 and literal/malformed .NET fixtures, not inferred from
license permissiveness or successful pixel output.

## Trust and Resource Boundary

This is an explicitly **managed-in-process** operation over potentially malformed
PNG bytes in a trusted, quiescent local filesystem. The caller chooses the operation;
descriptor claims cannot select execution policy. Trust reviewed managed code, CLR
and host. It is not an OS sandbox, credential boundary, process-RSS limit, hard
deadline or process-crash guarantee. The future admission lane retains its separate
decoder-containment, sealing and preservation requirements. No native-codec fallback.

Reuse the existing bounded inventory/capture/hash/structure workflow through a small
internal runner; never preflight a path and reopen it for decoding. Public structural
preflight remains unchanged and still accepts opaque invalid IDAT. Each unique PNG
is captured once; hash, structure and decoding consume that same private buffer.
Files are sequential, repeated frames do not repeat decoding, no partial successful
report or pixel buffers escape, and the operation writes nothing.

Retain existing metadata, inventory, encoded-file/bundle and decoded-pixel bounds.
Add a 60-second cooperative deadline for the entire verification operation and one
active verification under a host-lifetime shared Core guard, acquired before capture;
retain it until work unwinds and release on every exit. Reject competing calls immediately
with `ARTIFACT_VERIFY_BUSY`, without a queue. Check cancellation/deadline before I/O,
between existing bounded capture/hash/CRC steps and throughout decompression/filtering.
Limit DEFLATE blocks to 65,536 per unique PNG, including empty blocks. This is an
explicit verification resource limit, not a PNG validity rule. Budget exhaustion uses
`RESOURCE_LIMIT`; do not abandon ongoing work and claim it was forcibly stopped.

Use one captured encoded buffer, a fixed 32 KiB history, two sample rows of at most
32 KiB each, and bounded reusable Huffman arrays. No full-image inflated/RGBA buffer,
IDAT concatenation buffer, recursion or input-sized lookup table. Output is bounded by
`height * (1 + 4 * width)` checked filtered bytes; the maximum is 67,117,056 bytes,
not exactly 64 MiB. Check work at every block and at most every 1,024 emitted bytes
or decoded symbols, whichever threshold is reached first, plus during row
reconstruction/hashing. These checks are
cooperative; filesystem calls, CLR pauses and hash implementations are not hard-preempted.

## Complete Stream and Samples

Reuse `agd.png-rgba8-structure.v1` without changing its chunk/color allowlist or the
provided-artifact metadata profile. Traverse validated IDAT framing without a second
CRC implementation, treating consecutive payloads as one byte stream, including
zero-length chunks and arbitrary splits. Keep bytes and traversal in one owned call.

Require RFC 1950 CM=8, CINFO=0..7, valid FCHECK and no preset dictionary. Enforce the
advertised window size as well as the 32 KiB maximum. Support RFC 1951 stored, fixed
and dynamic blocks; verify stored LEN/NLEN, bounded counts/repeats, canonical Huffman
construction and EOB. Reject oversubscribed trees and invalid code lookups. Code-length
trees must be complete; literal/distance trees may use the legal single-symbol
one-bit case; reject all other incomplete trees. An empty distance tree is usable
only when that block executes no match, never as a zero-bit distance symbol. Require
literal/length symbol 256. Reject reserved literal/length or distance symbols when
decoded. This strict profile requires length 258 to use code 285; reject code 284
with extra value 31, even though some permissive decoders accept that representation.
Distance must not exceed bytes already produced or the advertised window;
overlapping valid copies are supported. Reset no history across members because
exactly one member is permitted.

Consume actual final-block termination, discard only residual alignment bits to the
next byte boundary (without requiring their values to be zero), and consume all four
Adler-32 bytes. Adler covers the filtered output, including filter bytes. Require
exact output length and no remaining IDAT payload byte, including an empty or
nonempty second zlib member. End-of-input or filled output alone is not success.

Reverse PNG filters 0..4 with four-byte stride, modulo-256 addition, zero unavailable
neighbors, correct Average floor and Paeth ties. The DEFLATE history retains original
filtered bytes, not reconstructed samples. Hash RGBA sample rows in order using plain
SHA-256, excluding filter bytes. No gamma conversion, alpha premultiplication,
transparent-RGB cleanup, resizing, rendering or re-encoding occurs. The descriptor's
sRGB/straight-alpha interpretation is applied, not proof of the producer's intent.

## API and Surfaces

Add `Core.Artifacts.ArtifactVerification.CheckAsync(string descriptorPath,
CancellationToken cancellationToken = default)` returning immutable bounded facts.
Use the existing fail-first structured exception model and `$.files[index]` locations.
Malformed compressed data uses `ARTIFACT_PNG_INVALID`; cancellation propagates.
Keep existing path/input errors and bounded non-echoing diagnostics.

CLI: `agd assets verify <DESCRIPTOR> [-f text|json]`. MCP: read-only
`verify_artifact(descriptorPath)` in `ArtifactTools`, still storeless and confined.
Both call the same Core service and keep existing envelope/exit conventions.

Return flat metadata facts matching preflight (`metadataDigest`, `profile`, `assetId`,
`width`, `height`, `fileCount`, `frameCount`, `totalBytes`) and unique-file records
`fileSha256` / `rgbaSha256`, sorted ordinally by encoded hash. At most 4,096 records;
no absolute paths, original metadata blob or pixels are returned. Always include:

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

Metadata digest retains its existing definition. Raw RGBA hashes are observations
under the explicit dimensions/profile, not new global artifact identities, signed
proof, source-rights approval, retained payloads or a project selection.

## Required Evidence

Independent fixtures cover all filters, Paeth ties and boundary samples, hidden RGB
under alpha zero, stored/fixed/dynamic/multiblock streams, valid overlap, valid small
windows, IDAT splits, every truncation of small members, checksum/trailing/member
failures, invalid histories, reserved codes, malformed/incomplete/oversubscribed trees
and repeats, invalid filters and short/excess output. Include the source-derived
invalid-history member `78 9c 03 03 00 00 05 00 01`; do not treat matching Adler/size as
sufficient. Test block/memory bounds, cooperative cancellation/deadline callbacks,
busy rejection, no partial reports, original file indexes and unchanged inputs.
Include successful singleton/empty-distance blocks, an in-history match exceeding a
smaller advertised window, nonzero alignment bits, legal repeats crossing the
literal/distance boundary, and guard release after failure/cancellation.

CLI/MCP and installed-tool tests must exercise successful raw sample hashes and the
distinction between successful structural preflight and failed decoded verification
for matching-CRC/hash invalid IDAT. Preserve the full existing regression baseline.
Implementation review and qualification for this bounded managed profile completed on
2026-09-09. That evidence does not qualify later native pixel storage or evaluation.
