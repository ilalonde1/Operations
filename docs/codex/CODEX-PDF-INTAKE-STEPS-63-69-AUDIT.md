# Codex — adversarial audit of intake steps 63–70 and the night's infrastructure (read cache, page record, BridgeChains, partial ledger, column trace)

**Reading set: about 160 KB at HEAD on `develop` (the commit named in the paste-ready prompt), measured
with `wc -c` before handover — 40 KB of `docs/PdfIntake.md`, 6 KB of `CLAUDE.md`, ~110 KB of source
and tests, whole files and named line ranges as listed. No commit range. No build, no test run, no
drawing files, no `.e2k`, no `.dxf`, no CSV, no database, no network, no other repository, nothing
under `Baselines/`, `docs/etabs-handoff/` or `%LOCALAPPDATA%`.** Recommended reasoning: **medium**.

## What is being audited

Between 2026-09-14 afternoon and night, on one session's word, the following landed on `develop`
(each claim is in `docs/PdfIntake.md` §72–§80 with WHAT THIS COVERS / WHAT IT DOES NOT):

- **Step 63 (§72)** a wall is six inches or more, with a half-inch slack on the floor, applied on the
  PDF side (`GeometryFilterService`) and at four composer sites (`StructuralPlanClassifier` ×3,
  `WallOutlineDecomposer`).
- **Step 64 (§73)** yardstick provenance and age; a storey's rigid part.
- **Step 65 (§74)** the reference plan is the plan the most other plans can be SET ON (fits, not
  name counts).
- **Step 66 (§75)** one plan naming no storey is a one-storey building.
- **§76** the gate's read cache keyed on the reader's sources; the page record; BridgeChains'
  candidate grid; the per-set partial ledger.
- **Step 67 (§77)** the wood-plan rule: a sheet with two thirds of its walls as unfilled pairs (and
  twenty) is a wood plan, and on it unfilled pairs under 8 in and filled bands under the floor are
  partitions.
- **Step 68 (§78)** small-job title blocks; a sheet number glued to its title; every composed column
  carries the branch that made it.
- **Step 69 (§79)** numbered buildings are building tags; a tagged sheet keeps to its building's
  storeys only where the model names storeys by building; numbered buildings on one plan-named
  ladder share its storeys and one ROOF.
