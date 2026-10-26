# Native Pixels Storage and Writes v1

**Status**: normative v1 contract, implemented and shipped since 0.2.0-preview.2 on .NET 10
Linux x64. Authority: [native pixels](../../specs/parts/p1-core-engine/native-pixels.md) in
the spec; this file holds the exact storage and write detail that the spec does not restate.
**Scope**: bounded native layered still-image metadata, payloads, source identity, snapshots
and single-asset writes; animation, transforms and indexed palettes are later work.

## Authority and Scope

The companion [evaluation contract](native-pixel-evaluation-v1.md) owns exact color
transfer, fixed-point arithmetic, composition and flattened PNG interpretation. The
[API contract](native-pixel-api-v1.md) owns operations, request/result envelopes,
input confinement, CLI/MCP spelling, preview/export/check and their additional work
limits. These are jointly reviewed implementation contracts, not evidence of
implemented capabilities.
Storage must not substitute a composite digest for the source identity defined here.

One asset is one dedicated **directory**, not a JSON file plus a global payload store.
The fixed canvas contains an independent, flat, bottom-to-top layer stack. Every
successful semantic edit persists immediately. No long-lived dirty session, generic
JSON patching, vector schema extension, migration, native import, animation, tiling,
global registry, automatic GC, Git automation or receipt ledger is introduced.

This document adds no runtime code, schema, dependency, platform qualification or
package/release claim. The existing vector format and discovery remain unchanged.

## Directory Contract

Example asset locator and authoritative contents:

```text
artwork/hero/
  asset.agd-pixels.json
  payloads/
    <sha256>.bin
```

`artwork/hero` is the asset directory passed to native operations. Its name is a
locator, not the asset name or ID. `asset.agd-pixels.json` is the one metadata file;
`payloads` is the one payload directory. Exact case and spelling are required. The
metadata suffix does not match vector `.agd.json` discovery. No path, URI, source
digest, global-index key or payload inventory is stored inside the document.

Every payload locator is derived as `payloads/` + the referenced lowercase hash +
`.bin`, relative to this asset only. No user-controlled path segment is substituted.
Native loading validates the **current referenced closure**, not an exact directory
inventory. It looks up only the fixed metadata file, `payloads` directory and at
most 32 referenced hash paths. Do not enumerate, open, classify or traverse other
entries in the asset root or `payloads`, including unreferenced history, README,
`.git`, unknown directories and stale temporaries. Those entries are outside this
snapshot, are never used to interpret or recover it, and do not make it invalid.

Metadata, addressed payloads and the temporary namespace below remain writer-owned;
ignoring unrelated entries is not permission to overwrite or delete them. Retain
completed unreferenced payloads without GC. Success verifies only current references,
not every retained version. When a writer addresses an existing hash path, fully
verify the matching regular blob before reuse, even if it was previously ignored.
Never repair or overwrite a corrupt, different or wrong-type addressed object.

### Temporary Namespace

