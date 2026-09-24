# CODEX — the silent storeys: a placed sheet, slab outlines read, no floor, and no reason given

**One defect. Open only the files named in §4.** Do not run the test suite, do not build, do not touch
anything outside those files. Answer in prose with file:line citations.

---

## 1. The defect, in one sentence

**A plan sheet is placed on the model's grid, the reader reads slab outlines from it, the storey it names ends
up with no floor plate at all — and the report gives no reason.**

The engineer's own ETABS model has a floor on that storey. Ours has nothing, and nothing in 80 KB of report
says why.

## 2. The witness

`31093-01`, storey **L2**, sheet **`S2.09_1_LEVEL 2 SHOWING LEVEL 3 FRAMING OVER EAST.dxf`**:

| | |
|---|---|
| sheet placed on the model's grid | **yes** |
| slab outlines the reader read from it | **111** |
| floor plates in the model on L2 | **0** |
| her model's L2 floor area | **27,028 sq ft** |
| lines in the report explaining it | **none** |

"None" is exact, and it is the whole defect. The reader has a dozen refusal messages it prints when it throws
an outline away — *"slab edges: N outline(s) would not close"*, *"closed ring of N sq ft on its own — too
small for a floor plate"*, *"CANDIDATE NOT MODELLED"*, *"no wall or column stands anywhere under them"*,
*"lies inside one already written"*, *"an outline that closes through itself"*. **Not one of them appears for
this sheet.** 111 outlines went in and nothing came out and the tool said nothing.

## 3. It is a class, not one set

Measured over run 45 (`ledger-sets-2026-09-24-run45-step140.csv`, a full read of 295 sets on develop
`e1b1d3c1`), across the 57 sets where the engineer's model can be compared:

- **43 storeys** carry **no plate at all** where her model has one — **384,522 sq ft** of her floor area.
- Of those, **37** have a plan sheet that names the storey **and was placed on the grid**. So neither the
  storey ladder nor sheet placement is the fault on those: the read is.
- **0** are storeys whose sheets were named but unplaced. That theory was tested and is dead.
- **9 of the 37 report no reason whatsoever.** They are:

| job | storey | her sq ft | slabs read | sheet |
|---|---|---:|---:|---|
| 31093-01 | L2 | 27,028 | **111** | `S2.09_1_LEVEL 2 SHOWING LEVEL 3 FRAMING OVER EAST.dxf` |
| 31104-01 | L2 | 23,609 | 0 | `S2.05.1_1_LEVEL 2 PLAN - CONCRETE OUTLINE.dxf` |
| 31009-01 | L8 | 14,026 | 0 | `S2.09.1_1_LEVEL 8 PLAN - CONCRETE.dxf` |
| 30989-01 | L3 | 7,750 | 0 | `S2.15_1_LEVEL 4 19 PLAN CONCRETE OUTLINE.dxf` |
| 30989-01 | L4 | 7,728 | 0 | `S2.15_1_LEVEL 4 19 PLAN CONCRETE OUTLINE.dxf` |
| 30849-01 | ROOF | 4,633 | 0 | `S2.12_1_E.M.R.ROOF PLAN.dxf` |
| 31224-01 | L4 | 4,281 | 8 | `S2.06_1_LEVEL 4 PLAN CONCRETE OUTLINE.dxf` |
| 31224-01 | L3 | 4,281 | 8 | `S2.05_1_LEVEL 3 PLAN CONCRETE OUTLINE.dxf` |
| 31053-01 | L2 | 365 | 4 | `S2.06_1_LEVEL 2 PLAN SLAB REINFORCING.dxf` |

**93,701 sq ft.** Note the split: four sheets read outlines and produced nothing (111, 8, 8, 4), and five read
**zero outlines from a sheet whose own title says CONCRETE OUTLINE**. Those may be two different faults; say
so if they are.

## 4. The files — open only these

```
Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs
Kor.Operations.EngineeringTools.Core/Dxf/DxfFloodFillPlateDetector.cs
Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs
Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs
```

The slab pass lives in `StructuralPlanClassifier` (`SplitSlabsAndOpenings`, the chain walk, the
`Refused(...)` calls that print every message quoted in §2, and `PriceSlabsByTheCalloutsInsideThem`). The
perimeter-wall fallback is at `StructuralPlanClassifier.cs:1678`, `:1704` and `:1744`. The composer writes the
plates in `E2kGeometryComposer`.

## 5. The rule the answer is judged against

The engineer's banked ruling, 25 Aug 2026, in `reference_andrea_answers_index` and as an `analysis.Ruling`
row `floor-from-perimeter-wall`:

> *"It should always follow the outer edge of the walls."*
> *"A step line is not the slab edge; the outer continuous line is."*

And this repo's own coverage test already declares that ruling **unobeyed**:

> `RulingCoverageTests`: `["floor-from-perimeter-wall"] = "The fallback exists and runs where a sheet closes
> no slab at all, but no test asserts it fires from HER rule … It also changed twice on 24-25 August, widened
> and narrowed again, with nothing red either time."`

Both fallbacks at `:1704` and `:1744` are gated on `result.Slabs.Count == 0` **per sheet**. Whether that grain
is the fault is exactly what we do not know — it was assumed once already tonight and never verified.

