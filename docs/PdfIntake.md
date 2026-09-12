# PDF intake — what it does today, and what it leaves on the page

## 0. START HERE (state as of 2026-09-11, after step 43 — the audit of steps 31–41 answered, and a reissue read)

A session picking this up cold reads this section, then §30 (what the PDF alone gives), then the
last three step sections (§50, §51, §52). It does not need to read §1–§29 to work; those are the record of how each
rule was arrived at, and are read when a rule is being changed.

**Where the data is.** `%LOCALAPPDATA%\Temp\kor-drawings\stickfiles` holds the six PDFs (31065,
31130, 31138, 31168, 31202, and `31170-01-arch` — the ARCHITECT's set for 31170, the only one from
another office and the measure of whether a rule is universal). `...\kor-drawings\harness` holds one folder per job, the banked `.e2k`
baselines named for the step that produced them (`pdf-only-<job>-s42.e2k` is the most recent, with
its console beside it as `...-s42-console.txt` from s42 on), and
`revit-31168\out.e2k`, which is the Revit route's answer for the same building and the only yardstick
that is not our own output. Nothing here is read over SMB.

**The one command that measures every deliverable.** `bash docs/etabs-handoff/pdf_only_all.sh`
rebuilds all six jobs from their PDFs alone, in parallel, about 5 minutes, and prints ⛔ for a job
with FAILED pages or NO MODEL. Then `bash docs/etabs-handoff/six_set_diff.sh s42` reads every job
against the banked step in one line each — byte-identical, or plates moved / columns and walls lost
and gained, counted by `members_diff.py` on keyed members in a grid-aligned frame. When a step is
kept, `bash docs/etabs-handoff/six_set_bank.sh s43` banks model and console together. A change that
is not run through all six has not been measured; one job alone is `pdf_only_one.sh <job> <scale>`,
for characterising, never for deciding.

**Look at it, do not count it.** `takeoff pdf-overlay <pdf> <page> <png> --scale 96 --dpi 200
[--walls] [--columns]` renders what the reader saw onto the sheet, and `docs/etabs-handoff/crop_mm.py`
cuts a window out of it; `model_to_page.py` takes a model coordinate (a wall the diff named) back to
the sheet's millimetres through the grid and the overlay's own wall census, so the window is cut
where the member is. `render_storeys.sh` draws every storey of a model on one sheet. Step 27 was a
day spent guessing closing rules that a rendered view would have settled; that is the mistake this
line exists to stop.

**Where the route stands on 31168**, PDF-only against the Revit route, both built by the same
DXF-to-ETABS code:

| | PDF only | Revit route |
|---|---|---|
| columns | 2,502 | 2,380 |
| walls | 1,209 | 1,832 |
| storeys with a plate | **36 of 62** | all |

Parkade plates agree closely (P1 77,182 sq ft against Revit's 76,967), and since step 28 the tower
plates agree within 0.1% on every storey that has one (A-L27 9,753 against 9,743, B-L29 9,735
against 9,726, C-L4 15,002 against 14,989). The tower wall count is now the largest gap.

**The open list, largest first.** Each is one rule, and each is measured on the six sets before it
is kept. ⚠ Step 35 (§44) is the second rule this month that the six-set run REFUSED in its first
cut — a removal that was right on the architect's set and wrong on four of KOR's, because the
plate reader reads fragments there. A rule that acts on "outside the plate" is not universal until
the plate is; until then such rules COUNT and LIST. The count is now printed per storey ("n of m
wall(s) … beyond every plate read for the storey") and `outside_plate.py` gives it on an `.e2k`:

1. **Finish or abandon the boundary walk** (§38) — stashed, worth +13 storeys, held back by one red
   gate and an 11% corner notch. L15–L26 read 8,721 and 8,699 sq ft against Revit's 9,831 and 9,835
   — 11% under, the shortfall being exactly the four 287 sq ft corner blocks cut out. The bounding
   box is right to the millimetre; the boundary walk rounds the INSIDE of each corner block. It is
   rendered in `plan_sheet.py` as a rectangle with four notches and the corner columns outside it.
2. **A ring that is a piece of the floor rather than the floor** (§37). 31168's LEVEL 2 takes a
   4,222 sq ft rectangle where Revit's storey is 48,501 sq ft in three plates; tower C's L5–L8 take
   a 1,922 sq ft strip of a 14,988 sq ft floor. The gates measure the neighbourhood against the
   RING's own size, so a small ring in the corner of a big floor passes. By this document's own
   standard — *a storey with no plate is honest* — a 9% plate is not.
2. **Fifteen views on 31168 do not close, and they are NOT one cause.** Those 15 give all 25 of the
   storeys with no plate. Only **one** has been diagnosed — BLDG A's L4–L14, where A draws its tower
   corners as their own 5,133 × 5,186 mm rectangles and such a block **shares two of its four edges
   with the perimeter** — its top edge is a
   piece of the north edge, both at y 15,700 meeting exactly at x −10,482, and its outer vertical is
   a piece of the west edge. A segment can belong to only one ring, so whichever ring is built first
   spends them. ⛔ Feeding the small closed loops to the bridging pass was tried and **changed
   nothing on any of the five sets** (31168 stayed at 37 floors, zero storeys moved): the block
   re-closes on its own exact joins in that pass too. ⛔ **Seeding the walk from the longest segment**
   was tried second and is worse: it did not close BLDG A and cost 31168 a floor (37 → 36) and 31065
   a floor and four columns (9 → 8, 605 → 601). Two attempts on one symptom is where CLAUDE.md rule
   10 says to stop, so it is stopped. The fix is either that a piece of linework may serve a small
   loop AND the floor's edge, or that the outer boundary is found by something other than chaining —
   and the next attempt needs a differential showing which ring each shared segment ought to belong
   to, not a third heuristic. Both costs are written in `SlabEdgesFromLoops` where the code was.

   ⚠ **And the corner block does NOT explain the other fourteen.** Measured 2026-09-10 across the
   failing views: the block-sharing-the-extent signature is on **3 of 12** checked, and BLDG A's
   L15–26 — which an earlier draft of this item claimed it explained — has **none**. That claim was
   an unverified generalisation from one view and is withdrawn. The 15 are: A and B's L15–26
   (12 storeys), C's L5–L8 (4), the three L1 sheets (3), C's L3, A's L35, B's L28, B's L37.
   **Fourteen of them have never been rendered and looked at**, which is the next thing to do and
   is bounded work — not another rule.
3. **31202 — was 0 of 34 sheets, CLOSED by step 30's title rule (§39): 12 placed, 13 storeys,
   1,164 columns.** Not yet rendered or checked against anything; that is its next step.
   **31170's P1** (§39): 22 overlapping plates, no columns, 311 "columns" read off the plan —
   parking stalls, at a guess. And 31170's 49 room-sized plates are item 2 at full strength.
4. **Tower walls, 33 a storey against Revit's 40.**
5. **The storey-rise convention.** The top storeys sit one level off the Revit route's.
6. **Mezzanine levels** are read as storeys but not placed.
7. **31130's halves** do not join.
8. **L10 carries 58 columns against Revit's 48.**
9. **Parkade plan titles are not found as views** (§35's title reader), so a parkade sheet is not
   split the way a tower sheet is.
10. **C's floor lands on C-L4** where Revit has it on C-L5 to C-L8; **A-L34 reads 9,326 against
    Revit's 5,949**, unexplained.

**The working rule for all of them.** One universal rule per step, never a fix for one drawing;
banked as a test that states what it covers AND what it does not; measured on all five sets before
and after; and the cost written down when a rule is rejected, so it is not tried again.

---

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

## 31. Step 22, done 2026-09-09: the PDF alone gives the parkade its plate

Three rules, each Andrea's own or the drawing's own, and together the first floor plate the
PDF-only route has produced. Measured against Revit's plate on 31168's P1 and P2, 76,967 sq ft
each: the PDF alone now reads **77,240 sq ft on P1 and 77,478 on P2**, within 0.4% and 0.7%.

**A storey's plate is what its wall panels enclose, at their outer face.** Her ruling, 25 Aug:
*"it should always follow the outer edge of the walls"*; 07 Aug: *"just one thickness per floor,
general outline at first."* The DXF side had this for a Revit export's perimeter wall, drawn as a
ring inside a ring (`PairConcentricWallRings`); walls that arrive as separate panels — every plan
the PDF route emits — gave nothing, and the storey went to the model with no diaphragm.
`DxfFloodFillPlateDetector.EnclosedByWallPanels`: the panels painted solid on a raster, the paint
closed across gaps up to a doorway (`dxf.max-opening-span`, the banked 72": a wall stops at a
doorway and the floor does not), the outside flooded from the raster's edge and grown back by the
same radius so the closing adds no width, and what is left is the walls and everything they
enclose; its boundary is the outer face. A ring that does not close leaks, the outside floods
everything, and the storey keeps having no plate rather than being given the sheet. It runs under
the same gate as the ring reading — only where the drawing closes no slab at all, and never on a
FOUNDATION sheet (P3 is slab on grade, *"we just don't model it"*, 25 Aug). Where it cannot close
the ring, the report says at what gap it would have (a ramp or a garage door is wider than a
doorway, and the engineer decides whether the floor runs across it).

**A plan too wide for one sheet is split on a match line, and the sheet says so.** Her ruling,
banked in migration 059: *"A plan too wide for one sheet is cut on a MATCH LINE and drawn twice."*
The DXF side joined such sheets already, but only where a row `match-line-join.<job>.<storey>`
said to — a fact per building — and only on a MATCH layer the PDF route never wrote. Now
`SheetFurniture.MatchLines` reads the words MATCH LINE (stacked in the margin, or one word) and
the line spanning at least 40% of the page beside them, drawn dash-dot as many collinear pieces —
the line of the label's own orientation, since the label is written along it (31065's MATCHLINE
stands 6 pt wide and 48 pt tall beside a vertical seam on grid 8; 31168's MATCH / LINE lie flat
beside a horizontal one on grid J), the nearest such line within six text heights;
the pieces are read (`PathReason.MatchLine`), `Geometry.MatchLines` carries the line, and
`DxfExporter` writes it on `KOR_MATCHLINE`. On the DXF side a sheet read off a PDF sits in its own
page frame, so the seams are compared on the model's grid (each through its by-name frame, within
the fit's own tolerance, 6") as LINES that overlap — each half draws its own match line at its own
length (31065's north half runs 86 m from x −30.3 m, its south half 86 m from x −7.2 m, both on
y 24,722 to the millimetre) — and a partner's linework is brought into the leader's frame; a Revit
export, one frame for every sheet, is joined exactly as before. And where she banked which storeys
to join, those and only those join; where she banked nothing — every job nobody has modelled —
the drawing decides, `MatchLineSheetJoin` still asking that the other sheet carry the same line,
the same storey, and its linework on the other side. The census from step 21: MATCHLINE appears
on 31168 p11–13 and on 31065 p14–16 (its parkade is split the same way), BEAM falls by the dashes
(31168 p11 2,923 → 2,818), 7 of 13 files identical.

**A job nobody has modelled has no grid to set the sheets on, so the drawings' own reference plan
is the grid.** With no reference model, no sheet was set on the grid by name, each sat in its own
page frame, and 31168's P2 halves stood 7 m apart — the ring "closed at 24 ft", which was the
offset, not a gap. Now the sheet naming the most axes stays in its frame and its axes are the
reference (`GridAlignment.Carried` at the identity), and every other sheet is set on it by name:
22 of 22 on 31168, *"19 of 20 X and 8 of 10 Y grid lines matched by name … agreeing within 0.0"*.

**And a ring of walls that stands over few of the storey's columns is a core, not the floor.**
The banked `dxf.min-floor-coverage` (0.6), the composer's own rule for a floor that stops short of
its members, applied to the one reading that can produce a plate a core's size: before it, 31138's
L3–L22 and 31065's L2, L3 and L19 read 8 m x 9 m "floors" from their core walls, and the 31138
Revit-route baseline moved; with it, those storeys keep having no plate, the report says why
("stands over 7 of the storey's 34 columns"), and the baseline holds.

Measured after, on all five sets built from their stick files alone (`pdf_only_all.sh`):
31168 joins P3, P2, P1 and L1 from their halves and carries P1 77,240 and P2 77,478 sq ft against
Revit's 76,967; 31065 joins P3, P2, P1 and carries P1 35,490 and P2 35,640 sq ft against the
engineer's own 40,067 on P1 (89%, its ramp side still open); 31138's P1–P5 read 18,590 sq ft each
from their perimeter walls (no model to score them against); 31130's halves carry no MATCH LINE
label (its four MATCH words are notes, "to match wall verts") and stay apart; 31202's title block
reads nothing, so none of its 34 sheets names a level and none is placed — two readers' items,
not this rule's. The full suite, the Revit route and the two engineer models included: 1,194 pass.

Measured after, 31168 from the PDF alone (`pdf-takeoff` 23 DXFs, `pdf-levels` 22 storeys,
`dxf-to-etabs … -`): P3, P2 and P1 read as one plan each from their two halves; P1 and P2 carry
a plate each at the walls' outer face, P3 none; 1,042 walls, 2,328 columns. Still open on this
job, named by the report: the L1 halves meet on their seam now but their perimeter is slab edge,
not walls, so L1 has no plate; the L2 sheets and the tower plans have no perimeter wall either and
wait for the slab-edge rule (the outermost closed loop of cut-pen lines); the mezzanines have no
storey. The Revit route, the two engineer models and the five sets' PDF-only builds are the
differential — the full suite and `pdf_only_all` in this step's commit.

WHAT THE CHECK COVERS (`AStoreysPlateIsWhatItsWallsEncloseTests`, 6; `APlanTooWideForOneSheetIsSplitOnAMatchLineTests`, 6):
a rectangle of panels giving its outer rectangle; a doorway in one wall still the plate; a gap
wider than a doorway, none; two panels, none; an L; a turned ring; the stacked label beside a
dashed line spanning the page, a turned label beside a vertical line with a longer horizontal one
close by, a flat label ignoring a vertical line close by, no label, a label beside a short line; the pieces
fated MatchLine and not beams, the DXF's MATCHLINE layer and the seam the DXF side reads. WHAT IT
DOES NOT: the join on a real set (measured on 31168 in the build above, not banked as a test); a
match line drawn at an angle; two on one sheet with one label; a ring's re-entrant corner, which
the closing fills by about the doorway's radius (2.3% on the test's L).

## 32. Step 23, done 2026-09-09: what the audit of steps 20 to 22 changed

Codex read steps 20–22 against their own claims (brief
`docs/codex/CODEX-INTAKE-CONVERGENCE-31-AUDIT-STEPS-20-TO-22-THE-PDF-ALONE.md`, response
`docs/codex/CODEX-31-AUDIT-RESPONSE.md`, fourteen findings). Every one was checked against the
code. Six were real and are fixed, two are real and are now recorded as measured costs rather than
traded for something worse, and the rest were answered by instrumenting or by narrowing a sentence.

**The job is what the inputs say it is, not what the output was called.** A fact the engineer
banks against a job — which storeys of 31168 join on a match line, how many slabs a storey carries
— was matched against the output file's base name, so `out.e2k` matched nothing and every split
plan in the set joined, LEVEL 1 included, which is the one storey she asked us to leave as it was.
The job now comes from `--job`, or from the five-digit number in the stick file's, the DXF folder's,
the reference's or the output's name, first found; and where rules are banked for some job and none
for this one, the report says so instead of going quiet.

**Her row names a storey in her model's words.** Fixing the above surfaced the other half of it:
her row says LEVEL, this job read off its PDF alone calls the same storeys P3, P2, P1, L1, and the
restriction then silenced every join rather than narrowing it — 31168's parkade lost all four joins
and both its plates. Where her rows name no storey a run has, they cannot say anything about that
run: the drawings' own match lines decide, and the report says her rows went unused and why.

**A seam is read in the model's unit.** The by-name fits are solved in the model's unit, and their
frames were being applied to raw drawing-unit linework. Millimetre drawings on an inch model — the
stick-file route on 31168 — put the two halves' seams a factor of 25 apart, so the parkade she
asked us to join stayed apart on that route while joining on the PDF-only one, where both sides are
millimetres and the fault cancels. `TheStickFileBuildsAModelOnItsGridTests` now reads three sheets
and places two, the two LEVEL P2 halves being one plan.

**The other half is one sheet, on the other side of this sheet's line.** The side test measured the
partner against the partner's own match line, so the same seam drawn end for end flipped the sign
and refused the real other half; and a seam shared by three sheets took them all. Both halves are
now judged against the leader's seam, and where more than one sheet qualifies the one whose line
overlaps this one's the most is the other half.

**The plate's boundary is the outer face, not the raster's edge.** A cell was painted when its
centre lay within three quarters of a cell of a panel, so the traced ring ran up to a cell outside
the wall — about half a per cent of a parkade, which the first measurement read as agreement with
Revit. Each edge of the ring is now moved onto the panel edge it runs along and each corner is
where the moved edges meet.

**A pair is judged along its length.** `Covered` asked one point, the pair's midpoint, so two walls
that cross read as one under the other or not by which was made first. It now samples five points
along the pair's axis and asks for a majority.

**A line drawn as two strokes a point apart is one line.** The match-line reader bucketed strokes by
their rounded coordinate, so a thick pen or a dash-dot line whose pieces straddle a whole point fell
into two buckets and neither spanned the page. Buckets that continue each other end to end are now
one line, keeping the coordinate of the longest piece. On 31065 this changes which line the reader
calls the seam: it now reads the vertical line the drafter labelled MATCHLINE, beside the word,
instead of a horizontal line elsewhere on the sheet.

**And the trace says why every pair was or was not a wall**, including the pairs that never
qualified — the taper, the gap, the overlap and the aspect each now report themselves — and the
differential comparator compares the collections steps 20–22 added (`LineWidths`, `WallFaceLines`,
`FirstFaceWall`, `MatchLines`), which it did not.

**Two findings are real and were not traded.** A face line is consumed whole, so a retaining wall
whose inner face is broken by pilasters gives its longest pier only (Codex F7). Letting each wall
take just the stretch of face it lies along was built and measured: 31168's BLDG A tower plan went
28 walls to 52, every new one a balcony band beside a balcony's own box, and the same on towers B
and C. Balconies pair with a slab-edge line exactly as pilasters do, and no test on the pair alone
separates them, so the line stays whole and the cost is written into the check. The Band fate (F8)
likewise refuses a genuinely narrow floor — a 66" corridor slab reads as Band — and that waits for
the slab-edge rule rather than for a threshold.

Measured before and after on identical inputs (`plate_diff.py`, the same DXFs built by both
binaries): every set's storeys, walls, columns and floors come out the same, and only the plate
areas move, each one closer to what it is measured against — 31168 P1 77,246 → 77,182 and P2
77,481 → 77,144 against Revit's 76,967; 31065 P1 35,485 → 35,374 against the engineer's 40,067;
31138's P1, P2, P3 and P5 18,590 → 18,374 with no model to score them. The five sets' ledgers move
on 13 pages of 294 and no page's total changes; the thirteen banked plan DXFs are identical but
31065's three, where the match line is now read; the full suite passes 1,210 and the App suite 478.

WHAT THE CHECKS COVER (`TheJobIsWhatTheInputsSayItIsTests`, 6; the join and frame cases added to
`MatchLineSheetJoinTests`, `AnnotationOverlayTests`, `APlanTooWideForOneSheetIsSplitOnAMatchLineTests`,
`AStoreysPlateIsWhatItsWallsEncloseTests` and `TwoFaceLinesAWallsThicknessApartAreAWallTests`): the
job read from each kind of input path and their order of precedence; a frame unapplied is its own
inverse to the bit at a quarter turn; the other half found when its line is drawn end for end, and
one partner chosen where two qualify; a line drawn as two strokes a point apart; the plate's ring
on the outer face to a hundredth of an inch, turned as well as square; two walls that cross both
read whichever comes first; the face line spent on one wall. WHAT THEY DO NOT: the banked rows
against a live rules database (the service's own use of them is measured on the five sets, not
asserted); a job numbered otherwise than five digits; 31138's LEVEL P4, whose plate is suppressed
as a member already standing in that place — true before this step and after it, and not yet
explained.

## 33. Step 24, done 2026-09-09: a floor's edge is the outermost closed loop the plan draws

A parkade's perimeter is a wall, and step 22 takes its plate from the walls' outer face. Every
other storey's perimeter is a slab edge — the tower plans, the ground floors, the mezzanines —
and the drafter draws it as ordinary lines: 31168's tower sheets are titled CONCRETE OUTLINE after
it. The intake emitted those lines as beams, the DXF side saw no slab layer and no wall ring, and
every tower storey reached ETABS with no diaphragm at all.

**The rule.** After the walls are read, the lines nothing else claimed — not a wall's face, not a
match line, not annotation — are chained into rings by the builder the DXF side already uses on a
Revit export (`PlanLoopBuilder`, endpoints within a millimetre being one corner), and a ring is the
storey's slab edge when it is big enough to be a floor (400 sq ft, the DXF side's own
`MinPlateArea`), something stands in it, of the columns and walls within a tenth of its size most
stand in it, and it lies inside no other such ring. The ring goes to the DXF's slab layer as a
closed polyline and its lines are read as its edge (`PathReason.BecameSlabEdge`), not written as
beams. Where the drawn edge does not meet itself, this finds nothing and the storey keeps having
no plate, which the DXF side already reports. Nothing is bridged, extended or flooded.