`nonce` below means exactly 32 lowercase hexadecimal characters generated privately
for each exclusive temporary/staging allocation. `lockKey` is defined under
[Arbitration](#arbitration). Neither is a document ID or persistent request identity.

| Location | Reserved name and allowed type | Reader behavior |
|----------|--------------------------------|-----------------|
| Asset root | `.agd-pixels-tmp-<nonce>.tmp`, regular file | No lookup/enumeration; never use it as metadata or a payload |
| Existing asset parent | `.agd-pixels-lock-<lockKey>.lock`, stable regular lock file | Do not open, create, acquire or modify it for reads |
| Existing asset parent | `.agd-pixels-stage-<nonce>`, private creation staging directory | No lookup/enumeration or traversal when reading the target |

Temporary files may be incomplete. Writers use exclusive creation; a collision
never grants ownership of an existing entry. Before Create publication, owned
staging contains only complete metadata and `payloads`.

Writers clean up only unpublished temporary entries that the current attempt created
and still owns, using recorded identities and a bounded list. They do not scavenge
stale files, recursively delete unknown trees, unlink the stable lock, or delete
published hash objects. Cleanup failure must not conceal the primary write outcome.
Stale reserved files do not mean a transaction is pending and cannot resume one.

### Bounded Closure Work

There is no retained-payload count limit or lifetime storage quota. Per-read work
depends only on bounded metadata/current sample bytes and needed-path ancestor
checks, not the amount of Git history. Do not enumerate ancestor/sibling directories
to search for aliases or other assets.

Each write records at most 64 owned payload-temporary paths and one metadata-temporary
path, including unpublished Create staging files, plus its fixed owned directories
and lock handle. At most 32 final payload references need publication; discarded
intermediate surfaces are not staged. Cleanup uses only these bounded records,
never an all-temporaries scan. This is not an on-disk history/temp cap or a synthetic
"store full" condition; actual storage/I/O failures still follow the write protocol.

## Closed Metadata

Every listed member is required. Reject unknown fields, duplicate decoded field
names (including escaped-key duplicates), incorrect types and nulls everywhere
except `maskSha256`. Member order below is canonical; input object order is not
semantic. Array order is semantic and is never sorted by ID, name or hash.

| Object | Canonical property order |
|--------|--------------------------|
| Root | `schema`, `version`, `profile`, `assetId`, `revn`, `name`, `canvas`, `layers` |
| Canvas | `width`, `height` |
| Layer | `id`, `name`, `visible`, `opacity`, `pixelsSha256`, `maskSha256` |

| Field | Exact v1 rule |
|-------|---------------|
| `schema` | String `agd.native-pixels` |
| `version` | Digit-only JSON integer `1` |
| `profile` | String `agd.pixels.rgba8-linear-over.v1` |
| `assetId`, layer `id` | Nonempty GUID in lowercase D form; see below |
| `revn` | Digit-only integer 1 through 9,223,372,036,854,775,807 inclusive (`Int64.MaxValue`) |
| Root/layer `name` | Required bounded nonblank Unicode display name; see below |
| `canvas.width`, `canvas.height` | Positive digit-only integers within the selected runtime bounds |
| `layers` | 1 through 16 layers under the initial runtime; array index zero is the bottom layer |
| `visible` | JSON boolean, no numeric/string coercion |
| `opacity` | Exact JSON decimal from 0 through 1, at most six fractional digits |
| `pixelsSha256` | Exactly 64 lowercase ASCII hexadecimal characters |
| `maskSha256` | Exactly 64 lowercase ASCII hexadecimal characters, or explicit JSON `null` when absent |

IDs match `[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}` and must not
be `00000000-0000-0000-0000-000000000000`. Do not add a UUID version/variant restriction
to the file syntax. The asset ID and every layer ID are pairwise distinct within
the asset; there is no global uniqueness lookup. Create and add-layer generate IDs
automatically and validate uniqueness before use. IDs survive edits and reloads;
path/name changes do not regenerate them. Names need not be unique and are not
references. A copied or Git-restored asset can carry an existing asset ID.

The fixed dimensions apply to every layer and mask. There are no offsets, transforms,
groups, blend-mode fields, per-layer dimensions or linked-layer semantics. An absent
mask means full coverage. A present mask is a distinct semantic state, including an
all-255 mask. Visibility, opacity and masks affect evaluation, never stored source
RGBA. Native hidden RGB under alpha zero is preserved exactly.

### Names and Unicode

Validate decoded scalar values, not UTF-16 code-unit counts or escaped token length.
A name has 1..128 Unicode scalars and at most 512 bytes when encoded as strict UTF-8.
Reject ill-formed UTF-8, isolated surrogates and invalid surrogate escapes. Reject
C0 controls U+0000..U+001F, DEL/C1 controls U+007F..U+009F, line/paragraph separators
U+2028/U+2029 and BOM U+FEFF, even when escaped. For display safety this contract also
rejects bidi embedding/override U+202A..U+202E and isolate U+2066..U+2069. Other valid
scalars are allowed, including U+200D zero-width joiner and joined emoji; no blanket
format-category ban, Unicode-version property table or `IsPrintable` test applies.

Nonblank means at least one scalar outside this explicit whitespace set:
U+0009..U+000D, U+0020, U+0085, U+00A0, U+1680, U+2000..U+200A, U+2028, U+2029,
U+202F, U+205F, U+3000. Control rejection still applies independently. Combining
marks, ordinary letters/symbols and permitted spaces are retained exactly. No font
coverage or visible-glyph guarantee is implied. Do not trim, case-fold, normalize
to NFC/NFD, or silently replace a character. Precomposed and decomposed names differ
in semantic identity even if a UI renders them alike.

### JSON Lexemes

Input and canonical output must each fit 1 MiB (1,048,576 UTF-8 bytes). Require one
complete JSON document, strict UTF-8, no BOM, comments, trailing commas or extra
document. Standard JSON whitespace is allowed around tokens in input. Limit parser
depth to four containers, each raw string token to 1,536 bytes excluding quote
delimiters but including escape spelling, and each number token to 19 bytes. Check
bounds before allocating input-sized data, including for programmatic models.

Integer lexemes contain ASCII digits only, without a sign, fraction or exponent;
standard JSON also forbids leading zeros. Parse with checked integer arithmetic,
not binary floating point. Values out of range are rejected, never clamped.

The accepted opacity grammar is `(?:0|1)(?:\.[0-9]{1,6})?`, with exact numeric
range checking; any digits after `1.` must be zero. `0.500000`, `1.000000` and `0.0`
are valid. `-0`, `-0.000000`, `+0`, `.5`, `1.`, `01`, `5e-1`, `0.0000001` and
`0.5000000` are invalid. Validate spelling and precision before conversion; even
an excess trailing zero is excess precision. Canonical opacity removes fractional
trailing zeros and then an empty decimal point, so the three valid examples become
`0.5`, `1`, `0`. Internally it is exactly `opacityMillionths / 1_000_000`, with
integer `opacityMillionths` in 0..1,000,000. The evaluator consumes that integer;
no persisted binary-float approximation or alternate opacity field exists.

## Canonical Bytes and Identity

This is schema-specific canonical JSON, not RFC 8785/JCS. Serialize valid metadata
in the table order, compact UTF-8, without BOM, insignificant whitespace or a final
newline. Emit integer digits and canonical opacity invariantly, JSON booleans in
lowercase, and an absent mask as `null`. Invalid models have no canonical bytes.

Decode permitted input escapes before validation. Canonical strings escape only
quote as `\"` and backslash as `\\`; emit all other allowed scalars as their raw,
shortest strict UTF-8 encoding. Never escape `/`, HTML characters or non-ASCII
characters. No `\u` escape or control escape appears in canonical output. Input
`"\u00e9"` emits bytes `22 c3 a9 22`; input `"e\u0301"` emits
`22 65 cc 81 22` and is a different name. Input `"\u002f"` emits `22 2f 22`.
The valid canonical string `"a\"b\\c/d"` retains the required quote/backslash escapes.
The valid joined-emoji input `"\ud83d\udc69\u200d\ud83d\udcbb"` emits exactly
`22 f0 9f 91 a9 e2 80 8d f0 9f 92 bb 22`; escaping does not remove the joiner.

Define `canonicalSourceBytes` by serializing the same fully validated document,
but **omitting only the root `revn` member**, including its name, value and separator.
All remaining property order and bytes are unchanged; do not replace `revn` with
zero/null or remove matching text from a serialized string.

```text
sourceDigest = lowercaseHex(SHA256(
    ASCII("agd.native-pixels.source.v1\n") + canonicalSourceBytes))
```

The prefix ends in the one byte LF (`0a`), with no NUL terminator. Source bytes
have no final LF. The digest includes schema/version/profile, asset/layer IDs,
all names, canvas, layer order, visibility, opacity, mask presence and every
referenced payload identity. It includes hidden/render-neutral content and is not
a composite, encoded PNG, raw-sample-only, path or revision digest. Locators and
`sourceDigest` are never stored in metadata. Compute it from validated captured
state; a metadata-only digest is not proof that required files exist.

Changes to input whitespace, property order, accepted escape spelling, or permitted
opacity trailing zeros do not change source identity. Moving the complete directory
does not change it. Changing only `revn` does not change the digest, but does change
the snapshot. Hashes detect byte mismatch under SHA-256 assumptions; they are not
authentication, provenance, licensing evidence or a backup.

### Snapshot and No-op

The snapshot shape is exactly `{assetId,revn,sourceDigest}`. Its ID/revision obey
the metadata rules and its digest is 64 lowercase hexadecimal characters. A query
returns the token for its one captured, fully verified closure, not a promise that
the path is still current after return. Every existing-asset mutation requires all
three expected values and compares all three under the writer lock.

After validating the guard, all inputs, the entire ordered batch and every
intermediate budget/invariant, compare final `canonicalSourceBytes` to the captured
before bytes **byte for byte**. Do not use equality of digests alone or equality of
composites as the no-op test. A request that would be a no-op still fails for a stale
guard, corrupt closure, invalid operation or exceeded intermediate budget.

If equal, return unchanged before/after snapshots and no created IDs or changed
resources. Do not increment `revn`, normalize/rewrite noncanonical input metadata,
publish payloads or create storage staging/temp files. Taking the required writer
lock may create its stable sibling marker; that marker is operational state, not
payload publication. Add-then-remove of a temporary layer reports no created ID;
if the final metadata is otherwise unchanged, the whole batch is a no-op. A changed
batch likewise reports only generated IDs that survive in its persisted final state.

If different, preserve `assetId`, set `revn = oldRevn + 1` exactly once and publish
one complete final state. The API rejects removing the last layer at any step; later
repair in the same batch does not excuse an invalid intermediate state. At
`Int64.MaxValue`, a valid no-op succeeds, but a semantic change fails before any
payload publication with `RESOURCE_LIMIT`; never wrap, reset or silently re-ID.

## Framed Payloads

Each immutable payload is a 24-byte header followed by exactly one whole-surface
sample array. Hash the **entire header and data**, not just samples.

| Offset | Bytes | Value |
|--------|-------|-------|
| 0 | 8 | ASCII `AGDPIXL\n`, hexadecimal `41 47 44 50 49 58 4c 0a` |
| 8 | 4 | Unsigned 32-bit big-endian format version `1` |
| 12 | 4 | Unsigned 32-bit big-endian kind: `1` RGBA8 sRGB straight, `2` linear A8 coverage mask |
| 16 | 4 | Unsigned 32-bit big-endian width |
| 20 | 4 | Unsigned 32-bit big-endian height |
| 24 | Exact remainder | `width * height * 4` bytes for kind 1; `width * height` bytes for kind 2 |

Source pixels are tightly packed `R,G,B,A` unsigned 8-bit channels in that order;
mask samples are one unsigned coverage byte, 0..255. Origin is top-left, x increases
rightward, y downward; offset is `24 + channels * (y * width + x)`. Pixel `(x,y)`
occupies `[x,x+1) x [y,y+1)` in canvas edge coordinates. There is no stride, padding,
compression, endianness swap of samples, crop, orientation tag or trailing byte.
Header arithmetic uses checked wide integers before any read/allocation.

Every `pixelsSha256` reference requires kind 1; every nonnull `maskSha256` requires
kind 2. Dimensions must exactly match the document canvas. The document profile
fixes their interpretation through the evaluation contract; the header does not
introduce a second color-profile selector. Reject unsupported version/kind, wrong
magic, dimensions, short/excess length, hash mismatch and filename mismatch.
Require exact EOF, including when a file's reported length looked correct.

Load each distinct referenced hash into owned bounded bytes (or an equivalently
bounded captured representation), then validate framing and SHA-256 against both
the reference and exact filename. Verification and subsequent sample use consume
the same captured bytes, not a later reopen of a mutable input. At most 32 logical
references exist in this runtime. Sharing a verified buffer or addressed object is
permitted; editing one layer/mask uses an independent dirty copy or equivalent
copy-on-write and retargets only that layer's reference. Equal payload hashes never
create linked edits and do not exempt logical source or targeted work from budgets.

## Runtime Reservation

`agd.pixels.runtime.v1` is a runtime qualification profile, not another persisted
field and not a permanent format capacity. All limits below are inclusive and all
must hold simultaneously. MiB means 1,048,576 bytes.

| Resource | Limit/accounting |
|----------|------------------|
| Canvas axes | Any integer width and height in 1..8192 |
| Canvas area | `A = width * height <= 4,194,304` |
| Layer/mask count | `1 <= L <= 16`; at most one mask per layer, `0 <= M <= L` |
| Logical source samples | `A * (4 * L + M) <= 67,108,864` bytes (64 MiB) |
| Metadata / request | Each at most 1,048,576 encoded UTF-8 bytes; canonical metadata also bounded |
| Working reservation | 268,435,456 bytes (256 MiB) per admitted operation |
| Current closure | One metadata file, one payload directory, at most 32 referenced payload paths; no history enumeration/quota |
| Owned write records | At most 64 payload-temporary paths and one metadata-temporary path; not an on-disk history/temp cap |

Logical samples include hidden layers, zero-opacity layers, all masks and duplicate
logical uses of equal payloads. Headers are excluded only from the logical-sample
formula; their actual buffers/I/O remain charged elsewhere. Two masked 64x64 layers
use 40,960 logical bytes. At 2048x2048, three masked layers use 60 MiB and four use
80 MiB; the latter fails even though its axes, area and layer count separately fit.
32x32, 64x64, 37x23, 1xN and Nx1 are fixtures, not presets.

Reserve before allocating or capturing. Peak working accounting includes simultaneously
live old source bytes, dirty copies, captured replacement inputs, intermediate/final
output, composite/codec work, metadata strings/models/UTF-8 serialization, hash/I/O
buffers and collection storage, including growth/copy overlap. An implementation
must qualify its conservative accounting; a logical 64 MiB source is not a 64 MiB
peak. Charge every intermediate state and cumulative work, not just the final
candidate. Reuse/streaming can reduce physical reservations but not logical budgets.

The API contract additionally bounds batches to 64 operations, replacement inputs
to 16 MiB aggregate and cumulative targeted pixel/mask samples to 16,777,216; it
owns query/preview/PNG bounds and shared host admission/deadline policy. Admission
does not waive this storage reservation. These are cooperative in-process budgets,
not process-RSS limits, a hard memory sandbox or forcibly preempted I/O deadlines.

Source capture, hashing and copying use chunks of at most 64 KiB, checking
cancellation/deadline between chunks and before each needed-path probe/allocation.
Full captured buffers still count toward the working reservation. The API owns
the whole-operation deadline and additional sample-loop checkpoints.

## Filesystem Boundary

Initial qualification target is a **trusted, quiescent local filesystem on .NET 10
Linux x64**, with external edits, Git and other noncooperating writers paused for
the entire operation. Cooperating native writes are allowed as specified below.
No hostile ancestor-replacement race, network filesystem, power-loss durability,
multi-asset atomicity or process-crash outcome ledger is promised.

Existing [AssetPaths type probes](../../src/FTPlus.AgentGraphicDesign.Core/Assets/AssetPaths.cs)
provide source/type-inspection context only: Linux regular-file inspection uses a
qualified runtime-specific `System.Native` ABI, and check/open are separate. This
contract does not newly qualify Windows or other Unix configurations. The S0
`nativePixels` gate refuses all native operations off .NET 10 Linux x64, including
Windows inspect/validate/region reads that formerly passed the shared probe. Existing
[vector write helpers](../../src/FTPlus.AgentGraphicDesign.Core/Persistence/FileDocumentStore.cs)
are not evidence that native directory publication or per-directory locks are safe.

Validate confinement and exact needed-path components before access, without
ancestor/sibling enumeration. Reject symlink/reparse points, dangling links,
FIFOs, sockets, devices and wrong entry types for every
accessed source, metadata, payload, parent, staging, lock or output path. Pin/check
the relevant parent, asset directory and opened-file identities at capture and
publication boundaries using the qualified backend. Detect a changed identity and
fail before publication when possible; never continue through a substituted object.
Attribute checks alone do not identify all Unix special files. No generic helper
may turn an unsupported probe into an ordinary-file assumption.

Initial qualification uses case-sensitive filesystem spellings and exact fixed-path
lookups. Check relevant source-root/metadata and opened-object bindings, not unrelated
case variants elsewhere in a directory. A non-case-sensitive filesystem needs a
separately qualified canonical directory binding, including common lock identity for
aliases, before publication; otherwise fail `ASSET_PLATFORM_UNSUPPORTED`. Do not add
a guessed case-folding or scan-based fallback. Operators pause renames and external
path changes; these checks are not a hostile-filesystem confinement proof.

The opened metadata object defines a reader's generation. Its identity may differ
from an earlier pathname probe because of a cooperating atomic replacement; validate
the opened regular file rather than requiring the path to keep its old inode. A
later permitted metadata replacement does not invalidate an already captured old
generation. Parent/asset-directory identities and immutable payload identities must
remain stable; this exception is not permission for arbitrary source replacement.

Regular-file hard links are not ownership or immutability proof. Read captured bytes;
do not modify addressed objects in place. Compare actual file identities as well as
lexical paths for known inputs/outputs. The trust assumption excludes external
mutation through an undiscovered alias; there is no global hard-link search or
hostile-filesystem security claim. Hash claims do not authenticate their author.

The actual filesystem/backend must qualify atomic metadata replacement, immutable
file publication without replacement, and atomic directory publication without
replacement for Create. Unsupported primitives/filesystems fail safely with
`ASSET_PLATFORM_UNSUPPORTED` before attempting the operation; no weaker fallback.
Qualify the primitives needed by that operation; a read does not require permission
to create a lock or exercise Create's publication primitive.
In particular, `exists` followed by `Directory.Move`, or an ordinary replacing
rename with a precheck, is **not** proof of race-free no-replace directory publication.
This contract specifies required semantics, not an unsafe choice of .NET API.

## Reader Protocol

1. Validate the asset locator, fixed metadata/payload-directory paths and needed
   ancestor types/identities without enumeration or native/lock writes. Missing
   targets are errors, not Create requests.
2. Open metadata once in a mode compatible with the qualified atomic replacement,
   capture at most 1 MiB and require EOF. Parse, validate and retain owned metadata.
   Do not stat a generation, close it, and reopen a potentially different generation.
3. Derive and capture the unique current references, validate their full framing,
   exact length, kind, dimensions and hash, and enforce all bounds. Retained old
   objects are not substituted for missing references or comprehensively reverified.
4. Compute the source digest and return one verified snapshot/owned query result.
   Do not reread metadata to chase the latest generation or merge layers from two
   captures. A failure returns no successful partial asset.

During a healthy cooperating write, a reader may capture complete old metadata or
complete new metadata. All addressed dependencies already exist before new metadata
becomes visible, and old dependencies remain immutable and retained, so either
capture has a complete closure. Temporary files do not choose a generation. Reads,
inspection, validation, evaluation and checking never repair, normalize or initialize
sources. Filesystem access times may change; no contents, mtimes, directory entries,
native markers or reports are created by a read-only operation. Explicit output
publication is a separate API-authorized write.

## Writer Protocol

### Arbitration

All cooperating writers, including Create while the target is absent, arbitrate on
the same stable sibling `.agd-pixels-lock-<lockKey>.lock` under the **already existing**
parent, where:

```text
lockKey = lowercaseHex(SHA256(strictUTF8(exactDirectoryLeaf)))
```

Hash only the exact case-sensitive directory-leaf spelling, with no prefix, newline,
case-folding or Unicode normalization. This opaque lock key is not `assetId` or a
source digest. The fixed-length lock/staging names do not append to a potentially
maximum-length legal directory leaf. Validate parent/leaf binding and the regular
lock file's identity, and retain its exclusive handle through guard checking,
publication, outcome determination and cleanup. All cooperating writers to that
same directory path must use that lock, across processes and within one process;
an in-memory mutex alone is insufficient. Never place it inside the renamed asset.

The marker may be created by a writer and remains after release. Existence is not
ownership: an OS/qualified-backend exclusive lock is the authority. Never delete or
replace the marker on release, failure or a supposedly stale-lock timeout. Acquisition
is cancellation-aware and has a 5-second monotonic contention budget, measured from
the first attempt; expiry returns `PIXEL_BUSY` with fixed message
`Asset writer lock wait expired.` The API's immediate host-slot rejection also uses
`PIXEL_BUSY`, with fixed message `Native operation slot is busy.` Do not retry
non-contention I/O, permission or unsafe-path failures as contention. This is cooperative
waiting, not hard interruption of a blocked filesystem call. The API's host-wide
heavy-operation guard is additional to, not a replacement for, this directory lock.

### Create

1. Validate/capture the bounded request and reserve work; qualify the required backend.
   Require an existing permitted parent and a target directory entry that is entirely
   absent, including no dangling link. Do not create missing ancestors.
2. Acquire the stable sibling lock, then revalidate parent identity and target absence.
   Any existing entry, including an empty directory, incomplete asset, unknown schema
   or corrupt metadata, rejects Create with `PIXEL_ALREADY_EXISTS`; never initialize,
   migrate, merge with or repair it.
3. Generate a nonempty asset ID and distinct layer ID. Construct `revn:1`, the requested
   valid asset name/canvas, and exactly one layer named `Layer 1`, `visible:true`,
   `opacity:1`, all-zero RGBA samples and `maskSha256:null`. The opacity control is
   one; the initial pixels remain fully transparent.
4. Create private exclusive same-parent staging with the reserved staging name. Build
   the complete payload directory and canonical metadata there. Flush payload and
   metadata writes to the qualified backend, close/verify completed files and verify
   the candidate closure. Staging access is private until publication.
5. Revalidate target absence, parent/staging identities and cancellation, then publish
   the **entire directory atomically with no replacement**. The backend must reject
   even an empty target created between the check and publication. The no-replace
   primitive, not the precheck or cooperative lock alone, enforces that rule.
6. Publication is the visibility point. Return the initial verified snapshot and IDs.
   A reader can see absence before it or the complete asset after it, never a partially
   populated target. On prepublication failure, remove only this attempt's owned
   unpublished staging entries; an independently appeared target is never cleanup.

### Existing Asset Edit

1. Validate the bounded request envelope/expected token and qualify/reserve work before
   substantive capture. Acquire the sibling lock and revalidate directory identity.
2. Reload metadata and **all currently required payloads** using the reader closure
   rules while holding the lock. Never trust cached samples or a cached revision.
   Recompute the token and compare expected `assetId`, `revn` and `sourceDigest`;
   mismatch returns `REVISION_CONFLICT`, including same-revision different content.
3. Capture each bounded external replacement input once through the API's confined
   file/owned-buffer boundary, require exact size and EOF, and operate only on the
   captured bytes. Validate/evaluate the complete ordered batch privately, with
   checked arithmetic and every intermediate invariant/reservation/work limit.
   Invalid final operations reject everything; no incremental persistence is allowed.
4. Canonicalize the candidate source and perform the exact no-op test. If equal,
   return without creating storage temporaries or publishing anything. Otherwise
   reject revision overflow and reserve owned temporary records and bounded
   success-result construction/serialization capacity before staging.
5. Produce only final required new payloads in exclusive private root temporary files;
   do not publish intermediate states that the batch later discarded. Complete,
   flush, close and verify every new header/data/hash before it can be referenced.
6. Publish each required new hash object **without overwriting** an existing entry.
   If its addressed name already exists, fully verify its type, identity, framing,
   dimensions, kind, exact length and hash, and compare its captured bytes with the
   intended bytes. Reuse only an exact match. A corrupt/different object is an error,
   not an invitation to overwrite, salvage or silently change its name.
7. Complete/flush/verify canonical final metadata (`oldRevn + 1`) in an exclusive root
   temporary file. Confirm the full new closure is already published, and revalidate
   the expected directory/metadata binding and cancellation. Atomically replace
   `asset.agd-pixels.json` **last**, without a delete-then-move visibility gap.
8. This metadata replacement is the one visibility point. Record success before
   observing later cancellation or doing optional owned-temp cleanup. Return the
   before/after snapshots and final-state effects through the API contract.

Flush and atomic visibility obligations require actual filesystem evidence; flush
calls alone do not establish power-loss persistence. A failure after one or more
payload publications but before metadata publication may leave complete unreachable
objects. They are safe orphans, must remain unmodified and are not an accepted edit.
Do not roll back published objects or replace the old metadata with a guessed state.

### Cancellation and Uncertainty

Cancellation observed before the visibility primitive is entered prevents semantic
publication; the old asset (or absent Create target) remains authoritative. Cleanup
may leave only the permitted owned temporaries/orphaned complete payloads/lock marker.
Check cancellation cooperatively throughout bounded capture, hashing, batch work and
lock waiting, with a final check before publication.

Once publication is known to have succeeded, **success wins** even if cancellation
arrives or cleanup fails immediately afterwards. Do not return a cancelled/rolled-back
result for a known committed state. If the process/transport dies, or an I/O outcome
is genuinely ambiguous while crossing the visibility point, no definite rollback
claim is valid. An available response uses `PIXEL_WRITE_OUTCOME_UNKNOWN`; the caller
must inspect/reload the target, not blindly retry. A process crash cannot always send
that response. Tokens compare state; they are not durable request receipts.

## Output and Git Safety

Preview/export output paths must not overwrite or be inside any involved native
asset directory, its metadata/payload namespace, lock/staging paths, or captured raw
replacement inputs. Reject same-file aliases/hard links by identity as well as path
overlap and case checks. Existing candidate/output files additionally require link
count exactly one under the qualified probe, including otherwise unrelated PNGs.
This conservative output rule rejects lock aliases without readers inspecting the
lock or searching globally for links; the API specifies the corresponding fixtures.
Do not overwrite an existing asset or raw input merely
because overwrite was requested. The API contract owns explicit output-overwrite
options and bounded output publication; check never writes or repairs an output.

For a user-managed Git/Git LFS restore, preserve metadata **and the complete referenced
payload closure** for every version intended to remain usable. A hash, LFS pointer
text or unmaterialized remote object is not a payload. Native load neither downloads
objects nor invokes Git/LFS. Missing data is an error even if its hash is known.
Retained complete payloads are project/history bytes, not disposable render cache;
runtime lock/temporary files carry no sole project intent and should not be tracked.

Pause all native writers/readers during checkout/restore/merge or other external
replacement, then reload and fully verify. No cache, newer remembered revision or
lock contents can override restored tracked state. A restore can rewind `revn`,
produce different content at the same `revn`, or repeat the **exact** old token.
Different ID/revision/digest rejects a prior guard. An exact restored token can make
that old guard valid again: this is compare-and-swap state identity, **not** anti-rollback,
monotonic history, semantic undo or lost-response deduplication. A later edit advances
once from the restored revision; it does not resume a cached newer branch.

Unknown schema/version/profile, malformed metadata, corrupt/missing referenced
objects and incomplete preexisting directories never trigger initialization,
migration, downloading, normalization or repair. This fail-closed behavior is part
of the reviewed baseline, not a compatibility path.

## Structured Failures

Use the API contract's `NativePixelException` and bounded validation-error shapes,
with one fail-first error and no successful partial state. Existing lower-level
`DocumentEngineException` codes are mapped through that boundary, not a competing
public pixel exception contract. Reuse existing
`EngineErrors` spellings where applicable. The following pixel-specific strings are
the reviewed API-aligned baseline, not constants registered by this documentation.

| Code | Storage meaning |
|------|-----------------|
| `INVALID_ARGUMENTS` | Invalid request envelope or missing expected snapshot |
| `PARSE_ERROR` | Malformed JSON, invalid UTF-8, duplicate/unknown metadata member or incorrect shape |
| `INVALID_ID` | Invalid, empty or duplicate asset/layer ID |
| `INVALID_PROPERTY` | Invalid name, opacity, hash spelling or other metadata value |
| `INVALID_DIMENSIONS` | Nonpositive/invalid integer canvas dimensions |
| `UNSUPPORTED_VERSION` | Unsupported native document schema/version or payload version |
| `PIXEL_PROFILE_UNSUPPORTED` | Unsupported document profile or payload kind |
| `RESOURCE_LIMIT` | Runtime bound, owned temporary-record bound, working reservation or revision overflow; no retained-history quota |
| `PATH_ESCAPE` | Unconfined/aliased forbidden path, symlink/reparse point, unsafe type or output/input collision |
| `DOCUMENT_NOT_FOUND` | Absent asset target or required metadata |
| `REVISION_CONFLICT` | Any expected snapshot component differs under lock |
| `ASSET_PLATFORM_UNSUPPORTED` | Unqualified type probe, filesystem or required atomic primitive |
| `PIXEL_INVENTORY_MISMATCH` | Missing required payload directory or referenced payload; unrelated entries are not an inventory mismatch |
| `PIXEL_CONTENT_MISMATCH` | Required payload framing, size, kind/dimension binding, bytes or hash mismatch |
| `PIXEL_ALREADY_EXISTS` | Any entry already occupies a Create target |
| `PIXEL_BUSY` | Immediate native host-slot rejection or five-second writer-lock wait expiry, distinguished by the fixed messages above |
| `STORE_UNAVAILABLE` | Expected access/I/O failure with a known prepublication outcome |
| `PIXEL_WRITE_OUTCOME_UNKNOWN` | Visibility may have occurred; inspection required before another mutation |

Cancellation before known publication propagates through the existing cancellation
envelope, not as a successful edit. Error messages are at most 1,024 Unicode scalars
and 4,096 UTF-8 bytes; locations at most 512 ASCII characters, using schema JSON paths
such as `$.layers[0].maskSha256`, a request operation index, or `$` plus a byte offset
when necessary. Use fixed/bounded messages, explicit diagnostic truncation and no
untrusted name, raw data, absolute path or arbitrary exception-detail echo. A typed
observed conflict snapshot is permitted only after fully verifying it; diagnostics
are not another authority or receipt. Transport/exit-code mapping belongs to the API.

## Literal Vectors

The following are approved reference bytes, not runtime fixture execution. Hex
payload vectors include every byte, including framing. Independent joint review
recomputed and approved all framed-payload and source-hash vectors below, separately
from any native serializer or writer implementation.

| 1x1 payload | Complete bytes (hex) | SHA-256 |
|-------------|----------------------|---------|
| Transparent RGBA | `4147445049584c0a0000000100000001000000010000000100000000` | `dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b` |
| Hidden RGB `11 22 33 00` | `4147445049584c0a0000000100000001000000010000000111223300` | `b6a504dc8edff031f684f4a317efd10f6626ec3d9802c0291e12711c1a7677cf` |
| Full mask `ff` | `4147445049584c0a00000001000000020000000100000001ff` | `acaa4a126cbcef28d29bf07df0b4ca22fe3561515f79145914717a7d58bb5a77` |
| Zero mask `00` | `4147445049584c0a0000000100000002000000010000000100` | `270d14708db43fb170ef64ce3d984f7c60781198a2875149e4f4df9c5f2edb29` |

This canonical metadata example is exactly 392 UTF-8 bytes. The Markdown fence's
display newline is not part of the file. Literal fixture IDs are valid test inputs,
not Create's generated production IDs.

```json
{"schema":"agd.native-pixels","version":1,"profile":"agd.pixels.rgba8-linear-over.v1","assetId":"11111111-1111-4111-8111-111111111111","revn":1,"name":"Pixel","canvas":{"width":1,"height":1},"layers":[{"id":"22222222-2222-4222-8222-222222222222","name":"Layer 1","visible":true,"opacity":1,"pixelsSha256":"dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b","maskSha256":null}]}
```

Its canonical source is exactly 383 UTF-8 bytes:

```json
{"schema":"agd.native-pixels","version":1,"profile":"agd.pixels.rgba8-linear-over.v1","assetId":"11111111-1111-4111-8111-111111111111","name":"Pixel","canvas":{"width":1,"height":1},"layers":[{"id":"22222222-2222-4222-8222-222222222222","name":"Layer 1","visible":true,"opacity":1,"pixelsSha256":"dfb72ce0479252c26845a887800c571ce3e0bdb004d4b3f23328319b33d47d4b","maskSha256":null}]}
```

The complete verified token is:

```json
{"assetId":"11111111-1111-4111-8111-111111111111","revn":1,"sourceDigest":"5183bc41cda41700726ef8584f6be9977b2f59a1602e101ef9e20554b46a6b91"}
```

Each following row starts from the base document, with only the stated semantic
change. Changing `revn` alone, for example to `2`, retains the base source digest.

| Change from base | Expected source digest |
|------------------|------------------------|
| Layer opacity `0.5` (also input `0.500000`) | `dc5a3216fb4da08ac9e9a5a6203cb2055b368eb978c4c9f34e3bf5b2fbd8b0cd` |
| Attach the full-mask payload above | `e77ebd1bf294c40179233e2a82fe196a85eea3bf530185e20db8c0f4c5195754` |
| Replace RGBA with the hidden-RGB payload above | `80de8ffac45996eeedbc18e0f4ddfaa5b4d4f97c4193232889d0e5e14783a78c` |

All three can leave this transparent example's flattened samples unchanged, yet
must change native source identity. The approved byte/digest vectors must be carried
into independent test fixtures and exercised against the implementation before
runtime qualification; their exact values do not await another contract review.

## Required Evidence

Expected bytes, hashes and failure outcomes must be specified independently of the
implementation under test. Tests must not derive their expected canonical output
or golden hashes with the production serializer/writer. Literal malformed storage
fixtures are appropriate for contract tests; CLI/MCP authoring workflows must use
public native operations rather than fabricate missing features through file writes.

| Fixture group | Required positive and negative evidence |
|---------------|-----------------------------------------|
| Canonical metadata | Literal vectors above; input object order/whitespace/allowed escapes and opacity trailing-zero equivalence; cultures do not affect bytes; quote/backslash/raw Unicode, ZWJ and joined emoji accepted unchanged; precomposed versus decomposed names yield different bytes/digests |
| Strict parsing | Duplicate and escaped-duplicate keys, unknown/missing fields, omitted versus explicit-null mask, other nulls, invalid UTF-8/surrogates, exact C0/C1/line-separator/BOM/bidi rejection ranges including escaped spellings, explicit-whitespace-only names, scalar/byte/token/depth/input/canonical limits, uppercase/empty/duplicate IDs and invalid hash spelling; repeated valid payload hashes remain allowed |
| Exact numbers | Integer signs/fractions/exponents/leading zeros; opacity negative zero, excess trailing precision and out-of-range values; inclusive `0`, `0.000001`, `1`, `Int64.MaxValue`; changed-at-max fails and valid no-op-at-max succeeds |
| Source identity | ID/name/canvas/layer-order/visibility/opacity/mask-presence/mask-data/RGBA mutations, including hidden or render-neutral edits; only revision/locator changes retain digest; no stored self-hash accepted |
| Payloads | Complete literal frames above; 37x23 distinct corners/rows and thin canvases; hidden RGB exact reload; wrong magic/endian/version/kind/dimensions, every truncation of small frames, one trailing byte, filename/hash/data mismatch, kind-swapped reference and cross-asset missing local object |
| Independent layers | Duplicate RGBA/mask storage hashes, edit only one reference, unchanged sibling samples; duplicate/hidden surfaces still charged in logical/work budgets; no-mask versus all-255-mask identity difference |
| Resource bounds | Inclusive axes/area/layers/logical bytes; invalid combinations; peak old+dirty+input+output accounting and transient over-budget batch that ends small; bounded owned-temp records and 64 KiB capture/hash/copy checkpoints; current closure load/edit succeeds independently of retained-history counts above 4,096 |
| Closure-only reads | README, `.git`, unknown nested entries, many stale temporaries and malformed/wrong-type unreferenced payload paths are not enumerated, classified or opened; no ancestor/sibling scan; at most 32 current hash paths captured; corrupt ignored object fails full validation if later referenced/addressed for reuse; no native/lock/repair writes or unknown-file deletion |
| Needed-path safety | Reject wrong-type, broken symlink/reparse/FIFO/device/socket entries at required paths; exact case-sensitive fixed lookup and qualified parent/source-root/metadata/lock identities; unrelated case variants are not searched; non-case-sensitive backend fails closed unless its common canonical binding is qualified |
| Create | Generated distinct stable IDs and required initial layer; missing parent and existing empty/corrupt/unknown target reject; maximum legal directory-leaf lengths use fixed-length hashed lock and staging names; cooperating creators serialize; an independently inserted empty target between precheck/publication is never replaced; unsupported no-replace backend fails with no fallback |
| Guard and batches | Same-revision/different-ID, RGBA, names, visibility, opacity and masks reject; invalid last operation and invalid intermediate state leave source untouched; inverse edits and add/remove temporary-layer no-ops retain exact metadata bytes/token and publish nothing/report no removed ID |
| Writer exclusion | Two same-snapshot semantic writers cannot both commit; exact leaf yields the same sibling lock across target absence/Create/metadata replacement, independent of asset ID; independent processes and same process exclude; five-second lock wait versus immediate host rejection uses PIXEL_BUSY with the specified fixed messages; cancellation, non-contention errors and lock release/identity checks |
| Publication failures | Inject before/during/after each payload completion, flush, verification, no-replace publication and final metadata replacement; only absence/complete initial Create or complete old/new edit visible; wrong existing hash object never overwritten; safe orphans retained; only owned unpublished temps cleaned |
| Reader/writer overlap | Reader captures old metadata before replacement and reads old retained payloads afterwards; reader captures new metadata and all new bytes are complete; no lock marker from readers, torn metadata, generation mixing or temp-based recovery |
| Outcome and collisions | Cancellation before visibility versus success after visibility; lost/ambiguous reply requires inspection, not replay; cleanup failure does not undo success; output/input/native/lock/staging lexical and same-file aliases are never overwritten |
| Git restore | User-managed complete old closure restores IDs/order/names/controls/samples/token; same-revision alternate state rejects old guard; exact restore repeats old token and allows CAS again; next revision starts from restored state; missing payload/LFS pointer fails without download/repair; no Git executable/worktree needed |
| Shared workflows | Public CLI and MCP two-layer 32x32 and 64x64 loops plus 37x23 and 1xN/Nx1 fixtures; immediate reload after edits, masks/control source identity, native-resolution export/check and independent reference samples per companion contracts |

## Review Gates

Joint review is complete for exact syntax, Unicode rules, canonical bytes/hashes,
closure and write semantics, error/`NativePixelException` alignment, API boundaries
and the companion's exact evaluation/encoding math. The output-alias safety rules
are included in that approved baseline. These choices do not await reconciliation
or repeat review of identical contract text. Approval is for implementation only;
the following implementation qualification remains required:

- Select and qualify the real .NET 10 Linux x64 filesystem backend, cross-process
  lock, identity probes and atomic no-replace directory/file and metadata-replace
  primitives. Do not infer those guarantees from the existing vector writer.
- Measure conservative peak reservations and execute the independent failure,
  concurrency, cancellation, alias and Git-restore fixtures before claiming runtime
  support, including the five-second writer wait and immediate host-slot rejection.
- Qualify the implementation against the approved canonical/hash and evaluation
  vectors, exact PNG output/check behavior, and public CLI/MCP workflows and errors.
  Storage-vector agreement alone does not prove correct compositing or public surfaces.

No runtime tests, package gates, filesystem/memory qualification or shipped native
capability are claimed by this documentation approval.
