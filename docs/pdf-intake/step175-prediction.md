# Step 175 — a slab edge the drawing closed is closed

## Written 2026-10-24, with Ian's go-ahead ("Go on you go's as long as they don't interfere with the virtual drafter session"), before the gate below has judged it.

**Knob:** `KOR_STEP175_OFF=1` reads slab edges only as the walk re-traces them, as before (`StructuralPlanClassifier`, slab role).

**Target set:** 1 witness set, 31032-01 (L2, page 24). Reach on the corpus NOT yet known; any set whose DXF holds a closed
slab-edge polyline the walk produces nothing for moves. The gate below measures it and this line is corrected with the figure.

## The witness

31032-01 L2 lost 3,076 sq ft in the corpus gate (runs e7605731, a6228331). The PDF side's outline is right: one closed,
valid polyline of 5,735 sq ft on the slab-edge layer, no edge crossing another (scratchpad valid.py, 2026-10-23). The DXF
side never saw it as one: `StructuralPlanClassifier` re-walks every slab-edge segment among the sheet's other linework and
ignores the closed flag; the walk reported the outline among those that "would not close", and the chain closure then
refused it "with no slab thickness printed inside it".

## What the change is

- Every closed polyline gets its own number as it is read (`DxfSegment.ClosedOutlineId`; each block insert its own).
- After the walk, the chain closures and the rescue, a closed slab-edge polyline the walk did not find - no ring of its
  area (within 2%) standing where it stands - is taken as drawn, if it does not cross itself and is a plate's size. Rings
  inside it are then judged as openings, as everywhere else. (Two cuts that failed, traced: standing down for any ring
  inside the outline, then for any plate-sized one - 31032's S2.06 walk closes two 4-corner rings of 67 and 77 sq ft
  inside the 5,735 sq ft outline, over the 50 sq ft plate minimum, and neither cut ever fired on it.) Open chains lying wholly on it are no longer reported as not closing. A flag names it.

## What must NOT move

- Any outline the walk already found, or found anything over: the step only fills what the walk lost.
- Halifax (31180-01, the Virtual Drafter's revit-to-dxf route, 650 of 650 polylines closed): the e2k is byte-identical,
  checked on its own command from a local copy before this is committed. If it moves, this is not committed until the
  Virtual Drafter's gates pass on the candidate.
- The six banked sets: byte-identical with the step on and off.

## The measurement that decides it

1. 31032-01 L2 plate, on and off.
2. Halifax e2k and the six banked sets, on and off: identical.
3. `takeoff corpus-gate --bisect KOR_STEP175_OFF` on the corpus, judged on her plate area, thickness and openings.

⛔ Run where the drawings are local. Never over the VPN.

## Judged on its witness, 2026-10-24 (with step 176)

The third cut alone took 31032 from 27,830 to 21,213 sq ft: L1 lost its floor. Traced to two rules that disagreed on
which of two readings of a storey is the floor (step 176, its own note). With 175 and 176 together, 31032-01: 30,680 sq ft
of her 37,333 (82%, from 75%), no storey under half of hers, L1's columns back to a 50 mm median against hers. Halifax
byte-identical, e2k and report.

LOOKED AT, every storey rendered off, on and hers (`takeoff model-render`): L1 one floor where it was two stacked (the
partial plan's plate in the overall floor's corner); L2 a full plate where it was a comb-shaped strip and a second piece;
L3 the drawn outline - the same shape as L4 above it - where it was a flood-fill blob running past the building.

On L3 a 241 sq ft opening (KO25) hangs past the plate's edge, four of six corners off it. A "step 177" refusing any ring
with under nine tenths of its ground over the floor was written, measured and REMOVED: it also took L1's KO7 (30 sq ft,
76% over the floor), which is one of her openings - her own L1 cuts its small shafts on the slab edge. An opening at the
edge is how she models one; it stays.
