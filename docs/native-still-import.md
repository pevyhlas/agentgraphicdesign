# Preserved native still import

**Availability:** feature 005 development source; not included in the published 0.3.0
packages. Discover the executable's help, MCP tools and `get_capabilities` before use.

Import one untimed provided-artifact v1 PNG into a new native asset, or append an
independent top layer to a same-size asset. A standalone arbitrary PNG needs its
provided-artifact descriptor and exact two-file bundle; the descriptor is not a vector
document, asset-build manifest or external producer execution receipt. See [provided artifacts](provided-artifacts.md).

```sh
agd pixels import artwork/chosen.pixels \
  --descriptor incoming/still/artifact.json --name Chosen --layer-name Imported -f json
agd pixels inspect artwork/chosen.pixels -f json
agd pixels region artwork/chosen.pixels --layer <initialLayerId> --target pixels --rect 0,0,3,2 -f json
agd pixels export artwork/chosen.pixels --output chosen.png -f json
agd pixels check artwork/chosen.pixels --output chosen.png -f json
```

MCP: `import_pixel_asset` with `assetDirectory`, `descriptorPath`, `name`, `layerName`.
For append, read the current snapshot and write an original UTF-8 request file:

```json
{
  "descriptorPath": "incoming/overlay/artifact.json",
  "expectedSnapshot": {
    "assetId": "<current native asset GUID>",
    "revn": 1,
    "sourceDigest": "<current lowercase SHA-256>"
  },
  "layerName": "Overlay"
}
```

The placeholders above must be replaced with observed values. Run
`agd pixels import-layer artwork/chosen.pixels --request import-layer.json -f json`.
MCP `import_pixel_layer` takes `assetDirectory` and that same closed `request` object.
CLI paths resolve from the invocation directory, including paths inside request files;
MCP paths are confined to the configured working directory. Duplicate/unknown/null fields,
nonliteral GUID/hash spelling, numeric exponent/sign syntax and stale tokens reject.
Original request bytes are bounded to 1 MiB, snapshots to 1 KiB.

## Interpretation and qualification

Profile: `agd.pixel-still-import.managed-local.v1`, qualified .NET 10 Linux x64.
Pause supplier/project writers: source and target filesystems must be trusted and
quiescent. PNG/metadata content may be untrusted but bounded. The same private capture
feeds encoded hashing, strict managed decoding, native framing and preservation; no
supplier path is reopened after validation.

- Exactly one still/file, metadata <=1 MiB, encoded PNG <=32 MiB, axes 1..8192,
  area <=4,194,304 pixels; existing 16-layer/64 MiB logical-source bounds.
- Shared native host slot (`PIXEL_BUSY`), 256 MiB conservative cumulative reservation,
  60-second cooperative deadline. These are not process-RSS or hard-termination limits.
- Exact sRGB-encoded straight RGBA8 sources, including hidden RGB at alpha zero.
  No resizing, trimming, premultiplication or palette quantization.
- Pivots and producer claims are historical input evidence, not native placement,
  verified source intent, rights or quality approval.
- Existing Q24 linear-over/mask/opacity evaluation remains authoritative. For opaque
  red below half-alpha green (alpha128), native output is `#bbbc00ff`; an encoded-byte
  compositor may produce different results. Fully transparent flattened output clears
  hidden RGB, while editable source samples retain it.
- `nativeImageImport:true` describes this profile; `providedArtifactAdmission:false`,
  result `admitted:false`, `provenanceVerified:false` remain explicit. No OS sandbox,
  hostile-writer seal, animation import or Blender execution is implied.

## Preserved project state

The result's `import.recordPath` identifies
`imports/<recordSha256>/record.json` under the asset. Alongside it, `bundle/descriptor.json`
and the declared PNG path preserve exact original bytes. A PNG path beginning
`descriptor.json/` is unsupported by this import layout and rejects before PNG allocation.
The record names decoder/interpretation/execution profiles, original metadata/encoded/raw
hashes, dimensions/pivot and import-time native asset/layer/framed-payload mapping. It is
not a command receipt or verified artifact-set identity. Hash kinds remain distinct:
raw RGBA hashes omit the native 24-byte header; native payload hashes include it.

New asset creation publishes one complete directory without replacement. Layer append
publishes complete immutable history and payload objects before replacing native metadata
last. Known commit success wins over subsequent cancellation; failed pre-commit work keeps
previous chosen references intact. Complete unreferenced historical objects may remain
after a failed append; an orphan record does not prove the native edit committed.

Track original attachments, metadata and required payloads in [project history](git-workflow.md).
Supplier mutation/deletion does not change imported samples. Restore those project files,
re-inspect and continue editing; no runtime cache, selection database or automatic Git is
required. Native validation verifies the current payload closure, not all historical
attachments. Exact restored tokens may become valid again; lost replies do not authorize
blind retries. The normative [v1 contract](contracts/native-still-import-v1.md)
defines canonical record bytes, ownership, publication and error boundaries.