## 6. The three questions, in order

1. **For 31093-01 L2 specifically: where do the 111 outlines go, and why is nothing printed?** Trace the path
   from the read outlines to the composer and name the line that drops them silently. A `continue` with no
   `Refused(...)` beside it is the shape to look for.
2. **Are the five zero-outline sheets a different defect?** A sheet titled `… CONCRETE OUTLINE` that yields no
   slab outline at all has failed earlier than the slab pass.
3. **Is the per-sheet gating of the perimeter-wall fallback right?** A storey drawn by three sheets, one of
   which closes a small wrong ring, gets a plate — so no sheet has `Slabs.Count == 0` and her rule never runs,
   even though the storey ends with no real floor. If that is the fault, say which line should ask the storey
   rather than the sheet; if it is not, say why.

## 7. What NOT to do

- Do not propose relaxing `SlabChainJoinFraction` (the 10% interruption limit). It was measured on 2026-09-24:
  117 refused candidates, 360,244 sq ft, and its largest beneficiary is a set **already reading 106%** of the
  engineer's area. It feeds the over-readers and starves the gap.
- Do not propose a "walls outside the plate" trigger. Also measured and dead: precision never exceeds 30% at
  any threshold; at ≥90% walls outside, only 7 of 45 storeys read under half of hers.
- Do not widen a rule to buy one set. Every rule here is judged on all 57 of her models before it is banked,
  and four rules were killed this week for feeding sets that did not need it.

## 8. Context you may want but should not open

- The one measure: **4,841,728 of 6,278,461 sq ft (77.12%)** of the engineer's plate area, run 45.
- The remaining gap is **1,436,461 sq ft**, and **1,046,910 of it — 73% — sits in the 72 storeys where our
  plate reads under half of hers.** The 43 zero-plate storeys are 384,522 of that.
- Narrative: `docs/pdf-intake/part-3-s61-onward.md`, logs 164–168.

---

# ⚠ CORRECTION, 2026-09-24 07:0X — the audit broke this brief's witness, and it was right to

Codex's answer (`CODEX-SILENT-STOREYS-RESULT.md`) refused to name a cause without evidence and challenged
the one number this brief was built on:

> *"the final sheet table overwrites that count with floor objects attributed to the sheet at
> `DxfToEtabsService.cs:2754`. Thus '111 outlines read' cannot safely be treated as 111 raw candidates."*

**Verified and correct.** `DxfToEtabsService.cs:2754` is `s with { … Slabs = kept.Floors }` — the ledger's
`slabs` column is floor objects attributed to the sheet after the building cut, **not outlines read**. §2's
headline was built on a misread column.

Checking it properly then broke the rest of the brief, and the finding is better than the one it replaces.

## 31093-01 L2 is not silent. It has a floor, in the wrong place.

The model has **13,658 sq ft on L2 — two identical 6,829 sq ft rectangles of FOUR points each**, and the same
two rectangles appear again on L3 and on L4. The yardstick's own note says why it scored zero:

> *"13,659 sq ft of our plates stand beyond her model's footprint on their storey."*

So "reads nothing" in §3 did not mean *no plate*. It meant **no plate area inside her footprint**, which
conflates two unrelated faults. (Her 31093 model is also dated **712 days before the drawing's issue**.)

## Re-measured against the models, the 43 split cleanly in two

| | storeys | her area |
|---|---:|---:|
| **genuinely no floor object on that storey** | **19** | **205,589 sq ft** |
| **floors exist but land outside her building** | **24** | **178,933 sq ft** |

They are different problems with different fixes:

- **The empty 19** — 30989 L1 (32,344), 31104 L2 (23,609), 30986-02 L1 (20,509), 31009 L8 (14,026), 31098 L4
  (13,274), 31017 L6 (13,144), 31065 L5 (10,870), 31005 L1/L5/L8/L10 — is the class the perimeter-wall
  ruling is for, and step 132 already takes 31009 L8 and 31065 L5 from zero to a floor.
- **The misplaced 24** are a registration or a shape fault, not a closure one. 31093's twin 4-point
  rectangles on three consecutive storeys are the witness, and they look like a bounding box rather than a
  floor.

## So the brief stands, narrowed

**Question 1 is withdrawn** — 31093-01 L2 is answered. **Question 2 (the five zero-outline sheets) and
question 3 (the per-sheet gating of the perimeter-wall fallback) stand**, and question 3 gained weight:
Codex's own §6 says the enclosure comparison at `StructuralPlanClassifier.cs:1678` sees only ONE sheet's
walls and slabs, that a perimeter assembled from several sheets is unavailable there, and that merely
swapping `Slabs.Count == 0` for a storey-wide count would still accept a wrong small ring. That is the
architectural limitation, stated without overclaiming a cause, and it is the right next thing.

**The witness for a re-run should be one of the empty 19 — 30989-01 L1, 32,344 sq ft — not 31093-01 L2.**

⭐ **And the lesson, which is this repo's oldest: a count is not a measurement until you know what it counts.**
`slabs` in the sheet ledger means two different things depending on where you read it, and I built a brief on
the wrong one. The audit that refused to guess is what caught it.
