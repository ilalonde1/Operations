# Engineering fix 3 of 7: the app and the publish do what they say

> ⛔ **WITHDRAWN 2026-10-19 — DO NOT RUN.** Item 5 would have reinstated the "Sheets read" tab, which was removed on
> purpose on 2026-08-07 (84eb76b1: "60 rows nobody reads in a spreadsheet"). Item 6 extended Ian's "never overwrite"
> rule to the publisher without asking him; explainer withdrawal is deliberate (`PublishExplainers`).
> Superseded by the plan of 2026-10-19. Kept as a record.

Bounded brief for Codex. Source edits and gates only. **Do NOT run `dotnet build` or `dotnet test`**: Claude verifies on
the dev box. Apply, read your own diff back, write the response, stop. No git operations beyond your own edits.

The response goes beside this file as `...-RESPONSE.md`. Reasoning: medium. Reading set, measured: about 70 KB
(listed at the end).

## Six defects, each verified in the code on 2026-10-19

1. **The app's "Drawings to ETABS Model" window builds from compiled defaults.**
   - `Kor.Operations.App/EngineeringTools/DxfToEtabs/DxfToEtabsWindow.xaml.cs` checks that
     `KOR_ENGINEERINGTOOLS_STANDARDSDB` is set (around 212-221).
   - It then builds a `DxfToEtabsRequest` that sets `RuleSettingsConnection` but not `RequireRuleSettings` (around
     230-238). `RequireRuleSettings` appears in that file 0 times.
   - Rules load only when the flag is true (`DxfToEtabsService.cs`, around 778-780). The CLI, publish and stick-file
     routes all set it.
2. **The publish summary tells the engineer something false.**
   - `Kor.Operations.EngineeringTools.Core/Dxf/PublishSummary.cs:111`: "No loads, diaphragms, stiffness modifiers,
     section properties, meshing or design — those are yours."
   - The composer writes WALL PROPERTIES, SLAB PROPERTIES and FRAME SECTIONS, and mesh assigns (`OBJMESHTYPE`,
     `AUTOMESH`; `E2kGeometryComposer.cs` around 1064, 1136-1137, 1227, 1392, 1689).
   - It writes diaphragms when `AssignDiaphragms` is on (a rule, off by default).
3. **The publish refusal names a flag that does not exist.**
   - `PublishDiscovery.cs:221` says "--dxf <name>".
   - `PublishVerb` accepts `--dxf-folder` (`TakeoffCli/Verbs/PublishVerb.cs:71`) and rejects anything else as unknown.
4. **A comment says the stack merge is off; the code runs it.**
   - `DxfToEtabsService.cs` around 2599-2603: "⚠ OFF, AND ONE LINE FROM ON. Turn this true to run it".
   - `MergeStacksIntoOneLabel` defaults to `true` (`DxfToEtabsService.cs:124`) and is applied at 2633.
5. **The questions workbook's "Sheets read" tab is never written.**
   - `ModelQuestionnaire.WriteSheetLedger(XLWorkbook, DxfToEtabsReport)` (`ModelQuestionnaire.cs:1809`) has no caller:
     1 repo hit, its definition.
   - The remark on `Write` (around 1296-1306) describes it as one of the sheets.
6. **A publish overwrites what it delivered before.**
   - `JobPublisher.cs` lands with `File.Copy(..., overwrite: true)` (around 216 and 222).
   - `Withdraw(...)` and the `-QUESTIONS-for-Andrea.xlsx` cleanup (around 230-236) delete files from the engineer's
     model folder.
   - Office rule, binding: **a delivered artefact is never overwritten or deleted. The previous one is kept beside the
     new one.**

## The fixes

1. **The window sets `RequireRuleSettings = true`.** If the load throws, show its message in the window's existing
   error path rather than crashing.
2. **The summary says what was written, from the run's own facts.**
   - It wrote: wall, slab and column section definitions; mesh settings on the members it made; pier and spandrel
     labels; diaphragms only if that run assigned them (read the flag the request or report carries).
   - It did not touch: loads, load cases, stiffness modifiers, design.
3. **`PublishDiscovery.cs:221` names `--dxf-folder <path>`.**
4. **Rewrite the stack-merge comment** so it states the truth: on by default, `MergeStacksIntoOneLabel`. Keep its
   proof paragraph.
5. **`Write` calls `WriteSheetLedger(workbook, report)`** in the order the remark gives. If the remark and the call
   order disagree, make the remark match the code.
6. **Land without destroying.**
   - Before copying a staged file over an existing target, rename the existing target to
     `<name-without-ext>-v<N><ext>`. `N` is one more than the highest `-v<N>` beside it.
   - `Withdraw` and the `-QUESTIONS-for-Andrea` cleanup park the same way instead of deleting.
   - `StaleOwnedFiles` must not count a parked `-v<N>` file as stale. Read it and exclude them.
   - The landed names (the bare names) are unchanged, so everything that reads them still works.

## Gates (authored, not run)

- **App** (`Kor.Operations.App/EngineeringTools.Tests/`): a source-scan test asserting `DxfToEtabsWindow.xaml.cs`
  contains `RequireRuleSettings = true`.
- **Core** (`Kor.Operations.EngineeringTools.Core.Tests/`):
  - `PublishSummaryNamesWhatItWrote`: the HTML for a report with diaphragms off contains "section" and "mesh" under
    what it wrote, and does not contain "section properties" under what it did not touch.
  - `EveryRefusalNamesARealFlag`: a source scan. Every `--[a-z-]+` token inside a string literal in
    `Core/Dxf/Publish*.cs` and `JobPublisher.cs` is accepted by `PublishVerb.cs`. Collect the accepted set from its
    `Equals("--...")` checks.
  - `TheQuestionsWorkbookHasItsSheetsReadTab`: `ModelQuestionnaire.Write` on a minimal report produces a workbook with
    a "Sheets read" worksheet.
  - `ALandingParksWhatWasThere`: in a temp model folder, land a file twice. Both versions exist, as `x.e2k` and
    `x-v1.e2k`, and nothing was deleted. Run `StaleOwnedFiles` against it and it reports nothing. Extract the park
    step as an internal method if that is what it takes to test it without a build.

## Never read

`Baselines/`, `docs/etabs-handoff/`, any `.e2k`, `.dxf` or `.csv`.

## Read

- `Kor.Operations.App/EngineeringTools/DxfToEtabs/DxfToEtabsWindow.xaml.cs` (whole, 13 KB)
- `Kor.Operations.EngineeringTools.Core/Dxf/PublishSummary.cs` (whole, 8 KB)
- `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs` (whole, 20 KB)
- `Kor.Operations.EngineeringTools.Core/Dxf/PublishDiscovery.cs` 200-230
- `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/PublishVerb.cs` (whole)
- `Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs` 115-130, 2590-2640, 3790-3800
- `Kor.Operations.EngineeringTools.Core/Dxf/ModelQuestionnaire.cs` 1270-1360, 1800-1860

## Response

Files changed, gates added, and one line per defect. Under 60 lines.
