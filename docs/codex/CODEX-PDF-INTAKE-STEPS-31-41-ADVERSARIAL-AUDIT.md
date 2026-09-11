# Codex — deep adversarial audit of the PDF-only route, intake steps 31–41

**Scope: this repository only. Static reading of the commits named below. No build, no test run, no
drawing files, no database, no network, no other repository.** Everything you need is in the diff
and the two documents named under "Read first". If a claim needs a drawing or a database to check,
say so and move on; do not try to reach either.

## What is being audited

Between `50823467` (step 31) and HEAD on `develop` (step 41, 2026-09-10) the PDF-only route — a
structural or architectural drawing set as a PDF becomes an ETABS `.e2k` with no Revit, no
reference model — took eleven rules, one per step, each claimed universal and each measured on six
drawing sets (five of KOR's, one from another office). The claims for each step are written in
`docs/PdfIntake.md` §40–§50 (one section per step: the rule, what was measured, WHAT THE CHECKS
COVER / WHAT THEY DO NOT, and what was tried and refused with its cost). The tests are named in
each section.

Your job is to find where the code does not do what those sections say, where a test's name is
wider than what it checks, where a rule that is called universal is fitted to one set, and where
a "refused" clause left residue.

## Read first, in this order

1. `CLAUDE.md` — the twelve working rules; rule 11 (a check must state what it covers and what it
   does not) and rule 12 (a truncated command cannot support a claim about the whole) are the
   ones this audit turns on.
2. `docs/PdfIntake.md` §0 (START HERE), then §40 through §50.
3. The diff `50823467^..HEAD` restricted to:
   - `Kor.Operations.EngineeringTools.Core/Intake/` (DrawingIntake, AssemblySchedule, WallTypeTagging,
     DimensionStrings, SheetViews, SetStoreys, StoreyLadder, SheetRecord, PathFate)
   - `Kor.Operations.EngineeringTools.Core/PdfToSafe/` (GeometryFilterService, PdfGeometryModels,
     DxfExporter, PolygonProcessor, PdfIntakeOptions)
   - `Kor.Operations.EngineeringTools.Core/Dxf/` (DxfToEtabsService, PlanSheetNaming,
     StructuralPlanClassifier, DxfModels, PlanGeometryTransform)
   - `Kor.Operations.EngineeringTools.Core/ScheduleGridReader.cs`, `PlanAgreesWithItsSchedule.cs`
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/` and the root test files the sections name
   - `docs/etabs-handoff/*.py`, `*.sh` (the instruments)
   Do not read `Kor.Operations.EngineeringTools.TakeoffCli/Program.cs` beyond the verbs the
   sections name (`pdf-overlay --columns/--walls/--agreement`, `pdf-assemblies`, `pdf-levels`); it
   is 5,000 lines and a known refactor debt, not the subject.

## The questions, in priority order

**A. Universality.** For each rule in §40–§50, is there an input a drawing could plausibly contain
on which the rule does the wrong thing, that the section's WHAT IT DOES NOT list does not already
name? Give the smallest input. The ones the author is least sure of:
- §46 step 37: three or more column-shaped filled shapes of one size, edge to edge, are a fill
  pattern's cells. A row of three identical precast panels? Three wall piers of one size in a row?
- §47 step 38: `IsWallShape` admits a four-point filled shape with parallel long edges and end
  edges running no further along than the thickest wall — a mitre. Find a non-wall that passes.
  `PatternStripesAreNotWalls` then removes three or more filled walls of one thickness and length
  at one pitch across their width. Find a real trio of walls it would remove.
- §44 step 35: `DimensionStrings.StandDownWalls` flags a wall whose outline holds a dimension word
  running its way, stating more than its thickness plus an inch. Find a real wall this flags.
- §45 step 36: a roof plan goes to the storey one above the highest numbered plan of its building.
  Find a set where that is the wrong storey.
- §50 step 41: a level named by a word alone; the longest phrase first; one base per set. Find a
  ladder where "longest phrase first" changes a numbered level's reading.

**B. The refused clauses.** §44, §46, §47 each record a first cut the six-set run refused and its
cost. Check the code carries NO residue of the refused behaviour (the stipple clause, the global
line join, the two-shape cell rule, the remove-members-outside-the-plate clause) and that the
rejection notes are where the code was, not only in the document.

**C. Index and state integrity.** Steps 35, 37 and 38 REMOVE items from lists that other lists run
parallel to (`ExtractedGeometry.Walls`/`WallColors`/`WallIsAnnotation`/`WallTypeCodes`/
`WallIsPartition`/`WallIsDimensionString`; `Columns`/`ColumnColors`/`ColumnIsAnnotation`/
`ColumnSizes`; `Lines`/`LineColors`/`LineWidths`/`LineIsAnnotation`/`LineSectionHints`) and that
fates (`PathFate.ObjectIndex`), `Doorways.FirstPier`, `WallFaceLines` and `SlabEdgeLines` index.
One such bug threw on 53 of 67 pages before it was caught (§47). Find the next one. Pay attention
to `SheetViews.Split`, which copies these lists per view, and to `DxfExporter`, which skips flagged
walls: is every parallel list the same length at every exit?

**D. The checks versus their names.** For every test file the sections name, compare the class
summary's WHAT THIS COVERS against the assertions. Name any assertion that is weaker than the
sentence claiming it, and any claimed coverage with no assertion at all.

**E. The instruments.** `docs/etabs-handoff/members_diff.py` takes a modal displacement out before
matching; `columns_vs_yardstick.py` matches frames by grid label; `six_set_diff.sh` calls both.
Can either report "identical" when the models differ, or "moved" when they do not? State the
input.

**F. Rule 12.** Find any number in §40–§50 that is stated as a population figure but was produced
by a truncated command or a sample. The harness output lines and the census commands are quoted
in the sections.

## What is known and not to be re-found

- `Program.cs` at 5,000+ lines: known, a refactor is owed, not a finding.
- The scratch-DXF detour between intake and composer: known, the in-memory handoff is owed with a
  byte-identical gate; not a finding.
- The failing slow test `Langara31168ParkadePlansBuildOnTheReferencesGridByName`: a new stick
  file was issued for that job on 2026-09-10; the test reads the file by name; not code.
- 31202's PENTHOUSE stated twice and 31065's ROOF 5,793 mm over L19 (§50): known, open.
- The 38 "14 × 36" blocks at every stall line on 31170's P1 plan, kept as columns (§46): known.
- `dxf.level.*` and `dxf.assembly.*` rows: migration 082 written, not yet applied; the code runs
  on compiled defaults until it is.

## Output

`docs/codex/CODEX-PDF-INTAKE-STEPS-31-41-ADVERSARIAL-AUDIT-RESPONSE.md`. One finding per heading,
most severe first, each with: the sentence in `docs/PdfIntake.md` it contradicts (section and the
quoted words), the file and line, the smallest input that shows it, and what the code does with
that input. Findings are source deductions; say so. Do not propose fixes longer than a sentence;
do not write code. Cap: 25 findings. If you run out of real findings before 25, stop.
