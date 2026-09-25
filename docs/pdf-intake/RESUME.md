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

## ⚠ STEP 147 — THE BIGGEST WIN, AND THE ONE OPEN DECISION

**A reinforcing sheet is refused as a plan and still draws the slab.** `CONCRETE OUTLINE` sheets
often close no slab at all while the `SLAB REINFORCING` sheet of the same storeys does — rebar is
drawn INSIDE a bounded slab. The slab edge only is taken; walls, columns, partitions and tags stay
refused, because 30990's footings drawn filled for their bars once read as 54 columns on P3.

    30989-01:  7 floors -> 45,  7 of 27 storeys floored -> 25 of 27,  columns 594 -> 594
    30993-01 (guard):  ours 510,530 -> 551,272 sq ft on the SAME denominator — 91% -> 98%
    31130-01:  32 of 33 sheets placed (was 19), columns 1,313 (was 1,318), ours +1,784 sq ft

Corpus reach, measured before the rule was written: **1,032 of 1,201 reinforcing views carry slab
edges across 129 of 297 sets**; the 68 sets that also have unplated storeys hold **479 of them** and
**965,538 sq ft** of gap where she has a model.

⚠ Two regressions were caught by the six-set gate and fixed, both the same cause — a slab-only sheet
must never influence WHERE the model sits:

1. it must not be the **reference plan** (the frame every other sheet is set on);
2. it must not **register by columns**, nor be registered against — `MembersOf` reads the RAW
   segments, so its rebar comes back as columns, which is the very fault its refusal exists to stop.
   Before that fix 31130-01's whole model moved **69 metres** in X.

### ⛔ STASHED, NOT BANKED — and the exact reason

`git stash` holds it: *"step 147: reinforcing sheets slab-only (blocked on 31130-01 registration)"*.

**It is NOT on develop.** The six-set gate fails on it, and running that gate with
`KOR_STEP147_OFF=1` **passes**, which proves step 147 is the cause rather than anything banked
earlier. The fault is always the same and always only 31130-01:

    31130-01: plates moved 4, columns lost 600 / gained 600, walls lost 258 / gained 261
    (the second model sits -69,375, +0 from the first)

**The whole model moves 69 metres in X.** The other three sets are clean or better — 31138-01 has
identical columns and walls with plates 12,405 -> 12,652 sq ft, which is exactly what 147 is for.

⚠ **Three fixes were tried and none of them worked**, each closing one place a slab-only sheet could
influence where the model sits:

1. it must not be the **reference plan** — the frame every other sheet is set on;
2. it must not **register by columns**, nor be registered against (`MembersOf` reads the RAW
   segments, so its rebar comes back as columns — the very fault its refusal exists to stop);
3. it must **lend no axes** to the extended grid other sheets are then solved against.

All three are in the stash and all three are right in principle. **The shift survives all of them**,
so there is a fourth path not yet found, and it only shows in the REFERENCE-MODEL build: a two-arm
differential through `stickfile` (no reference model) gives 473 walls / 1,313 columns with 147 on
against 474 / 1,318 off — five columns, no shift at all.

⛔ **Stopped there rather than trying a fourth patch.** Three failed fixes on one symptom is rule
10's trigger: the model in my head is wrong and every further patch is a coin flip. It goes to the
audit as question 2 with all of this, which is worth more than a fourth guess at midnight.

### ⭐ AND THE FIFTH ATTEMPT FOUND WHY IT CANNOT BE SEPARATED (2026-09-25 morning)

The right characterisation was written and built: **a slab-only sheet contributes GEOMETRY and must
never influence PLACEMENT**, so keep it out of `files` entirely and read its slab back AFTER every
placement decision closes, through the frame it earned as a frame carrier.

It took **nothing**, and the instrumented reason is the answer to the whole thread:

    step 147 took nothing from 7 reinforcing sheet(s):
      S2.04_1_LEVEL P3 SLAB REINFORCING PM.dxf: no frame
      S2.06_1_LEVEL P2 SLAB REINFORCING PM.dxf: no frame   ... all seven, no frame

**A reinforcing sheet does not name enough grid axes to place itself.** So the version that WORKED
last night placed those sheets by **column registration on their own rebar** — which is precisely
the mechanism that moved 31130-01 by 69 metres. The win and the regression are the same mechanism.
You cannot keep one and drop the other by subtraction, which is why three patches failed.

⛔ **Both attempts are in `git stash`** (`stash@{0}` structural, `stash@{1}` the working-but-shifting
one). Neither is on develop. Five attempts on one symptom is well past rule 10's trigger.

**What would actually unblock it** — and this is the question for the audit, not another patch:
a reinforcing sheet needs a frame that does NOT come from its own bars. Candidates nobody has
measured: the frame of the sibling view on the same PDF page; a fit solved against the storey's
already-placed outline sheet; or the page's own extraction frame carried through
`SheetViews.Split`, which currently keeps title, filename and geometry but no page provenance.

