# Business Development and BD Reports — adversarial audit

Date: 2026-10-13  
Audited revision: `3df392b8` (`3df392b8f596038cc4c53a1afdc3a6fc21fbf270`)  
Scope: Business Development and BD Reports, including supporting opportunity, pursuit, enrichment, brief-generation, and export paths.  
Method: Read-only source inspection and cross-path tracing. No builds, tests, application execution, or database queries were performed.  
Finding status: All findings below are open, code-supported failure paths. They are not runtime-verified incidents or measurements of production prevalence. No fixes were made as part of this audit. This report records the audit previously delivered in the conversation.

## Overall assessment

Substantial correctness problems remain. The BD modules and their reports should not yet be treated as an authoritative basis for pursuit decisions. The strongest issues can create pursuits against dismissed opportunities, overwrite newer assessments, silently skip evidence, and present the wrong records as actionable work.

The audit covers the current code after the four BD fix rounds and subsequent changes. It does not assume the original audit's findings are still present. Several corrections are in place, but alternate callers and parallel report implementations do not consistently share them.

Severity indicates potential impact, not observed frequency. **High** findings can persist incorrect state, materially misdirect pursuit decisions, or export the wrong report. **Medium** findings create narrower but substantive misrepresentations. The examples below are reproduction scenarios derived from the code; they were not executed.

## Findings

### BD-A01 — HIGH — Alternate pursuit creation bypasses the dismissal guard

The normal `GrabAsync` path correctly rejects dismissed opportunities. However, when grabbing fails and no engagement exists, `EnsureEngagementForAsync` falls back to `ClaimOwnedAsync`. That SQL checks whether an engagement already exists, then creates one without checking dismissal or validating the opportunity's owner.

**Failure case:** Alice opens an opportunity; Bob dismisses it; Alice promotes her stale row. The guarded grab fails, but the fallback creates Alice's engagement against the dismissed opportunity. An already-owned opportunity can similarly receive an engagement owned by somebody else, leaving ownership inconsistent.

The transaction and unique index prevent duplicate engagement creation; they do not enforce eligibility. The later status update can encounter a concurrency exception that is caught after the engagement has already been created.

Evidence:

- [Fallback caller — OpportunitiesViewModel.cs](../../Kor.Operations.App/Opportunities/OpportunitiesViewModel.cs), around lines 802–840, especially 820.
- [Claim and shared engagement-birth SQL — SqlPursuitGrabStore.cs](../../Kor.Opportunities.Data/Crm/SqlPursuitGrabStore.cs), around lines 82–95 and 143–170.

**Required invariant:** Every engagement-creation path must validate the intended dismissal, status, and ownership rules against current database state in the same transaction as creation.

### BD-A02 — HIGH — On-demand synthesis can overwrite a newer assessment

Synthesis reads the current assessment, makes an external AI request, and subsequently updates whichever honing row is latest. The write does not require the assessment's version or refresh timestamp to match what was read.

**Failure case:** On-demand synthesis reads `PURSUE`; another process records that the project is awarded or dead; the first request returns and overwrites the newer verdict with its older conclusion. It also stamps that conclusion freshly reviewed.

This is a conditional concurrency defect, not evidence that a particular production assessment has already been overwritten.

Evidence: [OnDemandHoningSynthesizer.cs](../../Kor.Opportunities.Data/BdReports/OnDemandHoningSynthesizer.cs), assessment read around line 154 and unconditional persistence around lines 318–347.

**Required invariant:** A synthesis result must only update the assessment version on which it was based; conflicting changes must cause rejection or re-evaluation.

### BD-A03 — HIGH — Synthesis advances its evidence cutoff past unprocessed evidence

The query counts all new signals but supplies only `TOP 12` observations and `TOP 12` actions, without ordering. Successful persistence then sets `LastRefreshAtUtc` to completion time.

**Failure case:** Thirteen actions arrived since the last assessment. The omitted action contains decisive award information. After synthesis, all thirteen precede the new cutoff, so the omitted action is no longer considered new. An action arriving between the read and write can be skipped in the same way.

The cutoff means "the synthesis finished," although subsequent queries treat it as "evidence through this time was considered."

