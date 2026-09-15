# PdfIntake — sections §61–§85

Part of `docs/PdfIntake.md` (start there: §0 is the state; this file is the record for these steps).

## 61. Step 53, 2026-09-12: `pdf-at`; the grid is drawn with one pen; a run may stop just past its anchor — and a class found and NOT shipped: the model depends on where the origin is

**The instrument first.** 11 of 55 anchor blocks on 31202's L7–12 plan (p32) were still columns
and the tendon census said only "nearest tendon end 1,696 mm" — while `vector-lines` showed the
page draws a 16 m line from that very block. Which fate had the intake given that line? Nothing
answered at a point. `takeoff pdf-at <pdf> <page> <x> <y> --scale N [--radius mm]` now does: every
path and word within a radius of a point — page millimetres, the frame `pdf-overlay --mark` and
`model-to-page` use — each with the fate the intake gave it, its pen, its shape and its distance.
The ledger `pdf-inventory` sums, opened at one spot.

**What it showed.** `path #15488  Read GridAxis  stroked w16pt  box 16041x0 mm`: the tendon along
grid F, 16 pt heavy, had been read as the grid — every stroke lying on a grid axis was the grid,
whatever its pen. On p32, 234 of 1,517 "grid axis" paths were 9, 16, 4 and 5 pt strokes lying
along the 3 pt grid: the tendons on F and J and other linework drawn on the axes. A tendon the
reader never saw reaches no anchor.

**Rule 1 — the grid is drawn with one pen.** The pen is read where each axis enters its bubble
(`GridBubbles.PenAtTheBubbles`: the stroked line on the bubble's rule whose end lies nearest the
bubble's centre — a tendon stops at the slab edge, well short of the bubble; the median over the
bubbles), carried as `SheetFurniture.Set.AxisPenPts`; a stroke on an axis heavier than
`AxisPenHeavierBy` (2) times it is not the grid (`IsGridPen`). **It is kept apart** — fated
`StrokeOnGrid`, held in `ExtractedGeometry.StrokesOnGrid`, read by the tendon reader alone and
by nothing else, not exported. The first cut let those strokes into `Lines`, where the face-line
wall reader took them for the faces of the filled walls beside them, and walls moved on five of
the six sets; the scoped cut changes no wall (31168's p22 read, loop for loop, is byte-identical to
the reading before). Unknown pen: as before. `TheGridIsDrawnWithOnePenTests`.

**Rule 2 — a run may stop just past its anchor.** The next block `pdf-at` opened ("189 Kips"): the
tendon, now a chain from 9,479 to 88,685, starts 632 mm *past* the block — the leader stub to the
label drawn on the tendon's own line — so neither end lay inside it. A block the run passes
through with an end within `EndOvershootMm` (800) of it is its anchor; one the run passes through
and runs on past a bay is not (`TendonAnchors.EndsJustPast`; the case in
`ATendonsAnchorIsNotAColumnTests`). **For fittings only:** the first cut stood down a 36" round
column on 31130's west tower, sixteen storeys of it, because a chain broke at a MID mark a metre
past it (rendered, p22); the clause now applies only to a block smaller than the set's smallest
scheduled column, as the P/T-sheet clause does. The six-set gate caught it: 31130 lost one column
a storey with no wall moved, and the crop said what it was.

**Measured on p32:** anchors stood down **44 → 53 of 55**; tendons 220 → 244; grid axes 23 → 23.
The two that remain are drawn with a sloped piece the axis-aligned chain does not follow — the
stated limit. 31202 yardstick **90% → 93%** (661 of 711 judged inside her footprint), 95% held.
Six-set gate: 31202 alone moved — 89 columns stood down across L7–L13, ROOF and PENTHOUSE, no
wall, no plate; five sets byte-identical. Banked.