### What it is worth, so nobody drops it

    30989-01:  7 floors -> 45,  7 of 27 storeys floored -> 25 of 27,  columns 594 -> 594
    30993-01 (guard):  ours 510,530 -> 551,272 sq ft, SAME denominator — 91% -> 98%
    31138-01:  columns and walls identical, plates 12,405 -> 12,652 sq ft

### ⏸ The decision that was deliberately not made alone

`SixSetsBuildAsBankedTests` is byte-identical against stored baselines and now fails on 31130-01,
31138-01, 31065-01 and 31202-01 — because 147 deliberately changes every model with reinforcing
sheets. The two-arm differential says the change is good on every axis. **Either the baselines are
re-banked, or a byte-identical gate is the wrong instrument for a rule that intentionally moves
every model.** Put to `docs/codex/CODEX-WHOLE-SYSTEM-AUDIT.md` question 2 rather than decided here,
because re-banking baselines quietly is how a gate stops meaning anything.

## ⭐ THE WHOLE-SYSTEM TRIAGE — four fault classes, not one problem

Every set with the engineer's model, classified from its OWN yardstick.txt. This is the map: "get
it working on nearly everything" is four different pieces of work, and a rule for one class does
nothing for the other three.

| class | sets | gap sq ft | what it needs |
|---|---:|---:|---|
| **D. good — 85%+ of her plate area** | **18** | 216,762 | nothing; these work |
| **C. plates short — 50-84%** | **16** | **815,462** | the biggest pool; step 147 moves some |
| **B. plates short — registration fine, under 50%** | 9 | 315,808 | floors, as C |
| **A. REGISTRATION — under 25% of columns within 100 mm** | **7** | 232,506 | the model is in the WRONG PLACE |

**36% of sets are already at 85%+.** Baseline, before step 147.

### Class A is a different fault entirely, and 31005-01 is its witness

```
frames matched on 2 X and 2 Y grid labels both models name;
offset (-14,185, -44,708) mm, the labels' own disagreement up to 28,753 mm
ours -> theirs: median 3,919 mm; within 100 mm 0 (0%)
```

The building is **14 m by 44 m out**, and the grid labels both models name disagree by **28.7
metres**. Since step 147 it builds 21 floored storeys — and 17 of them score 0 against her because
they do not overlap her footprint. **No floor rule can ever fix a class A set.** Its 14% is not a
floor problem and must not be read as one.

Worst of class A: `31005-01` 0%, `50054-01` 0%, `31183-01` 3%, `31098-01` 9%, `30986-02` 11%,
`30819-01` 16%, `31158-01` 17%.

⚠ Note `70057-01` reads **144%** of her area with 27% registration — over-reading and mis-registered
at once. A percentage above 100 is a fault, not a success.

## ⚠ READ THIS BEFORE QUOTING ANY PERCENTAGE

**The percentage is NOT comparable across runs, because the denominator moves.** Re-running the 34
under-half sets with 143+144+145 banked:

| | run 45 | now |
|---|---:|---:|
| storeys reading under half | 72 | **95** |
| **our** plate area on them | 3,713,619 | **3,729,302** sq ft |
| **her** plate area on them | 5,093,137 | **5,293,613** sq ft |
| reads | 72.9% | 70.4% |

Ours went UP and the percentage went DOWN. Steps 144 and 145 build storeys we did not have before,
so more of HER storeys are now shared and comparable — the denominator grew by 200,476 sq ft. The
model is strictly better and the ratio is worse.

- `31005-01` — shared storeys 9 → 19, ours **8,035 → 23,468**, hers 75,694 → 160,245.
- `30989-01` — ours unchanged at 61,823, hers **114,633 → 230,558**, under-half 4 → **19**. Step 145
  gave it the `LEVEL 4 19` range, so fifteen storeys now exist and match hers **and every one is
  empty**. The range fix created the storeys; the floor is still missing.

⭐ This is the same fault as reading `slabs` in the sheet ledger, or a `null`, or a bare layer count:
**a number means nothing until you know what it counts.** Quote areas, not ratios, unless both runs
share a denominator.

## THE OPEN QUESTION — where the gap is

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
| **step 146 — seal the pinches** | **WRITTEN, MEASURED, REVERTED.** See below. |

#### Step 146: sealing the leak does not close the floor (tried 2026-09-24 23:5X)

Implemented: when the walk finds no floor, run the existing leak finder from every column in no
cell, seal any pinch narrower than 6 in with a connecting segment, rebuild the arrangement, repeat
up to four rounds. Safe by construction — the block only runs when the sheet would otherwise
produce nothing, so the guard sets never reach it.

On the witness it seals happily and **changes nothing**:

```
step 146 round 1: 161 face(s), largest 136 sq ft; 20 column(s) in no cell;  0 seal(s)
step 146 round 2: 163 face(s), largest 136 sq ft; 19 column(s) in no cell;  7 seal(s)
step 146 round 3: 163 face(s), largest 136 sq ft; 19 column(s) in no cell; 13 seal(s)
step 146 round 4: 163 face(s), largest 136 sq ft; 19 column(s) in no cell; 19 seal(s)
```

⭐ **AND THE TRACE SAYS WHY THE WHOLE LEAK THEORY IS WRONG:**

