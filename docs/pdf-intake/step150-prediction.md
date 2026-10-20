# Step 150 — a view titled by its storey alone is a view, when its scale is stated under it

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the corpus gate below has judged it.

**Knob:** `KOR_STEP150_OFF=1` restores "a view's title says PLAN" (`SheetViews.Titles`).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel, an architect's set, not in the corpus): 5 of its 8 plan
sheets carry two views. Corpus reach unmeasured — 0 of 297 counted, 0 of the 48 with the engineer's model.

## The witness

143 E 3rd titles every view in Revit's form — number bubble, "LEVEL 36", the underline, and the view's scale
directly under the line. No PLAN. `NamesAPlan` asks for PLAN or a framing-over clause, so A101 (B1 & B2), A102 (1 & 2),
A103 (3 & 4), A104 (5 & 6) and A106 (36 & 37) were one view each, and both plans landed on both storeys.

Step 90 already rules that a title which is a storey's name alone names that storey (`NamesAStoreyAlone`) — for
SHEET titles. This applies it to VIEW titles, guarded: the view's scale must be stated under its underline
(`SheetScaleReader.RatioOf`). That guard is what refuses A102's "LEVEL 1 PARKING", an underlined schedule heading
that the storey-alone rule alone would accept (PARKING is a joining word).

| | before | after |
|---|---:|---:|
| views written | 8 | 13 |
| views placed on one frame | — | 11 of 12 |
| storeys whose plates stack (x 17–30 m) | typical only | all but L1 |

A sheet with one title is one view as before: A105's lone "LEVEL 10" does not narrow A105 from L7–L35.

The registration that placed the split views is the existing one (step 55, by columns, seeded by A101's three
named axes). A gridless seed was tried as step 151 and REJECTED: byte-identical on the witness (stash, 2026-10-19).

## What must NOT move

- The six banked sets: structural stick files title views with PLAN, so they must be byte-identical.
- A sheet whose underlined storey-name line is a heading, not a view: the scale guard is the only thing between.

## The measurement that decides it

    takeoff corpus-gate --bisect KOR_STEP150_OFF

1. Views written per set, and storeys with a placed drawing — must not fall.
2. Her plate area we reproduce — must not fall.
3. The six-set gate.

⛔ Run where the drawings are local. Never over the VPN.
