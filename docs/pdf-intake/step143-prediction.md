# Step 143 — a set whose sheets will not say which storey is handed back, not shipped

Written 2026-09-24. The signal was measured on run 45's ledger **before** any code was written; the
outcome section below was filled in after, including the one defect this got wrong.

## Why this rule exists

It is not a reading rule. It is the first thing in this pipeline that lets the tool say **"I could not
build this one"** — which is the half of the design that was missing.

Ian, 2026-09-24:

> *"if there's an anomalous project drawing that is so broken you can't do it - ignore it and move on to
> the other 95% we CAN build. If something is SO garbled and shitty - just reject it with the list of
> questions we would usually present to the engineer"*

and, on where it belongs:

> *"the machine does as MUCH as it possibly can (and it gets better every time with the gained knowledge
> of answered questions). THAT'S THE WHOLE POINT"*

So this lands as a **`ModelQuestion` (J8)**, not as a warning. A warning is read once and decays. An
answer is banked and the question is never asked again. The first cut of this was a free-text warning in
`DxfToEtabsService`; that was the wrong shape and was deleted before it ever ran.

## The witness

`31005-01` builds **24 storeys, 8 with a floor**, and reads **10.6%** of the engineer's plate area — the
worst comparable set in the corpus. Nothing in its 80 KB report said the set was never readable.

Its title block writes the sheet title **up the page in three columns**:

| x≈2778 | x≈2820 | x≈2954 |
|---|---|---|
| `LEVEL` / `L01` / `PLAN` | `CONCRETE` / `OUTLINE` | `CONSTRUCTION` / `CHANGE` |

The reader assembles it **across** instead of **down**, so five sheets come out sharing the name
`OUTLINE PLAN CHANGE LEVEL CONCRETE CONSTRUCTION` and the `L01` is lost. Its sibling
`LEVEL L03-04 PLAN CONCRETE OUTLINE` parses correctly to L3/L4, so the parser half works.

`30940-01` and `30941-01` use the same title block and **do not print the level as text at all**. No
reader recovers that at any effort. It is a drawing to hand back with a question, not a rule to write.

## The signal — and the one that looked right and was not

**Rejected: "most of the plan views name no storey."** It is the obvious test and it is wrong.
`30993-01` has **121 of its 148 views naming none** and reads **91%** of her plate area, because a
reinforcing sheet correctly stood down names no storey, and neither does a typical-floor sheet serving
fifteen storeys.

**Taken: the set's own ladder against the floors it got.** The ladder comes from the elevations — it is
what the drawings say the building *is* — and a storey on it that never receives a plate is a storey the
tool did not build. Measured from the **finished file after every cut**, so a tower-only model is judged
on the storeys it actually ships.

Fires when the ladder has **≥ 4 storeys** and **half or more** carry no plate.

## What it catches, counted on run 45

| | |
|---|---:|
| sets in the ledger | 295 |
| with a ladder of ≥ 4 storeys | 190 |
| **fires** | **20 — 11%** |
| of the 48 sets where her model can be compared | **3** |

The three comparable sets it fires on, with what they read of her plate area:

| job | floored | reads |
|---|---|---:|
| 31005-01 | 8 of 24 | **10.6%** |
| 31224-01 | 3 of 6 | **23.4%** |
| 30989-01 | 7 of 27 | **53.9%** |

**No set that reads well trips it.** The best-reading set it fires on is that 53.9%; `31155-01` reads
**106%** of her area and leaves 38% of its ladder unfloored, under the cut.

## ⚠ What it does NOT cover

Required by working rule 11, and measured rather than guessed. **It is precise and it is narrow.**

It is a **count of storeys** and nothing else. It cannot see a storey given a plate of the wrong shape, in
the wrong place, or a tenth of the area it should be. It says nothing about walls, columns, thickness or
openings.

The same-class faults it would **not** catch are in the same ledger:

| job | floored | reads | |
|---|---|---:|---|
| 31174-01 | 6 of 7 | **0.0%** | the worst-reading comparable set in the corpus — silent here |
| 31143-01 | 7 of 7 | **3.9%** | floored every storey it has |
| 31064-01 | 3 of 3 | **21.4%** | |
| 31117-01 | 7 of 7 | **32.6%** | |
| 31048-01 | 7 of 7 | **34.2%** | |

So of the sets that badly under-read her, this catches the ones that could not **name** a storey and
none of the ones that named every storey and then **drew the wrong thing on it**. Those are a different
class with a different fix, and the yardstick against her model — not this — is what finds them.

A set whose **elevations are themselves short** also reads a perfect score here while missing most of the
tower, because both sides of the comparison come from the same drawings.

## The prediction

1. **No geometry moves.** This reads the finished model and adds a flag and a question. Plate area,
   walls, columns, openings and thickness must be identical. The six-set gate must be byte-identical.
2. **It fires on 31005-01 and is silent on 31155-01.**
3. **The one measure does not move.** 77.12% before, 77.12% after.

## What happened

1 and 2 hold. Verified by rebuilding both sets and **reading the artifacts**:

- `31005-01` report: `THIS SET'S DRAWINGS NAME 24 STOREYS AND ONLY 8 OF THEM RECEIVED A FLOOR` — and the
  8 of 24 matches the ledger exactly.
- `31155-01` report: the flag does not appear.
- The workbook carries **J8**, topic `a-set-whose-sheets-will-not-say-which-storey`.

### ⚠ And it shipped one defect, caught by opening the workbook rather than by any test

The first cut listed the unreadable drawings with `Walls + Columns + Slabs > 0`, to put the ones carrying
the most structure in front of her first. **On these sheets that sum is always zero.**
`SheetOutcome.Walls/Columns/Slabs` are read back from the finished file **after the cut**, and a sheet
that reached no storey contributed no objects. The filter removed precisely the sheets the question
exists to list, and 31005-01 — the set it was written for — produced:

> *"None of this set's drawings is waiting on a level."*

Every test was green. The report flag was correct. Only the workbook showed it.

This is the **same trap the Codex audit caught the day before** on the sheet ledger's `slabs` column, and
it is the same sentence: **a count is not a measurement until you know what it counts** — and this one
counts what *survived*.

Fixed by dropping the counts entirely and filtering on `SheetsSetOnGridByName`: a sheet set on the
model's grid by its own axis names was read and positioned correctly, and the only thing missing is the
level. A details or sections sheet names no storey either, is not on the grid, and stays out of a list
she is asked to work through. `31005-01` now names **10 drawings**, five of them the identical scrambled
title — which is the fault, visible to her, answerable by sheet number.

The regression test is built on a fixture where **every count is zero**, so that filter cannot come back.
