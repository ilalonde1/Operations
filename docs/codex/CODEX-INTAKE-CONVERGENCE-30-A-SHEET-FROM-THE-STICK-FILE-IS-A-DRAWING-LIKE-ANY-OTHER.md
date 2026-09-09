# Codex 30 of N — a sheet from the stick file is a drawing like any other

## Goal

Make the PDF-derived DXFs reach the ETABS consumer: placed on storeys, on the model's grid.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## Measured first

`dxf-to-etabs` on the baseline's 31168 p11–p13 DXFs against the reference: no model (layer names).
On `--kor-layers`: a model, but the sheets carried no storey (the file name is where the reader
reads it) and the set-wide grid fit compared millimetres to inches and found nothing.

## The class, in one sentence

A sheet from the stick file is named as the office names a view, sits on the office's layers, and
is set on the model's grid by the names of its axes — one frame per sheet; and an axis placed on the
grid is a grid line for the rest of the set.

## Changed

- `Intake/SheetDxfName` (new); `pdf-takeoff` names each page's DXF as a view.
- `GridAlignment`: `NamedAxis`, `ReferenceGrid`, `NamedAxes`, `SolveByName`, `Carried`, `GridLines`.
- `DxfToEtabsService`: per-sheet frames by name, the second pass through placed sheets' axes,
  `ReadGridLines`, GRIDS written from the drawings when the reference has none.
- Tests: `ASheetSitsOnTheModelsGridByNameTests`, `ASheetFromTheStickFileIsNamedLikeAViewTests`,
  `TheStickFileBuildsAModelOnItsGridTests` (Slow, live).

## Measured after

31168 p11–p13: 3 of 3 sheets by name (19 of 19 X; Y within 0.9"); 182 columns, 65 walls; P3 → P2
70 columns, P2 → P1 112; extents identical on both storeys. Doc §23.

## What remains

- Parkade floor plates (no filled region; the perimeter is two face lines).
- The title-block reader's word order across lines ("FOUNDATIONS PLAN BLDG A - & B").
- Footings are SAFE's; the FOOTING layer feeds the SAFE window, not ETABS.
- Storey heights read off elevations (§16) are not yet written into a shell built without a reference.
