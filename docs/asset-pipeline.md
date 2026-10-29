# Asset Pipeline

The pipeline, included in published **0.3.0**, moves generic generation and drift checking into AGD.
Consumers declare assets in one manifest and keep only application-specific wiring.
The qualified release supports .NET 10 Linux x64; see the [CHANGELOG](../CHANGELOG.md).
Persisted reuse and broader editor domains remain separate work.

Core callers now use [explicit typed-producer composition](asset-producers.md).
The old engine-only pipeline constructor is removed; no migration or forwarding
API is provided. CLI/manifest inputs remain unchanged in this implementation slice.

## Quick Start

Using the development CLI, from the repository root:

```sh
dotnet build FTPlus.AgentGraphicDesign.slnx --configuration Release
dotnet src/FTPlus.AgentGraphicDesign.Cli/bin/Release/net10.0/agd.dll assets build examples/assets.json
dotnet src/FTPlus.AgentGraphicDesign.Cli/bin/Release/net10.0/agd.dll assets check examples/assets.json -f json
```

The checked-in example produces a `currentColor` icon, a text-bearing SVG/PNG
card, and a portable SVG sprite in `examples/out/assets/`. This example output is
ignored by git. Consumer repositories normally commit their generated assets and
run check against them in CI.

With the verified installed 0.3.0 CLI, the same commands are:

```sh
agd assets build assets.json
agd assets check assets.json -f json
```

These commands first shipped in 0.2.0-preview.2 and are in 0.3.0; the first release, 0.1.0, does not contain them.

## Manifest Version 1

```json
{
  "version": 1,
  "outputRoot": "generated",
  "defaults": { "strict": true, "profile": "web-icon" },
  "assets": [
    {
      "source": "sources/power.agd.json",
      "output": "generated/power.svg",
      "options": { "currentColor": "#000000" }
    },
    {
      "source": "sources/card.agd.json",
      "output": "generated/card.png",
      "options": { "profile": null, "scale": 2 }
    }
  ],
  "sprites": [
    {
      "sources": ["sources/power.agd.json", "sources/warning.agd.json"],
      "output": "generated/icons.svg",
      "options": { "currentColor": "#000000" }
    }
  ]
}
```

All paths, including `output`, are relative to the manifest directory, not to
`outputRoot` or the invoking shell. Put the manifest at the common ancestor of
your source and output directories. Paths use forward slashes; absolute paths,
`..`, globs, device names, and symlinks/reparse points are rejected. Hard-linked
regular files are permitted; publication replaces the destination directory entry
rather than truncating its previous inode.

`outputRoot` declares ownership of an entire generated-only subtree. Every file
under it must be a declared output, including files in nested directories. Keep
handwritten files, framework wrappers, sources, fonts, and the manifest outside
that root. Case-insensitive collisions, including conflicting existing filename
or directory casing, fail before publication rather than adopting an orphan.

`assets` and `sprites` are explicit ordered lists; either can be omitted, but the
batch must be nonempty. Unknown/duplicate JSON fields and invalid versions fail.
The output extension selects SVG, PNG, JPEG (`.jpg`/`.jpeg`), or WebP. Sprite
outputs must be SVG; symbol IDs come from the source document names. Repeated or
invalid symbol IDs and colliding gradient/clip IDs are rejected by the same
composer used by `agd sprite`.

## Export Options

Per-entry `options` inherit `defaults`; omitted fields inherit, while explicit
`null` clears an inherited setting and restores its built-in default.

| Option | Default | Meaning |
|--------|---------|---------|
| `strict` | `true` | Reject validation warnings as well as errors. Validation always runs. |
| `profile` | No additional profile | `web-icon` restricts output to self-contained geometric scenes. |
| `currentColor` | None | SVG-only exact resolved-paint substitution; gradients/background remain unchanged. |
| `scale` | `1` | Raster-only scale, 0.1 to 4. |
| `width`, `height` | Document canvas | Raster-only viewport overrides, 1 to 8192 before scale. These do not fit/rescale geometry; use `scale` to scale the scene. |
| `fontDirs` | `[]` | Raster-only explicit, non-recursive font directories, relative to the manifest. |
| `systemFonts` | `false` | Raster-only opt-in to host fonts; weakens reproducibility. |
| `strictFonts` | `true` | Raster-only missing-family failure instead of substitution. |

For 128×128 artwork at 32×32 pixels, use `"options": { "scale": 0.25 }`
and omit width/height. A 128×64 canvas with the same scale becomes 32×16.
`"width": 32` alone instead selects a 32-unit viewport and crops the artwork;
it does not shrink 128 units into 32 pixels. Combining `"width": 64` with
`"scale": 0.25` produces a 16-pixel-wide output of that cropped viewport.

