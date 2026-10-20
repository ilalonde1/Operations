# Step 159 — a member rises only where there is a floor to rise to

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP159_OFF=1` restores rising unconditionally (`E2kGeometryComposer.RiseUnlessNothingOver`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): members on the roof plan outside the upper roof's
small plate (1 m slack) rose to UPPER ROOF and stood in the air; they stay on ROOF. Only members whose storey above is
UPPER ROOF are judged. Corpus reach: sets with an UPPER ROOF storey (step 158) - 0 of the 6 banked sets.

## The fault

The member-rises rule (a solid member on plan N belongs to N+1) put roof-level columns and walls up to a storey whose
floor covers a fraction of the roof.

## What the change is

A member that would rise to UPPER ROOF and lies outside every plate there (1 m slack) stays on its own storey. Walls
use the wall storey. Every other storey is unchanged.

## What must NOT move without a reason

- The six banked sets: no UPPER ROOF storey; byte-identical expected.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP159_OFF

⛔ Run where the drawings are local. Never over the VPN.