**The class found on the way, and why it is NOT in this commit.** Chasing the first cut's wall
changes with the frames registered (`model-yardstick new old`: every set's columns 99–100% within
100 mm of the old bank's), the DXF's origin turned out to be the drawn content's length-weighted
centroid — `Lines` included — so every sheet's frame, and every model's (its reference plan's),
moved whenever the reading changed: 31168 by 723 x 283 mm for no change in any member. Making the
frame the page's (`$INSBASE` = 0) fixed that and exposed the class beneath it: **the composed
walls depend on where the origin is.** The same 36 DXFs of 31168 translated by 5 m x 3 m build a
model whose columns match to 100% and whose walls differ by 25 lost / 11 gained — tower A's stair
core reads as 18 walls in one frame and 8 in the other (A-L35; one sheet alone is stable in both
frames, so the flip is in how two sheets' readings of one core are reconciled — the "earlier sheet
drew more than half" rule on inch-snapped coordinates is the suspect, not yet proven). The old
bank was one draw of those dice; the page frame is another and lost that core's lower walls.
Shipping that blind is exactly what the gate is for. The page-frame change is stashed
(`stash@{0}`), and the next step is rule 11's: a differential — *the same drawings shifted on the
page build the same structure* — then the fix, then the page frame, then the six re-banked once
with the frames stable.

WHAT THIS DOES NOT: a tendon drawn with the grid's own pen along the grid (still the grid); a page
whose grid is drawn with two pens (the median takes the commoner); the two sloped tendons on p32;
the frame class (next); `ModelDiff`'s registration, which the six-set gate reports through and
which called 177 columns lost and gained on 31065 under a pure translation the yardstick's
registration matched to 100% — the gate's diff should register the way the yardstick does (next,
with the frame).

## 62. Step 55, 2026-09-12, late: a sheet that names no axis stands where its members stand

**The class, from §61.** The composed model depended on where the origin was because a sheet the
names could not place — 31168's LEVEL 35 PLAN - BLDG A names axes 4 and 5 and nothing across, its
LEVEL 36 names nothing — stayed "in its own frame", which is wherever the page put it: near the
tower in one frame and fifty metres off in the other. Its members are the tower's; the model
should not care where the page drew them.

**The rule.** `GridAlignment.SolveByColumns`: the displacement most of a sheet's members share
with the members already placed is its frame, at 0 degrees — votes in `ColumnRegistrationMm`
(100 mm) bins, the fullest bin refined to the median of its pairs, and a fit only when at least
`LeastConvincingByColumns` (4) of the sheet's members land within a bin of a placed one *and* at
least half of them do. Runs after the by-name placement and the carriers, for sheets still
unplaced, and a sheet placed this way lends its members to the next (`DxfToEtabsService`, the
loop after the carriers). `ASheetThatNamesNoAxisStandsWhereItsColumnsStandTests`.

**Two faults in the first cut, both found by the instrument, not the gate.** `grid-names` now
prints, per sheet, its members against the model's ("members: 44 …; the model has 2425: …"), and
on the L4-L14 plan — placed by name, twenty-four of whose columns provably stand on model
columns (`sheet_vs_model_columns.py`: 24 of 24 within 100 mm) — it said "the displacement most
share has **2 of 44**". Reproducing the vote in python on the same inputs gave the same answer,
so the inputs were right and the vote was wrong:

1. **A placed member is a place, however many storeys stand one there.** The model held 2,056
   panel corners for some seventy walls — one panel per storey — and a vote per *pair* let two
   of the sheet's corners over a thirty-storey core cast 114 votes against the twenty-four
   columns' 24. The placed set is now distinct to a tenth of a bin and each sheet member votes
   once per bin. `APlacedMemberOnThirtyStoreysIsOnePlace`.
2. **Register on the joints the composer writes, not an outline's corners.** With the vote fixed,
   L35 registered on tower B by 4 of 10 — refused by the half rule, one short of a wrong fit —
   and on tower A by nothing, because a wall outline's corner sits half a thickness from the
   panel's end (191 mm for the 382 mm core walls; outside the bin). Members are now
   `StructuralPlanClassifier.MemberPoints`: each column's centre and each wall's axis ends, the
   points the model is made from, classified in the drawing's own unit (`requested.InUnitOf(
   drawingUnit)` — the raw segments, not the model-unit options the full classification uses)
   and read back from the model the same way by `grid-names` (column joints, a panel's two plan
   ends). L4-L14: 60 of 60 at (-24,974, 8,273); L35: **8 of 12 at (-25,131, 10,136)** — the X is
   what its two named axes give to the millimetre (-25,131.5), the Y is where its core walls'
   ends meet the L34 plan's. Its two 24x12 columns land 375 mm off the centres of the 30x41 columns
   below them, flush at one corner (faces within 10 mm) — a non-concentric stack, and the walls
   outvote them. `grid-names`, reading
   a model that already holds an unplaced sheet where the composer left it, registers that sheet
   on itself at (0, 0); it says so and prints the fit against the other members as well.

**Measured.** Six-set gate: 31168 alone moved — A-L36's two columns and five walls, from
(-4,256, -4,684) to the tower: walls at (-29,301, 12,641) 1,742 long, (-24,790, 11,936),
(-24,778, 13,512), (-24,778, 10,565), (-20,255, 12,641), each within 3 mm of the wall below it
(KW637, KW632, KW636, KW244, KW635). Five sets byte-identical. Banked. 31168's yardstick is
unchanged (her model is the parkade and L1–L2; 95% / 83%). A stale `<job>-diff.txt` from an
earlier run read as three regressions for a minute; the gate now deletes a set's diff when the
set builds identical. 31202's two unplaced sheets are S7 SEISMIC INSTRUMENTATION plans with no
level number, and place nothing.

WHAT THIS DOES NOT: a sheet drawn a quarter turn from the model (0 degrees only); a sheet that
names axes in ONE direction, which could be fixed in that direction by name and voted in the
other (L35's by-name X and its by-members X agree to 0.5 mm, so it was not needed); the frame
class itself — the differential *the same drawings shifted on the page build the same structure*
is still owed, then the page frame (`stash@{0}`), then `ModelDiff`'s registration.

## 63. Step 56, 2026-09-13: the same drawings shifted on the page build the same structure — a place is decided by distance, never by a cell

**The differential first (rule 11).** `TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests`:
each of the six banked sets read once (`PdfOnlyBuild.WriteSheets`, no files) and composed twice —
as it is, and with every view moved 5,000 x 3,000 mm on the page (`DxfSheet.Shifted`, a public
`PdfOnlyBuild.Compose` over views) — and the two models compared registered (`ModelDiff`: by the
grid labels both carry; the registration itself asserted to be the shift). Both readings and both
models are left in `TestResults/shifted/<job>/{as-is,shifted}/`. First run: **five of six sets
built a different structure** — 31168 walls 308 lost / 165 gained and 22 columns gained, 31138
51 / 33 with three plates moved and a column lost, 31170 13 / 23, 31202 12 / 12, 31130 7 / 5;
31065 alone identical. The registration was (5,000, 3,000) to the unit on all six: `ModelDiff` is
not the frame problem §61 suspected.

**The class, characterised once (rule 10).** A model is made of members from many sheets, and at
five places the route decides that two things are *the same place*: two sheets' readings of one
wall or column (one member per place per storey), two ends of linework that should meet (a loop's
node), two dashes on one line, a point and the joint already written for it, a wall on this storey
and the pier label it shares with the storey below. Every one of those decisions was made by
**which cell of a grid anchored at the origin a point fell in** — an inch grid for members, a foot
for plates, the join tolerance for nodes and joints, the offset tolerance for dashes, six inches for
labels — and whether two points a millimetre apart share a cell depends on where the cell edges
fall between them, which is to say on where the origin is. The rule that has to hold is one
sentence: *a place is decided by distance, never by a cell; a cell is only an index for the search.*
The contradictions, each fixed the same way (the nearest thing within the tolerance, searched over
the cell and its neighbours; a cell holds a list, so a second occupant does not evict the first):

1. `E2kGeometryComposer` — `PlacedMembers` for walls, columns, spandrels (an inch), plates (a foot)
   and openings (0.01 unit), replacing five `HashSet<(long, long, …)>` keys; `LabelledPlaces` for
   pier and spandrel labels (6 in). This alone took 31168 from 308 / 165 + 22 to 0 / 1.
2. `PointAt` — returned the joint in the point's own cell before looking at the neighbours; a
   point 0.5 mm from a joint across the edge took the one 1 mm away in its cell. 31168's LEVEL 3
   wall along y = 6,931 joined the stack at 6,930 in one frame and at 6,932 in the other, and the
   gap fill then gave LEVEL 2 a wall. The last 0 / 1.
3. `PlanLoopBuilder.NodeOf` — a node was the cell; two ends a tenth of a millimetre apart either
   side of an edge were two nodes. Found by the new `dxf-inspect --members` (every wall and column
   the reader hands the composer, one per line, sorted) diffed sheet by sheet between the frames:
   **9 of 31202's 44 sheets were read differently** — on LEVEL 4 a 6" wall along the tops of two
   48" piers came out left of the first pier in one frame and right of the second in the other.
4. `DashedLineJoiner` — dashes grouped by (angle cell, offset-from-origin cell); a run of dashes
   either side of an offset cell edge was two lines. Grouped by distance in sorted order now.

With those four in, 31168 and 31065 built the same structure shifted and four sets still did not
(31130 20 / 13, 31138 37 / 31, 31170 20 / 28, 31202 15 / 12), and the same 8 or 9 sheets of 31202
read differently however the composer was fixed — so the second half of the class is in the
READER, and it is not cells. Two more shapes of the one fault, found by probing the sheets that
differed (`dxf-inspect --members` on the as-is and shifted readings the differential now leaves on
disk, then a probe inside the stage that differed):

5. **A tie decided by rounding noise.** `WallOutlineDecomposer` offered 31202's LEVEL 4 bottom
   edge three partners at separation 154.432 mm and overlap 5,539.232 mm — three equal 6" loops
   along the tops of two 48" piers, their bottom edges run into one line by the dash joiner —
   and the shifted frame computed one overlap as 5,539.232000000002: two trillionths, which
   `overlap > bestOverlap` took for the longer face. Everywhere the reader picks *the best* of
   candidates that can be equal by construction, a tie is now a tie (1e-6) and is broken by the
   geometry or by the order the candidates came in, never by the arithmetic: the decomposer's
   partner (then first along the face), `PairOpenFaces` (the same), `WallNetwork`'s corner moves
   (distance to the micron, then axis and end), its openings, `PlanLoopBuilder`'s continuation,
   its nearest node and the composer's nearest joint (the earlier one on a tie — the cells were
   searched in the frame's order), `LoopGeometry`'s least-area box (a rectangle's two orientations
   tie exactly), the keyhole vertex, and every ordering by area (rounded to 1e-3).
6. **A threshold equal to a drafted dimension.** 31202's LEVEL 1 outline has a 12" gap where a
   pier's top meets the run of a wall face, and `WallBridgeTolerance` is 12" — the same
   304.8 mm; the measured gap came out 304.79999999 in one frame and 304.80000001 in the other,
   bridged in one and not the other, and a 610 mm wall read as 1,069 mm for it. Every distance or
   size the reader compares with a tolerance a drafter could draw — the builder's join, bridge and
   extension, the decomposer's and the open-face pairing's thickness and overlap floors, the
   network's snap and reach, the wall-versus-column box sizes (a 48" loop *is* 48" in every frame)
   — is compared to the micron (`LoopGeometry.Within` / `Beyond`: both sides rounded to 1e-6
   before `<=`). Nothing a micron or more from a threshold changes.

After 5 and 6, every sheet of 31202, 31130, 31138 and 31168 read the same in both frames and the
models of 31065, 31130, 31168 and 31202 were identical shifted; 31138 gained one 610 mm panel and
31170 six 1,829 mm panels — spandrels over doorways of exactly 24" and 72", `MinOpeningSpan` and
`MaxOpeningSpan` to the millimetre: shape 6 again, in `WallNetwork.FindOpenings` (`--members` did
not list openings until then, which is why the sheets read "the same"). And the last one:

7. **A probe on a drawn line.** 31170's LEVEL 2 draws a 229 mm wall with both faces and its
   centreline; the decomposer's concrete-or-void probe sits between the faces exactly on the
   centreline, and a ray cast at a point on an edge is the frame's coin toss — 114 mm in one
   frame, 229 in the other. The probe now asks a hair to each side of the midline (1% of the
   separation) and either side inside is concrete; `MaterialRun`'s probe the same. And a member
   whose midpoint lies on its plate's edge (a perimeter wall) is on the plate: `E2kDocument`'s
   support test takes a point within an inch of an edge as inside.

What is left at the sheet level is one wall on one sheet of 31170 whose thickness is 317.5 mm and
prints as 318 or 317 — the same wall.

**And the six-set gate said which draw the old dice had made.** With the differential green the
gate moved on all six, and 31168's tower A core read as 8 walls on L5–L15 where the bank had 18:
the south wall (28", 9 m) gone, its two 30x41 returns read as columns. §61's "18 in one frame and
8 in the other" — and the deterministic reading had landed on the 8. The cause was the eighth
shape, in the joiner: the returns and the wall band draw their bottom edges on one line, and the
`DashedLineJoiner` merged those three touching, collinear edges into one segment (its cells had
merged them in one frame and not the other), which took the band's outline apart — it no longer
closed, its bottom face was strung between the returns in one open chain, and its top face, 711 mm
away, was beyond the 18" ceiling the open-face pairing keeps for ambiguous pairs. The first cut —
"a dash has a gap", no merging of touching collinear segments at all — gave the L4-L14 plan the
bank's 18 walls in both frames, and the gate then showed what else the merge had been doing: a
face cut at a T is two touching pieces of one loose line, and unmerged those pieces read as stubs
and slivers on every set (31065 L6: 250 and 300 mm walls, two returns read as columns, +1 column a
storey that her model does not have). **An edge of a closed outline is a finished shape's edge,
not a dash** (`DxfSegment.OfClosedOutline`, set by the reader for closed polylines): the joiner
leaves those alone and joins loose lines as it always did. The core then reads as its outlines
say — the 28" south wall between the returns' inner faces, and the two 30x41 returns as columns
(under 48", her rule) — 12 walls and 26 columns, in both frames; a run of one segment is emitted
untouched either way (rebuilding it from a projection put rounding noise on its ends). Also from
this: `WallOutlineDecomposer.Decompose` hands back the edges no panel used, and a chain that was
partly read offers its leftovers to the pooled pass, and `PlanLoopBuilder.PickContinuation` breaks
a tie between two edges leaving one way by the one that closes the outline, then the shorter.

`dxf-inspect` also read every millimetre sheet at inch thresholds until tonight ("-> 0 panel(s)"
on every loop); it reads the sheet in its own unit now, as `grid-names` does, and `--members`
prints what the reader hands the composer.

**Measured.** Ninth run of the differential: **all six sets build the same structure shifted** —
registered at (5,000, 3,000) to the unit, no plate moved, no column or wall lost or gained. Six-set
gate against the step-55 bank, every set moved, and this is what the frame's dice had been
deciding: 31168 walls 607 lost / 821 gained, columns 24 / 87 — tower A's core read two ways on
different storeys (18 walls on L5–L15, 8 on L16 up, the returns as columns on some and stubs on
others) now reads one way on all 32 (the 28" south wall, the returns as 30x41 columns); 31202
walls 81 / 137, 31130 119 / 155, 31138 111 / 135 (columns 16 / 33), 31065 139 / 140 (columns
9 / 38, and L2 now has two columns where it had none), 31170 75 / 90. The yardsticks say what
the columns did: the matched counts are identical on all five sets that have one (31130 457,
31138 415, 31202 661, 31065 236, 31168 534), and ours-judged rose by 2 to 15 a set — returns and
short piers read as columns that she models as walls, the column-versus-pier question already
open for WP6. Rendered (31168, every storey): one core, box and middle wall, on every tower
storey. Banked, all six.

WHAT THIS DOES NOT: a rotation; a shift that is not a whole number of millimetres — the classifier
still keys exact duplicates at 0.1 mm (`seenEdges`) and 1 mm (`SameWall`), and a 0.05 mm shift
would move those (a real duplicate is exact, so nothing measured turns on it); the page frame
itself (`stash@{0}`, next); the PDF reader's own page-frame bins (`GeometryFilterService`,
`SheetFurniture`), which a shift of the DXF cannot reach — the same drawing printed at a different
place on its sheet is the next differential; a dependence that shows only under a different vector;
whether a 30x41 return is a column or a pier (her 31138 has 121 piers; ask at WP6); the coverage
fault the L4 case exposed — one long face facing three loops pairs with one of them and the other
two are lost in either frame (`WallOutlineDecomposer`/`PairOpenFaces` consume a face on its first
pairing) — a reading rule for a later step.

## 64. Step 54, 2026-09-13: a sheet's frame is its page's

**The rule.** The DXF a view is written to — and the in-memory view the composer reads — was
recentred on the drawn content's length-weighted centroid, so every sheet's frame, and every
model's (its reference plan's), moved whenever the reading changed: §61 measured 31168's model
moving 723 x 283 mm for no change in any member when step 53 read the strokes along the grid as
lines. The origin is now the page's lower-left corner (`DxfExporter`: `cx = cy = 0`,
`$INSBASE` = 0), which nothing read can move. Held back from §61 until the differential of §63
could say the structure would not change with it, and it did not: with the page frame every set
builds the same structure shifted, and the six-set gate against the step-56 bank reads four sets
as a pure translation of 40–50 m (registered by their grid labels: no plate moved, no member lost
or gained).

**Two things it found.** 31065 came back 184 columns and 185 walls "lost and gained" — every one
3 mm from its twin. The models' grids had registered exactly; the members had moved 3 mm against
the grids. `GridAlignment.AgreedOffset` broke a tie between two clusters of votes (the same labels,
the same count) by *the smaller move* — the absolute offset of the fit, which is where the sheet
happens to sit against the model's origin and not a property of the fit — and under the page frame
the other cluster was the smaller move. A tie is a tie: the tightest cluster wins, then the first;
the smaller move is kept for one caller only, `SheetDiff` — a reissue, where both issues share one
page frame, one name each way is a tie, and the page is the same page until the names say
otherwise (`AReissueIsWhatMovedTests`). With the spread first and the smaller move still last for
every fit, 31065's L1 plan alone still moved 3 mm — which is how the rule was found to belong to
the reissue and not to the fit. And 31170 lost one column on L2: two readings of one column
25.9 mm apart against the inch of `PlacedMembers` — the last threshold not compared to the micron;
it and `ColumnJointNear` are now (`LoopGeometry.Within`).

`ModelDiff`'s registration, which §61 suspected of calling 177 columns lost under a pure
translation, was right both times: the members had moved against the grids. Nothing to fix there.

**Measured.** With the smaller move out of the fit, the gate against the step-56 bank reads
31130, 31138, 31168 and 31202 as pure translations of 40–50 m; 31065's L1 plan stands 3 mm from
where it stood (8 columns and 30 walls, each a 3 mm pair; the rest of the set a translation) and
on 31170 one column drawn on two sheets 25.9 mm apart has its storeys split between its two stacks
differently (5 lost, 4 gained). Neither is a frame dependence: the differential's vector was made
fractional — (5,000.37, 3,000.61) mm, so that sub-millimetre keys and hair-fine tolerances are
exercised, which a whole-millimetre shift never did — and it is **green on all six** in the page
frame. The two are the new rules against the old bank: the tighter cluster chosen where the
smaller move had been, and 25.9 mm read as more than an inch to the micron. Banked in the page
frame; a second build is byte-identical.

WHAT THIS DOES NOT: `model-to-page` and `pdf-overlay --mark` read `$INSBASE`, now (0, 0), so a
point in a view is a point on the page directly — `TheInstrumentsShareTheReadersFramesTests`
asserts it; a page whose media box does not start at (0, 0) (a cropped PDF) keeps its own offset.
A sheet that stands on no grid stacks by its frame — so what its frame IS decides what stands under
what, and this step changed that for every set with unplaced sheets: the six-set gate could not see
it because all six sets stand on grids (found by run 7 against run 8, §68: 128 sets with the same
storeys and the same placement composed differently).

## 65. Step 57, 2026-09-13: the adversarial audit of steps 44–56, answered

The brief is `docs/codex/CODEX-PDF-INTAKE-STEPS-44-56-ADVERSARIAL-AUDIT.md`, the response beside
it; 25 findings, source-deduced, each checked against the cited lines (20 lines read per finding,
not the file). Seven High, all real, all fixed with a test that encodes the counterexample:

| # | The fault, verified at the source | Fixed in | Test |
|---|---|---|---|
| F1 | The pattern-cell pass removed columns without compacting `columnByShape`; the quadrant pass then read the survivors by stale flags and a declared pair behind three cells went as a target's quadrants | `GeometryFilterService.PatternCellsAreNotColumns` compacts the flags with the other parallel lists | `CellsRemovedAheadOfADeclaredPairDoNotHandThePairTheirFlags` |
| F2 | `SheetViews.Split` copied a view's columns without `ColumnIsTendonAnchor`; the exporter, seeing an empty list, wrote a stood-down anchor as a column again in a split view | the view carries the flag | `AStoodDownAnchorStaysStoodDownInItsView` |
| F3 | Plan-only parkade levels below the first stated elevation walked up from zero and met it at zero (P2 0, P1 3,000, L1 0 — the ladder folded) | `StoreysFromPlans`: storeys before the first stated one step *down* from it by the storey height | `PlansBelowTheFirstStatedLevelStepDownFromIt` |
| F4 | Two towers with one core plan: a top plan named for A fits B just as well, and the fuller bin was whichever came first | a sheet that names a building registers on the members of sheets that name it (or none); `SolveByColumns` refuses two places that fit alike | `RepeatedSheetPointsAreOnePlaceAndTwoPlacesThatFitAlikeAreRefused` |
| F5 | Four wall axes meeting at one junction were four of the quorum; the placed set was made distinct, the sheet's was not | both sets distinct by distance; the minimum asked of places | same test |
| F6 | `placedSlabs.Add` ran before `AnythingStandsUnder`, so a legend ring refused as an orphan held the place of the supported floor drawn at the same centre | the place is claimed after the support test | `AnOrphanRingDoesNotReserveTheSupportedFloorsPlace` |
| F7 | `TendonAnchors.Inside` used the longer half-side on both axes: a 305 x 914 block held an end 400 mm off its short side | the block's own rectangle | `AnEndBesideABlocksShortSideIsNotInItsFootprint` |

With F4 came F9 (Medium): the fullest bin alone was refined, and a displacement straddling a bin
edge could lose to a stray bin — every bin within one vote of the fullest is refined and judged by
its support (`ADisplacementStraddlingABinEdgeStillWins`). And F23 (Medium) explains the vocabulary
flake CLAUDE.md said to look for a static behind: `DxfToEtabsService.Run` *writes*
`PlanSheetNaming.Vocabulary`, and eleven composing test classes sat outside the collection that
serialises its readers; they are in it now, and the gate `EveryReaderOfTheSharedVocabularyIsSerialised`
counts a class that composes as a reader.

**Accepted and queued, with the reason each waits** (all Medium/Low, none moves a member on the six
sets today — the differential and the gate are green after the fixes above):
F8 dash offsets measured against each segment's own normal are not comparable between nearly
parallel lines (measure against the group's first); F10 `PlacedMembers.Near` bounds X and Y
separately, so two readings 28 mm apart diagonally are "within an inch" (make it Euclidean, as
`ColumnJointNear` is); F11 nearest-node registration is order-dependent when three points lie
within one tolerance (known; a differential over entity order is the check); F12 curve points
keyed at 0.01 mm (a cell); F13 the thresholds the sweep of §63 missed (listed in the response);
F14 two `dxf.pdf` slab rows are loaded and never passed to `Classify` (wire them and prove it by
breaking); F15 a grid drawn heavier than its tendons; F16 the yardstick's preferred-export path
skips the self-output guard; F17–F18 `ModelDiff` cannot see a wall turned in place and matches
`Any` rather than one-to-one; F19–F20 yardstick summaries (a complete recall miss suppressed; "on
her wall" by bounding box); F21 `grid-names`' self-standing clause; F22 `corpus-query plan-titles`
splits on `;` where the writer joins with ` | ` (a counting instrument — the run-7 row already says
its count is the verb's); F24–F25 the unit differential and the ledger round-trip test are narrower
than their names. Each is a line of work with its own measurement; none is carried silently.

**Not accepted:** none. Two findings restate limits already named (F11's order dependence, F17's
rotation) but each adds an input the WHAT-IT-DOES-NOT lists did not, so they stand as queued.

## 66. Step 47, 2026-09-13: a storey may be named by a word

**Measured first.** Run 7's ledger: 68 of the 86 sets without a model read no storey ladder — "the
elevations chained none and no plan names one". Their 383 plans: 186 with no title the page reader
found, 64 named by a floor word or an ordinal, 43 foundations, 24 roofs, 22 numbered. And the
named ones were small jobs — "S-7 - MAIN FLOOR PLAN SHOWING 2ND FLOOR FRAMING OVER", "S-8UPPER
FLOOR PLAN SHOWING ROOF FRAMING OVER", "S-6 - FOUNDATION PLAN" — whose sheet number, in the
hyphenated form, the page reader never took for one, so the view was named by the stem and page
and the composer saw no name at all. Even "S-6 - LEVEL 1 PLAN" was lost that way.

**The rule.** A storey may be named by a word. A floor word names its level (`dxf.floor-words`:
MAIN and GROUND are 1, UPPER is 2, WORD=LEVEL); an ordinal names its level (2ND, SECOND, 7TH —
English, compiled); a basement word names the parkade level under the main floor
(`dxf.basement-words`); a loft word names the storey above the highest numbered plan, under the
roof (`dxf.top-floor-words`, ranked by the ladder as that number); a word counts before a floor
noun only (`dxf.floor-nouns`: "MAIN STREET" names nothing); and what a plan is the plan *of* is
said before its framing-over clause (`dxf.framing-over-words`: SHOWING) — the roof, foundation
and mezzanine kinds are read from that part too, so "LOFT PLAN SHOWING ROOF FRAMING OVER" is a
loft, not a roof. Read only where no number names a level. Migration 089, five rows (Ian applied
it 16:50–17:00; the first cut shared one topic across five rows and declared the units `words` —
`names` is the reader's list unit — both corrected in the migration itself).

**And step 46 completed on the way** (`SheetDxfName.For`): a title that begins with a hyphenated
sheet number is that number and the rest; a title with no number at all still names the view,
the stem standing where the number would. `PlanSheetNaming.Parse` reads words past the view
name's `<number>_<n>_` prefix (an underscore is a word character; `\b` never crossed it).
`AStoreyMayBeNamedByAWordTests`.

**Measured.** The 68 sets through the analyzer with the rows: **29 build** (207 → 236 of 293 on
that count; run 8 over the whole corpus, 17:45 → 20:08, says the same: **236 of 293**, 39 with
no storey, 17 with no plan, 1 with no slab edge; §1b of the plan has the row); 39 still read no storey — the class with no
title on the page, which this step never claimed. Six-set gate byte-identical, differential green:
none of the six names a storey by a word. What the 29 contain is the next measurement, not this
one: "3 storeys, 0 walls, 248 columns" on a five-page house is 0 walls rightly (wood frame) and
248 columns wrongly — a small job's filled symbols (posts, hangers, hold-downs) read as columns.
That class now has 29 sets to be measured on.

WHAT THIS DOES NOT: a plan with no title the reader found (39 sets; the page reader's business);
another office's word for a floor (a row); two lofts; a plan named in another language; a word
level beside a numbered level on one title (the number wins, the word is left alone); what the
small jobs' "columns" are.

## 67. Step 58, 2026-09-13: a column is drawn with four corners

**Measured first.** 01389 (a five-page house, one of step 47's 29): "3 storeys, 0 walls, 248
columns". `dxf-render` of its main floor plan: rows of small blue squares every metre along three
bearing walls. `pdf-at` at one of them: an unfilled 849 mm square drawn with a 10 pt pen, and
inside it two filled shapes of **three points** — a bearing-wall symbol, a square with two
triangles — read `BecameColumnByShape`. Over the whole page: 139 columns read, 139 of them three
points. On 31168 p22, 31202 p32 and 31138 p12: 48, 108 and 24 columns read, every one four points.

**The rule.** A column is drawn with four corners, or as a curve. A filled shape of three points
is a symbol's triangle — a bearing-wall symbol, an arrowhead, a hatch — and is fated
`FilledTriangle`, discarded, before the size and aspect tests (`GeometryFilterService`; the fate is
in the fixture and the frozen-classifier differential leaves it out as it does step 49's). After
it 01389's five plans read 0 columns and 200–541 triangles each: a wood-frame house has no concrete
columns, and its posts are not drawn as filled squares either, so the model is honestly empty
rather than wrong.

**Measured.** Five of the six byte-identical; 31170 (the architect's set) **re-banked**: L1 lost
five columns, 58 → 53, and every one was looked at with `pdf-at --points` and `pdf-overlay`. Two
are the corner cell of a diagonal wall hatch — the stripes cut the wall's end into one right
triangle, 13" and 26" on the leg, and the bank had read each as a column standing in the wall.
Three are the arrowhead of a spot-elevation tag (450 × 325 mm) painted over a real column, which
the bank read as a second column at the same place; the real one (six points, 1090 × 1208 mm) is
still read. The yardstick against her model says the same: 331 of ours inside her footprint
before and after, and "beyond it" 11 → 6. With labels normalised the bank moves by 18 lines
removed and none added: five points, five columns, five assignments, and the three sections only
they used. Differential green. Run 8 (the whole corpus on steps 47–57, 17:45 → 20:08, without
this step) says what the small jobs read before it: 113,069 columns from run 7's 104,761 — 7,059
on the 29 sets step 47 brought in (01389 alone 248), and the 207 sets both runs built moved
104,761 → 106,010, 149 of them changed by steps 54–57 (which sheets stand on the grid, so which
columns are in the model: 30924-01 3,195 → 1,947, 30840-01 397 → 1,385). Run 9 on this step says
after; what steps 54–57 did to those 149 is a measurement owed (§1b).

WHAT THIS DOES NOT: a post drawn as a small filled square on a wood-frame plan (four points; the
size floor decides); a column drawn as a triangle (none seen on 293 sets); a four-cornered shape
that is a hatch cell or an arrowhead (the harness holds none, and this rule counts corners only);
what a wood-frame house's model should hold at all — a question for the plan.

**Corrected by run 9 (§70).** The rule as shipped was too wide, and the six harness sets could
not say so: run 9 over the corpus on this step alone took 113,069 columns to **65,106**, moved 144
sets' compositions, and made ten yardsticks worse against one better (31048-01: 74 of 449 within
100 mm → 15 of 48; 31017-01: 161 of 1,237 → 47 of 204). A PDF driver draws a filled rectangle as
TWO triangles on its diagonal, and many sets' columns — and 01389's grey wall pieces, 560 × 152 mm,
which is what its 139 "columns" were: not a bearing-wall symbol, as §67 first said from one look —
arrive that way. Two filled triangles of one colour sharing an edge whose union is a convex
quadrilateral are one shape (`TriangleTwins`, step 61); a lone triangle is a symbol's, as here.

## 68. Step 59, 2026-09-13 night: what moved between two runs is classed by the first thing that changed — and the yardstick's frame is judged by support, never by the order of our columns

**Measured first.** Run 8 against run 7, set by set: 236 of 293 build from 207, and the columns
113,069 from 104,761. The 29 new sets carry 7,059; the 207 sets both runs built moved +1,249 net,
**149 of them changed count** — while the per-sheet column sum over the 8,697 sheets moved by
seven (271,274 → 271,267: the READING did not change; the composition did). That question needed
an instrument, and the python that answered it was thrown away for one: `corpus-query diff
<before-sets.csv> --ledger <after>` (`CorpusDiff`), which puts every set in ONE class by the first
thing that changed — a model gained or lost, its storeys, which sheets stand on the grid, or with
both the same the composition — with the columns, walls and plates each class moved and the
yardstick's verdict where a set has one. Run 7 → run 8 reads: NewModel 29; Storeys 34 (step 47's
words on sets that already built: 30840-01 2 → 6 storeys); Placement 11; **Composition 128**
(−1,592 columns, +1,808 walls, +40 plates; yardsticks 14 better / 8 worse / 16 same, 5,678 of
12,068 → 5,865 of 12,197 within 100 mm); Unchanged 91.

**The class the six-set gate could not see.** The 128 are the page frame (§64): a sheet that
stands on no grid stacks by its frame, and the frame moved from the content centroid to the page.
All six harness sets stand on grids, so the gate read the page frame as a pure translation and it
was — for placed sheets. §64 now says so under WHAT THIS DOES NOT. On the yardsticks the page
frame is the better stacking (13 better, 7 worse among the 41 Composition sets with one; plates
556 → 586 on them), and the 8 worse are named in the table for the next reading step.

**And the yardstick's own ruler had moved.** Four sets were Unchanged to the member and their
yardstick verdict differed (31174-01: 8 of 64 supported, then 5). `ModelYardstick.Register` took
the fullest bin of PAIR votes with `MaxBy`, whose tie fell to whichever bin our columns voted first
— the order the model lists them, which steps 54–57 changed — and a bin's votes count pairs, so
three readings of one column out-voted three columns. It is the rule of §62 now: every bin within
one vote of the fullest is refined to its median and judged by its SUPPORT; a tie in support goes
to the tighter cluster, then the smaller move. `TheYardsticksFrameIsJudgedBySupportAndNeverByTheOrderOfOurColumns`
fails on the old code (proved by running it against the stash) and passes on the new. Every corpus
yardstick number before this step was measured with the old ruler; run 9's `--reuse` pass
re-measures.

**F22 on the way.** `corpus-query plan-titles` split a sheet's DXF files on `;` where the writer
joins with ` | `; one splitter now (`CorpusAnalyzer.DxfFilesOf`), the writer's separator declared
beside it, `ASheetsDxfFilesComeBackAsTheWriterJoinedThem`.

WHAT THIS DOES NOT: why any one set moved (the ledger holds counts; `dxf-inspect --members` on
two builds does); a placement that changed to the same COUNT of different sheets (reads as
Composition); the sheet ledgers are not banked beside the set ledgers (the DB has them per run,
`analysis.IntakeSheet`), so the reading line of `diff` prints only against a live corpus folder.

## 69. Step 60, 2026-09-13 night: a stick file that is another job's; a title may run two lines; the set's own order of its floor words

**Measured first.** The 39 sets that read no storey after step 47, one by one through the ledger.
**16 are one file**: `01783-01 2026-02-23 Bean Around The World Lytton Stickfile.pdf`, 878,068
bytes, filed under 00904-01, 00966-06, 01323-03, 01749-01 … 01811-01 and 31237-01 — someone's
copy landed in sixteen other jobs' `05 Stickfile` folders. The census of 2026-09-11 had flagged
exactly this (plan §1a: "one job's stick file copied into 16 others") and the analyzer built all
seventeen anyway, for three days of runs: a flag nobody acts on is a count, not a rule. 31089-01 (Burke Mountain parcel 5) is
eleven townhouse buildings, two sheets each, two plans a sheet, titled "FOUNDATION PLAN" and
"GROUND FLOOR SHOWING" over "MAIN FLOOR FRAMING OVER" — the word PLAN in neither floor title,
each line underlined, the sheet's own title "BUILDING 1 FOUNDATION AND FLOOR PLANS" naming no
storey. 30768-01's 18 plans carry no title at all ("-"); 30888-01's and 30980-01's titles are the
title block's other words ("HILLS ARCHITECTURE DUFFY DRAWING LANDSCAPE PERMIT…", "PLAN SLAB MIXED
USE SEE DEVELOPMENT") — the title reader's, next step.

**Three rules.** (1) *A stick file that is byte-identical to another job's is that job's.* The
analyzer groups by length and name, hashes only a group's members (the mirror's copies), and reads
the file once under the job whose number its name carries; the other rows say
"the stick file of another job: byte-identical to 01783-01's (…)" and build nothing
(`AnotherJobsFile`; `AStickFileThatIsAnotherJobsIsReadOnceTests`). The population is jobs, not
copies: 293 − 16 = **277**. (2) *A title may run two lines, and a floor named by a word with a
framing-over clause names a plan.* `SheetViews.Titles`: an underlined line that names no plan by
itself takes the line directly above it (within two heights, sharing its span) as its first line
when the two together do, and that first line is then no title of its own; a stroke with another
line of text between it and a line underlines that other line. `NamesAPlan` judges the part before
the framing-over clause and accepts a word floor, a basement or a loft word
(`ATitleMayRunTwoLinesAndAWordFloorWithItsFramingOverNamesAPlan`). (3) *The set's own order of its
floor words.* The row says MAIN and GROUND are both 1 — true of a set that uses one of them — and
31089-01 uses GROUND, MAIN and UPPER as three floors whose order its clauses state. Where the
titles chain floor words that way ("GROUND … SHOWING MAIN", "MAIN … SHOWING UPPER") the chain
ranks them from 1 upward, anchored by a number shown over the top word where there is one, and a
word the chain never names keeps the row's level; two stories about one word leave the row alone
(`DrawingVocabulary.WithFloorWordsRankedBy`; the ladder and the composer rank from the same view
names; `TheFramingOverClausesRankTheSetsFloorWords`). `PlanSheetNaming.Parse` takes a vocabulary
outright now, and `TitleOf` strips only a `.dxf` (a sheet number holds a dot).

**Measured.** 31089-01: 45 views written where 21 were (four a building: foundation, ground, main,
upper), **3 storeys L1–L3** where none was, a model where none was — 0 walls, 0 columns, wood
frame, honestly empty; its eleven buildings are numbered, which the building tag does not read
(letters only), so they stack at one place: the next class. The 16 copies build nothing and say
whose file they hold.

WHAT THIS DOES NOT: a copy under a different NAME (grouped by name first, never hashed); numbered
buildings (BUILDING 1 … 11); a basement in a word chain (a basement word is a parkade level by its
own rule); a title of three lines; the title reader's failures (30768-01's "-", 30888-01's and
30980-01's word salad) — the next step; what a wood-frame townhouse's model should hold.

## 70. Step 61, 2026-09-14 early: the audit's queue closed — and what closing it found

§65 queued F8, F10–F22 and F24–F25 as "a line of work with its own measurement each". Each is
done, and three of them found something the audit had not said.

| # | The fix | Test |
|---|---|---|
| F8 | dash offsets measured from the direction's first segment along its normal, not from the page origin along each dash's own (`DashedLineJoiner`) | `ADashedLineIsJoinedWhereverThePageOriginIsTests` |
| F10 | `PlacedMembers.Near` by distance (Euclidean, to the micron): two readings 0.75" east and 0.75" north are two columns | `TwoReadingsOfAColumnMoreThanAnInchApartDiagonallyAreTwoColumns` |
| F11 | **the differential over entity order**: `DxfSheet.Reversed()`; the six sets composed as they are and with every view's entities reversed must be the same structure (Slow, beside the shifted one) | `TheSameDrawingsInAnotherOrderBuildTheSameStructureTests` |
| F12 | a loop vertex is on a curve when a curve's end is within a hundredth of it — by distance, not a cell key (`CurveEnds`) | (the frozen classifier and the six-set gate) |
| F13 | a node, a joint or a dash gap AT the tolerance is within it (`Within`) in `PlanLoopBuilder.NodeOf`, the composer's `PointAt`, the joiner's gap; the yardstick's 100 mm likewise | (the six-set gate) |
| F14 | the two `dxf.pdf` slab rows reach `Classify` on the one ingestion point; proved by breaking: a 25.8 sq m outline is no floor at the row's 37.16 and a floor at 1; the bridge row likewise | `ASlabRowReachesTheReaderThroughTheOneIngestionPoint` |
| F15 | the grid is drawn with one pen EITHER way: a 1 pt tendon along a 3 pt grid is not the grid | `TheGridIsDrawnWithOnePenTests` |
| F16 | the preferred export path refuses our own output and a columnless shell, as the model folder's did | `OurOwnOutputIsNeverTheYardstickTests` |
| F17, F18 | `ModelDiff`: a wall carries its extent along each axis (a wall turned in place is lost and gained); every member takes ONE partner (a second copy is gained) | `ATurnedWallAndASecondCopyAreSeen` |
| F19 | a set with none of ours inside her footprint and columns of hers on the shared storeys is a complete recall miss, said so in the summary and counted in `corpus-query summary` | — |
| F20 | "on her wall" is the distance to the wall's edges (`DistanceToWall`), not to its box | — |
| F21 | `grid-names`' (0, 0) clause is a question, not a diagnosis: the model does not say which sheet drew a member | — |
| F22 | one splitter for a sheet's DXF files (§68) | `ASheetsDxfFilesComeBackAsTheWriterJoinedThem` |
| F24 | the unit differential compares every joint's place to a ten-thousandth of an inch, every member's kind, storey and joints, and both D and B | `AModelIsTheSameInInchesAndMillimetresTests` |
| F25 | the ledger round trip asks the typed readers and asserts record equality | `TheCorpusLedgerRoundTripsTests` |

**What closing them found.** (1) *F24, the moment it compared positions*: the same two column
outlines 1.5 mm apart made one column at 112.06" in the inch model and at 112.03" in the
millimetre one. `DashedLineJoiner.Join`'s across-the-line tolerance was 0.15 of whatever the
drawing counts in — 3.8 mm on an inch drawing, a hair on a millimetre one — a length written as a
literal; it is `DashOffsetTolerance`, 0.15 INCH, converted with the rest. (2) *F11's differential,
before it ran*: the yardstick's frame was order-dependent (§68). (3) *The two-line title of §69, on
31168*: a view the old reader never split off — "LEVEL 40 (UPPER ROOF) PLAN CONCRETE OUTLINE
BLDG B", drawn under a two-line title on S2.34.1, whose content had been going to the LEVEL 38
view beside it — and with L40 named by a plan the ladder's roof rule fired: building C's "ROOF
PLAN" put a ROOF storey above tower B's 40th floor. *A building's roof plan names that building's
roof*: a tagged roof plan names `<TAG>-ROOF` after that building's highest level, and only an
untagged roof plan names the set's ROOF (`StoreysFromPlans`). (4) *Run 9, banked while this ran*:
step 58 alone over the corpus lost 47,963 columns on 144 sets and made ten yardsticks worse — a
PDF driver draws a filled rectangle as two triangles on its diagonal (§67's correction). *Two
filled triangles of one colour that share an edge and whose union is a convex quadrilateral are one
shape*: the first carries the four corners, the second takes the first's fate outright — the same
reason, the same object (`TriangleTwins`; the fixture holds a 600 mm column drawn so and a lone
triangle beside it). (5) *F11's differential, the first time it ran honestly* (its first cut broke
every R12 polyline into VERTEX entities and lost everything): all six sets built other walls with
their entities reversed — 31168: 473 lost, 241 gained — because `PlanLoopBuilder` seeded its walk
and numbered its nodes in arrival order. *The order the entities came in is not information about
the building* — and the one cut tried, a canonical geometric order (each segment from its lesser end,
sorted by that end then the other), was **reverted the same night**: it moved every set's walls
against the bank (31130: 117 lost / 99 gained), was still not the same under reversal on 31065
(6 columns), and moved a plate under the page-shift differential that had been green — an order
keyed on floating coordinates is the frame class of §63 by another door. The differential is in
the suite **skipped, red, with its numbers in the skip reason**; the next cut needs a rule for
which ring a shared edge belongs to, not a sort. That is the next step, and it is not small.

**Measured.** Fast suite 1,284 green. Six-set gate byte-identical against the bank re-banked in steps 59-60 (31065, 31168, 31170-arch, each with its reason there), the shifted differential green on all six, both at 10:14 on 2026-09-14. The
entity-order differential: red on all six, skipped with its numbers, the diffs under
`TestResults/reversed/`. Run 9 (step 58 alone) banked (§1b). **Run 10** (steps 58–61, 10:22 → 12:39): 238 of 278 jobs
build (15 rows are another job's file); columns 85,605 — the twin rule gave back what step 58
had thrown away and kept the symbols out; yardsticks 58% / 52%, 4 sets at 100%+, 11 at 75–99%;
and **walls 151,191 from 46,617**, which is the finding: 31066-01, rendered, is a wood-frame
block over a podium whose every stud partition — a filled band, tessellated — is a wall now.
The reader has no rule for what a filled band is a wall OF. That is the next reading step, and
no wall of run 10 reaches an engineer before it (§1b). And the night itself: the gate launched at 23:35 did not run until 09:41 — the session went
idle behind a queued background task and nothing after it ran; steps 59–61 sat uncommitted for ten
hours. A background wait is not a wait if nothing wakes the session; that is now a feedback rule.

WHAT THIS DOES NOT: F23 was closed in §65 with the collection; a permutation that is not a
reversal (the reversed differential is one draw); a roof plan tagged for a building whose plans
the chain names above their highest (covered, as before); the yardstick's residual statistics
(`<= 100` on a distribution, not a place decision).

## 71. Step 62, 2026-09-14: the second audit's brief A (the instruments), answered

`docs/codex/CODEX-PDF-INTAKE-STEPS-57-61-AUDIT-A-INSTRUMENTS.md` and its response beside it; 12
findings, source-deduced, each checked at the cited lines. All twelve real; all twelve fixed with
a test that encodes the counterexample, in the sitting after the response landed.

| # | The fault, verified at the source | Fixed in | Test |
|---|---|---|---|
| 1 | `ModelDiff` ran one greedy pass from each side: {0, 2} against {−2, 1} read one lost, none gained, where both pair | one MAXIMUM matching (Kuhn's augmenting paths over the candidate pairs, nearest first), read both ways | `OneMatchingIsReadBothWaysAndTheTwoDiagonalsOfABoxAreTwoWalls` |
| 2 | a triangle with two convex partners (a tessellation twin and an adjacent cell on a side) took whichever came first | the shared edge must be the LONGEST edge of both halves, as a diagonal is; two genuine hatch cells meeting on a diagonal are named as what geometry cannot tell | the fate fixture: a third triangle on the first's side stays a lone triangle |
| 3 | the twin lookup rounded a vertex to the millimetre and looked in one cell; a shared vertex a hundredth apart across a cell edge was never presented | the vertex's cell and its eight neighbours | the fate fixture: shared vertices at .499 and .501 |
| 4 | a wall was its centre and its extents along X and Y: the two diagonals of one box were one wall | a wall carries the angle of its longest edge (0–179°); partners match to a degree | the same test: one diagonal then the other, lost and gained |
| 5 | the unit differential compared XY multisets and sorted a member's joints: a Z offset and a panel's perimeter order were invisible | joints carry Z; a member's joints in their own order, rotated to the least; every storey's elevation compared | `AModelIsTheSameInInchesAndMillimetresTests` |
| 6 | it flattened every section's D and B into one multiset: sections could swap dimensions unseen | each member carries its section's own dimensions in inches | the same test |
| 7 | `CorpusDiff` called a set with every count the same "Unchanged"; a changed `SheetsWritten` was ignored | the class is `SameCounts` (counts, not sameness) and `Views` (the sheets read differed) comes before Placement | `WhatMovedBetweenTwoRunsIsClassedByTheFirstThingThatChangedTests` |
| 8 | a complete recall miss (none of ours inside her footprint, columns of hers there) had no yardstick verdict in `diff` | a set has a yardstick when ours OR hers were judged on both sides; the table prints theirs beside ours | the same test |
| 9 | "better / worse / same" was by supported COUNT: 8 of 64 → 5 of 10 read as worse | the verdict is by SHARE, to half a point, and the column says so | the same test |
| 10 | `DxfSheet.Reversed` kept a POLYLINE whole and broke an INSERT with attributes following into INSERT, ATTRIB, SEQEND | an INSERT with 66 = 1 runs through its ATTRIBs to its SEQEND | `AReversedViewHasItsEntitiesInTheOppositeOrderAndNothingElseMoved` |
| 11 | the yardstick's last tie-break, "the smaller move", depends on where either model sits: ours at 0 between hers at ±1,000 chose −1,000; ours at 1,500 chose −500, the OTHER column | an exact tie keeps the lower bin in key order — the same correspondence whichever frame either model sits in (§64 dropped the same tie from the by-name fit for the same reason) | `TheYardsticksFrameIsJudgedBySupportAndNeverByTheOrderOfOurColumns` |
| 12 | that test never pitted support against votes | four of hers clustered under one of ours: four pair votes, one supported; the true frame with three and three wins | the same test |

The response also answered question C from the source — where entity order enters
`PlanLoopBuilder.Build`: the seed order and global edge consumption first, then node creation
and numbering (the first coordinate encountered is a node's representative, which can change the
partition itself), then adjacency insertion order at a junction, then `PickContinuation`'s tie,
then `BridgeChains` on an already order-dependent chain list. A rule for which ring owns a shared
edge must decide seed and edge consumption, continuation at junctions, and what becomes of consumed
edges when a walk fails; node equivalence is a separate, earlier ambiguity. That is the
specification of the next step on the red differential, and it is recorded here so the next
sitting starts from it, not from the symptom.

**Measured.** Fast suite 1,285 green; six-set gate byte-identical and the shifted differential green at 13:30 (the twin rule tightened and the yardstick tie changed touched none of the six).

WHAT THIS DOES NOT: brief B (the reading rules) is not yet run; the red differential is still red
(§70); walls on wood-frame sets (§70's run 10) are not yet a rule.

## 72. Step 63, 2026-09-14: a wall is six inches or more — and brief B answered

**Measured first.** Run 10's 151,191 walls (§70) were looked at on 31066-01: every stud partition
of a wood-frame block over a podium, a filled band a driver tessellates, read as a wall now that its
two triangles are one shape. `dxf-inspect --members` on its L2 plan: 139, 142, 112, 115 mm thick —
2×4 and 2×6 walls with their sheathing. Migration 064 had measured the floor on ONE model (31138:
736 walls, thinnest 6 in) and left it at 4 in deliberately, because on the DXF side the only things
under 6 in were 3.1–3.4 in linework and "admitting a 4-inch line produces a member the engineer can
see and delete". Measured again over **every engineer's model on hand — 101 exports, 51,127 wall
areas**: 6 in 684 (1.3%), 8 in 5,921, 12 in 10,133 (19.8%), 24 in 6,222 (12.2%), up to 60 in, and
**not one under six inches**. 2,620 stud walls a set is not a cost an engineer can pay by deleting.

**The rule.** *A wall is six inches or more.* `dxf.min-wall-thickness` 4 → 6 in (migration 090, on
both rows that carry the key: the 036 convention and the 064 ruling — a ruling outranks a
convention in `vw_RuleSetting`, and the first cut of the migration updated the convention alone
and the reader still read 4). **What the floor can and cannot tell.** The first cut held it to an
eighth of an inch — the gap between a 2×6 stud wall (5.5 in) and six — and the gate refused 60
walls on 31065's concrete tower: `pdf-at` on its south-tower plan shows six-inch walls DRAWN at
142–150 mm (5.6–5.9 in), which is where a 2×6 stud wall lands too. Thickness alone cannot tell a
thinly drawn six-inch wall from a 2×6; it can tell a 2×4 (89–115 mm), which is what the floor
refuses on a wood-frame set (31066-01's 112 and 115 mm bands). So the floor carries the same
half-inch slack as the other limits (`WallFloorSlackMm`), and a filled band of wall proportions
under it is `ThinBand` — a stud wall, a curb, a line drawn wide — not a wall and not a slab
candidate (`ABandThinnerThanTheFloorIsAThinBand`; the fate fixture holds a 115 mm band and a
152.4 mm one). The 2×6 stud wall needs another discriminator — the fill, the set's typology — and
is named below as not done.
**And the floor is applied TWICE, once per side.** The reader (`GeometryFilterService`, in mm) reads a
band into a wall view; the composer (`StructuralPlanClassifier` and `WallOutlineDecomposer`, in the
drawing's unit) reads that view's outline again and compares its thickness to `MinWallThickness`
— and did so with no slack (a hundredth of an inch in the decomposer, none in the classifier). With
the row at 4 the composer's floor never bit; at 6 the gate lost walls the reader had read: 31168
67 (`pdf-at` at a lost wall's coordinate: `Read BecameWall #11 filled 5490x154 mm` — read, then
gone in composition), 31065 60, 31138 49, 31202 20. The composer now carries the same half inch as
the reader (`PlanClassificationOptions.WallFloorSlack`, converted with the other lengths;
`WallFloor` = the row less its slack, at the decomposer's face-pair separation, the classifier's
ribbon band, open face pairs and the filled rectangle). With the slack on both sides the gate's losses fell from 60 / 49 / 67 / 20 walls (31065 / 31138 / 31168 / 31202) to 0 / 1 / 4 / 5, and 31065's six-inch walls are all there (KOR-W152.4: 130 → 130). Two readers of one rule
with two tolerances is the class the options merge (§0's next efficiency step) removes.
And `dxf.dash-offset-tolerance` (0.15 in) is a row in the same migration: step 61 shipped it as a
compiled option, which `EveryNumericOptionIsARuleOrDeclaredNotOne` refused the first time the slow
suite ran — the fast suite had never asked.

**Brief B, answered.** `docs/codex/CODEX-PDF-INTAKE-STEPS-57-61-AUDIT-B-READING.md` and its
response; 10 findings, all real, all fixed with tests in the sitting after they landed:

| # | The fault, verified at the source | Fixed in | Test |
|---|---|---|---|
| 1 | the numeric readers scanned the whole title: "MAIN FLOOR PLAN SHOWING LEVEL 2 FRAMING OVER" read level 2 where "… 2ND FLOOR …" read 1 | the numbers are read from the part before the framing-over clause too; "LEVEL 4" over a word anchors the chain as "4TH FLOOR" does | `TheNumbersAreReadBeforeTheClauseAndTheChainsAreOnePerBuilding` |
| 2 | the ladder started from the STATIC vocabulary — the previous job's derived ranks — where the composer started from the rows | `PdfOnlyBuild.WriteLevels` sets the office's vocabulary (`DxfToEtabsService.OfficeVocabulary`) before the ladder; `Run` restores it after the set's chain | (the serialised collection; the class is F23's) |
| 3 | a roof plan tagged for a building none of whose storeys the model names fell back to EVERY storey and landed on tower B's 40th floor | a tagged sheet with no storey of its building goes nowhere | `ABuildingsRoofGoesNowhereWithoutItsBuildingAndItsElevatorRoofIsAboveIt` |
| 4 | two buildings' chains fused into one (A's GROUND → MAIN, B's MAIN → UPPER) | one chain per building tag; they must agree on every shared word or the row stands | the words test |
| 5 | a cycle with a tail (… UPPER → MAIN) and a floor shown over itself were ranked | every clause must agree with the chain; a self-reference is two stories | the words test |
| 6 | a building's main roof and elevator roof collapsed to one `<TAG>-ROOF` | `<TAG>-ELEVATOR ROOF` above `<TAG>-ROOF` | the roof test |
| 7 | an underlined note under "LEVEL 3 PLAN" joined it as the title's second line | a title's second line is made of title words — at least half its words are the vocabulary's, a plan kind's, a number or a tag ("CONCRETE OUTLINE - NT"); a note is a sentence. The first cut asked the line to BEGIN with a title word and lost 31065's and 31168's second lines, which begin with CONCRETE | `ANoteUnderATitleIsNotItsSecondLineAndTheJoinReachesTwoHeights` |
| 8 | a sole joined view on a sheet whose own name says no storey was written as `job-p01.dxf` | written under the view's title | the same test |
| 9 | `Parse` took `.01_1_MAIN FLOOR PLAN` for an extension without `.dxf` | only a `.dxf` is an extension | the words test |
| 10 | the join reached 2.2 heights where §69 said two; the fixture sat inside both | two heights, and the fixture sits on the boundary (16 pt joins, 17 does not) and holds the interposed-line case | the same test |

