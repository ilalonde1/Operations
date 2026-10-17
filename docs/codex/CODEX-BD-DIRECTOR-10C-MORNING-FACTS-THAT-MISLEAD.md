# BD Director 10c — the morning brief reads the Financials engine, not around it

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-10C-MORNING-FACTS-THAT-MISLEAD-RESPONSE.md`: plain markdown,
one section per part (what changed, the gate authored and not executed, anything in the code that contradicted this
brief). Do not open earlier responses. `git diff --check` clean. **Rule 14 holds: no model call, no outside service.**

**Run order:** `CODEX-FINANCIALS-AR-BALANCE-IS-PER-PHASE.md` first; this brief assumes its change is in the tree.
**This replaces the first 10c draft.** A partial run of that draft is parked in `stash@{0}`
("codex-10c-partial-2026-10-17"). Do not pop, apply or drop it. Part 4 below reuses its BD hunks by reading them.

## Why

10b's composer (`tools/MorningBrief`) calls `WipFinancialsService`, `ArFinancialsService`, `FirmAnalyticsService` and
`ProjectAnalyticsService` directly, unscoped. That bypasses the Financials engine Ian trusts, which is
`FinancialsService.GetSnapshotAsync` (the active-project snapshot) feeding `ExecutiveSummaryService` (the Executive
Summary screen). The result on 16 Oct: WIP 3.2M of which 2.94M was `XICRV-CA` intercompany and 239K `9OHVA-00`
overhead (the snapshot's WBS1 rules drop both, `FinancialsService.cs:423-429`); "253 of 9960 projects" over budget
(no status filter in `ProjectAnalyticsService`). **The fix is to consume the engine, not to add filters.** Do not add
ChargeType, status or prefix filters anywhere, and do not edit any Business or App service.

## Part 1 — MorningBrief references the App

`tools/BdBriefSmoke` is the precedent for a console that references `Kor.Operations.App`. Match its csproj:
`net8.0-windows10.0.19041.0`, `UseWPF`, the System.IO implicit-using restore, plus a ProjectReference to
`Kor.Operations.App`. Keep the existing references. `Kor.Operations.App` must never reference MorningBrief (App.Tests
already references both; that is fine).

## Part 2 — COO section = the Executive Summary

In `GatherCooAsync` (`tools/MorningBrief/MorningFacts.cs`), replace the AR, WIP, firm-utilization and over-budget reads
with one engine read:

```
var financials = new FinancialsService(odbc, financialsOptions);
var snap = await financials.GetSnapshotAsync(forceRefresh: true, ct, watchlistOnly: false);
var exec = new ExecutiveSummaryService(financials,
    new SqlFinancialPortfolioSnapshotStore("unused: trend supplied by caller"),
    new ExecutiveSummaryDeltekLoader(odbc, financialsOptions));
var result = await exec.GetExecutiveSummaryAsync(forceRefresh: true, existingSnapshot: snap,
    existingTrend: Array.Empty<PortfolioTrendPoint>(),
    existingUtilRows: snap.Rows.Select(UtilizationRow.FromProject).ToArray(), ct);
