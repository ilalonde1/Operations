# Step 151 — a set that names no grid axis is registered on its members

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP151_OFF=1` leaves a gridless set's plans where they sit on their pages (`DxfToEtabsService`,
`noSheetNamesAnAxis`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): no sheet names a grid axis, so registration never ran
and every plan stayed on its page - the roof 35 m from the tower, a two-plan sheet's left view beside its right. 13 of 13
sheets now set on the typical floor's frame. Only sets where NO sheet names an axis and no reference grid exists can
move; the six banked sets all name axes, so byte-identical is expected. Corpus reach unmeasured - 0 of 297 counted.

## What the change is

The plan placing the most storeys stands in its own frame (then most members, then name, for a stable tie); every other
plan is set where its columns and walls stand over it - the registration a gridded set already gives a sheet naming no
axis. It was first judged byte-identical on the whole-page read and dropped, then restored: in markup-only mode
(step 153) it is what puts 143 E 3rd's views on one frame.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP151_OFF

⛔ Run where the drawings are local. Never over the VPN.
