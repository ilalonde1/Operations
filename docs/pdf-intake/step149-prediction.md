# Step 149 — a symbol's triangle is three corners, not three points

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the corpus gate below has judged it.

The rule was built and measured on one witness before this doc existed — the order
`EveryRuleStatesItsTargetSetBeforeItIsBankedTests` exists to stop. Recorded so the gate, not the witness, decides.

**Knob:** `KOR_STEP149_OFF=1` restores step 58's point count alone (`GeometryFilterService`, the column branch).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel, an architect's set, not in the corpus); corpus reach
unmeasured — 0 of 297 counted, and 0 of the 48 with the engineer's model. The gate run below is the count.

## The witness

143 E 3rd Hotel, `Desktop\Omar\…KOR prelim markup.pdf`, an architect's set (Formosis). Every plan carries section and
elevation markers whose filled arrowhead's third side follows its bubble: five points, three corners —

    (1758.4,1568.2) (1770.5,1580.3) (1770.5,1572.8) (1766.1,1572.3) (1762.0,1570.7)   page 6, 12 x 12 pt

Step 58 discards a filled shape of **fewer than four points**. This one has five, so it became a column.

| storey | columns before | outside every plate before | after | outside after |
|---|---:|---:|---:|---:|
| L8–L35 (typical, each) | 33 | 20 | 11 | 0 |
| whole model | 1,698 | — | 626 | — |

All 20 of 20 marked on the sheet were marker arrowheads (rendered: `scratchpad/omar-stickfile-rules/l35-marked.png`).

## What the change is

A corner turning more than 30° is sharp. A filled shape with exactly three sharp corners is a symbol's triangle,
however its sides are drawn. Rectangle 4, round column 0, L 6, octagon 8 — no column shape has three. Step 61's
twin triangles are paired before this branch and are untouched (fixture rows 221-227 hold).

Side effect, intended by step 1870's cut-line rule: a recognised arrowhead also lets the section-cut line ending at
it be dropped. On the witness one 4.9 m strip plate left L1–L6 with it.

## What must NOT move

- The six banked sets: byte-identical, unless a structural set's markers were columns too — then her column count
  decides, and ours may only move TOWARD it.
- Columns the engineer has: on the 48, our columns must never fall below hers on any storey where they did not before.

## The measurement that decides it

    takeoff corpus-gate --bisect KOR_STEP149_OFF

1. Columns per storey against hers — must not drop below.
2. Her plate area we reproduce — must not fall (the cut-line side effect is the risk).
3. The six-set gate.

⛔ Run where the drawings are local. Never over the VPN.

## Judged, 2026-10-21 (corpus gate run 24b09de9, 57 sets; six-set attribution by knob)

- **Corpus gate:** 1 set of 56 lost to this rule - 30972-01, her plate we read 209,434 -> 203,254 sq ft of 246,123, all of
  it on L1 (20,951 -> 14,771 of her 39,292). Her openings we have rose 28 -> 32 of 45.
- **Looked at** (L1 rendered on, off and hers, `model-render --storey L1`): with the rule on, the east wing her model
  slabs is read and the upper-left, which her model leaves open under one large void, is no longer covered. That is
  closer to hers in shape than the off build. What is wrong: a diagonal plate edge across the lower-left middle, where
  her slab runs on - the cut-line side effect this doc warned of, cutting a plate that should stand.
- **Six sets:** 31065 plate 85% -> 81% of hers and one opening fewer; 31170 173% -> 182% (already oversize).
- **Verdict:** KEPT, not banked clean. The plate loss on 30972 is mostly a void we no longer cover; the diagonal edge and
  the 31065 drop are open and are this rule's to answer.
