# CODEX — the readable title and the usable drawing are two different files

**One defect. Open only the files named in §4.** Do not run the test suite, do not build, do not
touch anything outside those files. Answer in prose with `file:line` citations. If the answer is
"there is no safe rule here", say that — a refusal with a reason is worth more than a rule that
fires on the wrong sets.

---

## 1. The defect, in one sentence

**A drawing set extracts the same drawing twice — once as a view whose title reads perfectly and
which contains no structural geometry, and once as a view that carries the geometry under a title
the title block scrambled — and nothing pairs them, so the level on the first is never used to place
the second.**

## 2. The witness

`31005-01`, from its own DXF folder (55 files). Two views of the same drawing:

| file | title | places? | geometry |
|---|---|---|---|
| `S0.00_3_LEVEL L01 PLAN CONCRETE OUTLINE.dxf` | **reads perfectly — L01** | **no** | **none** |
| `S2.02.1_1_OUTLINE PLAN CHANGE LEVEL CONCRETE CONSTRUCTION.dxf` | scrambled, level lost | yes | the structure |

The report says why the first does not place, in its own words:

> `S0.00_3_LEVEL L01 PLAN CONCRETE OUTLINE.dxf: no structural outlines found on the expected
> layers — not placed.`

and

> `11 sheet(s) could NOT be set on the grid by name and stay in their own frame — their axes name
> nothing the model or a placed sheet names: … S0.00_3_LEVEL L01 PLAN CONCRETE OUTLINE.dxf,
> S0.00_6_LEVEL L02 PLAN CONCRETE OUTLINE.dxf, S0.00_7_LEVEL L03-04 PLAN CONCRETE OUTLINE.dxf …`

The `S0.00_*` series carries **ten clean level names** — L01, L02, L03-04, L05, L06-07, L08, L09-19,
L20-21, L22, ROOF — and not one of them reaches the drawing that needs it.

The set builds **10 of its 24 storeys with a floor** and reads **10.6%** of the engineer's plate
area — the worst comparable set in the corpus.

## 3. Why this is not the fault we already fixed

Two steps were banked today against this set and **neither one addresses this**:

- **Step 143** (`MostOfTheLadderGotNoFloor`) reports that most of the ladder got no floor and asks
  the engineer which drawing draws each storey. It is a question, not a repair.
- **Step 144** (`LevelsFromSheetOrder`) infers a level from a drawing's position between two
  drawings whose titles did parse. On this set it recovers **2** — L5 and L8 — and it only works at
  all because ordering and claims were scoped to the sheet SERIES, so the empty `S0.00` views stop
  claiming levels they cannot deliver. That scoping is the workaround that makes the empty views
  harmless; it does not make them useful.

The information to place all ten is present in the set and is being thrown away. That is this brief.

## 4. The files — open only these

```
Kor.Operations.EngineeringTools.Core/Dxf/PlanSheetNaming.cs
Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs
Kor.Operations.EngineeringTools.Core/Dxf/LevelsFromSheetOrder.cs
Kor.Operations.EngineeringTools.Core/Intake/SheetDxfName.cs
Kor.Operations.EngineeringTools.Core/Intake/SheetViews.cs
```

`PlanSheetNaming.Parse` turns a view's file name into a `PlanSheetInfo` with its `Levels`.
`DxfToEtabsService` builds `sheetInfoByFile` at **line 689** and matches storeys at **line 1606**
via `PlanSheetNaming.MatchStories`. `LevelsFromSheetOrder` is step 144, and its `SeriesOf` documents
why the two series must not mix. `SheetDxfName` / `SheetViews` are where a page's views are named.

## 5. The three questions, in order

1. **Is a `S0.00_<n>_` view and a `S2.xx.y_1_` view of the same drawing pairable from what the
   intake already knows?** Page number, view index, geometry extent, layer set, grid axes — name the
   evidence that exists at `DxfToEtabsService.cs:689`, and say plainly if none of it is sufficient.
   Do not invent a similarity score.

2. **If they are pairable, where does the level transfer belong?** It has to land before
   `MatchStories` at line 1606 and must not overwrite a title that already parsed. Say which line.

3. **What is the shape of the risk?** The `S0.00` prefix is this office's index sheet. Name at least
   one way a pairing rule could attach a level from one drawing to a genuinely different drawing,
   and what evidence would rule that out.

## 6. What NOT to do

- **Do not propose reading the title block geometry to un-scramble the title.** That is a separate
  and much larger fault (its columns run UP the page and the reader assembles them across), and
  31005-01's siblings `30940-01` and `30941-01` use the same block and **do not print the level as
  text at all**, so no title reader can save them. A pairing rule helps all three; a title reader
  helps one.
- **Do not widen step 144's inference.** Its looser tier was written, measured at 58.9% over 1,776
  drawings, and deleted. The exact tier measures 99.8% and is not the thing to loosen.
- **Do not propose a rule judged on this set alone.** Every rule here is measured across the 48 sets
  where the engineer's own model can be compared before it is banked, and four were killed this week
  for feeding sets that did not need it.

## 7. Context you may want but should not open

- The one measure: **77.12%** of the engineer's plate area, run 45.
- `31005-01` is 1 of **20 of 190** sets that leave half their ladder unfloored (step 143).
- Narrative: `docs/pdf-intake/step143-prediction.md`, `step144-prediction.md`, and
  `docs/pdf-intake/part-3-s61-onward.md` log 169.
