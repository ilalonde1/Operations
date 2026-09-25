# Step 144 — a level taken from where the sheet sits, guessed rather than skipped

Written 2026-09-24. The rule was written, measured against the corpus, and **half of it was deleted
because the measurement said so**. That is the point of this file.

## Why

Step 143 finds the sets whose sheets will not say which storey they draw — 20 of 190 — and asks the
engineer. Asking is not enough. Ian, 2026-09-24:

> *"rather than skipping an unknown completely (like a slab) or not running the thing at all if
> there's a question (there will ALWAYS be questions) - can AI please make a best guess, then LOOK at
> the result as a sanity check, then proceed - whilst showing clearly in the question workbook that
> this was an assumption"*

So the tool answers what it can before it asks.

## The rule

A drawing whose title named no level, sitting between two drawings that did, takes the level
between them — **when the arithmetic leaves no choice.**

⚠ **The stem carries the storey, not the sheet**, and this came from the data rather than from taste.
31005-01's sheets come in pairs — `S2.05.1` and `S2.05.2`, two areas of one drawing — and only the
first one's title parsed. Ordering view by view offers three candidates (`S2.05.2`, `S2.06.1`,
`S2.06.2`) for the single level between L4 and L6 and determines nothing. Grouping by the stem
`S2.05` / `S2.06` / `S2.07` gives one gap and one unclaimed level, and L5 follows.

The set as its own ledger has it:

    S2.01  P1        anchor
    S2.02  -         gap     three gaps between a PARKADE level and L3, and no arithmetic
    S2.03  -         gap     knows how many storeys are in that stretch — DECLINED, she is asked
    S2.04  -         gap
    S2.05  L3, L4    anchor
    S2.06  -         gap     one gap, one unclaimed level  ->  L5
    S2.07  L6, L7    anchor
    S2.08  -         gap     one gap, one unclaimed level  ->  L8
    S2.09  L9 - L16  anchor
    S2.11  ROOF      anchor

**Target set:** **2 of 297 corpus sets**, and **1 of the 48** with the engineer's model
(`31005-01`, `90105-01`) — **6 drawings** in the whole corpus.

⚠ Measured AFTER the rule was written, which is the wrong order and is why
`EveryRuleStatesItsTargetSetBeforeItIsBankedTests` now exists. The 99.8% below says the inference is
RIGHT; it says nothing about whether anything needs it. Both numbers are required before building,
and only one of them was taken.

On its target set it is worth having: 31005-01 goes from 8 storeys floored to 10, and from
**8,034 to 23,468 sq ft** against the engineer's 75,694 — **10.6% to 31.0%**, the worst comparable
set in the corpus nearly tripling. That is a good outcome reached by a bad method.

## The measurement that decided it

Leave-one-out over run 45's sheet ledger: hide the level of a drawing whose title **did** parse,
infer it from the order, compare. Across **1,776 drawings**:

| | right | wrong | |
|---|---:|---:|---|
| **exactly determined** — the gap holds as many sheets as there are unclaimed levels | **480** | **1** | **99.8%** |
| spread over the gap in order | 56 | 39 | **58.9%** |

**And the one wrong answer is not wrong.** `30892-01`'s `S2.53` is titled
*"LEVEL 19-20 PLAN & LEVEL 21 PLAN - CONCRETE OUTLINE"*. The drawing covers 19, 20 and 21; the title
reader recorded only 19 and 21; the order supplied the 20 it had missed. The exact tier is
**481 of 481 against a ground truth that was itself one short.**

### So the spread tier was written, measured and deleted

At 58.9% it is a coin flip, and it puts a floor on a storey the engineer never drew. This tool's own
question J8 says what that costs: *a floor put on the wrong storey looks exactly like a floor you
drew.* **A wrong floor is worse than an absent one, because nothing about it asks to be checked.**
Those gaps go to her as step 143's question, which is the honest place for a coin flip.

This is the rule Ian asked for, bounded by what the corpus says it is worth.

## ⚠ What it does not do

