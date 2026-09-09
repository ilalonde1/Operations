# Codex 29 of N — a wall is what its clip lets through, and its doorways are its piers

## Goal

Close the wall gap against Revit on 31168 that step 5 recorded as 425 to 850.

Implemented by the verifier on 2026-09-08 (Codex out of usage); this is the record.

## Measured first

`takeoff pdf-vs-dxf --views` (new flag): one row per DXF view under its sheet, with the DXF's
walls by layer. The 850 summed every level a typical sheet stands for and a "for reinforcing plan"
copy of each; sheet to its own concrete-outline view, the DXF carries 521. 795 of 850 on
JBP_V-WALL, 26 on JBP_B_WALL.

`takeoff pdf-overlay --walls` (new flag): each wall the page read, and every path painted on it
in paint order with colour, fill and stroke. On p22 each core face is one 328" x 30" fill drawn
immediately after a clip of three rectangles (74", 118", 41") — no ink of their own. On p17 five
white rectangles 40"–55" wide are painted over walls after them.

## The class, in two sentences

A filled shape is visible only where the clip in force lets it through; a wall drawn right after
its clip, every piece of which lies on the wall, is those pieces. A paper-coloured fill painted
over a wall after it, across its thickness and a door wide, is a doorway, and the wall is the piers
on either side.

## Changed

- `VectorPageReader.GeomPath` / `RawSubpath`: `IsClipping`, `PathOrdinal`.
- `GeometryFilterService`: `ClipPiecesOn`, `DoorwaysOn`, `Piers`; paper fills and no-ink paths
  are fated after the walls (`Doorway`, `ClipOfWall`, both read) and this call's fates are kept in
  path order.
- `PathReason.Doorway`, `PathReason.ClipOfWall`; `ExtractedGeometry.Doorways`; ledger rows.
- `pdf-overlay` paints doorways yellow; `--walls` lists walls and what is painted on them.
- Tests: `AWallIsWhatItsClipLetsThroughTests`, `AWallIsThePiersBesideItsDoorwaysTests`; the fate
  table has a clip and a doorway case; `FiveStickFilesTests` rebanked (13, 30, 43, 39, 22).

## Measured after

31168 p22: 10 walls per plan, the same 10 Revit's LEVEL 4 view carries. p17: 25 (was 17).
DXF census step 12 → 14: WALL only, 13 of 13 files. Fast suite 1,003 of 1,003.

## What remains

- The north arrow's shaft (48" x 6", filled) reads as a wall on 3 of 5 sets — next rule.
- A clip ended by `Q` before the wall is painted is invisible to PdfPig; the guard is geometric.
- A stroked white knockout is not in the paper-fill population.
