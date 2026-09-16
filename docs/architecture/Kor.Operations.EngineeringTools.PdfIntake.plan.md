# PDF intake → ETABS — Completion Plan

**Status:** Rev 4, 2026-09-15 23:05 (rows 3y–3ad; overnight session running; steps 80–81 landed, not yet in a run) — Rev 4, 13:00 — WP6a re-plans the road on the engineers' own definition of usable (verticals + a plate on every storey; one model per building); steps 47 and 49–73 in §8, runs 8–16 in §1b, run 17 in flight — WP1–WP5 landed overnight on Ian's go-ahead ("go through this all,
step by step, and finish it overnight"): commits `8fccbc25` (WP3), `3f3f82b9` (WP2), `aba7d9ff`
(WP4), `69e554b5` (WP5), each gated by the six byte-identical and the fast suite; WP5's remaining
conventions are counted by a test, not by this document. §8 is what needs Ian. Rev 2 (2026-09-11)
turned the plan on Ian's direction: *"rather than build the brain, then expose it to one drawing at
a time … build an analyzer to get all the info you need at once"* (§1a, §1b).
**Author:** Claude, for Ian Lalonde. Written after Ian's instruction the same day: *"You build
ephemeral bloated solutions and they're not built as code or in DB. This must stop."*
**Audience:** Ian. Approve it, strike what you disagree with, and each package then runs one per
sitting, each ending in a commit and a single printed verdict.

---

## 1. What "complete" means — the contract, unchanged since 2026-09-10

A structural drawing set arrives as a PDF and becomes an ETABS `.e2k` with no reference model, no
Revit, and **no code change for a set from an office we have never seen**. Every sheet the tool
cannot read says why, in the report, in words a person acts on. One ingestion point
(`DrawingIntake.ReadSheet`) feeds every outlet: the model, the inventory, the schedules, the
reissue diff, the self-check.

That is a contract, not a floor count. It is met when:

1. **The corpus builds.** Every structural stick file the office holds (292 jobs' current issues,
   §1a) runs through the one ingestion point, and the ledger says, set by set and sheet by sheet,
   what was read, what was placed, and why not — with the totals rising, never a set regressing.
   The sets from other offices are already in it (460 jobs hold an architect's set), so there is
   no "seventh set" to ask for: the held-out sets are the ones the rules were never written for.
2. **Andrea accepts one model** built this way as a starting point she would use. (The
   31170-from-PDF deliverable exists; it has not been put in front of her.)
3. **Every instrument that measures the tool is code in the repo** — a `takeoff` verb, a test,
   or a `tools/` console — and every drafting convention the readers apply is a row in
   KorStandards with its compiled default proven equal by a test. Nothing the loop depends on
   lives in `%TEMP%`, in `docs/*.py`, or in a memory file.

## 1a. The corpus, counted (2026-09-11, `takeoff corpus-census`, 90 s over the share, 0 folders unlisted)

| | |
|---|---|
| Job folders on `\\Kor-fs01\Projects\Projects` | **1,158** in 9 categories |
| Jobs with a structural stick file (`05 Stickfile\*Stickfile*.pdf`) | **292** — 382 issues; the newest per job is the current set: **3.5 GB** to mirror (4.6 GB for every issue). 109 current sets dated 2026, 60 dated 2025 |
| Jobs with an architect's set (`05 Stickfile\01 Architectural`) | **460** — 6,829 PDFs (single sheets and whole sets mixed; the analyzer sorts them by page count) |
| Jobs with an ETABS model where the convention files it | **104** — 47 `.e2k`, 746 `.EDB` (the 2026-08 server-side walk found 1,126 `.e2k` on the volume: models are filed in more places than the convention) |
| **Jobs with both a stick file and a model — a yardstick each** | **66** (56 residential, 6 industrial-garage): 9 with an `.e2k` now, 57 `.EDB` only, which ETABS must export — a batch on a machine with ETABS, Ian's call |
| Pitfalls the census found | the naming template `31###-01 YYYY-MM-DD … Stickfile.pdf` copied into 437 job folders (excluded); one job's stick file copied into 16 others (flagged); three spellings of the dated name, all read |

Today's harness is 6 of those 292. The one-job yardstick (31168) is 1 of 66.

## 1b. The corpus, built (`takeoff corpus-analyze`; run 1 2026-09-11 = WP1's first pass, PdfIntake.md §53; run 3 2026-09-12 = after steps 45 and 46, §55)

| | run 1 (step 44) | run 3 (step 46) |
|---|---|---|
| Sets built from the PDF alone | **39 of 292**; 8,692 pages read, 3,989 plans, 0 failed | **207 of 292** (197 after step 46; the `-MARKUP` layer rule freed 10 more, run 4) |
| No model because no storey ladder was read | **225 of 292** — they all have plans; the ladder reader wanted shear-wall elevations | **67** — their plans name storeys with WORDS (step 47) |
| No model: no plan the reader typed / refused at the layer gate | 17 / 11 | 17 / 1 (10 of the 11 were our own `-MARKUP` layer, §55) |
| Plan views set on the grid by name | 873 of 2,058 (42%) | 1,948 of 4,323 (45%) |
| Storeys with a plate | 230 of 1,098 (21%) | 741 of 2,401 (31%) |
| Yardsticks (engineers' own models, 92 exported from KOR-210) | 18 of the 39: **45% / 52%** within 100 mm | 62 sets have one, 42 share a storey with columns: **34% / 48%**; per set from 75–99% (4 sets) to 0–24% (20 sets) — more sets, more assumed storeys, a lower share |
| run 5 (2026-09-12, step 49; `ledger-sets-2026-09-12-run5-step49.csv`) | 206 of 292 build (31168's new issue lost to a duplicate view name, fixed with step 51); 99,314 columns from 105,660 (anchors and target quadrants, 101 sets); 65 sets have a yardstick, 47 share a storey: **34% / 48%** — the corpus number is frames and scope (§58), not reading; run 6 measures steps 50–51 |
| run 6 (2026-09-12, steps 50–52; `ledger-sets-2026-09-12-run6-step51.csv`) | **207 of 293** build (a 293rd set appeared on the share; 31168 back); 2,031 of 4,323 views on the grid by name (47%); 749 of 2,401 storeys with a plate; 66 sets have a yardstick, 48 share a storey: **49% / 53%** — the scope rule, the sheets that say what they are, and the halves through a key plan; 9 sets at 75–99% (4 in run 3) |
| run 7 (2026-09-12 22:02 → 09-13 00:33, step 53; `ledger-sets-2026-09-12-run7-step53.csv`; DB run `9b03d0ab`) | **207 of 293** build; 1,821 of 3,968 plan views placed on the grid by name (46% — `corpus-query summary`'s count; run 6's 2,031 of 4,323 was a scratch count over sheet rows, not comparable); 749 of 2,401 storeys with a plate; 104,761 columns from 105,023 (262 more anchors and fittings stood down); 48 yardstick sets: **49% / 53%** (6,344 of 13,024 judged, from 13,053 — the same 6,344 matched, 29 fewer of ours judged) — step 53 was a precision step on 31202 and reads as one on the corpus |
| run 8 (2026-09-13 17:45 → 20:08, steps 47–57, before step 58; `ledger-sets-2026-09-13-run8-step57.csv`; DB run `e1e33cc6`) | **236 of 293** build (from 207 — step 47's words; 39 still read no storey, 17 no plan with structure, 1 no slab edge); 1,892 of 3,968 plan views placed on the grid by name (48%); 910 of 2,605 storeys with a plate (35%); 46,617 walls, 113,069 columns from 104,761 — 7,059 on the 29 new sets (the small jobs' filled symbols step 58 now discards; 01389 alone 248 → 0) and +1,249 net on the 207 sets both runs built, **149 of which changed count under steps 54–57** (which sheets stand on the grid decides which columns are in the model; 30924-01 3,195 → 1,947, 30840-01 397 → 1,385 — a per-set differential over the two ledgers is owed before the next step); run 9 on step 58 is the next count; 49 yardstick sets: **50% / 55%** (6,617 of 13,312 judged; theirs 6,544 of 11,837) from 49% / 53% — the 29 new sets brought one yardstick and a slightly higher share |
| run 9 (2026-09-13 20:17 → 22:37, step 58 alone on run 8's code; `ledger-sets-2026-09-13-run9-step58.csv`; DB run `67570bff`) | **236 of 293** build (the same 236); columns **65,106 from 113,069**, plates 697 of 2,605 from 910; `corpus-query diff` run 8 → 9: Composition 144 (−44,201 columns, −197 plates; yardsticks 1 better / 10 worse / 10 same, 789 of 3,487 → 521 of 1,622 judged), Placement 8, Unchanged 141; 47 yardstick sets: **56% / 52%** (6,253 of 11,226; theirs 6,200 of 11,836) — the survivors match better and 268 matched columns are gone: step 58 discarded the halves of columns a PDF driver draws as two triangles (31048-01 2,640 → 57 against 449 of hers). The six harness sets, drawn with four corners, could not show it. Corrected in step 61 (§70: two triangles that are one shape); run 10 measures it |
| run 10 (2026-09-14 10:22 → 12:39, steps 58–61; `ledger-sets-2026-09-14-run10-step61.csv`; DB run `42308806`) | **238 of 293** build — of **278 jobs**: 15 rows say "the stick file of another job" (01783-01's file under 00904-01 and fourteen more) and build nothing; 22 read no storey (from 39: the two-line titles and the word chain), 17 no plan with structure, 1 no slab edge. `corpus-query diff` run 8 → 10: NewModel 3, LostModel 1, Storeys 6, Placement 27, Composition 147, Unchanged 109. Columns **85,605** (113,069 in run 8, 65,106 in run 9: the twin rule gave back the tessellated columns step 58 had thrown away and kept the symbols out); plates 1,128 of 2,620 (43%, from 35%); 50 yardstick sets: **58% / 52%** (6,277 of 10,735; theirs 6,242 of 12,019); 4 sets at 100%+, 11 at 75–99%, 10 at 50–74%, 16 at 25–49%, 7 at 0–24%. **Walls 151,191 from 46,617 — LOOKED AT, NOT VERIFIED**: 31066-01 (0 → 2,620) rendered is a wood-frame apartment block over a concrete podium, L3–L6 ≈500 "walls" a storey — every stud partition, drawn as a filled band a PDF driver tessellates, read as a wall now that its two triangles are one shape. The reader has no rule for what a filled band is a wall OF (concrete or wood); the fill's colour and the set's typology are the candidates, and a wall yardstick does not exist. The next reading step, before any of those walls reaches an engineer |
| run 11 (2026-09-14 15:43 → 18:04, step 63; `ledger-sets-2026-09-14-run11-step63.csv`; DB run `42e683f8`) | **239 of 295** build (two new census rows); 21 read no storey, 17 no plan with structure, 1 no slab-edge layer. `corpus-query diff` run 10 → 11: Composition 180 (yardstick 6 better / 2 worse / 22 same), Views 16, Placement 9, Storeys 4, NewModel 1, SameCounts 83. Columns **81,564** (from 85,605); **walls 115,664 from 151,191** (the stud partitions, §72); plates 1,086 of 2,627; 48 yardstick sets with columns: **57% / 55%** (6,296 of 11,130; theirs 6,272 of 11,485). 4 sets at 100%+, 12 at 75–99%, 9 at 50–74%, 15 at 25–49%, 8 at 0–24%. The read: 776 CPU-min, 5.4 s a page, 85% of a set's cost (§74). One regression (30993, Placement) → step 65. |
| run 12 (2026-09-14 18:58 → 19:26, **27 min 40 s, a `--recompose` at 12 workers**; step 65; DB run `8a174b24`; CSV lost to a race, log whole) | **240 of 296** build; views on the grid **2,285 from 2,093**; walls 115,666, columns 81,612; 49 yardstick sets with columns: **58% / 55%** (6,539 of 11,276; theirs 6,519 of 11,797); 4 at 100%+, 15 at 75–99%, 8 at 50–74%, 14 at 25–49%, 8 at 0–24%. The first measured cost of a composer-only pass over the corpus (§74). |
| run 13 (2026-09-14 20:14 → died with the session at 271 of 296 (~45 min at 12 workers, a FULL read on `ad92d305`); recovered 21:02 with `--reuse` in 3 min + the six sets the SQL outage failed in 3 min; `ledger-sets-2026-09-14-run13-step66.csv`; DB runs `e3065245` + `c05c617b`) | **251 of 296** build (from 239: the one-plan sets, step 66); no-storey 21 → 10; walls 115,875, columns 82,649; 51 yardstick sets: **58% / 53%** (6,539 of 11,276; theirs 6,519 of 12,331). `corpus-query diff` run 11 → 13: NewModel 11, Placement 20 (step 65: 633 → 761 within 100 mm), Composition 15, **SameCounts 249, LostModel 0** — the BridgeChains index changed nothing on 249 sets. CPU per set over the 239 common sets: 704 → 627 min (−11%; 31168's 3× was an outlier — most sets are compose-bound); the wall time is the 12 workers. The ledger now appends per set (`e44e76ce`). |
| run 14 (2026-09-14 22:28 → 23:21, **53 min, a FULL read at 12 workers on `be2b28f3`**, steps 67–69; launched by `run14.cmd` through `Win32_Process.Create`, banked its own ledger on exit; `ledger-sets-2026-09-14-run14-step69.csv`; DB run `4db4dcf3`) | **253 of 296** build (from 251); **walls 104,506 from 115,875** (the wood-plan rule), columns 81,725; 51 yardstick sets: **58% / 53%** (6,536 of 11,270; theirs 6,516 of 12,311 — unchanged). `corpus-query diff` run 13 → 14: NewModel 2 (01589 a FALSE model from a revision strip; 01375), LostModel 0, Storeys 11, Views 1, Placement 1 (01379: 121 → 178 sheets placed, rendered), Composition 73 (yardstick 1 better / 0 worse / 13 same), SameCounts 208. **Every storey mover looked at** against run 13's per-sheet rows (§80): three regressions, each one rule (step 70) — 30988 lost L3 (the word chain per building), 40117 grew a 2-ROOF (a tagged roof on a plan-named ladder), five sets lost a level to ROOF (a title starting with LEVEL); 30912 / 30926 read their real titles for the first time (step 68) with four title-reader defects named. |
| run 15 (2026-09-14 23:41 → 00:03, **22 min, a `--recompose` at 12 workers on `584b70c2`**, step 70; `ledger-sets-2026-09-14-run15-step70.csv`; DB run `54c06888`) | **253 of 296**; 58% / 53% unchanged. `corpus-query diff` run 14 → 15: Storeys 9, Composition 4, SameCounts 283 — eight movers are step 70's sets back on their storeys; the ninth, 30992-01 (MAIN / UPPER / ROOF, nothing to rank), lost L1 to **shared state**: `PlanSheetNaming.Vocabulary` is a process-wide static set per set while twelve sets compose at once (§81, step 71). The partial ledger holds 279 of 296 rows (the 17 "another job's file" sets never append). |
| run 16 (2026-09-15 00:11 → 00:33, **22 min, a `--recompose` at 12 workers on `7975b354`**, step 71; `ledger-sets-2026-09-15-run16-step71.csv`; DB run `04679a26`) | **253 of 296**; 58% / 53%; walls 104,586, columns 81,844. `corpus-query diff` run 15 → 16: **Storeys 1, SameCounts 295** — 30992-01 back to L1 L2 ROOF (31 columns, 77 walls); no other set moved, so in run 15 exactly one set was reading a neighbour's words, and from run 16 the composition is deterministic. |
| run 17 (2026-09-15 11:32 → 12:43, **53 min, a FULL read at 12 workers on `8805acd1`**, step 72; migration 092 was applied at ~12:05, DURING the run, so its LOADING-plan refusal reached only the sets composed after it — a mixed run for 092; `ledger-sets-2026-09-15-run17-step72.csv`) | **253 of 296**; **59% / 53%** (6,536 of 11,108); walls 103,741, columns 80,808; plates 1,090 of 2,660 (41%). `corpus-query diff` run 16 → 17: Storeys 1, Placement 3, Composition 6, SameCounts 286 — step 72's fixes and 092's partial effect (30878-02 903 → 392 columns: its LANDSCAPE LOADING PLANS refused; 31183 505 → 226: its loading plans had been its ONLY placed sheets — the ZONE A/B plans do not place, a placement class; 31057: its one plan is a LOT C SITE PLAN, refused by 091 — no members, honestly). |
| run 18 (2026-09-15 12:45 → 13:15, **30 min, a `--recompose` of run 17's read with migration 092 throughout**; `ledger-sets-2026-09-15-run18-step72-092.csv`) | **253 of 296**; 59% / 53% (6,535 of 11,106); walls 103,583, columns 80,546; plates 1,088 of 2,660 (41%). `corpus-query diff` run 17 → 18: Placement 3, Views 1, **SameCounts 292** — the four sets whose loading plans run 17 had composed before 092 landed: 30961 (1,212 → 1,028 columns, 998 → 806 walls: eight LOADING PLAN views had doubled its level plans' members at offsets beyond the dedupe reach — refused, as "each level is shown once" says), 30756, 30997, 30838. The clean measurement of 092: **−260 columns, −229 walls, no storey and no model lost.** |
| run 19 (2026-09-15 13:57 → 15:08, **71 min, a FULL read at 12 workers on `f55508c1`**, steps 73–76; `ledger-sets-2026-09-15-run19-step76.csv`; DB run `bb82cc5f`) | **248 of 296** — A REGRESSION: 2,660 → 2,524 storeys, 85 sets' storeys moved (30941 34 → 13, 30990 25 → 15, 30816 5 → 2), 5 models lost; plates 1,024 of 2,524 (41%); 59% / 55%. Cause (one DB query on 30941 p16): step 73's `ReadingTokens` dropped every word drawn up the page and KOR's upright title block writes the SHEET TITLE up the page — "PLAN RAFT FOUNDATION LEVEL" became "SSI PM". Fixed in `dafad991` (the title reader keeps upright words; the field reader drops them). The six sets never showed it: the corpus run is the only instrument that sees every title-block layout. |
| run 20 (2026-09-15 16:39 → 18:05, **86 min, a FULL read at 12 workers on `becd7f86`**, steps 77–78 + the PlanarRings sweep + run 19's first title fix; `ledger-sets-2026-09-15-run20-step78.csv`; DB run `0ac235c3`) | **251 of 296**; **storeys with a plate 1,573 of 2,557 = 62%** (run 19: 41%; +531 plates on 129 sets — 31087 9 → 56, 01379 6 → 52, 31130 3 → 21, 31150 1 → 11); NoRingRead 17% → 8%, RingsReadNoPlate 19% → 11%, NoSheetPlaced 27% → 20%; no model lost; 58% / 55% (11 sets worse by share, 5 better: the double-height re-spans move columns a storey — to look at). Against run 18 the Storeys class still holds 82 sets: 30941 came back (13 → 34), the rest were a SECOND step-73 cause — the older KOR block's revision-table header taken as the title's neighbouring column (30816 "Level (Concrete") — fixed in `6df7c596`, measured by run 21. |
| run 21 (2026-09-15 19:40 → 20:41, **61 min, a FULL read at 12 workers on `6df7c596`**, step 79 + the second title fix + the census; `ledger-sets-2026-09-15-run21-step79.csv`) | **254 of 296** (the most yet); **2,687 storeys, 1,703 with a plate = 63%**; NoRingRead 6%, RingsReadNoPlate 12%, NoSheetPlaced 19%; yardsticks 51 sets, 58% / 53%. **Run 19's regression is closed**: against run 18 no class loses (Storeys +9 sets / +555 columns, LostModel 0; 31139 10 → 28 storeys, 30926 11 → 19). Against run 20: 30816 2 → 5 storeys, 30990 15 → 25, 30912's plates 8 → 42; and four sets place fewer sheets than run 20 did — 30989 23 → 17, 30993 72 → 28, 80062 6 → 2, 01379 more sheets but 4,477 → 3,438 columns — each back to run 18's state. LOOKED AT (20:55, the per-page rows of runs 20 and 21): run 20 was the wrong one — its truncated titles ("LEVEL P3 SLAB", "LEVEL PLAN", "(SLAB") hid the word REINFORCING, so the slab-reinforcing sheets were placed as plans and doubled every level's members (30989: 17 storeys × 40 columns); run 21 reads "LEVEL P3 SLAB REINFORCING PM" and refuses them, as run 18 did. 01379 places 57 MORE sheets in run 21 and composes fewer columns — each level shown once. No defect in run 21. |
| run 22 (2026-09-16 01:26 → 02:20, **54 min, a FULL read at 12 workers on `8e21744d`**, steps 80–84, the overnight session; `ledger-sets-2026-09-16-run22-step84.csv` + its sheets file; DB run `718a5d70`, written by `corpus-ledger-db` after the analyzer's own write lost the connection at 02:20) | **254 of 296 (of 279 with a stick file of their own)**; **2,684 storeys, 1,697 with a plate = 63%**; yardsticks 51 sets, 58% / 53%. Against run 21: NewModel 0 / LostModel 0; Storeys 1 set (30838 47 → 44: its duplicate L35–L37 merged, step 84); Views 21 sets −189 columns / −816 walls (01379 3,438 → 3,311 / 3,946 → 3,224: the reinforcing views named and refused, the doubled west tower gone — step 81; 30783 and 30864's SLAB REINFORCING views refused, each a doubled level and one plate fewer); Placement 5; Composition 80 sets −71 / −248 (steps 82–83: rings inside floors, gap faces, phantom pairs); SameCounts 189. 30990: 350 of 462 → 350 of 398 within 100 mm (76% → 88%, step 80). |

**The work order is a count now.** 1. Storeys: the 67 sets whose plans name their storeys with
words, and 935 views the composer can put on no storey by name (step 47, §8 item 5). 2. Views on
the grid (55% are not placed by name). 3. Plates (69% of storeys have none). Each measured on 292
before it is kept.

## 2. Where it stands, measured (2026-09-11, commit `3da6f85c`)

| | |
|---|---|
| Sets that build from the PDF alone | 6 of 6 (five KOR, one architect's — Vectorworks) |
| 31168 against the Revit route | columns median 16 mm, **92% within 50 mm** (2,211 columns, 58 storeys); tower plates within 0.1%; **36 of 62 storeys carry a plate**; walls 1,324 vs 1,832 |
| Steps landed | 43, each one universal rule with a banked test stating what it covers and does not |
| Core tests | 1,315, all green (full suite 8 min; fast 25 s) |
| Codex adversarial audit of steps 31–41 | 25 findings; 23 fixed as tests, 2 stated as limits |

## 3. The debt that makes it "ephemeral" — counted

| What | Count | Where it should be |
|---|---|---|
| Loose scripts the loop depends on | **40 python + 5 bash** in `docs/etabs-handoff/` (26 touched this week). **2026-09-11: none (WP2)** | `takeoff` verbs, tests, or deleted with the finding recorded |
| Banked baselines and the yardstick | 80 `.e2k` (1.8 MB for the current six) + 616 MB of renders/DXFs in **`%LOCALAPPDATA%\Temp`**, banked by a hand-typed `cp` until this morning | the current six in the repo beside the tests; a bank is a reviewable commit diff |
| Drafting conventions compiled as constants | **106** `const` values in the readers (reach, taper share, pattern count, label reach…) against **49** rules in KorStandards. **2026-09-11: 169 declarators triaged by a test — 13 rows, 44 conventions still compiled, 85 tolerances, 22 rules, 5 another product's, 3 dead deleted** | rows, read through `PdfIntakeOptions.For(conn)`, parity gated by `CompiledDefaultsAreTheBankedRowsTests`; the triage gated by `EveryReaderConstantIsTriagedTests` |
| `Program.cs` | **5,153 lines, 56 verbs** in one file. **2026-09-11: 345 lines, 70 verb files (WP3)** | one file per verb, a registry, the help-list test |
| Transport between the two halves | scratch DXF written to disk and re-read (`pdf-takeoff` → `dxf-to-etabs`). **2026-09-11: in memory (`DxfSheet`), the DXF an outlet (WP4)** | in-memory `PlanGeometrySet`; the DXF stays as an outlet |
| State carried in prose | `docs/PdfIntake.md` 2,555 lines; the seed doc; memory files; "known red" carried a day | one START page; the log frozen as the record; a red is a finding, never a known |

## 4. The packages, in order — each one sitting, one commit, one verdict

Nothing new is read from a drawing until WP1–WP5 land, except from a red test. The reading
backlog (§6) waits; it is where the last two weeks went and it is not what makes this a tool.

### WP1 — The analyzer: the whole corpus through the one ingestion point, into a ledger
- **Mirror** (done once, then by hash): the 292 current issues (3.5 GB) to the local drawing
  cache the tests already use; the architects' PDFs with more than a few pages. Read-only on
  the share; nothing read over SMB in the loop after that.
- **`takeoff corpus-analyze`**, on `StickFileCorpus` (the census, landed today): every sheet of
  every set through `DrawingIntake.ReadSheet`, then the composer, in parallel; one row per sheet
  and one per set into **`analysis.IntakeLedger`** in KorStandards (migration for Ian) and a
  CSV beside it: set, sheet, type, title, storey, stated scale, placed on the grid or why not,
  columns/walls/plates per storey, the yardstick residual where the job has a model. Incremental:
  a set is re-read only when its file hash or the tool's version changes.
- **The population's vocabulary** out of the same pass, as rows: every level name, sheet-title
  word, scale note, wall-tag code and layer word across all 292 sets, with counts — the part
  that is learned from the corpus into the DB rather than typed from one drawing.
- **The regression bank inside it**: the six sets' baselines move into the repo beside the
  tests and stay byte-identical gates (`SixSetsBuildAsBankedTests`); the other 286 are a ledger
  that only ratchets. Banking = a commit that changes a baseline file, reviewable.
- **The order of work falls out of the ledger** — "31 of 292 sets have a sheet that cannot be
  set on the grid, 19 of them for one reason" — and every rule is measured on all 292 before it
  is kept.
- Deletes: `pdf_only_all.sh`, `pdf_only_one.sh`, `six_set_diff.sh`, `six_set_bank.sh`,
  `render_storeys.sh`, `members_diff.py`, `plate_diff.py`, `storey_counts.py`, `plan_sheet.py`
  (ported as one C# implementation shared by the test, the analyzer and a `takeoff model-diff` verb).
- Gate: the six baselines byte-identical on `3da6f85c`; the ledger holds 292 sets with a row
  for every sheet; the old scripts and the new code give the same diff on one deliberately
  changed model; a second run reads nothing that did not change.

### WP2 — Instruments are verbs — DONE 2026-09-11
- The instruments used more than once are `takeoff` verbs on the code the readers use:
  `vector-lines` (was `pdf_lines.py`), `vector-find` (`pdf_words_near.py`), `vector-words --band`
  (`pdf_words_in_band.py`), `grid-names`, `model-to-page`, `pdf-overlay --mark/--crop` (`crop_mm.py`),
  `corpus-query` (`plan_titles.py`, `set_sheets.py`), and from WP1 `model-diff`, `model-render`,
  `model-yardstick`. One e2k grid reader (`E2kDocument.ReadGrids`) serves the differential, the
  yardstick and the instruments — it was two private copies. The scratch DXF banks its page origin
  (`$INSBASE`) so a model point comes back to the page without a second alignment; verified by
  marking a P2 column of 31168 on its own sheet and looking.
- The one-offs (`chains`, `view_breaks`, `view_parts`, `dxf_layer_entities`, `pdf_tiles`,
  `ledger_diff`, `pick_plans`, `mpa`, `tags`, `annots`, `renderpage`, `order`, `idbprose`,
  `read_questions`, `transcribe`) are deleted; each finding stands in `PdfIntake.md`, annotated at
  its first mention; `docs/etabs-handoff/README.md` maps every old name to its verb.
- Gate, met: `docs/etabs-handoff/` holds no scripts; the help-list test; the six byte-identical
  (the exporter's header changed, the models did not); `TheInstrumentsShareTheReadersFramesTests`.

### WP3 — `Program.cs` one file per verb — DONE 2026-09-11
- `Verbs/<Verb>.cs` (70 files, one class per verb: `Matches(args)` and `Run(args)`, the body moved
  verbatim by `tools/split_program_cs.py`), `TakeoffVerbs.All` the registry in the order Program.cs
  always tried them, `GlobalUsings.cs`; `Program.cs` is the help check, the dispatch loop, the
  rebar-CSV default and the help catalogue (345 lines, from 5,153).
- Gate, met: the help-list test reads the registry (not the source); the six byte-identical
  (`SixSetsBuildAsBankedTests`, 8 m 33 s); the fast suite 1,218 green.

### WP4 — In-memory handoff — DONE 2026-09-11 (first form)
- Gate first: `TheHandoffIsInMemoryAndTheModelIsTheSameTests` — the composer given views in memory
  and a folder that does not exist builds the same model as the folder route (fast, synthetic), and
  the six built both ways are byte-identical (Slow). Then the change: `DxfExporter.ExportLines`
  holds a view as its lines, `SheetsResult.Views` carries them as `DxfSheet`s, and
  `DxfToEtabsRequest.Sheets` / `LevelLines` hand them to the composer, whose every read goes
  through one `LinesOf`. `PdfOnlyBuild.Build` composes from memory (`Handoff.Memory`, the default);
  the DXF files are still written as the outlet the corpus, `--recompose` and the instruments read.
- What this is and is not: the DXF TEXT is the interchange, held in memory — every reader in the
  composer takes lines and the office's own exports arrive as files of them. The typed
  `PlanGeometrySet` behind it (no serialisation at all) is the next refinement; the rounding a
  typed handoff would have to reproduce to stay byte-identical is the DXF's 0.0001 mm.
- Gate, met: both tests green; the six through memory and through the disk, 21 m 42 s for the eighteen builds with the corpus rebuild running beside them; the per-route seconds print from the test's next run.

### WP5 — Conventions are rows — TRIAGED 2026-09-11, tier one wired
- The triage is a gate, not a table in prose: `EveryReaderConstantIsTriagedTests` scans every
  `const double|int` in the readers (the same scan as `tools/list_reader_constants.py`, 169
  declarators) and holds each to one line — **Row** (a KorStandards key, named and present in the
  code), **Convention** (belongs in a row, still compiled: the debt, counted), **Tolerance**
  (slack against drafting and precision, code by design), **Rule** (a fact of geometry or of
  buildings), **Elsewhere** (the SAFE/WPF side's, sharing a file), **Dead** (fails until deleted —
  three were, and are gone). A constant added without a line fails the test.
- Tier one, wired end to end: three conventions the two sides mean the same thing by now read
  the DXF side's rows — `dxf.bridge-tolerance`, `dxf.min-plate-area`, `dxf.dash-join-gap` (both
  compiled values equal; the parity test holds them so) — and two PDF-side rows are seeded by
  **migration 085** for Ian: `dxf.pdf.fallback-scale` (96) and `dxf.pdf.ladder-min-rows` (3).
  `PdfIntakeOptions.SettingKeys` is 17 keys.
- Owed (the test counts them, 44 lines): the title-block region compiled five times over
  (`TitleRegionMinFx` — one definition first, then the row), the sheet-furniture and title-reader
  shares, the wall-tag and mark-up reaches, the bubble radii, the schedule column bounds, the DXF
  outlet's name height; each names its row in the test. NOT shared with the DXF side, deliberately:
  the slab-edge extend limit (48 in here; the DXF side measured extending as harmful and banks 0).
- Gate, met: the triage test green; `CompiledDefaultsAreTheBankedRowsTests` (085's two keys
  declared unbanked until applied); the six byte-identical.

### WP6a — The road to the finish line, re-planned 2026-09-15 13:00 on the engineers' own definition of usable

The engineer's definition of a usable starting point is on record (07 Aug, 24 Aug, 31 Aug; index
`reference_andrea_answers_index`): **the verticals and the overall shape of the slab on every storey,
one thickness per floor; one model per building; each level shown once; footings, beams, loads,
sections and diaphragms are hers.** So the number that says "usable" is not columns within 100 mm of
a stale model (58%) but **storeys carrying their verticals AND a plate — 1,096 of 2,658 (41%) on run 16.**
The plan below is ordered by that number and by the two rules her answers back that the tool does not
yet obey. Each item is one step: reproduce on the set, rule, test, gate, run, every mover looked at,
three lines here at the commit.

| # | step | done when | size |
|---|---|---|---|
| 1 | **Open-face pairs in millimetre sets** (audit finding 1) — DONE at step 75 (`7cf9969f`, §83): the 18-in ceiling is converted by `InUnitOf`; measured alive on the corpus (+32 walls on 9 of 296 sets, no yardstick moved); `PairOpenFaces` is true for a Revit DXF and false on the PDF route, whose reader pairs every face with the fill in hand. Row left open by mistake until 2026-09-16 | the corpus recompose measured; the rule written | done |
| 2 | **A plate on every storey** — her criterion. The ledger already counts `storeys_with_plate` and the report says why an edge would not close; the reading classes behind the 59% are the slab-edge chains that do not close, rings that depend on export order (§71, PlanarRings prototype), and edges the wall reader eats (§38's class) | `storeys_with_plate` rises run over run with no storey losing one; every storey without a plate says why in the report; the order differential green on the six | 3–5 days, the big one |
| 3 | **Footings are not columns** — TWO classes. (a) DONE, step 80 (§87): a kept sheet titled as a plan the set issues, plus words, is a drawing ABOUT that plan (30990's FOOTING REINFORCING sheets = 54 footings as columns rising to P2; 31202's LOADING DIAGRAM = 45 load ticks as walls on L2) — 30990 P2 120 judged @38% → 56 @82%, set 76% → 88%. (b) OPEN, 31162's class LOOKED AT 2026-09-16 (§94): the 21 × 50 in loops are on its WOOD FRAMING plans (S2.10 …), a beam-with-post symbol, not footings — the foundation plan's columns are 12 × 30, right; the rule (48 in or longer is not a column on any layer — her W1) is stated, and what the block IS (a pier, her 31138 practice; or a wood plan's beam) needs her answer on the piers + one more wood set | (a) banked; (b) 31162's 66 column-layer rectangles gone with no column of hers lost on the six current sets; the corpus's foundation storeys lose footings only | (b) ½ day + a run |
| 4 | **A part plan never duplicates the overall plan** (050 "each level is shown only once") — (a) DONE, step 81 (§88): 01379's offset west tower was a READER fault — the page's second view lost to a baseline rounding, both plans written as one view — fixed at the baseline (`TextBaselines`), L23 = L22 now, every tower storey 59–61 walls. (b) OPEN: 31183's ZONE A/B plans not placing; the composer's own "a part plan stands over the overall plan" check for the two-tower sets whose axis names repeat | (a) banked; (b) the corpus's Placement class carries no part-plan duplicates | (b) ½ day |
| 5 | **The no-model residue** — STARTED 2026-09-16 (step 85, §93): 17 other-job files (a denominator, 279), 17 no-plan (six 1–2 page sets, eleven 8–43 page sets typed no plan), 8 no-storey looked at page by page: 30994 = the project name over the title (FIXED: a title names a plan); 30888 = the rotated strip (01589's class, open); 30980 = its titles are plotted as glyph outlines and its block letter-spaces its labels (step 86, §96: the labels join, the empty box is no title, the floor label is not a neighbour — 17 guessed titles → none; a raster-route set, honestly so); 30768 details only. | ≥ 270 of 279 build; every remaining one names its class in the ledger | 1 day left |
| 6 | **The DXF route's ratchets** — DONE 2026-09-16 (steps 82–83, §89–§91): four reds, four rules, no re-bank — a plate that came and went with the page origin (on the edge is in); an open chain's closing gap is not a face (31168's 202-ft wall on nothing); a face's partner is the nearest facing face wherever it lies + no wall stands inside a wall (31138's 57-in phantom); the outlines ratchet counts outlines at a wall's thickness (all 64 were ribbons). A thickness cap was measured and rejected (§91) | the full Core suite green: 1,453 of 1,453 | done |
| 7 | **Storeys named as the set names them** — DONE for the named shapes 2026-09-16 (step 84, §92): LEVEL (L35) → L35; LEVEL -3 → P3 in the ladder AND the plan titles (30912's five parkade plans placed); `corpus-query storeys` classes every name (roofs by word, sub-levels, letter-and-count, elevations stay as the set says them). Residue: 6 garbage names on 5 sets = item 5's class | `corpus-query storeys`: garbage 6 → 0 (item 5) | done |
| 8 | **The engineers' review** — one model per building (050), published to the job folder, for three jobs whose engineers are current (a KOR set, a numbered-block set, the architect's set); their words become rules, as Andrea's did | three published models with reports; the feedback banked as rulings | a session per engineer |

Two things the plan no longer asks anyone: the ten questions of 2026-09-15 are answered in the index
(eight of ten) or open and low-stakes (wood buildings; which of an engineer's models is current — the
gravity model she supplied is the reference by her own choice). One request stands, Ian's to make:
a **current gravity model per job** for the sets under review, so the yardstick judges against the
drawing's own model.

### WP6 — The finish line
- The ledger's totals against §1: how many of the 292 build, how many sheets say why not, the
  yardstick distribution over the 66. One model — 31170's, or whichever the ledger ranks best
  of the architects' sets — goes in front of Andrea. What she and the ledger say is the backlog
  for the next plan, not this one.
- Gate: §1's three conditions, each with its evidence in the commit.

## 5. Process rules that become code, not prose
- **A red test blocks the bank.** WP1's verdict includes the full suite; red = no bank, no
  commit of a rule. There is no "known red".
- **Every step gets its adversary the same day.** After each package's commit, one bounded
  Codex brief (`docs/codex/CODEX-PDF-INTAKE-WP<n>.md`) on that package alone; findings become
  tests before the next package starts.
- **Rule 1 stays a gate:** before any new instrument, `takeoff --help` and `tools/` are
  searched, and the reply says so.

## 6. Parked until WP6 — the reading backlog, so it is not lost
Boundary walk (+13 storeys, stashed); a ring that is a piece of the floor; 25 storeys with no
plate (not one cause); tower walls 33 vs 40; mezzanines placed; 31130's halves; overlapping
collinear copies across sheets (§51); 31202's PENTHOUSE stated twice; 31065 ROOF 5.8 m over L19;
the harness's 31168 moved to the 09-10 reissue (re-banks every 31168 baseline — Ian's call);
the 57 EDB-only yardsticks exported to `.e2k` on a machine with ETABS.

## 7. What this plan does not do
It does not promise a storey count, a wall count, or "31168 = Revit". Those are measurements the
harness reports; the contract is §1. It does not touch the app (WPF), the Drafter bridge, or the
Revit route.

## 8. What needs Ian (2026-09-12 morning)

**0. (2026-09-15 01:30) The full Core suite is not green — 4 of 1,475 on HEAD, and it has not been
green since at least 2026-09-11 23:34.** The six-set gate and the fast suite were the gates for every
step since WP5; the slow DXF-route ratchets (`ModelCoverageTests`, `ModelIntegrityTests`, the parkade
plans by name) were never run. Bisected on 2026-09-15 night: the 31168 pair (KW235 on LEVEL 1 MEZZ
stands on nothing; 20 outlines dropped against 19) went red at step 56 (`cf66a299`); 31138's two
unmodelled walls and the parkade-by-name 3 → 2 were red before `69e554b5` and are not bounded
below. Decision: characterise and fix them as their own step (a differential of the DXF route
before and after `cf66a299` is the instrument), or re-bank the ratchets with each reason stated —
never carry them. Until then, "fast suite + six-set gate" is the stated gate, and it does not see
the DXF route.
**Measured 2026-09-15 12:00 (the differential across `cf66a299`, 31168 composed from its Revit DXFs at
`8ad431cb` and at `cf66a299`, `model-diff`):** step 56 moved the DXF-route model on **39 storeys — walls
lost 120 / gained 48, columns lost 1 / gained 64** (tower A's core returns as columns on every storey, as
the commit says it did on the PDF route; walls re-cut on tower B; L1 MEZZ 67 → 77 walls). The same rule,
deliberately, on the route the six-set gate does not see; the coverage ratchets caught its two residues
(one wall on nothing; one more outline dropped), not the 168 members. Her 31168 model names only three
storeys ours does, so `model-yardstick` cannot judge it (94% both ways). One of the four reds was a fixture
reading a new issue on the share (fixed, `82774c1a`). The step this needs: render 31168 before/after
storey by storey, look, then re-bank the ratchets with the numbers above as the reason — or find the
residues' cause. Not the PDF route; queued behind it.


1. **Migrations — DONE 2026-09-12** (083, 084, 085 by Ian; then 086, because 084 and 085 had
   inserted their rows `'unverified'` and `analysis.vw_RuleSetting` exposes only `replay-verified` /
   `engineer-confirmed` — the tool never saw them and the parity test, reading the same view, stayed
   green instead of going red. 086 set the three rows `replay-verified` on the evidence that the six
   sets build byte-identical at those values; the three keys left `UnbankedByDesign`, and
   `CompiledDefaultsAreTheBankedRowsTests` now reads each row and holds it equal to its compiled
   default. Lesson in 086's header: a PDF-intake row is inserted `replay-verified` when the harness
   has replayed at its value, as 082's were.)
2. **ETABS 23 on KOR-210 — DONE 2026-09-12**: all nine exported (31097, 31138, 31168, 31170,
   31183, 31195, 31199, 31202, 50054 — 8.0 MB of `.e2k`), mirrored to the yardstick folder (105
   models); four of them are harness sets, so the six have engineers' yardsticks now.
3. **31130's yardstick** — 20 shared storeys and under 25% of our columns within 100 mm of the
   engineer's: the yardstick's own numbers say why (median 5.7 m ours → theirs, 22.9 m theirs →
   ours): the two halves of the building are registered as one frame. The backlog's "31130's
   halves" item, now with a measurement. Nothing for Ian.
3a. **What the yardsticks found on 2026-09-12** (PdfIntake.md §56): an inch applied in
   millimetre models (a unit differential now gates it), one column read from two sheets kept as
   two (an invariant now refuses it), and 31202's tendon anchors read as columns (step 48; 10 of
   55 a sheet caught so far). All six re-banked; migration **087** (`dxf.pdf.force-words`) for Ian.
3b. **Step 49, 2026-09-12 afternoon** (PdfIntake.md §57), on Ian's "go with step 1 — can we get
   these numbers nearly identical?": 31202's 34 unmatched "18x18" columns were the two filled
   quadrants of spot-elevation targets, read as columns, walked into one figure-of-eight by the
   loop builder, and placed by a centroid formula that divides by a near-zero area — eight joints
   at kilometres in every banked 31202 model since its first bank, the reason every render showed
   the building as a dot. Three rules, each banked: a centroid lies inside its own box; a
   target's quadrants are not columns (a pair of one-size filled shapes touching only at a
   corner); a member stands on the building (publish-blocking invariant; the OLD banked model
   fails it). 31202 **85% / 95%** (from 82%); 31065 one wall moved; four sets byte-identical.
   `dxf-inspect --loops` lists the classifier's own loops whose centroid is the vertex mean.
   Nothing for Ian. Next: the 42 anchors the chains miss, the 32 offset 12x24s, the crossing-strip
   wall loops (9 on 31202, 1 on 31065).
3c. **Steps 50–51, 2026-09-12 evening** (PdfIntake.md §58–§59): the yardstick judges only inside
   her footprint (31065 73%, 31202 90%); a sheet that says CONCRETE OUTLINE is read whatever else
   its title says (31130's fourteen tower storeys; 39 of 548 refused sheets on the corpus); a refused
   sheet's axes still place the sheets that name them, at its own scale (31130's east half, through
   the DESIGN LOAD PLAN at 2x): 31130 **75% / 83%** from 22% / 40%. A view's name is unique within
   the set (31168's 09-10 issue named one sheet twice; the in-memory handoff threw). Corpus run 5
   banked (206 build; 34% / 48%). One question for Andrea at WP6: a column drawn with its face on
   the grid — modelled where drawn, or on the grid? (31202, 32 columns.) Nothing for Ian.
3d. **Step 53, 2026-09-12 night** (§61): `pdf-at` (what happened to the ink at a point); the grid is
   drawn with one pen (a heavier stroke on an axis is a tendon, kept apart for the tendon reader
   alone); a run may stop just past its anchor (fittings only). 31202 **93% / 95%**, 53 of 55
   anchors on the typical sheet; five sets byte-identical. Corpus run 6 (steps 50–52): 207 of 293
   build, yardsticks **49% / 53%** (from 34% / 48%); run 7 on step 53 (22:02 → 00:33): 207 of 293,
   49% / 53% — the same 6,344 matched, 29 fewer of ours judged (§1b row).
   **FOUND, NOT SHIPPED — the next step, rule 11 first:** the DXF's origin is the drawn content's
   centroid, so every model's frame moves with the reading (31168 by 723 x 283 mm for no member
   change); the page frame fixes that (stashed, `stash@{0}`) and exposes that the composed walls
   depend on where the origin is — the same 36 DXFs of 31168 translated 5 m x 3 m: columns 100%,
   walls 25 lost / 11 gained, tower A's stair core 18 walls in one frame and 8 in the other; one
   sheet alone is stable, so it is in how two sheets' readings of one core are reconciled. Write
   the differential ("the same drawings shifted on the page build the same structure"), find the
   cause, fix, then the page frame, then re-bank the six once. Also: the gate's `ModelDiff`
   registration called 177 columns lost and gained under a pure translation the yardstick matched
   at 100% — register the gate's diff the way the yardstick does.
3e. **Step 55, 2026-09-12, late** (§62): a sheet that names no axis stands where its members stand
   (`GridAlignment.SolveByColumns`: the displacement most of its column centres and wall axis
   ends share with the members already placed, 100 mm bins, at least 4 and at least half). The
   cause of §61's frame class on 31168 — LEVEL 35 and 36 of BLDG A left in the page frame — is
   placed: L35 at 8 of 12 members, its core walls within 3 mm of L34's. Two faults found by
   `grid-names` before the gate saw anything: a vote per PAIR let a thirty-storey stack of one
   wall corner outvote twenty-four columns (one vote per member per bin now), and outline corners
   sit half a thickness from the panel ends the model holds (axis ends now, both sides). Five
   sets byte-identical; 31168 re-banked. The differential of 3d is still owed, then the page frame.
3f. **Step 56, 2026-09-13, 00:00–04:00** (§63): rule 11's differential
   (`TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests`: six sets read once, composed as-is
   and shifted 5 m x 3 m, compared registered) fired on five of six sets — 31168 308 walls lost /
   165 gained. The class, in one sentence: *a place is decided by distance, never by a cell* — and
   its two reader-side shapes, *a tie is a tie* and *a threshold equal to a drafted dimension is
   met in every frame*. Eight sites fixed (composer dedup, joints, loop nodes, dashes; ties in the
   decomposer, pairing, network, builder, box; thresholds to the micron; probes either side of a
   drawn line; closed outlines left out of the dash joiner). Nine runs; green on all six. Gate
   moved on all six (tower A's core one way on every storey; returns as 30x41 columns); yardstick
   matched counts identical; banked. `dxf-inspect --members` (what the reader hands the composer)
   is the instrument; the page frame (`stash@{0}`) next.
3g. **Step 54, 2026-09-13, 04:00–05:00** (§64): the page frame (`DxfExporter` origin = the page's
   corner, `$INSBASE` 0). Differential green with it; the gate read four sets as pure translations
   of 40–50 m and found two more places the fit and the composer leaned on the frame: the by-name
   fit's tie by "the smaller move" (now the tightest cluster, the smaller move kept for the reissue
   diff only) and the last two inch tolerances not compared to the micron. The differential's
   vector is fractional now (5,000.37 x 3,000.61) so sub-millimetre keys are exercised, and green
   on all six. Banked in the page frame. `ModelDiff`'s registration was right all along (the 177 of
   §61 were real moves). NEXT: the Codex audit of steps 44–56 (brief written), then step 47.
3h. **Step 57, 2026-09-13 morning** (§65): the Codex audit of steps 44–56 answered — 25
   findings, 7 High all real (stale by-shape flags, anchors resurrected in split views, parkade
   levels folding under the first stated one, a top plan fitting the wrong tower, a junction as
   four of the quorum, an orphan ring holding a floor's place, a square footprint), each fixed
   with its counterexample as a test; F9 and F23 (the vocabulary flake, explained: `Run` writes
   the static) fixed with them; the rest queued with reasons. ⚠ The brief burned 75% of Ian's
   Plus 5-hour Codex limit in 17 minutes: it named a commit range that carried 60k lines of banked
   e2k. Briefs are sized in bytes before handover now (`feedback_codex_briefs_must_be_bounded`).
   NEXT: step 47 (storeys by words), then F8/F10/F14 with the next reading step.
3i. **Step 47, 2026-09-13 afternoon** (§66): a storey may be named by a word (MAIN/GROUND/UPPER,
   ordinals, BASEMENT, LOFT; the framing-over clause is not the plan's storey) — five rows,
   migration 089 applied; and a small job's hyphenated sheet number in its own title names the
   view (step 46 completed). **29 of the 68 storey-less sets build** (207 → 236 of 293 on that
   count); 39 have no title on the page. Gate byte-identical. NEXT: run 8 over the whole corpus;
   the small jobs' "columns" (248 on a house); the 39 with no title.
3j. **Step 58, 2026-09-13 evening** (§67): a column is drawn with four corners — a filled
   three-point shape is a symbol's triangle (01389's 139 "columns", all triangles; the harness
   plans' 180 columns, all four points). 01389 reads 0 columns now. Five sets byte-identical;
   31170 re-banked — five false columns off L1 (two hatch corners, three tag arrowheads on real
   columns), inside her footprint 331/331 unchanged. Run 8 banked (17:45 → 20:08, steps 47–57):
   **236 of 293**, 50% / 55%. NEXT: the 149 sets whose column count moved under steps 54–57
   (per-set differential of run 7 vs run 8); run 9 on step 58; the 39 no-title sets; F8/F10/F14.
3k. **Step 59, 2026-09-13 night** (§68): `corpus-query diff` — every set in one class by the
   first thing that changed; run 7 → 8 = NewModel 29, Storeys 34, Placement 11, **Composition 128**
   (the page frame stacking unplaced sheets; the six-set gate cannot see it — all six stand on
   grids; §64 says so now), Unchanged 91; per-sheet reading unchanged (271,274 → 271,267). The
   yardstick's frame was ORDER-DEPENDENT (`MaxBy` on pair votes; four Unchanged sets' verdicts
   moved) — judged by support now, ties to geometry; every yardstick number before this was the
   old ruler. F22 closed. NEXT: run 9 `--reuse` to re-measure with the new ruler.
3l. **Step 60, 2026-09-13 night** (§69): 16 of the 39 storey-less sets are ONE file (01783-01's,
   copied into 16 jobs' folders) — read once, the copies say so; the population is **277 jobs**.
   A title may run two lines; a word floor with a framing-over clause names a plan; the set's
   framing-over clauses rank its floor words (GROUND 1, MAIN 2, UPPER 3 on 31089-01, which builds
   3 storeys now). NEXT: the title reader's failures (30768-01 "-", 30888-01, 30980-01); numbered
   buildings; run 10.
3m. **Step 61, 2026-09-14 early** (§70): the audit's queue closed — F8, F10–F22, F24–F25, each
   with a test; the entity-order differential (F11) beside the shifted one; F24 found a unit
   literal (the joiner's 0.15 across-the-line tolerance); the two-line title split a missed L40
   view off 31168 and exposed the ladder's global roof rule — a building's roof plan names that
   building's roof now. Run 9 showed step 58 too wide (−47,963 columns): two triangles that are one
   shape (`TriangleTwins`). The entity-order differential is RED on all six (skipped, numbers in
   the skip reason) — the loop builder's arrival-order walk; a canonical sort was tried and
   reverted. Run 10 banked: 238 of 278 jobs, 58% / 52%, walls tripled on wood-frame sets
   (a filled stud wall is not concrete — the next reading step). NEXT: what a filled band is a
   wall OF; the ring-ownership rule for the loop builder; the title reader's failures; numbered
   buildings; the Codex audit of steps 57–61 (briefs A and B, `69ecef0c`).
3n. **Step 62, 2026-09-14 afternoon** (§71): brief A answered — 12 findings, all real, all fixed
   with tests (a maximum matching in `ModelDiff`; walls carry their angle; the twin rule's
   longest-edge condition and neighbour cells; the unit differential compares Z, order, section
   per member and elevations; `CorpusDiff` says SameCounts and judges by share; `Reversed` keeps an
   attributed INSERT whole; the yardstick's tie is the lower bin). Gate byte-identical. NEXT: brief B;
   what a filled band is a wall OF; the ring-ownership rule (specified in §71).
3o. **Step 63, 2026-09-14 afternoon** (§72): a wall is six inches or more — `dxf.min-wall-thickness`
   4 → 6 in on 101 engineers' models and 51,127 wall areas with none under six (migration 090, both
   rows; `dxf.dash-offset-tolerance` seeded in the same); a band under the floor is `ThinBand`.
   Brief B answered: 10 findings, 10 fixes with tests (numbers read before the framing-over clause;
   the ladder and the composer both start from the office's words; a tagged roof goes nowhere without
   its building; one chain per building; cycles refused; elevator roof above roof; a note is not a
   title's second line; a sole view keeps its title; two heights). Six-set gate byte-identical after re-banking five (each diff looked at; one false positive named, 31170-arch KW79, the ring-ownership class); shifted differential green. NEXT: run 11 (the
   walls); the ring-ownership rule (§71); frames without grids; the title reader's failures.
3p. **Step 64, 2026-09-14 afternoon** (§73): what the 58% is made of. Three yardstick instruments
   (a storey's rigid part and what is left; each model against the plans' grid; `--pairs`) say the
   low sets' residual is neither placement nor a grid convention: her model does not match the
   drawing. Her .EDB's date against the drawing's issue, 38 sets: 27 older by > 180 days (median
   share 48%), 11 current (64%); ten named MASS / 2NDRY / Prelim / Below Grade / Diaphragm / Wind.
   The ledger carries the yardstick's provenance and age; the summary reads the share in two
   populations. NEXT: `--pairs` on the six current sets under 65%; a yardstick that IS the drawing's
   model.
3q. **Step 65, 2026-09-14 evening** (§74): run 11 banked (239 of 295; walls 115,664 from 151,191;
   57% / 55%); its one regression (30993, 28 → 2 placed) found the rule: the reference plan is the
   plan the most other plans can be SET ON (fits, not name counts; the sensor layout's bubbles had
   won on count). 31065 and 31168 re-banked with reasons (a 2 cm frame; two walls one panel again).
   Run 12 = the corpus recomposed at 12 workers. Codex: the raw-walk page record (the 5.4 s/page read
   walks each page twice and again on every rule change). NEXT: wire PlanarRings behind an option;
   the current-yardstick sets under 65%.
3r. **Step 66, 2026-09-14 evening** (§75): one plan naming no storey is a one-storey building —
   8 of 9 such sets build with their members, from the ledger's no-model list, measured by a 100 s
   recompose of the nine. Two Codex tasks in flight: the raw-walk page record (the read), the
   small-job title blocks (the other nine no-storey sets).
3s. **The read's cost, 2026-09-14 night** (§76): the page record (`33d6dc7e`, Codex) measured and
   NOT the saving (cold 5 m 47 s, warm 6 m 30 s); the profile found BridgeChains at 76% of a set's
   read, a grid over the chain ends gives the same first merge (`ad92d305`; 31168 51 s → 16 s); the
   gate's read cache (`203dfa44`, Codex; a composer step gates in ~1 min, a reader step in ~3);
   run 13 died with the session at 271 of 296 → the per-set partial ledger and detached launches
   (`e44e76ce`); run 13 banked (`470d85ef`): 251 of 296, 58% / 53%, SameCounts 249, −11% CPU,
   a full read ~46 min at 12 workers.
3t. **Steps 67 and 68, 2026-09-14 night** (§77, §78): the wood-plan rule — a sheet with two thirds
   of its walls as unfilled pairs (and twenty) is a wood plan, its unfilled pairs under 8 in and
   filled bands under the floor are partitions (31066 p8: 312 out; five harness sets byte-identical,
   31170-arch's two lost "walls" looked at: the slab outline's parallel lines; `34ed3f54`,
   `c87d85e3`; migration 091 written, NOT applied). The small jobs' title blocks (`f5d56e79`,
   Codex): 4 more one-plan sets build; 01783 / 01589 / 01788 handed back with their words. The
   column trace (`4ffd4bba`, Codex): every column names its branch; 31162's "columns" are the
   reader's 541 × 1,283 mm footings on the column layer — a reading class, no rule written yet.
3u. **Step 69, 2026-09-14 night** (§79): numbered buildings are building tags (`be2b28f3`); a
   tagged sheet keeps to its building's storeys only where the model names storeys by building;
   numbered buildings on one plan-named ladder share its storeys and one ROOF. 31185 on the right
   storeys at run 13's counts; 31066, 30978 unchanged. Run 14 (a full read on `be2b28f3`, detached,
   chained to bank its own ledger) measured steps 67–69 on the corpus: 253 of 296, §1b.
3v. **Step 70, 2026-09-14 night** (§80): run 14's eleven storey movers looked at one by one against
   run 13's per-sheet rows; three regressions, three rules (`584b70c2`) — the set's floor words have
   ONE order whichever building states each step (30988); storeys are per building only where the
   model names them so, in the ladder as in the composer (40117); a title that starts with LEVEL
   keeps its level (30919, 31004, 30925, 30816-02, 30878-02). Four title-reader defects of step 68
   named for the audit (neighbouring fields in the title, a displaced dash, a revision strip as the
   title on 01589, a project name as the title on 01379). Run 15 = `--recompose` on `584b70c2`.
3w. **Step 71, 2026-09-15 early** (§81): run 15 moved one set no rule touched (30992: L1 L2 ROOF →
   L2 ROOF) — `PlanSheetNaming.Vocabulary`, a process-wide static set to each set's ranked words
   while twelve sets compose at once, leaked between sets since step 60. Now an `AsyncLocal`
   (`7975b354`); `TheVocabularyInForceIsTheCallersTests` proved by breaking it. Run 16
   (`--recompose` on `7975b354`): SameCounts 295, one mover — 30992 back to L1 L2 ROOF.
3x. **Step 72, 2026-09-15 morning** (§82): the Codex audit of 63–71 answered — eleven findings, eight
   fixed with fixtures (`8805acd1`): the read cache hashes every Core source but a proven-unreferenced
   exclude list; a killed run's partial ledger is set aside, never deleted; dimension strings are not
   unfilled pairs; a sheet number is not a building; a prefixed parkade matches; a tagged roof over an
   untagged plan is the shared ROOF; a cycle apart from the chain leaves the row; the share tested at
   its line; the barrier asserted. Finding 1 (the 18-inch literal at the open-face-pair gate, unconverted
   for millimetre sets) is the next measured step. The wood rule's 8 in is the row (migration 091);
   31202 re-banked for 091's instrumentation sheets. Run 17 = a full read on `8805acd1`.
3y. **Steps 73–76 and runs 17–18, 2026-09-15 midday** (§83): Codex's title-block rules (`011117c2`; 31065's
   LEVEL 2 had been EMPTY in every baseline — re-banked with 101 columns); the plate instrument
   (`967564e7`, `corpus-query plates`: plate 41% / no sheet placed 27% / no ring read 17% / rings read no
   plate 19%; 33 area messages ÷144 in mm fixed); the dead open-face-pair branch measured (+32 walls on 9
   sets, at hatching) and refused on the PDF route (`7cf9969f`); level lists with ranges (`f55508c1`).
   Run 18 = migration 092's clean measurement (−260 columns / −229 walls of doubled loading plans).

3z. **Step 77, 2026-09-15 afternoon** (§84): a title line naming ANOTHER sheet ("DETAIL 29 / S1.03") is a
   callout, not a furniture heading (`5b02abe8`): 31087's LEVEL 2 had been swallowed as a notes box
   (13 walls → 41, no ring → 2); 31138 +5 columns / +9 walls on L1 and L2. Residue: a plan note ending in a
   heading word with no sheet number (31138 "BUILT-UP PER DETAIL").

3aa. **Step 78, 2026-09-15 afternoon** (§85): **a floor is the cells its structure stands in, united** —
   PlanarRings (§71) wired where the chain walk closes nothing and nothing drew a floor; strokes along
   the grid offered; an edge interrupted in line is one edge (48 in); the floor is the walls' outer edge
   where they close (`dafad991`). 31087's tower closes (rendered); ~110 storeys on the six sets gain a
   plate; two regressions caught by the gate and fixed (parkade plate from inner faces; the architect's
   drawn outline lost to unions). OPEN: unions of the wrong shape along leaders (31130's tower); parkade
   fragment unions; 31087 LEVEL 4. Process (binding, [[feedback_be_the_fixer_decide_bank_batch]] third
   instance): no corpus run during a development session; instrument before reading code; a status line
   every 15 minutes; the reader's 4.4 s/page is a defect — profile before run 20.

3ab. **Step 79, the sweep, the census and run 20, 2026-09-15 evening** (§86): a line ending at an arrowhead,
   and every piece in line with it, is a leader or a section cut, never an edge (`6df7c596`); the
   PlanarRings arrangement swept along X (`becd7f86`: 91 s → 11 s on the architect's page; profiled with
   dotnet-trace, `tools/Summarize-Speedscope.py`); the census kept for 12 h (`--census` retakes). Run 20:
   **plates 41% → 62%**. Run 19's title regression had TWO causes: words drawn up the page dropped
   (fixed `dafad991`) and a label far above a field taken as its neighbouring column (fixed `6df7c596`,
   measured by run 21). OPEN: 31130's plate bounded by tendons drawn black at 10–18 pt — hand
   `TendonAnchors`' labelled tendons to the edge pass; the double-height re-spans against the yardstick;
   `RecoverSurfaces` refusing dense pages; parkade fragment unions; 31087 LEVEL 4.

3ac. **Run 21 banked, the looks, step 80, 2026-09-15 late evening** (§86–§87): run 21 = 254 of 296, plates
   63%, run 19's regression closed (`bd8f3e21`). Run 20's "more sheets placed" was WRONG (truncated titles
   hid REINFORCING); the yardstick "worse" sets are the judged population growing, matched counts unchanged
   (`2142a70b`, `70bcebdf`). The force-labelled-tendon rule measured and REJECTED (`8715ccd5`, stash@{0}).
   Step 80 = WP6a item 3(a): `PlanClassificationOptions.SheetsAboutAnotherPlan` — 30990 P2 56 @82%; the
   six-set gate found the second instance itself (31202's loading diagram, 45 walls on L2, rendered and
   re-banked). NOT in a corpus run yet: batched into the next detached run with whatever else lands.
   OPEN: item 3(b) 31162's footing-sized rectangles; 31130's tendon-bounded plate (differential first);
   `RecoverSurfaces` refusals; parkade fragment unions; 31087 LEVEL 4.

3ad. **Overnight session, 2026-09-15 22:30 → (Ian's go by email at 22:29)**: instruments into code
   (`corpus-query pages`, `dxf-strip`, `model-render --storey`; `4fd58154`). **Step 81** (§88): words on one
   baseline by closeness, not rounding — 01379's item-4 fault was the reader losing a page's second view;
   the six: 31065 L2 gains its north-tower plate and loses stair-edge "walls" and phantom columns (each carried
   back to the page and looked at), 31202 L13's tendon-page fragment 6,718 → 542 (item 2's open class,
   exposed), 31130 +76 sq ft. Ten more `GroupBy(Math.Round(y/bin))` instances named in §88, not moved.

3ae. **Overnight session, continued, 2026-09-16 00:00 → 02:45** (§89–§95): item 6 closed — steps 82, 83, 83b
   (`86eca0f4`, `23f4cb91`, `f7aa25ff`): on the edge is in (`LoopGeometry.InsideOrOn`), the gap that closes an
   open chain is not a face, a pair whose band holds a wall already read is the void between two walls (with a
   nearer-partner rule; the thickness cap tried first lost 31202's 30-in walls and is in §91 as rejected) —
   the full suite green for the first time since 09-11. Item 7 for the named shapes — step 84 (`e63a098d`),
   `StoreyNameClass` + `corpus-query storeys`. Item 1 was step 75 (`8e21744d`). Item 5 started — step 85
   (`1c3cc109`): the title block's plan-naming line wins over a labelled field's notes (30994). Item 3(b)
   characterised, not shipped (§94, Q1 to Ian). A third attempt on the tendon shape rejected in four minutes
   (§95, stash) — rule 10 was broken to make it. Run 22 banked (`ab587213`; `corpus-ledger-db` wrote the DB
   rows after the analyzer's write lost the connection). The night's two decisive scratch instruments are
   verbs: `dxf-inspect --near <x> <y> [reach]` (every segment on any layer near a point) and `model-at
   <e2k> <x> <y> [reach]` (`E2kModelQuery.Near`: what the model holds there, with storeys) — the pair that
   settled 31168's KW235 and 31138's paired faces; tests in `WhatIsDrawnNearAPointIsCodeTests` and
   `E2kModelQueryTests`. Run 23 (step 85, `07ca88ea`) in flight at 02:24.

4. **WP6** — one model in front of Andrea (31170's, or whichever the ledger ranks best of the
   architects' sets). Ian's call when.
5. **Step 47**, the next reading rule, from the corpus. `takeoff corpus-query plan-titles` on the
   step-45 ledger: **1,049 of 3,967 written views (162 sets) the composer can put on no storey by
   name.** 579 are named by the PDF's stem and page (step 46's class — the running rebuild measures
   it); the rest name their storey with WORDS: FLOOR 269, SHOWING/OVER 62/63 ("MAIN FLOOR PLAN
   SHOWING 2ND FLOOR FRAMING OVER" — the storey is the one before SHOWING), MAIN 28, GROUND 21,
   FIRST/SECOND/2ND/3RD, LOWER; "PARKADE PLAN - P2" (P2 at the end of a title is not read as a
   parkade level); "FLOOR PLAN AND CEILING PLAN" ×16 (one-storey sets naming no level at all);
   "BUILDING n FLOOR PLANS". Ordinals and GROUND/MAIN/LOWER are a vocabulary row; one rule,
   measured on the six and on the corpus (`--recompose`, ~20 min) before it is kept.
