# Step 140 — a sheet that prints one field thickness prints it for the sheet

**Target set:** **46 plan sheets** that print exactly one field thickness no plate took, across the
**112 of 297 corpus sets** that default a plate. Measured before the rule was written.

Written 2026-09-23 23:0X PDT, **before** anything was built. Judged against this file.

## Why this rule exists

The report has been saying, of 112 sets, that a plate carries the 12″ default *"because no thickness call-out
is printed inside them on the drawing"*. That sentence is true and the impression it gives is false. Counted
over the built corpus before writing a line of code:

| | |
|---|---:|
| floor plates carrying the 12″ default | **1,586 of 2,763 — 57%** |
| sets that default a plate | 112 |
| **of those, sets whose drawings print NO thickness call-out at all** | **0 of 112** |

Not one of the 1,586 is in a set where the drawing is silent. The thickness is on the page; the reader only
looks inside the plate.

Of the **1,433 plan sheets that produced a floor plate**:

| what the sheet prints | sheets |
|---|---:|
| no field call-out at all | 456 |
| exactly one, and a plate took it | 448 |
| **exactly one, and NO plate took it** | **46** ← this rule |
| several, and a plate took one | 414 |
| several, and none was taken | 69 |

## The rule

A plate that claimed no call-out of its own, on a sheet whose field call-outs (≤ 16″, so a mat or transfer
slab is not collected) are all the same number, takes that number. The flag says it was printed OUTSIDE the
plate so a reader can tell it from a plate's own reading. Knob `KOR_STEP140_OFF`.

It does **not** touch a plate the pass above refused for carrying two different numbers, nor a plate a previous
pass already settled. The first cut did both, and the banked test
`TwoDifferentCallOutsInOnePlateAreRefusedRatherThanGuessedBetween` caught it.

## The prediction

1. **Thickness agreements rise and nothing else moves.** This rule sets a number on a plate; it changes no
   geometry. Plate area, columns, walls and openings must be **identical** in both arms. If any of them moves,
   the rule has reached somewhere it should not and it is parked.
2. **The corpus thickness agreement (223 of the judged storeys as develop stands) rises by single figures,
   not tens.** The target is 46 sheets across the whole corpus, most of them in 01379-01, which has no
   yardstick. I expect **between 2 and 10** more storeys agreeing across the 57 judged sets.
3. **No set loses a thickness agreement.** A plate that had the default 12″ and now reads 8″ can only move
   toward or away from hers; where it moves away, that set is named and the rule is re-scoped, not banked.
4. **The six-set gate moves on thickness alone**, or not at all. A plate's section changes (KOR-S304.8 →
   KOR-S203.2), so a byte-identical build is not expected — but plates moved, columns lost/gained and walls
   lost/gained must all be **zero** for all six. Anything else is a stop.

## What would stop the bank

- Any movement in plate area, column count, wall count or opening count in either arm.
- A net fall in thickness agreements, or any single set losing one.
- The six-set gate showing anything other than a section change.

## What this does NOT claim

It does not touch the 456 sheets that print nothing, which is the larger pool and needs the storey or the set
rather than the sheet. It does not make our thickness agree with hers where we already read one: 31017-01's L1
reads 12″ from its own `12" SLAB` printed twice against `10"` once, and the engineer used 10″ — a plate can be
read correctly off the drawing and still disagree with her, and no rule here changes that.
