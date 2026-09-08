# Seed — the drawing pipeline, intake to deliverable

Paste this into a fresh session. It is written for someone with no context, and it is deliberately
specific about what has been MEASURED versus what is assumed, because most of the wasted effort on
this pipeline has come from re-deriving things that were already measured, or trusting things that
were never checked.

Last brought up to date 2026-09-07, after the border-bounded schedule reader landed. The earlier
"READ THIS FIRST — there is uncommitted work" section is gone because the work is committed
(c707ec5b, 5199f091, and the commit that carries this edit); its regression story is kept below
under "What went wrong before", because the lesson is the point.

---

## What the pipeline is

A structural drawing arrives as a PDF, CAD, or a Revit model. It has to become an analysis model
(ETABS `.e2k`, SAFE `.f2k`), a quantity takeoff, and a set of deliverables an engineer will open.

    drawing PDF ──> geometry + schedules ──> DXF ──> dxf-to-etabs ──> .e2k ──> publish gate ──> the engineer
    Revit ─────────────────────────────────> DXF ──┘

Both halves are in `Kor.Operations.EngineeringTools.Core`, driven by one CLI, `takeoff`
(`Kor.Operations.EngineeringTools.TakeoffCli`, 49 verbs; `takeoff --help` lists them and a test
enforces that the list is complete).

## Start here

- `CLAUDE.md` in the repo root. Twelve rules, each one written after it was broken at cost. They
  are gates, not advice. Rules 1 (search first), 5 (verify the artefact), 9 (data local, one
  harness, LOOK at the output) and 12 (a truncated command cannot support a claim about the whole)
  are the ones this pipeline keeps needing.
- **`FiveStickFilesTests` (Core.Tests, `Speed=Slow`; the C# port of the retired
  `tools/Measure-StickFileSchedules.ps1`, 2026-09-08) is the one command that measures every intake
  deliverable** on the five local stick files: footing totals and mark sets, column mark sets and
  the route each was read by, wall marks, and the plan-against-schedule self-check per page. Run
  it before and after any reader change. It is green at 50-odd checks; it states what it covers
  and what it does not.
- `takeoff sched-border <pdf> <page>` prints what the schedule reader SEES: every heading, the
  border found under it or why not, its column rules, and every row band with its mark cell and
  cells. Look at a table read this way before believing a count from it.
- `docs/etabs-handoff/FINDINGS-2026-09-01.md` — the measured issue list for the ETABS side.
- `docs/codex/CODEX-INTAKE-CONVERGENCE-1..12-*.md` — the intake series. 1–10 landed; 11 is an
  adversarial audit and the most useful single read for the intake's history; 12 is the design of
  the border-bounded reader and landed 2026-09-07 through a Claude session rather than Codex.

## Test data is local and you should keep it that way

Five KOR jobs, whose stick files exercise five different drafting conventions, mirrored at
`%LOCALAPPDATA%\Temp\kor-drawings\stickfiles\<job>.pdf` (frozen 2026-09-04; the share copies are
at `\\Kor-fs01\Projects\Projects\03 Residential\<job>\05 Stickfile\*.pdf` and may be newer):

    31130-01 (Uplands Lot 12)      imperial, two towers, PARKADE COLUMN SCHEDULE, no .e2k at all
    31138-01 (2170 W 1st)          imperial, 13 column marks incl. PL1 and PL2 (28 1/2" x 36")
    31168-01 (YMCA Langara)        imperial, SUFFIXED marks (C02-A, PC03-A), GC11-C wrapped over
                                   two lines, C03-B sized "<varies>", a BLANK foundation schedule
    31065-01 (5350 Heather)        METRIC (states 1:100), wrapped schedule rows, three column
                                   tables on one sheet
    31202-01 (Hotel Circle SD)     US units (ksi), column marks are circled NUMERALS 1..8, titles
                                   underlined inside the table, foundation is a RAFT SLAB

## What is TRUE, measured, do not re-derive

- **Extraction is not the weak link.** `fitz.get_drawings()` returns 3,696 paths against PdfPig's
  3,683 on the same page — within ~1% on 3 of 4 pages sampled. Extracted text matches the RENDERED
  PIXELS character for character, including the drawings' own typos. ⛔ Do not re-platform onto
  PyMuPDF, pdfplumber or pdfium. It has been proposed and measured down twice.
- **The failures are all one class: a convention compiled into a binary.** A metric-only size regex
  read 1 of 5 jobs. A 2.5 column-aspect limit discarded 54 shapes at exactly 14x36 on one sheet
  because 31168 draws `PC01` at 14"x36". A 260-point heading scope fit one sheet size and returned
  nothing for a larger one. A mark regex could not match `C02-A`. A ±18%-of-the-page band around a
  heading read a neighbouring table's cell as a mark.
