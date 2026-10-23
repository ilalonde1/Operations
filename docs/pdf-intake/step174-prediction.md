# Step 174 — a wall Revit's own PDF export split is still a wall

## Written 2026-10-23, with Ian's go-ahead ("do the reader fix"), before the gate below has judged it.

**Knob:** `KOR_STEP174_OFF=1` judges a wall on its raw points again (`GeometryFilterService`, the wall call site).

**Target set:** 1 witness set, 31170-01's two drafted sheets (S2.01.4, S2.02.1) exported by Revit 2024's own PDF
export from the Virtual Drafter's sandbox. Reach on the corpus: expected NONE — the stick-set plots in the corpus come
through a print driver that writes each wall as one `re` rectangle (0 of 321 fills on 31170-01's own S2.02.1 plot end
on their start without a close flag).

**Measured 2026-10-23 04:25 — the reach is ONE set of the 52 judged, and the prediction was wrong about it.** 30933-01
(River District Parcel 11, a 2024-07-18 stick file) is drawn the way Revit's export draws: the bank read it at **5
walls**, and so does the tree with the knob on; with step 174 it reads **651 walls, 1,243 columns and 37 floors**. Both
renders were looked at (`takeoff model-render`; the Virtual Drafter's `etabs-diff/render-30933/`): with 174 the tower
cores stand at every level and the parkade shear walls span the plates; without it the set has no walls at all. The
gate still calls the set a LOSS — "her openings we have fell 24 -> 22" — and the number is true but it is not a
regression: without 174 the same tree matches 28 of her 46 openings, 6 of them her 0.5 x 1 m shaft openings matched
within 1.5 m by our 0.5 x 2 m "openings", which are filled slots — the core stub walls 174 now reads as walls. Her shaft
openings were never read; they were matched by a mis-read. Plate 216,034 -> 214,941 sq ft (the wall-taken part, within
tolerance). 30933-01 is to be re-banked at these counts by whoever next banks the corpus, and the openings reader's
weakness (a filled 0.5 x 2 m slot taken for an opening) is a step of its own.

Everything else held: of the 52 judged sets, no other set moves with the knob — the four whose walls rose most against
the bank since step 140 (30989-01 278 -> 506, 31005-01 204 -> 414, 31087-01 955 -> 1,195, 31183-01 285 -> 379) read the
same walls, columns and floors with 174 on and off, so those rises belong to the committed steps 141-172; and the eight
other losses the gate names against the 09-24 bank (31183-01, 30972-01, 31032-01, 31101-01, 31117-01, 60061-03,
30838-01, 31009-01) are identical with 174 off, with the tree's other uncommitted step (173) off, and with both off,
so they predate both and are owed to the bank's owner, not to this step.

## The witness

The Virtual Drafter drafted 31170-01's P1 & L1 wall updates (2026-10-22) and the drafted sheets were plotted with
`exportsheets` (Revit's native PDF export). Run through `takeoff stickfile`, the plot of the SAME sheets before the
drafting read 42 walls; the export read 0, while 75 of 76 columns matched within 3 mm. The grey fills were there (78
on P1 against 76). Looked at vector by vector: the print driver draws SWA as one rectangle item; Revit's export draws
it as five line segments ending on the start point with no close command, and one of the vertices sits on the west
face, collinear — the exporter breaks a face wherever something touches it. The page walk marks the path closed (ends
within half a point) and keeps the returning point by contract (`APageIsWalkedOnceTests`); the wall rule then saw six
points and asked for four. 251 of 296 fills on that page end on their start without a flag.

## What the change is

- `GeometryFilterService.WallRing`: the corners of a closed outline — consecutive repeats and the repeated closing
  point dropped, then every vertex whose two edges run straight on (a turn under one degree). A mitre (step 38's
  31170 P1 perimeter) turns tens of degrees and stays; an L-shaped ribbon keeps its six corners and is still not a wall.
- The wall call site judges `WallRing(pts)` with the existing `IsWallShape`; the outline it keeps is the one drawn.
- The page walk is unchanged (its returning point is a tested contract and the page cache replays it).
- Two tolerances, triaged: `WallRingSamePointMm` (0.01 mm), `WallRingStraightRad` (one degree).

## What must NOT move

- Any wall read before: a four-point rectangle's ring is itself.
- Tapers (audit F1), ribbons, notched outlines: a real corner turns and stays, so their counts do not change.
- Plates: untouched by this step. The same export leaves slab edges unclosed ("12 outlines would not close" on P1,
  the L1 plate closed by joining its own loose ends) — a second, separate fault, recorded in the Virtual Drafter's
  `etabs-diff/README.md` and not fixed here.

## The measurement that decides it — and what it said (2026-10-23)

1. `takeoff stickfile` on the drafted sheets' export: walls from 0 to about 42; `model-diff` plot-before against
   export-after should name the drafted changes and nothing else. **Measured: 48 walls, 75 columns, 3 floors** (the
   extra walls are the drafted ones); the plot-before/export-after diff is noisy at 3 mm of frame residual and a
   tolerance for `model-diff` is owed separately (the Virtual Drafter's `etabs-diff/README.md`).
2. The six-set bank, run alone with the knob on and off (`SixSetsBuildAsBankedTests`; its read cache keys on the
   knob): **the six models are byte-identical with 174 on and off, 6 of 6** (`takeoff model-diff` per set). The suite's
   six-set and shifted-page failures tonight are on the tree without 174 — the uncommitted step-173 hunks, whose
   session is no longer running — and are not this step's.
3. The corpus gate against the local mirror, as four full arms over the lost sets rather than `--bisect` (whose
   "restored" arrow is plate area only and would mark every knob for an openings loss): control, 174 off, 173 off, both
   off. **One set moves with 174 — 30933-01, as described above; eight losses predate both steps; the four largest wall
   risers since the bank do not move.** On the way the gate's stick-file identity was found to be the full PDF path,
   which classed all 52 judged sets RE-ISSUED from a mirror; it is now the file name and byte count
   (`CorpusDiff.SameStickFile`), and the existing re-issue test caught the first draft of that (a two-segment path is
   a UNC root to `Path.GetFileName`), so the name is the literal last segment.
