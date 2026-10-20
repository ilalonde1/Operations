# Step 160 — a markup-only read keeps the architect's shafts and stairs

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP160_OFF=1` drops the carried openings (`PdfOnlyBuild`, `GeometryFilterService.OpeningRegions`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): after step 153 read A105 for the engineer's markup,
every lift and stair was floored over. With the whole-page read's X marks, stair wells and stair boxes carried across:
L35 7 openings (freight lift, 4 lifts, stairs A and B), L2 8, L37 7. Only pages step 153 re-reads can move - on the six
banked sets, 0 pages carry structure ink, so byte-identical is expected.

## The fault

A markup-only read sees only the engineer's ink. He marks walls, columns and the floor; the shafts are the architect's.
And the page read wrote an X or a well only where it lay wholly inside one of its own room-fill plates, so the freight
lift and stair B, which straddle fill edges, were never written even on the page.

## What the change is

- `ExtractedGeometry.OpeningRegions`: every X region, every stair well, and for a stair whose well does not form, the
  flights' box grown to the faces of the walls and filled markup round it (within one flight's width,
  `TreadMaxWidthMm`; `BoxToItsWalls`). A "stair"
  whose box exceeds the 60 m² lobby limit is not boxed (L36's deck hatch read as a 111 ft flight).
- `PdfOnlyBuild`: the regions are written into the markup-only geometry as closed rings, so the composer cuts the ones
  inside the engineer's floor.

## What it does NOT do

- A stair drawn with dashed treads gives no flights and no box (143 E 3rd's L36 stair A). Not cut; said in the READ FIRST.

## What must NOT move without a reason

- The six banked sets: no markup-only pages; byte-identical expected. A move means the page read's own openings changed.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP160_OFF

⛔ Run where the drawings are local. Never over the VPN.