**Measured first**, on 31168's BLDG A tower plan (p22): of the plan's 7,296 lines, one ring
closes on its own at 31,585 x 29,021 mm — the same bounding box, to the inch, as the plate Revit's
own export gives that storey (1,243 x 1,142 in) — and encloses 9,866 sq ft against Revit's 9,743,
the 1.3% being the balcony steps the drawn edge rounds off. Revit's tower plates, built from its
DXF export through the same DXF side (`harness/revit-31168`): A 9,743 sq ft on levels 27–32, B
9,726, C 14,988 on levels 5–8, A-LEVEL 33 9,676, B-LEVEL 38 9,612, B-LEVEL 39 9,465.

**Measured after**, the five sets from their stick files alone: 31168 goes from 2 floors to 28 —
towers A and B carry 9,870 and 9,867 sq ft on every storey L4 to L14, and the roof sheets' rings
(A L33 9,867; B L38 9,869, L39 9,616 by their boxes) close too but land on no storey, the levels
file ending at L19. 31065 goes from 3 floors to 5, both on L19 (6,665 and 3,305 sq ft: that sheet
draws the level, the roof and the elevator roof side by side, and two of the three rings close).
31130, 31138 and 31202 are unchanged in every count; the thirteen banked plan DXFs are identical
13 of 13 (the parkade and foundation plans, whose perimeters are walls). The ledgers move on 19
pages of 294 with no page's total changed — the lines that became a floor's edge are read now,
and on 31168's two roof sheets that is 244 and 248 of them. Core 1,220 pass, App 478.

**Open, named by the measurement, not by the rule:** tower C's L5–L8 on 31168 carry a 1,922 sq ft
ring — a strip of the 14,988 sq ft floor whose edge does not otherwise meet — and the neighbourhood
gate does not refuse it because C's columns stand more than a tenth of the strip's size from it;
31168's L15–L32 sheets and 31065's L2, L3 sheets have no ring that closes as drawn (their longest
chains end metres apart), which is the bounded bridging the DXF side does on a Revit export and
this step deliberately does not; a sheet that draws two storeys' plans side by side gives each its
ring, and where the sheet feeds one storey (31065's L19 with its roof) both rings land on it; and
on four notes pages (31130 p3, 31138 p3, 31168 p4, 31065 p4) twelve lines read as a floor's edge —
a detail's border with a small filled shape in it — which reaches no model, no plan page being
involved, and says the "something stands in it" gate is a weak one.

**And a cost found and removed:** the loop builder's bridging pass is every pair of open chains,
restarted after every merge, and with exact joins asked for it can never merge anything; on a
hatched plan with ten thousand dashes it spent a minute a page finding nothing. It now returns at
once when the bridge and the extension are no wider than the join, and the five-set batch runs in
four minutes.

WHAT THE CHECK COVERS (`AFloorsEdgeIsTheOutermostClosedLoopTests`, 8): four lines closing a
floor-sized ring becoming one slab, its lines read as its edge and kept out of the beams; a ring
too small; a ring with nothing in it; a core's ring inside a floor's; a strip with the structure
beside it, and the same strip with the structure in it; two plans' rings on one sheet; a ring left
open; a wall's own faces not an edge. WHAT IT DOES NOT: the areas on a real sheet (the build above,
not banked); a plan whose edge is drawn in pieces that do not meet; which storey a ring lands on;
a ring whose "structure" is a legend's filled square.

## 34. Step 25, done 2026-09-09: an elevation's ladder is every column of it, and a break is not a storey

The storeys come off the wall elevations (step 10), and the reader took one column of level
labels per sheet — the busiest — with the number beside each. An elevation taller than its sheet is
drawn in strips side by side, each strip a column of labels with its own level lines, and above
the storeys the buildings share each tower's levels are labelled for it: 31168's S3.12 carries
LEVEL 2–19 in one column and B-LEVEL 27–41 in others, at the same y as the lower strip's labels.
One column read the lower strip and merged the others' labels into its rows by y, so B-LEVEL 37
sat on LEVEL 16's row and was lost; the levels file stopped at L19, and every tower storey above
it had nowhere to land whatever the plans said.

**Three rules, each measured first.**

*Every column of the ladder is a ladder.* Level labels within 12 points of one x are a column;
a column of three or more is a strip's ladder and gives its storeys; a column of fewer is a
caption. A strip's ladder is drawn on both sides of it, so one statement per sheet is kept per
storey. *A level labelled for a building is that building's storey*: "B-LEVEL 37" is B-L37, the
building riding with the name, and the existing plan matcher places a BLDG B plan on it; a storey
stated from one building's level to another's — two towers' labels on one row — is nobody's and
is not chained. *A storey that skips names is a break.* 31065's S3.14 draws LEVEL 13 straight
above LEVEL 3 with a break line between them, and 31138's S3.10 LEVEL 17 above LEVEL 7: how a
drafter draws a run of typical storeys once. The drawn gap is the break's, not a height, and the
levels it skips stand at the set's typical storey each, said so per level. The chain that does
this moved out of the CLI into `SetStoreys.Levels`, where it can be tested; it was the pdf-levels
verb's own loop until now.

