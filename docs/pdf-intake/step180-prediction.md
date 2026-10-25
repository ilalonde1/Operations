# Step 180 — one column read off two sheets is one stack

## Written 2026-10-25, measuring step 179 on 31065 against the engineer's model, before the gate below has judged it.

**Knob:** `KOR_STEP180_OFF=1` keys a column stack by its exact plan position, as before
(`E2kDocument.ModelDoubleHeightMembersOnBothFloors`).

**Target set:** 1 witness set, 31065-01 (two ramp-edge columns, L1). Reach on the corpus NOT yet known; any job whose
sheets register a few inches apart and whose columns have a one-storey gap moves. The gate below measures it and this
line is corrected with the figure.

## The witness

Step 179 gave 31065's L1 its south tower floor, and 16 column-storeys went with it: "double-height members modelled on
both floors" fell from 29 to 13. Ten of the places hold no column of the engineer's on L1 (the engineer's nearest 1.9-5.0 m away) - tower
columns carried down past the transfer floor, rightly gone. Two are the engineer's: C600x900 at 74 mm, the columns on the ramp's
dashed edge. Ours on P2 and P1 stand 51 mm from ours on L2 - two stacks, so L1 between them was never a gap; their L1
segment existed only while the carry-down (a column hanging over a storey with no plate) happened to reach it.

## What the change is

Two column stacks within `dxf.already-modelled-tolerance` (KorStandards, 6 in - the reach at which a generated column is
"the one already modelled") that share no storey are one stack. Two that share a storey are two columns and stay apart.
Walls are keyed by both ends and left as they were.

## What must NOT move

- A stack with no gap: merging adds nothing to fill.
- Two columns a foot apart, or two on one storey.
- Halifax and the six banked sets: on and off, before commit.

## The measurement that decides it

1. 31065-01 L1: the engineer's two ramp-edge columns back, the ten tower columns still gone.
2. The six banked sets, on and off, columns against the engineers' models.
3. `takeoff corpus-gate --bisect KOR_STEP180_OFF` on the corpus.

⛔ Run where the drawings are local. Never over the VPN.
