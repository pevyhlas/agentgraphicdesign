# Agent Graphic Design — why it works

### Reviewable, reproducible sources (R-04, Q-01)

- **Mechanism**: one canonical serializer writes typed properties in a fixed order, sorts
  dictionary keys ordinally and fixes precision; stable GUID ids and fractional sibling indices
  make a property edit or a reorder a one-line diff. Native pixels keep metadata as canonical
  JSON and pixels as immutable content-addressed payloads.
- **Assumptions**: reviewers read JSON diffs and rendered previews; payloads are kept in Git or
  Git LFS.
- **Alternatives**: SVG-native storage (reorders attributes and caps editor semantics); a zipped
  JSON bundle as in other editors (not diffable); one universal graphics model (forces sprite
  and tile semantics into vector nodes).
- **Revisit when**: a format needs content that cannot be diffed or previewed.

### One engine for every surface (R-01, C-04)

- **Mechanism**: CLI and MCP are in-process adapters over P1; shared services own every write
  and check; a manifest test binds registered commands and tools to the source.
- **Assumptions**: local single-user or agent use; a future UI or HTTP host is another adapter.
- **Alternatives**: separate CLI and MCP implementations (drift); a generic MCP wrapper that
  shells out to the CLI (off the shelf, but loses typed results, bounds and confinement); an
  HTTP service first (deferred until a deployment need, D-02).
- **Revisit when**: a hosted or multi-user mode is selected.

### Few bounded calls per task (J-01, R-03, Q-03)

- **Mechanism**: capability discovery, atomic patches of many edits, layout helpers, paged and
  subtree inspection and small previews replace per-element loops; every operation checks its
  published limits before work. The three-card recipe (`docs/mcp-card-recipe.md`) runs as a
  protocol test in five MCP calls.
- **Assumptions**: agents batch edits when the surface allows it; the recipe is representative
  of real layout tasks.
- **Alternatives**: one call per node or pixel (context and latency grow with the asset);
  agent-written SVG or scripts (the status quo the bet replaces).
- **Revisit when**: real use shows agents needing far more calls than a person would.

### Asset sets without project scripts (J-03, R-05)

- **Mechanism**: one manifest declares every output; build writes exactly those files, each
  atomically, through typed in-process producers that charge budgets before work; check reads
  only and reports drift and leftovers as results.
- **Assumptions**: consumers keep framework wiring, accessibility and visual approval; outputs
  are committed or rebuilt in CI.
- **Alternatives**: per-project export scripts (drift and duplication across projects);
  general build tools driving SVG converters (off the shelf, but no output ownership or drift
  semantics, and other runtimes, C-03).
- **Revisit when**: adopters still keep their own export or check scripts.

### Safe writes without a transaction log (Q-04, C-06)

- **Mechanism**: vector writes take a cooperative lock, check an optional expected revision and
  atomically replace the file; native edits require the caller's snapshot token, write
  immutable payloads first and publish metadata last.
- **Assumptions**: no other process rewrites the tree during an operation; Git operations
  happen with writers paused. No power-loss or cross-file atomicity is claimed.
- **Alternatives**: SQLite receipts and selection (withdrawn, D-08); Git as the transaction log
  (restores revisions but is not identity, undo or deduplication).
- **Revisit when**: a workflow needs durable retry, semantic undo or multi-file publication.

### Native pixel semantics owned by AGD (R-06, Q-01)

- **Mechanism**: a versioned fixed-point linear-light source-over evaluation with independent
  fixtures defines composite results; codecs only encode and decode exact RGBA8.
- **Assumptions**: sRGB straight-alpha RGBA8 stills are the first useful subset.
- **Alternatives**: let Skia compositing define results (changes with library versions);
  vector image nodes holding bitmaps (violates C-09).
- **Revisit when**: indexed palettes, transforms or animation need new arithmetic (a new
  evaluation version).

### External producers instead of hosted Blender (R-11, C-07)

- **Mechanism**: a separate producer project runs Blender and exports PNG stills with sidecars;
  AGD preflights, verifies and imports them as data only.
- **Assumptions**: the producer publishes a consumer-export contract to co-design admission.
- **Alternatives**: an AGD-hosted prepared-scene worker (retired as the default, D-09); a general
  `bpy` bridge or embedding (rejected, C-03 and C-07); a hosted render service (off the shelf,
  but breaks self-hosting, C-02).
- **Revisit when**: the owner makes a fresh decision on hosted execution.
