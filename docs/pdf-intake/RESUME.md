# PDF intake — RESUME (state as of 2026-09-24 22:0X)

**Read this first.** It is the state, not the history. History is `part-3-s61-onward.md`.

## The one measure

**77.4%** of the engineer's plate area — 4,927,404 of 6,363,866 sq ft — across the **48** of 295
corpus sets where her own model can be compared (run 45, `ledger-sets-2026-09-24-run45-step140.csv`).

⚠ That comparison only exists from run 44 (2026-09-22). There is no earlier baseline; anything
claimed about "before" is not measurable.

## Banked today (2026-09-24), newest first

| commit | what |
|---|---|
| `d167d89b` | why no model came out, in the words of what was read |
| `696a9b98` | every refusal says WHY and HOW TO FIX IT + the gate |
| `f0863b90` | the outline was never missing — it is on BEAM |
| `7db0d436` | Codex brief: the outline sheet with no outline |
| `293d36b7` | every `EnclosedByWallPanels` null now states its reason |
| `834c21ad` | Codex brief: the floor the walls will not close |
| `acd927dc` | **step 145** — "LEVEL 9 19 PLAN" is eleven storeys + the reach gate |
| `bbdc91fc` | audit fix — a roof was being handed a storey number |
| `9f14289c` | **step 144** wired — a level from where the sheet sits |
| `c5f0f1d1` | **step 143** — the set that cannot say which storey, as question J8 |

**Step 145 is the win:** +239 storeys carrying a floor across 16 sets, **0 lost**. 31005-01 went
8 → 19 of 24 storeys, 14 → 26 floors, 204 → 375 walls, 103 → 237 columns.

## THE OPEN QUESTION — where 96% of the gap is

**1,379,518 of the 1,436,462 sq ft gap** sits in the **34 of 48** sets that have at least one storey
reading under half of hers — **72 storeys**.

### The diagnosis chain, run to the bottom on 30989-01 (27 storeys, 7 floors)

Each step killed the previous theory. **Do not re-walk these.**

1. ~~Closure refused the floor~~ → closure never ran; there was no slab to refuse.
2. ~~The drawing has no perimeter~~ → it has one; I rendered only the layers I expected.
3. ~~The outline is missing~~ → it is on `BEAM`, 1,756 entities, near-closed.
4. ~~Extraction lost it~~ → `pdf-inventory`: **931 paths `EmittedAsLine`, 0 slabs**.
5. ~~It fails to join at columns~~ → step 97 **joined 51 ends through 18 columns** and still failed.
6. **THE ACTUAL FAULT** — `pdf-overlay --walls` trace:

```
slab pass: arrangement 161 cell(s); columns in a cell 20 of 40
slab pass: cells by selection: holding structure 50 (413 sq ft), enclosed 20 (241 sq ft),
                               open to the page 91 (1419 sq ft)
slab pass: 0 cell(s) of 500 sq ft or more
slab pass: column at (264.8,171.6) ft is in no cell;
           the outside reaches it through a gap 2 in wide at (264.9,171.6) ft
slab pass: 847 of 847 lines offered; walk found a floor: False; floors 0
```

**The floor leaks out through a two-inch gap.** 91 of 161 cells are open to the page. It is a PINCH
between two line bodies, not an end near an end — `0 end(s) short of another edge's middle by under
the bridge` — so **widening the bridge cannot fix it.**

### Reproduce the whole chain with shipped verbs (no scratch code)

```
takeoff dxf-render    <view.dxf> out.png --layers BEAM
takeoff dxf-inspect   <view.dxf> --faces --layers BEAM
takeoff pdf-inventory <stickfile.pdf> --pages 21-21 --scale 96
takeoff pdf-overlay   <stickfile.pdf> 21 out.png --scale 96 --walls
```

Witness: `30989-01`, page 21, view `S2.15_1_LEVEL 4 19 PLAN CONCRETE OUTLINE.dxf`.

### ⚠ Measured DEAD — do not propose again

| candidate | measurement |
|---|---|
| widen the flood-fill bridge | 16,296 sq ft against a 583,781 sq ft gap — **2.8%** |
| "most of the storey's walls stand outside the plate" | precision never above **30%** at any threshold |
| relax `SlabChainJoinFraction` | biggest beneficiary is a set already at **106%** of her area |
| step 141 | earned nothing — sized against a pool step 140 had consumed |
| step 142 | earned nothing — and its "null means it does not close" was NOT established |

### The population, and what is NOT yet known

