# Owner inbox

<!-- Open items only: questions, then owner direction awaiting incorporation; oldest first within
each. Each item is a `###` heading, then `*<YYYY-MM-DD> · <owner|agent> · open*`.

An agent question: a heading that names the decision, a sentence or two of context with links,
numbered options as `**<label>**: <what it means>`, the recommended one first and marked
(Recommended), and, when the choice is reversible, `**If no answer**: <default>`. The owner
answers with an option number or their own words.

An owner item: a short heading, the owner's words verbatim as a `>` quote (also when relayed
verbatim by another session), and one line on what it answers.

Name roles, never people. Incorporate resolved answers and provenance in canonical spec/policy,
transfer remaining work/holds, then remove the item in the same edit. Partial answers remain open;
deferrals belong at their canonical destination with a revisit trigger. -->

## Waiting for you

### Confirm the restructured spec

*2026-10-20 · agent · open*

The spec now follows the Product-Kit 1.1 layout: [product.md](product.md) with journeys and
detail files under [product/](product/), [architecture.md](architecture.md) with topics, and two
parts. Owner-stated requirements, journeys J-01 to J-05 and the running parts P1 and P4 are
marked validated. The restructure also settled two conflicts in the earlier canon as the code
stands: canonical output writes typed properties in a fixed order and sorts dictionary keys
ordinally (P1.B-01), and the earlier stack's HTTP host and container images, which have no code,
are later modes under D-02 rather than current stack.

1. **Confirm** (Recommended): the spec is current canon, including the validated markings and
   both settlements.
2. **Correct**: name what to change, such as one rule for key order in code instead.

**If no answer**: treat it as current canon meanwhile.

### Accept the implemented decisions D-10 and D-11

*2026-10-20 · agent · open*

Sub-node export and bounded SVG reference import shipped in 0.3.0 and are tested, but their
decisions were never moved past proposed ([decisions](architecture/decisions.md)).

1. **Accept both** (Recommended): record D-10 and D-11 as accepted.
2. **Keep proposed**: behavior is unchanged either way.

**If no answer**: keep them recorded as proposed and implemented.

### Accept the Blender decision D-09

*2026-10-20 · agent · open*

D-09 is recorded as proposed, while the earlier product canon treated an external Blender
producer as settled direction; C-07 already binds.

1. **Accept D-09** (Recommended): the external producer is the interop model; the admission
   contract stays open under R-11.
2. **Keep proposed**: C-07 still holds as a constraint.

**If no answer**: keep D-09 proposed; C-07 holds.

### Which stage the deferred engineering rules are due before

*2026-10-20 · agent · open*

The constitution defers multi-user locking and authentication, server distribution and a public
package feed, durable multi-file operations and hosted hardening until the mode that needs them
is selected; Product-Kit 1.1 asks each deferred rule to name the stage it is due before.

1. **Due before GA, or earlier if the mode is selected** (Recommended): no rule is weakened, and
   determinism, fidelity, licensing and self-host rules stay always on.
2. **Only when the mode is selected**: no stage bound.

**If no answer**: keep them deferred until the mode is selected, as now.

### Stage of the planned domains and the first one to build

*2026-10-20 · agent · open*

R-09 sprites, R-10 tiles, R-11 artifact admission and R-12 raster artwork are owner-decided but
unscheduled, now planned on [change 014](changes/014-full-editor-domains.md); their journeys
J-06 to J-08 are outlined hypotheses, and no stage marks them.

1. **Unscheduled, no stage marker** (Recommended until a domain is chosen): build none early.
2. **Pick a first domain** and the stage it is needed from; its journey is then shaped with you.

**If no answer**: leave them unscheduled with no stage marker.

### When CI runs doctor in strict mode

*2026-10-20 · agent · open*

CI runs the Product-Kit doctor non-strict. Strict mode fails while inbox questions are open,
and on the [archive](archive.md)'s missing Base commit: the pre-public history cannot be named
here, but this repository's first commit can be recorded as Base once it exists.

1. **Switch to strict once this inbox has no open questions and Base names this repository's
   first commit** (Recommended).
2. **Stay non-strict.**

**If no answer**: stay non-strict.

### Where the normative native pixel detail lives

*2026-10-20 · agent · open*

The [native pixels component](parts/p1-core-engine/native-pixels.md) is the authority but links
the v1 storage, evaluation, API and still-import contracts, now under `docs/contracts/`, for
byte layouts, arithmetic tables and error matrices it does not restate; where they differ, the
component wins.

1. **Accept the split** (Recommended) until a change touching native pixels moves the normative
   detail into the spec.
2. **Move it now.**

**If no answer**: keep the split.

### Product-Kit as a vendored copy or a pinned submodule

*2026-10-20 · agent · open*

Product-Kit is vendored only: copies under `.prdkit/` and the harness command folders, with no
submodule or adoption pin, so the adoption check verifies the manifest hash and command surface
but cannot check pin drift. Product-Kit 1.2.1 is now publicly released, and the vendored notice
reproduces its license verbatim.

