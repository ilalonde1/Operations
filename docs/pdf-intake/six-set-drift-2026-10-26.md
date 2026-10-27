# Six banked sets: what moved since the bank, commit by commit (2026-10-26)

`SixSetsBuildAsBankedTests` has been red since 231e0f69. Before anything is re-banked, every storey that moved was traced to the
commit that moved it, and each commit was read against the engineer's model. **Nothing has been re-banked.** That waits until
Ian or operations-ee has read this.

**How.** `tools/CorpusGateBisect/attribute_baseline_drift.py walk` built the six sets at each of the 31 Core/CLI commits since
73458eec, in order, in Debug, on the test's route (PdfOnlyBuild + KorStandards). There were 0 failed builds; the ledger has 186 rows (31 × 6).
`... attribute` then produced the table: [six-set-drift-2026-10-26.txt](six-set-drift-2026-10-26.txt). Its first part is the per-storey
table. Its second part lists what each commit changed against the commit before.

The base is 73458eec, which is the bank's geometry. A Debug build at 73458eec equals the bank except for LINEASSIGN, and that difference is
exactly 231e0f69's ANG patch; all 6 of 6 sets were checked.

**Result.** 67 storeys moved, and 67 of 67 have a first commit. 13 of the 31 commits moved something; the other 18 moved nothing.

## Read this before reading the table

Two of the six sets carry **two buildings** where the engineer modelled one: 31065 and 31130. The table compares per-storey totals,
so a second building's floor that closes reads as a plate "growing" away from hers. That happens on 31065 L2, L5 and L7–L17, and on
31130 L2. Each of these was rendered (`takeoff model-render --storey`) beside her model. In every case the extra area is the second
building's floor, which the bank either lacked or had with a hole in it.

## Verdict per commit (13 of 31 moved something)

| Commit | What | Verdict | How it was judged |
|---|---|---|---|
| 8ed10d03 steps 149–168 | 27 storeys | **Mixed.** The plate moves were all undone later by 674b2890 (31065 L1, 31170 L1/L5/L6). The roof split on 31202 (ROOF 12,160 → ROOF 8,736 + UPPER ROOF 3,423) is a **fix**: her model has an upper-roof storey ("L15 UPPER ROOF"). The member moves that last (31170 L3–L7: 6 walls each; 31138 L2: 12 columns; 31065 L2: 8 columns) have **not been looked at**. | rendered 31202 roofs; her storey list |
| 06afccff steps 150/151/162/167 | 9 storeys | **Fix.** It puts back the one column per storey that 8ed10d03 moved on 31202 L7–L13 and ROOF. Net: none of those storeys differ from the bank in members. | net column |
| d2cec177 step 169 | 3 storeys | **Not looked at.** 31168 L10/L11: 1 wall each. 31202 ROOF: 30 walls and 3 columns (12 walls net to the bank). | — |
| 8fa0d9d6 steps 170–172 | 48 storeys | **Fix (the bank's error)** on what was rendered: 31065's second tower gets its floor on L5 and L7–L17 (the bank had only a core sliver there); 31130 L2's floor hole is closed; 31138 L3/L4 move to 12,715 against her 12,718; 31202 L7–L12 move 28,585 → 28,921 against her 34,635. The wall counts on 31065 L1/P1/P2 and 31130 L1/P2/L0-P1 (14–32 walls each) have **not been looked at**. | rendered 31065, 31130 L2; numbers |
| f5a74e04 step 170 repaired + 173 | 26 storeys | **Regression on 31168 C-L3.** 8fa0d9d6 had closed C-L3's floor to the full outline (23,199 sq ft). ⚠ The first reading quoted "her C-LEVEL 3 at 31,473 over the same outline", but its outline plate is KF84, this tool's own earlier output in 31168's yardstick. The verdict stands on the drawing instead: S2.40.1 (p30, looked at) runs the level 3 slab to the outer outline, printing 14" in the west bays and the south strip. f5a74e04 cut it back to the bank's inner rectangle (11,885), which leaves the left and bottom bays of columns off the floor. Today's build still has it, and so does the bank. The rest are **fixes**: 31130 L1 back to 33,110 against her 55,644; 31130 L15 20,236 → 18,878 against her 10,435. | rendered C-L3 at both commits and hers |
| cd7dc0de steps 175–177 | 5 storeys | **Neutral.** 31202 ROOF 8,736 → 9,683; thickness shares; 1 column on 31202 L2. | numbers |
| 674b2890 steps 178–180 | 7 storeys | **Fix.** It undoes 8ed10d03's plate moves: 31065 L1 back to 32,792 against her 58,968, and 31170 L1/L5/L6 back to the bank. The column moves (31065 L1: 14; 31138 L2: 3; 31138 L20: 1) are step 178's turns; **not looked at**. | numbers |
| 7240315b step 182 | 2 storeys | **Fix.** 31130 P2: 12"/10" → 10", against her 10" on 49,264 sq ft. 31138 L22: 12" → 10", against her 8"/10". | her thicknesses |
| 21fb2b10 / 0de30b47 / ea108775 step 183 | 27 / 24 / 6 | **Mostly a fix, with over-zoning.** Zones that match hers: 31138 L2 46"/52"; 31130 L14 16"; 31130 L16 18"/10"/12"; 31065 L1 35.4". (31168 L1/L2 were listed here too, but read through 31168's yardstick, which holds this tool's own earlier output; judge_drift_moves.py now drops it, and 31168 is not judged against "hers".) **Regressions:** on 31138 L21 the whole interior is 24" (7,215 sq ft) where hers nests 8"/12"/24" (24" on 2,613); on 31202 L13, 12" covers 8,738 sq ft where hers has 1,128 (⚠ CORRECTED below: 31202 L13 follows the drawing, which prints three 12" areas; only 31138 L21 is a regression). **Candidates not yet decided:** 31065 L2 35.4" (3,097; hers none); 31065 P1 5.9" (1,726; hers none); 31130 L2 9" (4,290; hers none); 31130 L20 18" (her storey not matched). | her thickness mix; rendered 31138 L21, 31202 L13 |

