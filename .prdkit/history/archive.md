# Agent Graphic Design — archive

<!-- Past tense, condensed: directions tried, why they stopped, lessons and notable outcomes.
What still binds lives in the spec, never here. Agents read this file on demand (a history
question, before re-trying a direction), never by default. Each entry is a few lines; its
details stay in git at the base. Past the budget, condense older entries harder.
new-change.sh reads the Base and Last lines to never reuse an old feature number. -->

**Base**: the pre-public main branch, before this repository started from fresh history; that
history is not part of this repository.

**Last 0.x feature**: 005; new changes number above it.

## Releases before the public repository

- **Outcome**: v0.1.0 (2026-09-02), the v0.2.0-preview.2 prerelease and stable v0.3.0
  (2026-10-01) were published as GitHub Release bundles, qualified on .NET 10 Linux x64 only.
  Their assets are not republished here; the [changelog](../CHANGELOG.md) records each.
- **Lesson**: build every release from a clean archive of one commit, freeze the bundle, and
  verify the hosted bytes against it; later evidence commits never change a release.

## The 0.x product canon and the 1.0 cutover

- **Tried**: a 0.x canon of product spec, domain architecture documents, eleven ADRs, a
  roadmap, open decisions and feature folders, then a 1.0 cutover that kept them read-only
  under an archive folder beside a condensed spec.
- **Stopped because**: two canons surfaced in searches and conflicted; the 1.1 restructure
  folded what binds into the spec, moved the shipped v1 contracts to `docs/contracts/`, and
  removed the rest.
- **Lesson**: one canon on main; history in git and in this file.

## 0.1 stabilization (feature 001)

- **Outcome**: released v0.1.0 with a reproducible bundle and provenance; document format 1
  became the vector contract.
- **Stopped**: its task to regenerate a downstream consumer's assets with the packaged tools
  never ran inside AGD; consumer uptake stayed consumer-owned (R-05).

## 0.2 design with feedback

- **Outcome**: atomic patches, bounds and layout helpers, text layout, lint, contact sheets and
  deterministic fonts (bundled Inter). The exit test, three cards in five MCP calls, passed as
  an executable recipe (`docs/mcp-card-recipe.md`).

## Full-editor design and native pixels (feature 003)

- **Tried**: one design for the full editor (vector, pixels, raster artwork, animation, sprites
  and tiles) with work packages and gates; delivered provided-artifact metadata, typed
  producers, preflight, managed PNG verification and native layered stills (0.2.0-preview.2).
- **Stopped**: the remaining gates (identity, time, topology, sprites, tiles, admission,
  atlases, effects, UI) were not scheduled; they live on as change 014 and D-04.
- **Lessons**: native zlib and SharpZipLib were rejected for strict verification because they
  accepted invalid streams, so AGD wrote its own bounded managed decoder; an OS-sandbox
  requirement (bubblewrap or systemd) and a copyleft host-utility exception were proposed and
  withdrawn; the owner set permissive, not MIT-only, licensing (D-01).

## SQLite commit, receipt and selection store

- **Tried**: a standalone SQLite store for commits, receipts and asset selection, with its own
  CLI and MCP surfaces; never released.
- **Stopped because**: the owner chose Git-first project state (D-08); a ledger without a
  concrete workflow added a database and duplicated history.
- **Lesson**: durable jobs, retries and semantic undo need a named workflow and their own
  qualification; Git is not their substitute either.

## AGD-hosted Blender worker

- **Tried**: an optional prepared-scene worker that would run a separately installed Blender
  under policy, isolation and reproducibility gates.
- **Stopped because**: the owner chose an external Blender producer that owns its GPL boundary
  (D-09, C-07); AGD only verifies and imports its exports.
- **Lesson**: keep GPL execution and Python outside AGD; co-design admission with the
  producer's export contract (R-11).

## 0.3 reusable assets (feature 002, target 0.3 reuse close-out)

- **Outcome**: the manifest asset pipeline, arc geometry, sub-node export (D-10), bounded SVG
  reference import (D-11), unified font policy and stable 0.3.0 on Linux x64, with uptake
  guidance for consumers.
- **Stopped**: the persisted-instance half of reuse (token overrides) needed a format review and
  was not selected; its first slice is now R-13. Non-Linux-x64 platforms stayed owner-gated;
  macOS is now change 008.
- **Lesson**: AGD owns generic pipeline mechanics; consumers keep framework wiring,
  accessibility and visual approval.

## Authoring reliability (feature 004)

- **Outcome**: documents load with a `type` discriminator in any position, and file validation
  and asset batches no longer create a store in the invoking directory (P1.B-02, P1.B-08).
  Unreleased: in the development source after 0.3.0.
- **Lesson**: read-only and file-only commands must never create state where they run.

## Still image import (feature 005)

- **Outcome**: one provided PNG still imports into a new native asset or an independent top
  layer, keeping the original input and its mapping; results always say nothing was admitted
  (P1.B-13). Unreleased: in the development source after 0.3.0.
- **Lesson**: keep interpretation explicit and retained, and keep import distinct from admission.

## Choices rejected along the way

- SixLabors.ImageSharp (split commercial terms), copying Penpot code (MPL) and Svg.Skia (not
  needed: the importer uses in-box XML APIs).
- Range or family dependency approvals with text-parsed audits, replaced by exact approvals and
  a structured audit.
- A "sorted keys" serialization claim: the code writes typed properties in a fixed order and
  sorts dictionary keys ordinally (P1.B-01).
