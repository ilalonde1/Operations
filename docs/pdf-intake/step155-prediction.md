# Step 155 — a grid shift is judged by the members that could stand on it

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP155_OFF=1` restores "half of all members" (`GridAlignment.SolveByColumns`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): the 7-35 typical plan set its sheets 35 m apart before,
because half was counted over every member on the sheet, including those outside the placed extent at that shift. Corpus
reach unmeasured - 0 of 297 counted, 0 of the 48 with the engineer's model.

## The fault

`SolveByColumns` accepts a shift when half the members land on members already placed. Members outside the extent the
placed sheets cover at that shift cannot land on anything, yet they counted in the denominator, so a sheet that agrees
exactly over the ground both cover was refused.

## What the change is

"Half" is counted over `couldStand`: the members inside the placed extent at that shift.

## What must NOT move without a reason

- The six banked sets: a sheet's placement. Any move is a shift this step now accepts.
- Coverage ratchets (`ModelCoverageTests`): may only rise.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP155_OFF

⛔ Run where the drawings are local. Never over the VPN.
