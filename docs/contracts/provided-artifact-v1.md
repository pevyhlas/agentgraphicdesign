# Provided Artifact Metadata Contract v1

**Status**: normative v1 contract, implemented and shipped; no admission or native-format
approval. Authority: [provided artifacts](../../specs/parts/p1-core-engine/provided-artifacts.md).
**Scope**: metadata parsing, validation and canonicalization only, not artifact admission.

## Scope

The library accepts a descriptor's unverified claims for either a still image or
one clip/view/variant tuple. It does not open files, decode PNGs, traverse dependencies,
create/select/admit artifacts, publish output, mutate native documents or run a producer.
No CLI/MCP registration, vector format, asset-manifest v1 or dependency changes.

The intended pure Core API is `ArtifactDescriptorContract.Parse(ReadOnlyMemory<byte>)`,
`Validate(ProvidedArtifactDescriptor)`, `SerializeCanonical(ProvidedArtifactDescriptor)`
and `ComputeMetadataDigest(ProvidedArtifactDescriptor)`. Parsed collections are owned
and immutable. Canonicalization rejects invalid programmatic models. Results and XML
documentation must say metadata validity, not verified image/asset identity.

Successful `Parse` means every lexical, structural, semantic, aggregate and canonical-
size metadata check passed; failure exposes no successful descriptor. `Validate`
returns the first `ValidationError` or null. Parse results contain that error or an
owned descriptor. Error messages are bounded to 1024 characters and locations to
512, with explicit truncation; syntax/token failures may use `$` and a byte offset.
Serialization/digest computation throws `DocumentEngineException` for invalid models
and returns no successful bytes/digest. Validation and canonical writing share the
same invariants; `Parse(SerializeCanonical(validModel))` must succeed.

## Closed Shape

Every object rejects unknown/duplicate fields and explicit null. Required fields
are listed below; only `provenance` and its indicated members are optional. Order
within this table also defines canonical property order.

| Object | Properties in canonical order |
|--------|-------------------------------|
| Root | `schema`, `version`, `profile`, `assetId`, `canvas`, `cropOrigin`, `files`, exactly one of `still` or `clip`, optional `provenance` |
| Canvas | `width`, `height`, `coordinates` |
| Crop origin | `x`, `y` |
| File claim | `sha256`, `path`, `byteLength` |
| Still | `name`, `fileSha256`, `pivot` |
| Clip | `id`, `viewId`, `variantId`, `ticksPerSecond`, `playback`, `frames` |
| Frame | `name`, `fileSha256`, `durationTicks`, `pivot` |
| Pivot | `x`, `y` |
| Provenance | optional `producer`, optional `recipeSnapshotSha256`; at least one present |
| Producer | `id`, `version` |

Root literals: `schema` is `agd.provided-artifact`, `version` is integer `1`, and
`profile` is `png8-rgba-srgb-straight-fixed-canvas-v1`. `assetId` is a provider-scoped
logical identifier, not a globally registered asset. No self-hash is stored.
The separate JSON Schema describes structural/value constraints; lexical integer/
precision rules, duplicate keys, graph relationships and aggregates need the parser.
Neither schema validation nor metadata hashing verifies actual payloads.

## Interpretation

The profile claims ordinary noninterlaced PNG, 8-bit color type 6 RGBA, root canvas
dimensions for every file, sRGB transfer for RGB, straight alpha as linear coverage,
and no implicit removal of hidden RGB. Actual PNG headers/chunks and decoder behavior
remain unchecked until a later admitted/qualified profile verifies them.

`canvas.coordinates` is `top-left-pixel-edges-x-right-y-down`. Pixel `(i,j)` occupies
`[i,i+1) x [j,j+1)` and its center is `(i+0.5,j+0.5)`. Pivots are continuous edge-space
coordinates in the untrimmed canvas, allowed outside it within numeric bounds.
`cropOrigin.x/y` are required integer zero: this profile does not describe trimmed,
rotated, layered, atlas or per-frame-size content. This is not a universal native
AGD coordinate/precision ceiling. Unsupported profiles require review, not coercion.

Files are a set of unique claimed hashes and unique portable paths; frames reference
hashes. Every file is referenced. Reusing a file in distinct named frames preserves
logical frame identity and does not imply linked native cel edits. A still has exactly
one file and no timing fields. One-frame clips remain clips, not stills.

For clips, one tick is exactly `1 / ticksPerSecond` seconds; positive integer frame
durations and array order are authoritative. Derived starts are checked prefix sums
with half-open intervals. `playback` is `forward-loop` or `forward-once-hold`: a loop
wraps at total duration, while once-hold retains its final frame. Negative time is
outside this profile. The library validates metadata but implements no evaluator.
30000 ticks/second and 1001-tick frames preserve 30000/1001 FPS without rounding.

