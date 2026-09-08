# Codex 22 of N — a grid axis is a named line

## Goal

Carry the grid as data. The intake already finds every grid bubble with an axis through it
(670 / 657 / 743 / 834 / 710 on the five sets, 31130 p11 19 of 19) and uses the axes only to
DISCARD the grid lines: the ledger filed "grid bubbles with an axis", "grid axes" and "words: grid
axis names" as discarded, and 18–32 thousand paths per set as `GridAxis`, discarded. The names and
positions reached no consumer. Downstream, `DxfToEtabsService` puts a drawing on the engineer's
model by matching grid lines on a layer whose name contains GRID (`GridAlignment.Solve`,
spacings) — a PDF-derived DXF had no such layer, so it could never be aligned.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## The class, in one sentence

A grid axis is a named line: its name is the bubble's label, its position is the rule through the
bubble, the two ends of one line are one axis, and the same name on two sheets is the same line.

## Changed

- `GridBubbles.Axis(Name, Vertical, At, Bubbles, LabelsDisagree)` and `Grid.Axes`: bubbles on an
  axis sorted by the coordinate they share, one axis per run within `AxisTolerancePts`; the name
  is the label, both labels joined by "|" when the ends disagree.
- `ExtractedGeometry.GridAxes` (`GridAxis(Name, Vertical, AtMm)`), filled by `PdfPlanReader.Read`
  and by `DrawingIntake.ReadSheet` from the same bubbles, scaled to the geometry's millimetres.
- `DxfExporter`: a GRID layer, one LINE per axis across the drawn extent (padded 5 %, at least a
  metre) and the name as TEXT at both ends, recentred with everything else. Never mapped to a KOR
  layer; `GridAlignment.LooksLikeAGridLayer` already reads it.
- `PathFate.DispositionOf(GridAxis)` = Read: the pieces the drafter drew are consumed into named
  axes; the path has no object index because many pieces make one axis.
- Ledger: "grid bubbles with an axis" and "grid axes, named" are Read and list the names by
  direction, any disagreeing ends, and any name used twice in one direction — a second view on the
  sheet, the per-view split's signal, listed, not merged. "words: grid axis names" is Read. The
  JSON ledger carries `GridAxes` (Name, Dir X|Y, AtMm) per page.
- `pdf-overlay` draws the axes cyan across the page and prints the names.
- Tests: `SheetFurnitureIsNotStructureTests.TheTwoEndsOfAGridLineAreOneNamedAxis`;
  `AGridAxisIsANamedLineTests` (GRID layer with a LINE and two TEXTs per axis, recentred with the
  slab; no layer without axes; the fate is Read); `DrawingIntakeTests` asserts the record's axis;
  `FiveStickFilesTests.NamedGridAxesOnTheSchedulePageAreTheBankedCount`.

## Measured (`pdf-overlay`, the five schedule pages)

    sheet         axes   X                                   Y
    31130 p11       19   1,3,5,7,8,9,10,11,13,15,16          Q,P,L,G,F,E,B,A
    31168 p11       26   1–19                                R,P,N,M,L,K,J
    31138 p9        15   1–8                                 G,F,E,D,C,B,A
    31065 p14       17   1–8                                 F,A,G,F,E,D,C,B,A   (F and A twice)
    31202 p17       17   1,2,3,4,10,13,14                    4,1,N,M,L,I,F,C.2,B,A

31065 p14's second F (y 4,324 mm) and A (8,401 mm) sit below the plan's own F (24,287) and A
(54,527): a second view at the foot of the sheet carries grid bubbles too. 31202 p17 puts the
numerals 1 and 4 on horizontal axes — what the sheet draws, kept as drawn.

## Measured after

- Axis positions against the bubble labels' own positions (fitz, independent of PdfPig), 31130
  p11: 1 at 21,125 mm vs 21,120; 3 at 25,392 vs 25,387; A at 68,647 vs 68,605; B at 63,466 vs
  63,423 — 4 of 4 within 45 mm.
- DXF census against step 9: a GRID layer on 13 of 13 DXFs, three entities per axis (57 on 31130
  p11 = 19 axes); every other layer identical on 13 of 13.
- Ledger, totals unchanged on 5 of 5: paths GridAxis Discarded → Read 18,166 / 22,607 / 20,578 /
  26,970 / 32,470; axis-name words 670 / 658 / 742 / 795 / 694; read on 31130 8,735 → 27,571 and
  discarded down by the same. Unread and unaccounted did not move.
- `FiveStickFilesTests.NamedGridAxesOnTheSchedulePageAreTheBankedCount` banks 19 / 26 / 15 / 17 / 17.

## Two findings for other steps

- The named axes make alignment by NAME possible: `GridAlignment` matches by spacings today; the
  drawing's "3" and the model's GRID "3" are the same line. A later brief in `DxfToEtabsService`.
- A name used twice on one sheet is the per-view split's measurement; the split itself is the
  multi-view sheets' step (31168's S2.21.1 and up).
