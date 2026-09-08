# Codex 21 of N — a footing outline is drawn only where nothing stands on it

## Goal

Place the 20 of 140 scheduled footings brief 20 misses, and stop counting a box that no label
names as that mark. Repo-only; the five stick files are local at
`%LOCALAPPDATA%\Temp\kor-drawings\stickfiles`.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## Evidence (brief 20, `takeoff pdf-inventory` per-mark rows; overlay crops on 31065 p15)

    sheet         read of placed   missed
    31130 p11         35 of 36     F4 2 of 13; and F1 2 of 1 (a box with no mark in it)
    31130 p12         34 of 39     F1 1, F2 3, F4 1
    31065 p14         26 of 31     F2 1, F3 1, F4 3
    31065 p15         14 of 23     F1 1, F2 5, F3 3

Looked at on p15, every miss had the same shape. **The drafter draws the footing only where
nothing stands on it.**

- Cut by the match line between the two halves of the plan: one full side (3,001 mm, 6 pieces)
  and two stubs (758 mm, 2 pieces each) pointing the same way. Four of the five F2 misses on p15.
- Under the perimeter wall: the side beneath the wall is absent, or a few short pieces — and those
  pieces sit at x = 77,208 and 77,220 mm, either side of a 12 mm bucket boundary, so neither
  bucket reaches `MinPieces`. The five misses along p15's east wall.

Diagnostic listing (scratch, exploration only) for the west F2 at (35,382, 41,256) mm:

    H at y=40974: x 34988..35746 span 758 pieces 2      ← top stub, from the match line
    V at x=35746: y 40974..43972 span 2997 pieces 6     ← the one full side, F2's 3,000
    V at x=34988: y 18195..55190 span 36995 pieces 1    ← the match line itself, solid

## The class, in one sentence

A footing whose outline is interrupted by what stands on it is placed from one full side of the
scheduled length and the stubs at its ends; the plan's own label, standing in the box or just
beneath it and nearer to it than to any other footing, names it; and nothing inside sheet
furniture is a footing or a placement.

## Changed

`Core/Intake/FootingOutlines.cs`:

1. **Sides cluster by sorted coordinate, not fixed buckets.** Pieces sort by the coordinate they
   share and a new side starts where the gap to the previous exceeds `CollinearMm`. A side at
   77,208 and 77,220 is one side.
2. **Second pass, after the closed boxes:** an unclaimed dashed chain (≥ `MinPieces`) whose span is
   a scheduled length within `SizeToleranceMm`, with a perpendicular chain starting within
   tolerance of EACH end, running the same way and no further than the box is deep, is that
   footing, extended from the full side toward the stubs. A side already in a closed box is not a
   candidate. One side and one stub is not a footing.
3. **A label names the footing it is nearest to** when it stands in the box or within half the
   footing's size of its edge — measured: inside on 31065 and 31138 (37 of 37), 254–461 mm beneath
   the outline beside the column on 31130 (67 of 68). `FootingOutline.LabelledOnThePlan`.
   `PdfPlanReader.ReadFootings` takes the scale so the labels (PDF points) land in the outlines'
   millimetres.
4. **Furniture is neither footing nor placement.** `FootingOutlines.Read` skips pieces inside sheet
   furniture; `FootingScheduleReader.CountPlacements` / `PlacementPositions` take an optional
   `SheetFurniture.Set` and skip words inside it. Callers without one keep the old count; the
   intake, the ledger, the overlay and the harness pass it.

`Core/SheetInventory.cs`: the footing row counts labelled footings of placed labels per mark and
lists boxes no label names apart ("no label names 1 F4-sized box(es)").

`TakeoffCli` `pdf-overlay`: a footing no label names is magenta and listed with its position; so
is every placed label no footing answers.

Tests: `AFootingIsADashedRectangleTheScheduleSizesTests` — a full side and two one-piece stubs
place the footing its scheduled depth from the side; one stub, or stubs pointing apart, do not;
pieces 10 mm either side of a line are one side; a label in or 400 mm beneath the box names it, one
elsewhere or of another mark does not. `FiveStickFilesTests` re-banked: 31130 p11 36 of 36,
31065 p14 30 read of 29 placed.

## Measured after (labelled footings of labels placed)

    sheet         after         before      what remains
    31130 p11     36 of 36      35 of 36    —  (the legend's 4 ft square is out)
    31130 p12     37 of 39      34 of 39    2 rotated footings on the angled wing
    31138 p9      11 of 11      11 of 11    —
    31065 p14     29 of 29      26 of 31    the note's two "F4" mentions are out; one 4.5 m dashed
                                            box no label names — the core footing — listed apart
    31065 p15     22 of 23      14 of 23    one F2 with one side and one stub drawn
    total        135 of 138    120 of 140

DXF census, step 9 against step 8: FOOTING 35→36 (p11), 34→37 (p12), 26→30 (p14), 14→22 (p15);
BEAM falls by the pieces consumed; the other 9 of 13 DXFs identical; COLUMN, WALL and SLAB
identical on 13 of 13. The 31130 ledger total unchanged at 566,264.

One more, found by the flag: 31130 p15 (L0 WEST, a floor plan that repeats the FOUNDATION SCHEDULE
and places no footing mark) reads one 4 ft dashed square round a PCX column as an F1. Unlabelled on
a sheet that places nothing, the ledger files it as unaccounted, not read.

## Two findings for other steps

- `takeoff footings` and the rebar takeoff still call `CountPlacements` without furniture, so on
  31065 p14 they count 13 F4 where the plan places 11 — 74 cu.yd that is not there. Step 9 (every
  tool reads the record) closes it; until then it is a known over-count.
- Rotated footings (31130 p12, the angled wing) are outside every axis-aligned rule here, as
  rotated marks are outside the column self-check. One rule for rotated geometry, later.
