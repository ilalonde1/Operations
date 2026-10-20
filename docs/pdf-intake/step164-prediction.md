# Step 164 — a cell that says OPEN TO BELOW is a void, however its X is drawn

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP164_OFF=1` (`GeometryFilterService`, after the stair wells).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): B1 draws two voids with dashed X's (5-8 pt strokes,
some under the 200 mm line minimum), so step 106's X never forms. Written as opening regions only, so only pages a
markup-only read takes across (step 160) can move: 0 pages of the 6 banked sets.

## The fault

Step 106 lets the words decide a void, but only once an X has formed. A dashed X does not form.

## What the change is

OPEN with BELOW beside it (within 1.5 m on the page) names the smallest closed cell of the slab pass's arrangement
holding it, between 2 and 300 m², as an opening region. Never OPEN alone: OPEN TO ABOVE is this plan's own floor.

## What it does NOT do

- The page's own read is unchanged (regions are not cut on the page).
- A void whose cell does not close stays floored; the trace says so ("no closed cell holds it").
- The composer still refuses a hole with a column standing in it.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP164_OFF

⛔ Run where the drawings are local. Never over the VPN.
