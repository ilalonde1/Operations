# Codex 8 of N — the DXF carries what he DREW, on a layer saying what he MEANT

## Goal

`DxfExporter` should write a wall as the **closed outline the engineer drew**, on a layer named for
the **element type he assigned**. Today it writes a centreline, and the DXF-to-ETABS intake cannot
read one.

## The evidence

Omar's markup, with his own assignment applied (`ReclassifyByColor`, red → Wall), exported to DXF
and fed to the real intake:

```
takeoff dxf-inspect kor-convergence-markup-as-walls.dxf

  BEAM-MARKUP     segs    1   closed loops   0   open   1
  COLUMN-MARKUP   segs  148   closed loops  37   open   0
  WALL-MARKUP     segs    5   closed loops   0   open   5
      loose ends 10   gap median 2083   max 2700

takeoff dxf-to-etabs …
  "no structural outlines found on the expected layers — not placed."
```

The columns close. The walls do not, and the gaps are metres wide, because they are not open
outlines at all — **they are centrelines.**

## Why, and it is not a bug in either side

The two halves model a wall differently, and both are internally right:

| | a wall is… | where |
|---|---|---|
| PdfToSafe | a **centreline plus a section** | `ReclassifyByColor` turns the polygon into a `Lines` entry with a `LineSectionHints` (Width, Depth) |
| DXF → ETABS | a **closed outline with two parallel faces** | `WallOutlineDecomposer.Decompose(PlanLoop loop, …)` |

PdfToSafe's is right for SAFE, which wants a wall as a line with a section. The intake's is right for
a DXF, which is a DRAWING: a draughtsman draws a wall as its two faces, and that is exactly what the
Revit bridge emits on `JBP_V-WALL`.

**And what Omar drew was a closed polygon** — a Bluebeam Square. The centreline is something the tool
derived on the way to SAFE. The DXF should carry what he drew.

## What to change

**Give `DxfExporter.Export` the colour→`SlabColorSettings` map** — the same
`Dictionary<(byte,byte,byte), SlabColorSettings>` that `SafeF2kExporter.Export` already takes — and
use it for the LAYER NAME only:

- a shape whose colour he assigned "Wall" goes on `WALL` (or `WALL-MARKUP`), **with its geometry
  unchanged** — the closed polygon he drew
- likewise Column, Slab, Beam
- unassigned colours keep today's behaviour, the size-based classification

So the layer carries his meaning and the geometry carries his drawing. Nothing is reconstructed,
nothing is thickened, and no centreline is invented or consumed.

The caller then exports the geometry **before** `ReclassifyByColor` — or, if that is awkward,
whatever preserves the original closed shapes. Say which you chose and why.

## What NOT to do

- **Do not change `ReclassifyByColor`.** It is right for the SAFE/CSI path, which is what it exists
  for, and that path is not in question.
- Do not reconstruct outlines from `LineSectionHints` by thickening a centreline. The real polygon
  is still available; a reconstruction would be a worse copy of something we already have.
- Do not touch `EngineeringTools.Core`, the classifier, or `WallOutlineDecomposer`.
- Do not rename layers to match the Revit patterns (`_COL`, `SLABEDG`). Still a separate rules
  decision.
- Do not build, do not run tests, do not publish. I verify.
- No destructive operations: no deletions, no git commands, no schema changes.

## What I will check

- `dxf-inspect` on the result: `WALL-MARKUP` shows **closed loops, not open segments**
- `dxf-to-etabs` gets past *"no structural outlines found on the expected layers"* — that sentence
  is the one this whole prompt exists to delete
- his 5 wall shapes still measure what he drew (his own dimension line still reads 20'-5")
- both suites, and every existing PdfToSafe gate
- the SAFE/CSI export path is untouched
