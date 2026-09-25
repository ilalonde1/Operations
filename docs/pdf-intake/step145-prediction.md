# Step 145 — a typical-floor range whose dash did not survive extraction

**Target set:** **16 of 297 corpus sets**, **61 views**, **778 interior storeys** currently
unreachable; **5 of the 16** have the engineer's model, and the two badly under-read among them —
31005-01 and 30989-01 — hold **120,469 sq ft** of measurable gap, 8.4% of the corpus's remaining
1,436,461. Measured from the real DXF view names **before the rule was written**.

## The fault

`S2.09.1_1_LEVEL 9 19 PLAN.dxf` draws **L9 through L19** — eleven storeys on one drawing. The dash
was lost on the way out of the PDF, so there is nothing between the numbers: `Range` needs a range
word, `LevelList` needs a comma or ampersand, and the title falls through to two `SingleLevel`
matches. L10 to L18 are never drawn.

The same drawing appears in the set's index series as `S0.00_14_LEVEL L09-19 PLAN CONCRETE OUTLINE`
— **with** the dash. That is the corroboration that it is a range and not a list.

**Why it hid:** at two wide the two readings are identical. `LEVEL L03-04` is 3 and 4 whether you
read endpoints or a range, and every narrow case in the corpus looked correct. Only a wide range
shows the difference, and then it is nine storeys at a time.

## The sets

| job | views | storeys lost | floored | reads |
|---|---:|---:|---|---:|
| 31103-01 | 4 | 120 | 22/56 | — |
| 30864-01 | 7 | 93 | 20/41 | — |
| 30820-01 | 2 | 72 | 14/55 | — |
| 30941-01 | 3 | 72 | 14/37 | — |
| 30892-01 | 4 | 58 | 21/59 | — |
| 31007-01 | 6 | 54 | 22/55 | — |
| 30972-01 | 4 | 52 | 20/20 | **85%** |
| 30694-01 | 4 | 42 | 38/63 | — |
| 30993-01 | 9 | 42 | 39/40 | **91%** |
| 30867-01 | 7 | 35 | 35/52 | — |
| 31005-01 | 2 | 18 | 8/24 | **11%** |
| 30989-01 | 1 | 14 | 7/27 | **54%** |

⭐ **Six of these are sets step 143 flags** for leaving half their ladder unfloored — 31103-01,
30864-01, 30820-01, 30941-01, 30892-01, 31007-01. **This fault is causing what 143 reports.**

## What is declined, and why

`LEVEL 7 25 PLAN ODD NUMBERS MARKET TOWER` — 30864-01, 31103-01 and 30972-01 draw alternating
floors, odd on one sheet and even on another. A contiguous expansion puts a floor on ten storeys
neither drawing serves. **A wrong floor is worse than a missing one**, because nothing about it asks
to be checked. Those are declined and go to the engineer as step 143's question.

Also declined: a gap of one. `LEVEL 6 7` reads as 6 and 7 either way, so there is nothing to win.

## The prediction

1. **31005-01 gains L10–L18** — nine storeys — and 30989-01 gains its range interior.
2. **30972-01 (85%, 20/20 floored) and 30993-01 (91%, 39/40) do not move.** They are already floored
   and are the guard sets: if either loses a storey or over-reads, the rule is wrong.
3. **The six-set gate stays byte-identical** unless a banked set holds a bare range.
4. No sheet whose title already read a level, a list or a proper range is touched — this is the last
   numeric reading tried, and only when every other one found nothing.

## What would stop the bank

- 30972-01 or 30993-01 moving at all.
- Any storey losing a plate.
- An ODD/EVEN sheet being expanded.