- It infers a **level number** and nothing else — not a building, not a title, not a repair to the
  title block.
- It cannot tell a **mezzanine** from a storey. A set numbering L1, L1M, L2 has a level between two
  anchors that no integer names; the count then disagrees and the run is declined — or, worse, agrees
  for the wrong reason and places a floor one storey out.
- It is **blind to a set whose sheet numbers do not ascend with the building.** Nothing checks that
  the drawings were numbered bottom-up. Where an office numbers them some other way, every inference
  in a run is wrong together and every unit test still passes. Only the back-test can see it.
- ⚠ **The 99.8% flatters slightly.** The back-test hides drawings whose titles DID parse; the
  drawings this runs on in anger are ones that did NOT. They are not guaranteed to be the same
  population. It is the best estimate available and it is a strong one, but it is an estimate.

## The prediction

1. **31005-01 gains L5 and L8**, taking it from 8 of 24 storeys floored to 10 of 24.
2. **No set that reads well loses anything.** A sheet whose title named a level is never overwritten.
3. **The six-set gate moves only where a banked set has a gap sheet**, and if it moves at all the
   change must be a storey gaining a floor — never a storey losing one.
4. Every inferred level appears in the workbook as an **assumption**, with its working, not just its
   answer.

## What happened — and the two defects the unit tests could not see

Prediction 1 holds, **after two faults that were both invisible to the ten tests in this rule's own
file and both found by rebuilding 31005-01 and reading the report.**

    BEFORE   THIS SET'S DRAWINGS NAME 24 STOREYS AND ONLY  8 OF THEM RECEIVED A FLOOR
    AFTER    THIS SET'S DRAWINGS NAME 24 STOREYS AND ONLY 10 OF THEM RECEIVED A FLOOR

    ⚠ ASSUMED, NOT READ: 2 drawing(s) … S2.06.1 → level 5 (sheet S2.06 sits between S2.05
    (level 4) and S2.07 (level 6), and level 5 is the only one between them that no drawing
    claims); S2.08.1 → level 8 (…).

### 1. The sheet-number pattern is anchored at `^`, and the pipeline passes a FULL PATH

`PlanSheetInfo.FileName` is whatever `PlanSheetNaming.Parse` was handed, which in the pipeline is
`C:\…\dxf\S2.06.1_1_….dxf`. Every unit test here passed a bare name. So `StemOf` matched nothing,
`Infer` returned an empty list on every real set, and **all eight tests stayed green.**
`PlanSheetNaming.Parse` and `TitleOf` both take the base name first; now so does this.

### 2. ⭐ An empty index series had already CLAIMED every level

This is the one worth remembering. 31005-01 holds **two views of every drawing**:

    S0.00_3_LEVEL L01 PLAN CONCRETE OUTLINE.dxf          title reads perfectly
    S2.02.1_1_OUTLINE PLAN CHANGE LEVEL CONCRETE …       title scrambled, carries the structure

The `S0.00` views are an index series. Their titles are clean and they contain **nothing**:

> `S0.00_3_LEVEL L01 PLAN CONCRETE OUTLINE.dxf: no structural outlines found on the expected
> layers — not placed.`

Ordering by the letter prefix alone put both series in one sequence, so those empty views claimed
levels 1 through 22 and the `S2` gaps had nothing left to be. **A level claimed by a view that draws
nothing is not claimed.** Scoping both the ordering and the claims to the SERIES — the whole first
component, `S0` / `S1` / `S2` — fixes it, and matches how the drawings are actually organised: S0
general, S1 notes and diagrams, S2 plans.

Re-measured after the change, the back-test **improved**: 485 right, 1 wrong — five more recovered,
no new errors.

### And it corrects the 143 diagnosis

31005-01's problem was never *"the title block is unreadable."* The set contains a perfectly
readable title for every drawing — on views that carry no geometry and never place. The readable
copy and the usable copy are different files. That is a different fault from the one 143 names, and
it is still open.

## What would stop the bank

- Any storey losing a plate it had before.
- Any sheet whose title named its level being moved.
- The one measure falling on any set.
