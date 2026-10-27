# Step 183 — a floor the drawing zones by thickness is modelled as its zones

## Written 2026-10-25, before any code, from what the drawing and the engineer's model were LOOKED at. Design, not yet built.

**Knob (to be):** `KOR_STEP183_OFF=1` unites the floor's cells into one plate as before (steps 78/98).

**Target set:** 1 witness, 31065-01 L1 (S2.04.1.1 north, S2.04.2.1 south). Reach on the corpus NOT known: every plate the
classifier refuses for two or more call-outs is a candidate. On the six banked sets that is 4 plates (31065 L1N, 31130
L16 east, 31138 L1, 31168 L2 bldg C); 3 more are now decided by step 182.

## What the drawing does (S2.04.1.1, p24, looked at with each pen drawn alone)

- **Heavy solid pen (0.96, black):** the outer edge, the openings, AND interior edges where the floor changes - the
  slanted edge from grid 5 to the crane opening, the box round grids 6-7, the edges along the east core walls. These
  bound the engineer's transfer bands (the 1805 mm band follows the slanted edge exactly).
- **Dashed (0.6 black, drawn as separate dash segments - the PDF carries no dash pattern, 23,289 of 23,617 strokes are
  solid):** zone outlines - "500 SLAB + STEP" / "600 SLAB + STEP" in the east bays, the 900 band, the floor above's edge.
- **Call-outs** «N SLAB» and «N SLAB + STEP» stand inside the zone they name; «+ STEP» stands across a dashed line.
- Grey (0.24, 0.67) = stirrup patterns (SG1-4) and grid; NOT zones.

## What the engineer models (yardstick L1, both towers, 143 plates)

900 field 37,540 sq ft; 300 x10 9,120; 500 x5 5,191; transfer bands 1,040-1,805 mm (~5,200 sq ft) along the heavy interior
edges; 110 "Stiff1" 1,945 mm stiffeners on walls (not slabs - out of scope). Non-overlapping plates, one thickness each.

## What we do now

The slab pass (GeometryFilterService, step 78) builds the planar arrangement of the slab-edge lines: 3,955 cells on L1N,
the heavy interior edges dividing them (largest 4,302 / 2,060 / 1,619 / 1,463 sq ft). It then UNITES the floor's cells
into one plate (PlanarRings.RecoverSurfaces). The dashed outlines arrive as dash pieces (524 dangling ends) and divide
nothing. The classifier gets one 19,352 sq ft ring with 300/500/600/900 printed inside: refused, 12" default.

## Measured before building, 2026-10-25 evening (the design below is CORRECTED by these)

1. **The heavy pen alone does not zone the floor.** A trace-only study in the slab pass (cells joined across every edge not
   on the outline's pen; `slab pass: step 183 study`, traced through SlabPassTraceProbe, KOR_SLAB_TRACE_JOB=31065-01
   KOR_SLAB_TRACE_PAGES=24): 3 large zones, 14,247 sq ft printing 900/600/500/300, 3,697 printing 300/900, 1,579 printing
   300/600. The 900/500/600 zones are divided by the DASHED lines. Joining dashes is the core of this step, not a second part.
