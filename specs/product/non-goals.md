# Agent Graphic Design — non-goals

<!-- What the product deliberately does not do. Past the budget, split into
non-goals/<area>.md and keep one line and a link per area here. -->

- **N-01 Live collaboration**: no simultaneous editing of one canvas, presence or live sync
  (C-05). Why: the workflow is solo authors, agents and pull-request review; live sync adds
  much complexity for little value there. Revisit when: the owner reopens D-02.
- **N-02 Team access control**: no team management, permission hierarchies or organization
  structures; a future multi-user mode has owner and reviewer roles only. Why: AGD is not a
  team-collaboration tool; simple scopes suffice. Revisit when: a multi-user mode is selected.
- **N-03 A GUI-first product**: no editor built around a person at a screen, and no GUI that
  agents would have to automate (C-04). Why: agents are the primary users. Revisit when: never
  by an agent.
- **N-04 A game engine**: no game state machines, damage rules, navigation, world generation or
  runtime asset-cache policy. Tile arrangement and frame playback are editor validation and
  preview only. Why: those belong to each game. Revisit when: never by an agent.
- **N-05 Consumer-owned concerns**: no consumer framework wiring, application accessibility
  labels or visual approval for consumers, no arbitrary shell hooks and no AGD runtime inside
  every application. Typed, pure export target adapters are intended scope, not a non-goal: the
  brand-workflow pilot is their named use case (change 007 design, change 013 slice S06). Only
  atlas packing still waits for a named consumer use case (change 014). Why: each project owns
  its own integration. Revisit when: a consumer asks AGD to own its framework wiring.
- **N-06 A public package feed now**: releases are GitHub Release assets pinned by checksum, not
  a NuGet feed. Why: the owner has not selected one; consumers pin each release by checksum.
  Revisit when: the owner selects a feed.
