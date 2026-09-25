# Step 147 — a reinforcing sheet is refused as a plan and still draws the slab

**Target set:** **68 of 297 corpus sets** hold a refused reinforcing view that closes slab edges AND
storeys with no plate — **479 unplated storeys** between them. **22 of the 68** have the engineer's
model, and they are short of her by **965,538 sq ft — 67% of the whole 1,436,462 sq ft corpus gap.**
Measured from the corpus DXFs **before the rule was written**.

## The fault

`30989-01` builds 27 storeys and 7 floors. Its sheet table says why in one line:

```
S2.15  LEVEL 4 19 PLAN CONCRETE OUTLINE             ->  0 slabs   40 cols  65 walls
S2.16  LEVEL 3 - 19 SLAB REINFORCING PM             ->  7 slabs   40 cols  65 walls
S2.17  LEVEL 21 MECH. PLAN CONCRETE OUTLINE         ->  4 slabs
S2.18  LEVEL 20 SLAB & L21 MECH. PM SLAB REINFORCING->  9 slabs
```

**The CONCRETE OUTLINE sheets close no slab. The SLAB REINFORCING sheets do** — and the reinforcing
sheets are refused:

> `7 sheet(s) in the folder are not structural plans and were not read: … S2.16_1_LEVEL 3 - 19 SLAB
> REINFORCING PM.dxf [REINFORC] … Governed by dxf.non-structural-sheet-patterns.`

It is not an accident of one set. **Rebar is drawn INSIDE a bounded slab**, so the reinforcing
drawing carries the closed boundary that the outline drawing spends on dimensions and leaders.
Corpus-wide: of **1,201** reinforcing views, **1,032 — 86% — carry slab-edge entities**, across
**129 of 297 sets**, totalling 225,121 slab-edge entities thrown away.

## The rule

A sheet refused by a `REINFORC` pattern is no longer dropped. It is kept **for its slab edge alone** —
walls, columns, partitions and wall-type tags are cleared before it reaches placement.

⚠ **The refusal is not wrong and is not being undone.** It exists because 30990's footings, drawn
filled for their bars, read as **54 columns on P3** and rose to P2 to stand 1.8 m from every column
the engineer modelled. Everything that caused is still refused. What is taken is the one thing a
reinforcing drawing states better than the plan it is about.

A plate read twice is already handled — `SettleFloorsAcrossSheets` drops a ring inside another
sheet's floor, and the classifier refuses a plate over one already read on the storey — so a storey
whose outline sheet DID close keeps what it had. Knob `KOR_STEP147_OFF`.

## The prediction

1. **30989-01 gains floors on most of its empty storeys.** It has 7 reinforcing views with edges.
2. **Columns do not move.** If any set gains a column, the slab-only restriction has leaked and the
   rule is wrong — that is the 30990 fault returning.
3. **The guard sets do not lose anything.** 30972-01 (20 of 20 floored, 85%) and 30993-01 (39 of 40,
   91%).

## What happened

| job | floored | floors | ours sq ft | vs her |
|---|---|---|---|---|
| **30972-01** guard | 20 → 20 | 20 → 20 | unchanged | **85% → 85%** |
| **30993-01** guard | 39 → 39 | 50 → 50 | 510,530 → **551,272** | **91% → 98%** |
| 30989-01 | 7 → **25** | 7 → **45** | 61,823 → **171,162** | 54% → 74% |
| 31005-01 | 8 → **21** | 14 → **28** | 8,035 → 23,468 | 11% → 14% |
| 31065-01 | 22 → 23 | 53 → **63** | +3,331 | 85% → 86% |
| 31017-01 | 26 → 26 | 37 → 38 | unchanged | 65% → 65% |
| 31087-01 | 59 → 59 | 60 → 60 | +5 | 90% → 90% |
| 30838-01 | 42 → 42 | 77 → 77 | unchanged | 82% → 82% |

**Nothing lost on any set.** Prediction 2 held exactly: 30989-01's columns are 594 before and 594
after, and its walls fell 500 → 492 — the reinforcing sheets' walls staying refused, as intended.

⚠ Read the percentages with care. Where more storeys are now shared with her model the denominator
moves, so 30989-01's 54% → 74% and 31005-01's 11% → 14% are not like-for-like. **The honest number is
30993-01: the same shared storeys, the same 560,266 sq ft of hers, and ours 510,530 → 551,272 —
91% to 98%.**

## What would stop the bank

- Any column count rising (the 30990 fault returning).
- Either guard set losing a plate or area.
- Any storey losing a floor it had.