**Measured before**, on the three elevation sheets the five-set check banks: 31168 p37 read 21
storeys (LEVEL 2–19 and the parkade) where the sheet states 45; 31130 p53 4 of 4; 31138 p53 17 of
17. **Measured after**, the five sets' levels files: 31168 22 → 62 levels — L1 to L26 shared, A-L27
to A-L36 and B-L27 to B-L40 each on its tower, C-L3 to C-L9, A-L1 and B-L1 — every storey the
engineer's own model names but the two mezzanines; 31138 18 → 27, L8–L16 filled at the typical
2,995 mm across the L7–L17 break; 31065 13 → 22, L5–L13 at the typical 2,845 mm across the L3–L13
break, and its old 13 had been read partly off the column schedule's row pitch, which by luck is
about a storey; 31130 and 31202 unchanged. Then the models: 31168's floors 28 → 34 with the roof
sheets' rings landing — A-L33 9,686 sq ft against Revit's 9,676, B-L38 9,621 against 9,612,
B-L39 9,475 against 9,465 — and tower C's strip on C-L5 to C-L8 rather than the shared L5 to L8;
31138 places 18 sheets of 20 (was 15), 605 walls and 834 columns (393 and 496); 31065 places 15
of 18 (was 9), 404 walls and 605 columns (293 and 385). Full suite 1,226 pass, App 478. Against
the engineer's own 31168 model (`TheLiveSetsStoreysAgreeWithTheirModelTests`), 61 storeys now
match by both names where 20 did, 60 of them within 25 mm, and one disagrees: C-L9 over C-L8, the
drawing 3,202 mm and her model 3,502 — tower C's top storey, read for the first time. That is a
question for her, named in the check rather than silenced.

**Open, named:** 31168's L15–L32 still carry no plate, their rings not closing as drawn (step 24's
open item, unchanged); S2.22.1's two rings both land on A-L33 while the sheet is titled for
LEVEL 33, 34 and 35 — the title's level list is read as one level, a sheet-naming item; 31168
reads 1,639 walls and 3,426 columns where the Revit route reads 1,424 and 2,365, the tower sheets
now feeding every storey they name, which is the one-object-per-stack question the engineer has
already ruled on and the next step; a level whose tag is glued to its number ("12-TN") reads as
the number now, and a level the reader cannot pair with a number at all still reads as "LEVEL".

WHAT THE CHECKS COVER (`AStoreyHeightIsTheDistanceBetweenLevelLinesTests`, +3;
`ALevelABreakSkipsIsATypicalStoreyTests`, 5; the banked counts in `FiveStickFilesTests`): two
strips side by side read column by column, a strip's lowest label giving no storey of its own; a
building's column standing on the shared level under it and keeping the building in its names; a
two-label column that is a caption; the chain from the base; a break filled at the typical storey
with the level above the break placed on the filled ones and a level another sheet drew keeping
its drawn elevation; the typical storey from the storeys that are not breaks; a name stated twice
keeping its first statement; a storey across two buildings left aside while each building chains
on; a set with a break and nothing typical to fill it, left unchained and said. WHAT THEY DO NOT:
the ladder on a real sheet beyond the three banked pages; a break across a parkade or mezzanine
name, which carries no number to skip; a typical storey that is wrong for the levels it fills —
the fill is the set's most repeated height, not the tower's own.

## 35. Step 26, done 2026-09-09: a sheet is its views

A tower sheet draws two plans side by side under two underlined titles — "LEVEL 3 PLAN" and
"LEVEL 4 PLAN (L4-L14)" on 31168's S2.20.1 — and was written as one DXF named for the sheet, so
both plans' columns and walls landed on every storey the sheet's title names: 100 columns a storey
on L4–L14 where Revit's export gives 48, 3,426 on the job where the Revit route reads 2,380. The
office's own export names a file per VIEW — sheet number, view index, view title — and the DXF
side already takes a view's storeys from that name, so the stick file's sheet is now written the
same way: one file per plan view, each carrying what is drawn above its title.

**The rules.** A view's title is a line of text that names a plan — a level, a parkade level, a
roof or a foundation, and the word PLAN — with a stroke drawn under it covering at least half its
width; the stroke runs from the view's number bubble to the end of the words, so it is longer than
the words, which is why the furniture reader's heading underline (a rule no wider than its text)
never found these. A title in the title block's fifth of the width is the sheet's; a heading that
ends in a colon or names notes, a legend, a schedule or a key plan is a list's, not a plan's; a
title drawn with a double stroke is one title. What is drawn belongs to the title nearest below
it: the drop to the title plus how far the thing sits outside the title's own span — plans side by
side share a title height, so the span decides, and plans stacked one above the other (tower C's
LEVEL 5–8, LEVEL 9 and roof on S2.41.1) share a span, so the drop decides. A grid axis goes to
every view it crosses. A view titled for a level but not for a building is the sheet's building's,
and its name says so, else "LEVEL 35 PLAN" on tower A's sheet lands on no storey. A sheet with one
title, or none, is one view and is written as before. And a tagged sheet's second chance at
storeys no longer takes another building's: once C-L4 to C-L9 were named, a BLDG A plan took them
by number and tower A's floors stood on tower C.

**Measured**, 31168 from its stick file, columns and walls per storey against the Revit route
(`storey_counts.py`): L5–L26 50 columns a storey against 48 (was 100); A-L27 to A-L33 26 against
24 (was 52); B-L27 to B-L38 24 against 24 (was 48) and 30 against 31; C-L5 to C-L9 41 against 41,
10 walls against 10 (was 49 and 12, on the wrong storeys); the job 2,502 columns against 2,380
(was 3,426) and 1,209 walls against 1,832 (was 1,639). 31168 reads 35 views from 23 plan sheets
and places 29; 31138 47 from 29, places 26, 574 walls and 676 columns (605 and 834); 31065 48 from
22, places 17, its counts unchanged, L19 gaining the elevator roof's 621 sq ft; 31130 and 31202
unchanged; the thirteen banked plan DXFs identical 13 of 13. Full suite 1,235 pass, App 478.

**And an honest loss:** 31168's floors go 34 → 9. The 9,870 and 9,867 sq ft rings that stood on
L4–L14 since step 24 were the LEVEL 3 plan's ring, credited to L4–L14 by the sheet's title; they
now stand on L3, where they were drawn, and the LEVEL 4 (L4-L14) view's own ring does not close
as drawn — the same open item as L15 and up. The plates that remain are each view's own: A-L33
9,686, A-L34 9,326, B-L38 9,621, B-L39 9,475, C-L9's strip, L3, and the parkade.

**Open, named:** the tower views' rings that do not meet as drawn (the bounded bridge, now the
biggest single item on 31168); 31168 reads a third fewer walls than the Revit route on the tower
storeys (33 against 40, 12 against 18), which is the wall rule's next measurement; the top storeys
disagree by one with Revit — A-L34 38 columns against 17, B-L39 37 against 9 — because a stick-file
plan at level N draws what stands ON it and rises to N+1, while Revit's plan export of level N
draws what rises TO it, and the DXF side reads both as rising; L10 reads 58 columns against 48
from a second sheet naming it; a parkade plan's own title is not found as a view (the stroke or
the words differ), which costs nothing while the sheet has one plan and would cost the plan if it
had two; views stacked with no title between them.

WHAT THE CHECK COVERS (`ASheetIsItsViewsTests`, 8; `ATaggedSheetsSecondChanceNeverTakesAnotherBuildingsStorey`):
two titled plans found from their words and the stroke under them, the stroke running past the
words; the columns, walls, slabs and lines above each going to it with the face-line and slab-edge
indices remapped; a vertical axis to the view it crosses and a horizontal one to both; one title or
none staying one part; a notes heading, a key plan, the title block's title and a double stroke not
making views; a title with no stroke not a view; plans stacked one above the other split by the
drop; a view without a building taking the sheet's; the view file's name; a tagged sheet refused
another building's storey. WHAT IT DOES NOT: a real sheet's titles (the builds above); a title
whose words the extractor put on two baselines; what is drawn under no title at all; a sheet whose
two views share one building tag in the title block but differ in the titles.

## 36. Step 27, 2026-09-09: an edge interrupted is still one edge — and what a flood fill answers instead

Step 24 gave a storey its plate from the outermost closed ring the plan draws. On 31168 that found
9 plates for 62 storeys. The towers have none: their outline is drawn as a "CONCRETE OUTLINE" that
steps out round every balcony, and on the L4–L14 view the west edge is two 5,186 mm pieces 18.6 m
apart. Exact rings on that sheet: 12. Open chains: 208.

**The rule kept.** A gap the width of a leader is not a gap. Open chains of 2 m or more are run a
second time through the same loop builder the DXF side uses, with its own banked tolerances — join
1 mm, bridge 6 in, extend 48 in — so an edge broken where a leader crosses it closes, and an edge
stopping short of its corner is carried to it. Five cases are banked in
`AFloorsEdgeIsTheOutermostClosedLoopTests`: the hand's-width break, the short corner, a 3 m gap
that stays open, a hatch of a thousand short dashes that is never searched (only chains ≥ 2 m are),
and an edge stepping round a balcony, which closes through the balcony's own diagonal and lands
between the outline's area and the outline-plus-balcony.

**Measured cost: nothing, and nothing gained.** Across 31065, 31130, 31138 and 31168 the second
pass moved zero plate lines against the step-26 baseline. 31168 stays at 1,209 walls, 2,502 columns
and 9 floors. The rule is right and it is free; on these five sets it changes no drawing.

**⛔ MEASURED AND REJECTED: the flood fill.** The DXF side already has a recovery for a slab edge
that will not close — `DxfFloodFillPlateDetector.RecoverAll`, which paints the long lines, bridges
the gaps, floods the outside and takes the boundary of what is left. It was wired in as the
fallback where no ring closed, gated the same way on area and on structure standing inside. On
31168 it did not find a tower plate. It found the **cores**: 858 sq ft on A-L27 to A-L32, 1,218 on
B's, 869 more beside them on L15 to L26, against Revit's 9,743 sq ft. And it cost the parkades
their step-22 plates, P1 77,182 → 5,536 sq ft and P2 77,144 → 893. A recovery that answers with the
core when it was asked for the floor is not a weaker version of the ring rule; it is a different
and wrong one. It is reverted, and the reason is written where the code was, so it is not tried a
third time.

**What this leaves.** The tower plate is still unread, and it is now the largest single gap in the
PDF-only route: 53 of 62 storeys on 31168 carry no floor. The next attempt does not start from
another closing heuristic. It starts by rendering the L4–L14 view's slab-edge candidates and
looking at them (`pdf-overlay`, `crop_mm.py`), because three closing rules have now been guessed at
and measured, and the picture has not.

→ **Answered in §37 (step 28).** Rendered, the edge turned out to be a plain rectangle the drawing
closes; nothing needed closing. All four sides had been eaten by the wall reader before the ring
builder ran. Every rule guessed at in steps 24–27 was aimed at a symptom whose cause was already
written in this repo's own code comment. **Render first is not advice.**

## 37. Step 28, done 2026-09-10: a wall standing on the slab edge does not remove the slab edge

Step 27 ended by saying the next attempt must not be a fourth closing heuristic, and must start by
rendering the L4–L14 view and looking at it. Rendered, the view settles the question in one glance:
**the tower slab edge is a plain rectangle at the outermost face, and the drawing closes it.** It
does not step out round every balcony. Each side is drawn as three collinear pieces, two five-metre
corner pieces and one long middle run, measured off the PDF's own paths with no reader involved:

| side | pieces (mm) | total |
|---|---|---|
| north | 5,133 + **21,320** + 5,133 | 31,586 |
| south | 5,133 + **21,320** + 5,133 | 31,586 |
| west | 5,186 + **18,649** + 5,186 | 29,021 |
| east | 5,186 + **18,649** + 5,210 | 29,045 |

That rectangle is 31,586 x 29,021 mm against the 31,572 x 29,007 mm bounding box of Revit's own
plate for the storey — twenty millimetres. Andrea's ruling reads straight onto the sheet (*"the
outer continuous line"*, 25 Aug): the stepped line inside it is the 60"/10" slab-thickness step,
which she has already said is not the edge, and the crossed boxes along the perimeter lie INSIDE
Revit's plate, so whatever they mark they are not holes in the floor.

**Why nothing closed: the edge was gone before the ring builder ran.** Each long edge line runs
parallel to a balcony band's own edge — 711 mm (28") away north and south, 264 mm (10.4") east and
west — and the two pair as a wall's faces. `WallsFromFaceLines` takes a face line WHOLE, deliberately
(the note there records the trade-off, and rejects taking only the overlapping stretch because it
read 52 balcony bands as walls). `SlabEdgesFromLoops` then skipped any line that was a wall's face.
So a 3,633 mm overlap consumed a 21,320 mm north edge and a 4,828 mm overlap consumed an 18,649 mm
west edge, on all four sides. Three ways of seeing it: the face trace makes exactly 4 walls on p22
(143"x28.0" twice, 190"x10.4", 190"x10.6"); `pdf-inventory` accounts exactly 8 paths as
BecameWallFace, those four edges and their four short partners; and the DXF holds no segment
between 9,046 mm and the title block's linework, the corner pieces surviving and the middle runs
gone. Step 27's *"the west edge is two 5,186 mm pieces 18.6 m apart"* was the eaten line: the 18.6 m
is exactly what the wall took.

