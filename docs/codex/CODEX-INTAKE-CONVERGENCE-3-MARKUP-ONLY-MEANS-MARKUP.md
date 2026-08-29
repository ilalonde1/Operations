# Codex 3 of N — a markup-only read must be markup-only all the way out

## Goal

Three faults in one path — the Bluebeam-markup-to-DXF path. They belong in one change because each
one silently undoes the others.

The job this tool does, in the owner's words: *"taking markups made on a Bluebeam file and being
able to see just those — removing all the architectural crap."* Not "convert a drawing to DXF".

---

## Fault 1 — text ignores the markup filter, so the architect's title block rides along

`GeometryFilterService.Classify` honours `annotationsOnly` and drops page content. `ExtractTextAnnotations`
does not: it returns `page.GetWords()` — every word on the sheet — and `PdfGeometryExtractor.Extract`
assigns it unconditionally (`PdfGeometryExtractor.cs:142`).

That was harmless until this morning, because the DXF writer emitted no text at all. It emits text
now (prompt 2), so **a markup-only DXF today carries 43 polylines of the engineer's walls and 323
words of the architect's practice name, address and title block.**

**And it drops the only words that matter.** On `~/Desktop/OAP-parcel11-arch-markup.pdf`, the
annotations carry their own `/Contents`, and they are engineering:

```
27  Square    12" x 30"        the engineer's shear-wall sections
 4  Polygon   252.9 sq in      the corner columns (Bluebeam area measurements)
 1  Line      20'-5"           his dimension
```

`12" x 30"` is a section size. `PdfToSafe` already has `BeamSectionParser` and `ColumnSectionParser`
that read that form, and the ETABS side reads slab thickness from text sitting inside a plate. So
this is not cosmetic — it is the difference between carrying the engineer's own sizes across and
carrying his architect's postal address.

**Wanted:** text obeys the same rule as geometry. A markup read yields the markup's own words —
annotation `/Contents` (and FreeText), positioned at the annotation — and a whole-page read yields
page words exactly as today.

**Expected after, on that file, markup-only:** ~32 text entities reading `12" x 30"`, `252.9 sq in`,
`20'-5"`. Not 323. Say the count you get.

Use `ann.Rectangle` for position — PdfPig normalises it, so it is already in page space. **Do not use
raw `/Rect` numbers**; that is the bug prompt 1 fixed.

---

## Fault 2 — `ReclassifyByColor` throws away which shapes were markup

`ExtractedGeometry` carries `SlabIsAnnotation`, `ColumnIsAnnotation`, `LineIsAnnotation`. They are
the only exact way to tell an engineer's red from an architect's red, and on Parcel 11 both are
`#F00000` — his shear walls and the property line round the site.

`ReclassifyByColor` builds a fresh `ExtractedGeometry` and copies metadata, text, colours, sizes and
hints — **but not those three flags** (`PdfGeometryExtractor.cs:274` and its add paths). `DxfExporter`
only writes `-MARKUP` when they are present, so after any colour reclassification
`PDF-F00000-MARKUP` collapses back into `PDF-F00000` and the engineer's markup is welded to the
architect's boundary again.

**Wanted:** the flags survive reclassification, exactly parallel to the colours already copied
beside them.

---

## Fault 3 — the colour layering is unreachable from the app

`DxfExporter.Export` takes `layerByColour` and defaults it false. `PdfGeometryExtractor.ExportDxf`
does not accept it, so it cannot be passed, and `PdfToSafeWindow.DoExportDxfAsync` cannot pass it.
The only call with it set is a measurement test. **Every DXF the product has ever written is layered
by the size-guess — `SLAB`/`COLUMN`/`BEAM` — which is the classification this whole exercise exists
to stop relying on.**

**Wanted:** `ExportDxf` accepts it and the app's DXF export passes `true`.

Reasoning, so it is a decision and not a default: on a markup, **the colour IS the meaning the
engineer assigned.** Red walls and blue slab edges are his separation, made by him, at the time. A
size threshold applied afterwards is a guess about what he meant. Keep `false` as the method default
so no other caller changes.

---

## Constraints

- Files: `PdfGeometryParser.cs`, `PdfGeometryExtractor.cs`, `PdfToSafeWindow.xaml.cs`. Nothing else.
- **Do not build, do not run tests, do not publish.** I verify.
- No destructive operations: no deletions, no git commands, no schema changes.
- The whole-page path must be **unchanged** — `ExtractWholePageForMeasurement` and the round-trip
  gate depend on page words still being page words there. This is about the markup path only.
- Do not touch the classifier, the scale reader, or `VectorPageReader`. Prompts 4 and 5.
- If an annotation has no `/Contents`, emit nothing for it. Do not synthesise a label.

## What I will check

- markup-only export: text count ~32, and the strings are the engineer's, not the architect's
- whole-page export: still 323, unchanged
- `-MARKUP` layer still present after a colour reclassification
- the app's exported DXF has `PDF-*` layers, not `SLAB`/`BEAM`
- `DxfRoundTripGate` and `WhereTheMarkupLandedMeasurement` still green, and the full app suite
