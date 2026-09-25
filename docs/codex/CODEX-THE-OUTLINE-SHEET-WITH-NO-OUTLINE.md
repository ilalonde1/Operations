# CODEX — the CONCRETE OUTLINE sheet that carries no concrete outline

**One defect. Open only the files named in §5.** Do not run the test suite, do not build, do not
touch anything outside those files. Answer in prose with `file:line` citations. **A reasoned refusal
is a valid answer** — the last two audits both produced one and both were right.

---

## 1. The defect, in one sentence

**A drawing whose own title says CONCRETE OUTLINE reaches the composer with walls, columns and
ZERO slab-edge entities, so the storey it draws can never be given a floor — and nothing anywhere
says the outline went missing.**

## 2. The witness, and a picture

`30989-01`, view `S2.15_1_LEVEL 4 19 PLAN CONCRETE OUTLINE.dxf`. Counted from the DXF itself:

| layer | entities |
|---|---:|
| `KOR_V-WALL` | 205 |
| `KOR_V_COL` | 121 |
| **`KOR_C_SLABEDG`** | **0** (the name appears once, as the layer-table declaration) |

Rendered (`takeoff dxf-render`, 216 segments drawn), the sheet is a **column-and-core tower floor
plate**: a lift-and-stair core in the middle, isolated columns on a grid around it, and **no
perimeter of any kind**.

That picture also settles an older question. The engineer's banked ruling
`floor-from-perimeter-wall` says *"it should always follow the outer edge of the walls"* — and on
this sheet there are no perimeter walls to follow. The existing fallback refuses correctly: the only
ring the walls enclose is the 625 sq ft core. **The floor is not recoverable from the walls here. It
is missing because the outline is missing.**

The set builds **27 storeys and 7 floor objects** and reads 54% of the engineer's plate area.

## 3. It is a class, and it is measured

Across the 297 corpus sets' extracted views:

| | |
|---|---:|
| DXF views whose title contains OUTLINE | 1,047 |
| **of those, with no slab-edge entity at all** | **132 — 13%** |
| sets affected | **30 of 297** |

Worst: `01379-01` (29 views), `30941-01` (21), `30993-01` (21), `31005-01` (9), `31168-01` (8),
`30989-01` (7), `31224-01` (6), `30820-01` (4).

⚠ **An empty outline view is not always fatal** — `30993-01` has 21 of them and still reads 91%,
because another view of the same storey carries the edge. So the question is not "why is this view
empty" alone; it is **why the linework a CONCRETE OUTLINE sheet certainly draws did not become slab
edge on these pages.**

## 4. Where this sits relative to work already done

Do not re-walk these; all three are measured and closed.

- **Widening the flood-fill bridge** until the walls close a ring: worth 16,296 sq ft against a
  583,781 sq ft gap on the sets with the engineer's model — 2.8%. Dead.
- **"Most of the storey's walls stand outside the plate"** as a trigger: precision never above 30%.
  Dead.
- **Pooling a storey's walls across its sheets** (step 142): returned null on all 42 combined walls.
  ⚠ A previous audit correctly noted that a null there is not a diagnosis; every return path now
  states its reason, and the first measurement over 16 sets found 18 refusals, all small rooms.

This brief is upstream of all of that. If the outline never became geometry, no closure rule can
help.

## 5. The files — open only these

```
Kor.Operations.EngineeringTools.Core/PdfToSafe/GeometryFilterService.cs
Kor.Operations.EngineeringTools.Core/PdfToSafe/DxfExporter.cs
Kor.Operations.EngineeringTools.Core/Intake/DrawingIntake.cs
Kor.Operations.EngineeringTools.Core/Intake/SheetViews.cs
```

`GeometryFilterService.cs:2764` is where a path becomes a slab edge
(`PathReason.BecameSlabEdge`). `DxfExporter.cs:342` is where the layer name is emitted.
`PathFate` / `Disposition` / `PathReason` record what happened to every path on the page — that
ledger is the evidence, if it survives to where anyone can read it.

## 6. The three questions, in order

1. **What has to be true of a path for it to become a slab edge, and which of those conditions do
   these pages fail?** Name the conditions at `GeometryFilterService.cs:2764` and above it. The
   witness draws walls and columns successfully on the same page, so the page is being read — it is
   the slab-edge classification specifically that yields nothing.

2. **Is the outline being CONSUMED by another role rather than lost?** On this witness the walls
   come to 205 entities. A slab edge that reads as a thin wall, a hatch boundary or an annotation
   would be counted somewhere else and never missed. Say which disposition would swallow it and
   whether `PathFate` already records enough to tell.

3. **Can the tool KNOW it has this fault, from what it already has?** A sheet whose title says
   CONCRETE OUTLINE and which produces no slab edge is self-evidently suspect — the title states the
   drawing's own purpose. Is there a safe check of that shape, and where would it live? ⚠ It must
   not fire on `30993-01`, which has 21 such views and reads 91% because sibling views carry the
   edge.

## 7. What NOT to do

- Do not propose inferring the floor from the columns' extent or convex hull. That is a different
  rule, it needs its own measurement, and it is not what this brief asks.
- Do not propose relaxing a wall or slab thickness threshold to admit more linework. Every such
  change is judged across the 48 sets with the engineer's model, and the guard sets are
  `30972-01` (20 of 20 storeys floored, 85%) and `30993-01` (39 of 40, 91%): if either moves, the
  change is wrong.
- **A wrong floor is worse than a missing one.** It looks exactly like a floor she drew and nothing
  about it asks to be checked.

## 8. Context you may want but should not open

- The one measure: **77.4%** of the engineer's plate area, run 45, 48 comparable sets.
- The closure characterisation and its dead ends:
  `docs/codex/CODEX-THE-FLOOR-THE-WALLS-DO-NOT-CLOSE.md` and its RESPONSE.
- Narrative: `docs/pdf-intake/part-3-s61-onward.md`, log 170.
