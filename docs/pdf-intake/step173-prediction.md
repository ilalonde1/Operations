# Step 173 — a column drawn with a curve is round

## Written 2026-10-22, with Ian's go-ahead ("yes" to fixing the round columns), before the gate below has judged it.

**Knob:** `KOR_STEP173_OFF=1` writes every column as a rectangle again (`GeometryFilterService.DrawnRound`).

**Target set:** 1 witness set, 31065-01 (C16, C18, C20, "450 Ø" on every tower plan). Reach on the corpus NOT yet
known; any set whose plans draw a column as a filled Bezier circle moves. The gate below measures it and this line is
corrected with the figure.

## The witness

31065's columns read 120% of Rory's Revit volume. Every one of the 1,197 column footprints in its DXFs (six-set build,
2026-10-21) is a four-point rectangle: the PDF walk flattens curves to points and keeps no word that a path was a
curve, the reader sized each column by its box, and the DXF wrote the box. A 450 mm round column priced as a 450 mm
square is 27% over (1 against pi/4).

The DXF side already models a column round when - and only when - it was drawn with an arc
(`StructuralPlanClassifier.AddColumn`): shape tests once turned 160 chamfered squares into 10 in circles on 31168.

## What the change is

- The page walk records, per subpath, whether it used a cubic Bezier (`HasCurve`); the page cache is version 2.
- The reader marks a column round when its filled outline was drawn with a curve, is square to a tenth, and fills
  0.72-0.85 of its box (`ExtractedGeometry.ColumnIsRound`, parallel to `Columns`).
- The DXF writes a round column as a full-circle ARC on the column layer; the classifier's arc rule takes it from there
  and the e2k carries a `Concrete Circle` section.

## What must NOT move

- A square column, a chamfered or radiused square (fills above 0.85), a circle drawn without curves.
- Anything but columns' shapes; the page cache bump re-reads every PDF once, same geometry.
- Not covered: the app's PDF window keeps its own copies of the column lists and still writes squares.

## The measurement that decides it

1. 31065-01 whole: column volume against Revit; the C16/C18/C20 placements round in the e2k.
2. The six-set gate and the corpus gate, `--bisect KOR_STEP173_OFF`.

## Judged on its witness, 2026-10-24 - and corrected

As first committed (f5a74e04) the step turned **none** of 31065's round columns round: a build of the committed reader
wrote 0 ARCs in 51 DXFs and no circular section. Traced on S2.16.1 (`pdf-at` at C18): the column is a grey 449 x 444 mm
**square** fill painted through a clip drawn just before it with the same box, and the clip is an 86-point **polygon** -
no Bezier anywhere. The premise "31065 draws a filled Bezier circle" was never true; the step was verified only on
synthetic tests.

Corrected: a filled column is round when its own outline passes the curve test (as before), or when its clip - found as
step 172 finds a wall's, the one clip drawn just before it, else the one clip with its box - is drawn with a curve or IS a
circle (`IsCircle`: square to a tenth, 16 or more corners, every corner within 5% of one radius, that radius half the box,
filling 0.72-0.85 of it). An octagon, a square clip, a clip that is not the column's, and a radiused square drawn by its
corner arcs alone stay square (tests).

31065-01 whole, one build (develop + this), `KOR_STEP173_OFF` on and off: 82 round columns in the DXFs, 3 circular
sections in the e2k; columns 971 -> 957 m3; slab 9,080 and wall 3,730 m3 identical both ways. Revit has 814 m3 of column.
Most of what remains is the pricing convention the takeoff states ([HEIGHT_FROM_STOREY]: a column is priced over the full
rise, through the slab; Revit cuts it at the slab) - not measured member by member.

Reach on the other corpus sets: NOT yet known. The corpus gate with `--bisect KOR_STEP173_OFF` measures it.
