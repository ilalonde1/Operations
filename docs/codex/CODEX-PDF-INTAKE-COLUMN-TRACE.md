# Codex — build task: every composed column traced to the loop and the branch that made it

**Scope: this repository only, the files named below. Write code and its tests. No `dotnet build`, no
`dotnet test`, no drawing files, no database, no network.** Reading set 46 KB measured with `wc -c`.
Recommended reasoning: medium. Work on `develop` HEAD; Claude runs the acceptance and reports back.

## The problem, measured

On 31162-01 (an engineer's model 58 days old — a current yardstick), 20 of our 52 columns on L1 are
columns she does not model; six of them are `KOR-C203.2x254 … x558.8` — **8-inch wall fragments**.
`pdf-at` at one shows the P1 plan draws an 8-in wall through a 398 mm clip at a corner (Revit draws
wall joins as clipped fills); the reader writes that piece as a 398 mm pier on the wall layer; the
composer (`StructuralPlanClassifier`) turns a 203 × 398 wall-layer loop into an 8×16 column. Two
edits were tried on the classifier's branches and the set did not change by a byte: the fragments
come through a branch nobody could name, because **nothing says which loop and which branch made a
column**. `dxf-inspect --walls` lists loops and says "-> 1 panel(s)"; for columns there is nothing.

## What to build

1. `ColumnFootprint` (`Core/Dxf/DxfModels.cs`, find the record) gains an `Origin` — a small record
   `(string Layer, int LoopIndex, double LoopLength, double LoopThickness, string Branch)` — set at
   EVERY site that adds a `ColumnFootprint` in `StructuralPlanClassifier.cs` (grep
   `result.Columns.Add(` — count them; every one gets a distinct `Branch` name, a short kebab-case
   string that names the rule: `declared-size`, `short-wall-layer-loop`, `standalone-stub`,
   `nothing-paired-up`, `column-layer-loop`, …, in the words the comment beside the site uses).
   Where the site has no loop (a stub converted from a wall), `LoopIndex` is -1 and the wall's
   length/thickness fill the two numbers. Default for anything not yet set: `Branch = "unknown"` —
   the acceptance counts those and wants zero.
2. `takeoff dxf-inspect <plan.dxf> --columns` (`TakeoffCli/Verbs/DxfInspectVerb.cs`; read how
   `--walls` is done) prints one line per column: centre, size, layer, loop index, the loop's box,
   the branch, and — when a wall panel of the same sheet contains the column's centre — that wall's
   length and thickness (`WallAxis` list in the result; containment = within half the wall's
   thickness of its axis segment: `LoopGeometry` has distance helpers, find `DistanceToSegment`).
3. `takeoff corpus-query columns <job>` (`TakeoffCli/Verbs/CorpusQueryVerb.cs`; read how `set` is
   done): for one job's composed model — the work folder's `dxf/` views — runs the same per-sheet
   listing and then prints a table `branch × (inside a wall / not) → count`, so the class "columns
   made from wall fragments" is one number per job.
4. Tests (`Core.Tests/Dxf/ColumnsSayWhatMadeThemTests.cs`): a wall-layer loop 203 × 398 mm inside a
   2,845 × 203 wall's outline → the column's `Branch` names the branch that made it and the
   inside-a-wall check says yes; a 400 × 400 loop on the column layer → `column-layer-loop`, not
   inside a wall; a standalone 300 × 900 wall-layer stub → `standalone-stub`. The class summary
   states WHAT THIS COVERS and WHAT IT DOES NOT (rule 11 of `CLAUDE.md`).

## Read, in this order

1. `CLAUDE.md` rules 5, 7, 11 (3 KB of 15).
2. `Core/Dxf/DxfModels.cs` — `ColumnFootprint`, `WallAxis`, `PlanLoop` (about 4 KB of 14).
3. `Core/Dxf/StructuralPlanClassifier.cs` — every `result.Columns.Add(` site with 30 lines of context
   each (grep first; about 12 KB of 168), and `AddWallOrColumn` whole (lines ~2330–2500, 10 KB).
4. `TakeoffCli/Verbs/DxfInspectVerb.cs` — whole (find it; ~6 KB).
5. `TakeoffCli/Verbs/CorpusQueryVerb.cs` — the `set` view (about 3 KB of 12).
6. `Core/Dxf/LoopGeometry.cs` — the distance helpers (grep `DistanceTo`, 2 KB of 28).

## Rules for the change

- The composition must not change by a byte: `Origin` is carried, never read by a rule. The six-set
  gate is the proof.
- Warnings are errors; xUnit analyzers on; rule 7 for regexes and paths.
- Do not touch `GeometryFilterService.cs`, `Baselines/`, `PlanLoopBuilder.cs`.

## Acceptance (Claude runs; you do not)

Fast suite green; six-set gate byte-identical (recompose, ~1 min); `dxf-inspect --columns` on
31162's P1 view names the branch of every column with zero `unknown`; `corpus-query columns
31162-01` shows the fragment class as a count.

## Output

The code, in place, and `docs/codex/CODEX-PDF-INTAKE-COLUMN-TRACE-RESPONSE.md`: the sites and their
branch names, and anything you could not do within the rules.
