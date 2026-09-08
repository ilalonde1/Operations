# PDF intake — what it does today, and what it leaves on the page

Written 2026-09-08 from the code and from a content inventory of the five local stick files
(31065, 31130, 31138, 31168, 31202: 294 pages). Every number below was counted on the whole
population named; nothing is from a sample. The intake brief series lives in
`docs/codex/CODEX-INTAKE-CONVERGENCE-*.md` (12 so far) and this is the state they have reached.

The purpose of the intake is stated once so the rest can be judged against it: **pull everything a
drawing set carries that any downstream tool could need, once, through one reader, and account for
what was not pulled.** Where this document says "unread", that is a gap against that purpose, not
a note.

## 1. What it is

One PDF reading library, PdfPig, reads every drawing in the engineering path (20 of 20 files that
read PDFs). Nothing rasterises for reading except two previews and one OCR fallback in the WPF app.
There is no OCR in Core; the CLI refuses an image-only set, the WPF `PdfTextWithOcr` reads one.

The base reader is `VectorPageReader.ReadPage` → `PageContent`: positioned words (nearest-neighbour
extractor), vector subpaths (fill, stroke, colour, points), and, when asked, the geometry of
Bluebeam annotations (vertices, lines, ink, appearance streams). Fourteen files consume it.

Three families sit on it:

| Family | Entry | Reads | Produces |
|---|---|---|---|
| **Plan geometry** | `takeoff pdf-takeoff` → `PdfPlanReader.Read` → `GeometryFilterService.Classify` (with `SheetFurniture`, `GridBubbles`) → `DxfExporter` | subpaths + schedule headings + bubbles | a DXF with layers SLAB, COLUMN, BEAM — **no WALL layer** |
| **Schedules and sheet facts** | `MarkRowScheduleReader` (+ `ScheduleTableBorder`), facades `ColumnScheduleReader`, `FootingScheduleReader`; `ScheduleGridReader` (a second, 2-D table engine); `SheetTitleReader`, `SheetScaleReader`, `StructuralGridReader`, `SlabThicknessZoner`, `DrawingDigest` | words + rules | rows (mark, size, strength), sheet level/zone, scale note, grid envelope area, thickness call-outs |
| **Reinforcing** | `RebarPdfReader`, `PdfPageTextReader` → `RebarChangeService`, `RebarOverlayGenerator` | **its own** `page.GetWords()`, not `VectorPageReader` | before/after callout counts, marked-up PDF, xlsx |

Plus `SlabTakeoffEngine` (vector + raster + vision, the `vector-*` verbs) for suspended-slab
quantities, and `StickFileSlabThicknessReader`, the one place the PDF side feeds the DXF→ETABS
publish (`takeoff publish --stick-file`).

## 2. Where each tool stands

| Tool | Path today | What it gets from the PDF | What it does not |
|---|---|---|---|
| **PDF → ETABS** | PDF → DXF (CLI) → `DxfToEtabsService` reads the DXF by layer | slabs, columns, a scale, the schedule's column sizes | **walls** (the DXF has none: 31130 p11 gave 42 COLUMN, 17 SLAB, 1,650 BEAM, 0 WALL), footings as objects, the grid, storey heights, openings from plan text |
| **PDF → SAFE / SAP** | WPF `PdfToSafeWindow` → `PdfGeometryExtractor` → F2K / E2K / CSI API | the same subpaths, parsed by the shared `PdfPlanReader.ParsePage` | **classification is not shared**: it calls `Classify` with no furniture and markup-only by default, so the WPF result differs from the CLI's on the same page and reads a clean issued set as empty. No `.s2k` writer exists; SAP is API-only |
| **Rebar takeoff / change** | `takeoff rebar`, `takeoff overlay`, two WPF windows | callouts by position, sheet identity, deltas | uses PdfPig's default word splitter, which `VectorPageReader` documents as splitting CAD text into single characters; the WPF windows call the page-text `Compare` and the CLI calls `ComparePdfs`, and no test says they agree |
| **Before / after drawings** | there is no general drawing diff; the rebar tools are the comparison | reinforcing callouts only | geometry deltas, moved/added members, revision clouds |
| **Structural takeoff** (`StructuralTakeoffService`) | CSV from Revit | nothing from PDF | — |
| **Slab takeoff** (`SlabTakeoffEngine`) | vector + raster + vision | plates, zones, thicknesses, grid envelope | — |
| **Bluebeam markups** | `VectorPageReader.ReadAnnotationPaths` | markup geometry | **markup text** is read only by the WPF `PdfGeometryParser`; Core never sees the engineer's FreeText |
| **Transmittals, standard details, sheet index** | PdfSharp bookmarks / composition | nothing from drawing content | — |

## 3. What the five sets carry, and what is read

All 294 pages are vector: 0 raster-only pages, text on every page, no PDF layers (OCGs) in any set.

