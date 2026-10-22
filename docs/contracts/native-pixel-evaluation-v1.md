# Native Pixel Evaluation v1

**Status**: normative v1 contract, implemented and shipped since 0.2.0-preview.2 on .NET 10
Linux x64; independent review reproduced its tables, hashes, arithmetic bounds and sample
vectors. Authority: [native pixels](../../specs/parts/p1-core-engine/native-pixels.md).
**Profile:** `agd.pixels.rgba8-linear-over.v1`.

Read with [storage](native-pixels-v1.md) and [API](native-pixel-api-v1.md). Storage
owns source bytes/identity, and the API owns operation, lifetime and output budgets.
This document defines one reference evaluator, not a choice of blend modes or a
native image importer. Existing vector/Skia behavior is not the reference.

## Source and Coverage

The fixed canvas uses integer pixel cells `[x,x+1) x [y,y+1)`, top-left origin,
x right and y down. Every layer and optional mask is canvas-aligned. Evaluation
walks visible layers in metadata array order, bottom to top, with no interpolation,
antialiasing, transform, group opacity, palette conversion or implicit background.

Source samples are straight RGBA8: RGB are sRGB-encoded bytes and alpha is linear
coverage. Masks are linear A8 coverage, not gamma-encoded grayscale. An absent mask
has sample 255 at every pixel. Opacity is the exact integer `O` in 0..1,000,000
represented by the document's decimal value `O / 1_000_000`. A hidden layer is skipped
in evaluation, but its source/control data remains validated and source-significant.

Do not modify source RGBA when evaluating, changing opacity/visibility or editing a
mask. Native RGB beneath alpha zero is preserved. Pixel fill/replace assigns exact
input samples; pixel clear explicitly assigns `(0,0,0,0)`. A new mask contains 255;
mask clear assigns zero and mask removal restores implicit full coverage.

## Fixed-Point Rules

Let `Q = 16,777,216 = 1 << 24`. Values in 0..Q use 32-bit unsigned storage; unity is
Q, not `UInt32.MaxValue` or `2^24-1`. Every arithmetic product and rounding numerator
uses checked 64-bit integers. For nonnegative integers `n` and positive `d`, define:

```text
R(n,d) = floor((n + floor(d/2)) / d)
```

This is round to nearest with exact half ties upward. Apply it only at the explicitly
listed steps; do not quantize to RGBA8 after each layer, reorder layers, reassociate
products, use platform-dependent floating point, or substitute fused operations.

### Transfer Tables

The mathematical sRGB-to-linear function is:

```text
E(s) = s * 25/323                         when s <= 809/20000
     = ((200*s + 11) / 211)^(12/5)       otherwise
```

All constants above are exact rationals. Define the 256-entry decode table and
255-entry encoded-byte rounding thresholds:

```text
F[c] = floor(Q * E(c/255) + 1/2)              c = 0..255
T[k] = ceil(Q * E((2*k+1)/510))               k = 0..254
Encode(q) = number of T entries <= q          q = 0..Q
```

Threshold equality selects the higher encoded byte. This performs final byte
rounding in encoded-sRGB space, not nearest-neighbor selection by linear distance.
No runtime `Math.Pow`, color-management resource, locale or host-library version
may change these tables. Generate/review literal tables using exact arithmetic.

For independent generation, F's rational branch applies at c<=10 and T's at k<=9.
In the nonlinear branches, the inner rational is `(200*c+2805)/53805` for F and
`(400*k+5810)/107610` for T. Fifth-power integer comparisons determine the floor,
half-integer rounding and ceiling exactly; no approximate root is normative.

The table identity is lowercase SHA-256 of ASCII
`agd.pixel-evaluation.q24-tables.v1\n` (35 bytes, one terminal LF), followed by F's
256 values and then T's 255 values, each unsigned 32-bit big-endian. Total length is
2,079 bytes. The independently calculated and reviewed identity is:

```text
353442b2a3b85e1030cc800349cc40d4d0b7e2bdf063a7a9766298f4dabaa7b8
```

