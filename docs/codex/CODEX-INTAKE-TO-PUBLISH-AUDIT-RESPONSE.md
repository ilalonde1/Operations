# CODEX-INTAKE-TO-PUBLISH-AUDIT - RESPONSE

Scope followed: no `dotnet build`, no `dotnet test`, no publish, no commit, no writes to `\\Kor-fs01`. Share access was read-only and limited to named 31168 artifacts.

## Four Questions

1. Are we extracting everything the intake gives us? Verdict: no new geometry extraction blocker found beyond the known 16; main structural line layers are read, unsupported structural entities are reported, but column/schedule text remains an unproven lead.
2. Are we using everything correctly and efficiently? Verdict: mostly yes on the current `*.dxf` intake path, but publish can still derive building split reach from fallback rules while the model build uses required DB rules.
3. What input best supports a meaningful ETABS model? Verdict: the current publish set is materially usable, but one shipped explainer contradicts the live reports/workbooks and tells the engineer nothing is waiting.
4. Are we banking knowledge so the next publish is better? Verdict: rules are load-bearing in the main build, but one pre-build reach path can bypass them and several coverage gates still turn missing share/reference state into a green return.

## Findings

### BLOCKING

None new in this pass. I did not find a fresh, non-duplicative defect that proves the shipped `.e2k` geometry is unusable independent of the already recorded 16 findings.

### SERIOUS

1. General read-first PDF says there is nothing waiting while the shipped 31168 job artifacts have two `NEEDS YOU` rows.

File: `\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\KOR-Model-From-Drawings-READ-THIS-FIRST.pdf:text lines 24-26`, `KOR-31168-SUMMARY.pdf:text lines 43-44`, `KOR-31168-TOWERS-SUMMARY.pdf:text lines 35-36`, `31168-FROM-DRAWINGS-report.txt:10`, `31168-TOWERS-FROM-DRAWINGS-report.txt:10`, `Kor.Operations.EngineeringTools.Core/Dxf/PublishExplainers.cs:121`.

Triggering input: the current 31168 publish artifacts on the share. Both model reports say `Questions for you: 2`; both model-specific summary PDFs say two rows are marked `NEEDS YOU`.

Wrong output: `KOR-Model-From-Drawings-READ-THIS-FIRST.pdf` says the `...-QUESTIONS.xlsx` judgements are already decided and that there is nothing waiting on the engineer. That is not a model-count mismatch, so the current explainer gate checks PDF readability and numeric count claims but does not reject this false workflow claim.

2. Per-building publish reach can silently fall back to built-in rules before the model build uses required standards DB rules.

File: `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:256`, `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:259`, `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:409`, `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:415`, `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:418`, `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:430`, `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:441`, `Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:635`.

Triggering input: `takeoff publish` with `--tower` or `--per-building` when `PlanRulesFor()` hits a transient standards DB failure on its first rule load, followed by a successful main model build rule load.

Wrong output: `PublishPlan.ForBuildings()` receives `ReachByStorey()` results classified with default `PlanClassificationOptions`, while `DxfToEtabsService.Run()` later builds the actual model with `LoadRequired()` DB-backed rules. A persistent DB failure refuses the model build, so the bad case is divergence/transience, not total outage. The consequence is a building/tower drop-storey plan computed by different rules than the model being published, with no warning.

3. Coverage/agreement tests still report success by returning when the share or reference inputs are unavailable.

File: `Kor.Operations.EngineeringTools.Core.Tests/GeneratedModel.cs:69`, `Kor.Operations.EngineeringTools.Core.Tests/ModelCoverageTests.cs:72`, `Kor.Operations.EngineeringTools.Core.Tests/ModelCoverageTests.cs:134`, `Kor.Operations.EngineeringTools.Core.Tests/ModelCoverageTests.cs:189`, `Kor.Operations.EngineeringTools.Core.Tests/ModelCoverageTests.cs:294`, `Kor.Operations.EngineeringTools.Core.Tests/ModelCoverageTests.cs:416`, `Kor.Operations.EngineeringTools.Core.Tests/ModelCoverageTests.cs:637`, `Kor.Operations.EngineeringTools.Core.Tests/StackMergeChangesNothingButLabelsTests.cs:74`, `Kor.Operations.EngineeringTools.Core.Tests/ShippedModelsAgreeWithEachOtherTests.cs:50`, `Kor.Operations.EngineeringTools.Core.Tests/ShippedModelsAgreeWithEachOtherTests.cs:274`, `Kor.Operations.EngineeringTools.Core.Tests/ShippedModelsAgreeWithEachOtherTests.cs:284`, `Kor.Operations.EngineeringTools.Core.Tests/ShippedModelsAgreeWithEachOtherTests.cs:337`.

Triggering input: test run on a machine without `\\Kor-fs01`, without the expected DXF cache, or without the named reference model.

Wrong output: tests that are meant to prove drawing/model agreement can pass while checking no generated model or no shipped model. This is a gate integrity defect, not a geometry defect.

### MINOR

1. Landed reports still say the model was written to the temp staging path.

File: `\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\31168-FROM-DRAWINGS-report.txt:1`, `31168-TOWERS-FROM-DRAWINGS-report.txt:1`, `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:116`, `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:126`, `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:215`.

Triggering input: normal landed publish from the temp stage to the project model folder.

Wrong output: the report beside the delivered `.e2k` says `Model written : C:\Users\ilalonde\AppData\Local\Temp\...` instead of the landed share path the engineer opens. It does not corrupt geometry, but it is wrong provenance in the delivered packet.

## LEADs

- The local 31168 DXF cache contains 3,806 `MTEXT` entities on `JBP_TAG_COL-1`. The current ETABS path reads positioned tags, but I did not prove a consumer maps column tag text or schedules into ETABS column sections. This is not a finding yet because columns are currently sized from drawn footprint geometry.
- Schedule readers exist elsewhere in core, but I did not find evidence in this pass that the DXF-to-ETABS publish path consumes footing/column/wall schedule data. Wall strength is already a known finding; do not duplicate it unless a new schedule-dependent wrong output is isolated.
- The bridge export still has no durable manifest in the published/cache folder for after-the-fact SafeName collision or export-option audit. This confirms the known observability gap but is not re-reported above as a new finding.
- The current DXF layer inventory showed large ignored layers dominated by dimensions, hatches, revclouds, notes, parking, furniture, doors/windows and architectural/fill pattern linework. I did not find a new silent structural layer loss beyond the already recorded wall/opening/slab findings.