- **A regex parsing a NOTATION is fine** (`PrintedLength` reading `4' - 0"`, `28 1/2"`, `7.0 ksi`
  has been right on every job). **A regex encoding a CONVENTION is the recurring fault.**
- **A table states its own extent.** Every FOUNDATION and COLUMN table on all five jobs is ruled
  (12 of 12, 55 of 55 real ones, 8 of 31202's drawn cell by cell), so the rules ARE the extent.
  Rows are the cells between the rules crossing the mark column; the mark is the first cell,
  read literally; a band no column rule crosses is a boxed note. See `ScheduleTableBorder`.
- **The last word before SCHEDULE is what is scheduled.** A SHEAR WALL ZONE SCHEDULE schedules
  zones, a COLUMN STIRRUP SCHEDULE stirrups. "Contains the word" read both wrongly.
- **A fill the colour of the paper, with no stroke, is invisible ink.** Measured on the five jobs'
  schedule pages: every shape matching a declared column size is grey-filled and unstroked; the
  white-filled unstroked shapes match none — 192 of 31168 p11's 266 "columns" were 36" and 46"
  white squares around each real one. Excluding them took over-detection from 3.6x to 0.9x with
  coverage unchanged on every page.
- **Sheet furniture is not structure.** Whatever sits inside a schedule's border or the title
  block is a table cell, a tie sketch, a rule or a logo box (`SheetFurniture`). Slabs on 31130
  p11 went 65 → 28, lines 2,283 → 1,948; no coverage number moved.
- **The scale is printed on the sheet** and 1/8" = 1'-0" is 1:96, not 1:100. `takeoff scale-scan`
  reads it where it is machine-readable (3 of 3 pages on 31065; 0 of 3 on 31168).
- **31168 reads 0 footings because its FOUNDATION SCHEDULE is a blank placeholder** on the
  2026-04-21 stick file — ruled, titled, five rows, no sizes. The verb says so now. 31202 has a
  raft slab, and the verb names the RAFT SLAB REINFORCING SCHEDULE instead of saying nothing.

## The self-check, which is the most valuable thing here

`PlanAgreesWithItsSchedule`. A sheet states its columns TWICE — as geometry and as a schedule —
read by entirely separate code, so agreement between them is evidence **needing no reference
model**. Every other gate in this repo compares against a reference model, an engineer's ruling,
or a previous run, so none can say anything about the first sheet of a new job.

    takeoff pdf-takeoff <pdf> <out.dxf> --pages 11-13 --scale 96
    ...  cover 43/74 labelled, emitted 70 (0.9x); unplaced C02-A,...

- **Coverage** = matched-to-their-own-mark / marks the drawing labels. Did we find what is there?
- **Precision** = emitted / labelled. Did we invent things? 0.9x–1.2x today on the four ruled
  jobs (it was 3.6x on 31168 until the invisible-ink rule).

It has already earned itself: it caught a **wrong scale** (31065 at 96 vs its stated 100), the
aspect defect, and every one of the over-detection classes above.

⚠ It cannot see a column the reader MISSED, because it only scores what was found. ⚠ On a job
whose marks are bare numerals (31202) the denominator also counts grid bubbles — 105 "labels"
for 8 marks on p17 — so its coverage floor is a floor on the numerator only.

## What went wrong before, kept so it is not repeated

Commit `8300439d` introduced a structural mark-column route and reported "31130 7 · 31168 6 ·
31138 10 · 31065 7" — **numbers measured one commit earlier and carried over.** What it produced
was 7 / 12 / 7 / 2, and 31065's footing total fell from 1,174 cy to 855, and 31130's read 48 cy
under marks 15M and 20M out of the shear wall schedule beside it. Two jobs regressed silently
behind a green-looking commit message. The harness above exists because of it. **A commit's numbers
come from that commit's build, and one command measures every deliverable.**

The table-extent fix was the FIFTH attempt. The four before it — a 260pt scope, the topmost
anchor, the densest anchor, a border grouped by shared span — each fitted some sheets and lost
others. The fifth worked because the premise was measured first (260 of 311 titles bordered),
the algorithm was settled on all five jobs' vector data before a line of C#, and three faults were
found only by LOOKING at what the reader saw: a merge that swallowed a separator, a rounded
y-bucket that split `16" x 48"` across two lines, and a rule drawn 43pt short of its border.

## What KorStandards says, and what it may say

The schedule readers read `dxf.schedule.<kind>.*` through `analysis.vw_RuleSetting`. Migration
`080_ScheduleReaderVocabulary.sql` (in `KOR.Drafter\db`, written 2026-09-07, **not yet applied**)
banks only what is TRUE OF DRAWINGS: the head-noun vocabulary per kind, the DEEP/DP a footing row
states, and that a column or footing states a size pair while a wall states one thickness. Its
header says why the rest is deliberately NOT seeded: mark patterns, the band fallback's point
geometry, plausibility bounds, and the `dxf.pdf.*` classifier thresholds are limits fitted to the
sheets we have, and a limit in a knowledge bank is worse than a default in code. The reach from a
title to its table is four TITLE HEIGHTS in code, not a key.

    dxf.max-column-aspect     3.0 ratio    replay-verified, ETABS/e2k — shared with the PDF side
    dxf.min-wall-length        48 in       ⚠ engineer-confirmed RULING (Andrea's) — do not touch
    dxf.column-layer-patterns  _COL;-COL;S-COL      (THREE patterns, not one)

⛔ **The DXF side's column SIZE bounds are not portable to PdfToSafe.** Measured 2026-09-02:
coverage identical, over-detection 2.6x → 4.3x, slabs collapsed. On the DXF side the LAYER says
what a shape is; PdfToSafe has no layers, so the size window IS the discriminator. A test pins this.

## Where the live jobs are — and how the gates find them

The Slow tests gate on two real jobs (31168, 31138) and benchmark a third (31065). They carried
the share's UNC root and the folders' full names as constants in nine files, and when the
generated 31168 artefacts were moved one folder down into `01 ETABS Models\TEST` on 2026-09-07,
eight of the nine went green by skipping. Now: `PublishDiscovery.ProjectsRoot` is the one root
(`KOR_PROJECTS_ROOT` in the environment, else the share); a job is found by NUMBER; a drawing set
or a reference by NAME, as deep as the publisher looks (`LiveProjects` in the test project). Two
outcomes only: the share is unreachable and the test SAYS it skipped, or the name is not found
and the test FAILS with where it looked. `takeoff publish` refuses to guess between the three
DXF sets 31168 now holds; name one with `--dxf <name>`.

## Closed 2026-09-08, each by a universal statement measured on all five jobs first

1. **Circled marks vs grid bubbles (31202).** A grid bubble is a labelled circle with a grid axis
   through it; a circled mark has none (`GridBubbles`). Measured: 19 of 19 on 31130, 15 of 15 on
   31138 have an axis; on 31202 17 of 115 do and the other 98 are the marks. The self-check's
   labels exclude bubbles, and 31202 p17 reads 67 of 101 with nothing unplaced (was 23 of 105).
2. **Flat shear-wall rows** are banked in the harness by mark, thickness and strength, read off
   the crops (31130 SWA 12" 35 MPa … SWD 16" 55 MPa; 31065 SWA 200 35 MPa, SWC 600 45 MPa).
3. **The column size window.** Re-measured with the invisible-ink and furniture rules in place:
   the DXF side's 152–3,353 mm window changes coverage on none of 12 pages and costs 10–28 slabs
   on each, so it is not adopted. What actually lost PC7 (18" x 60") and PC8 (18" x 96") was the
   3.0 aspect limit — on a sheet whose own schedule declares them. So: **a filled shape of a size
   the sheet's column schedule declares is a column**, and the window and aspect are only the
   fallback for a sheet with no schedule. On 31138 those two marks are not drawn on p9–p11 at all.
4. **Where a publish lands** is decided: beside the reference it was built against. That is
   what `TEST` now holds for 31168, and it is what the move to `TEST` did by hand.
5. **Grid lines, underlines, notes and legends.** A grid line is the line through a bubble; an
   underline is a rule under a line of text matching its extent; a notes box, table, legend or
   sheet of details is the ruled box under (or around) a title carrying NOTES, TABLE, LEGEND,
   ORDER, DETAILS, TRANSITIONS, ZONES or HEADERS — a vocabulary from the population of 705 titled
   boxes on 294 pages, chosen to leave out "… PLAN" and "OUTLINE", the plan's own viewport; and
   furniture is never half the sheet. Lines on 31130 p11 went 1,937 → 1,650; rendered and looked at.
6. **The two published-model comparisons.** The C2 finding said which members the cut re-homed
   "is not recoverable from the finished files". It is: a re-homed member stands at the same plan
   position in both files, on the twin storey within `dxf.storeys-at-one-level-gap`. The
   comparison now folds those back in by position, and prices them on the cut's storey height
   (C-LEVEL 3 is 215.5 in tall in the cut, LEVEL 3 was 210 in the site — the 2.6% that was left).
   The last disagreement was one wall, KW63 at (1674,2969), on LEVEL P1 in the cut and LEVEL P2
   in the site: the tower cut clipped the joined BLDG C + WEST plan at its match line, the
   whole-floor stand-down rule then measured coverage on the CLIPPED counts, the untagged LEVEL P1
   sheet stayed and placed first, and the joined plan's 97 members were refused as "a place
   another sheet had already filled". Coverage is now measured on what the sheets DRAW, before the
   seam takes anything off — the same drawing set chooses the same sheets for the site and for a
   cut of it. Both comparisons pass on a pair staged from this build (2 of 2), the site model is
   byte-identical before and after, and the cut's "already filled" count fell from 454 to 66.

## Landed 2026-09-08

Both 31168 models were published from this build into `TEST` beside the reference:

      takeoff publish 31168 --dxf-folder _DXF-from-Revit-2026-08-26 --reference 31168-reference.e2k --variant TOWERS --land
      takeoff publish 31168 --dxf-folder _DXF-from-Revit-2026-08-26 --reference 31168-reference.e2k --tower C --skip-dossier --land

The YMCA model is 14 storeys, 251 walls, 578 columns, 15 plates (was 267/645: the parkade
storeys no longer carry the towers' side of the seam). The site model is 64 storeys, 1,424 walls,
2,365 columns, 88 plates. Against the share, the build gate and both published-pair comparisons
pass, 3 of 3. The tower C landing skipped the explainer PDFs because the dossier gate refuses a
PDF older than the generator source; the dossier and one-pager HTML now carry 251/578/21 headers
and are not re-rendered.

## What still needs a person

Nothing. Migration 080 was applied 2026-09-08: 7 of 7 `dxf.schedule.*` rows are live in
`analysis.FormatConvention` under `CreatedBy = 'migration 080'`, read back through the readers'
own login, and the harness passes 54 of 54 with them live.

## The export and deliver side — much less examined, look here with fresh eyes

This half has had far more scrutiny on correctness and far less on whether it is the right shape.

- `takeoff publish <job>` discovers, builds, verifies, gates and lands. `ShippedModelInvariants`
  holds the publish-BLOCKING checks; `docs/etabs-handoff/README.md` and `FINDINGS-2026-09-01.md`
  describe what ships.
- Three models ship today: `31168-FROM-DRAWINGS` (14 storeys), `31168-TOWERS` (64), and
  `31138-FROM-DRAWINGS` (29).
- ⚠ **Two tests are red by design** — `TheTwoPublished31168ModelsAgreeOnEveryStoreyTheyShare` and
  `...PriceBuildingCTheSame`, documented as C2-OPEN: the comparison cannot see the cut re-homing
  members. Do not "fix" them without reading that finding.

**Questions worth fresh eyes on the deliver side:**

- **Is DXF the right interchange at all?** It carries lines and layer names — no thickness, material,
  section or STOREY — so everything downstream re-infers meaning from geometry, and that inference
  is where four days went on "which storey does this member belong to". Meanwhile `EtabsE2kExporter`
  already writes a full multi-storey `.e2k` directly, and `SafeF2kExporter` writes SAFE's native
  format. **The better format is not yet the better route, because every gate is written against
  the DXF path.** That tension is unresolved.
- The schedules now readable (mark, size, strength, reinforcing, ties — on every one of the five
  jobs) are exactly the semantics a DXF drops. Nothing yet carries them into the `.e2k`, the
  S-Concrete `.SCO` loop, or a rebar takeoff.
- Is the publish gate checking the things an engineer actually gets burned by? It was built
  incident-by-incident.

## Working rules that will save you

- **Verify against the five real stick files, not just unit tests.** Three rounds of this work
  passed their unit tests and were wrong on real drawings; the harness is the fourth round's answer.
- **Render it and LOOK.** `docs/etabs-handoff/plan_sheet.py` draws every storey of an `.e2k` on one
  sheet; `renderpage.py` rasterises a PDF page; `takeoff sched-border` prints a table read.
- **State findings as X of Y.** A `head`/`tail`-truncated command cannot support a claim about a
  population, and a hook stamps truncated output to remind you.
- **Full suite before a publish** (~5 min); `--filter` while iterating (seconds). A test that passes
  alone and fails in the suite is SHARED STATE, not luck — look for a mutable static first.
- **A gate that passes by not running is the fault it exists to catch.** Skip only when the share
  is unreachable, and say so.
- Ian runs Codex. Briefs go in `docs/codex/CODEX-<TOPIC>.md`, handed over, not invoked.
