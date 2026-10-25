# Sub-node export baseline (T029)

Captured from the unmodified engine at `7887bea7c4025fc47a937e14820efa09dc2fbec7`,
before the transient builder. SVG bytes are portable; PNG SHA-256 values are Linux x64.
Inputs and independent pixel assertions live in `SubNodeCharacterizationTests`.
Never regenerate these to make a refactor pass.

Hand-derived isolated D-10 geometry boxes, before translation to the viewport:

| Case | Legacy crop `(x,y,width,height)` | Derivation |
|------|--------------------------------|------------|
| nested | `(0,50,24,20)` | frame `(5,2,10,8)` scaled `(2,3)`, rotated 90°, translated `(30,40)`; child clipped inside frame; group opacity 0.5 |
| token | `(-3,7,14,10)` | rectangle translated `(-3,7)`; fill resolves `{ink}` to `#13579b` |
| stroke | `(0,0,20,20)` | rectangle geometry only; four-unit centered stroke has actual ink `(-2,-2,24,24)` |

The stroke's two-unit overflow on each side remains clipped by the 20×20 raster
viewport. “Ink” crop retains this legacy geometry-only measurement; it does not
claim stroke-expanded ink accuracy. Ancestor transforms, clips and canvas background
are excluded in every case. The nested case independently pins alpha 128.
