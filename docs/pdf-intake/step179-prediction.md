# Step 179 — a run is a straight line, not everything joined to it

## Written 2026-10-25, tracing 31065's L1 south tower (Ian's go on "the L1 thickness zones"), before the gate below has judged it.

**Knob:** `KOR_STEP179_OFF=1` joins every two pieces that share an end into one run, whatever angle they meet at, as
before (`GeometryFilterService.WithoutTheRunsEndingAtAnArrowhead`, step 79).

**Target set:** 1 witness set, 31065-01 (S2.04.2.1, L1 south tower). Reach on the corpus NOT yet known; any plan where a
leader's arrowhead touches a piece of connected linework moves. The gate below measures it and this line is corrected
with the figure.

## The witness

31065's L1 carried the north tower's floor and none of the south tower's: 29 of 101 columns stood on no plate. Traced
(`pdf-overlay --walls`): "5 run(s) end at an arrowhead and take 247 of 3501 lines with them; run sizes 231,8,6,1,1", and
the slab pass found no floor - "column at (223.0,120.6) ft is in no cell". With the run rule skipped (a probe, not
committed) the same page closes a 13,657 sq ft floor (ring 12,533 sq ft). Among the 231: a 31 m dashed line at x 67,716
mm, the slab's edge over the ramp, a column standing on it.

Step 79 (31130's section cuts) built a "run" by joining pieces that share an end and pieces in line across a gap. The
first join took no account of direction, so a run was the whole network of linework a piece touched, round every corner -
and one arrowhead at any piece of it took the lot.

## What the change is

Two pieces sharing an end join one run only when they run the same way (within 2 degrees). Pieces in line across a gap
join as before. A dash-dot section cut is still one run and still goes; an outline turning a corner is no longer swept in.

## What must NOT move

- 31130's section cuts (step 79's witness): still not offered as slab edges.
- Halifax (revit-to-dxf; the run rule is PDF-side): byte-identical.

## The measurement that decides it

1. 31065-01 L1: the south tower's floor, rendered, against the engineer's L1.
2. 31130-01: its typical floor without the slanted hexagon step 79 removed.
3. The six banked sets, on and off.
4. `takeoff corpus-gate --bisect KOR_STEP179_OFF` on the corpus.

⛔ Run where the drawings are local. Never over the VPN.