| Table entries | Required values |
|---------------|-----------------|
| F[0..3] | 0, 5092, 10185, 15277 |
| F[9..12] | 45831, 50923, 56146, 61682 |
| F[252..255] | 16331702, 16479430, 16627934, 16777216 |
| T[0..3] | 2547, 7639, 12731, 17824 |
| T[8..11] | 43285, 48378, 53491, 58876 |
| T[251..254] | 16258129, 16405469, 16553585, 16702478 |

Both arrays must be strictly increasing, F[0]=0, F[255]=Q, and `Encode(F[c])=c`
for every byte. Full tables and independent generation/check fixtures must be
committed with implementation; the digest alone is not test execution or an oracle
derived from production code.

### Source-Over

For each output pixel, initialize accumulator alpha `Ad=0` and premultiplied linear
channels `Pd[R]=Pd[G]=Pd[B]=0`. For a visible source layer, let a be its alpha byte,
m its mask byte or 255, and O its exact opacity millionths. Compute:

```text
As    = R(a * m * O * Q, 255 * 255 * 1_000_000)
Ps[c] = R(F[sourceRgbByte[c]] * As, Q)
u     = Q - As
Pd[c] = Ps[c] + R(Pd[c] * u, Q)       for R, G, B
Ad    = As    + R(Ad    * u, Q)
```

Each right-hand destination value is the previous accumulator. Combine alpha, mask
and opacity in one numerator; do not independently quantize those factors first.
Apply the resulting As once to source color and alpha. Invisible layers contribute
nothing; zero As is an exact accumulator no-op.

The invariants `0 <= Pd[c] <= Ad <= Q` hold after every step for valid input. Treat
an implementation violation as failure, not permission to silently clamp a faulty
compositor. The largest alpha numerator plus rounding bias is
`1,090,938,502,912,500,000`, within signed and unsigned 64-bit range. Other fixed-point
products are bounded by `Q^2 = 281,474,976,710,656`.

### Final Samples

After all layers:

```text
alpha8 = R(255 * Ad, Q)
if alpha8 == 0:
    output = (0,0,0,0)
else:
    linear[c] = R(Pd[c] * Q, Ad)
    output[c] = Encode(linear[c])
    output.A = alpha8
```

Unpremultiply with the high-precision Ad, not the quantized alpha8. Only final
flattened output canonicalizes hidden RGB to zero; source storage never does so
implicitly. The alpha-byte cutoff is Ad=32897: smaller values quantize to zero.
Partial mask/opacity round-off and canonical transparent output are part of this
versioned reference, not an artistic/visual-equivalence judgement.

## Literal Sample Vectors

Each row specifies a single output pixel. Layers are listed bottom to top; omitted
mask is 255, omitted O is 1,000,000, and layers are visible unless stated otherwise.
Hex values are straight source/output bytes in RGBA order.

| Source layers | Output RGBA |
|---------------|-------------|
| `FF0000FF` | `FF0000FF` |
| `11223300` | `00000000` |
| `000000FF`, `FFFFFF80` | `BCBCBCFF` |
| `FFFFFFFF`, `00000080` | `BBBBBBFF` |
| `0000FFFF`, `FF000080` | `BC00BBFF` |
| `000000FF`, `FFFFFFFF` with O=500000 | `BCBCBCFF` |
| `000000FF`, `FFFFFFFF` with m=128 and O=500000 | `898989FF` |
| `FF0000FF`, `FFFFFFFF` with m=0 | `FF0000FF` |
| `FF0000FF`, invisible `FFFFFFFF` | `FF0000FF` |
| `0000FFFF`, `FFFFFFFF` with O=0 | `0000FFFF` |
| `FFFFFF80`, `FFFFFF80` over transparent initial accumulator | `FFFFFFC0` |
| `FFFFFF01` with O=499999 | `00000000` |
| `FFFFFF01` with O=500000 | `FFFFFF01` |
| `FFFFFFFF` with O=1960 | `00000000` |
| `FFFFFFFF` with O=1961 | `FFFFFF01` |
| `336699C0` with m=173 and O=700000 | `3366995B` |

An initial empty accumulator is transparent, but an empty native layer list is not
a valid asset. `128/255` is not exact half. For opaque white with mask 128 and half
opacity, As is 4,210,752; rounding mask and opacity separately would incorrectly
produce 4,210,753. The two alpha=1 vectors give Ad 32896 and 32897 respectively.

