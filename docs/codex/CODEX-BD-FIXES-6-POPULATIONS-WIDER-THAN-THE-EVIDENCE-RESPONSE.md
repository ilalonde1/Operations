# BD fixes 6 — populations wider than the evidence

Applied 2026-10-13. **Source changes and regression sources only; not build/test verified.**
No builds, tests, application/model calls, database tools, migrations, data changes, deployments
or service actions were run. Source/diff review completed; `git diff --check` passed.

## Applied changes

- **BD-A05:** Added the shared `LivePursuit` SQL predicate: non-retired, undismissed and structured
  PURSUE/PURSUE_URGENT. Call sheets, dossiers, attack cards, the shared graph/treemap population
  and dossier signal selection now use it.
- **BD-A06:** Region and sector readers share the same qualified eligibility builders. Live
  prime RFPs require New status, no dismissal and a recorded deadline strictly after the read's
  captured `@asOf`. Forward projects require no retirement/dismissal and CapitalPlan or
  FacilityRenewal stage. Sector counts, costs, project lists and related inventory-based
  rankings use that forward slice. Sector documents disclose the selection; their KOR subset
  is explicitly labelled “Recorded KOR structural projects in the forward slice,” rather than
  implying an unrestricted historical track record.
- **BD-A07:** The pursuit store fetches the project's resolved structural organization, name
  and kind, with an unresolved source-name fallback. Screen, PDF and approach context show
  that appointment first. The architect's partner pattern is separately labelled as not
  project appointment evidence. Unknown/unresolved project seats are not inferred open, KOR's
  own appointment is identified, and partner research is not labelled confirmed project
  competition. The approach instructions also prohibit inferring displacement from a pattern.
- **BD-A12:** Both competitor ranking and detail queries use an explicit structural-role
  vocabulary. Generic engineering, negated and ambiguous roles are excluded. The document
  says “recorded structural role (may be historical)” instead of “confirmed as structural.”
- **BD-A13:** Only the normalized structured PURSUE_URGENT verdict establishes urgency. Free
  text, including “No urgent action; wait for funding,” cannot promote a PURSUE row to urgent.
  Existing tests that expected the old substring behavior were updated.
- **BD-A15:** Award candidates require a non-retired project with an active resolved
  KorStructural organization recorded as its structural engineer. Canada and US restrict to
  their respective subdivision codes; exact supported provincial/state regions match exactly.
  International is unrestricted; NorthAmerica supports the Canadian/US codes represented by
  the matcher. Missing or unrecognized region labels do not match. The column and methodology
  describe a recorded KOR structural project with a region match, not verified award eligibility.

## Supplied measurements — not remeasured

The brief supplied these live-database measurements from 2026-10-13:

| Finding | Supplied measurement |
|---|---|
| BD-A05 | Dismissed, non-retired projects with latest PURSUE/PURSUE_URGENT verdict: **0 of the active set**. This does not establish that the pre-fix path was correct. |
| BD-A06 | Dismissed New prime RFPs still eligible for the old sector population: **8 of 69**. Expired-but-New: **0**. |

No other live population sizes or incident frequencies are asserted.

## Gates authored, not executed

`BdEvidencePopulationRegressionTests` covers production SQL wiring, the real urgency classifier,
project appointment display and approach context, and generated competitor/award report wording.
Each class states its coverage and limitations. It does not instantiate the WPF window, render
PDFs, execute SQL or validate real-world identity and research accuracy.

The added `BdIntegrityCheck` entries follow the existing count/sample convention. Controlled
CTE/VALUES fixtures feed the actual shared SQL predicates rejected rows, even if production is
clean. They perform no database mutations when eventually executed:

| Gate | EXPECTED versus ACTUAL comparison |
|---|---|
| `bd_live_pursuit_fixture` / `bd_live_pursuit_population` | Only undismissed, non-retired PURSUE/PURSUE_URGENT records pass the production predicate; compare entity eligibility/counts with explicit expectations. |
| `bd_sector_eligibility_fixture` / `bd_sector_live_rfp_population` / `bd_sector_forward_population` | Reject dismissal, expiry (including the exact boundary), unknown deadline, wrong status, non-prime, completed/non-forward stage, retirement and wrong geography. Region fixtures were extended for forward dismissal as well. |
| `bd_structural_role_fixture` / `bd_competitor_structural_role_population` | Accept explicit structural roles; reject Mechanical engineer, Civil engineer, Engineer, No structural role, Non-structural engineer, unknown and unrelated roles. |
| `bd_award_project_match_fixture` / `bd_award_project_population` | Require KOR involvement plus compatible geography; fixtures include US/Canada reversal, AB, unknown region/code, absent/other/retired organization and retired project. Live candidate-pair checks cover involvement and geographic contradictions. |

There is **no executed pre-fix failure or post-fix pass claim**. Claude owns the Debug build and
targeted App regression execution; Ian owns database checks. The SQL fixtures must still be run
and negative controls verified before these fixes are called runtime-verified.

Limits: a wrong upstream project FK, a fabricated but correctly classified role/verdict, or a
delivered project incorrectly labelled CapitalPlan can still pass. SQL source-wiring checks
do not prove deployed query behavior. The structural-role allowlist deliberately trades recall
for explicit discipline evidence. Award matching deliberately excludes other forms of possible
KOR involvement and unsupported region spellings; it does not establish completion-year,
category or other award eligibility. The live award companion is narrower than the complete
geography vocabulary; controlled fixtures supply its negative cases.

## Files touched

- `Kor.Opportunities.Data/BdReports/BdReportPopulationSql.cs`
- `Kor.Opportunities.Data/BdReports/BdReportModels.cs`
- `Kor.Opportunities.Data/BdReports/SqlBdReportService.cs`
- `Kor.Opportunities.Data/BdReports/Generators/AwardProgramsReportGenerator.cs`
- `Kor.Opportunities.Data/BdReports/Generators/CompetitorIntelReportGenerator.cs`
- `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs`
- `Kor.Opportunities.Data/MajorProjects/PursuitBrief.cs`
- `Kor.Opportunities.Data/MajorProjects/SqlPursuitBriefStore.cs`
- `Kor.Operations.App/BusinessDevelopment/Briefs/BriefGenerator.cs`
- `Kor.Operations.App/BusinessDevelopment/Briefs/BriefPdfGenerator.cs`
- `Kor.Operations.App/BusinessDevelopment/Briefs/HtmlBriefPdfGenerator.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefPdfExporter.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefViewModel.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefWindow.Approach.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefWindow.xaml`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/BdEvidencePopulationRegressionTests.cs` (new)
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/BdReportMeaningRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/BdVerdictsTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/CallSheetReportGeneratorTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/SectorReportGeneratorTests.cs`
- `tools/BdIntegrityCheck/Program.cs`
- `docs/codex/CODEX-BD-FIXES-6-POPULATIONS-WIDER-THAN-THE-EVIDENCE-RESPONSE.md` (this report)

The other nine findings, entity identity/merging and pre-existing unrelated untracked files
were left untouched. No commit or deployment was performed.
