# PDF intake — what it does today, and what it leaves on the page

Written 2026-09-08 from the code and from a content inventory of the five local stick files
(31065, 31130, 31138, 31168, 31202: 294 pages). Every number below was counted on the whole
population named; nothing is from a sample. The intake brief series lives in
`docs/codex/CODEX-INTAKE-CONVERGENCE-*.md` (28 so far; 23 is written for Codex, not yet run; 24 was Codex's audit, answered in §18) and this is the state they have reached.

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

Measured at step 0 (2026-09-08). The step sections from §7 on record what has changed since:
walls are emitted (§9), only plans are taken off to DXF (§11), the scale is accounted for on
every sheet (§12), footings are objects (§13, §14), the grid is named axes on a GRID layer (§15),
storey heights are read off the wall elevations (§16) and checked against the model in the publish
(§17), the audit's eleven findings are fixed (§18), dimension strings are typed with their values
(§19), and an AS NOTED sheet's views are read at their own captions' scale (§20). The tables in
§2 and §3 are the starting picture.

| Tool | Path today | What it gets from the PDF | What it does not |
|---|---|---|---|
| **PDF → ETABS** | PDF → DXF (CLI) → `DxfToEtabsService` reads the DXF by layer | slabs, columns, a scale, the schedule's column sizes | **walls** (the DXF has none: 31130 p11 gave 42 COLUMN, 17 SLAB, 1,650 BEAM, 0 WALL), footings as objects (since step 7 the DXF has a FOOTING layer; `DxfToEtabsService` does not read it yet), the grid, storey heights, openings from plan text |
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