These vectors were calculated with a separate exact-integer C# calculator
and cross-checked against a non-normative double-precision sRGB calculation. Table
hashing was also cross-checked with an independent SHA-256 tool. No native runtime
implementation or package was executed by those contract calculations.

## Regions and Evidence

Source/mask queries return exact stored samples. Composite preview/export/check
use this evaluator. A composite region crops the full-canvas evaluation coordinates,
without moving layers/masks, changing sample centers or resampling. Hash output RGBA
rows as plain SHA-256 without filter bytes, padding, dimensions or metadata. Pair
that observation with the source snapshot, profile and rectangle/dimensions; it is
not the framed payload hash or native source digest.

Only row-sized composite accumulators are needed. Account for all live source,
dirty/replacement, row, encoder/decoder and serialization buffers under the API's
working reservation. Check cancellation/cooperative budget at least every 1,024
evaluated layer-pixels or emitted pixels and at row/I/O boundaries. Check hidden and
duplicate logical surfaces against source/work limits even when evaluation can skip
their pixel arithmetic. This does not provide process-RSS or hard-deadline isolation.

## PNG Profiles

Writer identity: `agd.pixels.png.v1`. Checker identity: `agd.pixels.png-samples.v1`.
Neither changes the existing vector renderer, asset manifest or supplied-artifact
metadata profile.

The writer emits native-resolution, noninterlaced RGBA8 PNG: exact signature, IHDR
color type 6/bit depth 8/compression 0/filter method 0/interlace 0, one sRGB chunk with
intent 0, consecutive IDAT and terminal empty IEND. Include correct CRCs. Use filter
byte 0 for each reference row and existing .NET zlib compression, not a new DEFLATE
encoder. Do not emit palettes, ICC/EXIF/text, APNG, preview decoration or implicit
RGB-only optimization. Bound encoded bytes and chunks; no compressed-byte identity
across runtime versions is promised.

The checker captures the existing output once, bounds its candidate dimensions,
then validates it under `agd.png-rgba8-structure.v1` and
`agd.png-rgba8-managed.v1`. Validate using the observed dimensions, not the expected
native dimensions, so a completely valid different-size PNG can report a mismatch
rather than being confused with malformed data. Candidate header extraction is not
acceptance: CRC, structure, complete decoding and budgets must all pass before a
comparison result is valid. Limit observed dimensions/area to the native runtime
profile even if a wider decoder helper supports more.

The comparison matches only if dimensions and decoded RGBA hash equal the captured
reference composite. Different compression, IDAT splits or allowed compatible tags
can match. Missing, malformed, unsupported or incomplete/budget-limited checks never
match. Untagged checked PNGs are interpreted as sRGB by this checker profile, not by
a universal rule for untagged images. The existing strict length-258/code-285 rule
also applies. Native hidden RGB and flattened transparent-RGB canonicalization are
different obligations; an otherwise invisible noncanonical output sample is a
sample mismatch, not a visual-equivalence pass.

Preview may encode only the requested bounded composite rectangle; export/check
cover the full canvas. Read-only checking writes no sources, markers, payloads,
reports or repairs. Output publication and overwrite/collision authority belong to
the API/storage contracts. All results describe a captured source/output snapshot,
not perpetual path freshness, provenance, artifact admission or a preserved backup.

## Review and Implementation Evidence

Independent exact table/hash/rounding review is complete. Implementation must commit
and execute the corresponding reference fixtures; contract calculations alone do not
qualify runtime behavior. Require
literal source vectors, all 256 opaque-channel round trips, all threshold boundaries,
mask/opacity combined-rounding counterexamples, order-sensitive and hidden-layer
cases, transparent output cutoffs and invariant/overflow proofs. Cover 32x32, 64x64,
37x23, thin canvases and resource boundaries with independently specified samples.

PNG qualification must compare decoded samples, not only signatures or a round trip
through the same encoder. Test valid different encodings, dimensions/sample drift,
CRC/stream/metadata failures, source/control changes with unchanged composites,
bounded regions, read-only state and output collisions. Existing managed decoding
qualification is useful but does not qualify this new reference math, writer or
native storage. No broader native pixel, animation, admission or release claim follows from
these contract calculations and reviews.