Explicit raster options on SVG, or `currentColor` on raster, are errors. Use
per-entry options or null clearing for mixed batches. Font diagnostics are returned
with source/output paths, including substitutions when `strictFonts` is false.
Validation strictness is separate from font policy and design lint.

Keep monochrome icons and full-colour illustrations distinct. An alpha mask uses
shape, an inline symbol can inherit CSS colour, and an external `<img>` does not
inherit page `currentColor`. The pipeline does not choose application semantics.

## Build and Check

Build validates and renders the whole batch into bounded memory before publishing
any output. Invalid later inputs or render failures leave earlier outputs untouched.
Unchanged files retain their bytes and modification times. Changed files use
same-directory temporary files and atomic replacement. Publication is atomic per
file, not crash-atomic across the whole batch; a filesystem failure during publication
can leave a partially updated batch. Re-run build/check after resolving the failure.

Check regenerates in memory and compares exact bytes plus the complete output
inventory. It creates no output directories, document store, lock files, or temporary
files. It never repairs or deletes anything. Reading may update filesystem access
times according to the host mount policy.

| File status | Meaning |
|-------------|---------|
| `unchanged` | Output matches generated bytes. |
| `written` | Build created or replaced a declared output. |
| `missing` | Check found no declared output. |
| `stale` | Check found different output bytes. |
| `unexpected` | File exists under the owned root but is absent from the manifest. |

Build also reports unexpected files and exits nonzero, but still updates declared
outputs. It never prunes unexpected files automatically. Remove obsolete assets
intentionally after reviewing the report.

JSON uses the normal AGD envelope. Successful runs have `ok: true`; drift has
`ok: false`, `error.code: "ASSET_DRIFT"`, and retains `data` containing `check`,
`success`, `files`, and `diagnostics` so CI can explain every difference.
Text-mode paths are JSON-quoted to escape control characters in discovered orphan
names. JSON paths retain actual Unix backslashes rather than turning them into
different directory paths.

Exit codes are 0 (clean), 6 (asset drift), 4 (invalid manifest arguments/version),
1 (validation, export, path, platform, or I/O failure), and 130 (cancellation).
Renderer diagnostics are capped at 256 entries with a truncation marker.

## Limits and Portability

Batches are bounded to 256 document occurrences (including sprite members), a
1 MiB manifest, 64 MiB aggregate document/font inputs and generated output (32 MiB per asset),
100,000 processed nodes, 64 million raster pixels, 8192 pixels per effective
raster dimension, and 4096 output-inventory entries. Repeated sources count toward
the work budget. Existing per-document and renderer bounds also apply.

**0.3.0's Unix file inspection is qualified only on .NET 10 Linux x64.**
It uses the pinned runtime's native metadata ABI to reject FIFOs/devices without
opening them. Other Unix/runtime/architecture combinations fail closed with
`ASSET_PLATFORM_UNSUPPORTED`. Development source's S1 registry admits Windows x64 on
.NET 10 unqualified using public file attributes; Windows arm64 and other runtime
majors now refuse. S1 also admits .NET10 macOS26+ arm64 asset inspection unqualified,
using the runtime PAL and bounded stored-spelling/normalization collision checks before writes.
No additional platform is qualified. Static path checks
assume a trusted local workspace, not hostile concurrent filesystem replacement.

Default raster font policy uses bundled Inter/explicit fonts without host lookup.
SVG text still depends on viewer fonts, and the existing SVG layout measurer does
not share per-entry raster font options. Cross-platform text determinism is not
claimed by this pipeline; shared font policy remains a prerequisite to release.

## Consumer Contract

AGD owns manifest parsing, safe export, sprite composition, colour substitution,
and drift reporting. Consumers own CSS/accessibility, output URLs, visual review,
framework wrappers, and approved tool installation. Do not add shell hooks or
framework-specific code generation (UI components, game-engine resources) to the
manifest.

The pipeline is offline: it never downloads packages or trusts a global tool
implicitly. CI must acquire an approved bundle separately, verify the package
SHA-256 before execution, install from an isolated feed, and verify the exact
version/source identity. A manifest read by an already-running executable cannot
authenticate that executable. Missing required tooling must not count as a passing
check. A reusable verified bootstrap and package qualification remain release work.

A project that scripts per-file validation and export, recolouring, sprite
composition and drift checking can replace those scripts with these commands.
Framework-specific wrapping and browser visual tests remain consumer concerns.
