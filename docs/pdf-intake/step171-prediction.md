# Step 171 — a table's top rule broken at its own divider is still its top

## Written 2026-10-21, before the gate below has judged it.

**Knob:** `KOR_STEP171_OFF=1` restores the furniture region to the border's own span (`SheetFurniture.AddTitledBox`).

**Target set:** 1 witness set, 31065-01 (the PODIUM COLUMN SCHEDULE on its L2..L6 plans). Reach on the corpus NOT yet
known; the gate below measures it and this line is corrected with the figure.

## The witness

31065's walls read 131% of Rory's Revit volume. 215 m3 of it is two 5 m "walls" 990 mm thick at y 12,100 on L2..L6 -
south of the building, outside every plate (the report has listed them as such all along). They are the two GC5 cells
of the PODIUM COLUMN SCHEDULE, which the drafter rules heavily. The schedule is two halves under one title; its top rule is
drawn 389.0-535.2 and 536.7-682.8 mm, the double divider in the 1.5 mm (4.3 pt) between (page 30, measured). The border
(`ScheduleTableBorder.Under`) takes the rule under the title's left edge, so the furniture region covered the left half
and the reader read the right half's ruled cells as structure.

## What the change is

The furniture REGION follows the top rule across a gap of up to 6 pt (`SheetFurniture.TableRuleGapPts`), either way, and
never past half the page. `ScheduleTableBorder` - and so every schedule's rows, column sizes and footings - is untouched.

## What must NOT move

- Every schedule reader's output (they do not read the region).
- Any plan member that is not inside a table: the region grows only along the table's own top rule.

## The measurement that decides it

1. 31065-01 whole: the 990 mm walls gone from L2..L6, nothing else moved but what step 170 moves.
2. The six-set gate and the corpus gate, `--bisect KOR_STEP171_OFF`.
