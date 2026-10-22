# Agent Graphic Design — qualities

<!-- Q- items: a band and its method, never a tuning number. Past the budget, split into
qualities/<area>.md and keep one line and a link per area here. -->

- **Q-01 Deterministic output** (validated): the same document and options give byte-identical
  serialization and exports on the supported profile; reviewed golden outputs reproduce from
  installed packages. Journeys: J-01, J-02, J-03, J-04. Enforced by: serializer,
  export-regression and package-verification tests.
- **Q-02 Honest fidelity** (validated): import and export preserve supported semantics or
  report the loss as a structured warning or error; nothing is dropped silently, and no result
  claims admission, provenance or qualification that was not performed. Journeys: J-01, J-03,
  J-04. Enforced by: import and export warning tests; owner review for new adapters.
- **Q-03 Bounded work** (validated): every agent-facing operation bounds input size, scanned
  nodes, output dimensions and decode work, and fails with a structured limit or busy error
  rather than unbounded work. Journeys: J-01, J-04. Enforced by: limit and hardening tests.
- **Q-04 Safe writes** (validated): a stale writer gets a revision conflict instead of losing
  another write when it supplies the expected revision (native pixel edits always require it);
  a failed or interrupted write leaves the previous or the new source, never a partial one (no
  power-loss guarantee). Journeys: J-01, J-04. Enforced by: store, patch and native-edit tests.
- **Q-05 Supported-platform honesty** (validated, planned: 008-macos-qualification): file
  safety is qualified only on .NET 10 Linux x64 now; Apple silicon on APFS with macOS 26 or
  later is planned, with its own CI lane. One shared asset file probe guards the asset
  pipeline, artifact preflight, still import and native pixel storage: it allows .NET 10 Linux
  x64 and fails closed with `ASSET_PLATFORM_UNSUPPORTED` on every other profile except
  Windows, whose unqualified pass-through is stated in P1.I-09. Qualifying a further profile
  is the owner's decision, and every unqualified profile must fail closed for the guarded
  operations (C-11); the Windows pass-through deviates from that (inbox). Vector document
  reads and writes are not guarded by the probe: they run on every profile, and their
  file-safety promise (Q-04, P1.I-02) is qualified only on .NET 10 Linux x64 (inbox).
  Journeys: J-03, J-04, J-05. Enforced by: `AssetPipelineTests` (platform guard); the Windows
  pass-through and unguarded vector document storage are unenforced (inbox).
- **Q-06 Reproducible releases** (validated): a release bundle is built from a clean archive
  of one commit with checksums and provenance, and scripts never publish it. Journeys: J-05.
  Enforced by: the release-bundle script at release time and package smoke in CI.