All of 9055a41e, 9670a49e (step 184) and 86a2e160 moved 0 storeys on the six sets.

## What a re-bank would bank

- **Faults the bank never had:** step 183's over-zoning on 31138 L21 (fixed: "a nest is not an island") and on 31065 P1 (the 150 box, open), plus the undecided candidates. 31202 L13 is not one: it follows the drawing (see the correction under the member moves).
- **A fault the bank had, still there:** 31168 C-L3's floor short of its columns.
- **Never judged:** the member moves listed as "not looked at" above.

## The member moves, judged (operations-ee's item 1, 2026-10-26 evening)

**How.** Each move was judged with `tools/CorpusGateBisect/judge_drift_moves.py members|zones`; the raw output is
[six-set-drift-2026-10-26-members.txt](six-set-drift-2026-10-26-members.txt). The engineer's model was moved into our frame by
model-yardstick's column registration. Registration held on the judged storeys: for example, 50 of our 51 columns land on hers
on 31170 L3, and 24 of 40 on 31138 L2.

**Model-diff's counts overstate moves.** A member re-placed by 100 mm reads as one lost and one gained, so "walls 6" was 3 walls
moved. The tool pairs each lost member with a gained one of its kind within 1,000 mm and scores the pair against her model.
Renders and overlays are in [six-set-drift-2026-10-26/](six-set-drift-2026-10-26/).

