# Step 178 — a column drawn turned is turned

## Written 2026-10-25, with Ian's go ("Go on you go's") on the open item "skewed-grid columns", before the gate below has judged it.

**Knob:** `KOR_STEP178_OFF=1` sizes every column by its box on the page's axes and writes it square to the page, as before
(`GeometryFilterService`, both column sites; `DxfExporter`).

**Target set:** 2 witness sets, 31065-01 and 31130-01 (oblong columns on a skewed grid). Reach on the corpus NOT yet
known; any set drawing columns off the page's axes moves - 31183-01's tower grid runs about 4 degrees (looked at,
S2.09.1, 2026-10-25). The gate below measures it and this line is corrected with the figure.

## The witness

With the column ANG fixed (231e0f69), every oblong column we share with the engineers' models at a place and storey stood on the engineer's
bearing - except 39 on a skewed grid: 34 of 31065's at 8 degrees off the engineer's (ours 90, the engineer's 82) and 5 of 31130's at 17 (ours
90, the engineer's 73). The reader sized a column by its box on the page's axes (`bboxW, bboxH`) unless the schedule declared a
size it only matched turned, and the DXF wrote every column as that box, square to the page. A 12 x 30 at 4 degrees
reads 30.8 x 14.1.

## What the change is

- `ExtractedGeometry.ColumnTurnDegrees`, parallel to `Columns`: the bearing of the width side, 0 square to the page.
- Declared-size path: a column matched turned carries its oriented box's bearing (it already carried its size).
- Shape path: a filled column whose oriented box is plainly smaller than its page box (under 95% of it) is sized and
  turned by its oriented box; a round one is not (its box has no bearing).
- `DxfExporter` writes the rectangle at that turn; the classifier's oriented box reads it back as the column's angle,
  which the e2k writes as ANG (231e0f69).

## What must NOT move

- A column square to the page: its oriented box is its page box, turn 0, the same rectangle as before.
- Round columns, walls, plates.
- Halifax: its revit-to-dxf route writes the manifest's columns itself (the turn there is the Virtual Drafter's
  e2k_columns override until revit-to-dxf carries it). Checked byte-identical before commit.

## The measurement that decides it

1. 31065-01 and 31130-01: bearings against the engineers' models (plates/colang.py), on and off - the 39 within a few degrees.
2. The six banked sets, on and off: only skewed columns move (sizes and turns).
3. Halifax e2k byte-identical.
4. `takeoff corpus-gate --bisect KOR_STEP178_OFF` on the corpus.

⛔ Run where the drawings are local. Never over the VPN.

## The Revit route, 2026-10-25 (agreed with the Virtual Drafter)

The Virtual Drafter's manifest now carries `rotation_deg` - the long face's bearing from X, written only for a turned
rectangle (KOR.Drafter tools/markup_to_plan.py). `RevitToDxfVerb` reads it into `ColumnTurnDegrees`: the bearing of the
side `ColumnSizes` calls width, the type's first number, so a width shorter than the depth turns a quarter more; absent
is turn 0, as before. Halifax on its own commands, local copies, the same build with and without the read: model-diff
"plates moved 0, columns lost 0 / gained 0, walls lost 0 / gained 0"; 88 column-storeys go from ANG 90 to the manifest's
bearings (152.6 x42, 153.2 x21, 62.9 x21, 62.2 x4), the other 184 keep 90.

## Found on the way: the view split forgot the turn (and the pier flag)

The first whole-31065 run read the 8-degree columns right on the page and wrote them square: `SheetViews.Split` copies
each per-column list by hand and did not copy `ColumnTurnDegrees` - the third time a per-column list was left behind
(the tendon-anchor flag, the round flag). `EveryListParallelToAMemberListGoesWithItIntoTheView` now fills every
Column/Wall/Slab/Line list by reflection and requires each view to carry it; on its first run it found a fourth,
`ColumnIsWallPier` (a stood-down pier written as a column again in a two-view sheet). Both are carried.