Provenance is optional and unverified. `recipeSnapshotSha256` is a claim about recipe
snapshot bytes, not an executable recipe key, verified source closure, live dependency,
licensing approval or execution authority. Producer ID/version are data only. Missing
provenance remains unknown. Selection, admission, rights and jobs are separate future
contracts; fields claiming those capabilities are rejected here.

## Bounds and Lexical Rules

| Quantity | Inclusive bound |
|----------|-----------------|
| Input and canonical output | At most 1 MiB UTF-8 each, including programmatic models; no BOM/comments/trailing commas/trailing documents; valid Unicode |
| JSON depth / raw tokens | 8 containers; strings 1536 raw UTF-8 bytes; numbers 32 raw bytes |
| Logical IDs and frame names | `[a-z][a-z0-9_-]{0,63}`; identifiers, not localized display text |
| Producer version | `[A-Za-z0-9][A-Za-z0-9.+_-]{0,63}` |
| Path | 240 lowercase ASCII characters, at most 8 segments, at most 64 per segment |
| Files / clip frames | 1 through 4096 each |
| Width / height / canvas area | 1 through 8192 each; area at most 16,777,216 |
| Declared bytes | 1 through 67,108,864 per unique file; sum at most 268,435,456 |
| Declared pixel work | Unique files x area at most 67,108,864; logical frames x area at most 268,435,456 |
| Tick rate | 1 through 1,000,000,000 |
| Duration ticks | 1 through 9,007,199,254,740,991 individually and in sum |
| Pivot coordinate | -1,000,000 through 1,000,000; at most six fractional digits, no exponent |

Integer fields use digit-only lexical tokens (no sign, fractional part or exponent).
Pivots use exact decimal syntax, permitting a minus sign and up to six fractional
digits; validate precision before conversion, never round into range. Normalize
negative zero only during canonical output. All aggregates use checked Int64 or
equivalent prechecked arithmetic, and diagnostics remain bounded.
Raw string token length excludes its quote delimiters and includes escape spelling;
raw numeric length includes its complete spelling. Canonical-size validation must
reject a programmatic model whose otherwise valid inventory would exceed 1 MiB.

Hashes are exactly 64 lowercase hex characters. Duplicate frame names are invalid,
as are missing references, unreferenced files or duplicate hash records with different
paths/lengths. Set-like file inventory order is not semantic; frame order is semantic.

Path segments start with a lowercase ASCII letter/digit and then use lowercase
letters, digits, `.`, `_`, `-`; no trailing dot, empty/dot/dot-dot segments or Windows
device stems (`con`, `prn`, `aux`, `nul`, `com1`-`com9`, `lpt1`-`lpt9`). Separator is
`/`; leaf extension is `.png`. Reject file/directory prefix conflicts. Absolute paths,
backslashes, URI/drive syntax, percent escapes, whitespace and uppercase are invalid.
This is lexical validation, not filesystem safety or evidence that a file exists.
Device stems are the portion of every segment before its first dot, so `con.extra.png`
and `aux.dir/frame.png` are rejected as well.

## Canonical Metadata Identity

Canonical output is compact UTF-8 without BOM or final newline. Emit properties
in the table order, file claims sorted by lowercase hash ordinally, and frames in
their declared order. Emit decoded allowed ASCII strings literally (no optional
slash/Unicode escaping); values cannot contain quotes/backslashes/control characters.
Integers use invariant base-10 digits. Decimal pivots have no exponent or trailing
fractional zeros and negative zero becomes `0`. Absent optional fields are omitted.
This schema-specific format is not a claim of RFC 8785/JCS conformance.

```text
metadataDigest = lowercaseHex(SHA256(
    ASCII("agd.provided-artifact.canonical-metadata.v1\n") + canonicalJsonUtf8))
```

Object order, inventory order, whitespace, permitted escaped-ASCII spellings and
`4` versus `4.000000` pivots are equivalent. Path/name/timing/hash/length/provenance
changes alter identity. Scaling timebase and durations together still changes this
metadata identity; no rational-equivalence reduction is performed. A payload change
without a changed claim is invisible to this library. This digest is not a verified
artifact-set digest, recipe key, admission receipt, selected result or quality certificate.

## Verification and Boundary

Commit independent still/sequence fixtures, literal expected canonical bytes and
independently computed digest vectors. Cover strict JSON and raw-token bounds,
escaped-key duplicates, semantic inventory errors, exact arithmetic, culture
independence, unsafe lexical paths and invalid programmatic models. Demonstrate that
valid claims for nonexistent files parse successfully without I/O.

Keep schema shape tests separate from parser/semantic expectations: JSON Schema's
numeric model does not express all lexical rules. A test-only schema evaluator must
fail on unsupported assertion keywords rather than silently accepting them. Preserve
existing vector/schema/asset-v1 baselines; no decoder or producer acceptance is claimed.