- **Step 70 (§80)** run 14's eleven storey movers looked at one by one; three rules from three
  regressions: the set's floor words have ONE order whichever building states each step; storeys
  are per building only where the model names them so (the ladder now uses the composer's test);
  a title that starts with LEVEL keeps its level (`StripSheetNumber` `>= 0`).

Your job: find where the code does not do what those sections say; where a rule called universal
is fitted to the set it was found on (31066's wood block, 31185's five buildings, 30993's sensor
layout, 31162's footings); where two copies of one rule disagree (the floor, the building prefix,
the storey ladder vs the composer); where a cache can serve a stale answer; and where a test's name
is wider than its assertions.

## Read, in this order (byte sizes are what you will read)

1. `CLAUDE.md` — rules 10, 11 and 12 only (5.6 KB of a 15 KB file).
2. `docs/PdfIntake.md` — §72 through §80, from the line `## 72. Step 63` to the end of the file
   (42 KB). Do not read the rest.
3. Source, whole files:
   - `Kor.Operations.EngineeringTools.Core/Intake/WallTypeTagging.cs` (12 KB)
   - `Kor.Operations.EngineeringTools.Core/Intake/StoreysFromPlans.cs` (19 KB)
   - `Kor.Operations.EngineeringTools.Core/Dxf/DrawingVocabulary.cs` (17 KB)
4. Source, named regions only (line numbers as of the commit in the prompt; if they have drifted,
   find the named member and read that):
   - `Kor.Operations.EngineeringTools.Core/Dxf/PlanSheetNaming.cs`: `SheetNumberPrefix` and
     `TitleOf` (~92–106), `Parse` (~108–262), `MatchStories` (~262–400),
     `NamedForAnotherBuilding` (~575–600). About 18 KB.
   - `Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs`: `WallFloorSlack` /
     `WallFloor` (~100–120) and the three sites that test `options.WallFloor` (~1970–1995,
     ~2225–2245, ~2340–2360). About 5 KB. Do not read the rest of this 171 KB file.
   - `Kor.Operations.EngineeringTools.Core/Dxf/WallOutlineDecomposer.cs` ~80–100 (1.3 KB).
   - `Kor.Operations.EngineeringTools.Core/PdfToSafe/GeometryFilterService.cs`: `WallFloorSlackMm`
     (~43) and the two sites that use it (~335–400). About 5 KB.
   - `Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs` ~1105–1145, the
     `referencePlan` block only (3 KB). Do not read the rest of this 216 KB file.
   - `Kor.Operations.EngineeringTools.Core/Dxf/ModelYardstick.cs`: `BuildingPrefix` (~88–100) and
     `Building` / the storey-name normaliser around it (~455–485). About 3 KB.
   - `Kor.Operations.EngineeringTools.Core/Dxf/DxfModels.cs` ~195–235, `ColumnOrigin` and
     `ColumnFootprint` (2.4 KB).
   - `Kor.Operations.EngineeringTools.Core/Dxf/PlanLoopBuilder.cs` ~295–390, `BridgeChains` with
     its `Index` and `Near` locals (5 KB).
   - `Kor.Operations.EngineeringTools.Core/Intake/DrawingIntake.cs` ~255–285, the `ReadPage` call
     into `WallTypeTagging` (2.3 KB).
   - `Kor.Operations.EngineeringTools.Core/Intake/CorpusAnalyzer.cs`: the `partialLedger` lines
     (~178–182, ~266) and `AppendSetRow` (~475–495). About 2 KB.
5. Tests, whole files:
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/AWoodPlansStudWallsArePartitionsTests.cs` (4.7 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/ASetsStoreysAreWhatItsPlansNameTests.cs` (10.5 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/SixSetReadCache.cs` (7.7 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/SixSetsBuildAsBankedTests.cs` (9.2 KB)

## The questions, in priority order

**A. The floor, five times (§72).** The reader keeps a filled band at `minWallThicknessMm −
WallFloorSlackMm` (12.7 mm) and over; the composer's `WallFloor` is `MinWallThickness −
WallFloorSlack` (0.5, converted by `InUnitOf`), tested at a ribbon band, an open face pair, a filled
rectangle, and in `WallOutlineDecomposer`. Are the five the same number for the same wall, in the
same units, with the same inclusive/exclusive edge (`LoopGeometry.Beyond` vs `>=`)? Smallest inputs:
a wall at exactly 139.7 mm; a set drawn in feet; a set whose `dxf.min-wall-thickness` row is not 6 in.
`ThinBand`: is a band under the floor kept anywhere the model reads?

**B. The wood-plan rule (§77, `WallTypeTagging.StudWallsOfAWoodPlan`).** It is called from `Apply`
(when the set has assemblies) and from `DrawingIntake.ReadPage` (when it has none): can one page be
partitioned twice, or never? The decision counts `fromFaces` against the sheet's walls — walls of
which layers, before or after the assemblies moved any? A filled band "under the floor without
slack": the PDF side already dropped everything under floor − slack, so the band this catches lies
in a 12.7 mm window — is that the 140 mm poché'd 2×6 the section names, at every scale? A sheet
with exactly twenty unfilled pairs and two thirds; a concrete plan drawn mostly unfilled (the
section says none of seven measured were — is seven the evidence for "universal"?); a wood plan
whose sole retaining wall is an unfilled pair at 8 in exactly (`<` or `<=`).
`UnfilledWallMinThicknessMm` is compiled and the KorStandards row is written but not read — which
gate says so, and would it stay green when the row is applied with a different value?

**C. Numbered buildings (§79).** `DrawingVocabulary.Building`: `(?:[A-Z]|\d{1,2}[A-Z]?)` after a
building word, closed by `\b`. Smallest inputs: "BUILDING 2 STOREY ADDITION", "BUILDING 12 UNITS",
"BLDG 1 OF 2", "BUILDING A1", "BUILDING PERMIT SET". `PrefixBuilding`: `(?<![A-Z0-9])` — does
"S2.3-LEVEL 2" (a sheet number before the dash) read building 3? `ModelYardstick.BuildingPrefix`
now accepts `\d{1,2}[A-Z]?-` before `L\d|P\d|LEVEL|ROOF|ELEV|MEZZ`: "1-P1", "2A-ROOF", and what
else in `ModelYardstick` strips or compares storey names with the old `^[A-C]-` assumption
(~455–485)? `MatchStories`: `storeysByBuilding` is true if ANY storey names a building — a model
with A-L1..A-L5 and an unprefixed ROOF: where does building B's tagged L1 plan go, and where does
the shared ROOF plan go? `StoreysFromPlans`: the shared ROOF is inserted when more than one tag
has no storey of its own — one numbered building with plans and roof, another with only a roof
plan (no level plan): count the tags, trace `roofAt`, and say whether `ladderReachesAboveThePlans`
and `covered` can leave a set with both a ROOF and a `1-ROOF`.

**D. The reference plan (§74, `DxfToEtabsService` ~1119–1132).** Chosen by the count of other plans
`SolveByName` can set on its carried axes, then matched axes, then axis count, then file name. A set
where every plan carries the same named grid (all tie → file name decides — is that stable across
the two routes?); a set where the sensor layout still fits the most (does anything exclude a
non-structural sheet before this, or only migration 091's pattern, which is not applied?); a set
with no plan at `LeastConvincingByName` named axes (`referencePlan` null — what then?).

**E. The sheet number glued to its title (§78, `SheetNumberPrefix`).** The regex
`^\s*S[-._ ]?\d+(?:[.-]\d+)*(?=[A-Za-z]|\s|_|-|$)(?:_\d+_)?[ _-]*` runs in `TitleOf` after
`^.*?_\d+_` and on the raw name in `Parse` (`ownName`). Smallest inputs: "S2.04_1_LEVEL 1 PLAN"
(the standard export name — is anything lost?), "S-6BASEMENT FLOOR PLAN", "S3 SECTIONS",
"S1A-FOUNDATION", "S2.1 2ND FLOOR PLAN" (does the `[ _-]*` tail eat the 2 of 2ND? — it cannot, but
confirm the lookahead), "SP-1 SITE PLAN", "S-101 LEVEL 1". Does `TitleOf` and `ownName` give the
same storey for every one?

**F. The read cache (§76, `SixSetReadCache.ReaderHash`).** The hash covers `PdfToSafe/**`,
`Intake/**` less five analyzer files, eleven named `Dxf/` files, and the options. Name any type
outside that set that `PdfOnlyBuild.WriteSheets` reaches while producing a set's DXF views or
`sheets.csv` (follow the calls from `DrawingIntake.ReadPage` and `PdfOnlyBuild.WriteSheets` by
name only; you have the file list, not the call graph — say what you could not follow). A file in
`Intake/` that is excluded but does affect the read? A KorStandards row read during the read that
is not in `options`? Each one is a way the gate passes on a stale read.

**G. BridgeChains' grid (§76, `PlanLoopBuilder` ~303–380).** The claim: "the first mergeable pair
is the one it always was". The old scan was ascending (i, j > i) restarted after every merge; the
new one lists `Near(i)` from a grid with cell = `max(bridgeTolerance, 2 × extendLimit) + 1e-5`.
Is `Near(i)` returned in ascending j, and does it search the 3 × 3 neighbourhood so a pair
straddling a cell edge is found? Two chain ends that reach one corner within `extendLimit` each
are within `2 × extendLimit` of each other — but is the corner test in `TryJoinByExtending` bounded
by `extendLimit` on BOTH ends, or can one end extend further?

**H. The partial ledger (§76).** `AppendSetRow` writes the header on the first row under one lock;
the sorted ledger is still written at the end; `--reuse` keeps manifests that stand. A run of
`--jobs a,b` overwrites `ledger-sets.csv` with two rows (known — see below); does it also delete
and restart `ledger-sets.partial.csv`, so a killed full run's partial ledger is lost by the
recovery run that follows it?

**I. Column provenance (§78, `ColumnFootprint.Origin`).** Excluded from equality and hash, so two
geometrically identical columns from two branches deduplicate — which one's origin survives, and is
it the same on both routes? Any site that constructs a `ColumnFootprint` without naming a branch
(the default is `"unknown"`; the listing prints "unknown origins: 0" — is that count from the
same default)?

**J. Step 70's three rules (§80).** (1) `WithFloorWordsRankedBy` is now one map over every title:
a set with two buildings that use the same words in genuinely different orders — A: GROUND → MAIN,
B: MAIN → GROUND — is a cycle and the row stands; A: GROUND → MAIN → UPPER, B: MAIN → UPPER → ROOF?
(ROOF is not a floor word: what does `FloorWordIn` return for the over-part "ROOF FRAMING OVER - BLDG
22", and can a building tag's NUMBER ever reach `SingleLevel` there?). (2) `StoreysFromPlans` and
`PlanSheetNaming.MatchStories` both test `storeysByBuilding`, but on different lists — the ladder's
`order` at that moment (the chain's names plus the plan-only storeys inserted so far) and the
composer's `stories` (the finished model's names). Name a set where the two answers differ (a chain
with one prefixed mezzanine? a ladder whose only building-named storey is the roof this loop is about
to add?). (3) `StripSheetNumber` at `>= 0` on a name that starts with ROOF then names a level ("ROOF
PLAN LEVEL 4 AREA") — which wins now, and did it before?

**K. The checks versus their names.** For each test file in the reading set, compare WHAT THIS
COVERS with the assertions. In particular `AWoodPlansStudWallsArePartitionsTests` (does any test
put a filled band in the 12.7 mm window, and is the two-thirds boundary tested at exactly two
thirds?) and `NumberedBuildingsOnOnePlanNamedLadderShareItsStoreysAndItsRoof` (does it assert
where the five buildings' LEVEL 1 plans GO, or only the ladder's names?).

## Known and not to be re-found

- Run 14's storey movers are looked at in §80's table; its title-reader defects from step 68 are
  named there (neighbouring fields in the title — "DRAWING NO S2.02.1", "PROJ. # 30878-02 DRAWING
  NUMBER"; a displaced dash — "LEVEL 5 LEVEL 14 - PLAN"; a rotated revision strip as the title on
  01589; a project name as the title on 01379's S212.9 and S402) and are NOT findings unless you can
  name the line in `TitleBlockFields` that produces one — the file is not in the reading set, so
  say so rather than guess. A LOADING PLAN naming storeys (30878-02) is named there too.
- Run 14's `ledger-sets.partial.csv` counted 280 lines by `wc -l` against 297 in the sorted ledger,
  and the file was deleted by run 15 before it could be parsed; run 15's partial is compared to its
  ledger in the morning message. If they differ, that is a finding for question H; if not, not.

- Migration `091_ASitePlanIsNotAStructuralPlanAndAnUnfilledWallIsARetainingWall.sql` (in the
  KOR.Drafter repo) is written and NOT applied; the wood rule's 8 in is compiled and the row is not
  read by code yet. Stated in §77.
- `PlanarRings` (`048912f1`) is a prototype, not wired. §71.
- 01783 / 01589 / 01788 are still blank-titled; 31162's "columns" are footings on the column layer
  and no rule was written; 31108's missing columns are un-looked-at. §78.
- A `--jobs` run overwrites `ledger-sets.csv` with its rows (the analyzer's known shape; the ledger
  is banked by copy before any second run touches the work dir).
- The shifted-page differential (`TheSameDrawingsShiftedOnThePageBuildTheSameStructureTests`) is
  owed on frame-class changes; steps 67–69 are not frame changes. It was run green on the
  BridgeChains change (§76). If it was run for steps 67–69 too, the paste-ready prompt says so.
- `CurrentYardstickDays` was re-classed Tolerance and `UnfilledWallMinThicknessMm` added as the
  49th Convention on `EveryReaderConstantIsTriagedTests`' ratchet — the ratchet moved by design.

## Output

`docs/codex/CODEX-PDF-INTAKE-STEPS-63-69-AUDIT-RESPONSE.md` (the file keeps its name; it covers 63–70). One finding per heading, most severe
first, each with: the sentence in `docs/PdfIntake.md` it contradicts (section and the quoted
words), the file and line, the smallest input that shows it, and what the code does with that
input. Findings are source deductions; say so. Do not propose fixes longer than a sentence; do not
write code; do not edit any file but the response. Cap: 15 findings. If you run out of real
findings before 15, stop.
