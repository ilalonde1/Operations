# Step 139 — the brackets say which of two kept sheets is the plan

**Target set:** measured over the run-44 sheet ledger — **5,018 sheet files across 265 sets**, of
which this rule changes the reading of the bracketed pairs only. Measured before the rule was written.

Written 2026-09-23 20:55 PDT, **before** anything was built. Judged against this file, not against a
reading of the result.

## The rule

Step 50 keeps a sheet a non-structural word would refuse when the name also carries a structural-plan
word. Step 80 stands that sheet down again when the set issues the same plan *plainly* as well.

31017-01 issues neither plainly — it puts the kind in brackets on both halves:

    S2.01.1_1_Foundation Plan Parking Level P2 - Tower A (Concrete Outline & Shear Reinforcing)
    S2.01.2_1_Foundation Plan Parking Level P2 - Tower A (Footing Reinforcing)

Both carry REINFORC, so step 50 keeps both by FOUNDATION PLAN in the stem, and neither is in the plain
list step 80 matches against. The rebar sheet is read as a floor.

Step 139: a kept sheet whose trailing `(...)` names a structural plan offers its **stem** as a plan too,
and a sibling on that stem whose own brackets say reinforcing is about it. Knob `KOR_STEP139_OFF`.

## Reach, measured before the rule was written

Over the run-44 sheet ledger: **5,018 sheet files across 265 sets**. Step 80 alone stands down 2. Step
139 stands down **2 more, both in 31017-01**:

    S2.01.2_1_Foundation Plan Parking Level P2 - Tower A (Footing Reinforcing)
    S2.03.2_1_Foundation Plan Parking Level P1 - Tower B (Footing Reinforcing)

So exactly one set in the corpus can change its judgement. (Measured with a replica of the classifier,
not the classifier — the run is what proves it.)

## What 31017-01 is now (develop, step 138 in, step 132 out)

| | |
|---|---|
| storeys built | 31 (37 floors) |
| storeys with a plate | 26 |
| plan sheets / placed | 30 / 26 |
| our plate area | 227,330.57 sq ft |
| her plate area | 347,987.09 sq ft — **65.3 %**, the largest gap in the corpus at 120,656 sq ft |
| columns / walls | 1,034 / 589 |
| our columns within 100 mm of hers | 176 of 683 |
| storeys where thickness agrees | 1 of 22 that both plate |

Its report also carries a 14,639,164 sq ft plate on P1 and 1,562,283 sq ft on P2 — both thrown away
downstream as "inside one already written" — and 240,992 drawing units of slab edge that would not close.

## The prediction

1. **31017-01 places two fewer sheets**: 26 → 24. Nothing else about which sheets are read changes.
2. **The flood-filled plates go.** The 14,639,164 sq ft plate on P1 and the 1,562,283 sq ft on P2 are the
   two rebar sheets' filled footings closing into one ring. They are discarded downstream already, so
   this is visible in the report, not in the plate figures.
3. **Columns fall on P1 and P2.** 30990's identical fault read 54 spurious columns from filled footings.
   I expect a drop of tens, not hundreds, and it should be confined to those two storeys.
4. **The unclosed slab edge falls by most of 240,992 units.**
5. **Our plate area does not fall**: ≥ 227,330.57 sq ft. If it falls materially, the rebar sheets were
   carrying real outline that the outline sheet does not, the reasoning is wrong, and step 139 is parked.
   I expect it flat or slightly up — the rebar sheets' own plates were being discarded anyway.
6. **Her comparison improves or holds**: columns within 100 mm ≥ 176 of a smaller "ours" total, because
   the spurious ones stood 1.8 m off in 30990's case.
7. **No other set in the corpus moves at all** — not one of the other 264.
8. **The six-set gate stays green.** None of the six issues a bracketed outline/rebar pair.

## What would stop the bank

- Our plate area on 31017-01 falls.
- Any set other than 31017-01 changes by a single figure.
- The six-set gate goes red.

## What this does NOT claim

Step 139 does not close 31017-01's 120,656 sq ft. That gap is mostly elsewhere: L3 reads 24,307 of her
58,282, L6 reads 0 of 13,144, `S2.24 Level 6 Plan Commercial` cannot be set on the grid at all, and 21 of
22 plated storeys carry the 12-inch default thickness against her 8 and 10 inch slabs. This rule removes
two sheets that are not floors. It is a prerequisite for reading that set, not the reading of it.