1. **Re-add a submodule pinned to the public v1.2.1** (Recommended after this repository is
   public): restores pin-drift checks; a clone then fetches the kit repository.
2. **Stay vendored only**: no outside dependency in a clone.

**If no answer**: stay vendored only.

### Reject Windows in the shared file probe or record the pass-through

*2026-10-20 · agent · open*

[C-11](product/constraints.md) says unqualified platforms fail closed. The shared asset file probe
(P1.I-09) still retains D-28 S0's Windows x64 .NET 10 exception: asset pipeline build/check
(including output writes) and artifact preflight run there unqualified. Child023 narrows all
Windows native operations, Windows arm64 and runtime majors other than10 to capability refusal.
The remaining pipeline/artifact pass-through is still a deviation, not an owner-ratified exception
([P1.I-09](parts/p1-core-engine/asset-pipeline.md), [Q-05](product/qualities.md)).

1. **Reject Windows in the probe** (Recommended): one change makes every guarded path fail
   closed with `ASSET_PLATFORM_UNSUPPORTED` off .NET 10 Linux x64, with the platform-guard test
   updated to match; matches C-11.
2. **Record the Windows pass-through**: keep it as a known unqualified path, with a test that
   pins the current Windows behavior, and narrow C-11 to name the exception.

**If no answer**: no code change; the spec states current behavior.

### Guard vector document storage or keep it unguarded off-profile

*2026-10-22 · agent · open*

[C-11](product/constraints.md) limits fail-closed behavior to the operations the shared probe
guards. Vector document reads and writes ([P1.I-02](parts/p1-core-engine/vector-documents.md):
cooperative lock and atomic replace) have no platform guard, so they run unqualified on macOS,
Windows and every other profile ([Q-04, Q-05](product/qualities.md)).

1. **Keep vector storage unguarded** (Recommended): vector authoring stays usable on every
   profile, C-11 and Q-05 keep naming it as unqualified off-profile, and change 008 qualifies
   it for Apple silicon macOS with the other operations.
2. **Guard vector storage**: route document reads and writes through a platform check that
   fails closed off the qualified profile, with tests; vector authoring stops on macOS until
   change 008 lands.

**If no answer**: no code change; the spec states current behavior.

### Confirm the lifecycle stage

*2026-10-22 · agent · open*

You set the status to experimental; [lifecycle.md](product/lifecycle.md) now names Public
preview, with an "experimental, interfaces may change" promise and proposed exit criteria.

1. **Public preview** (Recommended): matches an experimental public source release.
2. **Private preview**: invited users only.
3. **Another stage**, or different exit criteria.

**If no answer**: Public preview with the proposed exit criteria.

### How in-flight pre-public work lands here

*2026-10-22 · agent · open*

Changes [006](changes/006-agent-tool-usability.md) to [012](changes/012-review-analysis.md)
under [013](changes/013-brand-workflow-uptake.md) were in flight before this repository started
from fresh history (designs approved; 006, 010 and the first pieces of 008, 011 and 012
implemented and reviewed in the pre-public history), so their branches cannot merge here. What
is public here is the binding contracts, decisions [D-21 to D-29](architecture/decisions.md), and
per change a record with its bounds, task increments, grouped acceptance and review outcomes. The
full text of each acceptance criterion (008/AC-01 and the like) and task (011/T005 and the like),
and the review reports, stay in the pre-public history; each porting pull request restates the
criteria and tasks it closes in its body and its change record. Changes 006 and 010 already
landed publicly (PRs #5 and #4). Remaining source ports include 008's first increment (the
platform registry), 011's special-file gate and 012's raw RGBA/legacy characterization in child 018.

1. **Port each change as its own pull request here** (Recommended): reapply its reviewed work
   onto this tree with the criteria and tasks it closes restated, rerun CI and the recorded
   reviews, then continue.
2. **Re-implement them here** instead of porting commits: needs the criteria and task definitions
   restated first from the pre-public history, since the change records carry only group labels.
3. **Drop or restart** some of them; name which.

**If no answer**: continue the remaining reviewed ports in roadmap order.

### Platform class of glyph-outline geometry

*2026-10-22 · agent · open*

Text-to-outline results come from the platform's font scaler and may differ in the last digit
between hosts; only the text-outline increment of [change 011](changes/011-geometry-fidelity.md)
waits on this ([D-21](decisions/D-21.md), Determinism).

1. **Declare a per-platform class for text outline results** (Recommended): no new parser; an
   honest flag; converted outlines are ordinary cross-platform paths afterwards.
2. **A managed TrueType reader** for bundled and strict fonts: cross-platform outlines; a bounded
   parser of a few hundred lines plus fuzzing; CFF fonts still need the scaler.
3. **Treat glyph differences as a qualification defect**: unfixable short of option 2; blocks
   text outlines on macOS.

**If no answer**: option 1 as the working assumption; the text-outline increment stays gated.

## Your direction
