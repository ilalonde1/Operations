# Codex — adversarial audit of intake steps 57–61, brief A of two: the instruments and the corpus rules

**Reading set: 20 files at HEAD (`d42ad436` on `develop`), 88 KB measured with `wc -c` before handover.
No commit range. No build, no test run, no drawing files, no `.e2k`, no `.dxf`, no CSV, no
database, no network, no other repository, nothing under `Baselines/` or `docs/etabs-handoff/`.**
Recommended: reasoning **medium**. Brief B (the reading rules) is a separate file for a separate
window; do not read its files from here.

## What is being audited

Steps 57–61 (2026-09-13/14) answered the previous audit, banked corpus runs 8 and 9, and built
three instruments: `corpus-query diff` (what moved between two corpus runs, set by set), the
yardstick's frame judged by support, and the entity-order differential. Two of those found real
faults the same night (§68: four sets' yardstick verdicts moved with the order our columns were
listed; §70: a unit literal in the dash joiner; the loop builder's walk is order-dependent on all
six sets — **known, red, skipped**). The claims are in `docs/PdfIntake.md` §68 and §70 (§67's
"Corrected by run 9" paragraph too), each with WHAT THIS COVERS / WHAT IT DOES NOT. Your job is
to find where the code does not do what those sections say, where a test's name is wider than
its assertions, and where an instrument can report "same" when the models differ or "moved" when
they do not.

## Read, in this order (byte sizes are what you will read)

1. `CLAUDE.md` rules 10, 11 and 12 only (about 3 KB of a 15 KB file).
2. `docs/PdfIntake.md` — §68 and §70 only, and the last paragraph of §67 ("Corrected by run 9").
   About 14 KB. Do not read the rest of the file (280 KB).
3. Source, whole files:
   - `Kor.Operations.EngineeringTools.Core/Intake/CorpusDiff.cs` (4 KB)
   - `Kor.Operations.EngineeringTools.Core/PdfToSafe/TriangleTwins.cs` (6 KB)
   - `Kor.Operations.EngineeringTools.Core/Dxf/ModelDiff.cs` (13 KB)
   - `Kor.Operations.EngineeringTools.Core/Dxf/DashedLineJoiner.cs` (8 KB)
   - `Kor.Operations.EngineeringTools.Core/Dxf/DxfSheet.cs` (4 KB)
4. Source, named regions only (read the method, not the file):
   - `Kor.Operations.EngineeringTools.Core/Dxf/ModelYardstick.cs`: `Register` (line ~415 to the
     end of the method), `DistanceToWall` (~289), `Summary` (~324), and the `oursOnHerWalls` loop
     around line 190. About 8 KB.
   - `Kor.Operations.EngineeringTools.Core/Intake/CorpusAnalyzer.cs`: `YardstickFor` (~61),
     `AnotherJobsFile` both overloads and `SizeAndNameComparer` (~235–275), `DxfFilesOf` and
     `DxfFileSeparator` (~416). About 6 KB.
   - `Kor.Operations.EngineeringTools.Core/PdfToSafe/GeometryFilterService.cs`: the twin pass
     only — the block at line ~205–215 (`var twins = TriangleTwins.Pair(...)`) and the use at
     ~232–240 (`twins.OtherHalfOf` / `twins.Union`). About 2 KB.
   - `Kor.Operations.EngineeringTools.Core/Dxf/PlanLoopBuilder.cs`: `Build` (lines 30–147) —
     the KNOWN, MEASURED, NOT FIXED note at its head is the subject of question C. About 6 KB.
   - `Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs`: `PlacedMembers` (class
     at ~1961–1985) and the `PointAt` search loop around line 688–705. About 3 KB.
   - `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/CorpusQueryVerb.cs`: `Diff` and `Summary`
     (~48–65 and ~144–185). About 5 KB.