| On the page | Population (5 sets) | Read today | Gap |
|---|---|---|---|
| Vector paths | 296k–901k per set; single-line strokes 95–97% of them; fills 7k–18k | classified into slab / column / line; furniture, frame, grid, underlines discarded | **walls are not emitted**; footing outlines not emitted as footings; hatch regions (material, stirrup zones) unread |
| Schedules | 2–4 schedule sheets per set plus tables on plans | column, footing, flat shear-wall tables by border (harness 54/54) | stirrup, zone, beam, slab-reinforcing, bar-placing tables unread |
| Column / footing marks on plans | 326–1,194 mark spans per set; **rotated in 1,009 of 1,194 on 31168** | found as labels; used for the plan self-check | the self-check's label-to-column match fails on rotated marks: 31130 p11 reports 14 of 42 while all 42 columns are emitted where drawn. The metric is wrong, not the columns |
| Grid system | bubbles with an axis: 31130 19 of 19, 31138 15 of 15, 31202 17 of 115 | detected, used to discard grid lines and to size an envelope | **not exported**: no grid names/positions to ETABS, no registration between sheets or revisions |
| Title block | sheet number, title, revision, date, scale on every sheet | level and zone; scale when parseable | 31130 p11 prints `1/8"` in the title block and `scale-scan` reports "none stated" (0 of 1); revisions, dates, issued-for unread |
| Bookmarks (sheet index) | 59–73 in 4 of 5 sets (31138 has none) | unread in intake (a PdfSharp extractor exists in Rendering) | sheet list and type (plans 22–34, details 8–20, sections 4–7 per set) is free and unread |
| Hyperlinks | 42 / 151 / 140 named links in 3 of 5 sets, callout → sheet | unread | the section/detail cross-reference graph is on the page |
| Bluebeam annotations | 142 on 31065 (Ink 65, Line 50, Square 11, FreeText 7 …) by two authors, e.g. "KOR: add 15M3.11@12 on each side of the wall" | geometry in Core; text only in WPF | engineer's instructions never reach Core |
| Dimension strings | 800–2,300 spans per set | unread | would verify scale and give spans directly |
| Rebar call-outs on plans and details | 690–1,532 spans per set | rebar tools count them for comparison | not placed as quantities; not read by the model path |
| Notes | 2,250–4,425 sentence spans per set: f'c, fy, cover, codes, loads | strengths only where a schedule states them; `elev-scan` for elevations | material and design-basis notes unread |
| Sections / elevations | 4–7 sheets per set | `elev-scan` exists | storey heights are taken from the reference e2k, not the drawings |
| Images | 151–386 placements per set; 3D views tile into 90–168 images each; on plans all <1% of page (logos, stamps) | ignored | nothing structural here, correctly ignored |
| Dashed linetypes | 33 dashed paths in 31065, 0 in the other four | grid lines reassembled from pieces | linetype meaning (hidden, below, property) is lost at export and cannot be recovered |

## 4. Duplicates and bypasses in the code

- **Four text extraction paths**: `VectorPageReader` (nearest-neighbour, boxes kept), `PdfPageTextReader` and `RebarPdfReader` (default splitter), `PublishExplainers.PdfText` (no dedupe). Token boundaries differ on CAD sheets.
- **Four line-grouping rules**, **two table engines** (`MarkRowScheduleReader`, `ScheduleGridReader`) plus a vision-JSON parser, **three scale detectors** (`SheetScaleReader`, WPF `PdfGeometryParser.DetectScaleForLoad` at 72 dpi, `DrawingDigest.ScaleLine`), **three scaling conventions**, **two rasterisers**.
- **The WPF PdfToSafe window bypasses the shared classification** (`PdfGeometryExtractor.cs:97-99`).
- Eleven independent `PdfDocument.Open` call sites outside a reader.
- Tests: of 13 test files exercising `VectorPageReader`, 1 opens a real drawing PDF. `PolygonProcessor`, `PdfPageTextReader`, `DrawingDigestBuilder`, `RebarPdfReader` have no Core test.

## 5. How to audit it so the gaps stay measured

1. **The ledger is the harness.** Port the inventory (`pdf_inventory*.py`, session scratchpad) to a
   `takeoff pdf-inventory` verb: per page, every content class and which reader consumed it, which
   rule discarded it, or **unread**. The unread column, summed over the five sets, is the backlog and
   the number that must fall. A change that reads more moves a class from unread to read and nothing
   else; that is the differential.
2. **Contracts per sheet type.** A plan sheet yields grid, walls, columns, slabs, openings, footings,
   marks with sizes, thickness call-outs, dimensions, callouts, scale and title. A schedule sheet
   yields its tables. A section yields levels and heights. A notes sheet yields materials and
   design basis. Each contract is a banked check on the five sets in `tools/Measure-StickFileSchedules.ps1`
   until it is C#.
3. **Ground truth we already hold.** 31168 has both a stick-file PDF and a Revit DXF export of the same
   sheets. Per sheet, the PDF-derived DXF must carry the walls, columns and slabs the Revit DXF
   carries. That comparison needs no engineer and would have shown the missing WALL layer on day one.
4. **One reader.** Every text read goes through `VectorPageReader`; the WPF window calls
   `PdfPlanReader.Read` with furniture; one scale policy; one scaling convention.
5. **Render and look.** A `pdf-overlay` verb that draws the extracted objects on the rasterised page,
   for every plan page, is the human check in seconds per sheet. The two pictures that found the
   missing walls were made by hand today.
6. **Widen the population.** Five sets are a sample. The job folders on the share hold the stick files
   of the 1,126-model corpus; the ledger runs on all of them (filename search only, never a content
   walk over SMB).
