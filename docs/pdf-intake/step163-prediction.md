# Step 163 — a floor taken from the walls keeps the holes inside it

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP163_OFF=1` drops them again (`StructuralPlanClassifier`, `PlanGeometrySet.LooseRings`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): L1, B1 and B2 had 0 openings - lifts and stairs floored
over - and now 8 each, every one flagged "it stood inside no floor until that one was taken; check it is a hole".
Only storeys whose floor comes from the perimeter-wall fallback can move. Corpus reach unmeasured - 0 of 297 counted.

## The fault

Rings too small to be a floor are judged before the fallback floor exists. Inside no floor then, they were dropped as
linework, and the floor taken afterwards from the walls had no holes.

## What the change is

Those rings are kept (`LooseRings`). When the floor is taken from the walls, a kept ring wholly inside it and under half
its area is cut as an opening, once (a ring read twice is one hole), with a flag naming it.

## What it does NOT do

- Rings already cleared as a floor's interior (curbs, ramps - step 78) stay cleared. Only rings smaller than a plate.

## What must NOT move without a reason

- The six banked sets: a parkade from its walls may gain holes - each must be a hole in her model, or a curb, a drain or
  a pad wrongly cut, which is this step's failure mode and the reason it is knobbed.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP163_OFF

⛔ Run where the drawings are local. Never over the VPN.
