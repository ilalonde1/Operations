# Step 166 — a parkade level is covered under any parkade word

## ⚠ WRITTEN AFTER THE CODE, 2026-10-20. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP166_OFF=1` checks the ladder for `P<n>` alone (`StoreysFromPlans.Merge`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): elevations name B1 and B2, plans LEVEL B1 and LEVEL B2.
Checking `P<n>` alone added P1 and P2 beneath B2 at assumed heights - the two-level parkade modelled four times, 43
storeys where the building has 41. Only sets whose ladder names a parkade level with a word other than P can move.
Corpus reach unmeasured - 0 of 297 counted, 0 of the 48 with the engineer's model.

## The fault

A plan's parkade level reads under every `dxf.parkade-words` entry (P, B: step 88, "the plan and the storey meet on
the number"), but the merge asked whether the ladder already covered it only as `P<n>`.

## What the change is

A plan's parkade level `n` is covered when the ladder holds `<word><n>` for any parkade word.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP166_OFF

⛔ Run where the drawings are local. Never over the VPN.
