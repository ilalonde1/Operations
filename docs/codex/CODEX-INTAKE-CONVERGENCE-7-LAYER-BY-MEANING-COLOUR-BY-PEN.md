# Codex 7 of N — the layer carries what he meant, the colour carries what he drew

## Goal

A DXF out of PdfToSafe should be openable by an engineer AND importable into ETABS/SAFE without
choosing between the two. So: **the LAYER says what the shape IS, the entity COLOUR says what pen it
came off, and markup stays distinguishable from the architect's page.**

## Why — the thing that was already there and got thrown away

`PdfToSafeWindow.xaml:269` binds an **ElementType** dropdown per colour: Slab / Column / Wall / Beam.
The engineer says *"red is Wall"* in the tool, at the time, and `ReclassifyByColor` moves the shapes
accordingly. That is his own meaning, assigned by him, stored nowhere.

`DxfExporter`'s default layering (`SLAB`/`COLUMN`/`WALL`/`BEAM`) therefore is **not a size guess on
reclassified geometry — it is his assignment.** In prompt 3 I switched the app to
`layerByColour: true`, which serves "show me my markup" and **discards his element types**. That was
right for one engineer's stated ask and wrong for what the file is ultimately for.

The intent, in the owner's words:

> "Ultimately Omar doesn't really want the DXFs of his markup from a Bluebeam PDF — he wants to go to
> SAFE and ETABS etc with those DXFs. So… get it to export DXF from Bluebeam (or Revit with our
> existing Bridge). Then it can be imported into any program."

That makes the DXF the meeting point for every front-end, which it can now be: prompt 2 gave it
`$INSUNITS` and text, which were the two reasons it could not be.

## What to change, in `DxfExporter.cs`

**1. Per-entity colour, in every mode.** Today colour appears only in the LAYER table. Write group
code `62` on each `POLYLINE` (and its `TEXT`) with the nearest AutoCAD colour index of that shape's
source colour — the `NearestAci` helper already in the file. An engineer then sees his red as red
whatever the layer is. This is additive and changes no layer name.

**2. `-MARKUP` on the structural layer names too.** The structural path writes `SLAB`/`COLUMN`/
`WALL`/`BEAM` and loses the annotation origin, which is the only exact way to tell an engineer's red
from his architect's identical red. A markup shape goes on `WALL-MARKUP`, `COLUMN-MARKUP` and so on.
`layerByColour` already does this and the structural path should match it.

**3. Switch the app back to structural layering.** `PdfToSafeWindow.DoExportDxfAsync` passes
`layerByColour: true`; it should pass `false` (or stop passing it). With 1 and 2 in place that loses
nothing — the colour survives on the entity, the origin survives in the layer name, and the element
type he assigned survives as the layer.

Leave the `layerByColour` mode itself in place. It is still the right answer for "give me the
drawing separated by pen", and `DxfRoundTripGate` exercises both.

## What NOT to do

- Do not touch the classifier, `GeometryFilterService`, `ReclassifyByColor`, or anything in
  `EngineeringTools.Core`.
- Do not change the layer names beyond adding the `-MARKUP` suffix. In particular do not rename
  `SLAB`→`SLABEDG` or `COLUMN`→`_COL` to match the Revit-side layer patterns; whether those patterns
  widen is a rules question and a separate decision.
- Do not build, do not run tests, do not publish. I verify.
- No destructive operations: no deletions, no git commands, no schema changes.

## What I will check

- the app's DXF has layers `WALL` / `COLUMN` / `SLAB` / `BEAM` and their `-MARKUP` variants — his
  element-type assignment, not a colour code
- every entity carries a `62` colour, and Omar's markup reads as red
- his 43 markup shapes are all on `-MARKUP` layers and none of the architect's page content is
- `DxfRoundTripGate`, `WhereTheMarkupLandedMeasurement`, `MarkupSurvivesReclassificationGate`,
  `TheScaleComesOffTheSheetGate` all still green, and both suites
- and then, by hand: whether `dxf-to-etabs` gets further on the result than it did before, which is
  the whole point of the exercise. `dxf.wall-layer-patterns` is `WALL`, so a `WALL` layer should
  match immediately; `_COL` and `SLABEDG` will not match `COLUMN` and `SLAB`, and that gap is the
  next decision, not this one.
