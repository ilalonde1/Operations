# PDF intake — what it does today, and what it leaves on the page

Written 2026-09-08 from the code and from a content inventory of the five local stick files
(31065, 31130, 31138, 31168, 31202: 294 pages). Every number below was counted on the whole
population named; nothing is from a sample. The intake brief series lives in
`docs/codex/CODEX-INTAKE-CONVERGENCE-*.md` (29 so far; 23 was implemented by Codex and verified 2026-09-08 — the window's extractor is the CLI's `PdfPlanReader.Read` call, `TheWindowReadsWhatTheCliReadsTests`; 24 was Codex's audit, answered in §18) and this is the state they have reached.

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
| **Plan geometry** | `takeoff pdf-takeoff` → `PdfPlanReader.Read` → `GeometryFilterService.Classify` (with `SheetFurniture`, `GridBubbles`) → `DxfExporter` | subpaths + schedule headings + bubbles | a DXF with layers SLAB, COLUMN, BEAM, WALL (filled walls §9, their piers §21, and walls drawn as two cut-pen faces §28), FOOTING, GRID |
| **Schedules and sheet facts** | `MarkRowScheduleReader` (+ `ScheduleTableBorder`), facades `ColumnScheduleReader`, `FootingScheduleReader`; `ScheduleGridReader` (a second, 2-D table engine); `SheetTitleReader`, `SheetScaleReader`, `StructuralGridReader`, `SlabThicknessZoner`, `DrawingDigest` | words + rules | rows (mark, size, strength), sheet level/zone, scale note, grid envelope area, thickness call-outs |
| **Reinforcing** | `RebarPdfReader`, `PdfPageTextReader` → `RebarChangeService`, `RebarOverlayGenerator` | **its own** `page.GetWords()`, not `VectorPageReader` | before/after callout counts, marked-up PDF, xlsx |

Plus `SlabTakeoffEngine` (vector + raster + vision, the `vector-*` verbs) for suspended-slab
quantities, and `StickFileSlabThicknessReader`, the one place the PDF side feeds the DXF→ETABS
publish (`takeoff publish --stick-file`).

## 2. Where each tool stands

Measured at step 0 (2026-09-08). The step sections from §7 on record what has changed since:
walls are emitted (§9), only plans are taken off to DXF (§11), the scale is accounted for on
every sheet (§12), footings are objects (§13, §14), the grid is named axes on a GRID layer (§15),
storey heights are read off the wall elevations (§16) and checked against the model in the publish
(§17), the audit's eleven findings are fixed (§18), dimension strings are typed with their values
(§19), and an AS NOTED sheet's views are read at their own captions' scale (§20). The tables in
§2 and §3 are the starting picture, except the PDF → SAFE / SAP row updated for step 23.

| Tool | Path today | What it gets from the PDF | What it does not |
|---|---|---|---|
| **PDF → ETABS** | PDF → DXF (`pdf-takeoff --kor-layers`, one DXF per sheet named as a view) → `DxfToEtabsService` reads it by layer and sets each sheet on the model's grid by the names of its axes (§23) | slabs, columns, walls as piers (§21), a scale, the schedule's column sizes, the grid by name, the storey from the sheet's name | **floor plates** on a parkade plan (no filled region; the perimeter is two face lines the wall rule does not read), footings (SAFE's, not ETABS's — the FOOTING layer is for the SAFE window), storey heights (read, §16, not yet written to a shell), openings from plan text |
| **PDF → SAFE / SAP** | WPF `PdfToSafeWindow` → `PdfGeometryExtractor` → `PdfPlanReader.Read` → F2K / E2K / CSI API | the CLI's shared page reading and classification, including sheet furniture, walls, footings and grid axes; the window offers Read the mark-up (default) and Read the drawing, and adds markup text annotations | No `.s2k` writer exists; SAP is API-only. Step 23's code change awaits the verifier's tests and window check |
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
   design basis. Each contract is a banked check on the five sets in `FiveStickFilesTests`
   (Core.Tests, `Speed=Slow`), which replaced the PowerShell harness on 2026-09-08.
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

## 6. Step 0, done 2026-09-08: the instruments, and the starting numbers

Three verbs in the compiled CLI and one test class, no product code changed:

| Instrument | What it is | Run |
|---|---|---|
| `takeoff pdf-inventory` | the ledger: every word and path counted once under read / discarded / unread / ignored / unaccounted, plus context rows; `--json` banks it | `takeoff pdf-inventory <pdf> --scale 96 --json out.json` |
| `takeoff pdf-overlay` | what was extracted drawn over the rasterised page: slabs grey, columns blue, walls dark red, footings orange, leftover lines red, mark-shaped words green | `takeoff pdf-overlay <pdf> 11 out.png --scale 96` |
| `takeoff pdf-vs-dxf` | the differential against ground truth we own: PDF-side reads against the Revit DXF of the same sheets, per sheet number | `takeoff pdf-vs-dxf <pdf> <dxfFolder> --scale 96` |
| `takeoff intake-baseline` | the thirteen plan DXFs of the five sets (31130 p11–13, 31138 p9–11, 31168 p11–13 at 1:96; 31065 p14–16 at 1:100; 31202 p17 at 1:96), compiled defaults, the drawing read, plans only — written to a folder named for the step | `takeoff intake-baseline %LOCALAPPDATA%\Temp\kor-drawings\stickfiles %LOCALAPPDATA%\Temp\kor-drawings\harness\stepN-after` |
| `takeoff dxf-census` | the differential's eyes: entities per layer, per file, before against after; `--only` names the layers the step is meant to touch and exits 2 when any other moved | `takeoff dxf-census …\step9-after …\step10-after --only GRID` |
| `FiveStickFilesTests` | the harness, in C#: footing schedule, column marks and route, wall rows, coverage floors, mark-shaped unplaced, a fate for every path, walls, footings, grid axes and storeys read on the banked pages; 40 checks over 5 jobs (20 at step 0); FAILS when the local mirror is missing | `dotnet test --filter FullyQualifiedName~FiveStickFilesTests` |

**The procedure every step follows**, and the one a reader of this document can repeat: write the
baseline before the change with `intake-baseline` to `stepN-1-after`, make the change, write
`stepN-after`, run `dxf-census` between them with `--only` naming the layers the rule is meant to
touch, run `pdf-inventory` on the five sets and check the totals did not move, look at one
`pdf-overlay` crop, run the fast suite while iterating and the full suite before the commit. Until
step 10 the census was a scratch Python script; it is a compiled verb now, and the baseline list
lives in `IntakeBaseline.Jobs` instead of a shell loop.

**The ledger's starting numbers**, each word, path and sheet fact counted once (`%LOCALAPPDATA%\Temp\kor-drawings\harness\ledger-<job>.json`):

| Set | Pages | Items | Read | Discarded | Unread | Ignored | Unaccounted |
|---|---|---|---|---|---|---|---|
| 31130-01 | 60 | 568,310 | 85,133 | 9,165 | 32,392 | 411 | 441,209 |
| 31168-01 | 41 | 442,611 | 68,851 | 8,796 | 18,990 | 92,270 | 253,704 |
| 31138-01 | 61 | 531,450 | 88,526 | 8,001 | 33,191 | 421 | 401,311 |
| 31065-01 | 73 | 845,192 | 123,020 | 15,127 | 39,852 | 731 | 666,462 |
| 31202-01 | 59 | 1,006,086 | 105,750 | 24,340 | 34,763 | 235 | 840,998 |

Unaccounted is 57–84% of every set and is almost entirely one row: paths the classifier did not
emit, which today mixes grid lines the furniture rule dropped with wall faces nothing read. Step 1
splits that row per path; the number that must fall is Unaccounted first, then Unread.

**What the instruments found on their first run**, none of it known before:

- The scale reader reads the title-block scale on **79 of 294 pages**: 0 of 60 on 31130, 0 of 41 on
  31168, 0 of 61 on 31138, 35 of 73 on 31065, 44 of 59 on 31202. The scale is printed on every one
  of those sheets; the three sets it reads nothing from are the Bluebeam-stapled KOR sets.
- 31130 and 31168 carry an outline tree (the sheet index) that PdfPig's bookmark reader returns
  nothing for. 31138 has none; 31065 and 31202 read fine (73 and 59 entries).
- Against 31168's Revit DXF, over the 24 issued sheets both sides hold: PDF walls **0**, DXF walls
  **892**; PDF slabs **781**, DXF slabs **45**; column counts equal on 8 of 24 sheets, and within
  six on every single-view sheet. The multi-view sheets (S2.21.1 and up) need the per-view split
  before their column deltas mean anything.
- The plan self-check's 14 of 42 on 31130 p11 is the label match, not the columns: the overlay
  shows all 42 columns on the drawing's columns.

**What step 0 does not do:** it changes no reader and moves no number. It cannot tell a dropped grid
line from an unread wall face (step 1). The differential has no registration, so it counts and
does not place. The ledger's word kinds are shape rules and "other" is 25,950 words on 31130 alone.

## 7. Step 1, done 2026-09-08: one record, one home, a reason for every path

Codex brief 13, verified: the thirteen baseline DXFs are byte-identical; 930 of 930 tests pass
(889 fast, 20 harness, 21 Intake); the classifier's conditions are unchanged and every one of its
seventeen decisions now records a fate.

What exists now, under `Core/Intake/`: `DrawingIntake.Read(pdf, request)` → `DrawingSetRecord` →
`SheetRecord` per sheet, carrying everything today's readers produce plus `PathFates` and
`WordFates`. `SheetInventory` is a report over the record and derives nothing itself. `pdf-takeoff`
and `pdf-inventory` both read from the record.

**Two defects the step's own verification found, one in the step and one in the intake:**

1. **The ledger's population was the classifier's read, not the page.** The classifier thins points
   closer than `MinVertexDistanceMm` and drops any subpath left with fewer than two, and the first
   version of the intake counted only what survived: on 31130 p13 it reported 4,129 paths fully
   accounted for and said nothing about the other 41,386 (hatching, mostly). Fixed in the step: the
   record's `Content` is the unthinned read, the classifier still receives exactly the thinned read
   the baseline depends on, every fate is mapped back by subpath ordinal, and what thinning removed
   is `CollapsedByThinning`. `FiveStickFilesTests` now asserts, page by page, that every path on
   the page has exactly one fate.
2. **518 of 31130's 1,557 "slabs" are invisible clipping rectangles.** The report first printed fates
   for inked paths only and kept "no ink: discarded" as a row of its own, and the two disagreed by
   518: those paths' fates said `BecameSlab`. The invisible-ink rule covers paper *fills*; a closed
   path with neither fill nor stroke passes the slab test and is written to the DXF. Across the five
   sets: 518, 1,417, 986, 1,298, 1,697. This is the fault behind "PDF slabs 781 vs DXF 45" in the
   differential, and it is step 2's first change, not step 1's — step 1 alters no geometry.

**The ledger after step 1**, each word, path and sheet fact once:

| Set | Items | Read | Discarded | Unread | Unaccounted | of which collapsed by thinning | no-ink paths emitted as geometry |
|---|---|---|---|---|---|---|---|
| 31130-01 | 568,310 | 8,685 | 420,927 | 32,392 | 105,895 | 176,882 | 518 |
| 31168-01 | 442,611 | 11,997 | 239,853 | 18,990 | 79,501 | 16,582 | 1,417 |
| 31138-01 | 531,450 | 9,966 | 374,340 | 33,191 | 113,532 | 10,181 | 986 |
| 31065-01 | 845,192 | 16,378 | 649,333 | 39,782 | 138,968 | 9,333 | 1,298 |
| 31202-01 | 1,006,086 | 8,815 | 816,070 | 34,763 | 146,203 | 96,006 | 1,697 |

Unaccounted fell from 57–84% of every set to 14–19%, and what remains is now named: paths emitted
as lines of unknown meaning (76,448 on 31130, where the walls are), words no kind matched, and
letters outside any bubble. "Read" fell too, from 85,133 to 8,685 on 31130, because emitted lines
are no longer counted as read — a BEAM-layer polyline is not knowledge, and the old number was
flattering the intake. That is the honest baseline the walls step is measured from.

## 8. Step 2a, done 2026-09-08: a path that draws nothing is not geometry

Codex brief 14, verified. One rule added to the classifier: a content path with neither fill nor
stroke is `NoInk` and discarded; annotations exempt. The first change to what the intake emits, and
the differential says it did exactly one thing:

- **Thirteen baseline DXFs**: COLUMN and BEAM counts identical on 13 of 13; SLAB counts down on 13
  of 13 (31130 p11 17→11, p12 36→23, p13 18→16; 31168 p11 51→42; 31202 p17 63→18; 31065 p14 72→46);
  the two Bluebeam markup layers on 31065 p15 unchanged (133 and 37). Nothing else differs.
- **The ledger**: `paths: NoInk` equals each set's no-ink total (7,489 / 3,765 / 6,745 / 12,326 /
  15,371); the "no-ink paths emitted as geometry" context row is 0 on all five; `BecameSlab` on
  31130 fell 1,557 → 1,039, the 518 exactly; every other row unchanged; totals unchanged.