The cost was recorded in the code as *"the second pier"*. It was the entire floor plate on every
tower storey, and three closing rules were then guessed at against a symptom whose cause was already
written in the file.

**The rule.** A wall's face line is offered to the chain when the wall did not use the whole of it;
the line goes in WHOLE, because it is one line the drafter drew and a floor's edge runs along the
wall standing on part of it. A face the wall runs the full length of stays the wall's. The leftover
has to be long enough to be a piece of an edge, which is the bridging pass's own bound
(`SlabEdgeChainMinMm`, 2 m), not a new number.

Offering EVERY face line back was tried first and measured: it invents floors. 31168's LEVEL 2 sheet
chained slanted walls' own faces into a **12,391 sq ft chevron** with columns inside it, so the
neighbourhood gate passed it — and one look at the rendered storey showed it was not a floor. The
leftover test removes it. That is the second time on this pipeline that rendering caught what every
count called green.

**Measured on all five sets, before and after** (`pdf_only_all.sh`, then `plate_diff.py … mm`
against the banked s26 baselines):

- **31168: floors 9 → 37, storeys carrying a plate 9 → 36 of 62.** Twenty-eight storeys that had no
  plate gained one; **no plate that existed changed, and none was lost** — P1 77,182, P2 77,144,
  L3 9,870/9,867, A-L33 9,686, A-L34 9,326, B-L38 9,621, B-L39 9,475, C-L9 1,922 are identical.
  Walls 1,209 and columns 2,502 unchanged, so the rule moved floors and nothing else.
- **Against Revit, within 0.1% on every tower storey**: A-L27–L32 9,753 v 9,743; A-L33 9,686 v 9,676;
  B-L27 9,746 v 9,737; B-L29–L35 9,735 v 9,726; B-L36 9,649 v 9,641; B-L38 9,621 v 9,612; B-L39
  9,475 v 9,465; C-L4 15,002 v 14,989; L4–L14 9,870 v 9,859.
- **31138 and 31130: identical to baseline**, every plate unchanged.
- **31065: L19 only**, 3 plates to 4 (6,665 / 6,610 / 3,305 / 2,168) — the known open item, that
  sheet drawing the level, the roof and the elevator roof side by side. No other storey moved.
- Core suite 1,242 pass, App 484.

**Open, named by the measurement:**

- **Tower A's L4–L14 and L15–L26 views still do not close**, while tower B's L4–L14 does. On A's
  sheet the NE corner piece is drawn 0.8 pt (27 mm) off the middle run's y, so the pieces meet at a
  T rather than a corner. That is why L4–L14 carry one plate where Revit has two, and L15–L26 carry
  none: 12 storeys, the largest remaining piece.
- **A ring that is a piece of the floor rather than the floor.** LEVEL 2 now takes a 4,222 sq ft
  rectangle where Revit's storey is 48,501 sq ft in three plates. The gates measure the
  neighbourhood against the RING's own size, so a small ring in the corner of a big floor passes —
  the same gate that lets tower C's L5–L8 take a 1,922 sq ft strip of a 14,988 sq ft floor. This
  rule adds one instance to that class; it does not create it. **It is the next step**, and by this
  document's own standard (*a storey with no plate is honest*) a 9% plate is not.
- C's floor lands on C-L4 where Revit has it on C-L5 to C-L8 — storey assignment, not the edge.
- A-L34 reads 9,326 against Revit's 5,949; unchanged by this rule and unexplained.

WHAT THE CHECK COVERS (`AFloorsEdgeIsTheOutermostClosedLoopTests`, +2, 15 in all): a wall standing
on a quarter of the edge, read as a wall, with the floor still closing at its full area; and a wall
running 7 m of an 8 m edge keeping the line, so no floor is read. WHAT IT DOES NOT: the areas on a
real sheet (the build above, not banked); a ring that is a piece of the floor, stated in the class's
own remarks with both live instances; a face line shared by two walls; and the corner mismatch that
still leaves tower A's two views open, which only the build shows.

## 38. Step 29, ATTEMPTED 2026-09-10 and NOT LANDED: a floor's edge as the outer boundary

⛔ **The code for this is NOT in the build.** It is stashed, not committed, because it turned one
gate red (below). Everything here is measured and is kept so the next attempt starts from it
rather than from nothing. The shipped state is step 28: 36 of 62 storeys.

Step 24 read a floor's edge as *the outermost closed ring the plan draws*. On a tower plan that
question has no answer. The corner blocks and the balcony boxes are drawn AGAINST the edge and
share their outer sides with it, and a segment can belong to only one ring — so whichever ring is
walked first spends the shared piece and the floor is left in fragments. Two ways of choosing
between the rings were built and measured on all five sets, and both failed (§37's open item 2:
feeding the small closed loops to the bridging pass changed nothing; seeding the walk from the
longest segment cost 31168 a floor and 31065 a floor and four columns).

**The question was wrong.** What a floor's edge IS, is the outside of everything the plan draws.
That is a boundary walk, and it does not care which ring a shared segment "belongs" to, because it
travels along the segment's outward side and carries on.

**The rule** (`GeometryFilterService.OuterBoundary`). Loose ends are dropped first — a floor's edge
has none, and a leader or a dimension hanging off the perimeter would be walked out and back as a
spike; dropping every node with fewer than two neighbours, repeatedly, leaves only what lies on a
cycle. Then **every** remaining piece of linework whose own bounding box could hold a floor offers
its outside, and the gates already there — floor-sized, structure standing in it, most of the
structure near it inside it, inside no other ring — choose between them. Each walk starts at the
leftmost node of its piece, which is on the outside by construction, arrives heading south, and at
every node takes the most clockwise turn. Nodes are merged at the banked bridge tolerance
(`SlabEdgeBridgeMm`, 6 in) rather than exactly, because the drafter's pieces meet within a hand's
width and not on the millimetre.

**⛔ Two things measured and rejected inside this step, so they are not retried.** Walking only the
BIGGEST piece by node count: on a tower plan the perimeter and the core are separate pieces and the
core has far more nodes, so it traced the CORE and 31168 gained exactly one plate, on LEVEL 2, with
no tower view closing. And flipping the turn from clockwise to counter-clockwise: no change at all
on 31168 — the corner notch below is not a handedness problem, and the tests pin the convention.

**WHAT THIS IS NOT: a flood fill.** Nothing is painted, no gap is closed and no pixel is involved;
the rejected `DxfFloodFillPlateDetector.RecoverAll` (§36) answered with the core and cost the
parkades their plates. Where the perimeter is genuinely broken this returns the boundary of the
piece it is on and the gates throw it out, exactly as before.

**Measured on all five sets, before and after:**

- **31168: storeys carrying a plate 36 → 49 of 62; floors 37 → 63.** L15 to L26 gain both towers'
  plates, which is 12 storeys that had nothing. Columns unchanged at 2,502.
- **31065, 31130 and 31138 are identical to their step-28 baselines** — zero storeys moved on any
  of the three. This rule touches only the job whose plans are drawn this way.
- Walls 1,209 → 1,233: +2 a storey on L16–L26 and on B-L27, columns unchanged. Revit reads 40 a
  tower storey against our 33, so the move is toward it; it is named, not explained.

**Open, named by the measurement:**

- **The corner notch.** L15–L26 read 8,721 and 8,699 sq ft against Revit's 9,831 and 9,835 — 11%
  under, and the shortfall is 1,149 sq ft, which is exactly the four 287 sq ft corner blocks cut
  out of the corners. The bounding box is right to the millimetre (31,585 x 29,021); the walk goes
  round the inside of each corner block instead of its outside. **Rendered and looked at**
  (`plan_sheet.py`): the plate is a rectangle with four square notches and the corner columns
  stranded outside it. Merging nodes at the bridge tolerance did not close it.
- Thirteen storeys still carry no plate: L1 and A-L1/B-L1, C-L3, C-L5 to C-L8, A-L35, A-L36,
  B-L28, B-L40. P3 correctly has none.

WHAT THE CHECK COVERS (`AFloorsEdgeIsTheOutermostClosedLoopTests`, +2, 17 in all): a block drawn
against the edge and sharing a run of it, where the floor comes back whole and the block is simply
inside it — the shape ring-chaining could not answer; and a leader hanging off the edge being a
loose end rather than a spike in the floor. WHAT IT DOES NOT: the corner notch above, which is live
on a real sheet and not banked; a block whose outer side is drawn OFFSET from the edge rather than
along it, which is what the notch is; two floors touching, where one boundary would wrap both; and
the areas on any real sheet, which only the build's own count against Revit shows.

**The instruments this step needed are in the repo now, not in a scratchpad** —
`docs/etabs-handoff/pdf_lines.py` (what the PDF itself draws, no reader in the way),
`view_breaks.py` (a contact sheet of every failing view with its open ends ringed), and
`view_parts.py` (a view's wall panels as objects). `chains.py` was reading LINE and LWPOLYLINE only,
so it answered "0 segment(s)" on every DXF the PDF route writes; it reads POLYLINE/VERTEX now.

## 39. Step 30, done 2026-09-10: the words a drawing names a level with are a vocabulary — and the first set from another office

Ian, 2026-09-10: *"I want to make sure you haven't lost sight of what we're trying to do here with
one ingestion point and multiple outlets. Are you doing this the most efficient professional way?
How close are we to finishing? And finishing does not mean 47 of 69 floors."* The honest answer
was that steps 28 and 29 had moved only 31168 — the four other sets were byte-identical both
times — so the tool was being tuned to one job, whatever the rules were called. The right test
was a set it had never seen. He gave one: **the architect's 75% BP set for 31170** (2005–2045 West
49th, Vectorworks, 67 pages) — not a KOR stick file, the drawings that arrive *before* any
structural model exists, which is the case the PDF-only route is for.

**Run untouched, it read the hard part and failed on two phrases.** 67 of 67 sheets numbered,
titled and typed (49 plans, 6 schedules, 11 sections/elevations); scale on 60 of 67; plan
geometry on all 16 plan sheets — 56–57 columns a floor, the same count off the floor plans and
off the slab plans, which is two independent sheets agreeing. Then: **0 storeys from 11 elevation
sheets**, and every plan named `A101_1_-.dxf`, a sheet of no storey. No model. Both failures were a
convention compiled into the binary, which §3 of this document already names as the one recurring
class.

**Rule one: the words a drawing names a level with are a vocabulary.** The ladder reader
(`ScheduleGridReader.ReadLevelLadders`) keyed on the literal token LEVEL (or B-LEVEL). The architect
writes `Top of Slab-L5` down every section and elevation, with `260.50'` and `79.40m` under it and
`10'-5"` between levels — the cleanest storey ladder in the six sets, and the reader found none of
it. Now a level label is any word that ENDS a phrase from the vocabulary, matched against the words
to its left on the same baseline as a chain of neighbours (a space apart, not everything within
reach), with a level value glued to the phrase by a dash read as the level (`Slab-L5` → L5). The
compiled defaults are true of drawings generally — LEVEL / LVL / LEV, TOP OF SLAB / T.O. SLAB /
T/O SLAB / T.O.S. / TOS, TOP OF CONCRETE / T.O.C., FIN. FLOOR / FFL — and the KorStandards row
`dxf.level.label-words` extends them without a build. ⛔ Not a mutable static: the words are passed
down, because `PlanSheetNaming.Vocabulary` already cost a day as one (CLAUDE.md).

**Rule two: a sheet's title is whichever of its statements names a level.** A set states a sheet's
title up to three ways — the title block's field, the PDF's own bookmark, the title text on the
page — and `SetStoreys` already fell through them in that order. `SheetDxfName.For` took the field
alone; on this set the field reads `-` and the bookmark reads `A101-LEVEL P1 PLAN`. Now the field is
kept when it names a level, else the bookmark (its own leading sheet number dropped), else the
field, the bookmark, the fallback — so a KOR sheet, whose field names the level, is named exactly
as before.

**Measured on six sets:**

- **31170 (the architect's set): nothing → a model.** 11 of 11 elevation sheets state storeys;
  9 levels P1, L1–L8, storey heights within 5 mm of the drawing's own metres (L5→L6 3,175 mm
  against 82.58 − 79.40 = 3.18 m). 49 sheets read, 47 placed, 483 columns (~72 a storey),
  3,635 walls, and a 32,076 sq ft plate on every storey P1 to L8. **Rendered** (`plan_sheet.py`):
  the plate is the building's footprint on every floor with the columns inside it and the
  partitions on top — an architectural plan draws every wall, and that is what the input is.
- **31202: 0 of 34 sheets placed → 14 read, 12 placed, 13 storeys, 1,164 columns, 374 walls.**
  Open item 3 in §0 since step 22, "a whole job produces an empty model, the title block is the
  suspect" — it was rule two: the same convention, the second job. Not yet rendered or checked
  against anything; that is its next step, and it is a job that had NOTHING.
- **31065, 31130, 31138, 31168: identical to their step-28 baselines** — 0 of 4 moved a plate;
  31168's 62 levels byte-identical. Two rules, two jobs changed, four untouched: the shape a
  universal rule is supposed to have.