**Measured.** Fast suite 1,289 green; rows test green; the gate (six sets + shifted, 15:02–15:14,
12 min wall; the six-set test 5 m 46 s): the shifted differential green; 31130 byte-identical; the
five others re-banked after EACH diff was looked at with `pdf-at`, `model-to-page` and
`dxf-inspect`, by wall section (bank → now, only where changed):
- 31138: −1 KOR-W101.6 — a 4-in face pair on the LEVEL 2 plan (two w9pt lines 101 mm apart, p24),
  rising to L3. Refused by the rule.
- 31202: −4 KOR-W101.6 — the same 101-mm pair along y = 61,316 on L3 and L4 (p20); and −1
  KOR-W1219.2, a "wall" 102 mm LONG and 1,219 thick — a 4-in band read sideways — gone with it.
- 31065: +1 KOR-W203.2, 4,881 long at L1 — the east 4.9 m of a 16.3 m, 199-mm wall outline on the
  P1 north plan (`dxf-inspect`), which composed without that piece before; every other section
  count identical.
- 31168: KOR-W203.2 62 → 66 and nothing else — three long 8-in walls are each now two collinear
  panels whose lengths sum to the old one (11,592 + 2,435 = 14,027; 19,596 + 5,686 = 25,282).
- 31170-arch (the architect's set): −12 W101.6, −4 W114.3, −4 W127 (the partitions); three walls
  re-measured 165.1 → 152.4 (a partition's face no longer in the pair); two overlapping 36.8 m
  roof-line walls 127 mm apart now one; L8 (roof) 29 → 16. And ONE FALSE POSITIVE, named: KW79
  on L3, 647.7 thick, 1,372 long at (20,370, 42,171) — a 0.6-pt line 1,372 long at y = 41,848
  now closes a ring with a 406-mm wall's face at 42,495 (p11/p12), because the partition outline
  that used to close a different ring through those lines is no longer in the view. Which ring a
  loose line belongs to is the ring-ownership class (§71); it is carried to that step, not hidden
  here.
- Run 11 (the corpus on step 63) is next; its walls are the number to watch.

WHAT THIS DOES NOT: a 2×6 stud wall (5.5 in — the same drawn thickness as a thin six-inch wall; not
separable by thickness, the next discriminator is the fill or the set's typology); a concrete wall
drawn under six inches by another office (none in 101 models here); a wood-frame set's model beyond
"no concrete walls" (what it should hold is still the
plan's question); a set whose buildings genuinely order their floor words differently (the row
stands for both); a title of three lines.

## 73. Step 64, 2026-09-14 afternoon: what the yardstick's 58% is made of — her model's age

**The question.** Run 10 judges 48 sets against the engineer's own model and reads 58% of our
columns within 100 mm of one of hers; 23 sets sit under 50%. Before another reading rule: what IS
the residual on a low set? Three instruments, each in `takeoff model-yardstick` now:

1. **The rigid part of a storey's error** — the median offset VECTOR over the storey's pairs
   within 600 mm, and the median after it is taken out. A sheet set 300 mm off the grid shows as a
   rigid (300, 0) and a median of nil after; a storey read badly shows a rigid part near zero and a
   spread that stays. On 31053 (28%) and 30993 (33%) the rigid parts are ≤ 44 and ≤ 73 mm and
   removing them changes nothing: **not placement**.
2. **Each model against the plans' grid lines** — of our columns and of hers (in our frame), how
   many stand within 50 mm of a grid line in X, in Y, and of an intersection. Her joints are NOT on
   the intersections: 0% on 31053 and 30993, 1–13% on six more; nor are ours. **Not a grid
   convention** (the "face on grid or centred" question of §59 is answered: neither).
3. **`--pairs [storey]`** — every judged column of ours with the offset to the nearest of hers and
   both sections. On 31053's L10 the offsets scatter in every direction, 30 to 600 mm, between
   columns of the SAME size on both sides (ours `KOR-C304.8x762`, hers `C14x30`); two of the 23
   match to the millimetre. `pdf-at` on one: the drawn column is a filled 305 × 764 box centred at
   (72,950, 49,378); ours is at (72,951, 49,378); hers is 366 mm west, outside the drawn box, and
   nothing is drawn there. **Her model does not match the drawing.**

**Why: the yardstick's own date.** The export manifest (`yardsticks\export-2026-09-11.csv`) names
the .EDB each yardstick came from; its last write on the share, against the stick file's issue date,
for every yardstick set of run 10 with both (38 of 48):

| her model older than the drawing by | sets | median share of ours within 100 mm |
|---|---|---|
| more than 180 days | **27** | **48%** |
| 180 days or less (either way) | 11 | 64% |

The worst are the oldest: 31009 judged by a 2022-10 model against a 2026-08 drawing (1,415 days,
34%); 70057 1,451 days (44%); 31048 1,094 (33%); 31005 938 (0%); 31053 618 days (28%, the column
above). And ten of the 38 say by their NAME that they are not the drawing's model at all: `MASS
MODEL`, `2NDRY ELEMS`, `Prelim Model`, `secondary elements`, `Below Grade`, `Diaphragm Check`,
`Wind SLS`, `mass`. The exporter takes the newest .EDB in the folder, and the newest is what it is:
the lateral model is built at design development and seldom follows the drawings.

**What ships.** `CorpusAnalyzer.YardstickProvenance` reads the manifest (the `edb_written` column
where the exporter recorded it — `EtabsExportE2k` writes it now — else one stat of the .EDB on the
share, never a walk); three ledger columns at the end of `ledger-sets` (`yardstick_edb`,
`yardstick_written`, `yardstick_age_days`; older ledgers read with them null); the corpus summary
reads the share in two populations, current (≤ `CurrentYardstickDays` = 180) and older, with the
older population's median age; `corpus-query yardsticks` prints the date and the age beside each
verdict; each set's `yardstick.txt` opens with her model's name, date and age. The ledger's DB table
does not carry the three columns yet (a migration; Ian's).

**Measured.** `AStoreysResidualIsReadAsItsRigidPartAndWhatIsLeftAndEachModelIsPlacedAgainstTheGrid`
(a storey 300 mm over reads rigid (300, 0) → 0 mm; a scattered one reads rigid 0 → 300 mm stays;
hers on the intersections 16 of 16, ours 8 of 16); the ledger round trip carries the provenance
(41 fields); fast suite green. Run 11's ledger is written by the mirror's build (before this step)
and carries no provenance; run 12's will.

**What this changes.** The 58% is not one number. On the 11 sets whose model is current the share
is 64% and the faults there are OURS to find — 31162 (40%, 58 days), 60061 (31%, 111), 31158 (14%,
94, a "Prelim Model"), 30989 (59%), 31108 (60%), 70064 (64%) are the next `--pairs` to look at. On
the 27 whose model predates the drawing by a median of two years the yardstick judges a different
building and no reading rule will move it; what would is a yardstick that IS the drawing's model —
her current gravity model, or the model she builds from our output.

WHAT THIS DOES NOT: separate a model's KIND (mass, secondary, below-grade) from its age — the names
are read by a person here, not by the code; date a yardstick whose export has no manifest row (10 of
48); a rotation between the models (the rigid part is a translation); tell a column she moved in
design from one we misread on the 27 stale sets — only a current model can.

## 74. Step 65, 2026-09-14 evening: run 11, and the reference plan is the plan the others can be set on

**Run 11** (15:43 → 18:04, 2 h 21 min at 6 workers; step 63; `ledger-sets-2026-09-14-run11-step63.csv`;
DB run `42e683f8`): 239 of 295 build (two new rows in the census); **walls 115,664 from 151,191** —
the stud partitions of the wood-frame sets, gone as §72 said; columns 81,564 from 85,605; yardsticks
57% / 55% (6,296 of 11,130; theirs 6,272 of 11,485 — theirs up three points). `corpus-query diff`
run 10 → 11: Composition 180 (yardstick 6 better / 2 worse / 22 same), Views 16, Placement 9,
Storeys 4, NewModel 1 (80045-04), SameCounts 83. The read is 776 CPU-minutes — **5.4 s a page** —
and 85% of a set's cost (30993-01: 874 s to build, 125 s to recompose from its recorded views).

**One regression, looked at: 30993-01, Placement.** 28 of 78 views placed in run 10, 2 in run 11;
yardstick 157 → 133. Its reference plan — the sheet whose axes become the grid when no model has
one — became `S7.02 EARTHQUAKE SENSOR LAYOUT LEVEL 4 PARTIAL PLAN`: it names 45 "axes" (its sensor
bubbles, read as grid labels), had no storey in run 10, and B1 read "LEVEL 4" in run 11, so it
won the reference on axis COUNT. The 28 structural plans name the same axes (`grid-names`: 13 of 13
of the LEVEL 2 NORTH SIDE plan's names are on it) at another scale — the names agree, the fit does
not — and "could NOT be set on the grid by name".

**The rule.** *The reference plan is the plan the most other plans can be set on* — a fit by name
that agrees on one offset (`SolveByName`), counted per candidate; between plans carrying the same
number, the one the others match on more axes; then the plan's own axis count; then the name. The
first cut counted shared NAMES and still chose the sensor layout (the names agree; the fit does
not) — tried on the set before anything else this time: recomposed alone, 2 minutes, still 2 of
78; the fit-based rule: 28 of 78, reference `LEVEL P2 PLAN NORTH SIDE`, yardstick 157 of 484.

**On the six.** 31130, 31138, 31202, 31170-arch byte-identical. 31065 moved — 388 columns and
169 walls "lost and gained" — and the diff's own header says why: *the second model sits −23,139,
+0 from the first, by 13 X and 7 Y grid labels*: the reference moved from the north foundation plan
to the south (both carry 24 of 24 sheets), and two sheets' fits to the new one differ by 16–21 mm.
Her model is the verdict and calls it a wash: 492 → 489 of 581 within 100 mm, median 15 → 25 mm.
Re-banked as the same members in the south sheet's frame. 31168: two P2 walls the step-63 frame
had split into collinear pairs are one panel again (11,592 + 6,742 = 18,334; 19,596 + 5,686 =
25,282); `pdf-at` at the junction: one filled wall 203 × 17,979 mm, nothing crossing it — the ink
says one. Re-banked. That a 2 cm change of frame flips a wall between one panel and two is the
ring-ownership class (§71; `PlanarRings` is the prototype, §75 when it is wired).

**Measured.** `TheReferencePlanIsTheOneTheMostOtherPlansShareTheirAxesWith`; fast suite 1,305;
six-set gate byte-identical after the re-bank; shifted differential green. Run 12 = this step over
the corpus as a `--recompose` at 12 workers (composer-only; the views stand): **27 min 40 s** (18:58:44 → 19:26:24) for 296 sets, against 2 h 21 min for the read — the number for a composer-only pass. 240 of 296 build; views set on the grid **2,285 from 2,093** (+192, the rule); yardsticks **58% / 55%** (6,539 of 11,276; theirs 6,519 of 11,797); 15 sets at 75–99% (from 12), 14 at 25–49% (from 15). Its ledger CSV was lost to a race — a nine-set recompose of mine wrote the work folder's `ledger-sets.csv` forty seconds before run 12 finished; its rows are in the DB (run `8a174b24`) and its per-set log is whole. Never two analyzers in one work folder.

**The process, corrected the same evening** (Ian: "be the fixer"). Reproduce on the failing set
before the rule, the test and the gate — the first cut of this rule cost an 8-minute gate to learn
what a 2-minute recompose would have said. A composer-side step recomposes the corpus; only a
reader-side step reads it. And the read itself is being removed as a cost: the PDF walk runs twice
a page and again on every reading-rule change, though a PDF never changes — the record of the raw
walk, from which both reads derive to the bit, is Codex's task (`CODEX-PDF-INTAKE-PAGE-READ-CACHE.md`).

WHAT THIS DOES NOT: read a sensor bubble as not-an-axis (the bubbles still become named axes; the
rule only stops them leading); refuse an instrumentation sheet's members (a sheet-type row);
choose between two references that carry the same sheets on the same axes by anything but the
count of their own names and the file name; a set where no two plans share three names.

**Looked at and NOT fixed, so the next sitting starts from the instrument it needs.** 31162-01 is
one of the eleven sets whose yardstick is current (58 days), at 40%: of the 20 columns of ours her
model lacks, six are `KOR-C203.2x254 … x558.8` — 8-inch WALL FRAGMENTS. `pdf-at` at one: the P1
plan draws an 8-in wall through a 398 mm clip at a corner (Revit draws wall joins as clipped
fills); the reader writes the clipped piece as a 398 mm pier (`PierMinLengthMm` is 305); the
composer turns a 203 × 398 wall-layer loop into an 8×16 column. Two edits were tried on the
classifier — a short wall-layer loop leaves the "nothing paired up" branch as a wall, and `RunsInto`
meets within half of each thickness instead of one drawing unit — and 31162 recomposed byte-for-byte
the same: the fragments come through a branch neither touches, and `dxf-inspect --walls` says the
398 × 203 loop makes "1 panel". Both edits are stashed, not shipped. What was missing is the
instrument: **every composed column traced to the loop and the branch that made it**
(`dxf-inspect --columns`: layer, loop box, branch name, and the wall it stands in if any). That is
the next step for this class, and it is built before the next edit.

## 75. Step 66, 2026-09-14 evening: one plan naming no storey is a one-storey building

Worked from the ledger, not from a run: `corpus-query no-model` on run 11 lists 21 "no storeys"
sets, and nine of them are one plan — a garage, a tenant improvement, a sales centre — titled PLAN,
PLANS, PLAN AND DETAILS, GENERAL NOTES AND PLAN. **The rule:** *a set whose single plan names no
storey is a one-storey building; that plan is L1.* Two halves in two places: the ladder
(`StoreysFromPlans.Merge`) adds L1 — a FOUNDATION plan alone still names no storey, and two unnamed
plans stay unnamed, a foundation plan and a framing plan of one storey being one storey — and the
composer (`PlanSheetNaming.MatchStories`) puts the set's one plan on the model's one storey instead
of refusing it for having no level number (the first cut did only the first half: nine models with
one storey and nothing on it, "0/1 placed" — the recompose said so in 100 s).

**Measured** by recomposing the nine, twice, in 100 s each: 8 of 9 build with their members —
01746 23 columns, 01569 104, 30996-02 19 walls + 28 columns, 50046-07 825 columns, 31083-04 9 + 8,
30865-05 20 + 30, 01603 4 walls, 01715 1 wall. The ninth, 01788-01, has a blank title (the
small-job title block; Codex brief `CODEX-PDF-INTAKE-SMALL-JOB-TITLES.md`, nine sets of that
class with their words harvested). `OnePlanNamingNoStoreyIsAOneStoreyBuilding`; fast suite 1,308;
six-set gate byte-identical. Built in a worktree while Codex's page-cache edits sat half-written
in the main tree.

WHAT THIS DOES NOT: a set of two unnamed plans; a one-plan set whose plan IS a foundation plan;
what those 825 "columns" on 50046-07's one page are (the yardstick has no model for it).

## 76. 2026-09-14 night: the read's cost — the page record, the profile, and a run that dies keeps its ledger

Ian's question, verbatim: *"why do you KEEP ingesting and reading them EVERY test you run?"* Three
answers, in the order they were found, and one of them was wrong first.

**The page record (`33d6dc7e`, Codex, brief `CODEX-PDF-INTAKE-PAGE-READ-CACHE.md`).** The intake
walked every PDF page twice (a thinned and an unthinned read). `VectorPageReader.Walk` now records
what PdfPig saw — every flattened point, the Close command, colour, width, clipping, ordinals, words,
annotations — and `Derive` reproduces both reads from the record through the same `AddPoint` and
closure branches; `PageReadCache` keeps it under `kor-drawings/pages/<sha>/` as versioned gzip
binary, written atomically. **Measured, and it was not the saving:** the six-set gate forced to
read, byte-identical both ways, COLD (writing the record, 94 MB for six PDFs) 5 m 47 s, WARM
6 m 30 s. The walk is not where the read's time goes. The record stays (one walk, no PDF opened on
a warm read); the profile moved one layer down.

**The profile (`ad92d305`).** `dotnet-trace` on 31168's 63 pages (51 s): WriteSheets 47.7 s →
Classify 37.5 → SlabEdgesFromLoops 36.3 → PlanLoopBuilder.Build 36.2 → **BridgeChains 36.1**
(TryJoinByExtending 26.9 + RayIntersection 15.2). The PDF walk was 1.9 s; the schedules 4.5 s once.
BridgeChains tried every pair of open chains and restarted after every merge — 76% of the set's
read. A pair can merge only if two of their ends are within the bridge tolerance, or both reach one
corner within the extend limit, so within twice it; every other pair fails every test. The
candidates now come from a grid over the chain ends (cell = the larger of those reaches,
`reach = max(bridgeTolerance, 2 × extendLimit)`), scanned in the same ascending order, re-indexed
after each merge: **the first mergeable pair is the one it always was.** 31168's set 51 s → 16 s,
all 37 views byte-identical; the six-set gate reading plus the shifted differential 3 min (the read
alone had been 8 m 22 s).

**The gate's read cache (`203dfa44`, Codex, brief `CODEX-PDF-INTAKE-GATE-READ-CACHE.md`).**
`SixSetsBuildAsBankedTests` keys each set's read on the PDF's hash, the scale, and a hash of the
reader's sources (`PdfToSafe/`, `Intake/` less the analyzer files, and eleven named `Dxf/` files —
`PlanSheetNaming.cs`, `DrawingVocabulary.cs`, `StructuralPlanClassifier.cs`, `PlanLoopBuilder.cs`
among them) plus the options; a composer-only change recomposes the six in about a minute, a reader
change reads them in about three. The cache is honest by construction: it cannot serve a read made
under different reader code.

**Run 13 (a full read on `ad92d305`, 12 workers, 20:14).** It built 271 of 296 in 46 minutes and
**died with the session that launched it** — `Start-Process` from the tool is a child of the
session, and the ledger was written once at the end, so 271 rows existed only in the log. Fixed in
two places (`e44e76ce`): every finished set appends its row to `ledger-sets.partial.csv` (header
first, one lock; the sorted ledger is still written at the end), and a corpus run is launched
OUTSIDE the session's process tree (`Win32_Process.Create` with a `cmd /c "… > log 2>&1"` line),
verified alive before anything else is said about it. Recovered with `--reuse` in 3 min, the six
sets a KorStandards SQL outage had failed rebuilt with `--jobs … --force` in 3 min; banked as
`ledger-sets-2026-09-14-run13-step66.csv` (§1b of the plan): 251 of 296 build, 58% / 53%, diff
run 11 → 13 SameCounts 249 / LostModel 0 — the BridgeChains index changed nothing on 249 sets.
CPU per set over the 239 common sets 704 → 627 min (−11%): 31168's 3× was an outlier; most sets
are compose-bound, and the wall-time gain (2 h 20 m → ~46 min) is the worker count.

WHAT THIS DOES NOT: make the read free — a full corpus read is ~46 min at 12 workers, and a reader
change still owes one; profile the compose (the next cost); wire PlanarRings (§71, `048912f1`,
prototype only).

## 77. Step 67, 2026-09-14 night: a wall the drafter did not fill is a wall only at a retaining wall's thickness — the wood-plan rule

Step 63 (§72) took the floor to six inches and 35,000 stud partitions out of the corpus; on
31066-01, the wood-frame block over a concrete podium that run 10 had rendered at ~500 "walls" a
storey, what was left (L2 145, L5 98) was mostly stud walls drawn as two parallel lines the drafter
never poché'd, thick enough for the six-inch floor's slack to let through. Thickness alone cannot tell a 2×6 wall from a concrete one (a
filled 140 mm band is a poché'd 2×6; an unfilled 203 mm pair is a retaining wall on a house plan),
so the rule is about the SHEET:

**Measured on seven plans:** wood plans read ~80% of their walls from unfilled line pairs (31066 L2
117 of 145, L5 77 of 98); concrete plans 0–35%. **The rule** (`WallTypeTagging.StudWallsOfAWoodPlan`):
*a sheet with two thirds or more of its walls read from unfilled pairs, and at least twenty of them,
is a wood plan; on a wood plan an unfilled pair under 8 in and a filled band under the six-inch floor
— without the slack, the slack being for concrete walls drawn thin — are stud walls,* moved to the
partition layer the model does not read. Applied in `Apply` and, for a set with no assemblies, from
`DrawingIntake.ReadPage`. The first cut (a simple majority) moved walls on four concrete sets; two
thirds and twenty left five of the six harness sets byte-identical. 31066 p8: 312 walls to the
partition layer; the composer's walls now 170–300 mm (the podium and the party walls); the 140 mm
gone.

**31170-arch lost 2 walls** and was not banked until looked at: `pdf-at` at the 38.7 m one on the
architect's L6 plan found the slab edge drawn as several concentric outlines 82–91 mm apart, two of
which the face-line rule had read as a 3-inch wall. Not a wall; re-banked (`c87d85e3`).
`AWoodPlansStudWallsArePartitionsTests`: the sheet-kind decision by share and count, both kinds of
stud wall partitioned, the retaining and six-inch walls left alone, a concrete plan untouched.

The 8 in is compiled (`UnfilledWallMinThicknessMm` = 203.2, Convention, the ratchet's 49th);
migration `091_ASitePlanIsNotAStructuralPlanAndAnUnfilledWallIsARetainingWall.sql` (written,
**not yet applied**, Ian's) inserts `dxf.pdf.unfilled-wall-min-thickness-mm` and adds SITE PLAN /
INSTRUMENTATION / SENSOR LAYOUT to `dxf.non-structural-sheet-patterns`; the code does not read
the row yet. Built in the worktree `Operations-s66` while Codex edited the main tree.

WHAT THIS DOES NOT: a wood plan whose partitions ARE filled and thicker than the floor; a concrete
plan drawn mostly unfilled (it would read as wood — the seven plans measured had none under 35%);
whether the 8 in is the office's line (a row, once measured on more than seven plans).

## 78. Step 68, 2026-09-14 night: the small jobs' title blocks — and every composed column says what made it

**Step 68 (`f5d56e79`, Codex, brief `CODEX-PDF-INTAKE-SMALL-JOB-TITLES.md`).** The nine no-storey
sets left after step 66 were blank-titled: the small-job template labels its title block with a
standalone TITLE, DRAWN:, CHECKED: — short labels `TitleBlockFields` did not know — and glues the
sheet number to the title (S-6BASEMENT FLOOR PLAN). Two rules: the short labels, and a value whose
left edge passes the column's narrow margin is no longer dropped; `PlanSheetNaming` strips a sheet
number glued to its title (`SheetNumberPrefix`, used in `TitleOf` and the sheet's own name) before
every numeric reader, so S-5 is no longer level 5. One fixture Codex wrote beyond the evidence (a
hyphenated number glued to a title) was dropped until a drawing shows it. **Measured:** the nine
re-read in 2 min — 00904, 01746, 30996-02, 31083-04 build (one plan, titled, L1); 01375's four
plans read P1 / P1 / L1 / L2 (a 3-storey house, 0 of 4 placed — next); 31057 L1. Still blank:
**01783** (needs migration 091's SITE PLAN pattern), **01589** (a rotated title strip — its words
harvested to `scratchpad/01589-p7-titleblock.txt`: FOUNDATION and PLAN stacked at x 2439, the
revision words at x 2292–2340), **01788** (foundation-only by design). Fast suite 1,340; six-set
gate byte-identical (a read, 2 m 20 s).

**The column trace (`4ffd4bba`, Codex, brief `CODEX-PDF-INTAKE-COLUMN-TRACE.md`).** Two blind
classifier edits for 31162-01's 8-inch "columns" had moved nothing (stashed, unshipped). The
instrument instead: every site that adds a `ColumnFootprint` names its branch
(`column-layer-loop`, `declared-size`, `short-wall-layer-loop`, `standalone-stub`,
`nothing-paired-up`) in `ColumnFootprint.Origin`, carried and never read by a rule;
`dxf-inspect --columns` lists each column's layer, loop box, branch and whether a wall panel of the
sheet contains it; `corpus-query columns <job> --ledger <dir>` sums a branch × inside-a-wall table.
On 31162-01 the table says why the edits could not move them: 66 of 69 are `column-layer-loop`,
1 inside a wall — the READER put them on the column layer, and the listing shows them as
541 × 1,283 mm filled rectangles on the P1 plan, footing-sized, where her columns are 12 × 30 in.
The class is a reader rule, not a composer branch — and on the other five current-yardstick sets
under 65% the same table (run 13's work dir) reads:

| set | column-layer-loop | inside a wall | other branches |
|---|---|---|---|
| 31162-01 | 66 | 1 | standalone-stub 3 |
| 60061-03 | 216 | 0 | — |
| 31158-01 | 24 | 6 | — |
| 30989-01 | 408 | 2 | standalone-stub 26 |
| 31108-01 | 164 | 13 | — |
| 70064-01 | 60 | 2 | — |

So on the six sets the composer's wall-derived branches are not the columns' source; what differs
from her model is what the reader classes as a column on the column layer, and on 31162 those are
footings. No rule was written from that — a footing-vs-column rule needs the per-column looks
(`pdf-at`, `--pairs`) on more than one set, recorded here as the next reading class.

WHAT THIS DOES NOT: read 01589's rotated strip or 01783 before migration 091; place 01375's four
plans; tell a footing from a column (the trace only says which branch made each one).

## 79. Step 69, 2026-09-14 night: numbered buildings — BUILDING 1, BLDG 1A, 12-LEVEL 3 are building tags

Plan titles on ten corpus sets number their buildings (BUILDING 1 LEVEL 1 PLAN … BUILDING 5 ROOF
PLAN); the vocabulary read a building tag as a letter only, so on 31185-01 the five buildings' LEVEL 1
plans all landed on one storey. **The rules** (`be2b28f3`): `DrawingVocabulary.Building` and
`PrefixBuilding` accept a letter or a number with an optional letter (a word boundary closes the tag,
so BUILDING PERMIT names nothing); `PlanSheetNaming.Parse` splits the tag group on `&` (1A is one
building, A & B two); `ModelYardstick.BuildingPrefix` — which had been `^[A-C]-`, reading building D's
storeys as the whole job's and a numbered building's as nothing — is any tag before a storey word,
and never 31170's own `L-1`; `NamedForAnotherBuilding` uses that one definition instead of a second
regex.

**The first cut was wrong and the recompose said so in two minutes:** with numbered tags, B3
(a tagged sheet only matches its own building's storeys) sent every tagged sheet of 31185 nowhere
(walls 337 → 174) and the per-building roof of step 61 stacked five roofs (`5-ROOF … 1-ROOF`) up
the ladder. The refinement: *a tagged sheet keeps to its building's storeys only where the model
names any storey by building* (`storeysByBuilding`); on a ladder with no building in any storey
name — five numbered buildings on one plan-named ladder, L1 and L2 for all five — the storeys are
shared, and when MORE THAN ONE such building has a tagged roof plan they share one ROOF over the top
storey (`StoreysFromPlans`); one building's tagged roof over shared storeys is still one storey and
keeps its name (31168's C-ROOF, brief B's B6). **Measured** by recompose: 31185 4 storeys / 337
walls / 68 columns (as run 13, now on the right storeys); 31066 1,392 walls and 30978 2,153 walls
unchanged. `NumberedBuildingsOnOnePlanNamedLadderShareItsStoreysAndItsRoof`; numbered assertions in
`StoreyNamesMeetAcrossTheTwoRoutesSpellings`; fast suite 1,353; six-set gate green (cached read,
recompose). Run 14 measures the ten.

WHAT THIS DOES NOT: a numbered building whose OWN storeys are named on the ladder (1-L2) — the
per-building roof still applies there, untested on a real set; two buildings sharing a ladder but not
a roof height.

## 80. Run 14 and step 70, 2026-09-14 night: the full read after steps 67–69, its eleven storey movers looked at one by one, and three rules from them

**Run 14** (a full read on `be2b28f3`, 12 workers, 22:28 → 23:21, **53 min**; launched by `run14.cmd`
through `Win32_Process.Create`, which banked its own ledger the moment the analyzer exited;
`ledger-sets-2026-09-14-run14-step69.csv`; DB run `4db4dcf3`): **253 of 296 build** (251 in run 13);
walls **104,506 from 115,875** (the wood-plan rule, §77), columns 81,725; 51 yardstick sets **58% / 53%**
(6,536 of 11,270; theirs 6,516 of 12,311 — unchanged to the column). `corpus-query diff` run 13 → 14:
NewModel 2, LostModel 0, Storeys 11, Views 1, Placement 1, Composition 73 (yardstick 1 better / 0 worse /
13 same), SameCounts 208.

**Every storey mover was looked at**, not counted — the per-sheet rows of both runs in
`analysis.IntakeSheet` (`Storeys`, `DxfFiles`, `Placed`, the reader's counts) say for each sheet what
changed and whether its NAME changed (the reader, step 68) or only its storey (the composer, step 69):

| set | what run 14 did | why | verdict |
|---|---|---|---|
| 30988-01 | L1 L2 L3 → L1 L2; every block's MAIN plan on L1 | step 68 now reads its BLDG 5/11/12/22/23/24 tags; the word chain ranked each building from 1 at its own bottom, block 22 (GROUND on an untagged sheet) said MAIN 1 against block 5's MAIN 2, and the row stood | **regression → rule 1** |
| 40117-01 | 5 → 6 storeys: 2-ROOF under ROOF | its only building is BLDG 2; the first cut let one tagged roof keep its name | **regression → rule 2** |
| 30919-01, 31004-01, 30925-01, 30816-02, 30878-02 | L4 / L17 / L21 / L3 / L3, L6 → ROOF | `StripSheetNumber` cut a name at LEVEL only when LEVEL was not its first word; step 68 strips the number first, so "LEVEL 4 - ROOF DECK PLAN" fell through to ROOF | **regression → rule 3** |
| 70062-01 | A-ROOF + B-ROOF → one ROOF | two lettered buildings on a plan-named ladder share the roof | by design (§79) |
| 30912-01 | 40 → 47 storeys, 42 → 25 placed | step 68 reads its REAL titles for the first time ("LEVEL -4 PLAN - CONCRETE OUTLINE" where run 13 had a notes paragraph): LEVEL -5 … -1 are storeys now, the CONCRETE OUTLINE sheets are the placed ones and the REINFORCING ones refused (§50's rule), "LEVEL 5 - 7" came out "LEVEL 5 7" so the range is one storey | better; two title-reader defects named below |
| 30926-01 | 9 → 11 storeys | real titles now ("LEVEL 2 PLAN" for "ALL SLAB SLOPES AND ELEVATIONS LEVEL 2 PLAN"); "LEVEL 5 LEVEL 14 - PLAN" with the dash displaced reads L5 and L14, not the range | better; a title-reader defect |
| 01589-01 | NewModel: 1 storey, 325 "columns" | the rotated revision strip's words became the title ("PERMIT PERMIT PERMIT BUILDING BUILDING BUILDING ISSUED … PLAN RESIDENCE ROOF …") and every plan went to ROOF | **a false model** — the title reader took a revision strip |
| 01375-01 | NewModel: 3 storeys, 0 of 4 placed | step 68: P1 / P1 / L1 / L2 (§78) | as recorded |
| 01379-01 | 121 → 178 sheets placed; columns 4,396 → 3,374 | its OVERALL PLANs read twice the lines (1,865 → 3,621 on L2) and are placed on every storey, 3 of 7 X and 13 of 13 Y axes by name; the part plans stand over them. **Rendered:** two towers consistent L22–L47; on L23, L24, L26, L33, L41 a "Plan B West Tower" view sits offset to the south-west (set by 2 of 7 X / 9 of 13 Y axes) — a placement fault, not shown to be new | better; one fault named |

**Step 70 — three rules (`584b70c2`):**

1. *The set's floor words have ONE order, whichever building's title states each step of it*
   (`DrawingVocabulary.WithFloorWordsRankedBy`, one map instead of one chain per building). Two bottoms, a
   cycle, a word shown over itself, two stories about one word still leave the row standing — those are
   contradictions; a chain that is part of another is not. The second audit's B4 test now asserts the
   joined chain; `NumberedBlocksNamingTheirFloorsByWordsHaveOneOrder` is 30988's shape.
2. *Storeys are per building only where the model names them so*: the per-building roof (step 61) applies
   when the ladder carries a building in a storey name (the elevations' A-L27, B-L40); on a plan-named
   ladder every building's roof is the shared ROOF — the composer's own test (`storeysByBuilding`), now in
   `StoreysFromPlans` too, replacing §79's "more than one" count. B6's fixture moved onto a
   building-named chain; 40117's one building has one roof.
3. *A title that starts with its level word keeps its level* (`StripSheetNumber`: `>= 0`, not `> 0`).
   `ATitleThatStartsWithItsLevelWordKeepsItsLevelBesideTheRoofWord`.

Fast suite 1,358; six-set gate byte-identical (a read, 2 m 13 s). All three are composer-side, so run 15
is a `--recompose` of run 14's read.

**Title-reader defects from step 68, recorded for the audit and the next brief, no code tonight:** the
title carries the neighbouring fields ("DRAWING NO S2.02.1", a stray "1", "PROJ. # 30878-02 DRAWING
NUMBER" on every 30878-02 title); words are joined in an order that displaces a dash ("LEVEL 5 LEVEL 14 -
PLAN"); a rotated revision strip is taken as the title (01589); a project name replaces a title ("GALLERIA
PART PLANS" → "1200 STEWART" on 01379 S212.9 and S402). And a landscape LOADING PLAN names storeys
(30878-02's S1.12 "LEVEL 3, 6 & ROOF LANDSCAPE LOADING PLANS" puts L3 and L6 on a two-storey building) —
LOADING PLAN belongs in `dxf.non-structural-sheet-patterns`.

WHAT THIS DOES NOT: fix any of the title-reader defects; place 01375's plans; the offset part plans on
01379; a set whose buildings genuinely order the same words differently (the row stands, stated in the
test).

## 81. Run 15 and step 71, 2026-09-15 early: a set no rule touched moved — the vocabulary was another set's

**Run 15** (a `--recompose` of run 14's read on `584b70c2`, 12 workers, 23:41 → 00:03, **22 min**;
`ledger-sets-2026-09-14-run15-step70.csv`; DB run `54c06888`): 253 of 296, 58% / 53% unchanged.
`corpus-query diff` run 14 → 15: Storeys 9, Composition 4, SameCounts 283. Eight of the nine are the
sets step 70 was written for, back on their storeys — 30988 L1 L2 L3, 40117 one roof, 30919 L4, 31004
L17, 30925 L21, 30816-02 L3, 30878-02 L3 and L6, 31098 L20 — and the ninth is **30992-01**, which no
rule of step 70 touches: MAIN FLOOR PLAN, UPPER FLOOR PLAN, ROOF PLAN, FOUNDATION PLAN, SITE PLAN —
nothing to rank, no tag, no LEVEL — went from L1 L2 ROOF to **L2 ROOF, its MAIN plan on no storey**.
The unit ladder for its five names is L1 L2 ROOF.

**The cause is shared state, not a rule.** `PlanSheetNaming.Vocabulary` is a process-wide static.
`DxfToEtabsService.Run` sets it to THIS set's ranked floor words for the composition's duration (step
60, restored on dispose) — and the corpus analyzer composes twelve sets at once in one process. 30992
read MAIN with whichever set was composing beside it (a set whose chain says MAIN = 2). Which set
that is depends on scheduling, so every word-named set's storeys in runs 8–15 carried this noise —
the "same set, different storeys, no rule" class that CLAUDE.md names for tests ("a test that passes
alone and fails in the suite is shared state") holds for sets in the analyzer too.

**Step 71 (`7975b354`):** the static is an `AsyncLocal` — the same static to every caller on one
execution flow (a set's read and build; a test and the code it calls) and invisible to the flows
beside it. No call site changed. `TheVocabularyInForceIsTheCallersTests`: two flows set different
words, meet at a barrier, and each reads its own through `Parse`; **proved by breaking it** — with a
plain static it fails every time ("Expected MAIN=1;UPPER=2, Actual GROUND=1;MAIN=2;UPPER=3"). Fast
suite 1,359; six-set gate byte-identical (a read, 2 m 17 s). Run 16 (`--recompose` on `7975b354`)
measured how many sets were reading a neighbour's words: **run 16** (00:11 → 00:33, 22 min;
`ledger-sets-2026-09-15-run16-step71.csv`; DB run `04679a26`) — `corpus-query diff` run 15 → 16:
**Storeys 1, SameCounts 295**; 30992-01 back to L1 L2 ROOF, 31 columns, 77 walls. One set in that pair
of runs; from run 16 the composition is deterministic.

**Also measured on run 15:** `ledger-sets.partial.csv` holds 279 rows against the sorted ledger's 296
— the 17 "stick file of another job" sets never append (they are decided before the per-set loop).
For the audit's question H; no code tonight.

WHAT THIS DOES NOT: a set whose own composition runs on more than one thread (none does); the
reader's `PdfOnlyBuild` setting of the office vocabulary, which is the same value for every set and
was never the leak; the results of runs before 16 for word-named sets, which stand as measured but
carry the noise.

## 82. Step 72, 2026-09-15 morning: the audit of steps 63–71 answered — eleven findings, eight fixed, one measured next, two stated

Codex's `CODEX-PDF-INTAKE-STEPS-63-69-AUDIT-RESPONSE.md` (14.5 KB, eleven source findings, no build, no
drawings) read against the code, each with a fixture (`8805acd1`):

| # | finding | what was done |
|---|---|---|
| 2 High | the gate's read cache never hashed `VectorPageReader.cs` at the Core root — a page-walker change would recompose a stale read and pass | the hash is **every Core source but a six-file exclude list**, and `NoIncludedSourceReferencesAnExcludedOne` proves in the real repository that no hashed file references an excluded type (proved by breaking: excluding `PlanSheetNaming.cs` names four files). Composer-only edits read the six again — ~2 min — the price of a cache that cannot lie. Cache version 2 |
| 3 High | a recovery run deleted the killed run's partial ledger before writing a row of its own | `SetAsidePartialLedger` keeps it under its last-write time; the 17 "another job's file" rows append too (run 15's partial held 279 of 296) |
| 4 High | stood-down dimension strings counted as unfilled pairs, so twenty of them beside one filled 140 mm wall made a concrete sheet "wood" and the wall a partition | they count for nothing in the wood-plan decision or its partitions |
| 5 High | "S2.3-LEVEL 2" read building 3 from its own sheet number (the dot passed the look-behind) | the tag is read from the number-stripped name, as the storey already was |
| 6 High | a building-prefixed parkade storey (1-P1, A-P2) never matched the anchored parkade pattern; a tagged parkade plan on a building-named ladder went nowhere | matched with its prefix off (`ModelYardstick.Stripped`) at both sites |
| 7 High | a tagged roof over an UNTAGGED level plan vanished on a plan-named ladder (the shared branch sat behind "has this building a level plan") | the shared ROOF no longer needs the building's own level plan |
| 8 Med | a tagged ELEVATOR ROOF on a plan-named ladder is the roof's storey, not a storey above it | **stated, not changed**: an untagged elevator roof plan is the roof's storey today; the overrun as its own storey is a rule for the sets that draw one, measured before it is written |
| 9 Med | a cycle standing apart from the walked chain (MAIN ⇄ UPPER beside GROUND → 4) escaped the check and re-ranked GROUND | every word with a story of its own must be on the chain, else the row stands |
| 10 Med | the wood rule's two-thirds share was untested at its line | half (no) and two thirds exactly (yes) |
| 11 Med | the vocabulary test's barrier could time out and pass a plain static | `SignalAndWait`'s result is asserted |
| 1 High | `Math.Min(options.MaxWallThickness, 18.0)` at the open-face-pair gate is a literal 18 **inches** never converted by `InUnitOf`, so in every millimetre set that branch's ceiling is 18 mm and it never yields a wall | **real, older than step 63, and it changes corpus numbers** — its own measured step (§83), not this one |

Also this step: the wood rule's 8 in is the row `dxf.pdf.unfilled-wall-min-thickness-mm` (migration 091, applied
this morning): `PdfIntakeOptions` reads it (18 setting keys), `WallTypeTagging` takes it, the triage ratchet
49 → 48. Migration 091's INSTRUMENTATION pattern refuses 31202's two SEISMIC INSTRUMENTATION sheets: their
nine named axes leave GRIDS (46 → 37), every member identical — looked at in the report diff, re-banked.

Fast suite 1,367; six-set gate byte-identical after the re-bank. The wood-rule and tag changes are
reader-side: run 17 is a full read.

WHAT THIS DOES NOT: finding 1; the elevator-roof storey on a shared ladder (stated); question K of the brief
(the flow-local vocabulary's setters) — Codex read the setters as out of range and made no finding.

## 83. Steps 73–76 and runs 17, 18, 2026-09-15 midday: the title block's fields, the plate instrument, a dead branch measured and refused, a level list's ranges

Four steps and two runs in three hours, on the road WP6a orders (plan Rev 4: the engineers' own definition of
usable — verticals and a plate on every storey, one model per building):

**Step 73** (`011117c2`, Codex from `CODEX-PDF-INTAKE-TITLE-BLOCK-FIELDS.md`, the first brief under the
one-defect-per-prompt rule): five title-block rules from run 14's five defects — every office label ends the
field above it (DRAWING NO, PROJ. #, JOB TITLE …), a neighbouring labelled column bounds a field's width, a lone
digit at the column's right edge is a revision mark, a line is its tallest word's baseline and a short glyph
joins the nearest one (the displaced dash), a block whose labels stack along X is a rotated strip read with the
axes swapped. Re-banking exposed that 31065's LEVEL 2 had been EMPTY in every baseline since the first — two
columns where the storey has 101 and 90 walls — because its dash sat 3.4 pt below its line and the title read
as no level; rendered, banked.

**Step 74** (`967564e7`): the instrument before the work (rule 9). `PlateCoverage` classes every storey of a
built set — Plate / NoSheetPlaced / NoRingRead / RingsReadNoPlate — from the .e2k and the sheet ledger, and
`takeoff corpus-query plates` sums the corpus. Run 18's 2,660 storeys: **plate 41%, no sheet placed 27%
(placement), placed but no ring read 17% (reading), rings read but no plate 19% (composer)**. The composer class
led straight to a fault: thirty-three messages divided areas by 144 as if every area were square inches, so
every PDF-route report stated ring areas 645 times too small ("72,188 sq ft … too small for a floor plate" was a
112 sq ft stair ring). `PlanClassificationOptions.UnitInInches` + `SqFt` — the words were wrong, the rules were
right. The read cache's reference gate caught `PlateCoverage` referencing the analyzer and it joined the
exclude list: the gate working.

**Step 75** (`7cf9969f`, the audit's finding 1): the open-face-pair branch's ceiling was a literal 18.0 — inches,
never converted — so on every millimetre set the branch was dead. Measured before choosing: alive on the six,
five byte-identical and 31170-arch +2 walls at hatching; alive on the corpus (a recompose of run 17's read,
`ledger-sets-2026-09-15-exp-open-face-pairs.csv`), +32 walls on 9 of 296 sets, no yardstick moved. The rule:
`MaxOpenFacePairThickness` is converted by `InUnitOf`, and `PairOpenFaces` says which route this is — true for
a Revit DXF (31065's exterior wall arrives as nineteen open chains), false on the PDF route, whose reader paired
every face it could with the fill in hand.

**Step 76** (`f55508c1`): the largest class without a plate was "no sheet placed" (787 storeys), and its top set
30884-01 (35 storeys) titles its typical floors "LEVEL 13 & 14 - 27 PLAN": the list read "13 & 14" and dropped
the range after the "&". A list is read first and each item may be a range (`RangeInList`).

**Runs 17 and 18**: run 17 (53 min, a full read on `8805acd1`) was read while migration 092 (LOADING PLAN,
LOADING DIAGRAM … are not structural plans; 50 corpus sheets carry LOADING) landed mid-run, so run 18 (30 min,
a recompose of run 17's read with 092 throughout) is 092's honest measurement: −260 columns / −229 walls that
had doubled their level plans, no storey and no model lost; 31057's only plan is a SITE PLAN (now no members);
31183's real ZONE A/B plans do not place (a placement class, item 3). Run 19 (a full read on `f55508c1`) measures
73–76.

## 84. Step 77, 2026-09-15 afternoon: a line that names another sheet is a callout, not a heading

The reading class (placed, no ring read) taken set by set, largest first: 31087-01 (King Rise West, 53 storeys,
49 of 61 without a plate). Its LEVEL 2 plan read 13 walls, 8 columns and no ring where LEVEL 1 beside it reads
55, 58 and one; `pdf-inventory` said why — **4,813 paths discarded as FurnitureRegion** (782 on LEVEL 1), and
`sched-border` named the region: `furniture: DETAIL 29 / S1.03  x 1316..2863  y 924..2231`, 1547 × 1307 pt, a
callout the plan writes six times at its slab steps, the slab edge above one of them taken as a notes box's top
rule and the podium plan under it swallowed (just inside the half-sheet cap). Rendered: the blue box over the
whole right-hand podium.

The rule (`5b02abe8`): a title run that names ANOTHER sheet (`SheetTitleReader.IsSheetNumberToken`) is a callout
to that sheet, never a heading; a run naming THIS sheet is a detail drawn here and keeps its box. After it:
LEVEL 2 41 walls / 62 columns / 2 rings, FurnitureRegion 744. Six sets: 31138 gained 5 columns and 9 walls on
each of L1 and L2 ("SEE DETAIL 15 / S1.06" and "DETAIL 1 / S1.05" boxes on its LEVEL 1 plan; rendered, banked);
the other five byte-identical. Test `ACalloutToAnotherSheetIsNotATitledBoxAndADetailOnThisSheetIs`, proved by
breaking. Fast suite 1,377.

WHAT IT DOES NOT: a plan note ending in a heading word with no sheet number (31138 p22 "BUILT-UP PER DETAIL",
173 × 608 pt) — the same class, the half-sheet cap still its only guard; the geometric statement of the class
("a heading is at its box's top-left; a callout floats on the plan") was not written because the grid crosses
the schedules on 31087's own sheets, so "furniture is never on the grid" is false here.

## 85. Step 78, 2026-09-15 afternoon: a floor is the cells its structure stands in, united — PlanarRings wired; and run 19's title regression

**The class, on 31087 (King Rise, 53 storeys, 49 without a plate).** `dxf-inspect --faces` (new: the planar
faces of a page's lines, the united plates, and the longest open chains with the member beside each loose
end) on LEVEL 6–8 said, in one screen: the north edge is eleven pieces of 29.5 ft on one line with a
**36-inch gap at every 12 × 30 column** (the drafter stops the edge a foot short of the column each side;
the bridge is six inches); `pdf-at` at the joint between the two blocks found the corridor's edge, **8.6 m
at 9 pt along grid 5**, kept apart for the tendon reader by step 53 (`StrokeOnGrid`); and the balconies are
boxes AGAINST the outline sharing its edge, which the chain walk spends on whichever ring it closes first —
the shape §27 named and stopped at ("a piece of linework must be allowed to serve a small loop AND the
floor's edge, or the outer boundary must be found by something other than chaining").

**Step 78 (`dafad991`), three rules and the §71 prototype wired.** The exact-join walk (step 24) and the
bridged walk of long pieces (step 27) stand verbatim. Where they close no floor and nothing drew one:
(a) the heavy strokes along grid axes are offered as lines; (b) two ends on one line, facing, closer than
the corner-carry limit (48 in) are one edge (`BridgesInLine`); (c) the long pieces, those bridges and those
strokes are arranged as a planar graph (`PlanarRings`, `048912f1`) and **the cells holding a column or a
wall midpoint are united** (`RecoverSurfaces`) — a balcony's cell holds nothing and stays out, a slab step
across the floor divides it into cells that unite. The unions are gated as the walk's rings are (floor
size, structure standing in it, outermost — now by MOST VERTICES inside, since a centroid lies outside an
L-shaped floor) and defer to a drawn floor-sized closed path (the architect's outline). A refused
arrangement is reported on the sheet (`SlabEdgeArrangementRefused`). LEVEL 6–8 and LEVEL 10–50 close —
rendered: the whole floor, balconies as notches, both blocks joined.

**Six sets.** Plates on ~110 storeys that had none — 31138 L7–L21, 31130 L4–L20, 31065 L8–L18, 31202
L5–L12, 31168 C-L3 / C-L4 / L1 / A-L1; members unchanged except 31138 L1 and L22, where
`ModelDoubleHeightMembersOnBothFloors` (the engineers' "a column stands on a slab") stops extending the
L2 columns down through a storey that now has its own plate — proven by a differential (the same DXFs with
L1's plates stripped give the old counts, 3 minutes), after 40 minutes of reading the composer had not.
Two regressions found by the gate and fixed: (1) a union bounded by curbs and the walls' INNER faces took a
parkade's plate from the walls' outer edge (31138 P1 18,374 → 14,520; 31168 P1 77,030 → 2,902) — **the floor
is the walls' outer edge where they close** (the engineers' rule, 25 Aug), a slab ring inside that ring is
its interior (`StructuralPlanClassifier`, before the fallback); (2) the architect's set lost its 32,076 sq ft
drawn outline to unions on six storeys — the arrangement is skipped where a drawn floor stands, and the
strokes and in-line bridges go to the arrangement only, never to the walk.

**Cost.** The arrangement is quadratic in its lines: the architect's A003 (42,501 lines) took 565 s arranged
whole and bridged 25,504 hatch dashes in line with each other; as long pieces only, 91 s; skipped where the
outline closes. The gate reads the six in 3 min uncontended (13 min while run 19 hogged the CPU — the
reason a corpus run never again runs during a development session).

**Run 19 (steps 73–76; banked `ledger-sets-2026-09-15-run19-step76.csv`) — a regression the six never
showed.** 253 → 248 models, 2,660 → 2,524 storeys, 85 sets' storeys moved (30941 34 → 13, 30990 25 → 15,
30816 5 → 2). The DB's per-page rows said why in one query: 30941 p16 read "PLAN RAFT FOUNDATION LEVEL"
on every run until 19 and "SSI PM" on 19. Step 73's `ReadingTokens` dropped every word drawn UP the page
(to keep a rotated stamp out of the fields), and KOR's own upright title block writes the SHEET TITLE up
the page. The title reader keeps them (`dropUpright: false`); the field reader drops them as Codex meant.

**OPEN (the next steps, in order):** a union whose boundary follows a leader or a section line is the
wrong shape — 31130's right tower comes out a slanted hexagon over its columns (a plate where none was,
not the drawn shape): cell selection must ask more than "holds a column"; fragment unions on parkades
with no wall ring (31168 P2/P3, ~900 sq ft); 31087 LEVEL 4 (p28) still reads no ring; the rotated title's
word order ("PLAN RAFT FOUNDATION LEVEL" for LEVEL B4 …) loses B4.

## 86. Step 79, 2026-09-15 evening: a line ending at an arrowhead is a tendon or a section cut, not a slab edge — and the PlanarRings sweep

**The class, on 31130 (two towers, L3–L12 typical).** Step 78's union gave the east tower a slanted hexagon for
a plate. `dxf-render` of the page's SLABEDG and BEAM layers showed the union's boundary following long
diagonals across the floor; `pdf-overlay --crop` at the south-west corner named them: the plan is a
"CONCRETE OUTLINE & POST TENSION" plan and the diagonals are the **tendons**, each ending in an anchor drawn
as a filled arrowhead ("378 KIPS", "486 KIPS", "MID" beside them). The angled edge that remains at that corner
is the slab edge itself, labelled "8" MIN. AT SLAB EDGE" — looked at before it was believed.

**The rule (step 79).** The column reader already refuses a filled shape of three points as a symbol's
triangle (step 58, `PathReason.FilledTriangle`); those are now recorded (`ExtractedGeometry.Arrowheads`:
centre and size). A line with an end within an arrowhead's own size of its centre is a tendon, a section cut
or a leader, and never an edge candidate — and because a tendon or a dash-dot cut is drawn in PIECES with the
arrowhead ending only the last (31130's: 110 dashes under a metre, 26 of one to five, 17 longer), the pieces
in line with such a line within the in-line reach (the same `BridgesInLine` pairing the edge is bridged by)
form a run that is dropped whole, before the two-metre gate can hide its last dash. On 31130 p35 the plate is
the drawn outline: 26 vertices, the angled south-west edge, no tendon. Test
`ALineEndingAtAnArrowheadIsASectionCutNotAnEdge`, proved by breaking.

**The sweep (`becd7f86`).** dotnet-trace on 31101-01 (a wood set, 27 pages, 688 s in run 19):
`PdfOnlyBuild` 330 s single-worker, of which `SlabEdgesFromLoops` 234 — `PlanarRings.Build` 179
(`Arrange` 146: `BoxesMeet` 78, `Conflicts` 55) and `BridgeChains` 49 — `Compose` 24.5. Both pair loops in
`Arrange` tested every two lines' boxes (1.8 billion tests on the architect's A003); sorted by the left edge,
a line's partners are those whose left edge lies before its right edge plus the margin — the same pairs. A003
91 s → 11 s, 31101's S2.05 ~40 s → 3 s, the set 330 s → 180 s; the gate byte-identical.
`tools/Summarize-Speedscope.py` reads a speedscope profile so the next one is a two-line job. Also in the
profile: `StickFileCorpus.Census` at 1,013 s of thread time on a `--jobs` run that builds one set (~90 s of
wall clock per run, every run) — the next cost; and `RecoverSurfaces` refusing dense pages ("Face successor
is not a permutation": selected cells meeting at a vertex), which the reader reports and survives.

WHAT IT DOES NOT: an arrowhead the column reader read as something else (four points, an open outline, a
curve); a leader ending in a dot; the parkade fragment unions (31168 P2/P3); 31087 LEVEL 4.
