# Step 156 — the engineer's own title names a plan

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP156_OFF=1` drops markup titles (`SheetViews.MarkupTitles`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): the engineer wrote "Upper roof plan" as FreeText on
A107, a view the sheet itself leaves untitled; 1 view gains a storey. Corpus reach: only sets with a FreeText markup
naming a plan can move - 0 of the 6 banked sets carry one (to be confirmed by the gate), corpus unmeasured.

## The fault

A view the architect did not title was given no storey, though the engineer's markup names it.

## What the change is

A markup FreeText that reads as a plan title becomes a `View(..., Drafted: true)`. A drafted title never takes a view
whose drawing reaches further than its own width (`Owner` skips it where Beyond > width), so it names the plan it sits on.

## What must NOT move without a reason

- The six banked sets: no change expected (no FreeText plan titles).

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP156_OFF

⛔ Run where the drawings are local. Never over the VPN.
