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

## Which of two drawn copies is kept - characterised 2026-10-26 night, NOT fixed

**The class:** when a column is drawn twice a few hundred mm apart, step 162 snaps the later drawing onto the one WRITTEN
FIRST (E2kGeometryComposer, the `overlapping[0]` snap), so sheet order decides which copy survives. The check is
`tools/CorpusGateBisect/column_stack_near_miss.py` (8c2098e0). On the six sets it finds 11 WRONG TWIN, where our column misses
the engineer's by what its stack misses. These include 3 of the 4 instances this record left OPEN on 31138 L2.

**What does NOT decide it (measured on the six sets with e5b1409f):**
- **The stack.** On all 4 known 31138 L2 instances the only neighbour is L1 below; L3 has no column within 1 m. So "move onto
  the stack when above and below agree" fires on none of them. With one neighbour, either copy can be the right one: the
  check also finds 11 PARTNER pairs, where ours is on hers and the neighbour is off.
- **The grid.** Nearer the model's own grid lines picks the engineer's copy in 12 of 21 decidable pairs (31138 6-3,
  31065 2-3, 31130 0-1, 31170-arch 4-2, plus 1 tie): chance. The grids have 16 lines, and columns stand metres off them.

**What will decide it:** the drawing. Which sheet drew each copy, and whether one copy is a column risen from the plan below
while the other is the storey's own (143 E 3rd's L37 pair was exactly that). Next: trace each WRONG TWIN to the sheets that
drew it and LOOK, then write the rule from what is seen, gated by the check. Until then the snap stays as it is.