Evidence: [OnDemandHoningSynthesizer.cs](../../Kor.Opportunities.Data/BdReports/OnDemandHoningSynthesizer.cs), signal counts and selection around lines 179–190 and cutoff update at line 342.

**Required invariant:** The evidence-processing cutoff must represent a complete, bounded set of evidence actually considered. Truncation or signals arriving during synthesis must leave unprocessed evidence eligible for later processing.

### BD-A04 — HIGH — Another project's tender evidence can change a project's assessment

Observation matching uses a substring containing the first fourteen characters of the project name. It does not require a resolved project link, matching buyer, or matching geography.

**Failure case:** Two unrelated projects begin "North Vancouver…". A tender observation belonging to one can become new evidence for the other. The resulting synthesis can persist a changed verdict against the wrong project. SQL wildcard characters in project names further weaken the match.

Cross-association follows directly from the query; the resulting verdict depends on the model response.

Evidence: [OnDemandHoningSynthesizer.cs](../../Kor.Opportunities.Data/BdReports/OnDemandHoningSynthesizer.cs), observation matching at lines 180 and 185.

**Required invariant:** Evidence capable of changing a project's assessment must have a defensible association with that project. An ambiguous name-prefix match must not silently establish identity.

### BD-A05 — HIGH — Dismissed projects remain in reports recommending live pursuits

Project dismissal sets `DismissedAtUtc` and clears ownership. It does not retire the inventory row or change its honing verdict. The call-sheet, pursuit-dossier, and attack-card queries select non-retired `PURSUE`/`PURSUE_URGENT` projects without excluding dismissed rows.

**Failure case:** Dismiss a project as "not for us," then regenerate the reports. It can still appear as a live pursuit or urgent outreach candidate.

The write and report queries implement different definitions of actionable work.

Evidence:

- [Dismissal — SqlPursuitLifecycleStore.cs](../../Kor.Opportunities.Data/MajorProjects/SqlPursuitLifecycleStore.cs), around lines 114–130.
- [Report populations — SqlBdReportService.cs](../../Kor.Opportunities.Data/BdReports/SqlBdReportService.cs), call sheet around lines 269–280, dossiers around lines 645–680, and attack cards around line 1083.

**Required invariant:** A dismissed project must not be represented as actionable live work unless the report explicitly identifies and explains that broader population.

### BD-A06 — HIGH — Sector briefs count expired RFPs and completed projects as live or forward work

The sector opportunity filter requires `Status = 1`, prime-RFP classification, and geography, but excludes neither passed deadlines nor dismissal. The sector inventory filter requires non-retirement and geography, but does not restrict project stage or dismissal.

**Failure case:** An expired prime RFP still marked New counts as a "Live RFP"; a completed, non-retired inventory project contributes to "Forward pipeline" and its dollar total.

Region briefs now use stricter shared predicates. Those corrections have not reached the equivalent sector paths, so reports can disagree for structural reasons.

Evidence:

- [Sector filters — SqlBriefDataStore.cs](../../Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs), `BuildSectorMpiFilter` around line 1550 and `BuildSectorOpportunityFilter` around line 1611; sector list readers around lines 1756 and 1786.
- [Region predicates — BdReportPopulationSql.cs](../../Kor.Opportunities.Data/BdReports/BdReportPopulationSql.cs), around lines 17–23.

**Required invariant:** Reports using the same population labels must share compatible eligibility definitions, including deadline, dismissal, and stage rules.

### BD-A07 — HIGH — Pursuit briefs present architect-wide relationships as project seat status

The brief derives its seat status from the architect's `StructuralPartnerMap`. Its project query does not retrieve the project's resolved structural-engineer organization. The screen and PDF nevertheless present that assessment under "Likely prime & structural seat"; the approach context describes recurring partners as incumbents to displace.

**Failure case:** An architect generally rotates structural partners, but this particular project already names a structural engineer. The brief can imply an available seat while ignoring the project-specific appointment.

This is a mismatch between organization-level evidence and a project-level assertion.

Evidence:

- [Brief assembly and project query — SqlPursuitBriefStore.cs](../../Kor.Opportunities.Data/MajorProjects/SqlPursuitBriefStore.cs), around lines 39–73 and 86–116.
- [Displayed claim — PursuitBriefWindow.xaml](../../Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefWindow.xaml), around line 190.
- [PDF claim — PursuitBriefPdfExporter.cs](../../Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefPdfExporter.cs), around line 151.
- [Approach context — PursuitBriefWindow.Approach.cs](../../Kor.Operations.App/BusinessDevelopment/Workspace/PursuitBriefWindow.Approach.cs), around line 129.

**Required invariant:** Project-specific appointments must take precedence over general relationship patterns. The two kinds of evidence must remain visibly distinct.

### BD-A08 — HIGH — Pipeline amounts can silently treat USD as CAD

The repository's `PrimePipeline` view casts `Opportunity.EstimatedValue` directly into `EstimatedValueCad`, without consulting the stored currency or applying conversion. The dashboard sums that field as a Canadian-dollar amount. Opportunity entry supports different currencies.

**Failure case:** A USD 1 million opportunity contributes CAD 1 million to the pipeline total.

This finding applies when the deployed view matches the repository definition. The installed database view was not inspected.

Evidence:

- [View definition — 331_ViewsRespectDismissal.sql](../../Kor.Opportunities.Data/Schema/331_ViewsRespectDismissal.sql), around line 53.
- [Dashboard total — DashboardViewModel.cs](../../Kor.Operations.App/BusinessDevelopment/Workspace/DashboardViewModel.cs), around lines 330 and 374.
- [Currency entry — OpportunityEntryDialog.xaml.cs](../../Kor.Operations.App/Opportunities/OpportunityEntryDialog.xaml.cs), around line 328.

**Required invariant:** Monetary aggregation must preserve currency or use an explicit conversion policy. Renaming a numeric field does not convert its units.

### BD-A09 — HIGH — PDF export can save the previous report under the new filename

Generation commits the new document and its identity before the browser confirms successful rendering. If `NavigateToString` fails, the handler reports an error but leaves PDF export available. Export prints the browser's current content using the current model's filename.

**Failure case:** Report A is displayed; generation of B succeeds; navigation to B fails; Save PDF prints A under B's filename. Saving DOCX produces B, creating contradictory exports from the same apparent selection.

The explicit navigation-failure path establishes the risk without relying solely on a narrow timing race. Actual WebView behavior was not exercised.

Evidence:

- [Document identity update — BdReportsViewModel.cs](../../Kor.Operations.App/BusinessDevelopment/Reports/BdReportsViewModel.cs), around lines 246–250.
- [Preview failure handling and PDF export — BdReportsWindow.xaml.cs](../../Kor.Operations.App/BusinessDevelopment/Reports/BdReportsWindow.xaml.cs), around lines 265–279 and 320–360.
- [Export availability — BdReportsWindow.xaml](../../Kor.Operations.App/BusinessDevelopment/Reports/BdReportsWindow.xaml), around lines 230–231.

**Required invariant:** A PDF export must be bound to the identity of a successfully rendered document. A browser initialized successfully at some earlier time is insufficient.

### BD-A10 — HIGH — Editing a deadline's date can discard its closing time

The opportunity editor loads a timestamp into a date-only picker, then saves a new timestamp using the selected value and the workstation's local offset. There is no corresponding time editor preserving the closing time when the calendar date changes.

**Failure case:** Change a 2 p.m. closing deadline to the following day through the date picker. The selected date can be midnight, which is then persisted as the deadline. BD's dashboard edit path now saves that result.

This finding concerns changing or reparsing the date. Merely opening and saving an untouched value does not necessarily erase its time.

Evidence:

- [Deadline loading and reconstruction — OpportunityEntryDialog.xaml.cs](../../Kor.Operations.App/Opportunities/OpportunityEntryDialog.xaml.cs), around lines 91 and 330–331.
- [Dashboard persistence — DashboardView.xaml.cs](../../Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml.cs), around line 272.
- [Official WPF DatePicker implementation](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/DatePicker.cs), `ParseText` and `SetSelectedDate` behavior. The framework source was consulted; the UI scenario was not executed.

**Required invariant:** Editing a calendar date must preserve or explicitly replace the deadline's closing time and time-zone semantics.

### BD-A11 — MEDIUM — Failed receivables reads become factual zero balances

