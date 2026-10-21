# Step 162 — two columns that overlap are one joint

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP162_OFF=1` restores the old reach (`E2kGeometryComposer.ColumnJointNear`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): L37's 14x36 and 18x18 pairs, drawn overlapping, were
two columns side by side; with the reach the full larger side (`Math.Max(w, d)`) they merge. Corpus reach unmeasured -
0 of 297 counted, 0 of the 48 with the engineer's model. Composer: every set can move.

## The fault

A column joint was looked for within a fixed reach smaller than a long column's side, so two drawings of one column
standing in each other were kept as two.

## What the change is

The reach is the column's larger side. Half of it was tried first and left the L37 pairs apart.

## What must NOT move without a reason

- The six banked sets: a column count may fall only where two footprints overlap; each must be one column in her model.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP162_OFF

⛔ Run where the drawings are local. Never over the VPN.

## Judged, 2026-10-21

- **Corpus gate:** no set lost to this rule.
- **Six sets:** 41 of our columns removed on 31065, 31138, 31202 - 4 of hers lost on 31065, 3 on 31138; DXF route built
  31138's round column square.
- **Fixed:** two columns merge only where their footprints meet (`< (reach + size) / 2`, was `<= max`). 31202 gets 7
  back; the round column is round. OPEN: 31138 still merges 13, 3 of them hers - to render and look at.
