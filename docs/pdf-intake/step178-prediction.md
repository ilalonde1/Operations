# Step 178 — a column's turn is carried to ETABS

## Written 2026-10-25. What is committed is the carrying; the PDF reader's own reading of a turn is step 181, parked.

**Knob:** none. The turn carried is what the source gives - the Virtual Drafter's manifest (`rotation_deg`) on the Revit
route, 0 from the PDF reader until step 181 lands - so a model with no turns given is byte-identical to before.

**Target set:** 1 witness set, 31180-01 Halifax (the Revit route; 88 turned column-storeys). The PDF route does not move.

## The witness

With the column ANG fixed (231e0f69), every oblong column we share with the engineer's model at a place and storey stood on
the engineer's bearing - except columns drawn turned. On the Revit route the Virtual Drafter's manifest wrote Halifax's
20 turned columns square to the grid, and its own e2k_columns step set their ANG after the fact; on the PDF route 31065's
and 31130's columns on a skewed grid (8 and 17 degrees) went out square to the page.

## What the change is

- `ExtractedGeometry.ColumnTurnDegrees`, parallel to `Columns`: the bearing of the side `ColumnSizes` calls width.
- `DxfExporter` writes the footprint at that turn; the classifier's oriented box reads it back as the column's angle,
  which the e2k writes as ANG.
- `RevitToDxfVerb` reads the manifest's `rotation_deg` (the long face's bearing, written only for a turned rectangle,
  agreed with the Virtual Drafter): a width shorter than the depth turns a quarter more; absent, turn 0.
- `SheetViews.Split` carries it into each view.

## Measured

- Halifax on its own commands, local copies, the same build with and without the manifest read: model-diff "plates moved
  0, columns lost 0 / gained 0, walls lost 0 / gained 0"; 88 column-storeys go from ANG 90 to the manifest's bearings
  (152.6 x42, 153.2 x21, 62.9 x21, 62.2 x4), the other 184 keep 90; the rest of the text is column renumbering (the
  Virtual Drafter's tools key columns by position, checked by it in code).
- The PDF route: the reader writes 0, the DXF rectangle at turn 0 is the rectangle written before, corner for corner.

## Found on the way: the view split forgot per-column lists

`SheetViews.Split` copies each per-column list by hand, and three had been left behind before this (the tendon-anchor
flag, the round flag, the turn). `EveryListParallelToAMemberListGoesWithItIntoTheView` fills every Column/Wall/Slab/Line
list by reflection and requires each view to carry it; on its first run it found a fourth, `ColumnIsWallPier` (a
stood-down pier written as a column again in a two-view sheet). Both are carried.
