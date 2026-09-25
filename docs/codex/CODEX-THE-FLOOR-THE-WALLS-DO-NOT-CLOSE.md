# CODEX — the floor the walls will not close, and two triggers already measured dead

**One defect. Open only the files named in §5.** Do not run the test suite, do not build, do not
touch anything outside those files. Answer in prose with `file:line` citations.

**A reasoned refusal is a valid answer.** Two candidate rules have already been measured and killed
(§4). If the third is also unsafe, say so and say what evidence would be needed — that is worth more
than a rule that fires on the wrong storeys.

---

## 1. The defect, in one sentence

**A storey whose slab edge does not close gets no floor plate, even though the engineer's standing
ruling says exactly what to draw — the outer edge of the perimeter walls — and the code that would
do it is gated so that it almost never runs.**

## 2. Why this one and not another

It is, as far as the corpus can measure, **the whole remaining problem**.

| | |
|---|---:|
| sets where the engineer's model can be compared | 48 |
| total plate-area gap to her | **1,436,462 sq ft** |
| sets with at least one storey reading under half of hers | **34 of 48** |
| **their share of the gap** | **1,379,518 sq ft — 96%** |
| storeys involved | 72 |

Everything else banked this week — a scrambled title block, a lost dash in a range, a level inferred
from sheet order — moves storeys. This moves the area.

## 3. The witness

`30989-01`: **27 storeys, 7 floor objects.** Its sheets place correctly (17 of 17 set on the grid by
axis name) and, since step 145, name their storeys correctly. The report says why there is no floor:

> `S2.15_1_LEVEL 4 19 PLAN CONCRETE OUTLINE.dxf: No slab edge on this drawing would close, and the
> walls enclose a ring of 625 sq ft that stands over N of the storey's M columns: a core, not the
> floor. The storey has no plate.`

So the fallback **did** run here and correctly refused a stair core. The drawing's wall panels do not
enclose the floor within a doorway. A second witness of the same shape, from `31005-01`:

> `S2.11.1_4_UPPER ROOF PLAN.dxf: No slab edge on this drawing would close, and the 3 wall panels do
> not enclose the floor within a doorway (1829): the ring closes not within eight doorways.`

## 4. ⚠ TWO TRIGGERS ARE ALREADY MEASURED AND DEAD. Do not propose either.

**(a) Widen the bridge until the ring closes.** The code already computes this — it probes 2×, 4×
and 8× a doorway purely to *report* where the ring would close, and throws the answer away
(`StructuralPlanClassifier.cs:1729-1739`). Measured across 263 corpus reports on 2026-09-24:

| bridge | in feet | sheets | sq ft recovered |
|---:|---:|---:|---:|
| 3,658 mm | 12 ft | 38 | 51,066 |
| 7,315 mm | 24 ft | 58 | 187,792 |
| 14,630 mm | 48 ft | 22 | 53,973 |

Only the 12-ft tier is defensible — a garage door or a ramp. At 24 and 48 ft the "opening" is a
missing building face. **On the sets where her model can score it, the 12-ft tier is worth 16,296 sq
ft against a 583,781 sq ft gap — 2.8%.** It is not the answer.

**(b) "Most of the storey's walls stand outside the plate."** Measured earlier the same week:
precision never exceeds 30% at any threshold, and at ≥90% walls-outside only 7 of 45 storeys
actually read under half of hers. It fires on storeys that are already correct.

**(c) Do not relax `SlabChainJoinFraction`** (the 10% interruption limit). Measured 2026-09-24: 117
refused candidates, 360,244 sq ft, and its largest beneficiary is a set **already reading 106%** of
her area. It feeds the over-readers and starves the gap.

## 5. The files — open only these

```
Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs
Kor.Operations.EngineeringTools.Core/Dxf/DxfFloodFillPlateDetector.cs
Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs
Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs
```

The fallback is `StructuralPlanClassifier.cs:1698-1742`, gated on `result.Slabs.Count == 0` at
`:1705` and again at `:1745`. `DxfFloodFillPlateDetector.EnclosedByWallPanels` (`:41`) is what tries
to close the ring. `DxfToEtabsService.SettleFloorsAcrossSheets` is the existing cross-sheet pass and
is the only place that already reasons about a STOREY rather than a sheet.

## 6. The three questions, in order

1. **Is the per-SHEET gate the fault, and can it be asked of the STOREY instead?** Both gates read
   `result.Slabs.Count == 0`, which is one sheet's slabs. A storey drawn by three sheets, one of
   which closes a small wrong ring, has no sheet with zero slabs, so her ruling never runs on that
   storey at all. Name the line that would have to change and what it would need that it does not
   have. ⚠ Note the constraint found on 2026-09-24: the enclosure comparison sees only ONE sheet's
   walls, so a perimeter assembled from several sheets is not available at that point.

2. **This was tried once and failed — say why.** Step 142 combined a storey's walls across its
   sheets and called `EnclosedByWallPanels` on the union; it returned **null on all 42 combined
   walls**. Combining the walls did not make the ring close. Is that a property of the algorithm (it
   needs a connected chain) or of the drawings (the perimeter genuinely is not drawn on any sheet)?
   The answer decides whether this is fixable at all.

3. **Is "the outer edge of the walls" reachable WITHOUT a closed ring?** Her words are
   *"it should always follow the outer edge of the walls"* and *"a step line is not the slab edge;
   the outer continuous line is."* Neither requires a closed loop. If an outer boundary of the wall
   footprints can be taken directly — rather than by flood-filling to a ring — say where that would
   live and what it would get wrong. If it cannot be done safely, say that.

## 7. The rule the answer is judged against

Her banked ruling, 25 Aug 2026, `analysis.Ruling` row `floor-from-perimeter-wall`:

> *"It should always follow the outer edge of the walls."*
> *"A step line is not the slab edge; the outer continuous line is."*

And this repo's own coverage test **already declares that ruling unobeyed**:

> `RulingCoverageTests`: `["floor-from-perimeter-wall"] = "The fallback exists and runs where a sheet
> closes no slab at all, but no test asserts it fires from HER rule … It also changed twice on 24-25
> August, widened and narrowed again, with nothing red either time."`

## 8. What NOT to do

- Do not propose a rule judged on one set. Every rule here is measured across the 48 sets with her
  model before it is banked, and four were killed this week for feeding sets that did not need it.
- Do not propose anything that gives a plate to a storey that already has one. 30972-01 (20 of 20
  storeys floored, 85% of her area) and 30993-01 (39 of 40, 91%) are the guard sets: if either moves,
  the rule is wrong.
- **A wrong floor is worse than a missing one.** It looks exactly like a floor she drew and nothing
  about it asks to be checked. Where the evidence is ambiguous the honest output is the question
  (`ModelQuestion` J8), not a guess.

## 9. Context you may want but should not open

- The one measure: **77.4%** of her plate area, run 45, 48 comparable sets.
- Narrative: `docs/pdf-intake/part-3-s61-onward.md` logs 164–169, and
  `docs/pdf-intake/step143-prediction.md` through `step145-prediction.md`.
- The previous audit on a neighbouring fault, and its refusal:
  `docs/codex/CODEX-THE-READABLE-TITLE-AND-THE-USABLE-DRAWING-RESPONSE.md`.
