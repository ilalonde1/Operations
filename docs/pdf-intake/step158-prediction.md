# Step 158 — an upper roof is a storey above the roof

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP158_OFF=1` drops the UPPER ROOF storey (`StoreysFromPlans`, `DrawingVocabulary.UpperRoofWords`).

**Setting:** `dxf.pdf.upper-roof-words`, compiled default `UPPER ROOF`. No KorStandards row yet.

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): A107's second view, "Upper roof plan", was read as
the elevator roof and merged into ROOF; one storey added above ROOF. Corpus reach: sets with a plan named UPPER ROOF -
unmeasured, 0 of 297 counted.

## The fault

UPPER ROOF matched the elevator-roof words and was folded into the roof storey; its members stood on the roof.

## What the change is

UPPER ROOF is its own word list. Where an untagged plan names it, a storey `UPPER ROOF` is inserted after the last ROOF,
at the assumed storey height, and the levels file says ASSUMED.

## What must NOT move without a reason

- The six banked sets: none names UPPER ROOF; no change expected.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP158_OFF

⛔ Run where the drawings are local. Never over the VPN.
