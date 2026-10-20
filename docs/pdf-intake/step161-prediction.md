# Step 161 — a floor taken from the walls stays within the walls

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP161_OFF=1` keeps the traced ring whole (`StructuralPlanClassifier`, the perimeter-wall fallback).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): B1 and B2's traced ring ran to x 70.1 m and straight
back, a spike 5 m past every wall. Only storeys whose floor comes from the perimeter-wall fallback can move. Corpus
reach unmeasured - 0 of 297 counted, 0 of the 48 with the engineer's model.

## The fault

The fallback's traced ring can leave along a line past the walls and return. That vertex is the trace's, not the
building's.

## What the change is

A vertex beyond the walls' own extent (plus the thickest wall as slack) is dropped from a wall-taken floor.

## What must NOT move without a reason

- The six banked sets: a parkade floor taken from its walls may lose a spike; any other change is a fault.
- Coverage ratchets: plate area may fall only by spike area.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP161_OFF

⛔ Run where the drawings are local. Never over the VPN.
