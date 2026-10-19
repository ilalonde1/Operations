# Engineering fix 2 of 7: the stick-file route deletes only its own files and reads the same rules everywhere

> ⛔ **WITHDRAWN 2026-10-19 — DO NOT RUN.** The re-review found its central fix wrong. `corpus-analyze` relies on `Build`
> wiping its folder: `set.csv`, `sheets.csv`, `manifest.txt` and the yardstick results live there, and a partial
> delete would leave a previous run's results behind. The guard belongs in the `stickfile` verb. Its items 4-6 are
> latent: the `dxf.schedule` rows equal the compiled defaults. Superseded by the plan of 2026-10-19. Kept as a record.

Bounded brief for Codex. Source edits and gates only. **Do NOT run `dotnet build` or `dotnet test`**: your test runner
hangs here, and Claude verifies on the dev box. Apply, read your own diff back, write the response, stop. No git
operations beyond your own edits.

The response goes beside this file as `...-RESPONSE.md` (same name). Reasoning: medium. Reading set, measured: about
95 KB, listed at the end. Read only the line ranges given.

## Six defects, each verified in the code on 2026-10-19

1. **`takeoff stickfile <pdf> <outDir>` deletes `<outDir>` and everything in it.**
   - `PdfOnlyBuild.Build` (`Core/Intake/PdfOnlyBuild.cs:306`) runs `Directory.Delete(workDir, recursive: true)` before
     it writes.
   - So pointing the verb at the Desktop, a project folder, or the folder the PDF sits in wipes that folder.
2. **Without `--rules-db`, 11 of the 14 verbs that read PDFs ignore `KOR_ENGINEERINGTOOLS_STANDARDSDB`.**
   - These 11 call `PdfIntakeOptions.For(rules)`, and `For(null)` returns compiled defaults
     (`Core/PdfToSafe/PdfIntakeOptions.cs:278-281`).
   - Only corpus-analyze, corpus-gate and pdf-assemblies fall back to the variable.
   - `stickfile` then hands the composer `RequireRuleSettings = true`, which *does* read the variable. So one run
     reads the drawing under defaults and composes the model under the banked rows.
3. **`stickfile` hardcodes the fallback scale and describes it wrongly.**
   - It uses the literal `96` (`StickfileVerb.cs:84`) instead of `options.FallbackScale` (the `dxf.pdf.fallback-scale` row).
   - Its comment at line 82 says the scale "IS READ OFF THE SHEET unless given". In fact every sheet's stated scale
     wins even when `--scale` is given (`DrawingIntake.cs:129-139`); the number is only the fallback for a sheet that
     states none.
   - The printed line at :85 repeats the same misdescription.
4. **The per-page storey read ignores the job's level words.**
   - `DrawingIntake.cs:351` passes `ScheduleGridReader.DefaultLevelLabelWords` and `DefaultLevelNameWords`.
   - The set-wide read uses `options.LevelLabelWords` and `options.LevelNameWords` (`PdfOnlyBuild.cs:277`).
5. **`PdfPlanReader.Read` never passes the void and slab words to `GeometryFilterService.Classify`.**
   - The file contains `VoidWords` / `SlabWords` 0 times. `DrawingIntake` passes `options.VoidWords` and
     `options.SlabWords` (`DrawingIntake.cs`, at its `Classify` call).
   - So `pdf-overlay` and the PdfToSafe window classify an X with void words differently from the stick-file route.
6. **The `dxf.schedule.*` rows are never loaded in production.**
   - `MarkRowScheduleReader.ApplyRules` has one caller in the whole repo, a test (`MarkRowScheduleReaderTests.cs:48`).
   - Production uses the compiled `ColumnDefaults()` / `FootingDefaults()` / `ShearWallDefaults()`
     (`ColumnScheduleReader.cs:113`, `FootingScheduleReader.cs:58`, `ScheduleGridReader.cs:623`, `DrawingIntake.cs:220`).
   - KOR.Drafter migration 080 seeds 9 `dxf.schedule.*` rows, so wiring them in may change what a sheet reads.
     Claude measures that; you only wire.

## The fixes

1. **`Build` deletes only what `Build` writes.**
   - Replace the recursive delete with removal of its own outputs: the `dxf` subfolder, plus `levels.csv`, `out.e2k`
     and `report.txt` in `workDir`. Then `Directory.CreateDirectory(workDir)`.
   - Anything else in the folder is left untouched.
   - `StickfileVerb` overwrites `sheets.csv`, `questions.xlsx` and `model.png` as it does today.
   - Update the doc comment on `Build` to say this.
