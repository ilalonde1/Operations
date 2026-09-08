# Codex 19 of N — the scale is accounted for on every sheet

## Goal

Stop the ledger reporting "scale: none read" on a sheet whose title block states one. Brief 18
had already moved `SheetScaleReader` from 79 to 214 of 294 pages, because "SCALE:" was itself a
double-drawn label; the remaining 80 were unread, not absent. The record carries the SCALE field
verbatim and the reader parses it when its own pass declines.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## The class, in one sentence

A sheet that states its scale has told the reader; a sheet that states "AS NOTED" has told the
truth about itself; only a sheet that states nothing has no scale.

## Changed

- `SheetRecord.ScaleStatement` — the title block's SCALE field, verbatim (`TitleBlockFields.Read`).
- `SheetScaleReader.RatioOf(string?)` — parses a statement to a ratio; repairs the one export
  fault seen, an "=" dropped between the two lengths (`1/8" 1'-0"`).
- `DrawingIntake.ReadSheet` — `scale ??= SheetScaleReader.RatioOf(scaleStatement)`.
- `SheetInventory` — three scale rows: ratio read / stated without a ratio / none stated.

## Measured after

    outcome                                          31130  31168  31138  31065  31202   all
    ratio read                                          46     37     42     45     44   214
    stated without a ratio (AS NOTED, As indicated)     12      1     17     25     15    70
    none stated (covers, 3D views)                       2      3      2      3      0    10

294 of 294 pages accounted for. Thirteen baseline DXFs byte-identical to step 3; fast suite and
harness 965 of 965. Commit 6e52021e.