- **132 of 1,047** outline-titled views have no exported slab edge, across **30 of 297** sets.
- All 132 carry BEAM linework; **44 substantially** (500+ entities). **123 of 130 recover nothing**
  from BEAM alone under `dxf-inspect`.
- ⚠ **MEASURED 2026-09-24 22:4X — the pinch is 29%, not the story.** Categorised by
  `dxf-inspect --faces --layers BEAM` over all 132:

| signature | views | share |
|---|---:|---:|
| **C. LEAK — lots of linework, many bounded faces, no plate ≥400 sq ft** | **38** | 29% |
| **B. too little BEAM linework to be a perimeter at all** | **78** | **59%** |
| A. BEAM alone already recovers a plate | 7 | 5% |
| D. linework present, almost no bounded face | 7 | 5% |
| E. no face summary | 2 | 2% |

  The LEAK class is 8 sets: 30989-01 (6 views), 31224-01 (5), 30820-01 (4), 31065-01 (3),
  30852-01 (2), then one each in 01379-01, 30784-01, 30864-01. Of those, three have her model —
  30989-01 (54%), 31224-01 (23%), 31065-01 (85%).

  ⚠ **So the witness is in the MINORITY class.** Building pinch-sealing off 30989-01 alone would
  address 29% of a sub-population that is itself only part of the 96% gap. The 78 views that carry
  too little linework to be a perimeter are the bigger question and have no explanation yet:
  either the outline is on a sibling view of the same storey, or the page genuinely does not draw
  one there.

### ⭐ AND THEN THE SIBLING CHECK KILLED THE WHOLE THREAD (2026-09-24 23:0X)

For each of the 132, does another view of the SAME STOREY carry a slab edge?

| | views |
|---|---:|
| ✅ a sibling view does carry it — the empty view is harmless | **115 (87%)** |
| ❌ no sibling carries it — the storey is genuinely lost | **13 (10%)** |
| no storey readable from the title | 4 |

**The empty-outline population is 87% a non-problem.** The real loss is **13 views in 5 sets**:
31005-01 (5), 30941-01 (4), 31168-01 (2), 31007-01 (1), 31104-01 (1). The worst by linework is
`31104-01 S2.05.1_1_LEVEL 2 PLAN - CONCRETE OUTLINE.dxf` — 5,154 BEAM entities, fully drawn, no
sibling, storey lost.

⚠ **So `CODEX-THE-OUTLINE-SHEET-WITH-NO-OUTLINE.md` is answered and closed.** Its count was right
and its significance was wrong: 13 views, not 132, and that cannot be the 96%. Likewise the 38-view
pinch class — most of those have siblings too.

⚠ **THE 96% IS THEREFORE STILL UNLOCALISED.** It lives in the 72 storeys reading under half across
34 sets, and it is NOT mainly the empty-outline path. The next question has to start from those 72
storeys directly — what does each one HAVE — rather than from a sheet-level symptom.

### Guard sets — if either moves, the change is wrong

`30972-01` (20 of 20 storeys floored, 85% of her area) and `30993-01` (39 of 40, 91%).
⚠ 30993-01 has **21 empty outline views and still reads 91%**, because sibling views of the same
storey carry the edge. Any per-view warning would be wrong on it.

## Gates added today — these fail the build

- `EveryRuleStatesItsTargetSetBeforeItIsBankedTests` — a bisect knob must name a prediction doc
  stating **how many sets it touches, as a number**. Six knobs predate it and are listed; that list
  may only shrink.
- `EveryRefusalSaysHowToFixItTests` — a message saying something was lost must carry an action.
  One class is exempt and named: the perimeter-wall refusals, which cannot yet say what to do.

## ⚠ THE ERRORS I MADE TODAY, so they are not made again

All four are the same shape: **treating our own output as ground truth about the drawing.**

1. `null` from `EnclosedByWallPanels` read as "the union does not close". It has ten causes.
2. `0` entities on `KOR_C_SLABEDG` read as "the outline is missing". It was on `BEAM`.
3. Rendered only the layers I expected the answer on, and read the blank as the drawing's.
4. Generalised from one witness twice — the S0.00 index series, and "the outline is on BEAM"
   (true for ~44 of 132 views, not all).

Plus two mechanical ones: a new method wedged between a doc comment and its member **twice**
(`PlatesByStorey`, `SheetsResult`), and a sweep over 132 paths that all carried a trailing `\r`
and reported a confident, wholly wrong "0 sets".

## Next

1. Count how many of the 132 fail for the pinch reason (the trace prints it).
2. Only then decide whether sealing sub-N-inch pinches before the flood walk is safe — it is the
   shape of change that could invent floor by sealing a real opening.