- **Against 31168's Revit DXF**: PDF slabs 781 → 483 over the 24 issued sheets (DXF 45); columns
  unchanged, equal on 8 of 24 sheets as before.
- 939 of 939 tests. Scope: the three named files plus one new test.

The 438 slabs still in excess of the Revit count on 31168 are drawn geometry — footing outlines,
core outlines, notes-box borders that escape the furniture rule — and are the subject of the walls
and footings steps, where a closed outline becomes the object it is instead of a floor plate.

## 9. Step 2b, done 2026-09-08: a wall is a filled rectangle of wall proportions

Codex brief 15, verified with two corrections of mine (half an inch of slack on the limits, the DXF
side's own allowance, so a wall drawn at exactly 4" or 48" is not refused by floating point; and the
differential's walls column, which my step 0 code had hard-coded to zero). 960 of 960 tests.

**What the PDF side reads now**: a filled, non-paper, four-vertex rectangle on a plan whose narrow
side is 4"–60" (the banked `dxf.max-wall-thickness` row, not the code's 36), long side ≥ 48" and at
least twice the thickness, is a wall — after the declared-column-size rule, which wins. Its outline,
axis and thickness go into `ExtractedGeometry.Walls`, its fate is `BecameWall`, and the DXF carries
it on the WALL layer the DXF-to-ETABS classifier already reads. Ribbons (L and U cores drawn as one
outline) are counted, not split — `WallRibbonsNotSplit`, and only when the whole outline's box
passes the wall limits; a broad core with thin legs is not counted (audit F11).

**The differential**, thirteen baseline pages: COLUMN identical on 12 of 13, BEAM identical on 13 of
13, SLAB down on 13 of 13, WALL new on 13 of 13 (11 / 21 / 13 on 31130 p11–13; 25 / 23 / 27 on 31168
p11–13; 41 / 41 / 38 on 31138; 36 / 30 / 38 on 31065; 19 on 31202 p17). The one column that moved,
31130 p12, is a 24" × 53¾" filled rectangle the sheet's column schedule does not declare; it was a
column by shape and is a wall (a pier) now, which is the DXF side's own rule for an undeclared solid
footprint of wall proportions. The schedule decides what is a column; the rule decides the rest.

**Against 31168's Revit DXF**: PDF walls 0 → 461 over the 24 issued sheets (DXF 892); on the nine
single-view parkade and podium sheets the counts are 22/23, 25/33, 23/24, 27/37, 24/26, 45/47,
15/18, 17/40, 16/15, 16/16, 15/16, 13/18 — within two on 7 of 24 sheets overall. The tower sheets
compare one PDF plan against three to eight Revit views and are not yet comparable per view.
Columns unchanged: 1,193, equal on 8 of 24 sheets.

**The convergence proof**: 31168 p14 → `pdf-takeoff --kor-layers` → `dxf-to-etabs` against the job's
reference: 24 PDF walls on the sheet, 18 wall panels in the model (the DXF side merges collinear
runs under `dxf.connect-walls`), 66 columns. A PDF-derived plan reaches ETABS with walls through
the same code a Revit export uses. Before this step the number was 0.

**The ledger**: `BecameWall` 632 / 1,025 / 1,359 / 1,137 / 1,411 across the five sets; `BecameSlab`
on 31130 fell 1,039 → 424; totals unchanged.

**Two things the step found, for the next briefs:**

- **Section sheets read as plans.** S3.01 on 31168 is a sheet of sections, and the rule finds 36
  "walls" there — the poché of cut walls in section. The classifier runs on every page regardless of
  sheet type. The record knows the sheet type (§1's `SheetType`); geometry rules should run on plan
  sheets and say so on the others. That is the "contracts per sheet type" item of §5, and it is now
  a measured need rather than a design wish.
- **The compiled defaults are narrower than the banked rows** (`MaxWallThickness` 36 vs 60,
  `MaxColumnSize` 96 vs 132 on the DXF side), and nothing in the build proves the two agree. The
  parity test and the re-runnable corpus measurement are brief 16.

## 10. Step 3, done 2026-09-08: the build proves the compiled defaults and the banked rows agree

Brief 16, implemented by the verifier (Codex was out of usage) and verified the same way.

**Measured first**: 49 numeric `dxf.*` rows against their compiled defaults. 46 agreed. Three did
not: `dxf.max-wall-thickness` 60 (row, migration 038, corpus of 1,126 models) against 36 (code);
`dxf.max-column-size` 132 against 96; `dxf.outline-self-touch-tolerance` 0.5 (live row) against
0.05 (code, and migration 045's own committed text). The first two were the code never following
the corpus widening: a production run admitted a 42" core wall and every default-mode run refused
it. The third was a row that had drifted from its own migration.

**Changed**: the two code defaults are now the rows (60, 132), with the corpus basis in their
remarks; migration 081 (`KOR.Drafter\db\081_ARowMatchesItsOwnMigration.sql`, applied by Ian
2026-09-08) set the self-touch row back to 0.05. `DxfToEtabsService.BuiltInRuleValues` is internal
and `PdfIntakeOptions.BuiltInRuleValues()` mirrors it for the PDF side's eleven keys.

**The gate**, `CompiledDefaultsAreTheBankedRowsTests` (Slow; needs KorStandards; FAILS when the
connection is unset rather than skipping):

- every key the code reads equals its row within 1e-6 in the row's unit, and a key both sides
  read compiles to the same value on both sides;
- a key with no row must be declared `UnbankedByDesign` with a reason — the six `dxf.pdf.*` keys
  are, pending the corpus measurement — and a declared-unbanked key that gains a row fails the test
  until it is removed from the list;
- every public numeric property on `PlanClassificationOptions` and `ComposeOptions` is a rule
  (its kebab-case name is a `dxf.*` key) or is declared `NotARule` with a reason. Nine are: her
  per-storey slab count, the model unit, the storey convention, four pieces of per-run state, a
  report-only copy, and `SpandrelDepth`, superseded by the floor and ceiling rows and marked for
  removal.

**Result**: 0 divergences, 0 undeclared orphans, 2 of 2 tests green; the thirteen PDF-side DXFs
byte-identical to step 2b's (the PDF side already used 60). The full Core suite's result is in the
commit message.

## 11. Steps 4 and 5, done 2026-09-08: a plan is the sheet that says PLAN, and a glyph drawn twice is one glyph

Briefs 17 and 18, implemented by the verifier (Codex out of usage). Two steps landed together
because the second turned out to be the precondition of the first.

**The sheet type.** The record types a sheet from what the sheet says about itself, in this order:
the drafter's index (bookmark), the SHEET TITLE field of the title block, then, only if neither
names a kind, a parsed storey means plan, then the title text, then the title block's words.
Measured against the bookmark titles where PdfPig can read them: **60 of 60** on 31130, **41 of 41**
on 31168, **73 of 73** on 31065, **59 of 59** on 31202. The order matters and was measured: the
storey rule alone typed 14 of 31168's 41 sheets plan, because wall elevations and typical details
name storeys too; the largest right-edge text typed plans as notes, because a notes column sits in
the right fifth of a KOR sheet in title-size type.

**The title block is a form.** `TitleBlockFields` reads it by its labels — SHEET TITLE, SHEET NUMBER,
SCALE, PROJECT NO, PROJECT TITLE, DRAWN BY, CHK'D BY, REV, ISSUED FOR, DATE, CONSULTANT … — a value
beside its label when it shares the line (SCALE = 1/8" = 1'-0"), below it to the next label
otherwise. Ten fields per sheet on the three KOR-drafted sets (600 on 31130's 60 sheets, 496 on
31168, 611 on 31138); 31065 and 31202, whose blocks carry other labels, yield fewer and fall back.
The record carries the fields (`SheetRecord.TitleBlock`). Note what this already answers: SCALE is
read as a field on every KOR sheet, where `SheetScaleReader` reported none on 162 of 162 pages of
those three sets; wiring the field into the scale reader is one line in the scale step.

**A glyph drawn twice is one glyph.** The field reader was blind until the reader under it was
fixed: KOR's title blocks set their 8 pt labels and 8.4 pt notes in fake bold, every glyph drawn
twice at the same origin (358 of 5,579 letters on 31130 p11, offset exactly 0.0), and PdfPig's
nearest-neighbour extractor given both copies interleaves them — PROJECT TITLE arrived as
"PRPORJOEJCETC T TITTILTEL E". `VectorPageReader` now drops a letter whose value, size and origin
equal an earlier letter's to a tenth of a point before extracting words. Word totals fell by the duplicates (568,310 →
566,264 items on 31130); nothing else in the ledger moved; the thirteen DXFs are byte-identical.

**Only a plan is taken off to a DXF.** `pdf-takeoff` reports a non-plan page and writes nothing
for it; a whole-set run on 31130 writes 34 DXFs and names 26 pages as details (13), schedules (4),
sections (4), covers (2), notes (1) and other (2). The differential compares plan sheets only and
lists the rest: S3.01 is a shear wall schedule and is excluded; S1.11, "DESIGN LOAD PLANS", is a
plan and is compared. Over the 23 compared plan sheets: PDF walls 425, DXF 850; columns 1,191
against 1,576, equal on 8 of 23. The ledger carries a context row for geometry the classifier
emitted on a non-plan sheet.

## 12. Step 6, done 2026-09-08: the scale is accounted for on every sheet

Brief 19, implemented by the verifier. The glyph deduplication had already moved
`SheetScaleReader` from 79 to 214 of 294 pages, because "SCALE:" was itself a double-drawn label.
The remaining 80 are now accounted for rather than unread: the record carries `ScaleStatement`,
the SCALE field as the drafter wrote it (its tokens joined with single spaces), and
`SheetScaleReader.RatioOf` parses it as the fallback when the reader declines for want of a field
(repairing the one export fault seen, an "=" dropped between two lengths) — never when the reader
declined because two SCALE fields disagree (audit F8; the ledger says so).

| Outcome | 31130 | 31168 | 31138 | 31065 | 31202 | All |
|---|---|---|---|---|---|---|
| ratio read | 46 | 37 | 42 | 45 | 44 | 214 |
| stated without a ratio ("AS NOTED", "As indicated") | 12 | 1 | 17 | 25 | 15 | 70 |
| none stated (covers, 3D views) | 2 | 3 | 2 | 3 | 0 | 10 |

294 of 294. A details sheet that says "AS NOTED" is telling the truth about itself, and the ledger
counts it as read; "none stated" is now ten cover pages.

**Still open on rules, and where it goes**: the corpus measurement is a one-off hand run from
14 August. Making it a compiled, re-runnable verb that reports each rule's coverage of the
portfolio — and does the same over the stick-file corpus for the `dxf.pdf.*` keys — is the step
that turns "a row must not break a read" into a number. It has to run on the file server itself
(the corpus is 1,126 models on the projects volume, and SMB enumeration is the one thing this repo
has learned never to do from a workstation), so it is a self-contained publish started over RPC,
and its own brief.

## 13. Step 7, done 2026-09-08: a footing is a dashed rectangle whose size the schedule declares

Brief 20, implemented by the verifier. Before this the intake placed no footing anywhere: the
foundation schedule was read — marks, sizes, depths, how many of each mark the plan places — but
the drawn outlines went to the DXF as BEAM lines or were dropped as too short. Measured first, on
three foundation plans: not one footing outline is a closed path; every one is separate two-point
strokes, one per dash. Chained across the DXF side's own dash-join gap (`dxf.dash-join-gap` =
14 in), adjacent pieces within 12 mm of one another, three or more pieces to a side, and closed
into boxes, the boxes match the schedule's sizes to the millimetre.

`Intake/FootingOutlines` reads them before the classifier runs; every consumed dash is recorded
`BecameFooting` with its footing's index and reaches no other branch. The record carries
`Geometry.Footings` (mark, outline, centre, the scheduled L × W × depth); the DXF gets a FOOTING
layer; `pdf-overlay` draws them orange; the ledger carries a row per sheet — footings read of
marks placed, per mark — so the engineer sees which mark is short.

| Sheet | Read of placed | By mark |
|---|---|---|
| 31130 p11 | 35 of 36 | F1 2 of 1, F2 14 of 14, F3 8 of 8, F4 11 of 13 |
| 31130 p12 | 34 of 39 | F1 3 of 4, F2 17 of 20, F3 9 of 9, F4 5 of 6 |
| 31138 p9 | 11 of 11 | F1 7 of 7, F2 4 of 4 |
| 31065 p14 | 26 of 31 | F1 4 of 4, F2 12 of 13, F3 0 of 1, F4 10 of 13 |
| 31065 p15 | 14 of 23 | F1 4 of 5, F2 1 of 6, F3 3 of 6, F4 6 of 6 |
| 31168, 31202 | 0 of 0 | no spread footing scheduled (a placeholder table; a raft) — the ledger says so |

120 of 140 across the five foundation plans. DXF census against step 3: a FOOTING layer appears on
5 of 13 DXFs (35, 34, 11, 26, 14 polylines), BEAM falls by the pieces consumed, and COLUMN, WALL
and SLAB are identical on 13 of 13. Pieces recorded BecameFooting: 996 on 31130, 340 on 31138,
1,327 on 31065. The 31130 ledger total is unchanged at 566,264 items. The counts are banked per
schedule page in `FiveStickFilesTests.FootingsOnTheSchedulePageAreTheBankedCount`. Full Core
suite 1,033 of 1,033 (5 m 50 s).

**The 20 misses, looked at** (overlay crops on 31065 p15, `footing_miss.py` in the scratchpad):
one cause, two shapes. *A footing outline is drawn only where nothing stands on it.* At the match
line between the two halves of a plan the footing is drawn from the line out — one full side
(3,001 mm, six pieces) and two stubs (758 mm, two pieces each); under the perimeter wall the side
beneath the wall is absent or a few short pieces, and those pieces fell either side of a 12 mm
bucket boundary (x = 77,208 and 77,220 mm), so neither bucket reached three. The F1 "2 of 1" on
31130 p11 is the mirror fault: a dashed 4 ft square with no F1 standing in it is counted as F1,
because a box is not yet tied to its mark label. Brief 21 is that rule — place from one full side
and the stubs at its ends, cluster sides by sorted coordinate, carry whether a mark stands in the
box.

WHAT THE CHECK COVERS: axis-aligned dashed rectangles of a scheduled spread-footing size, either
orientation, on the five schedule pages. WHAT IT DOES NOT: strip footings (a size, not a box),
rotated footings, a footing drawn as a solid closed path, a footing the schedule does not declare,
and a box of a scheduled size that is not a footing — the check counts boxes against marks and
cannot tell a dashed 4 ft sump from an F1.

## 14. Step 7b, done 2026-09-08: a footing outline is drawn only where nothing stands on it

Brief 21, implemented by the verifier. The 20 misses of step 7 were looked at on 31065 p15 and had
one shape: the drafter draws a footing only where nothing stands on it. Cut by the match line
between the two halves of a plan, or under the perimeter wall, the outline is one full side and
two stubs — and two of a wall-side's short pieces sat either side of a fixed 12 mm bucket boundary,
so neither bucket reached three. Three rules, each measured first:

- **One full side and the stubs at its ends place the footing.** A dashed chain of a scheduled
  length with a perpendicular chain starting at each end, running the same way and no further
  than the box is deep, is that footing extended from the side. One side and one stub is not.
  Sides cluster by sorted coordinate, never by a fixed bucket.
- **The plan's label names the footing it is nearest to**, when it stands in the box or within half
  the footing's size of its edge. Measured: inside the box on 31065 and 31138 (37 of 37); 254–461 mm
  beneath the outline, beside the column, on 31130 (67 of 68). `FootingOutline.LabelledOnThePlan`.
- **Nothing inside sheet furniture is a footing or a placement.** 31130 p11's hairpin-stirrup legend
  draws a dashed 4 ft square — an F1 by size, and the "F1 2 of 1" of §13. 31065 p14's note "ADD
  BOND BREAKER BETWEEN F4 & CORE FOOTING" says F4 twice, and the placement counter took both as
  footings — 74 cu.yd that is not there. `FootingScheduleReader.CountPlacements` now takes the
  furniture; the intake, the ledger, the overlay and the harness pass it.

| Sheet | Labelled of placed | Was (§13) | What remains |
|---|---|---|---|
| 31130 p11 | 36 of 36 | 35 of 36 | — |
| 31130 p12 | 37 of 39 | 34 of 39 | two rotated footings on the angled wing |
| 31138 p9 | 11 of 11 | 11 of 11 | — |
| 31065 p14 | 29 of 29 | 26 of 31 | one 4.5 m dashed box no label names — the core footing — emitted with the F4 mark its size matches, flagged, and listed apart |
| 31065 p15 | 22 of 23 | 14 of 23 | one F2 with one side and one stub drawn |
| **All** | **135 of 138** | 120 of 140 | |

The ledger's footing row now reads labelled footings of labels placed, per mark, and lists boxes
no label names apart; `pdf-overlay` draws those magenta and prints them, and prints every placed
label no footing answers — the two ways the read and the plan can disagree, each with a position
to go and look at. The flag earned its keep at once: 31130 p15, the L0 WEST floor plan, repeats
the FOUNDATION SCHEDULE like every parkade sheet and places no footing mark, and one 4 ft dashed
square round a PCX column read as an F1. It is unlabelled, the sheet places nothing, and the
ledger files it as **unaccounted**, not read: a footing read on a sheet whose plan places no
footing mark is a box the size of a footing and nothing on the sheet says it is one. DXF census against step 7: FOOTING 35→36, 34→37, 26→30, 14→22 on the four
foundation plans, BEAM down by the pieces consumed, the other 9 of 13 DXFs identical, COLUMN, WALL
and SLAB identical on 13 of 13. Ledger totals unchanged on 5 of 5 sets. Full Core suite 1,037 of
1,037 (5 m 20 s); fast suite 942 of 942 after the ledger-wording edit that followed it.

**Two findings for other steps.** `takeoff footings` and the takeoff's foundation pricing counted
placements without the furniture, so on 31065 they priced 13 F4 where the plan places 11 — fixed
the same day: both pass the furniture now, and the banked 31065 spread-footing total moved from
1,174 to 1,099 cu.yd, the 74 that were never on the plan. Rotated footings on 31130's angled wing
are outside every axis-aligned rule here, as rotated marks are outside the column self-check — one
rule for rotated geometry, later.

WHAT THE CHECK COVERS (`FiveStickFilesTests.FootingsOnTheSchedulePageAreTheBankedCount`): the
count of footings read and of labels placed on the five schedule pages, and that every footing
read carries a scheduled spread mark. WHAT IT DOES NOT: whether each footing is labelled (the
ledger row says; the test does not assert it), the east halves (p12, p15), position, and a footing
read at the wrong place with the right mark.

## 15. Step 8, done 2026-09-08: a grid axis is a named line

Brief 22, implemented by the verifier. The intake already found every grid bubble with an axis
through it and used the axes for one thing: to discard the grid lines. The names and positions
reached no consumer, and the ETABS side — which puts a drawing on the engineer's model by matching
grid lines on a layer named GRID (`GridAlignment.Solve`) — could never align a PDF-derived DXF,
because that DXF had no such layer.

**A grid axis is a named line**: its name is the bubble's label, its position the rule through the
bubble, the two ends of one line are one axis, and the same name on two sheets is the same line.
`GridBubbles.Grid.Axes` names them (both labels joined by "|" when the ends disagree); the record
carries `Geometry.GridAxes` in millimetres; the DXF gets a GRID layer — one LINE per axis across
the drawn extent and the name as TEXT at both ends, recentred with everything else; `pdf-overlay`
draws them cyan; the ledger lists the names by direction. The pieces the drafter drew are **read**,
not discarded: `PathReason.GridAxis` is Disposition.Read, since many pieces make one axis and the
axis is in the record.

| Sheet | Axes | X | Y |
|---|---|---|---|
| 31130 p11 | 19 | 1,3,5,7,8,9,10,11,13,15,16 | Q,P,L,G,F,E,B,A |
| 31168 p11 | 26 | 1–19 | R,P,N,M,L,K,J |
| 31138 p9 | 15 | 1–8 | G,F,E,D,C,B,A |
| 31065 p14 | 17 | 1–8 | F,A,G,F,E,D,C,B,A — F and A twice |
| 31202 p17 | 17 | 1,2,3,4,10,13,14 | 4,1,N,M,L,I,F,C.2,B,A |

Spot-checked against the bubble labels' own positions on 31130 p11 (fitz, independent of PdfPig):
axis 1 at 21,125 mm against the label at 21,120; 3 at 25,392 against 25,387; A at 68,647 against
68,605; B at 63,466 against 63,423 — 4 of 4 within 45 mm, the label's offset from the circle's
centre. Banked per schedule page in `FiveStickFilesTests.NamedGridAxesOnTheSchedulePageAreTheBankedCount`.

**What moved in the ledger, and nothing else.** Totals unchanged on 5 of 5 sets. Read rose by the
grid-line pieces and the axis names, discarded fell by the same:

| Set | Paths GridAxis → read | Axis names → read | Named axes (all pages) | Read before → after |
|---|---|---|---|---|
| 31130 | 18,166 | 670 | 638 | 8,735 → 27,571 |
| 31168 | 22,607 | 658 | 558 | 10,644 → 33,909 |
| 31138 | 20,578 | 742 | 625 | 9,551 → 30,871 |
| 31065 | 26,970 | 795 | 695 | 16,635 → 44,400 |
| 31202 | 32,470 | 694 | 597 | 7,888 → 41,052 |

Unread and unaccounted did not move on any set. DXF census against step 7b: a GRID layer on 13 of
13 DXFs (three entities per axis: 57 on 31130 p11 = 19 axes), every other layer identical on 13 of
13. Full Core suite 1,046 of 1,046 (4 m 56 s).

**Two names on one sheet.** 31065 p14's second F (y 4,324 mm) and second A (8,401 mm) sit at the
foot of the sheet, below the plan's own F (24,287) and A (54,527): a second view on the same sheet
carries grid bubbles too. The ledger lists "names used twice — a second view on the sheet" and
does not merge them; that is the per-view split's measurement (the multi-view sheets in §6). 31202
p17 puts the numerals 1 and 4 on horizontal axes, which is what the sheet draws.

**For other steps.** The named axes make alignment by NAME possible — `GridAlignment` matches by
spacings today, and the drawing's "3" and the model's GRID "3" are the same line — a brief in
`DxfToEtabsService`. WHAT THE CHECK COVERS: the count and the names in order of the axes on the
five schedule pages (banked after audit Q7) and the DXF's GRID layer for a synthetic geometry. WHAT IT DOES NOT: an axis's position
against the drawn grid line (the spot check above was by hand), a bubble whose label sits outside
the circle, a grid drawn without bubbles, and whether the ETABS side aligns a PDF-derived DXF
better with the layer present — not yet measured.

## 16. Step 10, done 2026-09-08: a storey height is the distance between two level lines

Brief 25, implemented by the verifier. Storey heights came from the reference model only
(`E2kDocument.ReadStories`), and §3 listed that as a gap. Measured first, with `takeoff elev-scan`
extended to print the level ladder's gaps at the sheet's scale and a new `takeoff e2k-storeys`
printing a model's storeys: the five sets' level ladders sit on the wall elevation and section
sheets late in each set; absolute elevations are rare on them; **the storey height is the drawn
distance between consecutive level lines at the sheet's scale**, with a dimension string only where
a storey is typical.

| 31168 p37, SHEAR WALL ELEVATIONS - BLDG B, 1/8" = 1'-0" | From the ladder | The engineer's 31168 model |
|---|---|---|
| typical tower storey (L4 → L19, 16 of 21 storeys) | 2,946 mm | LEVEL 12–26: 116 in = 2,946 mm |
| L3 → L2 | 5,336 mm | LEVEL 3: 210 in = 5,334 mm |
| L2 → L1 | 2,808 mm | LEVEL 2: 110.5 in = 2,807 mm |

Within 2 mm on the three storeys the two name alike. `Intake/StoreyLadder` reads them on sheets
typed section/elevation only — a schedule's level column is a table's pitch, not a drawing's — into
`SheetRecord.Storeys`; the ledger carries a row with the count, the typical height and the first
pairs. 31130 p53 (WEST TOWER SHEAR WALL ELEVATIONS) reads four: L1M → CONCRETE 2,731, CONCRETE →
L0/P1 4,125, L0/P1 → P2 3,353, P2 → P3 2,743 mm — and "CONCRETE" is the level reader taking
"LEVEL 1 - CONCRETE" as a level named CONCRETE, a `ReadLevelLadder` finding recorded, not fixed.
Banked in `FiveStickFilesTests.StoreyHeightsOnAnElevationSheetAreTheBankedOnes` at ±5 mm, a
sixteenth of an inch on paper at 1:96.

Across the five sets the ledger reads 13 storeys on 31130, 48 on 31168, 48 on 31065 (typical
2,845 mm, 9'-4"), 40 on 31202 (typical 2,946) — and **0 on 31138** at this step, whose wall
elevation sheets state SCALE = AS NOTED and carry the scale as a caption under each view (`1/8" =
1'-0"` beneath "SHEAR WALL ELEVATION 3"). The reader took the sheet's scale and a sheet that says AS
NOTED has none; §20 reads the view's own caption and 31138 gives 28. Totals unchanged on 5 of 5. Full
Core suite 1,051 of 1,051 (6 m 21 s).

WHAT THE CHECK COVERS: two elevation sheets, the storey count, one named pair each, and the typical
height where one repeats. WHAT IT DOES NOT: a set's storey table (the union of its elevation sheets'
ladders reconciled by level name — the next brief, and the one that hands storeys to
`DxfToEtabsService` in place of, or as a check on, the reference model's), two buildings' ladders on
one sheet (the busiest column wins), an AS NOTED sheet whose views carry their own scale (31138,
0 of its elevation sheets read), and the mangled level name.

## 17. Step 11, done 2026-09-08: the drawings' storeys against the model's

Brief 26, implemented by the verifier. A set's storey table is the union of its section and
elevation sheets' level ladders, reconciled by the pair of level names each storey runs between
(`Intake/SetStoreys`: the median across sheets and their spread). Against a model, the height for
the same pair is **the difference of the two named elevations**, not the model's own "storey
below" — a site model interleaves several buildings' levels, and the storey under LEVEL 10 in
31168's list is another building's roof (`Intake/StoreyAgreement`). A check, not a replacement:
the model is not changed; `takeoff publish --stick-file` and `takeoff dxf-to-etabs` now carry one
line in their warnings, and `takeoff storeys-check <pdf> <e2k>` prints the table.

31168's drawings against the engineer's own model: 22 storeys on 4 of 4 section/elevation sheets;
20 match the model by both level names, and 20 of those are within 5 mm (deltas 0 to +5 mm, at a
25 mm tolerance). The two unmatched pairs, L2 → L1 and L1 → P1, are the drawings' "LEVEL 1" against
a model that names it A-LEVEL 1 and B-LEVEL 1 — a naming fact the line reports, not a fault.

WHAT THE CHECK COVERS: the reconciliation and the pair-wise comparison on synthetic data; by hand
the local 31168 set against its model; and live, `TheLiveSetsStoreysAgreeWithTheirModelTests`
resolves the newest dated stick file under 31168's "05 Stickfile" and the reference model by name
on the share, and holds at least 18 matched storeys with none off tolerance (13 s, skipped when
the share is unreachable). WHAT IT DOES NOT: a second live set (31130's reference is not on the
share under a name the test knows), AS NOTED sheets (31138 states nothing to compare), and a level
the two sides name differently.

## 18. The audit, 2026-09-08, and what it changed

Brief 24 asked Codex to read steps 1–11 as committed and find where the code contradicts its own
stated rule, where a test's name is wider than its check, and where a doc sentence is not something
the code does. Its response is `docs/codex/CODEX-24-AUDIT-RESPONSE.md`: eleven findings and eight
answers, every one with a file, a line and the smallest input. Each defect became the failing test
in `TheAuditsCounterexamplesTests` before its fix; the fixes below landed in one pass.

| Finding | What the audit showed | What changed |
|---|---|---|
| F1 | the wall rule tested the box and the vertex count, never that four points form a rectangle: a filled trapezoid became a wall | `GeometryFilterService.IsRectangle` — square corners within 3° and the polygon fills its box |
| F2 | an unlabelled footing box's pieces were read in the primary ledger, and only a context note said otherwise | `PathReason.FootingBoxNoLabel`, unaccounted; the box is still emitted, flagged |
| F3 | pass 2 took the first scheduled type that fit; a label could not correct it; two label reaches | one reach (half the footing's size) chooses the type and flags the footing |
| F4 | an axis sat at the bubble's centre, not on its rule, and bubbles within 1.5 pt merged | each bubble keeps the rule nearest its centre; axes cluster by rule (0.5 pt); the floor is the rule reader's 0.6 pt |
| F5 | "FOUNDATION PLAN NOTES" and "KEY PLAN" typed as plans | the PLAN regex refuses a following NOTES / SCHEDULE / DETAILS / LEGEND and a preceding KEY |
| F6 | a footing-only page wrote no DXF; FOOTING was written without a LAYER-table entry | footings weigh in the centring; the layer is declared |
| F7 | the remapper filled every empty slot with "collapsed by thinning", hiding a path decided twice or never | it throws on both |
| F8 | two different SCALE fields: the reader refused, the field fallback answered anyway | `SheetScaleReader.StatesConflictingScales`; the ledger says "states two different scales", unaccounted |
| F9 | the ledger re-read the footing schedule and placements instead of reporting the record | the record carries `FootingLabels`; the ledger counts them |
| F10 | title-block words were all unread though the field reader consumed some; the bookmark "had no reader" though it typed the sheet; axis names were read in markup-only mode | the field reader returns the tokens it consumed; the bookmark row is read when it typed the sheet; axis names are read only when an axis was exported |
| F11 | a declared column turned 30° fell through to the wall rule; a stroked white fill could be a wall; a REV label in the next column cut SHEET TITLE off; the parity detector skipped decimal and long | the oriented box serves both rules; non-paper is a wall condition; the field ends at the next label in its own column; the detector takes every numeric kind |

Measured after, with the harness the audit could not run: `dxf-census` step 11 → 12 moved BEAM
only, by 1–4 lines on 6 of 13 files (the axes' lines now sit on the rules); WALL, COLUMN, SLAB,
FOOTING and GRID identical on 13 of 13; the five sets' ledger totals unchanged; the Revit
differential unchanged at walls 425 / 850, columns 1,191 / 1,576, slabs 48 / 44. On the five sets no
trapezoid wall, no turned declared column and no stroked white wall existed — the fixes are guards,
not corrections of a banked number — and the unlabelled boxes (the core footing on 31065 p14, the
4 ft square on 31130 p15) moved 12 pieces from read to unaccounted. The ledger-honesty items
moved words the other way: the title-block words the field reader consumed are read now (5,146 on
31130, 3,128 on 31168, 4,959 on 31138, 4,888 on 31065, 3,495 on 31202), the bookmark row is read on
the two sets whose bookmarks PdfPig can read (31065 73 of 73 pages, 31202 59 of 59), and unread
fell by the same on each set — 31130 31,388 → 26,242 — with totals and unaccounted unchanged.

**What the audit said that this document now says differently.** "The inventory derives nothing"
was false until F9 and is true again. "Ribbons are counted" is counted only when the whole outline's
box passes the wall limits. "The SCALE field verbatim" is the field's tokens joined with single
spaces. "Collinear within 12 mm" is adjacent pieces within 12 mm of one another. "A glyph drawn twice
at one origin" is one within a tenth of a point. The banked grid check now holds the names in order,
not a count. And equal ledger totals mean the accounting is stable, not that the geometry is the
same: a bookmark, a rotation, an image or a link each add one item, and a footing's label moves
none.

**Not changed, on purpose.** Empty fates when no scale is requested (documented, tested). "DESIGN
LOAD PLANS" typed as a plan (policy). The two-read design (unthinned content, thinned classifier)
superseded the one-read sentence in brief 13. A ribbon is still not split, a wall with an opening
is still not reconstructed, and a curved wall is still not a wall — the audit's table in question 2
is the honest list of what the rectangle rule cannot read, and the 425 against 850 lives there.

## 19. Step 12, done 2026-09-08: a dimension string is a length the drafter wrote

Brief 27, implemented by the verifier. The ledger typed 1,680 to 6,629 words per set as dimension
strings and read none of them. `Intake/DimensionStrings` parses each — feet and inches, bare
inches, and a bare three-to-five-digit number as millimetres — into a value, keeps its position and
orientation on the record (`SheetRecord.Dimensions`), and for a string that sits between two grid
axes says which span and whether the written length agrees with the axes' spacing at the sheet's
scale, within an inch. A bare number is typed as a dimension only when a span agrees with it;
otherwise it stays what it was, a mark, a level or a count.

The premise this step began with — that a dimension between grid axes would confirm the scale
without the title block — is **false on KOR's structural sets**. Across the five sets, 294 pages,
0 dimension strings agree with any span of grid axes: the structural plans dimension members and
openings, and the grid spacing lives on the architect's drawings. The check stays, stated with its
zero, for a set that does dimension its grid; the value of the step is the typed text.

| Set | Dimension strings typed | Agree with a grid span | Unread before → after |
|---|---|---|---|
| 31130 | 4,001 | 0 | 26,242 → 22,241 |
| 31168 | 1,677 | 0 | 15,255 → 13,578 |
| 31138 | 5,858 | 0 | 27,336 → 21,478 |
| 31065 | 321 | 0 | 33,837 → 33,521 |
| 31202 | 6,626 | 0 | 31,195 → 24,569 |

Totals unchanged on 5 of 5. On the five schedule pages 49, 27, 72, 2 and 186 strings are typed and
banked (`FiveStickFilesTests.DimensionStringsOnTheSchedulePageAreTheBankedCount`). 31065's plans
carry their lengths as bare millimetre numbers, which this rule leaves unread until a span agrees:
128 on p14, none agreeing — so a metric set's dimensions are the next reader, with the dimension
LINE (its extension lines and ticks) as the witness instead of the grid.

WHAT THE CHECK COVERS: the parse, the tightest agreeing span, the adjacent pair, tall text against
the horizontal axes, furniture excluded; the typed count per schedule page. WHAT IT DOES NOT: the
dimension line, so a string's own extent is not read; a bare millimetre number on a metric sheet;
and whether a typed value is the length of the member beside it.

## 20. Step 13, done 2026-09-08: a view's scale is the caption under it

Brief 28, implemented by the verifier. §16 read no storeys on 31138 because its wall elevation
sheets say SCALE = AS NOTED, and the storey reader took the sheet's scale. Measured first with
`takeoff elev-scan`: on those sheets every view carries its own caption on one baseline — the
view number, the sheet reference and the ratio, "3 / S3.11 1/8" = 1'-0"" — two under p53's two
ladders (fx 0.43 and 0.65, under ladders at fx 0.50 and 0.72), five under p57's five views, three
under 31130 p53's three wall elevations besides the title block's own. None carries the word SCALE,
which is why `SheetScaleReader.ScaleNotesAnywhere` found none of them.

**A view's scale is the ratio in the caption under it.** `Intake/ViewCaptions` reads every caption
with its position, rejoining the imperial form the word extractor splits into three tokens and
reading the metric form whole; `StoreyLadder` takes, when the sheet states no ratio, the caption
nearest below the ladder — and when every caption on the sheet states one ratio, that ratio. On
31138 p53 the ladder now reads 17 storeys, typical 2,995 mm (9'-10"), and p57 reads 6 (P1 → P2
2,946 mm). Banked in the storey theory with 31168 and 31130. The level reader's own fault from
§16 is fixed with it: the level's value is the level-shaped token on the label's own baseline, so
"LEVEL 1 - CONCRETE" is L1 and "LEVEL 22 / MECH." is L22.

WHAT THE CHECK COVERS: the rejoined imperial caption and the metric one, the title-block fifth
excluded, the caption below and nearest, one ratio for a sheet whose captions agree, a ladder read
at a caption's scale with no sheet scale, and 31138 p53 banked. WHAT IT DOES NOT: which VIEW a
caption belongs to beyond "below and nearest" — two ladders one above the other take the same
caption; a plan on an AS NOTED sheet (the geometry still takes the requested denominator); and a
caption the extractor splits some other way.

## 21. Step 14, done 2026-09-08: a wall is what its clip lets through, and its doorways are its piers

Brief 29, implemented by the verifier. The wall gap against Revit on 31168 was stated as 425 to
850 since step 5. Measured per sheet first (`takeoff pdf-vs-dxf --views`): the 850 was the sum of
every DXF view a sheet stands for (S2.22.1 carries LEVEL 33 to 36) and a "for reinforcing plan"
copy of each; paired sheet to its own concrete-outline view the DXF carries 521 walls, and 795 of
the 850 sit on JBP_V-WALL, 26 on JBP_B_WALL. What remained was looked at: on the typical tower plan
(p22) the PDF read each core face as ONE 328" wall where Revit carries three piers, and on the
podium plan (p17) it read 17 walls to Revit's 29.

Two drawing conventions, found by listing what is painted on each wall in paint order
(`takeoff pdf-overlay --walls`):

**A wall is what its clip lets through.** Revit's export draws a core face as one grey fill the
length of the face and clips it (`W n`) with the path drawn immediately before it, one rectangle
per pier; the fill shows only through the clip. p22's west face is a 328" x 30" fill behind a clip
of 30" x 74", 118" and 41". `VectorPageReader` now carries a path's clipping flag and its ordinal
in the page; `GeometryFilterService` takes the clip drawn just before a wall's own path, every
piece of which lies on the wall (across its thickness, within its length), and emits the wall as
those pieces. A clip with a piece off the wall is left over from a graphics state the reader cannot
see the end of, and the wall is taken whole. The clip's pieces are read (`ClipOfWall`, indexed to
the first pier); a clip that shaped nothing stays `NoInk`.

**A doorway is a paper-coloured fill painted over a wall.** The podium's drafter draws the wall full
length and knocks each opening out with a white rectangle: painted after the wall, across its
thickness and no wider across than twice it, at least 18" along it (Revit's own doorways on 31168
measured 36"–48" on 142 of 160 and none under 18"). The wall is emitted as the piers on either side,
a pier being at least 12" (the DXF side's panel floor; 31138's model carries 9"–27" piers with
labels, so the wall's 48" minimum does not apply to a pier). The fill is read (`Doorway`, into
`Geometry.Doorways`); one painted before the wall is covered by it and stays `PaperFill`. p17 has
five, 40"–55" wide.

Measured after: p22 reads 10 walls per plan, which is what `dxf-inspect --walls` reads in Revit's
LEVEL 4 view (north, south, stair and landing walls, three piers each side); p17 reads 25 walls
(was 17). The census from step 12 moved WALL only, on 13 of 13 files, rising on each; the five
banked schedule-page counts moved 11→13, 25→30, 41→43, 36→39, 19→22 and are rebanked.

WHAT THE CHECK COVERS: a three-piece clip drawn before its wall; a clip not immediately before, or
with a piece off the wall; a clip and a doorway together; two knockouts making three piers, their
fates and object indices; a fill painted before the wall, one not crossing the thickness, a slot
under 18", a mask wider than the wall; a turned wall with a turned knockout; a knockout at a wall's
end; piers narrower than a panel dropped; every path fated once and in path order. WHAT IT DOES NOT:
a clip ended by `Q` before the wall is painted (PdfPig does not expose the graphics state — the
pieces-on-the-wall test is the guard); a knockout that is STROKED white; a doorway drawn as a gap
between two separate fills, which needs no rule; a curved clip; and the north arrow, whose 48" x 6"
shaft reads as a wall on 3 of 5 sets' baseline plans — the next rule.

## 22. Step 14b, done 2026-09-08: the north arrow is furniture

Found by §21's `pdf-overlay --walls`: a 48" x 6" filled bar read as a wall on 3 of 5 sets' baseline
plans, on every sheet that carries it. It sits at the sheet's top right beside the word NORTH — the
compass's shaft. Its ring (8 ft across at 1:96) was a slab and its arrow's four strokes were beams
on the same sheets, which the census found once the region existed.

**The north arrow is a compass beside the word NORTH, and nothing in it is structure.**
`SheetFurniture.NorthArrows`: a stroked path of at least twelve points, 30 to 200 points across
and no more than 2.5 times as long as wide (31168's is a closed ring, 31202's an open arrow outline
1/2" x 1"), whose centre lies within two lengths of a word NORTH; the region is the path's box and
the word's together. The word alone is not a region, because NORTH is a word in notes on the plan;
the ring alone is not, because a detail bubble is one too.

Measured after: the census from step 14 moved WALL −1, SLAB −1 and BEAM −4 on 6 of 13 files
(31168 and 31138), WALL −1 and BEAM −5 on 31202, and nothing on 31130 and 31065, whose compasses had
never read as anything. Banks 13/29/42/39/21.

WHAT THE CHECK COVERS: the ring beside the word with the shaft inside it; the word alone; the ring
alone; a ring two diameters away. WHAT IT DOES NOT: a compass with no closed or open twelve-point
outline at all, and a plan note reading NORTH beside a twelve-point stroked shape 30–200 pt across,
which this would wrongly swallow.

## 23. Step 15, done 2026-09-08: a sheet from the stick file is a drawing like any other

Brief 30, implemented by the verifier. Measured first: the thirteen-file baseline's DXFs for
31168 p11–p13, given to `dxf-to-etabs` against Andrea's reference, produced no model at all — "No
layer in this drawing set matched columns or slab edges, yet 9,263 segments sit on layers the tool
does not recognise". Written on the office's layers (`--kor-layers`) they built, but as sheets of no
storey: the reader takes a sheet's storeys from its file NAME, the way the office's export names a
view ("S2.01_1_LEVEL P3 PLAN …"), and a page named "31168-p11" says nothing. And the grid fit the
reader has is one frame for a whole set from grid POSITIONS, right for a Revit export in shared
coordinates and wrong for pages each in their own frame — it compared millimetres to inches and
found nothing.

**A sheet from the stick file is a drawing like any other: named as the office names a view, on
the office's layers, and set on the model's grid by the names of its axes.**

- `Intake/SheetDxfName`: `{sheet number}_1_{SHEET TITLE}.dxf` from the record; `pdf-takeoff` names
  each page's DXF that way (the page number only when the title block gave nothing).
- `GridAlignment.NamedAxes` pairs each grid-layer text of a name's length with the line whose end
  it sits at; `SolveByName` fits a sheet to the model's GRIDS table label to label, in the model's
  unit, at whichever quarter turn the names agree, when three or more do (`NameTolerance` 6").
  **An axis placed on the grid is a grid line for the rest of the set**: the model names X 1–19 and
  Y R and A only, building C's plan letters its Y axes B–J, but J is on the foundation plan too, so
  the placed sheets' axes join the reference for the sheets still unplaced (`Carried`). A sheet
  that still names nothing keeps its page frame, and the report says so.
- `DxfToEtabsService`: one frame per sheet where a by-name fit exists, the set's fit otherwise;
  no centring on top of a by-name fit; `ReadGridLines` reads the labels. A model built with no
  reference gets its GRIDS from the drawings' own named axes (`GridLines`).

Measured after, 31168 p11–p13 against the reference: 3 sheets read, 3 placed, 3 of 3 set on the
grid by name — S2.02 and S2.04.1 with 19 of 19 X and 1 of 2 Y agreeing within 0.8", S2.03.1 with
19 of 19 X and J through the foundation plan, within 0.9". 182 columns and 65 walls: the P3 plan's
70 columns rise to P2, the two P2 plans' 112 to P1, at x −1,160 to 2,392" on both, inside the
model's grid; the storeys are the reference's own (P3 1,366", P2 1,480", P1 1,594"). Rendered with
`plan_sheet.py` and looked at: the arrays line up storey over storey, the cores sit where the
drawings put them.

WHAT THE CHECK COVERS: the name from the sheet number and title, and what `PlanSheetNaming` reads
from it; text naming the line whose end it sits at; the millimetre sheet on the inch model at 0°
and the quarter-turned sheet; too few or disagreeing names; the GRIDS table from two sheets; the
live route on 31168 (`TheStickFileBuildsAModelOnItsGridTests`: 3 of 3 by name, columns on P1 and
P2). WHAT IT DOES NOT: floor plates — a parkade plan draws no filled plate and the perimeter is two
face lines, so the plates in this model are slivers; a building whose letters no placed sheet
shares; the reference's own member counts on the parkade (Andrea's file carries 16 columns in all,
it is a shell); the SHEET TITLE's word order across lines, which the title-block reader gets wrong
("FOUNDATIONS PLAN BLDG A - & B") and the name carries.

## 24. Step 16, done 2026-09-08: Reissue Impact, first cut — what changed between two issues

The first product of the foundation (`docs/KOR-Engineering-Tools-Foundation-2026-09-08.md` §3.1).
Ground truth: the share holds dated issues of one set per job — 31130 May and September (the May
one "on the architectural plan"), 31065 July and August (like for like), 31168 four. Mirrored to
the local drawings cache under `kor-drawings/issues`.

**What changed on a sheet is the difference between its objects on two issues, with the new issue
set on the old one's grid by name first.** `Intake/SheetObjects` is a sheet's objects in one light
record; `Intake/SheetDiff.Compare` pairs columns within five feet (moved past two inches, resized
past an inch), walls of one orientation and thickness whose axes lie within a foot and overlap half
the shorter, footings within two feet, grid axes and schedule rows by name, storeys by their two
level names; `takeoff set-diff <old> <new> --scale N [--sheet A,B]` reads both issues through the
one reader, pairs sheets by number and prints the table, or one sheet's changes in words.

Two rules were found by measuring, not designed:

- **One member, two readings.** A 48" x 24" pier sits on the wall-or-column boundary; 31130's
  S2.03.1 read it as a wall in May and a column in September, which came out as a wall removed and
  a column added at one place. Paired at one place, it is the same member re-read and not a change.
- **A name carried twice on a sheet anchors by the frame the most pairs agree on.** A key plan
  carries four buildings' grids, a sheet carries two views, and pairing the first "2" with the
  first "2" set 31065's S1.11 on the wrong building: 0 columns the same, 15 added, 15 removed, 33
  grid axes "moved" 26 m. `GridAlignment.AgreedOffset` now lets every same-named pair vote and
  takes the largest cluster (labels first, then votes, then the smaller move); the diff pairs each
  old axis with the nearest new one of its name. The ETABS route uses the same solve and is
  unchanged by it: the full suite and the live 31168 test pass.

Measured after. 31065 July → August, 73 sheets, like for like: **1 object change in all** — a
223" x 8" wall on S2.03.1.1 that July's read has and August's does not (26 other walls identical to
the millimetre, 37 columns the same); it was 466 before the two rules. 31130 May → September: 660
changes, of which the parkade and podium sheets carry the plausible handful — S2.03.1: a column
moved 241 mm and resized 24x30 → 42x24, two walls lengthened (261 → 577", 252 → 289"), one
removed, one pier re-read — and the tower sheets carry the architectural background of the May
"OAP" file read as columns and slabs (S2.17.1: 127 columns in May, 25 in September), which is a
population difference and not a reissue; 815 before the rules. 3 sheets only in May, 1 only in
September, found by number.

WHAT THE CHECK COVERS (`AReissueIsWhatMovedTests`, `ASheetSitsOnTheModelsGridByNameTests`): moved,
added, removed and resized columns; a wall lengthened and one gone; a footing re-marked; a grid axis
moved beside two that agree; a storey changed; a schedule cell rewritten and a row added; a whole
sheet shifted on its page with the grid shared, which is no change; the pier re-read; a name carried
twice. WHAT IT DOES NOT: the overlay as one PDF (`--overlay <dir>` paints each changed sheet on
the old issue's page as a PNG — green added, red removed, orange moved or changed, cyan a grid
axis moved, grey a member re-read — and 31130's S2.03.1 reads at a glance; binding them into a PDF
with the list is not done); a reissue
against the model rather than the previous issue; a grid renumbered between issues, which reads as
axes removed and added; the "OAP" variant against a plain one, which is two populations; a
schedule row the schedule reader invents from a NOTE line ("PC9ETON:" on 31130's parkade column
schedule), which the diff reports as a row added and which is the reader's defect to fix.

## 25. Step 17, done 2026-09-08: a mark-up is a list of instructions

The engineer-to-drafter loop (foundation §3.1): the engineer marks up the drafting issue in
Bluebeam and sends it back; the drafter adjusts the model. Measured first on what the share holds:
31168's Building C column-location diagram (the architect's mark-up on her own plan, 285
annotations by one author — FreeText "Move Column right 1'-11"" beside a Line annotation reading
1'-11", Polygons for the new column positions, KOR's reply "Column changes are OK structurally")
and 31065's MB-6 back-check (the engineer on the drafter's sheets: 70 Ink ticks, one comment about
dowels copied to three sheets, five measurements and "show hook" on the details sheet).

**A mark-up is a list of instructions.** The record's mark-up notes now carry their position
(`MarkupNote.Cx/Cy/Width/Height`, the annotation rectangle in points). `Intake/MarkupList.Build`
turns each note into an item: its kind — an instruction (the words start with a verb the office
uses: move, add, remove, align, extend, change, show …), a measurement (a Line or PolyLine whose
words are a dimension), an approval (Bluebeam's ink for a tick carries a character of nothing), or
a note — and for an instruction what it asks: the action, the subject (gridline, column, wall …),
the direction, and the distance, from the words or **from the dimension line drawn beside it**
(every one of Building C's moves has one within a metre). Each item is placed on the grid by the
sheet's named axes ("5/J", with the offset when past a foot) and beside the nearest member of the
drawing read within five feet. `takeoff markup-list <pdf> --scale N [--pages A-B]` prints it.

Measured after. Building C: 13 instructions, 10 with a distance (six gridline moves of 3"–8",
column moves of 2", 3" and 1'-11", two "change column to circular/rectangular", one "align
gridline with the multipurpose hall column"), 31 measurements, 78 notes — the notes are the
diagram's own labels and dimensions read as annotations, which is what an architect's mark-up on
an architect's plan carries. MB-6: 70 ticks, 9 notes, 1 instruction, 5 measurements; the back-check
is approval with one comment, which is what a back-check is.

WHAT THE CHECK COVERS (`AMarkUpIsAListOfInstructionsTests`): the parse of the words both files
carry; a dimension line as a measurement; the ink tick; the comment as a note; the grid reference
with its offset; the dimension line taken as the instruction's distance; the nearest member.
WHAT IT DOES NOT: the reconciliation of the drafter's next issue against this list (Reissue Impact
does the first half; matching its deltas to these items is the next module); an instruction split
across two annotations; deletions, which need a convention the reader can see (a strike or the
word DELETE); the mark-up's geometry (the red columns) as a second reading of the same instruction,
which the diff of drawing-read against mark-up-read would give and is not wired.

## 26. Step 18, done 2026-09-09: the drafter's reply is beside the thing it answers

The loop as the share actually holds it (31065, `02 Lateral Design\MB-Files…\Mark-ups`): the
engineer writes a round — "MB-4 (Column-final-design mark-ups)": 300 labels at the columns, the
mark, the size, the reinforcing, "45MPa", "8-35M verts 10M@175 ties"; "MB-6 (back-check
mark-ups)": ink rings round things to fix and his own small ticks — and the drafter replies **on the
same file**, a tick beside each item done, words beside one that could not be, saved under
`Back-checked\…-sz-back-checked-MB.pdf`; then the next round. So the reconciliation is
annotation against annotation on one page, not a drawing diff.

**The drafter's reply is beside the thing it answers.** The record now carries every annotation
but the links (`SheetRecord.Annotations`, with or without words; `Markup` stays the subset with
words the ledger counts). `MarkupReconcile.Reconcile(round, backChecked)` takes the engineer as the
round's main author, his instructions and notes as the items, and answers each with the nearest
annotation another author put within an inch of it on the paper: ink is done, words are a reply,
nothing is open; the drafter's words beside no item are unprompted. Three rules found by measuring:

- **Ink is read by its size on the paper, not by the character Bluebeam writes into it.** A tick is
  14 x 14 pt ("." or "/"); a ring drawn round a thing to fix is a hand's width and round (127 x 144,
  "o"); a long stroke is a leader. So the engineer's rings are items on a back-check round and his
  scribbles are not.
- **A tick answers everything within an inch of it.** The engineer writes three labels at a column;
  the drafter ticks the column once (MB-4: 300 labels, 228 ticks). A reply is not used up.
- **The drafter's ink beside an item is done whatever its size**: 18 x 18 or 93 x 87, a tick is
  drawn as large as the hand draws it.

Measured after: MB-6, 27 items, 13 done, 14 open, 2 unprompted; MB-4, 335 items, 148 done, 187
open, 27 unprompted. The open count is the product's own done-when: an engineer walks one round
and says which open items are truly open, which are ticked farther than an inch (a dense schedule
sheet puts labels ten points apart), and which were done without a tick. That is the next step,
and it is a person's, not a rule's.

WHAT THE CHECK COVERS (`TheDraftersReplyIsBesideTheThingItAnswersTests`): a tick beside one item,
words beside another, silence on a third; the engineer inferred; his own ticks not items; the
drafter's comment beside no item; one tick answering three labels; a ring as an item, a stroke as
none. WHAT IT DOES NOT: the two real rounds beyond their counts; a tick that means "no"; the next
round raising an item again; a reply farther than an inch on a dense sheet.

## 27. Step 19, done 2026-09-09: the set checks itself

The third product (foundation §3.3), from what the record already holds. `Intake/SetCheck.Sheet`
gives a sheet's own findings — no sheet number read, type unknown, a scale stated twice, a plan
with none, no SHEET TITLE, a column drawn a size other than its own mark's, a footing box no label
names, a footing label no footing answers, a grid name drawn twice on a plan — and `SetCheck.Set`
the set's: a sheet number used twice; **a schedule mark placed on no plan in the set** and **a
column of a size no schedule in the set declares**, both decided across the set because a mark
declared on the foundation plan is placed on the level above and a tower column's size is declared
on the column schedule sheet; a grid axis a plan draws elsewhere than the set's reference plan (the
one with the most named axes) draws it, after setting the plan on it by name; and the storeys
against the model when one is given. `takeoff set-check <pdf> --scale N [--reference model.e2k]`
prints the page.

Measured first, per sheet: 31130's September issue gave 33 findings of which "marks never placed"
and "sizes undeclared" were mostly the set's structure read per sheet (28 of 28 columns on a tower
plan "undeclared" because their schedule is on S4.02), and "grid name drawn twice" fired on every
wall elevation sheet, which draws the same bubbles on each elevation. Those three moved to the set
or to plans only. Measured after: 31130 September, 25 findings — a footing label no footing
answers (S2.01.2: F2, F1), a footing box no label names (S2.03.1), marks placed on no plan in the
set (C2 on three sheets, which is the gatekeeper's; "PC9ETON:" on six and "EXTENTS" on one, which
are the schedule reader's), and 12 sheets with column sizes no schedule in the set declares as
read, the tower schedules on S4.01 and S4.02 not being read as sizes yet. 31065 August, 62 — 33 of
them a grid name drawn twice on a plan, which on this set is every plan's layout and reads as a
note; a footing box no label names (S2.01.1, 4,500 x 4,500); one plan's axis drawn elsewhere than
the set's reference plan draws it. 31168 with its model: 47, and its storeys agree, which is a
finding of none.

WHAT THE CHECK COVERS (`TheSetChecksItselfTests`): each sheet class on a synthetic sheet; the set's
duplicate number and an axis drawn 300 mm off; a mark placed on another sheet and a size declared
on another sheet being no finding. WHAT IT DOES NOT: what the gatekeeper finds that this does not,
which is the done-when and a person's step on the last three issues; a grid renumbered between two
views of one sheet.

**A schedule row is a row whose mark is a mark** (same day). The column schedule reader offered
"PC9ETON:" (a NOTE line under PC9) and "EXTENTS" as rows on 31130, and both products downstream
reported them — Reissue Impact as a row added, Set Check as a mark placed nowhere. The intake now
keeps a column row only when its mark is shaped like one (the same shape a mark on the plan must
have), counts the rest in the ledger as "schedule rows whose mark is not a mark", and 31130's
September page fell from 25 findings to 17, the four marks placed nowhere all being C2.

## 28. Step 20, done 2026-09-09: a wall is what the cut pen encloses

The Model Start gap from §23: the parkade plates read as slivers because a retaining wall on a
foundation plan is drawn as its two faces, unfilled, and the filled-rectangle rule (§9) cannot see
it. After step 14b the PDF read 533 walls on 31168's plan sheets to the Revit DXF's 666.

Measured first, with a `pdf-overlay` instrument that paired any two parallel lines 48"+ long,
4"–60" apart, overlapping 48"+, not under a filled wall: 31168 p11 gave 23 pairs — its west and
south perimeter, the core's walls — but 31130 p11 gave 140 at 11", 23", 34", 45" and 57" (the
elevator pit's hatch, one pair per spacing) and 31065 p14 150. Written as a rule and run through
the census, WALL rose on 13 of 13 files (31130 p13 15 → 76, 31202 21 → 70) and 31168 read 1,061
walls to the model's 666. Looked at, page by page: on 31130 p13, 60 of the 61 new walls were the
rebar extent boxes drawn over the mats, 21"–57" across, and the one real wall was the west
retaining wall; on 31168, 31138 and 31065 the stair flights, 44"–45" wide; on 31202 a shaft drawn
with an X across it, the column outlines drawn again as four lines around eleven 14" x 48" columns,
the ramp's edges — and the real ones: the 8" CMU perimeter along grid N, 1,770" of it, the 28"
and 30" walls at grids 13 and 13.2.

Then the pens. A filled wall carries no stroke of its own on 5 of 5 stick-file plans; the lines
along its faces are drawn separately, and they are one pen: w9 on 28 of 28 (31130 p13), 40 of 41
(31202 p17), 43 of 44 (31168 p11) and 80 of 82 (31138 p9), w0.96 on 68 of 69 (31065 p14, another
producer). The rebar boxes are w2 and w4. That is the cut pen, and it is read off the sheet, not
declared.

**A wall is what the cut pen encloses.** `GeometryFilterService.WallsFromFaceLines`, after the
filled walls, doorways and clips: two lines in the sheet's cut pen (`CutPen`: the commonest width
of the lines along the filled walls' faces; a sheet with no filled wall has no cut pen and reads no
face wall), parallel within a degree, a wall's thickness apart, overlapping a wall's length, taking
the nearest such partner; with no third cut-pen line between them or at the same spacing beyond
either (a hatch repeats its spacing; adjacent faces are a wall's), at most one lighter line between
them (a tapered wall's batter line is one; 31138's flights carry a dozen 15" segments), nothing
drawn across them between their ends — three or more lines square to the faces, spanning most of
the gap, a tread apart, are a stair's risers whether inset from the stringers or run past them to
the walls; a line corner to corner is a shaft's X; a dimension's extension lines are a bay apart
and do not count — and not under a wall or column already read (a filled wall's own faces and a
column's outline are pairs too). The wall is the overlap, its thickness the gap; both lines are
read as its faces (`BecameWallFace`, indexed to the wall, `Geometry.WallFaceLines`) and stay in
`Lines` for the ledger, and `DxfExporter` writes the wall and not the faces as beams. The ledger
carries "walls read from two face lines"; `pdf-overlay` paints them purple and prints their
thicknesses, and `--walls` prints the sheet's pens, each face wall's pens, what lies beside it and
what is drawn between its faces, which is how the stairs and the batter lines were seen.

Measured after: the census from step 14b moved WALL and BEAM only, on 8 of 13 files, and the faces
left BEAM by the same count the walls entered WALL: 31130 p12 25 → 29, 31138 p9–11 42 → 43,
42 → 43, 38 → 40, 31168 p11–13 29 → 30, 23 → 24, 31 → 33, 31202 21 → 33; 5 identical. Against
Revit (`pdf-vs-dxf`): 31168's plan sheets read 681 walls to the model's 666, within two of it on
10 of 23 sheets. The banked schedule-page counts moved 29 → 30, 42 → 43, 21 → 33; 13 and 39 did
not, and are rebanked.

WHAT THE CHECK COVERS (`TwoFaceLinesAWallsThicknessApartAreAWallTests`): a pair in the cut pen
becoming one wall of the gap's thickness over the overlap, its faces' fates and their absence
from the exported beams; the cut pen read from the filled wall's face lines and a sheet with
none reading nothing; a pair in a lighter pen; one lighter line between (a wall) and several (not);
a third cut line beyond at the same spacing and one between; a filled wall's own faces and a
declared column's outline; a run of risers inset and a run overshooting; extension lines a bay
apart; an X; one end cap; unequal faces; a 2" and a 6' gap; a pair turned 30°. WHAT IT DOES NOT:
31168 p11's west property-line wall, 984" x 23" — filled and tapered, its inner face lighter than
the cut pen, its fill under the rectangle rule's 0.95 fill share — and 31130 p13's, 1,218" x 18"
with faces w9 and w4: the filled-wall rule's next counter-example, not this rule's; 31202's ramp
curb, 4" x 96" in the cut pen, which reads as a 4" wall; a hatched wall, whose diagonal hatch is
allowed across it but occurs on none of the five sets; a stair drawn without risers.

## 29. Step 21, done 2026-09-09: a wall's faces may converge, and a band thicker than any wall is not a slab

What §28 named as its counter-example, looked at. 31168 p11's west property-line wall is not a
tapered fill: the fill there is an axis-aligned grey band 66" x 1,631" (63" x 1,510" on p12,
p13), thicker than the thickest wall, and inside it run two cut-pen lines 15" apart narrowing to
12" over 1,527", slanted 1.9° off the grid along the property line — the wall. Three things kept
the rule of §28 from reading it, each found with the trace `pdf-overlay --walls` now prints (why
every pair of cut-pen lines 94"+ long was or was not a wall, and every long cut line with no
partner): the faces converge by 3.4", past the inch the rule allowed; the band's edge line
crossing the slanted pair at 1.9° read as a shaft's X; and a 96" stub at the wall's foot, 12"
from its outer face, took that face first because the walk went by index. And the band itself,
read as a slab, was the storey's only floor plate in the ETABS model — a strip 5.5 ft wide on P3
and P2 (§28's KF1–KF3) — and stood where the plate the perimeter walls enclose should go.

**A wall's faces may converge**: by an inch, or by a third of the mean gap, with a wall's
thickness at both ends. **A line crossing a pair and running on past both faces is somebody
else's line**; a shaft's X ends on the faces. **The longest pair first**, so a stub cannot take a
wall's face. **A face line is one wall's**: the lines along the filled walls' faces carry the
side their wall lies on (`FilledWallFaceLines`), and a line already a face — of a filled wall or
of a face wall made in this pass — may not be paired on its other side; that is what the 6" walls
flanking a stair flight are (31168 p11, 31138 p9: the 44"–45" between them is not a wall), and
the 8" ramp wall beside a 58" gap (31202). **A wall has a wall's proportions** in the face rule
as in the filled one (aspect at least 2): the tower plans' boxes of lines 49" x 38" around
unfilled columns, sixteen a sheet, are not walls. **A dashed line between the faces is one
line**, counted by where it lies across the wall, not by its dashes — the property line the
retaining wall stands on. And **a filled band thicker than the thickest wall, at least ten times
longer than thick and no wider than twice that thickness, is a band** (`PathReason.Band`,
unaccounted, inked and named in the ledger): not a slab, not a wall, what it is on the drawing
being a person's to say.

Tried and taken back, with the numbers: letting a face line serve several walls (piers along one
outer face) read the balcony bands along the tower slab edges as walls — 32 on S2.21.1 where Revit
has 28 walls in all, 750 across the set to the model's 666. Each face used once, longest first:
628, and S2.21.1 reads 28.

Measured after (the census from step 20): SLAB moved on 31168 p11–13 (17 → 16, 1 → 0, 1 → 0: the
three bands), WALL on 31130 p12 (29 → 30) and 31138 p11 (40 → 39), BEAM by the faces those took
or gave back; 8 of 13 identical; the five banked counts unchanged. Against Revit: 628 walls to
666, within two on 9 of 23 sheets, p11's west wall read at 1,527" x 12". The ETABS model from
the three parkade sheets now carries no floor plate at all — the slivers are gone and the
perimeter-wall fallback on the DXF side needs the walls as a ring inside a ring
(`PairConcentricWallRings`), which walls read as panels are not. That is Model Start's next
step, on the DXF side: a storey's plate is what its wall panels enclose.

WHAT THE CHECK COVERS (`TwoFaceLinesAWallsThicknessApartAreAWallTests`, 25 checks;
`AFilledBandThickerThanAnyWallIsNotASlabTests`, 6): faces converging within a third and beyond
it; a crossing line that overshoots; a stub at a wall's foot; a filled wall's face line refused
on its other side; the longest pair keeping a shared face; a box of column proportions; a dashed
line between; a band of 66", the 60" wall still a wall, a 121" strip a floor again, a short thick
fill a slab, a turned band, a paper band a mask. WHAT IT DOES NOT: the second pier along a shared
outer face, which stays lines; the balcony bands as spandrels, which the DXF side reads from BEAM
and this leaves there; what the 66" band is.

## 30. The PDF alone, measured 2026-09-09: what Model Start gives with no Revit and no reference

Ian, 09-09: *"What happens when we don't have the Revit model and just the PDF? That's what I'm
trying to build. I feel you're cheating by already having the Revit model."* Where the Revit side
has stood in this programme: the Revit DXFs are the yardstick only (`pdf-vs-dxf`: 628 PDF walls to
666), never an input to the PDF route; but every ETABS model built from the PDF so far took
Andrea's reference `.e2k` for its storey list, grid names, units and material names. That is the
cheat, and this section removes it and measures what is left.

`takeoff pdf-levels <stickfile.pdf> [levels.csv]` writes the set's storeys as the levels file
`dxf-to-etabs` already accepted in place of a reference (`-`): the storey heights the wall
elevations state (§16), chained from the lowest stated level at 0, in millimetres. On 31168 the
four elevation sheets give **22 levels, P3 to L19**, one base (P3), and one name stated twice
(L2 over L1 at 2,808 mm and L2 over P1 at 11,460 mm — building C's and the towers' second floors
share a name; the first stated is kept and the rest reported, the building-aware level names
being an open item).

Then, with nothing but the PDF — `pdf-takeoff --pages 9-32 --kor-layers` (23 DXFs, one per sheet,
named as views) and `dxf-to-etabs <dxfs> - out.e2k --levels levels.csv --levels-unit mm`:

| | PDF alone | same DXFs against her shell |
|---|---|---|
| sheets read / placed | 22 / 16 | 22 / 22 |
| storeys | 22 (the drawings') | 64 (the reference's) |
| walls | 1,007 | 1,607 |
| columns | 2,316 | 3,435 |
| floors | 0 | 0 |

The six sheets the PDF-only model cannot place are the two mezzanines (no elevation sheet states
a MEZZ level) and tower B's L28–L39 and tower A's L33–roof (the wall elevations read stop at L19;
the typical-storey note above that is not read yet). The walls and columns that do land are the
same objects either way; the count difference is range sheets (L4–L14, L15–L26) replicated onto
storeys the reference names and the drawings' ladder does not. Sections in the PDF-only model are
named by millimetre thickness with no concrete grade (`KOR-W305`), the grade being the engineer's
and the reference's `65 MPa Walls` the only source of the name.

**So the PDF-only route today**: every plan sheet to a view-named DXF on KOR layers; walls
(filled, clipped, doorway-split, and two-face), columns by declared size, footings, named grid
axes; a grid frame per sheet by axis name; a storey list off the wall elevations; a model that
ETABS opens. **What it does not give yet**: floor plates (the perimeter-wall plate that Andrea
ruled — *"follow the outer edge of the walls"*, 25 Aug — is written on the DXF side for walls
drawn as concentric rings, not for the panels the PDF route emits: the next step), the storeys
above the last wall elevation, mezzanine levels, building-aware level names, concrete grades.
None of these needs the engineer; each is a rule with a measurement.
