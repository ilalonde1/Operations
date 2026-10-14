# BD fixes 3 - source changes applied

2026-10-13. All six requested changes are applied. **No build, test, database tool,
data repair, migration, deployment or service action was run.** Regression gates
are authored but unexecuted; pre-fix failure and post-fix success are not verified.

Prior art searched: `tools/`, `docs/`, `../KOR.Drafter/db/tools/`, the existing
BdReports/BusinessDevelopment tests, Deltek service, pursuit snapshot/exporter,
Relationships and Briefs Maker entry points. Reused the existing Deltek capability,
report generators and BdIntegrityCheck. No retry/cache/resilience framework added.

## Applied behavior and gates

| Finding | Change | Regression source and limits |
|---|---|---|
| 6 | Pursuit Brief distinguishes not retrieved, not linked, available, partial and unavailable. Successful empty reads use source-scoped absence text; exceptions and null/degraded results do not. Overall load status discloses incomplete context. Both org-export entry points use one shared Deltek enrichment step and preserve unavailable/no-link explanations. | `BdMissingEvidenceRegressionTests`: injected successful-empty, thrown and degraded Deltek reads plus owner-history failure; asserts differing user-visible/PDF-snapshot strings. Shared enrichment gate checks actual DOCX text on failure and source wiring for both entry points. Excludes PDF/WPF rendering, ODBC execution and source identity; misattributed source data would escape. |
| 7 | Visuals label absent structural edges as seat status unknown, recorded SE links as recorded firms, and the contact colour/ring as research-contact presence. Contact presence explicitly does not establish a KOR relationship. Dossier prose no longer equates intel depth with a warm path. | Visual HTML gate rejects the old open-seat/we-know assertions and requires unknown/research-contact labels. Excludes browser layout and real-world relationship accuracy; an incorrectly linked firm would escape. No falsely-warm population is asserted. |
| 13 | Quiet cache checks no longer write LastRefreshAtUtc. Sector refresh results disclose cached, quiet, unsynthesized/deferred or unavailable review; cancellation propagates. Report provenance separates generation time from stored honing dates and explicitly says inventory source as-of dates are unavailable in these inputs. | Source gate rejects the quiet-touch method and an extra timestamp assignment. Rendered sector HTML must carry a refresh-unavailable note and its old stored date. Excludes live SQL/LLM behavior; another provider falsely stamping dates would escape. Existing timestamps are not repaired or claimed trustworthy. |
| 16 | Executive universal-verification assertions are replaced with input-scoped counts and limits. Fixed executive/sector guidance is labelled background/historical and not validated against current rows. Opportunity DOCX/PDF/HTML share an assessment that does not infer current procurement, fit or seat availability from an ID/deadline/classifier score. | Empty executive HTML and old opportunity assessment text must expose limits rather than current/verified assertions. Excludes factual correctness of supplied fields and browser/PDF layout; an incorrect upstream verdict would escape. Historical figures remain with qualification. |
| 19 | Prime relationship assessment is nullable. Boolean true/false and their string forms are recognized; missing/invalid values remain unknown. Reports retain retrieved rows, count firm identification separately, and distinguish usable firm/relationship assessments from partial/unusable payloads. Unknown prime is a research gap, not evidence that a prime has not been selected. | Parser and rendered-report gates cover malformed/empty/partial payloads, explicit false, string true and invalid type. BdIntegrityCheck adds a WARN worklist counting distinct active project IDs with incomplete payloads. Excludes research truth; an invented plausible firm would escape. |
| 20 | Admin maps any null-ended run to running; only ended runs map to ok/failed. | Gate invokes the actual Admin mapper for running/succeeded/failed combinations and compares displayed status. Does not prove worker liveness; an abandoned null-ended run still displays running. |

The tests assert EXPECTED versus ACTUAL text/count/status, rather than merely
asserting no exception. Each gate documents its coverage, exclusions and a
same-class fault it would miss. The SQL worklist is a data companion, not proof
of a deployed reader's behavior or a post-fix-zero claim.

## Data and verification limits

No new population measurements were made. The audit's 245 of 270 cards without a
resolved SE establish unsupported open-seat labels, not that those seats are
occupied. No count of false relationship absences or falsely warm organisations
can be inferred. The prior 148 non-null prime-research rows had zero missing firm
or known-to-KOR fields; that is not evidence of a current missing-field incident,
nor proof that every field's type or content was valid. The prior latest-50 run
sample had zero in-flight unsuccessful rows; no ingestion was started here.

Read back edits, reviewed diffs and ran `git diff --check` (no whitespace errors).
Builds/tests and red/green verification remain with Claude; database execution
remains with Ian. Existing Veeam documents and Calgary vocabulary file were left
untouched.

## Files touched (29, including this response)

```text
Kor.Operations.App/BusinessDevelopment/Briefs/BriefGenerator.cs
Kor.Operations.App/BusinessDevelopment/Briefs/BriefPdfGenerator.cs
Kor.Operations.App/BusinessDevelopment/Briefs/BriefsMakerWindow.xaml.cs
Kor.Operations.App/BusinessDevelopment/Briefs/HtmlBriefPdfGenerator.cs
Kor.Operations.App/BusinessDevelopment/Briefs/OrgBriefDeltekEnricher.cs
Kor.Operations.App/BusinessDevelopment/Reports/BdReportsViewModel.cs
Kor.Operations.App/BusinessDevelopment/Workspace/AdminViewModel.cs
Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefViewModel.cs
Kor.Operations.App/BusinessDevelopment/Workspace/RelationshipsView.xaml.cs
Kor.Operations.App/CompositionModules/OpportunitiesModule.cs
Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/ExecSummaryReportGeneratorTests.cs
Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/PrimeConsultantReportGeneratorTests.cs
Kor.Operations.App/Kor.Transmittals.App.Tests/BdReports/SectorReportGeneratorTests.cs
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdMissingEvidenceRegressionTests.cs
Kor.Opportunities.Data/BdReports/BdReportFormatting.cs
Kor.Opportunities.Data/BdReports/BdReportModels.cs
Kor.Opportunities.Data/BdReports/Generators/ArchitectFrequencyReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/BdVisualHtml.cs
Kor.Opportunities.Data/BdReports/Generators/CallSheetReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/ExecSummaryReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/PrimeConsultantReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/PursuitDossierReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/SectorReportGenerator.cs
Kor.Opportunities.Data/BdReports/Generators/StrategicRelationshipsReportGenerator.cs
Kor.Opportunities.Data/BdReports/OnDemandHoningSynthesizer.cs
Kor.Opportunities.Data/BdReports/PrimeConsultantParser.cs
Kor.Opportunities.Data/BdReports/SqlBdReportService.cs
tools/BdIntegrityCheck/Program.cs
docs/codex/CODEX-BD-FIXES-3-MISSING-DATA-PRESENTED-AS-FACT-RESPONSE.md
```
