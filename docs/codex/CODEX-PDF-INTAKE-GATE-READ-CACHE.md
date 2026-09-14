# Codex — build task: the six-set gate reads each PDF once and recomposes when only the composer changed

**Scope: this repository only, the files named below. Write code and its test. No `dotnet build`, no
`dotnet test`, no drawing files, no database, no network.** Reading set 60 KB measured with `wc -c`.
Recommended reasoning: medium. Work on the tree as it stands (HEAD `af7b7821` on `develop` plus the uncommitted step-63 edits in the working tree — leave those files as they are); Claude runs the
build and the acceptance test afterwards and reports back.

## The problem

`SixSetsBuildAsBankedTests` (`Kor.Operations.EngineeringTools.Core.Tests/Intake/SixSetsBuildAsBankedTests.cs`,
7 KB) is the gate every step runs: six drawing sets built from their PDFs and compared byte for byte with
the banked `.e2k`. It takes 6–7 minutes, of which the READING half — `PdfOnlyBuild.WriteSheets`, the six
PDFs into DXF views — is nearly all, and that half is byte-for-byte what it was whenever a step changed
only the composer (`Core/Dxf/*`). On 2026-09-14 it ran eight times.

The analyzer already has the pattern (`CorpusAnalyzer.Run`, `--recompose`: a set whose manifest stands
keeps the views on disk and runs the ladder and the composer again). The gate does not.

## What to build

In `SixSetsBuildAsBankedTests`, per set, a read cache under `TestResults/six-sets/<job>/`:

1. A manifest beside the views: the mirrored PDF's SHA-256, the scale, and a SHA-256 over the READER'S
   SOURCE FILES (the file list below), computed at test time from the repository root (walk up from
   `AppContext.BaseDirectory` to the directory holding `Kor.Operations.EngineeringTools.Core.Tests.csproj`,
   then `..`).
2. When the manifest on disk equals the manifest computed now: do NOT call `PdfOnlyBuild.Build`; call
   `PdfOnlyBuild.Recompose(pdf, work, pages, sheets, options, conn)` with `sheets` rebuilt from the
   `sheets.csv` the previous build left — exactly as `CorpusAnalyzer.Run` does in its `recompose` branch
   (read that branch: `CorpusAnalyzer.cs` lines ~168–190; `ReadSheetRows` and `DxfFilesOf` are the
   readers; `PdfOnlyBuild.SheetsResult` is the record). The test must write `sheets.csv` after a full
   build the way the analyzer does (`CorpusAnalyzer.WriteSheetCsv` and `Rows(...)` — read `Rows` at
   `CorpusAnalyzer.cs` ~301–360 to see what a `SheetRow` needs from a `BuildOutcome`).
3. When it does not match, or `sheets.csv`/`dxf/` is missing: full `PdfOnlyBuild.Build` as now, then write
   the manifest and `sheets.csv`.
4. Print, per set, which path it took: `31168-01: read (manifest changed: reader source)` /
   `31168-01: recomposed (read cache from 2026-09-14 14:02)`.
5. `Recompose` reads the views from `dxf/` on disk (`Handoff.Disk`); the full build must therefore write
   the DXF files (it does today: `Build` writes them).

The reader's source files — the hash covers these and nothing else:
- every `.cs` under `Kor.Operations.EngineeringTools.Core/PdfToSafe/`
- every `.cs` under `Kor.Operations.EngineeringTools.Core/Intake/` EXCEPT `CorpusAnalyzer.cs`,
  `CorpusDiff.cs`, `SheetDiff.cs`, `SetCheck.cs`, `StoreysFromPlans.cs`