5. Tests, whole files:
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/WhatMovedBetweenTwoRunsIsClassedByTheFirstThingThatChangedTests.cs` (5 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/AStickFileThatIsAnotherJobsIsReadOnceTests.cs` (4 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/TheSameDrawingsInAnotherOrderBuildTheSameStructureTests.cs` (9 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Dxf/ADashedLineIsJoinedWhereverThePageOriginIsTests.cs` (2 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/OurOwnOutputIsNeverTheYardstickTests.cs` (2 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/AModelIsTheSameInInchesAndMillimetresTests.cs` (9 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/TheDifferentialAndTheRenderAreCodeTests.cs`
     — the last test only, `ATurnedWallAndASecondCopyAreSeen` (2 KB)
   - `Kor.Operations.EngineeringTools.Core.Tests/Intake/AModelIsMeasuredAgainstTheEngineersOwnTests.cs`
     — the last test only, `TheYardsticksFrameIsJudgedBySupportAndNeverByTheOrderOfOurColumns` (2 KB)

## The questions, in priority order

**A. The twin rule (`TriangleTwins`, §67's correction, §70 item 4).** Two filled triangles of one
colour sharing an edge whose union is a convex quadrilateral are one shape; the first carries the
four corners, the second takes the first's fate outright. Find the smallest input on which this
(i) joins two triangles that are two things (two hatch cells of one colour meeting on a diagonal;
two arrowheads point to point — note the shared-edge test wants TWO shared vertices), (ii) leaves
a real tessellated rectangle as two lone triangles (a driver that writes the diagonal's vertices
in a different order, or 0.02 mm apart, or as a fan of three), (iii) gives the second half a fate
that contradicts the ledger's invariant that a path has one fate whose disposition is defined by
its reason alone (`EveryPathHasExactlyOneFateTests` asserts it). The pairing is by `Math.Round`
of a vertex to the millimetre for the neighbour lookup and a hundredth for equality: state a
vertex pair that lands in different cells and is missed.

**B. The corpus diff (`CorpusDiff`, §68).** Every set in ONE class by the first thing that changed.
Is the order of the classes right for what a reader would act on (a set that gained storeys AND
changed placement reads as Storeys)? A placement that changed to the same COUNT of different
sheets reads as Composition — is there a class the ledger's columns could distinguish that this
does not? The yardstick verdict per class sums `OursWithin100` — a set that went from 8 of 64 to
5 of 10 reads as worse by count and better by share: which does the table say, and is that stated?

**C. The loop builder's order dependence (`PlanLoopBuilder.Build`, §70 item 5) — KNOWN, RED,
SKIPPED.** The differential is in the suite with a `Skip` reason carrying the numbers, and one
cut (a canonical coordinate sort) was tried and reverted because it moved every set's walls and
a plate under the page shift. This question is not "is it order-dependent" (it is, measured) but:
from the source, WHERE does the order enter — the seed order, the node numbering, `PickContinuation`'s
tie, the adjacency list order at a junction, `BridgeChains` — rank them, and say which of them a
rule for "which ring a shared edge belongs to" would have to decide. Do not propose a sort.

**D. The yardstick's frame (`ModelYardstick.Register`, §68).** Every bin within one vote of the
fullest, refined to its median, judged by support; ties to the tighter cluster then the smaller
move. Is the support count itself order-independent (it counts OUR points with ANY of theirs
within 100 mm — can two of ours share one of theirs)? Is "the smaller move" a property of the
fit or of where the model sits (the reissue diff kept it for a reason; is that reason here)?
`DistanceToWall`: the point-in-polygon test for a two-point wall, and a wall whose ring is not
simple.

**E. `ModelDiff` one-to-one (§70 F17/F18).** Greedy nearest-first matching, each member one
partner, walls by extent along X and Y. Find an input where greedy pairing leaves two members
unmatched that a different pairing would match (three members in a row 1 unit apart); and a wall
turned by 45° in place (both extents change — is that lost/gained, and should it be?).

**F. `DxfSheet.Reversed` and `AnotherJobsFile`.** Reversed: an R12 POLYLINE is kept whole through
its VERTEX entities to SEQEND; what other compound entity or section (BLOCKS, INSERT with
ATTRIBs) would this break, and does the six-set reading contain it? `AnotherJobsFile`: grouped by
length and NAME before hashing — a copy under another name is never found (stated); is the
owner rule (the job whose number the file NAME starts with) right when the name carries a
different job's number than the folder, and what does the ledger then say?

**G. The checks versus their names.** For each test file above, compare the class summary's WHAT
THIS COVERS with the assertions. Name any assertion weaker than the sentence claiming it.
`AModelIsTheSameInInchesAndMillimetresTests` now compares joints to a ten-thousandth of an inch:
is `N()` at 4 decimals in inches a hundredth of a millimetre or coarser, and does the assertion
say what it does?

## Known and not to be re-found

- The entity-order differential is red on all six sets; the loop builder is the cause; a sort was
  tried and reverted (§70). Question C asks WHERE, not whether.
- The yardstick's residual statistics (`r <= 100` over a distribution) are not place decisions and
  are not compared through `LoopGeometry.Within` on purpose.
- The sheet ledgers are not banked beside the set ledgers (the DB has them per run); `diff`'s
  reading line prints only against a live corpus folder.
- 16 sets are one file (01783-01's); the population is 277 jobs; the plan's §1a had flagged it on
  2026-09-11 and nothing acted on it until step 60.

## Output

`docs/codex/CODEX-PDF-INTAKE-STEPS-57-61-AUDIT-A-INSTRUMENTS-RESPONSE.md`. One finding per
heading, most severe first, each with: the sentence in `docs/PdfIntake.md` it contradicts (section
and the quoted words), the file and line, the smallest input that shows it, and what the code does
with that input. Findings are source deductions; say so. Do not propose fixes longer than a
sentence; do not write code. Cap: 15 findings. If you run out of real findings before 15, stop.
