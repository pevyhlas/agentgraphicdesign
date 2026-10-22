# Agent Graphic Design — data

| Data | Owner part | Lives in | Kept for | Removed by |
|------|------------|----------|----------|------------|
| Vector documents (`.agd.json`) and the `.agd` store | P1 | the user's project, versioned by the user | as long as the user keeps them | the user |
| Native pixel sources (a native asset directory at any caller-chosen locator: `asset.agd-pixels.json`, content-addressed `payloads/*.bin` and, after still import, `imports/<recordSha256>/`) | P1 | the user's project; payloads in Git or Git LFS | as long as the user keeps them; old payloads are never collected implicitly | the user |
| Imported originals and their mapping | P1 | inside the native source | as long as the asset keeps the layer or import | the user |
| Asset manifests and their declared outputs | P1 | the user's project | as long as declared | the user; orphans are reported, never pruned |
| Previews, locks, staging and caches | P1 | local working files | rebuildable; never sole project intent | AGD cleanup or the user |
| Release bundles | none (release tooling) | `artifacts/release-bundle/`, then GitHub Releases | per release | the owner |

Required payloads are preserved by the user through Git, Git LFS or pinned durable storage; a
digest is not a backup (D-08).
