# Codex 24 of N — audit: what today's steps claim against what they do

## Goal

Read, do not build, do not run. Intake steps 1–9a landed on 2026-09-08 in the commits titled
"Intake step …" (first `01c97622`, last the step 9a commit) and are described in
`docs/PdfIntake.md` §7–§15 and `docs/codex/CODEX-INTAKE-CONVERGENCE-13..23-*.md`. Most were
implemented by the verifier, not by you, so nobody has yet read them who did not write them. Find
where the code contradicts its own stated rule, where a test's name is wider than what it checks,
and where a doc claim is not something the code does. Report; change nothing.

Repo-only. Files in scope:

    Kor.Operations.EngineeringTools.Core/Intake/            DrawingIntake, SheetRecord, PathFate, FootingOutlines, TitleBlockFields
    Kor.Operations.EngineeringTools.Core/PdfToSafe/         GeometryFilterService, PdfPlanReader, GridBubbles, SheetFurniture, DxfExporter, PdfGeometryModels
    Kor.Operations.EngineeringTools.Core/                   SheetInventory, FootingScheduleReader, VectorPageReader, SheetTitleReader, SheetScaleReader
    Kor.Operations.EngineeringTools.Core.Tests/Intake/      every test file
    Kor.Operations.EngineeringTools.Core.Tests/             FiveStickFilesTests, SheetFurnitureIsNotStructureTests, Rules/CompiledDefaultsAreTheBankedRowsTests
    docs/PdfIntake.md, docs/codex/CODEX-INTAKE-CONVERGENCE-13..23-*.md

No UNC paths, no databases, no other repos, no stick files: the ledgers and overlays are the
verifier's; you read code and prose.

## Questions, in order

1. **Every path has exactly one fate.** `DrawingIntake.RemapToPopulation` maps the thinned read's
   fates onto the unthinned population. Is there an input on which a path gets two fates, or none,
   that `EveryPathOnEveryBankedPageHasExactlyOneFate` and `ThePopulationIsTheUnthinnedReadTests`
   would not catch? State the input.
2. **The wall rule.** `GeometryFilterService` reads a wall as a filled 4-vertex rectangle of wall
   proportions with a half-inch slack, declared column size winning. PDF walls are 425 against
   Revit's 850 over 23 plan sheets. From the code alone, name every shape the rule cannot read
   that a drafter draws as a wall (ribbons, L-shapes, walls with an opening, walls drawn as two
   faces) and say which of `TheWallRuleChangesNothingElse` and `AWallIsAFilledRectangle…` would
   stay green if the rule were wrong about each.
3. **Footings.** `FootingOutlines` pass 2 places a footing from one full side and two stubs.
   Construct, in prose, a dashed shape that is not a footing and that pass 2 would place: is there
   one on a structural plan (a dashed slab step, a depression, an opening below)? Does the label
   rule (nearest label within half the footing's size) rescue it, and what does the ledger say
   about it?
4. **Grid axes.** `GridBubbles.Axes` clusters bubbles within 1.5 pt of one another. Two distinct
   grid lines 1.5 pt apart at 1:96 are 50 mm apart — can that occur (offset grids, C.2 beside C)?
   Which test would fail?
5. **Typing.** `DrawingIntake.FirstTyped` types a sheet from bookmark, then SHEET TITLE, then a
   parsed storey, then the title text, then region words. Name a sheet title in KOR's own
   vocabulary that types wrong (a "PLAN" that is not a plan: a "DESIGN LOAD PLAN", a "KEY PLAN",
   a "FOUNDATION PLAN NOTES"), and whether `pdf-takeoff` would write a DXF for it.
6. **The ledger's arithmetic.** `SheetInventory.Of` builds primary rows and context notes. Is every
   word and every path in exactly one primary row? Where can a total change without any fate
   changing?
7. **Names wider than checks.** For every test class under `Core.Tests/Intake/` and for
   `FiveStickFilesTests`, one line: does the class name promise more than its assertions cover? Rule
   11 of `CLAUDE.md` says each harness carries WHAT IT COVERS / DOES NOT — list the ones that do not.
8. **Doc against code.** In `docs/PdfIntake.md` §7–§15, every sentence of the form "X is Y" about
   the code: mark the ones you could not find in the code. Ignore numbers; the verifier owns those.

## Output

One file, `docs/codex/CODEX-24-AUDIT-RESPONSE.md`, findings first, each with file:line, the
sentence it contradicts, and the smallest input that shows it. No fixes, no build, no test run.
