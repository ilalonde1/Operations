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
| f5a74e04 step 170 repaired + 173 | 26 storeys | **Regression on 31168 C-L3.** 8fa0d9d6 had closed C-L3's floor to the full outline (23,199 sq ft; her C-LEVEL 3 is 31,473 over the same outline). f5a74e04 cut it back to the bank's inner rectangle (11,885), which leaves the left and bottom bays of columns off the floor. Today's build still has it, and so does the bank. The rest are **fixes**: 31130 L1 back to 33,110 against her 55,644; 31130 L15 20,236 → 18,878 against her 10,435. | rendered C-L3 at both commits and hers |
| cd7dc0de steps 175–177 | 5 storeys | **Neutral.** 31202 ROOF 8,736 → 9,683; thickness shares; 1 column on 31202 L2. | numbers |
| 674b2890 steps 178–180 | 7 storeys | **Fix.** It undoes 8ed10d03's plate moves: 31065 L1 back to 32,792 against her 58,968, and 31170 L1/L5/L6 back to the bank. The column moves (31065 L1: 14; 31138 L2: 3; 31138 L20: 1) are step 178's turns; **not looked at**. | numbers |
| 7240315b step 182 | 2 storeys | **Fix.** 31130 P2: 12"/10" → 10", against her 10" on 49,264 sq ft. 31138 L22: 12" → 10", against her 8"/10". | her thicknesses |
| 21fb2b10 / 0de30b47 / ea108775 step 183 | 27 / 24 / 6 | **Mostly a fix, with over-zoning.** Zones that match hers: 31138 L2 46"/52"; 31130 L14 16"; 31130 L16 18"/10"/12"; 31065 L1 35.4"; 31168 L1 30"/36"/56"; 31168 L2 14"/18"/48"/36"/30". **Regressions:** on 31138 L21 the whole interior is 24" (7,215 sq ft) where hers nests 8"/12"/24" (24" on 2,613); on 31202 L13, 12" covers 8,738 sq ft where hers has 1,128. **Candidates not yet decided:** 31065 L2 35.4" (3,097; hers none); 31065 P1 5.9" (1,726; hers none); 31130 L2 9" (4,290; hers none); 31130 L20 18" (her storey not matched). | her thickness mix; rendered 31138 L21, 31202 L13 |

All of 9055a41e, 9670a49e (step 184) and 86a2e160 moved 0 storeys on the six sets.

## What a re-bank would bank

- **Faults the bank never had:** step 183's over-zoning on 31138 L21 and 31202 L13, plus the four undecided candidates.
- **A fault the bank had, still there:** 31168 C-L3's floor short of its columns.
- **Never judged:** the member moves listed as "not looked at" above.

## What this does not cover

Plate position at the same size, member sections, beams, loads, and storeys the engineer named another way.
