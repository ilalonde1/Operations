# Step 157 — a sheet's sibling views are placed before strangers

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP157_OFF=1` restores registration in file order (`DxfToEtabsService`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): views of one sheet (`SheetOf`: the name up to
`_<n>_`) share a frame; registering a view whose sibling is already placed first put L36/L37 and B1/B2 on the tower.
Corpus reach: sets whose sheets carry two views - unmeasured, 0 of 297 counted.

## The fault

Views were registered in file order. A view registered against a stranger sheet before its own sibling took the
stranger's best fit, and two views drawn side by side on one sheet landed apart.

## What the change is

A view waits one pass for a sibling that is already placed and registers against it first; with none, it falls back to
the order before.

## What must NOT move without a reason

- The six banked sets: a placement moves only on a two-view sheet; each move must put the view where its sibling's
  frame says.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP157_OFF

⛔ Run where the drawings are local. Never over the VPN.
