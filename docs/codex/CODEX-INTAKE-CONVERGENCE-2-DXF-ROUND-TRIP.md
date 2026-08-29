# Codex 2 of N — make the exported DXF survive a round trip

## Goal

In `Kor.Operations.App/EngineeringTools/PdfToSafe/DxfExporter.cs`, emit the two things a DXF must
carry for anything downstream to read it: **its units**, and **its text**.

Right now a PDF-derived DXF is unitless and wordless. `DxfToEtabsService` rejects it outright, and
every slab thickness and section call-out the PDF carried is silently gone.

## Why, precisely

Your own audit found both (`docs/codex/CODEX-DRAWING-INTAKE-CONVERGENCE-AUDIT-RESPONSE.md`,
findings 2 and 3). I verified both in the code before writing this:

- `DxfExporter` writes `$ACADVER`, `$EXTMIN`, `$EXTMAX` and ends the header. **No `$INSUNITS`.**
  `DxfPlanReader.UnitInInches` returns null without it, and `DxfToEtabsService` throws:
  *"does not declare $INSUNITS, so there is no way to know whether it is drawn in inches, feet or
  millimetres. Every size rule and every coordinate depends on that."*
- The writer emits `POLYLINE`/`VERTEX` only. `ExtractedGeometry.TextAnnotations` already holds every
  word with its position (`PdfGeometryExtractor.cs:61`), and `StructuralPlanClassifier` reads slab
  thickness from text sitting inside a plate. That path is dead the moment a PDF goes through this.

## The contract, read out of the reader so you do not have to guess

**Units.** `DxfPlanReader.UnitInInches` scans the HEADER for `$INSUNITS`, skips group code `70`, and
maps the value: `1`=inches, `2`=feet, `4`=millimetres, `5`=cm, `6`=metres; `0` is unitless and is
rejected. `ExtractedGeometry` is in **millimetres**, so the value is **4**.

**Text.** `DxfPlanReader.ReadTextTag` needs, on a `TEXT` entity: code `8` (layer), code `1` (the
string), code `10` (x), code `20` (y). It keeps the tag only when the text is non-empty and BOTH
coordinates are present. Emit code `40` (height) as well so the entity is valid in R12 — the reader
ignores it, AutoCAD does not.

## The trap

`DxfExporter.Export` recentres every shape on a weighted centroid (`cx`, `cy`) before writing. **Text
must be recentred by exactly the same `cx`, `cy`**, or every call-out lands offset from the plate it
labels — which is worse than dropping it, because it looks right.

## What done looks like

I will write the round-trip gate and run it. It asserts, on a PDF-derived DXF:

- `DxfPlanReader.UnitInInches` returns non-null and correct
- `DxfPlanReader.ReadPositionedTags` returns a tag count matching `ExtractedGeometry.TextAnnotations`
- `DxfPlanReader.ReadSegments` returns a segment count matching what was exported
- the bounding box of the re-read geometry matches the exported geometry, within a tolerance
- text positions sit inside that same bounding box — the check that catches the centroid trap

## Constraints

- Touch only `DxfExporter.cs`.
- **Do not build, do not run tests, do not publish.** I verify.
- No destructive operations: no deletions, no git commands, no schema changes.
- Existing callers must be unaffected in geometry. Adding `$INSUNITS` and `TEXT` entities is
  additive; the `POLYLINE` output must be byte-identical to today for the same input.
- Text goes on its own layer. Follow the existing layering convention: under `layerByColour` name it
  `PDF-TEXT`, otherwise `TEXT`.
- If `ExtractedGeometry.TextAnnotations` is empty, emit no `TEXT` entities and no empty layer.
- Do not "improve" the classification, the colour layering, or the scale handling. Those are prompts
  3 and 4 and mixing them in makes this one unverifiable.

## One question I want answered, not assumed

`ExtractTextAnnotations` returns every WORD on the page (`page.GetWords()`), which on Parcel 11 is
the whole title block, the room schedule and every dimension string — order of a thousand entities.
Say what count you expect on that sheet. If dumping all of them into the DXF is wrong, say so and
say what you would filter on instead — but do not filter on your own judgement without saying so,
because a call-out silently dropped is exactly the failure this prompt exists to end.