```

- `watchlistOnly: false` means all active projects, as `FirmContextProvider.cs:74-77` already does. Say "all active
  projects (N)" in the section header using `snap.Rows.Count`.
- The trend store is never called when `existingTrend` is supplied (`ExecutiveSummaryService.cs:91`). Confirm this in
  the code and say so in the response; if it is called anyway, stop and say where.
- No collections provider is passed, because the Collections module is not built out (Ian, 17 Oct). The AR KPI then
  shows its single firmwide number. Print nothing about collections.
- Write a pure `public static IReadOnlyList<MorningFact> ExecutiveFacts(ExecutiveSummaryResult r, int activeProjects)`.
  For every `ExecutiveKpi`, write one fact: Title, ValueText, InlineSubText, StatusMessage, and up to 10 rows from
  whichever drilldown list is non-null, **in the engine's order**, with the true count as X of Y. For every
  `ExecutiveAlert`, write one fact the same way. `SnapshotLoaded`, `DeltekLoaded`, `LoaderFailureMessages` and
  `SchemaDriftMessages` become coverage notes. Format each row type plainly from its record
  (`ExecutiveSummaryService.cs:1298-1420`); money with `N0`, dates `yyyy-MM-dd`.
- Keep `EmployeeFacts` and the Operations doctrine cards read. Delete the composer's own over-budget ratio, firmwide WIP
  line, and direct AR, WIP, FirmAnalytics and ProjectAnalytics calls.

## Part 3 — invoices and partial weeks

Item 1 is fixed in the engine by the AR brief, and the composer only prints it. Item 2 is the one thing the engine does
not have; it stays in the composer as presentation, not as a new definition.

1. **Invoices repeat across phases.** Measured on 17 Oct against `LedgerAR`: 66 of 69 AR rows on multi-phase open
   invoices equal their own phase's ledger net (for example 00038658 is 84,000 on 2.SD plus 77,962.50 on 3.DD), and
   SUM per invoice matches the ledger on 358 of 367 open invoices. So the rows are phase shares. **The engine now groups
   them**: `docs/codex/CODEX-FINANCIALS-AR-BALANCE-IS-PER-PHASE.md` (site 5) runs before this brief and makes
   `ArFinancialsService` return one row per invoice. Do not group again in the composer. Print the drilldown rows as
   given. Also: 0 of 407 open AR rows carry a
   DueDate, so never print a due-date clause. Wherever the engine's days figure is computed from InvoiceDate, label it
   "days since invoice".
2. **Partial timesheet weeks.** The engine has no completeness check. Searched: `tkMaster` 0 files; `timesheet` 8 files,
   none a completeness check; the only "partial" logic is `RevenueMonthRow.IsPartial`. Measured in `tkDetail`, hours and
   people per Sunday week: 08-30 927.5/24, 09-06 952.5/24, 09-13 987.0/24, 09-20 928.0/24, 09-27 944.5/24,
   **10-04 632.5/19, 10-11 292.5/11**. Using `EmployeeAnalyticsService.LoadEmployeeWeeklyUtilizationSync` rows, a week
   is complete when its distinct-people count reaches the median of up to eight prior weeks (ties count as complete).
   Write one fact: "partial: week W, N of M people", and run `EmployeeFacts` on the last complete week. Name that week.
   Do not touch or annotate the engine's Utilization KPI. Measured on 17 Oct, its 30-day ratio is 72.7% against 71.6%
   over the last four complete weeks, because a missing timesheet drops hours from both billable and recorded. Partial
   weeks distort hour totals and per-person weeks, not the ratio.

## Part 4 — BD fixes (not financial)

Reuse the BD hunks from the parked run, read-only, with
`git diff stash@{0}^1 stash@{0} -- tools/MorningBrief/MorningFacts.cs`. Take only `Pursuit`/`PursuitFacts`/`QuietDays`/
`Describe`/`ActionFacts` and the pursuit SQL (organisation-name fallback via `CanonicalOrg`). The behaviour: stale
pursuits ranked by days quiet with fee as tie-break; `N days quiet`; dates `yyyy-MM-dd`; an empty title falls back to
the organisation, then `(untitled)`.

## Gates (in `MorningBriefTests`, authored, not run)

1. **Engine-only invariant:** read `MorningFacts.cs` as text and assert it contains none of `new WipFinancialsService`,
   `new ArFinancialsService`, `new FirmAnalyticsService`, `new ProjectAnalyticsService`, `ChargeType`.
2. `ExecutiveFacts` on a synthetic `ExecutiveSummaryResult` (see `Financials/Executive/SyntheticDeltekData.cs` for
   shapes): every KPI and alert appears; rows keep the given order; the count is shown as X of Y; DataUnavailable KPIs
   print their reason.
3. No due-date clause is printed for an invoice row whose DueDate is null; the age is labelled "days since invoice".
4. The seven week counts above as a fixture: 10-04 and 10-11 are partial; outliers come from 09-27.
5. Pursuits order by quiet days, dates only, title fallback.

**Each gate's summary says what it does not cover.** None reads Deltek, so a wrong snapshot scope, a constructor that
needs config, or an engine number that is itself wrong all pass. Claude checks those by running the composer live and
comparing the COO section against the Executive Summary screen.

## Read bound (about 45 KB, measured)

Read the ranges given. For anything else, search (`rg -n`) and read 40 lines around the hit. Never open a file over
30 KB whole.

| File | Range | For |
|---|---|---|
| `tools/MorningBrief/MorningFacts.cs` | all (22 KB) | parts 2–4 |
| `tools/MorningBrief/MorningBrief.csproj`, `tools/BdBriefSmoke/BdBriefSmoke.csproj` | all (2 KB) | part 1 |
| `Kor.Operations.App/Financials/ExecutiveSummaryService.cs` | 39–140, 1238–1420 | part 2 |
| `Kor.Operations.App/Financials/FinancialsService.cs` | 55–100 | part 2 |
| `Kor.Operations.App/Services/FirmContextProvider.cs` | 68–80 | part 2 |
| `Kor.Operations.App/Kor.Transmittals.App.Tests/Financials/Executive/SyntheticDeltekData.cs` | all (5 KB) | gate 2 |
| `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/MorningBriefTests.cs` | search `PursuitFacts`, `EmployeeFacts` | where gates go |
| the stash diff in part 4 | the named hunks only | part 4 |

Do not open live databases, Deltek or APP01. Touch nothing outside `tools/MorningBrief` and `MorningBriefTests`.