- Core suite 1,241 pass and one red that is **not code**: `Langara31168ParkadePlansBuildOnThe
  ReferencesGridByName` reads the newest dated 31168 stick file on the share, and
  `31168-01 - 2026-09-10 - … Stickfile - With Arch.pdf` was issued there today. It was red before
  any of today's edits (proved with every change stashed). That is a reissue, and the pipeline has
  a verb for it (`set-diff`); it is named here, not silenced.

**Open, named by the render:** 31170's **P1 is wrong** — 22 overlapping plates, no columns, though
the P1 plan read 311 "columns" (parking stalls, at a guess: the size window is the only
discriminator on a sheet with no column schedule, and an architect's set has none). 31170's walls
are every partition, so which are structural is a downstream question the architect's own WALL
SCHEDULE (A005) answers by type. And the 49 room-sized plates on 31170 (400–1,800 sq ft, every
apartment's walls closing a ring) are §37's open item 2, "a ring that is a piece of the floor",
at full strength: on an architectural plan every room closes.

WHAT THE CHECKS COVER (`AStoreyHeightIsTheDistanceBetweenLevelLinesTests`, +3, and
`ASheetFromTheStickFileIsNamedLikeAViewTests`, +3): "Top of Slab-L5" as three words with the level
glued to the last and the geodetic heights under it giving the ladder; "T.O. SLAB 2" as a phrase
with the level beside it and a parkade level in the ladder; a phrase outside the vocabulary
("TOP OF WALL") naming nothing; the bookmark naming the sheet when the field reads "-" or is absent,
with its own number dropped; the field kept over the bookmark when it names the level; and the
field-then-bookmark-then-fallback order when neither does. WHAT THEY DO NOT: the vocabulary read
from KorStandards rather than the compiled defaults (the row is not yet seeded — migration to
follow in KOR.Drafter\db); the real sheets (the six-set build above); a phrase split across two
baselines; and a level named by a symbol rather than words.

## 40. Step 31, done 2026-09-10: every sheet is read at the scale it states

The intake has read each sheet's stated scale since step 6 (`SheetScaleReader`) and then scaled
every sheet's geometry by the CLI's `--scale` anyway — the stated scale was recorded on the record
and never applied. It did not show on KOR's sets, whose plan sheets all state one scale. The
architect's set for 31170 draws each storey four ways: a floor plan and a slab plan at 1/8", and
enlarged part plans (SW, NE, SE, NW) at 1/4". Read at one scale the enlargements landed on the
storeys at twice their size — 3,635 walls on nine storeys, a 55,219 sq ft plate over the 32,076 one,
and 32 of 49 sheets that could not be set on the grid because their axes were a factor of two out.

**The rule.** The sheet's stated scale is read from the full (un-thinned) page read, which is in
hand before the geometry is parsed and whose words are the same, and the geometry is parsed at it.
The request's `--scale` is the fallback for a sheet that states none, and a title block stating two
different scales counts as stating none (`DrawingIntake.StatedScaleDenominator`). Whether to
classify at all is still the request's — `pdf-inventory` with no scale still reads no geometry.

**Measured on six sets:** 31170 — **49 of 49 sheets set on the model's grid by name** (was 17),
walls 3,635 → 1,453, floors 58 → 24, 1,880 duplicate members refused by the stand-down rule as
"a place another sheet had already filled". The five KOR sets **identical** to their step-30
baselines, every count and every plate. Core 1,250 pass and the known reissue red.

**Open, named by the numbers:** columns on 31170 went 71–77 a storey to 51, below the 56 a single
floor plan reads — so with several sheets drawing one storey, which sheet the storey is modelled
from needs its own rule (the plan at the set's plan scale; enlargements are references). That is
step 33's, after the wall types.

WHAT THE CHECK COVERS (`ASheetIsReadAtTheScaleItStatesTests`, 3): an imperial note giving its
denominator, a metric ratio, and a sheet stating nothing giving null so the request's scale stands.
WHAT IT DOES NOT: the real sheets (the harness); a view's own caption on an AS NOTED sheet, which is
`ViewCaptions`' and applies to ladders; a sheet whose stated scale is wrong for the plan on it, which
only the self-check can see.

## 41. Step 32, done 2026-09-10: an assembly schedule is a legend of cards, and a card is read whole

Ian: *"for the wall type 'unknowable' — is it knowable now that you yourself pointed to where it's
listed (A005)?"* It was, and the answer changes the model. An architect's set states its wall and
floor types on schedule sheets laid out as **cards**: a heading `CODE - NAME` with the code drawn
again in its own symbol beside it, the build-up one line per layer, a ratings block (F.R.R.,
S.T.C.) with the value provided and the reference, and remarks. 31170's A005 carries 21 wall
cards and A006 ten floor cards. The code letter is the material — **C** cast-in-place concrete
(C6…C20, C10.f/C12.f foundation, EC12 exterior), **B8** CMU, **S/SW/SF/ES** steel stud, **VS**
gypsum shaft wall — and the plans tag their walls with these codes: **887 tags on the enlarged 1/4"
plans (pages 49–67) against 24 on the 1/8" floor plans**, which is a drafting convention worth
knowing (the key plan is too dense to tag; the enlargement carries the tags). The tag counts say
what the building is: 635 steel-stud tags against 24 concrete. The solid party walls the tool was
sending to ETABS as concrete are `S8.1 STEEL STUD PARTY WALL`.

Ian: *"Can we make sure to extract all the info out of these schedules??? That seems important."*
So the card is read WHOLE — every layer, both ratings, the references, the remarks — and the
material and thickness the structural model takes are derived from the words by a vocabulary
(`AssemblySchedule.StructuralWords` / `PartitionWords`; KorStandards rows
`dxf.assembly.structural-words` / `partition-words`, not yet seeded). `takeoff pdf-assemblies
<set.pdf> [out.csv]` prints the table and writes every field.

**The rules, each of which the real sheet taught:** a row is words whose vertical extents overlap
(PdfPig sets a dash 3 pt below its letters, and a fixed bucket put every `-` on its own line, so no
heading had its dash and no layer its bullet); a legend is a **fixed grid** — a card's right edge is
the sheet's next column whether or not this row fills it (an empty column let C12 take the overflow
of the card above); a rating states a number (2HR, 1HR., 55 — `OmniClass` beside a label is a
reference); the thickness is the name's, else the **thickest** layer of the material (2" concrete
pavers sit on a 12" slab); nothing "@ … O.C." is a thickness; and a card whose symbol disagrees
with its heading is read and the disagreement recorded as a finding — **A005 draws `C13 - 13" C.I.P
WALL` with the symbol `C12` beside it**, a copied card whose symbol was not updated, which is a
coordination error to tell the architect, not a card to drop. A dashed heading with no symbol counts
only on a sheet that has at least one confirmed card; on a sheet with none it is a table row
(31065's `DW1 - 4-30M3200 @ 400 DOWELS`).

**Measured on six sets:** 31170 — **31 cards, 21 walls (13 structural, 8 stud), 10 floors (9
structural: F7.5 191 mm, F9 229, F12 305, F18 457, F22 559, F24 610, R1 305 under its pavers)**, the
count matching the raw PDF text's; the five KOR sets **0 cards** each (their schedules are
tabular, read by `MarkRowScheduleReader`). Core: `AnAssemblyScheduleIsALegendOfCardsTests`, 6.

⚠ **Rule 7 bit, exactly as written.** A `\b` written through a Python heredoc landed in the C# regex
as a BACKSPACE (`^H`), the pattern matched nothing, and `VS.1` read "@ 600mm O.C." as 600 mm. Found
by `cat -A`; fixed with a raw-string script that reads the file back and counts control characters.

**Not yet wired — step 33:** the plans' tags to the walls they sit on, a partition layer the DXF
side does not model, and the report saying how many walls are concrete, stud, and untagged.

WHAT THE CHECK COVERS (6): two cards side by side and one below each read whole; a symbol
disagreeing with its heading, read and recorded; a dashed row on a sheet with no confirmed card
not a card; the name deciding the material before the layers; the thickest structural layer as
the thickness, a decimal inch, an on-centre spacing not a thickness; the kind from the title.
WHAT IT DOES NOT: the real sheets (the six-set run above); a card whose lines wrap into the next
column; the vocabulary from KorStandards rather than the defaults; which wall carries which code.

## 42. Step 33, done 2026-09-10: a wall is what its tag says it is

With the assembly schedule read (§41), each plan's walls take their type from the plan's own tags:
a tag is a word equal to a schedule code, not furniture; a wall takes the nearest tag within reach
of its axis (`WallTypeTagging.ReachMm`, 1,200 mm — on 31170's 1/4" plans a tag stands within about
600 mm of its wall and a bay is 3 m or more); the code's material comes from the card. A wall whose
type is a partition (stud, gypsum) goes to the DXF's **`KOR_PARTITION`** layer, which no wall-layer
pattern matches, so the model does not read it; every tag on the sheet is written as TEXT on
`KOR_WALLTYPE` so a reader of the DXF can see what the plan said. A wall with no tag within reach is
modelled as drawn and counted — the report says what it could not decide; it does not guess.

The set's schedule is read once per `pdf-takeoff` run and rides in `IntakeRequest.Assemblies`;
`SheetViews` carries the per-wall type into each view. Layer names are how DXF carries meaning — the
Revit route's own convention — and this is the honest way to carry a fact through it. What DXF cannot
carry is a type learned on ANOTHER sheet, which is the next item and the case for the composer
reading the intake's record directly.

**Measured on six sets:** 31170 — **1,673 tags on the plans; 1,174 walls typed, 1,046 of them
partitions sent out of the model; 1,856 walls with no tag within reach, modelled as drawn.** Walls
1,453 → 1,062, columns 351 unchanged, L3 310 → 232. The L3 enlargement alone put 438 partitions on
`KOR_PARTITION`; the 1/8" floor plan put 0, because it carries no tags. The five KOR sets **identical**
to step 31 (no schedule, no tagging, every count and plate the same). Core: `AWallIsWhatItsTagSaysItIsTests`, 4.

**Open, named:** the 1,856 untagged walls are mostly the 1/8" floor plans', and most have a typed
twin on an enlargement. The composer's duplicate check is an exact endpoint key, so a wall drawn at
1/8" and the same wall at 1/4" never match — sheets stack. **Step 34: when two sheets draw the same
wall and one says what it is, the one that says wins** — a spatial stand-down (a wall within a
partition's footprint on the same storey is that partition, not a second wall). That is what will
take L3 from 232 walls to its concrete ones.

WHAT THE CHECK COVERS (4): a tag beside a wall typing it, a partition's code and a concrete code; the
nearer of two tags; a tag out of reach leaving the wall untagged and its tag still kept; no legend,
no typing. WHAT IT DOES NOT: the real sheets (the six-set run); a tag on another sheet; a tag reached
through a leader; the DXF layer the exporter writes.

## 43. Step 34, done 2026-09-10: a sheet that says what a wall is wins

Ian, from the first ETABS screenshot of a model built from an architect's PDF alone: *"looks a
little funky"*. It was walls — 1,062 of them, most steel stud. The 1/8" key plan draws every wall
and tags none; the 1/4" enlargements tag every wall assembly; the key plan places first (its file
sorts first), so its untagged walls were the survivors and the enlargements' typed twins were the
duplicates. And the composer's duplicate check is an exact endpoint key, which a wall drawn at 1/8"
and again at 1/4" never satisfies.

**One convention, three clauses**, each measured on six sets:

1. **A tag names its whole run** (intake, `WallTypeTagging`). A wall is drawn as piers where doorways
   cut it and as panels where the two-face reader found it; the drafter tags the run once. A pier
   that runs the same way as a typed neighbour, on the same line within a wall's thickness, and
   abuts it within a hand's width, takes its type.
2. **On a plan that tags its walls, a wall with no tag is not a wall** (intake). An architect's
   enlarged plan tags every wall assembly; what the two-face reader finds untagged there is millwork,
   a tub, a counter, a balcony rail. A sheet with at least `TaggingSheetMinTags` (10) codes has
   tagged its walls; on it, an untagged wall goes to `KOR_PARTITION` too, counted separately. A sheet
   with fewer has not tagged (the 1/8" key plan, 24 tags on 16 sheets), and its untagged walls are
   modelled as drawn.
3. **A storey that has a tagging sheet takes its walls from the tagging sheets; and two sheets
   drawing one wall in one place draw one wall** (composer, `DxfToEtabsService.StandDownToTaggedPartitions`).
   With every sheet in the model's frame: where a sheet carrying ≥10 `KOR_WALLTYPE` tags names the
   storey, sheets carrying none contribute no walls (their columns and plates still count); a wall
   whose axis midpoint lies inside, or a hand's width from, a partition footprint another sheet drew
   is that partition; and a wall on a later sheet whose midpoint lies within a hand's width of an
   earlier sheet's wall axis, running the same way, is that wall. The hand's width is the bridge
   tolerance's own 6 in, in the model's unit. `KOR_PARTITION` is a role of its own on the DXF side
   now (`PartitionLayerPatterns`), read into `PlanGeometrySet.Partitions` as footprints — never a
   member, never fed to the slab-edge builder.

**Measured on six sets:**

- **31170: walls 1,062 → 67**, columns 351 unchanged, floors 17. L3 232 → 43. **Rendered**: the
  footprint plate, 51 columns on grid, the elevator/stair core with its concrete walls, a handful
  of concrete walls, the partitions gone — what a stud building over a concrete podium looks like.
  1,142 walls stood down: 1,101 on sheets that tag no walls, 41 twice-drawn.
- **The five KOR sets: every plate and every column identical to step 33.** Walls: 31168 identical
  (1,209); **31138 574 → 543, 31202 374 → 360, 31065 403 → 400, 31130 193 → 191** — clause 3's
  second half. 31138 draws LEVEL 1 on six sheets (two outline plans, two mezzanine part plans, two
  reinforcing-slab sheets); a wall on the outline plan and the same wall on the reinforcing sheet is
  one wall drawn twice, the class the composer's own comment describes (KC249/KC2100 doubling 22
  walls on 31168). **Rendered before and after**: every storey visually identical — perimeter,
  core, wall pattern — while L1 goes 99 → 85. Duplicates removed; nothing visible lost.
- Core: `AWallIsWhatItsTagSaysItIsTests` +2 (the run, the tagging sheet), `ASheetThatSaysWhatAWallIsWinsTests` 4.

**Open, named:** the 1/8" key plan's walls are now references on 31170 — so on a set whose
enlargements tag but do not cover every part of a storey, the uncovered part would lose its walls;
this set's enlargements cover each floor in four quadrants. A wall drawn twice at more than ten
degrees is not caught. And the short red dashes outside the footprint on 31170 (balcony rails read
as two-face walls on the tagging sheets) are now out as "not walls" — the same reader reads them on
KOR's sets, where no tag exists to say otherwise, and *a wall stands on a floor* is the rule that
would catch them there.

WHAT THE CHECKS COVER: a tag naming its run through doorways and not across lines; an untagged wall
on a tagging sheet being no wall and on a sparse sheet modelled; a storey with a tagging sheet taking
its walls from it; an untagged wall inside another sheet's partition; the same wall on two sheets
modelled once with the first copy kept and a crossing wall untouched; a storey with no tagging sheet
and no partitions losing nothing to the first two clauses. WHAT THEY DO NOT: the real sets (the six-set
run and the two renders above); columns and plates, which no clause touches; the reach and the
ten-degree bound on a real sheet's imprecision.

## 44. Step 35, done 2026-09-10: a dimension string is not a wall — and across sheets, a ring inside the floor is not a second floor

Ian, from the second ETABS screenshot: *"some of this stuff I don't think should be here? … Or maybe
it should - I dunno!!"* Two things were on the storey sheet that a drawing does not put there: small
plates inside the footprint (17 floors on 9 storeys), and short walls above and below every storey's
plate. The rule set out was "structure stands on a floor"; what landed is narrower and true.

**What the short walls were.** Overlaid the intake's reading on A412 (LEVEL 2 PLAN (NE)) and looked:
the purple wall boxes along the top of the sheet sit on the **dimension strings** — two stacked rows
of dimensions an inch apart on paper, four feet at 1/4", paired by the two-face reader as walls 48 in
thick and a bay long ("48 in thick: 10 wall(s), length 142–475 in" on that sheet), the vertical
strings down the right edge as 24 in walls all exactly 166 in long. A tag lay within reach, so they
survived step 34. **A wall carries its thickness inside its faces, never its length**: a wall whose
outline holds a dimension word running the wall's way and stating a length more than an inch of
drafting greater than the wall is thick is a dimension string (`DimensionStrings.StandDownWalls`,
flag `ExtractedGeometry.WallIsDimensionString`, parallel to `Walls`; the exporter leaves it out, the
tagging neither types it nor makes it a partition, the console counts it on its own line, the sheet
record carries the count as `DimensionStringsReadAsWalls`). The reader that knows what a dimension
string is already existed (`DimensionStrings.Read`, brief 27); the rule is one method beside it.

**What the small plates were — and what the first cut got wrong.** Across the sheets of one storey
the composer knew neither a ring inside a floor nor a member outside every plate. The first cut of
`SettleFloorsAcrossSheets` made a ring inside another sheet's floor an OPENING and REMOVED a wall or
column standing beyond every plate. The six-set run refused both, and rule 10 applied:

- The removal: **31130 walls 191 → 58, 31202 360 → 247, 31065 400 → 304, 31168 1,209 → 1,061**, and
  31130 L0/P1's columns 97 → 10. One sentence: a member outside every plate is a stray only when the
  floor was read whole, and on KOR's sets the plate reader still reads fragments (31202 places 2
  plates on 13 storeys), so real structure "stood outside". On 31170 it had bought 3 P1 columns and
  had not touched the dashes at all (they were within reach of a plate on their own sheet). **Now it
  counts and lists — "L2: 1 of 39 wall(s) … stand beyond every plate read for the storey — strays, or
  a floor the tool did not read whole; nothing removed" — and removes nothing.** The count is the
  per-storey measure of how whole the floor was read; `docs/etabs-handoff/outside_plate.py` is the
  same measure on a finished `.e2k`.
- The opening: 31065 L19 has each tower's roof drawn on two sheets (ROOF PLAN concrete outline,
  ELEVATOR ROOF PLAN), the second reading a smaller plate inside the first — the same roof again, not
  a hole; cutting it as one would have holed both roofs. **Now a plate wholly inside a larger plate
  from another sheet leaves the floors and is listed as "a shaft, a stair, or the same floor drawn
  again in part; not a second floor, and not cut as an opening, which the drawing does not say".**
  Within one sheet the classifier's ring rule still applies as before.
