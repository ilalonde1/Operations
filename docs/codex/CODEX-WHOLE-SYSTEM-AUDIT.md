# CODEX — whole-system audit: PDF stick file to ETABS model

**This is a WHOLE-SYSTEM audit, not the usual one-defect brief.** Ian asked for fresh eyes on the
pipeline entire, now that it has a single entry point and a measured triage.

Answer in prose with `file:line` citations. **A reasoned refusal is a valid answer** — the last three
audits each produced one and each was right, and two of them overturned a finding I had recorded as
fact.

---

## 1. What the system is

One command, added 2026-09-24:

```
takeoff stickfile <stickfile.pdf> <outDir>
```

writes the `.e2k` model, `report.txt` (the why for every count), `questions.xlsx` (the engineer's
workbook), `model.png` (every storey on one sheet), `levels.csv`, `sheets.csv`, and the `dxf/` views.

The route is `PdfOnlyBuild.Build`: every page through `DrawingIntake.ReadSheet`, plan sheets written
as named DXF views on the office's layers, the set's storeys read off its elevations, then
`DxfToEtabsService.Run` composes the model.

## 2. How it is judged, and the ONE number

Against **the engineer's own ETABS models**. 50 corpus sets have one. The measure is her plate area
we reproduce.

⚠ **THE DENOMINATOR MOVES.** When the tool builds storeys it did not build before, more of her
storeys become shared and comparable, so ours can rise while the percentage falls. A ratio is only
quotable between two runs that share a denominator. This caught us twice on 2026-09-24.

## 3. ⭐ THE TRIAGE — this is four problems, not one

Every set with her model, classified from its own `yardstick.txt`:

| class | sets | gap sq ft | |
|---|---:|---:|---|
| **D. good — 85%+ of her plate area** | **18** | 216,762 | these work |
| **C. plates short — 50-84%** | **16** | **815,462** | biggest pool |
| **B. plates short — registration fine, under 50%** | 9 | 315,808 | |
| **A. REGISTRATION — under 25% of columns within 100 mm** | **7** | 232,506 | **the model is in the wrong place** |

**36% of sets are already at 85%+.** Class A cannot be fixed by any floor rule: `31005-01` sits
14 m × 44 m from her model and the grid labels both files name disagree by **28.7 m**. It builds 21
floored storeys that score ~0 because they do not overlap her footprint.

## 4. What was banked on 2026-09-24, and what each is worth

| step | what | measured |
|---|---|---|
| 143 | a set that cannot say which storey is handed back as `ModelQuestion` J8 | fires on 20 of 190 |
| 144 | a drawing with no level takes one from where it sits in the sheet order | 99.8% over 1,776; reaches 2 sets |
| **145** | **"LEVEL 9 19 PLAN" is eleven storeys — a lost dash** | **+239 storeys, 0 lost, 16 sets** |
| **147** | **a reinforcing sheet is refused as a plan and still draws the slab** | **30989-01: 7 floors to 45** |
| — | every refusal now states WHY and HOW TO FIX IT | 9 silent messages fixed |

**147 is the largest.** `CONCRETE OUTLINE` sheets frequently close no slab at all while the
`SLAB REINFORCING` sheet of the same storeys does — rebar is drawn inside a bounded slab. Corpus-wide
**1,032 of 1,201 reinforcing views carry slab edges across 129 of 297 sets**; the 68 sets that also
have unplated storeys hold **479 of them** and, where she has a model, **965,538 sq ft of gap**.

The rule takes **the slab edge only**; walls, columns, partitions and tags stay refused, because
30990's footings drawn filled for their bars once read as 54 columns on P3.

## 5. Three build gates now fail the build

- `EveryRuleStatesItsTargetSetBeforeItIsBankedTests` — a bisect knob must name a prediction doc
  stating how many sets it touches, **as a number**. Written because steps 141, 142 and 144 were all
  built before anyone knew whether they touched anything.
- `EveryRefusalSaysHowToFixItTests` — a message saying something was lost must carry an action.
- `ADocCommentBelongsToTheMemberBelowItTests` — a ratchet at 34.

## 6. ⚠ MEASURED DEAD — do not propose any of these

| candidate | measurement |
|---|---|
| widen the flood-fill bridge | 16,296 sq ft against a 583,781 gap — **2.8%** |
| "most of the storey's walls stand outside the plate" | precision never above **30%** |
| relax `SlabChainJoinFraction` | biggest beneficiary already reads **106%** of her area |
| **step 146 — seal the 2-inch leak** | sealed 19 pinches, largest cell stayed **136 sq ft**. The floor is not leaking through one gap; it is **shredded into 161 slivers** — 50 cells holding structure total 413 sq ft |
| step 141, step 142 | earned nothing |

## 7. The questions, in order

1. **Is the four-class triage right, and is class A tractable at all?** Registration is
   `GridAlignment` — by name against the model's GRIDS, then by column registration. On `31005-01`
   only 2 X and 2 Y labels match and they disagree by 28.7 m. Is there evidence in the drawings that
   would place it, or is a class A set one the tool should REFUSE with a question rather than ship
   in the wrong place?

2. **Step 147 changes the six banked baselines. Should they be re-banked?**
   `SixSetsBuildAsBankedTests` is byte-identical against stored baselines and now fails on 31130-01,
   31138-01, 31065-01 and 31202-01. The two-arm differential on 31130-01 says the change is good —
   32 of 33 sheets placed against 19, 1,313 columns against 1,318, ours 252,069 sq ft against
   250,285, same denominator, nothing lost. Is a byte-identical gate the right instrument once a
   rule deliberately changes every model, and what should replace or supplement it?

3. **Where should the next rule go?** Class C holds 815,462 sq ft across 16 sets and is the largest
   pool. Nothing in §6 touches it. What is the shape of the fault there — and is there a measurement
   that would tell us before a rule is written, as the reach gate now demands?

## 8. What NOT to do

- Do not propose a rule judged on one set. Every rule is measured across the sets with her model
  before banking, and the guard sets are `30972-01` (20 of 20 storeys floored, 85%) and `30993-01`
  (39 of 40, now **98%** after 147). If either loses anything, the change is wrong.
- **A wrong floor is worse than a missing one.** It looks exactly like a floor she drew and nothing
  about it asks to be checked. Where the evidence is ambiguous the honest output is the question.
- Do not read an absence in our own output as an absence in the drawing. That mistake was made four
  times on 2026-09-24: a `null` read as "does not close", `0` entities on a layer read as "the
  outline is missing" when it was on `BEAM`, and a render of only the expected layers read as a
  blank drawing.

## 9. Where to read the state

`docs/pdf-intake/RESUME.md` — kept current, holds the triage, the dead ends, the reproduce-it
commands and the errors not to repeat. Then `step143` to `step147-prediction.md`, and
`part-3-s61-onward.md` logs 164-170.