2. **One CLI helper for the rules.**
   - Add `PdfIntakeOptions.ForRun(string? rulesDb)` = `For(rulesDb ?? Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable))`.
   - Use it in the 14 verbs listed below, including the 3 that inline the fallback today, so there is one way.
   - Leave `For` itself unchanged: tests call `For(null)` to mean compiled defaults.
3. **`stickfile` uses `options.FallbackScale`** when `--scale` is absent, and gets it after options are loaded.
   - The comment and the printed line say what happens: each sheet is read at the scale it states; `N` is used only
     for a sheet that states none.
   - Apply the same wording fix to the stale "WHAT THIS IS NOT YET" remark at `PdfOnlyBuild.cs:18-23`. The in-memory
     handoff exists (`Handoff.Memory`, `SheetsResult.Views`), so remove the claim that it does not.
4. **`DrawingIntake.cs:351` passes `options.LevelLabelWords` and `options.LevelNameWords`.**
5. **`PdfPlanReader` passes the options' void and slab words** to `Classify` on every overload that has a
   `PdfIntakeOptions`. Overloads without options keep today's behaviour.
6. **Schedule rows reach the readers.**
   - Give `PdfIntakeOptions` three init properties: `ColumnSchedule`, `FootingSchedule` and `ShearWallSchedule`, of
     type `MarkRowScheduleReader.Options`.
   - Default each to today's compiled `...Defaults()`. In `PdfIntakeOptions.ApplyRules`, set each to
     `MarkRowScheduleReader.ApplyRules(<defaults>, settings)`.
   - Pass them through where `DrawingIntake` calls the three schedule readers and `SchedulesOn`, adding an optional
     options parameter to each reader entry point that defaults to today's value.
   - Callers that pass nothing behave exactly as today.

## Gates (authored, not run), in `Kor.Operations.EngineeringTools.Core.Tests/Intake/`

- `BuildNeverDeletesWhatItDidNotWrite`: a temp `workDir` holding `keep.txt` and a subfolder `notes/`. Run the delete
  step: extract it as a small internal method `ClearOwnOutputs(workDir)` and test that. Both survive, a stale
  `out.e2k` and `dxf/x.dxf` are gone.
- `EveryPdfVerbReadsTheRulesTheSameWay`: a source scan of `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/*.cs`.
  `PdfIntakeOptions.For(` appears 0 times; `PdfIntakeOptions.ForRun(` appears in each of the 14 files below.
- `TheScheduleDefaultsAreTheCompiledDefaults`: `PdfIntakeOptions.Default.ColumnSchedule` equals
  `MarkRowScheduleReader.ColumnDefaults()`, and the same for footing and shear wall.
- `ScheduleRowsReachTheReader`: `ApplyRules` with a fake settings dictionary carrying one `dxf.schedule.column.*` key
  changes `ColumnSchedule` and nothing else.

## Never read

`Baselines/`, `docs/etabs-handoff/`, any `.e2k`, `.dxf` or `.csv`. Do not re-bank anything.

## Read (line ranges are inclusive)

- `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/StickfileVerb.cs` (whole, 10 KB)
- `Kor.Operations.EngineeringTools.Core/Intake/PdfOnlyBuild.cs` 1-60, 296-320
- `Kor.Operations.EngineeringTools.Core/PdfToSafe/PdfIntakeOptions.cs` (whole, 22 KB)
- `Kor.Operations.EngineeringTools.Core/PdfToSafe/PdfPlanReader.cs` (whole, 15 KB)
- `Kor.Operations.EngineeringTools.Core/MarkRowScheduleReader.cs` 60-200
- `Kor.Operations.EngineeringTools.Core/Intake/DrawingIntake.cs` 200-300, 340-360
- `Kor.Operations.EngineeringTools.Core/ColumnScheduleReader.cs` 100-125
- `Kor.Operations.EngineeringTools.Core/FootingScheduleReader.cs` 40-70
- `Kor.Operations.EngineeringTools.Core/ScheduleGridReader.cs` 610-640
- The 14 verbs, only the line holding `PdfIntakeOptions.For(`:
  - CorpusAnalyze, CorpusGate (two lines)
  - MarkupList, MarkupReconcile
  - PdfAssemblies, PdfAt, PdfInventory, PdfLevels, PdfOverlay, PdfTakeoff, PdfVsDxf
  - SetCheck, SetDiff
  - Stickfile

  Each is in `TakeoffCli/Verbs/<Name>Verb.cs`.

## Response

Files changed, gates added, and one line per defect saying how it is now closed. Under 70 lines.