Deltek loading tolerates an AR query failure by leaving AR unavailable and marking the result degraded. The brief enricher converts that missing value to zero. The HTML brief then displays "AR outstanding $0" and "$0 at 90+."

**Failure case:** Company information loads successfully but the AR query fails. The brief asserts no receivables despite having no balance information. A generic note that "some sections were unavailable" does not identify those monetary assertions as unknown. Contact-load failures have a similar zero-count ambiguity.

Evidence:

- [Partial-read handling — DeltekClientContextService.cs](../../Kor.Operations.App/Crm/DeltekClientContextService.cs), around lines 206–239.
- [Null-to-zero conversion — OrgBriefDeltekEnricher.cs](../../Kor.Operations.App/BusinessDevelopment/Briefs/OrgBriefDeltekEnricher.cs), around lines 32–33.
- [Rendered balances — HtmlBriefPdfGenerator.cs](../../Kor.Operations.App/BusinessDevelopment/Briefs/HtmlBriefPdfGenerator.cs), around lines 485–486 and 509.

**Required invariant:** Unavailable financial or contact data must remain distinguishable from a successfully measured zero, including in exported documents.

### BD-A12 — MEDIUM — Generic engineering roles become confirmed structural appointments

Competitor project selection accepts research roles containing either `structural` or any occurrence of `engineer`. The resulting list is headed "Projects confirmed as structural engineer."

**Failure case:** A competitor's recorded role is "Mechanical engineer." Its project can appear as a confirmed structural-engineering appointment, affecting both the displayed portfolio and ranking.

Deduplication does not repair this classification error.

Evidence:

- [Role predicates — SqlBdReportService.cs](../../Kor.Opportunities.Data/BdReports/SqlBdReportService.cs), around lines 460 and 496.
- [Confirmation wording — CompetitorIntelReportGenerator.cs](../../Kor.Opportunities.Data/BdReports/Generators/CompetitorIntelReportGenerator.cs), around line 58.

**Required invariant:** A claim of a confirmed discipline-specific appointment must be supported by discipline-specific evidence.

### BD-A13 — MEDIUM — "Not urgent" is classified as urgent

`IsUrgent` treats a `PURSUE` record as urgent whenever its narrative contains the substring `URGENT`, ignoring negation or context.

**Failure case:** "No urgent action; wait for funding" becomes an urgent flag, contributes to executive urgency counts, and can enter the call sheet's "THIS WEEK" recommendations.

This is deterministic and requires no missing data or concurrency.

Evidence:

- [Urgency classifier — BdReportModels.cs](../../Kor.Opportunities.Data/BdReports/BdReportModels.cs), around lines 51–61.
- [Call-sheet use — CallSheetReportGenerator.cs](../../Kor.Opportunities.Data/BdReports/Generators/CallSheetReportGenerator.cs), around lines 32–53.
- [Executive counts — ExecSummaryReportGenerator.cs](../../Kor.Opportunities.Data/BdReports/Generators/ExecSummaryReportGenerator.cs), around lines 45–51.

**Required invariant:** Structured urgency claims must not be inferred merely from the presence of a word in free text.

### BD-A14 — MEDIUM — Filtered attack-card PDFs retain the unfiltered total

Typing in the browser filter replaces the card list but leaves the header's total unchanged. PDF export prints that filtered DOM. DOCX intentionally exports the full generated snapshot.

**Failure case:** Generate 100 cards, filter down to one, and export PDF. The document contains one card beneath a "100 live pursuits" heading. The corresponding DOCX contains all 100.

Exporting a filtered view can be legitimate, but its title, count, and scope need to describe that view. The DOM inconsistency is visible in source; PDF output was not rendered during this audit.

Evidence:

- [Header and filtering — BdVisualHtml.cs](../../Kor.Opportunities.Data/BdReports/Generators/BdVisualHtml.cs), around lines 166–182; full-snapshot Word behavior around lines 53–65.
- [Browser-based PDF export — BdReportsWindow.xaml.cs](../../Kor.Operations.App/BusinessDevelopment/Reports/BdReportsWindow.xaml.cs), around line 354.