```
cells by selection: holding structure 50 (413 sq ft), wrapping 0, enclosed 20 (241 sq ft),
                    open to the page 91 (1419 sq ft)
```

**Fifty cells hold structure and they total 413 sq ft.** The floor is not one region leaking through
one pinch — it is **shredded into 161 slivers** by interior linework, and the biggest is 136 sq ft
against a storey of ~4,246. Sealing the boundary cannot help when the interior is already cut to
pieces. The "2 in gap" is real and is a symptom, not the cause.

The work is in `git stash` (not discarded) if the cell-subdivision problem is ever solved and the
seal becomes useful on top of it.

**So the next question is the subdivision, not the boundary:** why do 847 offered lines produce 161
cells on a floor that should be one or two? Interior detail — dimensions, leaders, hatching, grid
strokes — is reaching the arrangement as if it were slab edge.

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

### ⭐ CLASS C CHARACTERISED (2026-09-25) — it is not a separate fault, and the podium is where it lives

Class C was framed as "plates short, 50-84%" — implying a systematic under-read on every storey.
**That was wrong, and measuring it rather than assuming it is the whole point.**

| | |
|---|---:|
| class C gap | 815,462 sq ft |
| **held in under-half storeys** | **537,224 — 66%** |
| spread across every other storey | 278,238 — 34% |

So class C is the SAME fault as class B — a storey with no plate or a tiny one — occurring in sets
that are otherwise fine. Two thirds of it sits in **34 storeys across 13 sets**, and **17 of the 34
are completely empty**.

#### And those 34 are not spread through the building — they are the PODIUM

| band | storeys | gap sq ft | share | avg her area |
|---|---:|---:|---:|---:|
| **L1-L3 (podium)** | **16** | **338,588** | **63%** | **27,970** |
| L4-L9 | 10 | 96,251 | 18% | 10,841 |
| L10+ | 5 | 82,403 | 15% | 17,808 |
| ROOF | 3 | 19,982 | 4% | 6,957 |

**Podium floors are 2.6x the area of a typical storey and that is where the tool fails.** The single
largest holes in the corpus are all low: 70061-01 L2 (58,995), 31202-01 L13 (44,553, empty),
30990-01 L1 (38,642), 31017-01 L3 (33,975), 30989-01 L1 (32,344, empty), 31202-01 L2 (32,014).

#### The 16 podium storeys decompose into three causes, from their own view names

- **Several plans for one storey, one per building.** `Level 3 Plan Tower A` + `Level 3 Plan Tower
  B` (31017-01); `Level 6 Plan Tower A` + `Level 6 Plan Commercial`; `TOWER A - LEVEL 1 PLAN`
  (30990-01). The podium spans both towers and the commercial block, and we take one of them.
  12 of the 16 are drawn by more than one view.
- **The outline sheet closes nothing and the reinforcing sheet does** — 31202-01 L13 is
  `LEVEL 13 PLAN -CONCRETE OUTLINE` + `LEVEL 13 PLAN -REINFORCING`, and it is EMPTY. That is step
  147's fault, which is stashed and blocked.
- **A single sheet whose outline simply does not close.** `31104-01 L2` is the cleanest witness in
  the corpus: ONE view, `S2.05.1_1_LEVEL 2 PLAN - CONCRETE OUTLINE.dxf`, **5,154 BEAM entities,
  ZERO on KOR_C_SLABEDG**, 23,609 sq ft missing. Rendered, the perimeter is there in fragments —
  left edge, part of the top, a long bottom run — with large gaps between, and it never closes.

#### ⛔ AND THE "WE TAKE ONE BUILDING" HYPOTHESIS IS FALSE — checked before it was built

`31017-01 L3` reads 24,307 of her 58,282 and is drawn by `Level 3 Plan Tower A` and
`Level 3 Plan Tower B`. The obvious story is that we take one tower. **We do not.** Three sheets
name L3 and all three are placed and read — S2.10 gives 33 slab outlines, S2.11 gives 13, S2.21
gives 15. Sixty-one outlines for a storey that scores 24,307.

The report says what actually happens:

    L3: a 15,926 sq ft plate stands beyond the storey's 19,162 sq ft floor - another building,
        a ramp, a canopy, a podium edge, or a floor read twice; left as read for the engineer
    L3: a 5,144 sq ft plate stands beyond the storey's 19,162 sq ft floor - ...

The model HOLDS all three plates: 19,162 + 15,926 + 5,144 = **40,232 sq ft**. The yardstick counts
**24,307** because it counts only area INSIDE HER FOOTPRINT — so those plates sit outside the
building she modelled. 31017-01 has two towers and **her model may hold only one of them.**

⚠ **That means part of class C's "gap" is a measurement artefact, not a missing floor**, and the
size of that part is NOT yet known. Before any podium rule is written, the thing to measure is: of
the 34 storeys, how many have OUR plates present but outside her footprint, versus genuinely no
plate at all? The yardstick already prints `ours beyond` per storey — it has not been read.

⭐ **The next rule is about the PODIUM and about a storey drawn by several building plans** —
not about closure thresholds, and not about any of the five candidates already measured dead.

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
