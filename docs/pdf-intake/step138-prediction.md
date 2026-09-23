# Step 138's prediction, written BEFORE the judge ran (2026-09-23 13:35)

## What was measured first

Step 131 is net-negative over the 57 sets she has modelled: it earns ~99,000 sq ft on ten sets and loses
~325,000 on four, 321,957 of that on **30993-01 alone** (34% of her area against 91% with the knob off).

**30993 loses exactly nine sheets**, every one with the same signature — the floor plate gone, its seven
openings left as free-standing rings refused as linework, and the duplicate-edge count 2 → 11:

| page | sheet | storeys it serves | plate lost |
|---|---|---|---|
| p75 | S2.11.1 | L13 | 11,816 sq ft |
| p84 | S2.16.1 | L33–L34 | 11,802 |
| p80 | S2.14.1 | L23–L24 | 11,738 |
| p79 | S2.13.1 | L20–L22 | 11,727 |
| p85 | S2.17.1 | L35 | 10,110 |
| p81 | S2.15.1 | L27–L32 | 10,099 |
| p68 | S2.09.1 | L5 | 10,084 |
| p72 | S2.10.1 | L6–L12 | 9,931 |
| p76 | S2.12.1 | L14–L19 | 9,925 |

## The trace, both ways round, on one page of the loser and one of a winner

| | pieces | **walk found a floor** | largest ring | x extent |
|---|---|---|---|---|
| 30993 p68, 131 off | 108 | **False** | **10,084 sq ft**, 110 vertices | 109.0–216.0 ft |
| 30993 p68, 131 on | 115 | **True** | 9,094 sq ft, 98 vertices | **96.0–222.7 ft** |
| 30990 p30, 131 off | 83 | False | 516 sq ft | — |
| 30990 p30, 131 on | 129 | **False** | **11,150 sq ft** | — |

And the ON arm of 30993 is missing fifteen trace lines the OFF arm has — every one of the arrangement's own
diagnostics (cells, dangling ends, open chains, lone columns). **The arrangement never runs.**

## The fault, in one sentence

> `GeometryFilterService` line 1932 says the strokes on a grid axis are offered "to the ARRANGEMENT below, not
> to the walk". Line 2017 then bridges every PIECE into loops and adds them to the walk's own list, which is
> what `walkFoundAFloor` is computed from — and a walk floor makes the arrangement stand down entirely. So a
> ring closed out of INFERRED pieces silently outranks the arrangement's reading of what the drafter drew.

That is step-78-era code; step 131 simply put grid strokes among the pieces, and on 30993 the bridged pieces
close a 9,094 sq ft ring running out to the grid lines — six metres wider than the slab edge and a thousand
square feet smaller — while the arrangement's correct 10,084 sq ft floor is never built.

On 30990 the bridged pieces close nothing that stands in structure, the walk still finds no floor, the
arrangement runs, and 516 sq ft becomes 11,150. **That is why one set gains and the other loses.**

## Step 138: a floor the drafter drew stands the arrangement down; a floor bridged out of pieces does not

`walkFoundAFloor` is computed from the DRAWN walk's loops and the drawn paths only. The pieces' loops stay in
`loops` for everything else they are used for — they are still floors where nothing else reads one — but they
no longer, by themselves, stop the arrangement being built.

## The prediction

- **30993-01 recovers all nine sheets and 31 storeys**: ours 188,493 → about 510,450 sq ft, 34% → 91%.
- **30990-01 keeps its gain** (11,150 sq ft on p30 and p34 come from the arrangement, which this does not touch):
  ours stays near 139,777.
- **31087-01 keeps its 16,586**, for the same reason.
- **Step 131 stops being net-negative**: the corpus total goes from 4,519,496 sq ft (72%) to at least
  4,744,809 (76%) — the figure with 131 off — and should exceed it, because 131's ten winners keep their gains
  while its one catastrophe is gone.
- **No set loses.** The rule only ever makes the arrangement run MORE often; the arrangement's floor still has
  to pass the size, invention, thickness and neighbourhood gates it always did.

## Where the prediction would be wrong, and what each would mean

- **A set loses a floor**: somewhere a bridged-piece ring was the only reading, and standing the arrangement up
  beside it has cost the ring. The step 113/135 machinery already replaces a walk floor with the arrangement's
  where it supersedes it, so this would mean that replacement is not as complete as it looks.
- **30993 recovers but 30990 falls back to 516**: the gain I attributed to the arrangement is really the
  pieces' loops, and the whole reading above is wrong.
- **The corpus barely moves**: 30993 is the only set where the pieces' ring outranks a better arrangement, and
  the rule is right but worth one set rather than a class. That is still worth having, and it would be said.
