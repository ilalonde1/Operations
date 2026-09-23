# Step 132's prediction, written BEFORE the judge ran (2026-09-23 15:32)

## What step 132 is, and why it was parked

*The exact-join chain walk runs both ways from its seed, through plain continuations.* Written 2026-09-19 and
parked the same night. From its own commit message:

> Closes 30990's LEVEL 5 riser (516 → 11,651 sq ft), 31065's L5 (0 → 8,539) and 31130 +3,300 — and loses
> 31202's ROOF: the walk then closes an 11,610 sq ft ring (hers 14,944) that the DXF side refuses as a thin or
> hooked shape under 55% of its box, where the banked walk's 8,736 passed. **The box-fill gate on hooked roofs
> is the next thing; this re-enters behind it.**

**That box-fill gate is step 136, removed and judged on 2026-09-23: no set lost, no over-read.** So 132's one
blocker is gone and "behind it" is where we now are.

## The base it is judged on

Rebased onto develop first, so **step 138 is in both arms**. That matters: 138 stops a ring bridged out of
pieces from standing the arrangement down, and 132 changes how the exact-join walk closes — if they interact,
judging 132 on a pre-138 base would measure the wrong thing.

As develop stands (138 on), the sets 132 targets:

| set | ours | hers | share | storeys / with a plate |
|---|---:|---:|---:|---|
| 30990-01 | 139,776 | 217,921 | **64%** | 25 / 25 |
| 31065-01 | 246,647 | 291,705 | 85% | 24 / 22 |
| 31130-01 | 250,285 | 277,356 | 90% | 24 / 24 |
| 31202-01 | — | — | — | **no longer in the census** (its stick file was renamed 2026-09-21) |

30990 is the single biggest miss in the whole corpus: 78,145 sq ft short of her, 5% of the total gap.

## The prediction

- **30990-01 gains, and it is the point of the exercise.** The parked commit measured its LEVEL 5 riser at
  516 → 11,651 sq ft on a page serving four typical floors. Expect ordered tens of thousands of square feet
  across those storeys, and 64% to move up materially — to 75–85% if the riser is the whole story.
- **31065-01 gains its L5**, 0 → 8,539 sq ft on the page, some multiple of that across storeys; 85% → high 80s.
- **31130-01 gains about 3,300 sq ft**; 90% → 91%. It is a six-set gate set, so **the six-set gate must also
  stay byte-identical or be re-banked deliberately** — this rule changes what the walk closes, so a move there
  is expected and must be read, not waved through.
- **The corpus total rises from 4,840,310 sq ft (77%)** by roughly the sum of the above.
- ⚠ **31202's ROOF cannot be judged here** — the set is out of the census. Its ROOF is, as banked, FOUR separate
  plates with a peaked outline where every storey below is one clean rectangle (rendered 2026-09-23 13:45). If
  the rest of the corpus comes back clean, **build 31202 by name** and look at that storey before calling the
  blocker gone. The gate's word is not enough for the set the gate cannot see.

## Where the prediction would be wrong, and what each would mean

- **Nothing moves anywhere**: the knob is not reaching the rule, or the rebase dropped it. The arms are full
  reads of one binary, so this would be a wiring fault, not a neutral rule.
- **A set loses**: the both-ways walk took an edge from a ring another seed would have closed. The parked
  commit already measured that shape once — "the DXF route's benchmark lost 5 of 8 openings to a backward walk
  that chose at junctions" — and scoped the rule to plain continuations to avoid it. A loss means the scoping
  is not tight enough, and the losing set names where.
- **30990 gains far MORE than the riser**: the rule reached further than its measurement, which is the shape
  that cost 31202 its ROOF the first time. Worth understanding before banking, not celebrating.
- **31130 moves and the six-set gate goes red**: expected, and the baselines are re-banked only after the
  corpus judgement says the movement is a gain on her figures.