2. **Heavy + joined dashes do zone it.** Prototype on the page itself (scratchpad l1n/zones.py: the 0.96 pen's strokes and
   the 0.6 pen's dashes joined by the footing reader's rule - collinear, gap <= 10 pt, >= 3 pieces - polygonized, each face
   filled by the call-out in it). LOOKED: 900 L-band and slanted band, 900 field, 300 edge strips, 500 north-centre band,
   600 east bays - the drawing's zoning and the engineer's. 164 faces: 18 with one call-out, 1 with several (the field,
   which also holds the stair core's 200), 145 with none (boxes, slivers - they join a neighbour).
   On 31138 L1 (p22, pens 1.08 / 0.6, inch call-outs) the same prototype zones the 42" mat, 14", 12" and 10" areas; its gaps
   are curved edges, which the prototype does not read and the reader does.
3. **The overlay is not the build's slab pass** - known and recorded in SlabPassTraceProbe's header; PdfPlanReader.Read
   fills no PageWords. Use the probe, not `pdf-overlay --walls`, to trace this step.

4. **The classifier merges adjacent zones handed over on the slab layer.** Two closed rectangles sharing an edge, each with
   its own call-out, on KOR_C_SLABEDG: read as ONE 50,000 sq ft plate (and 36" by step 182). So zones do not go on the slab
   layer: they go on their own layer, KOR_C_SLABZONE, the floor stays on KOR_C_SLABEDG exactly as today, and one last
   classifier pass replaces a priced floor by the zones that tile it. No zone layer - the Revit route, Halifax, every floor
   with one call-out - and nothing changes.

## Measured as built, 2026-10-26 (frozen build cli-c183i; six sets on and off; KOR_STEP183_OFF=1 for off)

31170-arch identical. 31065, 31130, 31138, 31168, 31202 differ; 39 floors zoned, 1 set of zones refused (did not tile).
Judged by concrete VOLUME against the engineer's model (engineers overlay thicker plates on a field plate, so area by
thickness does not compare) and by the drawing where the yardstick cannot judge:

| Storey | Off | On | Engineer / drawing | Verdict |
|---|---|---|---|---|
| 31065 L1 (witness) | 305 mm x 20,259 sq ft + 875 | 900 13,276 / 875 7,755 / 600 4,482 / 500 2,433 / 300 3,787 | 900 field, 500, 300, transfer bands | better |
| 31065 L5 | 300 x 8,820 | 200 15,210 / 300 2,148 (342 m3) | 200 8,944 / 300 1,678 (258 m3) | better |
| 31130 L14 | 9" whole (446 m3) | 9" 17,976 / 16" 3,000 | 16" 3,452 sq ft | better |
| 31130 L20 | 10" whole | 18" 6,851 / 10" 711 / 9" 78 | drawing: 18" field, 10" wings (looked) | better |
| 31130 L0/P1, L2 | | | volume toward the engineer's | better |
| 31138 L2 | 24" whole (697 m3) | 24/46/52" (1,094 m3) | 1,631 m3 | better |
| 31138 L21 | 8" whole (179 m3) | 8/24" (453 m3) | 437 m3 | better |
| 31065 L2 | 200 whole | + 900 3,097 sq ft | drawing: three dashed «900 SLAB» areas (looked); engineer's L2 lacks the south tower | follows the drawing |
| 31202 L13 | 8" whole | 8" field + three 12" areas, each outlined and printed (looked) | engineer: 10" throughout | follows the drawing; drawing and engineer differ |
| 31065 P1, 31130 P2/L1/L19, 31138 L1/L5/P1/P3, 31202 L5 | | | within +-3% volume | marginal |
| 31130 L16, L17 | 12"/9" | 18" 7,802 / 18" 2,783 | engineer 18" 4,330 / 164 | **WORSE - cause found, not fixed** (below) |
| 31168 L1, L2 | | zoned | yardstick is this tool's own output (0db1608d) | not judged |

Halifax (Revit route): byte-identical. Core fast 1,675 of 1,675; AFloorTheDrawingZonesIsModelledAsItsZonesTests 14 of 14.

**31130 L16 FIXED, 2026-10-26 (step 183 continued, commit after 9327444e):** the cause was NOT a third pen. The 18" centre
is bounded by the tower-above outline in the outline's OWN pen; it steps down 0.9 m twice, each step one short stroke, and
short strokes were taken only as dashes - no run to join - and dropped, so the outline stood open. A short stroke of the
outline's pen is now a boundary piece too (counted per floor, "(N short)", in the trace - a dash pattern drawn in the
outline pen is the same-class fault it could bring in). L16 east: 10" 3,643 / 18" 4,189 / 12" 1,039 sq ft; the engineer's
3,561 / 4,330 / 1,147. Volume 576 -> 508 m3. Rejected the same night: the slab pass's inner rings as boundaries (L1N's
900 field went to 300). The six-set run of it found a regression - 31168 L1 (building C) stood down whole, "a zone wraps
another in a single cell" - so a zone that cannot be cut round what it wraps now takes it in and the union is made again
(its 21 zones back). Six sets vs 21fb2b10: 31170-arch identical; L16 as above; 31138 L5 12" 33 -> 2,461 (the engineer's
4,313); 31168 A-L1 a 12" zone from its two printed «12"»; the rest within a few hundred sq ft. Halifax byte-identical
(1898f6b4...). Core fast 1,677 of 1,677. The wrap-retry has no unit test of its own - 31168 L1 C on the six sets is its
witness.

