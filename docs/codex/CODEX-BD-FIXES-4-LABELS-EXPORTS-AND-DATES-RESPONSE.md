# BD fixes 4 — labels, exports and dates

Applied 2026-10-13. **Source changes and regression source only; not build/test verified.**
No build, test, application, database tool, deployment or service action was run.
`git diff --check` passed. Ingestion, timestamp parsing/storage, `RfpReleaseDate`,
entity resolution and upstream billing sources were not changed.

## Changes

1. **Finding 8:** DOCX and fallback PDF now say **Lifetime fee**, matching the HTML
   PDF path. All three label project billing **Billed (historical)** and disclose
   the `PRSummaryMain` freeze since **2026-02-17**. The brief section distinguishes
   unavailable queries from known stale billing; successful queries still produce
   `DegradedSections = true` because billing is stale. Fee and AR keep their own
   meanings and are explicitly distinguished from that frozen source.
2. **Finding 15:** Attack Cards DOCX now uses the existing Word block renderer for
   **every card in the generated snapshot**, including cards below the viewport
   and cards hidden by filtering. HTML and Word consume the same serialized
   snapshot; no second query is made. Word uses a text layout, disclosed in the UI
   and document. This is a small cards-specific adapter, not a visual renderer
   rewrite. Graph/treemap screenshot exports are labelled current viewport
   snapshots. The PDF printing path is unchanged.
3. **Finding 17:** Deadline display uses the **stored wall clock plus explicit
   numeric offset**, labelled “stored offset”; no workstation conversion or
   inferred buyer time zone. Briefs, Dashboard rows/month grouping, opportunity
   rows and context, Bazaar, historical details and competition grids follow that
   policy. Elapsed-time countdowns show days/hours, hours/minutes, minutes,
   less than one minute, due now, or expired. At 36 hours both consumers say
   **1 day 12 hr remaining**. The entry dialog shows the canonical stored-offset
   timestamp and explicitly labels its existing date editor as workstation-local;
   its existing save/conversion behavior is deliberately unchanged under the
   brief's prohibition on timestamp-storage changes.
4. **Finding 18:** Shared structural-seat prose recognizes resolved
   `Kind = KorStructural` as KOR's own recorded seat. An unresolved name gets an
   unconfirmed-identity statement, not a competitor claim. Resolved other firms
   retain the competitive callout. No name matching or identity rules were added.

Prior art searched: `tools/`, `docs/`, `../KOR.Drafter/db/tools/`, the existing brief
renderers, visual exports, date consumers and regression suite. Reused
`DocxBuilder`, `BdReportDocument`, the existing App test project, OpenXML and the
already referenced PdfPig library. The supplied audit already documents the
source freeze, clipping trigger, date discrepancy and KOR-seat example; none of
its live counts were remeasured.

## Authored regression gates

`Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPresentationRegressionTests.cs`
contains EXPECTED versus ACTUAL assertions. Initial regression source was added
before implementation; artifact and caller coverage was then expanded during
source review. **Neither pre-fix failure nor post-fix success was executed or
claimed. Claude must compile/run and establish that evidence.**

| Gate | Expected versus actual | Exclusions / same-class fault it can miss |
|---|---|---|
| `FeesAreNotBilled_AndOnlyBilledSourceIsFrozen` | 100,000 fee / 20,000 billed fixture in generated DOCX, production HTML content and extracted fallback PDF text; fixed source/date disclosure; successful-but-degraded state without a query-failure claim | No live financial reconciliation or browser-produced PDF; browser printing could hide a correct HTML disclosure |
| `CardsDocxContainsWholeSnapshot` | Mock service → actual report VM → generated DOCX contains each of 270 distinct cards, full-population label and KOR-seat text; window uses snapshot dispatch only for remaining visual snapshots | No browser interaction, Word pagination or PDF rendering; a field omitted from both Word and preview can escape |
| `DeadlinesUseStoredOffsetInBothConsumers` | Both brief formatters, Dashboard row factory, opportunity row and WPF converter agree across both sides of spring/fall DST, UTC midnight and a half-hour offset; countdown and expired/missing cases | No ingestion, storage or UI screenshot execution; clipped display text can escape |
| `DeadlineBindingsUseSharedDisplay` | Source wiring for historical/competition grids, entry readout and Dashboard countdown | No WPF instantiation; broken resource lookup can escape |
| `StructuralSeatDoesNotMakeKorItsOwnCompetitor` | KOR/other/unresolved prose; generated project DOCX, production HTML content and extracted fallback PDF exclude self-competition | No browser-produced PDF or entity-resolution verification; incorrect upstream Kind or stale membership can escape |

These are presentation defects reproducible from in-memory inputs. No database
invariant is necessary to exercise their failure conditions, so the existing
`tools/BdIntegrityCheck/Program.cs` was not extended or run.

## Files touched (23)

Paths are relative to the repository root.

- `Kor.Operations.App/BusinessDevelopment/Briefs/BriefGenerator.cs`
- `Kor.Operations.App/BusinessDevelopment/Briefs/BriefPdfGenerator.cs`
- `Kor.Operations.App/BusinessDevelopment/Briefs/HtmlBriefPdfGenerator.cs`
- `Kor.Operations.App/BusinessDevelopment/Reports/BdReportsViewModel.cs`
- `Kor.Operations.App/BusinessDevelopment/Reports/BdReportsWindow.xaml.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/BazaarView.xaml`
- `Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml`
- `Kor.Operations.App/BusinessDevelopment/Workspace/DashboardViewModel.cs`
- `Kor.Operations.App/Opportunities/CompetitionInfoView.xaml`
- `Kor.Operations.App/Opportunities/CompetitionInfoWindow.xaml`
- `Kor.Operations.App/Opportunities/HistoricalOpportunityDetailWindow.xaml`
- `Kor.Operations.App/Opportunities/OpportunitiesView.xaml`
- `Kor.Operations.App/Opportunities/OpportunitiesViewModel.cs`
- `Kor.Operations.App/Opportunities/OpportunityDeadlineConverter.cs` (new)
- `Kor.Operations.App/Opportunities/OpportunityDeadlineDisplay.cs` (new)
- `Kor.Operations.App/Opportunities/OpportunityEntryDialog.xaml`
- `Kor.Operations.App/Opportunities/OpportunityEntryDialog.xaml.cs`
- `Kor.Operations.App/Opportunities/OpportunityRowView.cs`
- `Kor.Operations.App/Opportunities/OrgDossierWindow.xaml.cs`
- `Kor.Opportunities.Data/BdReports/Generators/BdVisualHtml.cs`
- `Kor.Opportunities.Data/Briefs/BriefDataRecords.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPresentationRegressionTests.cs` (new)
- `docs/codex/CODEX-BD-FIXES-4-LABELS-EXPORTS-AND-DATES-RESPONSE.md` (new)

The pre-existing Veeam runbook/wedge files and relevance-vocabulary worklist were
left untouched.