- A plate the storey's largest does not contain is left as read and said — "another building, a
  ramp, a canopy, a podium edge, or a floor read twice"; on 31168 L3 it is tower B beside tower A.
  The first wording called it "kept", which this pass cannot promise: 31170's 1,008 sq ft L1 sliver
  was refused downstream by the composer's own "nothing stands under it" gate (`E2kGeometryComposer`,
  orphan plates), and the message said "kept" while the model did not carry it.

**Measured on six sets:**

- **31170**: walls 228 → 215 by storey (L2 43 → 39, L3–L6 −2 each, L7 −1); **223 dimension-string
  "walls" flagged on the plans**; floors 17 → 8 (the rings out, no openings cut); columns 351
  unchanged. **Rendered**: the two dashes above L2 gone, two of the three below every storey gone.
  One remains per storey at (6,925, −24,143): the 4'-7" bay whose text sits above its pair rather
  than between the lines — listed by the pass and by `outside_plate.py`, not caught. The 3 columns
  beyond L1's plate are the P1 plan's, listed.
- **The five KOR sets: walls and columns identical to step 34 on every storey** (`storey_counts.py`:
  0 storeys changed on each). Plates: 31065 L19 4 → 2 (the two second readings out; **rendered
  before and after**, one plate per tower, everything else identical); the other four identical.
  **0 dimension strings flagged on any KOR set** — their walls are filled, not paired from faces.
- Core: `ADimensionStringIsNotAWallTests` 4, `StructureStandsOnAFloorTests` 4; the step-34 tests
  unchanged (18 green together); full suite below.

**Open, named:** a dimension string whose only text sits above its pair (one per storey on 31170);
a real wall with its length written inside it, which this rule would flag (none seen on six sets);
the 15-m line along L6's north edge (the L5 plan) — unknown, inside the plate, left; ROOF on L8 not
L7; P1's 311 hatch cells; the tower-plate gap on KOR sets, which the "n of m beyond every plate"
count now names storey by storey.

WHAT THE CHECKS COVER: a feet-and-inches length inside a horizontal pair flagged; a thickness inside a
wall not; the word running the wall's way; a flagged wall neither typed nor a partition; a ring inside
another sheet's floor leaving the floors and not becoming an opening; members beyond every plate
counted "n of m" and left; a storey with no plate untouched and unmentioned; a plate beyond the largest
left and said without "kept". WHAT THEY DO NOT: the real sets (the six-set run and the renders above);
the exporter leaving a flagged wall out (measured on the DXF census); a split dimension token; whether
the composer's later gates keep what this pass leaves.

## 45. Step 36, done 2026-09-10: a roof plan draws the storey above the highest storey the set's numbered plans draw

Ian's first ETABS screenshot: the ROOF PLAN's plate sat on L8, and L7 — 34 columns, 17 walls —
had no floor. The rule was "roof = the storey named ROOF, else the topmost", and it held on KOR's
sets because their ladders end at the roof. The architect's set states L1–L8 on its sections
(`Top of Slab-L7` on 11 of 11, `Top of Slab-L8` on 5 of 11 — the elevator overrun, which only the
sections through the shaft see) and draws plans P1, 1, 2, 3, 4, 5, 6, ROOF. The drawing does say
which is the roof, in two places: the plan list, and the roof assembly card `R4 - PAVERS OVER L7
ROOFTOP`. A person reads the plan list and knows the roof is 7.

**The rule** (`PlanSheetNaming.StoreyAboveTheHighestPlan`, reached from `MatchStories` when the
caller passes the set's sheets): a roof plan with no level number goes to the storey NAMED roof if
the model has one; else to the eligible storey numbered one above the highest level any numbered,
non-roof plan of the set draws — for the sheet's own building where it names one; else, as before,
to the topmost. An elevator roof takes the storey above that, when there is one. A roof plan that
carries its own level number ("ROOF PLAN (L20)") matches by number and never reaches this.

**Measured on six sets:** 31170 — the 32,076 sq ft plate moves L8 → L7, the only plate that moved;
L7 walls 18 → 17 (the roof plan's copy of a wall the L6 plan also draws, now on the same storey and
caught by step 34's third clause); L8 keeps the one wall that rises to it (the overrun). **Rendered**:
L7 has its floor over its columns; L8 has a wall and nothing else. **The five KOR sets: every plate,
wall and column identical to step 35** — their plans end where their ladders end, so "one above the
highest plan" does not exist and the topmost stands. Core: `ARoofPlanDrawsTheStoreyAboveTheHighestPlanTests` 5.

**Instruments made permanent this step:** `docs/etabs-handoff/pdf_words_near.py` — every line of a
PDF's text mentioning given words, with its pages, most-repeated first: the "what does the drawing
CALL this?" question, answered before a rule is written (it found `Top of Slab-L8` on 5 of 11 and
`R4 - PAVERS OVER L7 ROOFTOP`); and `docs/etabs-handoff/render_storeys.sh` — `.e2k` → every-storey
PNG in one call. ⚠ **The "flaky" Edge screenshot had a cause**: a running Edge window takes the
headless call, opens nothing, writes nothing and exits 0; `--user-data-dir` on its own profile
fixes it, and a mixed backslash-then-slash path from `$LOCALAPPDATA` fails the same silent way. Both
are handled in the script; nobody needs to remember either.

**Open, named:** the KOR ladders themselves stop short of the roof levels the plans name — 31065's
plans say ROOF (L20) and ELEVATOR ROOF while `pdf-levels` reads L19 as the top, 31202's say ROOF and
UPPER ROOF above an L13 ladder — so on those sets the roof plan and the top floor plan share a
storey (31065 L19: two plates, 6,665 and 6,610). That is a level-reader item (the sections' words for
a roof level — `ROOF`, `T/O ROOF`, `U/S ROOF SLAB` — are not level-label words yet), and it is the
same class as step 30. "UPPER ROOF" is not an elevator-roof word.

WHAT THE CHECKS COVER: the storey above the highest numbered plan when the ladder runs higher; the
topmost when the ladder ends with the plans, with and without the set passed; a storey named roof
winning; an elevator roof one higher, and sharing when there is no higher; a tagged roof plan
counting its own building's plans. WHAT THEY DO NOT: the real sets (the six-set run and the render
above); a roof plan with its own level number; "UPPER ROOF"; a set whose highest numbered plan is
mistitled.

## 46. Step 37, done 2026-09-10: a pattern's cells abut, three and more of a size; a column stands alone

Ian: "P1 is wrong — 4 plates, no columns, no walls; the plan reads 311 columns." The first thing
was to LOOK, and the second was to build the instrument that shows it: `pdf-overlay --columns` is
a census of the column reads — size to the inch, pen, and how many of that size stand edge to edge
with a twin. On A101 (LEVEL P1 PLAN) it read: **248 of 311 are 36" × 48", black, and 245 of those
abut a twin.** Overlaid at 150 dpi they are the walls: the concrete walls are drawn with a stipple
fill (a "concrete" pattern), and Vectorworks writes that fill as its pattern's cells — closed,
filled, one cell in size, shoulder to shoulder along every wall — each the size of a column. The
reader took the cells as columns, and their boxes then "covered" the walls' own face lines, so the
two-face reader made no walls from them either.

**The rule** (`GeometryFilterService.PatternCellsAreNotColumns`, after the classification loop and
before the face-line wall reader): a pattern is many of one thing. Three or more shapes read as
columns by shape, of ONE size (within an inch), each edge to edge with the next (facing edges an
inch apart or less, overlapping by half the shorter side), are its cells; a shape of the run's
width abutting it is the run's last cell, cut short where the wall ends. Cells leave the columns,
are kept on the geometry as `PatternCells` (the overlay draws them orange; `--columns` lists them),
and their paths are fated `PatternCell` (discarded) so the ledger says what they were. Fates that
pointed at a column are re-pointed.