**31130 L16 / L17, looked at after the commit (S2.19.1, p43) - the first reading, since corrected above:** the drawing zones L16 east as 10" west, 18" centre (two
«18" SLAB»), 12" east, 10" south strip - the engineer's 10" 3,561 / 12" 1,147 / 18" 4,330 sq ft agrees with it. The 18"
centre is bounded by THIN SOLID lines, a third pen: neither the outline's pen nor dashed, so the cut does not read them,
the west 10", the 18" centre and the south 10" stay one zone, and 2 «18"» against 2 «10"» goes to 18" by ground share
(volume 467 -> 576 m3, the engineer's 317). The next rule, NOT yet written because hatching and dimensions are thin solid
lines too: a solid line of any pen that separates two different printed thicknesses inside the floor is a zone
boundary - measured on the six sets before it lands.

Faults found by this measurement and fixed before commit (each now a test): zones replacing the floor broke every
"plate inside a floor" reader (31138 L1 doubled); PlanGeometryTransform.Move dropped Zones (e2k unchanged though the
report said zoned); longest-edge fill gave 31130 P2's field 12"; an island in one cell stood a whole floor down
(31065 L1 north); zones priced without the sheet's other plates and openings; only horizontal/vertical dashes joined
and grid-axis strokes left out (31130 L14's 16" areas open). Rejected: "a mixed zone takes the floor's answer" -
fixed L14 and undid five true zones.

## A nest is not an island (2026-10-26 evening; found by the six-set drift record, six-set-drift-2026-10-26.md)

**The witness.** 31138 L21 (S2.36, p44, looked at) nests an 8" edge ring, a 12" band, a 24" box and an 8" core, and the
engineer's model nests the same. We wrote 24" over 7,215 sq ft. The trace (SlabPassTraceProbe, KOR_SLAB_TRACE_PAGES=44) read
every boundary, then: "7184 sq ft printing 12 ... taken into the zone round them".

**The class, in one sentence:** a face was located by its outer ring alone, so a ring-shaped face (one with a hole) was
located in its hole, and every test asking "where is this face" (in the floor? an island? which side of the cut? inside
the wrapped hole?) answered for whatever stood in the hole. Seven sites in SlabZones.Cut asked it. Behind that, a zone that
is a single ring-shaped cell has no cell above it and none below, so the wrap-cut could not part it, and it was taken in.

**The check:** `AZoneRoundAnotherIsWhereItsOwnCellsAreNotWhereItsHoleIs` builds 31138 L21's shape synthetically (ring /
band / box / core, every ring's centroid in the core). It was RED before the change: "its zone lines divide nothing ...
4 faces, 4 labelled" (all one thickness). It is GREEN after: 8" 416 / 12" 340 / 24" 144 m², with the core taken in as an
island, as before.

**The change.**
1. A face is located inside its outer ring AND outside its holes (`InteriorPoint(ring, holes)`; `FacePoint(i)` at all seven
   sites).
2. Where a face's hole holds two faces or more (a nest, not one box), two short cut lines run from its outer edge to the
   hole, west and east, at the hole's mid-height. They cross the ring only, so an island inside the nest (the core) is
   still an island. A line straight across split the core and kept it 8".
3. A cell the cut made reads the call-outs of the cell it was cut from, never the nearest call-out (beside a core, that
   is the core's).

A hole holding one face is an island exactly as before (31065 L1N's box).

**Measured, 2026-10-26 ~20:20 PDT.** Halifax raw e2k 1898f6b4c5a8ad26: identical. Core fast 1,687 of 1,687. The six sets
against 86a2e160 (attribute_baseline_drift.py): 31170-arch byte-identical. 18 storeys moved, every move a re-split of
thickness; no plate area and no member moved. Each was checked against what its sheet prints:

| Storey | Before → after | The drawing / the engineer |
|---|---|---|
| 31138 L21 | 24" 7,215 / 8" 2,188 → 12" 3,554 / 8" 3,006 / 24" 2,947 | S2.36 nests 8/12/24/8 (looked at); hers 12" 3,614, 24" 2,613 |
| 31138 L2 | 46" 4,730, 52" 978 → 46" 3,772, 52" 1,935 | hers 46" 4,491, 52" 1,855 |
| 31168 L3 | 10" whole → 10" 12,883 / 66" 3,432 / 60" 3,423 | S2.20.1 prints `60" SLAB` ×4 (bldg A); S2.30.1 prints `66" SLAB` ×8 (bldg B): transfer slabs the take-in had erased |
| 31138 P1 | 12" 176 → 3,519 | S2.10 prints `12" SLAB` ×7 beside `10"` ×17; the engineer models 10" throughout |
| 31065 L5 | 300 2,148 → 6,082 | S2.16.1 (looked at): 300 north and an east strip, 200 elsewhere; ours the same three parts. Hers covers the north tower only |
| 31065 L4 | 200 whole → + 300 1,298 | S2.08.1 prints 300 once |
| 31130 L19 | 14" 220 → 4,444 | S2.09.1 prints `14" SLAB` ×3 beside `9"` ×4 |
| 31168 B-L39 | 12" whole → 12" / 8" halves | S2.34.1 prints `8" SLAB` ×2 beside `12"` ×3 |
| 31202 L5 | + 10½" 344 | S2.06.1 prints `10 1/2" SLAB` ×2 |
| 31065 L1, 31130 L1/L1M/L0-P1, 31138 L5, 31168 L2/P1/A-L1, 31202 L13 | a few hundred sq ft re-split | not looked at one by one |

**What it does not do:**
- A call-out whose own outline is not read still labels the face round it. 31065 P1: a `150 SLAB` box of about 6 × 2 m
  labels 1,726 sq ft. That is open, a separate fault.
- A nest whose rings are not convex enough for a horizontal line at the hole's mid-height to reach the hole from each
  side is not cut; it stays taken in, as before.

## The change (as built - supersedes the cell-union design below it)

The floor is NOT re-derived. The slab pass recovers it exactly as today; then a separate pass cuts each finished floor into
zones: the floor's own ring plus the zone lines inside it (the heavy pen's strokes, and dashed runs joined into lines) are
arranged into faces; a face takes the call-out inside it; a face with none joins the neighbour it shares most edge with;
like faces are united. A zone that wraps another is cut into two pieces through the inner one, so no plate overlaps
another. One call-out, or none, in the whole floor: one zone - the floor as today, byte-identical.

## The first design (cell union inside the slab pass) - not built, kept for the record

In the slab pass, unite the floor's cells **by zone, not all at once**: a cell takes the thickness of the call-out standing
in it; a cell with none takes its neighbour's across the edge they share longest (the field fills the gaps); adjacent cells
of one thickness are united; each union is its own plate, sharing its edges with the next. The floor is still the union of
all of them - same outline, same openings. Then:

1. **Export:** each zone as its own closed polyline on the slab layer (step 175 keeps a drawn closed outline as drawn), so a
   shared edge is not "drawn twice, read once".
2. **Classifier:** each zone has one thickness inside it and is priced by step 118 as any plate is. Nothing new.
3. **Composer:** adjacent plates already compose (a floor drawn on two sheets meets at an edge).

Dashed outlines (the east 500/600 zones) are a SECOND part, after the first is measured: join a run of collinear dash
pieces with regular gaps into one line before the arrangement. Not in the first cut.

## What must NOT move

- A floor with one call-out, or none: one zone, the same plate as today - byte-identical.
- A floor where the walk closed the outline and the arrangement did not run.
- Halifax (Revit route, no slab pass): byte-identical.
- The floor's outline and openings, on every sheet: the union of the zones must equal today's plate (checked per plate).

## The harness that decides it (exists before the first code change)

1. Six banked sets on and off: identical except the zoned plates; per zoned plate, union-of-zones area = today's area.
2. 31065 L1 against the engineer's L1: area by thickness, and volume (today 12" everywhere; the engineer's 4,655 m3).
3. Render L1 by zone (plates/platemap.py) beside the engineer's, and LOOK.
4. `takeoff corpus-gate --bisect KOR_STEP183_OFF` on the 57 projects.

⛔ Run where the drawings are local. Never over the VPN.