- under `Kor.Operations.EngineeringTools.Core/Dxf/`: `PlanSheetNaming.cs`, `DrawingVocabulary.cs`,
  `DxfSheet.cs`, `DxfModels.cs`, `LoopGeometry.cs`, `PlanLoopBuilder.cs`, `DashedLineJoiner.cs`,
  `MatchLineSheetJoin.cs`, `GridAlignment.cs`, `StructuralPlanClassifier.cs`, `RuleSettings.cs`
  (the reading half references these: `DxfExporter` calls `GridAlignment` and
  `StructuralPlanClassifier`; `GeometryFilterService` calls `PlanLoopBuilder` and `LoopGeometry`).
- `PdfOnlyBuild.cs` itself (both halves live in it; a change there invalidates - the safe direction).
- the rows: the hash also covers the string `PdfIntakeOptions` serialises to for the options in force
  (`PdfIntakeOptions.For(conn)` — find its record's members; `ToString()` of a record is enough).

A change to `E2kGeometryComposer.cs`, `DxfToEtabsService.cs`, `WallOutlineDecomposer.cs`, `WallNetwork.cs`,
`ModelDiff.cs`, `ModelYardstick.cs` or the Tests project therefore reuses the cache. State that list in
the test's summary as WHAT THE CACHE DOES NOT INVALIDATE ON, and say what fault that would hide: a
composer that reads a reader-side static set during reading (there is one - `PlanSheetNaming.Vocabulary`,
which `PdfOnlyBuild.WriteLevels` sets from the rows before the ladder; that is why the rows are in the
hash).

## Read, in this order

1. `CLAUDE.md` rules 5, 7, 11 (about 3 KB of 15 KB).
2. `Kor.Operations.EngineeringTools.Core.Tests/Intake/SixSetsBuildAsBankedTests.cs` (7 KB) - whole.
3. `Kor.Operations.EngineeringTools.Core/Intake/PdfOnlyBuild.cs` (22 KB) - `Build`, `Recompose`,
   `Compose`, `SheetsResult`, `SheetOutcome`, `Handoff` (lines 27–62 and 242–300).
4. `Kor.Operations.EngineeringTools.Core/Intake/CorpusAnalyzer.cs` - the recompose branch (~168–190),
   `Rows` (~301–360), `WriteSheetCsv`, `ReadSheetRows`, `DxfFilesOf` (~356–470). About 12 KB of 44.
5. `Kor.Operations.EngineeringTools.Core.Tests/Intake/TheCorpusLedgerRoundTripsTests.cs` (5 KB) - how a
   `SheetRow` is built and read back in a test.
6. `Kor.Operations.EngineeringTools.Core/Dxf/DrawingMirror.cs` (6 KB) - `SingleFile`.

## Rules for the change

- One new small class is fine (`SixSetReadCache` beside the test, or a nested class); no new project.
- Warnings are errors repo-wide; xUnit analyzers are on (argument order on `Assert.Equal` is
  expected-then-actual).
- Rule 7: no regex or Windows path through a non-raw string. Rule 5: the manifest is read back after it
  is written.
- The full-build path must be byte-for-byte what it is today: same `Build` call, same arguments.
- Do not touch `Baselines/`, `PdfOnlyBuild.cs` or `CorpusAnalyzer.cs`; if the recompose branch's readers
  are `private`, say so in your report and use `internal` ones only (`ReadSheetRows`, `ReadSetRow` and
  `Csv` are `internal`, `InternalsVisibleTo` the test project).
- Add a WHAT THIS COVERS / WHAT IT DOES NOT paragraph to the class summary, per rule 11.

## Acceptance (Claude runs; you do not)

`dotnet test --filter FullyQualifiedName~SixSetsBuildAsBanked` twice in a row with no source change: the
first run reads (6–7 min), the second recomposes and finishes in under 2 minutes, both byte-identical
against the bank. Then a whitespace-only edit to `E2kGeometryComposer.cs`: still recomposes. Then a
whitespace-only edit to `GeometryFilterService.cs`: reads again.

## Output

The code, in place, and a report at `docs/codex/CODEX-PDF-INTAKE-GATE-READ-CACHE-RESPONSE.md`: what you
changed (files, members), the manifest's exact contents, and anything you could not do within the rules.
