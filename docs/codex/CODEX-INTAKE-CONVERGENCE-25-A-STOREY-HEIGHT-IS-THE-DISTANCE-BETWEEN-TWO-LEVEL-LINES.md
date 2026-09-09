# Codex 25 of N — a storey height is the distance between two level lines

## Goal

Read storey heights off the drawings. Until now they came from the reference model only
(`E2kDocument.ReadStories`, copied verbatim), and `docs/PdfIntake.md` §3 listed "storey heights
are taken from the reference e2k, not the drawings" as a gap. The drawings state them on every
wall elevation.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## Measured first (`takeoff elev-scan`, extended to print ladder gaps at the sheet's scale; `takeoff e2k-storeys`, new)

The five sets' level ladders sit on the wall elevation and section sheets late in each set (31130
p46–58, 31168 p32–39, 31138 p48–58, 31065 p52–73, 31202 p44–59). Absolute elevations are rare on
them; the storey height is the drawn distance between consecutive level lines, at the sheet's
scale, with a dimension string only where a storey is typical (7'-2", 7'-4").

    31168 p37  SHEAR WALL ELEVATIONS - BLDG B, 1/8" = 1'-0"      31168 reference .e2k
      L19 → L18 … L4 → L3     2,946 mm  (9'-8")                  LEVEL 12–26   116 in = 2,946 mm
      L3 → L2                 5,336 mm  (17'-6")                 LEVEL 3       210 in = 5,334 mm
      L2 → L1                 2,808 mm  (9'-2.6")                LEVEL 2     110.5 in = 2,807 mm
      L1 → P1                 8,652 mm                            (the ground storey + mezzanine)
      P1 → P2, P2 → P3        2,898 / 2,894 mm

    31130 p53  WEST TOWER SHEAR WALL ELEVATIONS, 1/8" = 1'-0"
      L1M → CONCRETE 2,731 · CONCRETE → L0/P1 4,125 · L0/P1 → P2 3,353 · P2 → P3 2,743 mm

Within 2 mm of the engineer's model on the three storeys the two name alike. "CONCRETE" is the
level reader taking "LEVEL 1 - CONCRETE" as a level named CONCRETE — a `ReadLevelLadder` finding,
recorded here, not fixed in this brief.

## The class, in one sentence

A storey height is the distance between two level lines on an elevation drawn to scale; a
schedule's level column is a table's pitch and is not one.

## Changed

- `Core/Intake/StoreyLadder.cs` (new): `Read(page, scaleNote)` → storeys top → bottom
  (`Storey(Level, LevelBelow, HeightMm, YPts)`) from `ScheduleGridReader.ReadLevelLadder` and
  `PlanGeometry.MetresPerPixel(scaleNote, 72)`; `MinRows` 3; `Typical()` = the most repeated height
  to 5 mm.
- `SheetRecord.Storeys`, filled by `DrawingIntake.ReadSheet` on sheets typed section/elevation
  only.
- Ledger: "storey heights from the level ladder at the sheet's scale" — count, typical, the first
  six pairs.
- `takeoff elev-scan` prints the ladder gaps at the sheet's scale; `takeoff e2k-storeys <e2k>`
  prints a model's storeys with heights — the two sides of the comparison above.
- Tests: `AStoreyHeightIsTheDistanceBetweenLevelLinesTests` (three lines at 1:96 give two storeys
  of the drawn height in order; no scale or two lines give nothing; the typical is the most
  repeated); `FiveStickFilesTests.StoreyHeightsOnAnElevationSheetAreTheBankedOnes` banks 31168
  p37 (21 storeys, L3 → L2 5,336, typical 2,946) and 31130 p53 (4 storeys, P2 → P3 2,743), ±5 mm.

## Measured after (the ledger, five sets)

Storeys read: 31130 13, 31168 48, 31138 **0**, 31065 48 (typical 2,845 mm), 31202 40 (typical
2,946 mm). Totals unchanged on 5 of 5. 31138's wall elevation sheets state SCALE = AS NOTED and put
the scale as a caption under each view; a sheet that says AS NOTED has no sheet scale, so nothing
is read there — the per-view caption is the next rule.

## What remains

- An AS NOTED sheet: the scale is the caption under the view (`SheetScaleReader.ScaleNotesAnywhere`
  already finds them with positions); the ladder belongs to the view whose caption is nearest.
- A set's storey table is the union of its elevation sheets' ladders, reconciled by level name;
  this brief reads per sheet. The set-level table, and handing it to `DxfToEtabsService` in place
  of, or as a check on, the reference model's storeys, is the next brief.
- The level reader's "LEVEL 1 - CONCRETE" → CONCRETE.
- Two buildings' ladders on one sheet (31168 p37 carries B-LEVEL and LEVEL names): the busiest
  column wins today.
