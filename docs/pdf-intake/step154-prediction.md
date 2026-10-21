# Step 154 — a long face is the partner of every short face along it

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP154_OFF=1` restores one face, one partner (`WallOutlineDecomposer.Decompose`, the second pass).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel, A105 typical floor): L35 walls 11 -> 14, the three
added being the core's left wall beside stair A (5.69 m) and beside stair B (3.21 + 5.31 m). Corpus reach unmeasured -
0 of 297 counted, 0 of the 48 with the engineer's model. This is the composer: every set can move.

## The fault

`Decompose` pairs each face with its nearest opposite face and marks BOTH used. Where one face of a wall runs
unbroken and the other is broken by the walls meeting it, the unbroken face pairs once and every other piece of the
broken face is left without a partner. A105's core: outer left face one 26.8 m line; inner face four pieces (5.0,
11.25, 2.7, 4.8 m). The outer face paired with 11.25 m (the spine) and the core lost its left wall beside both stairs.

## What the change is

After every existing pass (the first pass and the deferred near-square pass, both unchanged), a face still unpaired
may pair with a face already used, by every existing test (parallel, a wall's floor to its max apart, overlapping by
the panel overlap, concrete between, the material run). Refused where a wall of the outline already runs along the
band (`RunsAlong`: parallel, axis within half the thicker, overlapping). A near-square leftover is not made. The used
face stays available to the other short faces along it.

## What must NOT move without a reason

- The six banked sets: any change is a wall this step adds; each must be a leg of a wall the engineer has (her model).
- Coverage ratchets (`ModelCoverageTests`): may only rise.

## The measurement that decides it

    the six-set gate (local mirror)            - what moves, set by set
    takeoff corpus-gate --bisect KOR_STEP154_OFF   - against her walls on the 48

⛔ Run where the drawings are local. Never over the VPN.

## Judged, 2026-10-21

- **Corpus gate:** no set lost to this rule.
- **DXF route:** 31138 builds 228 walls against her 204 +/-21 - 24 walls her model does not have, which fails this rule's
  own criterion above. With KOR_STEP154_OFF the walls are in tolerance. OPEN: not yet rendered and looked at.