| Storey @ commit | What actually moved | Verdict |
|---|---|---|
| 31170 L3–L7 @ 8ed10d03 | 3 walls per storey, each moved 76–127 mm. Against hers, 13 pairs are the same within 50 mm and 2 are further by more than 50 mm; none are lost or gained. | **Neutral.** |
| 31138 L2 @ 8ed10d03 | 12 duplicate columns removed: each had a twin 29–695 mm away, which still stands. One column per spot is what hers has (her 25 columns, our 52 before and 40 after). For 5 of the 12, the twin kept is nearer hers: 152 → 30, 634 → 30, 411 → 74, 528 → 1 and 77 → 59 mm. For 3, it is further: 0 → 342, 2 → 255 and 2 → 498 mm. The other 4 are not near hers either way. It also added 1 wall 6.6 m from any of hers. Overlay: `ov-31138-L2-8ed.png`. | **Fix (the bank had duplicates), with a fault: 3 of 12 kept the wrong twin.** |
| 31065 L2 @ 8ed10d03 | 1 of 8 columns was a duplicate whose twin kept is further from hers (89 → 402 mm). The other 7 were removed on the second tower, 46–58 m from anything of hers; at L2 her model covers one tower only. | **Wrong twin on 1.** The 7: **undecided, outside her model.** |
| 31202 ROOF @ d2cec177 | Mostly storey re-spanning (model-diff's "where the members went"): upper-roof walls no longer reach ROOF, and penthouse walls and columns now run down into it. Neither the walls before nor the walls after are within about 2 m of her 11 core walls. That fault is older than this commit, and the swap is the same either side. Of 3 columns carried down, 1 lands on hers and 2 (at y ≈ 61 m) are 15 m from any of hers. Overlay: `ov-31202-ROOF-d2c.png`. | **Mixed: 1 column fixed, 2 columns added that she lacks.** The roof walls stay a standing fault. |
| 31065 L1 / P1 / P2 @ 8fa0d9d6 | Walls re-trimmed ("a slanted wall's clip"). 24 re-placed: 5 closer to hers, 3 further, 16 the same. Unpaired: L1 lost 2 columns and 2 short walls, each 1.0–2.1 m from any of hers (fix); P2 gained 2 walls (8.3 m and 0.7 m long), 7.3 m and 4.9 m from any of hers. | **Fix overall; the 2 P2 walls are regression candidates.** |
| 31130 L1 / P2 / L0-P1 @ 8fa0d9d6 | The same re-trimming. 30 re-placed: 16 closer to hers, 7 further, 7 the same. The unpaired moves are short walls 0.2–1.5 m long, 0.3–1.5 m from hers, plus 1 column lost that is 6.2 m from any of hers. | **Fix overall.** |
| 31065 L1 @ 674b2890 | 12 columns now stop at L2 instead of running down through L1. Her L1 has no column within 1.9–5.0 m of any of them. | **Fix.** |
| 31065 P1 @ 674b2890 | 1 column gained 10 mm from hers. | **Fix.** |
| 31138 L2 @ 674b2890 | The column on hers (30 mm) is swapped for one 604 mm away (634 mm from hers). | **Regression.** |
| 31138 L20 @ 674b2890 | 1 column gained 5.9 m from any of hers. | **Regression candidate.** |

**The four undecided step-183 zones** (`judge_drift_moves.py zones`: does the zone's centroid fall inside one of her plates, and at what thickness):

| Zone | Where it sits | Verdict |
|---|---|---|
| 31065 P1 5.9" (1,726 sq ft) | Inside her 9.8" plate. The bank had 9.8" there. | **Regression.** |
| 31130 L2 9" (4,290 sq ft) | Inside her 36" plate. | **Regression** (thinner than hers). |
| 31065 L2 35.4" (826 + 2,271 sq ft) | Outside every plate of hers. | **Undecided**: a part of the set her model does not cover at L2. |
| 31130 L20 18" (6,818 sq ft) | Her model has no L20 (her top is L18 ROOF). | **Undecided.** |

**CORRECTED the same evening, after looking at the drawings.** "Inside her plate" cannot see an engineer's overlay, and it does not ask what the drawing prints. Per storey:

- **31202 L13 is NOT a regression.** S2.09.1 (p35) outlines and prints three `12" P/T SLAB` areas (the pool box, the hatched east bay, the south-west area) beside an 8" field and a 10½" area. Ours follows the drawing; the engineer models 10" throughout. step183-prediction.md had already recorded this, and the first reading above missed it.
- **31138 L21 IS one.** S2.36 (p44) nests an 8" edge ring, a 12" band, a 24" box and an 8" core, and her model nests the same. The slab-pass trace says why ours was 24" over the band: "7184 sq ft printing 12 … taken into the zone round them". Fixed as its own commit (step 183, "a nest is not an island").
- **31065 P1 5.9" is a different fault.** S2.03.2.1 (p22) prints `150 SLAB` in a box about 6 × 2 m, bounded by a 150 wall. The box was not read as a boundary, so its call-out labelled a 1,726 sq ft face. **Regression, not fixed tonight.**
- **31130 L2 9" is a different question.** The 9" call-out is on the east tower's L2 sheet (S2.14.1, p33). The zone sits where her model has the west tower's 36" transfer (S2.05.1, p21). That is placement or attribution, not zoning. **Undecided.**

**A new class, found at two commits.** When the same column is drawn twice, the build keeps one of the two. That is right; hers has one. But nothing decides which of the two is kept: 31138 L2 has 3 of 12 further from hers, 31065 L2 has 1, and 674b2890 swapped one more on 31138 L2. The invariant: the column kept is the one the drawing's grid and the storeys above and below agree on. This is not fixed. It is raised to operations-ee as a candidate for its own step.

**The check exists (rule 11), the fix does not yet:** `tools/CorpusGateBisect/column_stack_near_miss.py`. A column whose stack above or below misses it by 100–700 mm is a near-miss, and against the engineer's model it is a WRONG TWIN when it misses hers by about what the stack misses. On the six sets (506be566): 11 WRONG TWIN across 31138 L2 (3 of the 4 known instances), 31065 L2, 31130 L14 and 31170-arch L7. It misses two instances: the known 31065 L2 one, with no column above or below it, and 31138's fourth, which reads OFF HERS. The tool's header says what it covers and what it does not. The fix comes in daylight, with this as its gate.

## What this does not cover

Plate position at the same size, member sections, beams, loads, and storeys the engineer named another way.
