# Codex 28 of N — a view's scale is the caption under it

## Goal

Read storeys off a sheet whose title block says AS NOTED. 31138's wall elevations read nothing in
brief 25 because the storey reader took the sheet's scale and the sheet states none; every view on
those sheets carries its own scale caption.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## Measured first (`takeoff elev-scan`, extended to print caption-shaped runs)

    31138 p53   "S3.11 1/8" = 1'-0""  at fx 0.43, 0.65  fy 0.04   — ladders at fx 0.50, 0.72; rows fy 0.12–0.89
    31138 p57   "S5.02 1/8" = 1'-0""  five: fx 0.32, 0.66, 0.81 at fy 0.52; 0.33, 0.67 at fy 0.11
    31130 p53   "S3.08 1/8" = 1'-0""  three at fy 0.17, and the title block's own at fx 0.92

No caption carries the word SCALE; `SheetScaleReader.ScaleNotesAnywhere` found none of them.

## The class, in one sentence

A view's scale is the ratio in the caption under it; a ladder belongs to the caption nearest below
it, and when every caption on the sheet states one ratio, that is the sheet's drawing scale.

## Changed

- `Core/Intake/ViewCaptions.cs` (new): `Read(page)` — captions outside the title block's fifth,
  the imperial form rejoined from "N/D"" "=" "1'-0"", the metric "1 : N" whole; `For(captions, x,
  bottom)` — one ratio when they all agree, else the caption below and nearest.
- `StoreyLadder.Read(page, scaleNote, captions)`; `DrawingIntake` and `SetStoreys` pass the sheet's
  captions.
- `ScheduleGridReader.ReadLevelLadder`: the level's value is the level-shaped token on the label's
  own baseline before the word wrapped under it (CONCRETE, MECH.).
- Tests: `AViewsScaleIsTheCaptionUnderItTests`; the level-reader case in
  `TheAuditsCounterexamplesTests`; `FiveStickFilesTests` banks 31138 p53.

## Measured after

31138 p53: 17 storeys, typical 2,995 mm (9'-10"), L22 → L21 3,910, L21 → L20 3,698; p57: 6
storeys, P1 → P2 2,946 mm. 31130 p53's ladder names are L1M, L1, L0/P1, P2, P3 (was CONCRETE for
L1). No reference model for 31138 is on the share under a name the tests know, so the numbers are
banked, not checked against a model.

## What remains

- Two ladders one above the other on one sheet take the same caption.
- A plan on an AS NOTED sheet still takes the requested denominator for its geometry.
