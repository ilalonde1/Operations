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
| `takeoff pdf-overlay` | what was extracted drawn over the rasterised page: slabs grey, columns blue, leftover lines red, mark-shaped words green | `takeoff pdf-overlay <pdf> 11 out.png --scale 96` |
| `takeoff pdf-vs-dxf` | the differential against ground truth we own: PDF-side reads against the Revit DXF of the same sheets, per sheet number | `takeoff pdf-vs-dxf <pdf> <dxfFolder> --scale 96` |
| `FiveStickFilesTests` | the harness, in C#: footings, column marks and route, wall rows, coverage floors, mark-shaped unplaced; 20 checks over 5 jobs in 32 s; FAILS when the local mirror is missing | `dotnet test --filter FullyQualifiedName~FiveStickFilesTests` |

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
outline) are counted, not split: `WallRibbonsNotSplit`.

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

**Still open on rules, and where it goes**: the corpus measurement is a one-off hand run from
14 August. Making it a compiled, re-runnable verb that reports each rule's coverage of the
portfolio — and does the same over the stick-file corpus for the `dxf.pdf.*` keys — is the step
that turns "a row must not break a read" into a number. It has to run on the file server itself
(the corpus is 1,126 models on the projects volume, and SMB enumeration is the one thing this repo
has learned never to do from a workstation), so it is a self-contained publish started over RPC,
and its own brief.
