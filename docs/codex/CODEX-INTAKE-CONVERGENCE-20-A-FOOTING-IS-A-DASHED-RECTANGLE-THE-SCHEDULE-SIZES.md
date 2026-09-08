# Codex 20 of N — a footing is a dashed rectangle whose size the schedule declares

## Goal

Make the intake place footings. Before this it placed none: the foundation schedule was read
(marks, sizes, depths, a count of marks on the plan) but the drawn outlines went to the DXF as BEAM
lines or were dropped as too short, so the rebar takeoff priced footings from a count and the
ETABS/SAFE side had nothing to stand a column on.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## Measured first (fitz, three foundation plans, before any code)

Not one footing outline is a closed path. Every one is separate two-point strokes, one per dash.
Chained across the DXF side's own dash-join gap (`dxf.dash-join-gap` = 14 in = 355.6 mm), collinear
within 12 mm, three or more pieces to a side, and closed into boxes, the boxes match the schedule's
sizes to the millimetre.

## The class, in one sentence

A footing is a dashed rectangle whose size the foundation schedule declares, and its mark is the
schedule's.

## Changed

- `Core/Intake/FootingOutlines.cs` (new) — `Read(raw, types)` → footings + which raw-path index
  belongs to which footing. Constants: `DashGapMm` 355.6, `SizeToleranceMm` 60, `CollinearMm` 12,
  `MinPieces` 3. A side belongs to one footing.
- `PdfPlanReader.ReadFootings` runs before `GeometryFilterService.Classify`; the classifier records
  every consumed piece as `PathReason.BecameFooting` (Disposition.Read, object index = the footing)
  and lets it reach no other branch.
- `ExtractedGeometry.Footings` (`FootingOutline`: Mark, Outline, Centre, LengthMm, WidthMm,
  DepthMm); `DxfExporter` writes them closed on a FOOTING layer; `pdf-overlay` draws them orange;
  `pdf-takeoff` counts them; the ledger carries a row per sheet — footings read of marks placed,
  per mark.
- Tests: `AFootingIsADashedRectangleTheScheduleSizesTests` (a dashed square of a scheduled size is
  that footing; an unscheduled size is not; a solid side does not chain; the classifier records
  every dash as the footing's and emits nothing else from them);
  `FiveStickFilesTests.FootingsOnTheSchedulePageAreTheBankedCount` banks read and placed on the
  five schedule pages.

## Measured after (`takeoff pdf-inventory`, the ledger's footing row, read of placed)

    sheet         read of placed   by mark
    31130 p11         35 of 36     F1 2 of 1, F2 14 of 14, F3 8 of 8, F4 11 of 13
    31130 p12         34 of 39     F1 3 of 4, F2 17 of 20, F3 9 of 9, F4 5 of 6
    31138 p9          11 of 11     F1 7 of 7, F2 4 of 4
    31065 p14         26 of 31     F1 4 of 4, F2 12 of 13, F3 0 of 1, F4 10 of 13
    31065 p15         14 of 23     F1 4 of 5, F2 1 of 6, F3 3 of 6, F4 6 of 6
    31168, 31202       0 of 0      no spread footing scheduled (placeholder table; raft slab) — the ledger says so

120 of 140 across the five foundation plans. DXF census against step 3: a FOOTING layer appears on
5 of 13 DXFs (35, 34, 11, 26, 14 polylines); BEAM falls by the pieces consumed; COLUMN, WALL and
SLAB identical on 13 of 13. Pieces recorded BecameFooting: 996 (31130), 340 (31138), 1,327 (31065).
The 31130 ledger total is unchanged at 566,264 items.

## The 20 misses, looked at (overlay crops, 31065 p15)

Two shapes, one cause: **a footing outline is drawn only where nothing stands on it.**

- At the match line between the two halves of a plan, the footing is drawn from the line out: one
  full side (3,001 mm, 6 pieces) and two stubs (758 mm, 2 pieces each). Four of p15's five F2
  misses.
- Under the perimeter wall, the side beneath the wall is absent or a few short pieces — and those
  pieces fell either side of a 12 mm bucket boundary (x = 77,208 and 77,220 mm), so neither bucket
  reached three. The five misses along p15's east wall.
- The F1 "2 of 1" on 31130 p11 is the mirror fault: a dashed 4 ft square with no F1 beside it is
  counted as F1, because a box is not yet tied to the mark label standing in it.

Brief 21 is that rule.
