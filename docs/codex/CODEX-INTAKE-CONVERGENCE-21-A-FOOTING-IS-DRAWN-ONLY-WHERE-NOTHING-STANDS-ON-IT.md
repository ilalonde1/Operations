# Codex 21 of N — a footing outline is drawn only where nothing stands on it

## Goal

Place the 20 of 140 scheduled footings brief 20 misses, and stop counting a box that no mark
stands in as that mark. Repo-only; the five stick files are local at
`%LOCALAPPDATA%\Temp\kor-drawings\stickfiles`. Do not build or test; the verifier runs the
instruments.

## Evidence (brief 20, `takeoff pdf-inventory` per-mark rows; overlay crops on 31065 p15)

    sheet         read of placed   missed
    31130 p11         35 of 36     F4 2 of 13; and F1 2 of 1 (a box with no mark in it)
    31130 p12         34 of 39     F1 1, F2 3, F4 1
    31065 p14         26 of 31     F2 1, F3 1, F4 3
    31065 p15         14 of 23     F1 1, F2 5, F3 3

Looked at on p15, every miss has the same shape. **The drafter draws the footing only where
nothing stands on it.**

- Cut by the match line between the two halves of the plan: one full side (3,001 mm, 6 pieces)
  and two stubs (758 mm, 2 pieces each) pointing the same way. The label sits between the line and
  the full side. Four of the five F2 misses on p15.
- Under the perimeter wall: the side beneath the wall is absent, or a few short pieces — and those
  pieces sit at x = 77,208 and 77,220 mm, either side of a 12 mm bucket boundary, so neither
  bucket reaches `MinPieces`. The five misses along p15's east wall.

Diagnostic listing (scratch, exploration only) for the west F2 at (35,382, 41,256) mm:

    H at y=40974: x 34988..35746 span 758 pieces 2      ← top stub, from the match line
    V at x=35746: y 40974..43972 span 2997 pieces 6     ← the one full side, F2's 3,000
    V at x=34988: y 18195..55190 span 36995 pieces 1    ← the match line itself, solid

## The class, in one sentence

A footing whose outline is interrupted by what stands on it is placed from one full side of the
scheduled length and the stubs at its ends; the mark standing in the box says which footing it is.

## Change this, and nothing else

`Core/Intake/FootingOutlines.cs`:

1. **Sides cluster by sorted coordinate, not fixed buckets.** Sort the axis pieces by the
   coordinate they share and start a new side when the gap to the previous exceeds `CollinearMm`.
   A side at 77,208 and 77,220 is one side.
2. **Second pass, after the closed boxes:** for every unclaimed dashed chain (≥ `MinPieces`) whose
   span is a scheduled spread-footing length within `SizeToleranceMm`, look for a perpendicular
   piece or chain starting within `SizeToleranceMm` of EACH end and extending the same way (either
   ≥ 1 piece). If both exist, the footing is the scheduled square (or the scheduled L × W, either
   orientation, chosen by the full side's length) extended from the full side toward the stubs.
   Record the full side's and the stubs' pieces as the footing's. A chain already claimed by a
   closed box is not a candidate.
3. **A box carries whether a mark stands in it.** `Read` takes an optional list of
   `(string Mark, double X, double Y)` label positions (the `FootingScheduleReader` placements —
   expose their positions if `CountPlacements` only counts). `FootingOutline` gains
   `bool MarkStandsInIt`. A closed box with no label in it and a size shared by no other type keeps
   its size-matched mark; the ledger row says how many boxes have no mark in them.

`Core/SheetInventory.cs`: the footing row's note appends `, N without a mark in them` when N > 0.

`Core.Tests/Intake/AFootingIsADashedRectangleTheScheduleSizesTests.cs`: add (a) a full side plus
two one-piece stubs of a scheduled size is that footing, centred the scheduled length from the
side; (b) a full side with a stub at one end only is not; (c) pieces at 77,208 and 77,220 are one
side; (d) a label inside the box sets `MarkStandsInIt`.

`Core.Tests/FiveStickFilesTests.cs`: `FootingCount` moves up on the four short pages; the theory
comment says why (this brief). `FootingMarksPlaced` does not move.

## Do not

- Do not widen `SizeToleranceMm`, `DashGapMm` or lower `MinPieces` for full sides.
- Do not invent a footing from a single stub, a single side, or a label alone.
- Do not touch `GeometryFilterService`, `DxfExporter`, `PdfPlanReader` or the CLI.

## What the verifier will check

- Per-mark ledger rows on the five foundation plans: read rises toward placed; no mark's read
  exceeds its placed except where the note says a box has no mark in it.
- DXF census against `harness/step8-after`: FOOTING rises on the four pages, BEAM falls by the
  pieces consumed, COLUMN/WALL/SLAB identical on 13 of 13.
- Overlay crops on 31065 p15 at the match line and the east wall show orange boxes on the F2s and
  F1/F3.
- Unit tests, fast suite, `FiveStickFilesTests` re-banked with a sentence.