**Required invariant:** Exported population labels and totals must describe the records actually included, with an explicit policy for browser filters.

### BD-A15 — MEDIUM — Award matches establish neither KOR involvement nor consistent geography

Award-program matching considers any non-retired inventory project without requiring KOR involvement. Regions including `US` and `Canada` bypass geographic restrictions entirely, whereas several narrower regions lack matching branches.

**Failure case:** A US award program can select a BC project performed by another firm and display it in the "Light KOR-project match" column. The footer acknowledges a rough hint, but the query does not consistently establish even that hint's stated geography.

Evidence:

- [Matching query — SqlBdReportService.cs](../../Kor.Opportunities.Data/BdReports/SqlBdReportService.cs), around lines 782–799.
- [Report label and qualification — AwardProgramsReportGenerator.cs](../../Kor.Opportunities.Data/BdReports/Generators/AwardProgramsReportGenerator.cs), around lines 29 and 74.

**Required invariant:** A KOR-project match must establish KOR involvement, and a geographic eligibility hint must actually enforce its stated geography.

## Corrections observed in the current code

The prior fixes improved the code. The direct grab dismissal guard, stricter region populations, full-snapshot attack-card DOCX export, and measured billing-period freshness are in place.

The old hard-coded billing-freeze allegation is **not** carried forward. The current revision measures the billed posting period instead of asserting the earlier freeze date. This audit did not independently measure the live billing data.

The remaining defects often occur in alternate callers or parallel report implementations that do not share those corrections. Passing tests for the corrected paths do not establish the invariants for these other paths.

## Recommended repair order

1. Enforce pursuit eligibility across every creation path, add concurrency checks around assessment writes, and tie evidence cutoffs to the evidence actually processed. Resolve ambiguous evidence identity before allowing it to change persisted assessments.
2. Share definitions of live and forward work across report families. Use project-specific seat evidence and preserve monetary units and missingness.
3. Bind PDF export to a successfully rendered document identity and define filtered-export scope. Preserve closing time when editing deadlines.
4. Correct discipline, urgency, and award-match classification so report wording does not claim more than the evidence establishes.

## Suggested acceptance gates — not executed

The following are proposed regression scenarios, not passing tests or verified fixes:

- Concurrent dismissal and promotion, including the fallback claim path and an opportunity owned by a different user.
- A competing assessment writer changing the honing result while an on-demand model request is in flight.
- Thirteen new signals, with decisive evidence outside the selected twelve; separately, a signal arriving between classification and persistence.
- Two unrelated projects sharing the same first fourteen name characters, plus names containing SQL wildcard characters.
- Dismissed, expired, and completed records passed through each relevant report population.
- An architect-wide open/rotating relationship assessment alongside a project-specific structural appointment.
- A USD opportunity included in a CAD pipeline aggregate.
- A successful report A preview followed by report B generation and failed browser navigation, then PDF export.
- A deadline with a non-midnight closing time edited to another calendar date, including a workstation in a different time zone.
- Successful company reads combined with failed AR or contact reads.
- A competitor research role of "Mechanical engineer" and a pursuit narrative saying "No urgent action."
- An attack-card filter reducing the displayed population before PDF and DOCX export.
- An award program with US eligibility and candidate projects outside the US or without KOR involvement.

Where a repair is subsequently claimed as verified, its gate should first demonstrate failure against the pre-fix code and then pass against the fix. Existing passing tests do not by themselves establish the guarantees above.

## Verification limits and workspace state

This was a source-level adversarial audit, not an exhaustive proof of correctness or a production incident investigation. Database contents, deployed schema, actual concurrency frequency, and rendered PDFs remain unverified. No production incident counts or live data-quality rates are asserted.

No application files were changed during the audit. Repository status at completion contained only these pre-existing untracked files, which were left untouched:

- `docs/codex/CODEX-VEEAM-CLOUD-ORPHAN-DELETE-RUNBOOK.md`
- `docs/codex/CODEX-VEEAM-CLOUD-ORPHAN-DELETE-WEDGE.md`
- `tools/RelevanceGateDiff/candidate-calgary-vocabulary.txt`

This Markdown report was added afterward in response to the user's request for a saved report. Source line references describe the audited revision and may move in later revisions.
