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

## Judged, 2026-10-21 (corpus gate run 24b09de9, 57 sets)

- **Corpus gate:** 2 sets of 56 moved by this rule. 31117-01 lost every plate (5,421 -> 0 sq ft of 9,086; openings
  7 -> 0); 31064-01 gained plate (4,536 -> 10,654 of 21,181) and lost openings (10 -> 7 of 10).
- **Why, read from 31117's views:** S2.02, S2.03 and S2.09 carry the view's NUMBER ("1", "2") on the underline with the
  scale under it. Step 90's storey-alone rule reads a lone number as a storey's name, so LEVEL 1, 2 and 5 PLAN were
  split into views titled "1" and "2" that no storey has, and nothing reached a storey.
- **Fixed:** a storey-alone view title must say its storey in a word (`text.Any(char.IsLetter)`); test
  `AStoreyAloneViewTitleSaysItsStoreyInAWordAndABareNumberIsTheBubble`. 31117 and 31064 rebuilt after the fix: see below.
- **After the fix:** 31117 back to the bank (5,421 sq ft, openings 7 of 7); 31064 builds exactly as with
  KOR_STEP150_OFF (4,536 sq ft, 10 of 10). Its 10,654 sq ft under the unfixed rule came with the bare-number split and
  went with it; that L2 still reads 4,536 of her 21,181 is open and not this rule's.
