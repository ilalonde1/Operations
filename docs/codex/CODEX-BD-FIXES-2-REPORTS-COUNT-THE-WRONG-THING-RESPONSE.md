# BD fixes 2 - applied source changes

2026-10-13. All six requested fixes are applied as source. **No build, test,
database tool, migration, deployment or service action was run.** Gates are authored,
not verified; neither pre-fix failure nor post-fix success has been executed.

Prior art searched: `tools/`, `docs/`, `../KOR.Drafter/db/tools/`, and the App's
`BdReports` tests. Reused the existing report generators, DOCX renderer and
`tools/BdIntegrityCheck`; no replacement harness or data repair tool was created.

## Changes and regression boundaries

| Finding | Applied behavior | EXPECTED vs ACTUAL gate source |
|---|---|---|
| 3 | One canonical verdict vocabulary for synthesis, parsing, rank and urgency; SQL normalizes case before grouping. Synthesis rejects unknown tokens and foreign IDs, accepts `PURSUE_URGENT`, deduplicates returned IDs, and counts acknowledged writes only. | `BdReportMeaningRegressionTests`: legacy root/nested verdict parsing, actual sector sections, and a persistence delegate returning success/failure. Source gate checks affected-row acknowledgement. No SQL/HTTP execution; cannot establish factual correctness of a verdict. |
| 9 | Call sheet, dossier, executive/sector totals and architect values retain the known-cost subtotal and disclose cost coverage. Visual heat retains its weighted figure and labels it approximate, with distinct-project coverage for the pursuit pool and per-org coverage in hover text. Unknown costs contribute no heat; visual org values overlap. | DOCX text/KPI assertions for mixed and all-unknown inputs; executive/sector populations deliberately differ; visual HTML assertions. Excludes browser/PDF layout and cost-query execution. Incorrect source costs would escape. |
| 10 | Architect ranking remains a linked portfolio ranking including historical/delivered work. Figures and samples remain; active-pipeline and inferred open-seat claims are removed from that ranking. Legacy model property names are documented rather than breaking consumers. | Exported DOCX must retain a delivered-project sample, portfolio count/value and coverage, and must not label it active projects. Does not test SQL membership/deduplication; duplicate portfolio membership would escape. |
| 11 | Architect, competitor, dossier, attack-card/visual contact counts and linked-org brief summaries use distinct live people with current, nonretired affiliations. No identity resolver changes. | `BdIntegrityCheck`: independent EXISTS-based expected counts versus the report's shared population, plus SQL VALUES fixtures for duplicate/former/retired roles. Counts mismatched organisations, not affiliation rows. A duplicate real person under two IDs would escape. App source gate checks reader wiring. |
| 12 | Region count/list share the same eligibility predicates. Live RFPs require an undismissed New prime RFP with a deadline strictly after one captured instant; unknown deadlines are excluded. Forward projects use CapitalPlan/FacilityRenewal in both queries. DOCX/PDF/HTML headings disclose these populations and top-five lists. | `BdIntegrityCheck`: distinct-ID population comparisons and VALUES fixtures for dismissal, expiry (including equality), missing deadline, status, prime flag, province, stage and retirement. Source gate checks both query uses. Excludes city tokenisation, concurrency and real-world stage validity; a wrong city match would escape. |
| 14 | Strategic counts explicitly describe retrieved samples, not totals or verified live pursuits; each detail section says how many retrieved matches are shown. Six Safe helpers and the action-rollup recommendation clip append `... [abridged]`. | Exported strategic DOCX retains the count 12 while saying sample and 10-of-12 shown; six helper gates verify clips are marked and complete text is preserved. Excludes keyword-match quality and clips outside those boundaries. |

People fixtures and region fixtures are SELECTs over CTE VALUES, not data writes.
The new live invariants consume the shared production population fragments; they
do not prove that an older deployed binary is using the corrected reader. The
noncanonical-verdict invariant is a WARN worklist, not a post-fix-zero claim.

## Existing data left alone

These are the prior audit's measurements, **not new measurements**:

- 88 active project rows with lowercase verdicts: 87 `pursue`, one `monitor`.
  Readers now normalize them; persisted data has not been repaired.
- 103 of 270 pursuit projects had numeric costs; 167 were unknown. Unknown is
  incomplete information, not evidence for a zero-valued project.
- 21 nonretired architect-linked inventory rows were delivered/opened. Historical
  portfolio membership remains legitimate; those rows are not deleted or restaged.
- 13 of the top 20 architect counts were inflated. The 273 org groups / 491 excess
  affiliation rows were an exposure measure, not a reason to delete role history.
- BC's prior live-RFP group included five dismissed rows. New headline values have
  not been measured; the future-deadline requirement may exclude additional rows.
- Graham/EllisDon had 21/24 matches before TOP 12. The fix labels the retained
  samples; it does not claim to have fetched current full totals.

## Files touched (26, including this response)

```text
Kor.Opportunities.Data/BdReports/BdReportModels.cs
Kor.Opportunities.Data/BdReports/BdReportFormatting.cs
Kor.Opportunities.Data/BdReports/BdReportPopulationSql.cs
Kor.Opportunities.Data/BdReports/HoningResultParser.cs
Kor.Opportunities.Data/BdReports/OnDemandHoningSynthesizer.cs
Kor.Opportunities.Data/BdReports/SqlBdReportService.cs
Kor.Opportunities.Data/BdReports/Generators/ArchitectFrequencyReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/AwardProgramsReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/BdVisualHtml.cs
Kor.Opportunities.Data/BdReports/Generators/CallSheetReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/ExecSummaryReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/PrimeConsultantReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/PursuitDossierReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/SectorReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/StrategicRelationshipsReportGenerator.cs
Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs
Kor.Operations.App/BusinessDevelopment/Briefs/BriefGenerator.cs
Kor.Operations.App/BusinessDevelopment/Briefs/BriefPdfGenerator.cs
Kor.Operations.App/BusinessDevelopment/Briefs/HtmlBriefPdfGenerator.cs
Kor.Operations.App/BusinessDevelopment/Reports/BdReportsViewModel.cs
Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/BdReportMeaningRegressionTests.cs
Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/ExecSummaryReportGeneratorTests.cs
Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/PursuitDossierReportGeneratorTests.cs
Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/SectorReportGeneratorTests.cs
tools/BdIntegrityCheck/Program.cs
docs/codex/CODEX-BD-FIXES-2-REPORTS-COUNT-THE-WRONG-THING-RESPONSE.md
```

Read back edits and inspected their diffs. `git diff --check` reported no whitespace
errors. Unrelated NetworkOps edits, Veeam documents and the Calgary vocabulary file
were left untouched. Build/test and red/green gate validation remain with Claude;
database execution and any existing-data repair remain with Ian.
