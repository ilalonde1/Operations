# Codex — deep adversarial audit of the PDF-only route, intake steps 44–56

**Scope: this repository only. Static reading of the commits named below. No build, no test run, no
drawing files, no database, no network, no other repository.** Everything you need is in the diff
and the documents named under "Read first". If a claim needs a drawing, a model or a database to
check, say so and move on; do not try to reach any of them.

## What is being audited

Between `3da6f85c` (steps 42–43, the answer to the previous audit, 2026-09-11) and HEAD on
`develop` (`44a5bd96`, step 54, 2026-09-13) the PDF-only route — a structural or architectural
drawing set as a PDF becomes an ETABS `.e2k` with no Revit, no reference model — took thirteen
steps and the completion plan's first five packages. Each step is one rule claimed universal and
measured on six drawing sets (five of KOR's, one from another office) and on the whole corpus of
293 sets. The claims are written in `docs/PdfIntake.md` §53–§64 (one section per step: the rule,
what was measured, WHAT THIS COVERS / WHAT IT DOES NOT, and what was tried and refused with its
cost). The tests are named in each section. The plan is
`docs/architecture/Kor.Operations.EngineeringTools.PdfIntake.plan.md` (§8 items 3–3g are the
record of these steps).

Your job is to find where the code does not do what those sections say, where a test's name is
wider than what it checks, where a rule that is called universal is fitted to one set, where a
"refused" clause left residue, and — this audit's particular subject — where **the model still
depends on something that is not the building**: where a view sits on its page, the order the
entities came in, the frame a sheet was drawn in, a rounding boundary.

## Read first, in this order

1. `CLAUDE.md` — the twelve working rules; rule 10 (characterise, then fix once), rule 11 (the
   check states what it covers and does not) and rule 12 (a truncated command cannot support a
   claim about the whole) are the ones this audit turns on.
2. `docs/PdfIntake.md` §0 (START HERE), then §53 through §64.
3. `docs/architecture/Kor.Operations.EngineeringTools.PdfIntake.plan.md` §8 (items 3 to 3f) and
   §1b (the corpus runs 4–7 table).
4. The diff `3da6f85c..44a5bd96` restricted to:
   - `Kor.Operations.EngineeringTools.Core/Intake/` (DrawingIntake, PdfOnlyBuild, CorpusAnalyzer,
     SetStoreys, StoreysFromPlans, SheetViews, UniqueViewNames, PathFate, TendonAnchors,
     GridBubbles, SheetFurniture, AssemblySchedule)
   - `Kor.Operations.EngineeringTools.Core/PdfToSafe/` (GeometryFilterService, DxfExporter,
     PdfIntakeOptions, PdfGeometryModels)
   - `Kor.Operations.EngineeringTools.Core/Dxf/` (DxfToEtabsService, E2kGeometryComposer,
     E2kDocument, StructuralPlanClassifier, WallOutlineDecomposer, WallNetwork, PlanLoopBuilder,
     DashedLineJoiner, LoopGeometry, GridAlignment, ModelYardstick, ModelDiff, DxfSheet,
     DxfPlanReader, DxfModels, PlanSheetNaming, ShippedModelInvariants)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/` and the root test files the sections name
   - `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/` — only `GridNamesVerb`, `DxfInspectVerb`,
     `PdfAtVerb`, `ModelYardstickVerb`, `ModelDiffVerb`, `CorpusAnalyzeVerb`, `CorpusQueryVerb`
   Do not read the rest of the CLI, the App projects, or anything under `docs/etabs-handoff/`
   beyond the CSV names the plan's table cites.

## The questions, in priority order

**A. What the model may still depend on that is not the building.** §63 states the class in one
sentence — *a place is decided by distance, never by a cell; a tie is a tie; a threshold equal to
a drafted dimension is met in every frame* — and lists nine sites fixed. The differential
(`TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests`) is green on six sets under ONE
vector, (5,000.37, 3,000.61) mm, in the page frame (§64). Find what it cannot see:
- Any remaining identity, grouping or ordering keyed on a rounded coordinate or a cell of a grid
  anchored at the origin, in the files above. `grep` for `Math.Round(` and `Math.Floor(` applied
  to a coordinate divided by a tolerance, `Dictionary<(long, long` and `HashSet<(long, long`,
  `GroupBy` on a double, `OrderBy` on an area, a length or a distance without rounding. §63 names
  what was left on purpose (`seenEdges` at 0.1 mm, `SameWall` at 1 mm, the PDF reader's own
  page-frame bins in `GeometryFilterService` and `SheetFurniture`). Anything else is a finding.
- Any comparison of a measured length, thickness, separation, overlap, distance or area against a
  threshold that a drafter could draw exactly (whole inches, whole feet, a wall thickness) that
  is not made through `LoopGeometry.Within` / `Beyond` or an equivalent tolerance. State the
  drafted dimension that would sit on it.
- Any "best of" selection (`< best`, `> best`, `MinBy`, `MaxBy`, first-wins loops) over
  candidates that can be equal by construction (three identical loops; a rectangle's two
  orientations; symmetric drafting) with no tie rule.
- Order dependence: any result that changes if the entities of a sheet arrive in a different
  order (a `HashSet` iterated, a `Dictionary` enumerated, a greedy `used[]` pairing). The DXF's
  entity order is the PDF's path order, which a re-export can change.
- The `PlacedMembers` registry (E2kGeometryComposer): one member per place per storey, by
  distance within an inch, either way round, only placed members held. Find an input where its
  answer differs from the old inch-cell key in a way that loses a real member, or keeps a
  duplicate; and whether a chain of members each within an inch of the last can walk.
- `PointAt` and `PlanLoopBuilder.NodeOf`: the nearest within tolerance, a tie to the earlier. Is
  the result still order-dependent when three points lie within one tolerance of each other?

**B. Universality.** For each rule in §53–§64, is there an input a drawing could plausibly contain
on which the rule does the wrong thing, that the section's WHAT IT DOES NOT list does not already
name? Give the smallest input. The ones the author is least sure of:
- §62 step 55: a sheet that names no axis is set where at least four of its members and at least
  half share one displacement with the members already placed. Two towers of one set with the
  same core: can a top plan of tower A register on tower B? (31168's L35 came within one member
  of it before the axis-end fix.) What if the placed set is empty or all on one storey?
- §63: `DxfSegment.OfClosedOutline` takes an edge of a closed polyline out of the dash joiner.
  A closed polyline that is NOT an outline (a dashed-line pattern exported as a closed shape; a
  hatch boundary) — what happens to it now? And a closed outline whose edge is genuinely a
  dashed line drawn in pieces?
- §63: `WallOutlineDecomposer` hands unused edges of an open chain to the pooled pass. Can a
  leftover edge pair with a face of the SAME wall it was part of and make a second, thinner wall?
- §63: the decomposer's probe asks a hair to each side of the midline and either side inside is
  concrete. A corridor with a line drawn down its middle on the wall layer: does that now read as
  one wall across the corridor?
- §61 step 53: the grid pen read at the bubbles (`GridBubbles.PenAtTheBubbles`), a stroke on an
  axis heavier than twice it is not the grid. A set whose grid is drawn heavier than its tendons;
  a set with two grid pens.
- §60/§58 step 50: `dxf.structural-plan-words` — a sheet whose title says what it is is read for
  its axes alone. A title in another office's words. A sheet that says "CONCRETE OUTLINE" but is
  a detail sheet.
- §54 steps 45–46: a set's storeys are what its plans name. Two sheets naming one storey with
  different words; a plan named for a range that skips a level.
- §57 step 49: a centroid lies inside its own box; a target's quadrants are not columns; a member
  stands on the building. Find a real column that `ATargetsQuadrantsAreNotColumns` removes.

**C. The refused clauses.** §57, §61, §62 and §63 each record a first cut the six-set run refused
and its cost (the centroid fallback, the strokes let into `Lines`, the overshoot clause on a real
column, corners as registration points, "no touching merges" in the joiner). Check the code
carries NO residue of the refused behaviour and that the rejection notes are where the code was.

**D. The checks versus their names.** For every test file §53–§64 name, compare the class
summary's WHAT THIS COVERS against the assertions. Name any assertion weaker than the sentence
claiming it, and any claimed coverage with no assertion. In particular:
- `TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests` compares through `ModelDiff`:
  placements to two units, plates by area. What is a same-class fault it would pass? (§63 names
  one; find another.)
- `ASheetThatNamesNoAxisStandsWhereItsColumnsStandTests` and `APlacedMemberOnThirtyStoreysIsOnePlace`.
- `TheGridIsDrawnWithOnePenTests`, `ATendonsAnchorIsNotAColumnTests` (§61).
- `SixSetsBuildAsBankedTests` and `EveryPathHasExactlyOneFateTests` after the new fates
  (`StrokeOnGrid`, `SymbolQuadrant`): is every new `PathReason` reachable, and does the fixture
  really exercise it?

**E. The instruments.** `ModelDiff.Compare` registers by grid label, else by the modal nearest
displacement and says GUESS. `ModelYardstick.Compare` registers by grid label, else by column
votes in 100 mm bins, and judges only inside her footprint. `GridAlignment.SolveByColumns` votes
one per member per bin. `dxf-inspect --members` reads a sheet without its words and tags. For each:
can it report "identical" / "registered" when the models differ, or "moved" when they do not?
State the input. Is `grid-names`'s "the sheet standing on itself" clause sound?

**F. Rule 12.** Find any number in §53–§64 or the plan's §1b table that is stated as a
population figure but was produced by a truncated command or a sample. The harness output lines,
the corpus summary lines and the census commands are quoted in the sections; the plan's run-7 row
says which count is the verb's and which was a scratch count.

## What is known and not to be re-found

- The differential runs under one vector; a rotation is named as not covered (§63, §64).
- §64: the by-name fit's tie goes to the tightest cluster, and to the smaller move only for the
  reissue diff (`SheetDiff`); `SetCheck` takes the default. Whether `SetCheck` should prefer the
  smaller move too is a fair question, not a known fault.
- A 30x41 return read as a column where the engineer models a pier: a question for the engineer,
  recorded (§63, plan §8 item 4).
- One long face facing three equal loops pairs with one and loses two, in either frame: named as
  a coverage fault for a later step (§63).
- The `AnotherOfficesWordsTests.RoofMezzanineAndFoundationAreWhateverTheOfficeCallsThem` flake
  (failed once in the fast suite on 2026-09-13, passes alone and on re-run; the class is in the
  vocabulary collection): known, open; if you can see WHY from the source, that is a finding.
- The corpus analyzer's `plan views placed` count differs between the verb (1,821 of 3,968) and
  the earlier scratch count (2,031 of 4,323): recorded in the plan's run-7 row; not a finding.
- Migrations 084–088 and the ETABS 23 line: Ian's, not code.
- `dxf.pdf.*` rows equal their compiled defaults by test (`CompiledDefaultsAreTheBankedRowsTests`).

## Output

`docs/codex/CODEX-PDF-INTAKE-STEPS-44-56-ADVERSARIAL-AUDIT-RESPONSE.md`. One finding per heading,
most severe first, each with: the sentence in `docs/PdfIntake.md` it contradicts (section and the
quoted words), the file and line, the smallest input that shows it, and what the code does with
that input. Findings are source deductions; say so. Do not propose fixes longer than a sentence;
do not write code. Cap: 25 findings. If you run out of real findings before 25, stop.
