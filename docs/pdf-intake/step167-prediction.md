# Step 167 — a shaft cut above and below runs through

## ⚠ WRITTEN AFTER THE CODE, 2026-10-20. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP167_OFF=1` (`E2kGeometryComposer`, after the borrowed floors).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): 2 openings added - L36 stair A (drawn with dashed treads,
so no flight was read; cut on L35 and L37) and B1 stair B (cut on B2 and L1). Composer: every set can move.
Corpus reach unmeasured - 0 of 297 counted, 0 of the 48 with the engineer's model.

## What the change is

A storey with a floor and no opening where the storeys directly below and above it both cut one (boxes overlapping half
the smaller) is assigned that opening - the same area object, as a borrowed floor takes its donor's holes. Flagged
ASSUMED, by storey.

## What it does NOT do

- A storey with no floor; a hole cut on one side only; any hole a storey already has there.
- A real floor between two holes (a landing slab over a double-height void with a floor between) would be cut - the
  failure mode, and why it is knobbed and listed.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP167_OFF

⛔ Run where the drawings are local. Never over the VPN.

## Judged, 2026-10-21

- **Corpus gate:** no set lost to this rule (31168 shares no storey with columns with her model there, so the gate
  could not see it).
- **Six sets:** 31168's openings we have of hers fell 64 -> 28 of 64 - this rule; it also failed 31168's integrity test
  (29 openings cut where there is no plate).
- **Fixed:** the storeys below and above are judged AT THE HOLE (a plate under its centre, or a hole overlapping it by
  half), not by list position; the storey between must have a plate under the hole. 31168 back to 64 of 64; Omar's
  143 E 3rd byte-identical to v6 (L36 KO69 still carried); 31202 59 of 68 (KO1 on L2 is not cut: no plate there).