⚠ **The first cut was "two shapes that abut are cells" and the six-set run refused it**: 31138 lost
five columns on L1 and L2 and one on L6, 31130 one on L1M. `members_diff.py` (new: which members a
second `.e2k` lost or gained, per storey, with positions) put a finger on one, and the crop showed
**GC15 (18" × 49") drawn as two filled pieces, 18 × 41 and 11 × 18, where a bearing wall crosses it**
— one column in two pieces, which abut because they are one column. Hence "three and more of a
size": a column in pieces is two or three pieces of different sizes; a pattern is many of one. A
declared-size column is never a cell (31138's declared 1800 × 400s three in a row stay three columns).

**Measured on six sets:**

- **31170**: A101 columns 311 → 66 (245 cells), A201 287 → 42, A401 88 → 25, A403 148 → 33, A404
  102 → 12; **the plans' own cells: 245 on A101 alone**. In the model: L1 columns 68 → 67; walls
  214 → 209 net, with a handful of short walls changing storey (L5 −3, L3 −1 +1, L2 −1 +1, L4 +1 −1)
  — the cells' boxes no longer cover face pairs, so the two-face reader makes a few new walls, and
  step 34's stand-down then settles which sheet's copy stands. **Rendered before and after**: the
  same rows of columns on L2–L5, two short dashes at L5's top edge now on L3.
- **The five KOR sets: byte-identical `.e2k` to step 36** (`cmp`), once the rule asked for three of
  a size. With the two-shape rule they were not, and that is written above.
- Core: `APatternsCellsAbutAColumnStandsAloneTests` 8 (a run of three end to end, three side by
  side, the cut-short end cell, TWO alone standing — GC15 in two pieces and two identical cells —
  columns a bay apart, corners, declared sizes, the survivor's fate); `EveryPathHasExactlyOneFateTests`
  carries the `PatternCell` case and the re-pointed column fate.

**What P1 still is not.** The cells are gone; the walls they filled are still not read — A101 has
14 walls, the enlargements 3–8. The stippled walls' faces are drawn in short pieces (A101: 9,299
paths TooShort, the largest class on the page), and a face in pieces does not pair. That is the
next rule of this class and it is step 27's for walls: *a face interrupted is still one face.* And
the 38 "14 × 36" black shapes at every stall line (one per stall, 2.5 m apart) stand as columns —
concrete-filled, column-shaped, unlabelled; too dense for a column grid, not a pattern (they do not
abut). The drawing does not say what they are; they are kept and counted ("3 of 179 columns beyond
the plate" on P1).

**Instruments made permanent this step:** `pdf-overlay --columns` (the census, and the cells in
orange); `docs/etabs-handoff/members_diff.py` (lost/gained members per storey with positions,
tolerant of the one-unit re-rounding a moved offset causes); `docs/etabs-handoff/pdf_tiles.py`
(ground truth through fitz: closed rectangles by size and whether they sit under a clip — it
showed the cells are NOT single paths in the PDF, which is why the fix is in the classifier).

WHAT THE CHECKS COVER: three cells end to end and side by side leaving no column; the cut-short
end cell; two shapes alone standing (a column in pieces, and two identical cells); columns a bay
apart; corner contact; declared sizes; the survivor's fate and the cells kept on the geometry.
WHAT THEY DO NOT: the real sheets (the six-set run, the census and the crops above); a rotated
pattern; a pattern of exactly two cells; the walls the cells filled.

## 47. Step 38, done 2026-09-10: a wall is what a fill pattern fills

After step 37 the P1 plan of the architect's set had its pattern cells out of the columns and still
no walls: 14 on A101, 3–8 on the enlargements. Ground truth first (`pdf_lines.py` on a stippled
wall, then crops): the concrete walls are stippled bands whose FACES are drawn in pieces — the
west wall of the MAIN COMM. ROOM is two faces 203 mm apart, one in seven pieces and one in nine,
broken at every cell of the fill — in a light pen (w0.60) while the sheet's cut pen, taken from
its few filled walls, is w3.30. And the perimeter is a single pattern-filled band 54 m long, 10"
thick, with one end mitred against a 12" return, refused as "not a rectangle".

**Three clauses landed, two were refused.** Each measured on six sets, the five KOR sets
byte-identical to step 37 at the end (`six_set_diff.sh s37`).

1. **A face drawn in pieces through a fill pattern is one face** (`AFaceInPiecesIsOneFace`):
   two emitted lines of one pen and colour, both lying within step 37's pattern cells, on one line
   and meeting end to end within an inch — or overlapping — are one line, and their paths' fates
   point at it. ⛔ The first cut joined EVERY touching collinear pair and the six-set run refused it
   (31065 walls 400 → 388, 31202 360 → 343): a Revit export draws two walls that meet end to end
   as two touching faces, and joined, the one face pairs with neither. Touching is not the same
   line; the pattern running across both pieces is what says it is.
2. **A line inside a fill pattern's cells is a cut line whatever its pen** (`WallsFromFaceLines`):
   the pattern is the cut material, so the pen gate steps aside for lines with both ends in the
   cells. A101's interior walls came from this: 8" walls to 429" long.
3. **A wall's end may be mitred** (`IsWallShape`, replacing `IsRectangle` in the filled-wall rule):
   four points are a wall's shape when the long edges are opposite and parallel (a taper is not),
   each end runs no further along the wall than the thickest wall is thick (a mitre against any
   return; a square end at the least), and the polygon fills half its box (a bow-tie does not).
   The perimeter came from this. Audit F1's (0,0) (6000,0) (5800,300) (200,300) passes it too —
   parallel faces, chamfered ends — and its test now names the taper it meant (faces 300 → 450).
   **And so a pattern's stripes are not walls** (`PatternStripesAreNotWalls`): admitting a mitred
   end admits a hatch stripe — the accessible stalls' three grey 22" × 68" parallelograms each —
   so three or more filled walls of one thickness and length, parallel, at one pitch across their
   width, are a hatch (step 37's principle for the wall rule); 12 → 0 on A101.

4. ⛔ **A stipple beside a line is cut material — refused.** The perimeter has a dot stipple between
   a heavy face and a light one, and "a line with a stipple's dots beside it, on three rows, is a
   cut line" read it — and then 110 more walls on the LEVEL 1 key plan (195 → 305), and on KOR's
   31130 parkade turned the edges of a cross-hatched slab-reinforcing zone (`17-35M19.8 @ 12"
   EXTRA BOT.`, seen in the crop) into cut lines: 48"–114" stubs across L1, walls 191 → 264. A hatch
   marks what a drafter chooses; only the architect's convention makes it concrete. It bought 3
   walls on P1 that clause 3 reads anyway. The cost is written in `WallsFromFaceLines` where the
   clause was.

**Measured on six sets:**

- **31170**: A101 walls 14 → 49 (28 filled — the 10" and 12" perimeter at 288"–2,275", the 6"–20"
  interior — and 21 from face pairs), lines 2,478 → 1,989 (pieces joined inside the cells). In the
  model L1 walls 12 → 45: **rendered**, the parkade's perimeter on three sides, the bike rooms,
  the core, the north-west rooms. L2–L7 +3 to +8 each (mitred core walls), L8 1 → 30 (the roof
  plan's parapets and overrun walls, which rise to it). Columns 350 → 342.
- **The five KOR sets: byte-identical `.e2k` to step 37.** With the global join they were not; with
  the stipple clause they were not; both are written above.
- Core: `AWallIsWhatAFillPatternFillsTests` 3, `TheAuditsCounterexamplesTests.F1` rewritten to say
  what a taper is (and a bow-tie, a chamfer, a mitre, an over-long end); full suite below.

**What this step cost in runs, and what changed so it costs less.** Five six-set runs where two
should have done: the first cut of the join indexed one list by another and threw on 53 of 67
pages — the harness summary showed only "Sheets read", so one run was read as "the rule moved every
set" when most pages had crashed; then KorStandards went unreachable and four of six models were
not built at all, and the diff scripts compared against nothing. Both now print ⛔ lines in
`pdf_only_all.sh` (FAILED-page count; NO MODEL). And the harness runs its six jobs **in parallel**
(9 min → about 3), `pdf_only_one.sh` characterises a rule on one set before six are run, and
`six_set_diff.sh <step>` is the one-line-per-set reading that follows every run. Ian: "another
harness, another suite. This is TEDIOUS" — it was.

**Open, named:** the "14 × 36" concrete blocks at every stall line, still columns; the 59" × 217"
hatched accessible aisle, now a filled wall of wall proportions (the drawing does not say); the
L8 count wants a look (30 walls on the overrun storey — parapets rise there by the convention, but
30 is many); KOR's roof-level ladder words; the in-memory handoff.

WHAT THE CHECKS COVER: pieces joined with fates re-pointed, a gap and a pen change left, and the
parallel lists kept the same length with an annotation line first; light faces inside cells
pairing and the same faces without cells not; three stripes at a pitch leaving the walls, two
standing, three a room apart standing; a taper, a bow-tie, an over-long end refused and a
rectangle, a chamfer and a mitre read. WHAT THEY DO NOT: the real sheets (A101 and the six-set
run above); a face broken by a doorway; a Z-jog wall with two mitres of opposite sense beside two
siblings at its pitch; the stipple clause, which is gone.

## 48. Step 39, done 2026-09-10: a column stands at the centre of its outline

Found while writing the test for step 38's join: a 900 × 1200 pattern cell's box did not contain
the face at its own edge, because the cell's centre was 180 mm over. `PolygonProcessor.Centroid`
weighted a polyline's edges by length — the three drawn edges of a closed subpath and not the
fourth, because a PDF's close is a command, not a repeated corner. **Every column read by shape
had stood off centre towards its last-drawn side**: a 400 × 400 column 67 mm (2.6"), since the
first PDF read. The ring is closed now when its ends are apart and it has three or more points.

**Measured against the one yardstick that is not our own output** — the Revit route's 31168 model,
frames matched by grid name (`columns_vs_yardstick.py`, new; ported 2026-09-11 to `ModelYardstick` / `takeoff model-yardstick`, the script removed): before, the median column residual
to the nearest Revit column was **100 mm, 7% within 50 mm**; after, **18 mm, 71% within 50 mm**,
on 2,211 columns across the shared storeys. ⚠ Corrected 2026-09-11 (audit F23, F24): the "71%" was
measured with the script matching storeys by a stripped name, so 31168's A-L1 and B-L1 both matched
Revit's L1 and the residuals of one tower were taken against the other's columns; matched by full
name first, the same 2,211 columns are **median 16 mm, 92% within 50 mm**. And a claim this section
made about "tower storeys 96% within 100 mm" was read off the script's printed sample of eight
storeys, not a count of them, and is withdrawn. The script prints every storey now: of the **58
storeys both models name, 38 have 96–100% of their columns within 100 mm, 46 have 90% or more, and
9 are under 80%** — the top-of-tower storeys (A-L34–36, B-L39–40, where the PDF-only model reads
columns the Revit model does not have at those names: medians of 1.7–44 m are a storey mismatch,
not a positional one), L1 (67 mm median), L3, B-L37–38. And a second effect
the bias had hidden: with the corner columns of the tower cores sitting where they are drawn, the
cores' face pairs are no longer "under a column already read" and pair — **31168's tower storeys
33 → 43–45 walls** (Revit's 40; the open item "tower walls 33 vs 40" since step 25), L10 rendered
before and after: a U-shaped core became the closed box with its inner walls. Every set's columns
moved by their bias (the six-set diff shows 51–97 mm model shifts and every column "moved");
column counts settled a little where two reads of one column now coincide (31138 676 → 669, 31168
2,502 → 2,483). Core: `AColumnStandsAtTheCentreOfItsOutlineTests` 3.

WHAT THE CHECK COVERS: a ring's centroid at its centre with and without the repeated corner; a
two-point polyline unchanged; a column read by shape at the centre of its box. WHAT IT DOES NOT:
the yardstick numbers (the script, run by hand — the frames need GRIDS in both files, which is
step 40); the DXF side's own `PlanLoop.Centroid`, which closes its ring already.

## 49. Step 40, done 2026-09-10: the drawings' own grid is written to the model

`columns_vs_yardstick.py` could not match the frames at first: the PDF-only 31168 model had **no
GRIDS section at all** — the Revit route's has 21 named axes. Every sheet is placed on the set's
reference plan by axis name (step 25), and the branch that writes GRIDS ran only when no grid was
known at all; with the reference plan supplying the grid, `referenceGrids` was populated and the
branch skipped. A reference MODEL brings its own GRIDS; a reference PLAN did not, and the axes the
sheets were placed by are exactly the grid to write. Now written whenever the document carries no
GRID lines: 31130 44 axes, 31138 15, 31065 20, 31202 46, 31168 38, 31170 27 — the engineer opens the
model and finds the drawings' grid. Core: `TheDrawingsOwnGridIsWrittenToTheModelTests` 1.

WHAT THE CHECK COVERS: one sheet with named axes on its GRID layer and no reference model giving
GRIDS with those labels at those coordinates in the model's unit, and the warning. WHAT IT DOES
NOT: a reference model's own GRIDS (unchanged); several sheets disagreeing about an axis (the
median is taken).

## 50. Step 41, done 2026-09-10: a level may be named by a word alone

Step 36's open item: the KOR ladders stopped short of the roof levels the plans name — 31065's
plans say ROOF LEVEL (L20) and ELEVATOR ROOF while `pdf-levels` read L19 as the top; 31202's say
ROOF and UPPER ROOF above an L13 ladder — so on those sets the roof plan and the top floor plan
shared a storey and stacked their plates. Asked the drawings what they call it
(`pdf_words_near.py`): 31065 `ROOF LEVEL`, 31138 `ROOF`, 31202 `ROOF`, `HIGH ROOF`, `LOW ROOF`,
`PENTHOUSE`. "LEVEL 19" is a label and a value; these are a level's whole name, with nothing to the
right but the level line, and the ladder reader, wanting a value after a label, read none of them.

**The rule** (`ScheduleGridReader.DefaultLevelNameWords`, `LevelPhraseEndingAt` longest phrase
first, `LadderAt`): a word or phrase from the name vocabulary is a level named by itself — ROOF,
ROOF LEVEL, HIGH ROOF, LOW ROOF, UPPER/LOWER/MAIN/MECH ROOF, ELEVATOR ROOF, PENTHOUSE, PENTHOUSE
ROOF, T/O PARAPET. The longest phrase is tried first, so "ROOF LEVEL" is the level ROOF LEVEL and
not a LEVEL wanting a value. A name written on two lines — "PENTHOUSE" over "ROOF" at one x, a line
apart — is one name at the lower line. **And a set has one base** (`SetStoreys.Levels`): a
parapet detail on 31202 states HIGH ROOF 1,219 mm over LOW ROOF and nothing under LOW ROOF, which
made a second base at 0 and chained LOW ROOF to the ground; the set's base is the one the most
levels chain up from, and a ladder that reaches none of that chain is a detail's, reported as
"not chained to a base" and not placed.

**Measured on six sets:** 31065 gains the storey ROOF at 67,919 (the two roof plates that stacked
on L19 now on it; floors 7 → 9); 31138 gains ROOF at 85,905 (2,883 over L22; the roof plan's 13
walls on it); 31202 gains ROOF and PENTHOUSE (the roof plans' members rise there: columns 1,157 →
1,234, walls 354 → 368); 31130, 31168 and 31170 byte-identical to step 40. Core:
`ALevelMayBeNamedByAWordAloneTests` 3 (a roof word alone topping a ladder; ROOF LEVEL one name and
a two-line name one name at the lower line; one base, the detail's ladder reported).

**The vocabularies are wired to the bank now** (owed since step 30): `PdfIntakeOptions` carries
`LevelLabelWords`, `LevelNameWords`, `AssemblyStructuralWords`, `AssemblyPartitionWords`, each the
compiled defaults EXTENDED by the KorStandards row (`dxf.level.label-words`, `dxf.level.name-words`,
`dxf.assembly.structural-words`, `dxf.assembly.partition-words`) — a practice's phrase is added to
what is true of drawings generally, never in place of it — and `pdf-levels`, `pdf-takeoff` and
`pdf-assemblies` read through them. Migration `KOR.Drafter\db\082_LevelAndAssemblyVocabulary.sql`
seeds the four rows with the compiled words (Ian applies migrations). With no row the behaviour is
the compiled defaults; the six-set run is unchanged by the wiring.

**Open, named:** 31202's PENTHOUSE is stated twice (over L13 6,313 mm on one sheet, over HIGH ROOF
1,841 on another) and its ROOF sits 5,997 mm over L13 — two storeys' worth; the sections' two-line
"PENTHOUSE / ROOF" label did not merge on the real sheet though the synthetic one does, so the
level named PENTHOUSE may be the roof's upper word 317 mm above ROOF. The tool reports the double
statement; a person should read S3.xx. 31065's ROOF at 5,793 mm over L19 wants the same look
(ROOF LEVEL (L20) and ELEVATOR ROOF (L21) are two levels on the plans; one was read).

WHAT THE CHECKS COVER: a roof word alone as the top of a ladder at the drawn height; "ROOF LEVEL"
as one name; a two-line name read at the lower line and no phantom level; one base with a detail's
ladder reported. WHAT THEY DO NOT: the real sheets (the six-set run above); the rows (not applied);
a roof word used as a caption on a plan; 31202's two statements.

## 51. Step 42, done 2026-09-11: the adversarial audit of steps 31–41, answered

Codex read the summaries of steps 31–41 (§40–§50) against the source and the fourteen named test
files, statically, and wrote 25 findings
(`docs/codex/CODEX-PDF-INTAKE-STEPS-31-41-ADVERSARIAL-AUDIT-RESPONSE.md`): 13 High, 12 Medium,
each with the smallest input that breaks the claim. Every one was answered the same way — the
counterexample written as a test first, in `TheAuditsCounterexamplesForSteps31To41Tests` (14
facts) or in the step's own test file, then the fix, then all six sets.

**What the six-set run said.** With every fix in, four sets moved and two were byte-identical to
s41; the one-job switch (F7 reverted alone, all six byte-identical) attributed every moved member to
F7 — **the other 24 fixes change none of the six models**, which is what a fix to an edge case
should do. F7's first cut ("both ends of the later wall within reach of the earlier axis") returned
25 walls, and `model_to_page.py` (new) put the first four back on their sheets: 31138's L1 stub
walls, drawn 4'-0" long on the 55'-0 plan and 4'-8" on the 64'-1 plan — the same walls, and both
copies modelled. The rule kept is **the earlier wall must have drawn MOST of the later one** — its
midpoint within a hand's width, and the earlier axis covering more than half its length — which
refuses the audit's pier (a 1.2 m pier draws 20% of a 6 m wall) and takes the stub (86%). Under it
three walls return against s41: on 31138 one 8" wall the two L1 plans read as two overlapping
2.2 m pieces of one 3.6 m wall (each piece draws 47% of the other; both stand now, overlapping
1 m, where one stood short before) and its copy on L1; on 31170 a 140 mm end cap. Banked as
**s42**, model and console together (`six_set_bank.sh`, new — the s41 consoles that would have
said which clause took the stubs were never banked).

**The High findings, and what each became:**

| | finding | fix |
|---|---|---|
| F1 | a range sheet (LEVEL 2-3) shares one geometry list across its storeys, so a stand-down on one removes from both | the composer gives every storey of a range sheet its own copy (`PlanGeometryTransform.Copy`); calibration sees each sheet once |
| F2 | one base per set discards a second, independently founded building | a base with three or more levels chaining from it, or one numbered like a base, is kept (`SetStoreys`) |
| F3 | a finish layer named first made a concrete assembly a partition | material is the concrete or masonry layer anywhere in the card, a finish only when there is none |
| F4 | three precast piers abutting within an inch read as cells | **accepted as a limit**: three filled shapes of one size edge to edge ARE what a hatch looks like; a schedule that declares the size keeps them (§46's declared-size clause) — documented in the test's WHAT IT DOES NOT |
| F5 | three equal walls at one pitch read as stripes | **accepted as a limit**, the same way: the tests state it |
| F6 | a partition footprint crossing a concrete wall stood the wall down | the wall must run the footprint's way (parallel to its longest edge) with both ends inside or within reach |
| F7 | a short earlier wall took a much longer later wall | above |
| F8 | a sheet of only partitions was refused at the admission gate before it could say what a wall is | a sheet with no structure but partition footprints or wall-type tags is kept for what it says, placing nothing of its own |
| F9 | a bare number inside a wall stood it down as a dimension string | a bare number must agree with a grid span; a written length ("19'-8"") needs none |
| F10 | a wall flagged as a dimension string still passed its tag along a run | a flagged wall neither takes nor passes a tag |
| F11 | one roof plan landed on every storey whose name contains ROOF | one roof per sheet: an elevator sheet's is the ELEV level, otherwise the lowest named roof without ELEV or PENTHOUSE |
| F12 | "ROOF LEVEL 3" read as the level ROOF LEVEL | a name word is a name only when no level-shaped value follows it on the baseline |
| F13 | splitting a tagging sheet into views left one view with the tags and the others untagged | every view carries the sheet's whole tag list |

**The Medium findings:** F14 stripe removal drops the doorways whose pier left and remaps the rest;
F15 a joined face's eventual fate (wall face, slab edge) reaches every piece, not the first
(`WhenAJoinedFaceBecomesAWallEveryPieceOfItBecomesThatWallsFace`); F16 a run's end cell may not be
larger than the run's cells (`NoLargerThan`), so a column standing beside a run is not its end;
F17 a filled wall's faces may taper by no more than a quarter of the thickness
(`WallShapeTaperShare`); F18 every card line that is not a rating, heading or layer is a remark;
F19 a decimal millimetre thickness ("12.7 mm") is read; F20 `pdf-assemblies` reads the vocabulary
rows too. The instruments: F21 `members_diff.py` keys walls by (centroid, length) so a wall turned
about its centre is seen; F22 it aligns frames by shared grid labels and says GUESS when it must
fall back to the mode; F23 `columns_vs_yardstick.py` keys grids by system and matches storeys by
full name first (§48's figure corrected there: **92% within 50 mm**, not 71%); F24 `six_set_diff.sh`
sums the bracketed counts, not printed samples, and the yardstick prints every storey.

**F25, the tests that promised more than they asserted:** the grid test asserts every axis's
direction and coordinate and a column on its axes; `StructureStandsOnAFloor` asserts the key plan's
openings and the members' coordinates unchanged; `APatternsCellsAbut` has a real cell run beside its
declared column; `AWallIsWhatAFillPatternFills` checks the optional list is empty or parallel;
`ASheetThatSaysWhatAWallIsWins` has the within-reach case; `ASheetIsReadAtTheScaleItStates` has
the two-scales-give-null case and the same two scales agreeing giving one. Not strengthened, and
said so: the card test's layer text and order; the tag test's doorway records.

**Measured on six sets:** 31130, 31065, 31202, 31168 byte-identical to step 41; 31138 +2 walls,
31170 +1, above. Core: `TheAuditsCounterexamplesForSteps31To41Tests` 14, and the strengthened
facts named above; fast suite 1,203 of 1,203.

WHAT THE CHECKS COVER: each finding's smallest input, as Codex wrote it, refused or read as the
summary claimed. WHAT THEY DO NOT: F4 and F5 (limits, stated); a range sheet whose storeys
genuinely differ (the copy is identical by construction); F7 on two pieces of one wall from two
sheets, which now both stand — a collinear overlapping join across sheets is the next candidate
rule, and it is NOT the global collinear join step 38 refused (that joined abutting Revit walls;
this would join overlapping copies).

## 52. Step 43, done 2026-09-11: a bubble labelled twice with one name is labelled once

Found by refusing to wave a red test through. `Langara31168ParkadePlansBuildOnTheReferencesGridByName`
— the end-to-end test that reads the NEWEST dated stick file on the share, writes 31168's three
parkade sheets on the office's layers and builds them on the Revit reference's grid — went red on
2026-09-10 and was carried as "the new stick file, not code". It was code. On the 09-10 reissue the
two BLDG A & B sheets placed 0 of their axes ("their axes name nothing the model names"); on the
08-25 issue 26 of 26. `grid_names.py` (new) put the two side by side: the reissue's sheets carried
9 labels, H–R, and no numbered axis at all. `pdf_words_in_band.py` (new) showed why: **every
numbered bubble holds its label twice** — "5" at y 234.8 and "5" at y 240.4, the architect's
underlay's grid under the engineer's ("With Arch" is in the file's name) — and the bubble reader
wanted exactly one word inside the circle. The numbered grid lines are also drawn twice, once in
7.6 m pieces and once as a dash-dot pattern (`pdf_lines.py` prints the vertical runs now); that
was not the fault, the rule reader already sums the pieces through a bubble.

**The rule** (`GridBubbles.On`): the words inside a circle are its label when they are ONE
distinct name — written once, or written again over itself by an underlay. Two different words
inside a circle are still a mark, not a bubble. Core: `ABubbleLabelledTwiceWithOneNameIsLabelledOnce`
in `SheetFurnitureIsNotStructureTests`. The end-to-end test is green on the reissue: 3 of 3 sheets
on the grid.

**Measured on six sets:** all six byte-identical to s42 (none of the harness PDFs carries an
underlay's labels; the harness's 31168 is the 09-04 file, not the reissue). Not banked as a step of
its own — s42 stands.

**What the red test is, for the record.** Its name is the claim it makes, as every test here is
named; it is job-specific because it is an end-to-end test on a real set — the reference route
needs a real Revit model and a real stick file, and 31168 is the one we have both for — not a rule
tuned to a job. It reads the newest issue on the share, so it is also a monitor: when the office
reissues the set it measures the new drawing, and "a reissue builds with no code change" is
exactly the bar §0 sets. A day of "known red" was a day of not looking.

WHAT THE CHECK COVERS: a doubled label read once, two different words refused. WHAT IT DOES NOT:
an underlay whose bubble is offset from the engineer's (two circles, two bubbles — the axis reader
would merge them within 0.5 pt of one rule and otherwise name two axes); the harness on the
reissue (open: move the harness's 31168 to the 09-10 issue, which re-banks every 31168 baseline
and is Ian's call).
