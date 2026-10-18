# Financials: the posted period, the AR trend and the portfolio trend each state what they really measure

Bounded brief for Codex. Source edits only. You build nothing, run nothing, apply nothing and deploy nothing.
The response goes beside this file as `CODEX-FINANCIALS-POSTED-PERIOD-AR-BALANCE-TREND-SCOPE-RESPONSE.md`, with one
section per part: what changed, the gate authored and not run, and anything in the code that contradicted this brief.
`git diff --check` must be clean.

Three engine faults, each found by running the morning brief live on 17 Oct and each measured against Deltek or
KorTransmittals. They are three different faults, so each has its own gate.

## Part 1: "latest posted period" ignores the project scope

**The fact.** `SELECT MAX(Period) FROM PRSummaryMain` returns **202611** on 2026-10-17. That comes from 1 row on
**99999-00**, a placeholder project with Billed 0 and Revenue 0. The real latest period is **202610**: 164 rows on 136
projects. The snapshot already excludes `'[A-Z]%'`, `'9[A-Z]%'` and `'99%'` (`FinancialsService.cs:423-429`). These
reads do not.

**Every site.** I searched `MAX(Period)` across the repo `*.cs` (16 lines). Six of those lines run the query; the
other 10 are comments, messages or a test note. Of the six, three read `GLSummary`, which is a different table and
out of scope. Those three are `GlPnlT12moLoader.cs:95`, `BilledFinancialsService.cs:732` and
`GlProfitLossService.cs:506`. The PRSummaryMain reads, which are all in scope, are:

| Site | Effect of 202611 |
|---|---|
| `Kor.Operations.App/Financials/FinancialsService.cs:1210` | The 24-month revenue timeline ends one month in the future, on an empty month |
| `Kor.Operations.App/Financials/FinancialsService.cs:1266` (`LoadMaxPostedPeriodSync`) | `MaxPostedPeriod`, the "Deltek posted through" banner, reads Nov 2026 |
| `Kor.Operations.App/Crm/DeltekClientContextService.cs:307` | The client screens' billed-posting period reads 202611 |
| `Kor.Operations.Business/WipFinancialsService.cs:129` | WIP's as-of period is 202611, where nothing is posted |

**Fix it once.** Add one helper in `Kor.Operations.Business` that both projects can use (App already references
Business):

```
public static class PrSummaryPeriod
{
    // The latest PRSummaryMain period on a real project: the same WBS1 rules as the Financials snapshot.
    public static string MaxPostedPeriodSql(string catalog) => $@"SELECT MAX(Period) FROM [{catalog}].dbo.PRSummaryMain
WHERE WBS1 NOT LIKE '[A-Z]%' AND WBS1 NOT LIKE '9[A-Z]%' AND WBS1 NOT LIKE '99%';";
}
```

Make all four sites use it. Keep each site's existing parsing and fallback.

**Gate (authored, not run).** Add a source test in App.Tests that walks the repo's non-test `*.cs`. It fails on any
string literal containing both `MAX(Period)` and `PRSummaryMain` outside `PrSummaryPeriod.cs`. Its summary states
what it does not catch:
- a latest period taken by `ORDER BY Period DESC` or `TOP 1`;
- a `GLSummary` or `LedgerAR` latest period;
- a `.sql` file outside the repo.

## Part 2: "AR Outstanding (Recent Months)" plots monthly changes, not balances

**The fact.** `PRSummaryMain.AR` is each period's **change** in AR. For the active scope, summing it over all 146
periods gives **1,379,050**, against an open balance of **1,365,977** in `AR.InvBalanceSourceCurrency`. The last six
periods are −158,358, −15,214, −100,669, −45,969, +71,196 and −129,420. The trend prints its latest point as the
outstanding figure (−21,687 live), and its status says "Period-end AR from PRSummaryMain."

**Fix.** In `Kor.Operations.App/Financials/Loaders/RevenueLoader.cs`, the series assembly (about 441-470):

- Turn `Ar` into a running total before any period is dropped.
- Order `prByPeriod` by `Period`, as ordinal strings. Period `"0"` sorts first; it carries the opening balance and has
  no end date. Accumulate `Ar` across **all** periods, including `"0"` and any period whose end date cannot be parsed.
- Assign each period its running total as `Ar`. Only then apply the existing end-date filter, the closed-period
  filter and `TakeLast(points)`.

Extract the accumulation as a pure `internal static` function. App grants `InternalsVisibleTo` to App.Tests
(`AssemblyInfo.cs:14`).

`SeriesPeriod.Ar` has exactly one consumer, `ExecutiveSummaryDeltekLoader.cs:349` (`ArSeries`), which I searched. The
AR trend's title and status then become true without being edited. Change nothing else in the loader.

**Gate.** Input periods `"0"` (+1,000), `"202608"` (+200), `"202609"` (−300) and `"202610"` (+50), with `points = 2`.
The output is two periods carrying 900 and 950. Also assert that period `"0"` never appears in the output.

## Part 3: the portfolio trend mixes scopes

**The fact.** `KorTransmittals.dbo.FinancialPortfolioSnapshot` has 103 rows, from 2025-11-23 to 2026-10-17. Their
`TotalProjects` runs from 67 to 137, and 0 rows exceed 200. All active projects is 812. The rows are written by
`FinancialsViewModel.PersistSnapshotAndLoadTrendAsync` (about 815-835), once per day on first open, from whatever
scope the screen shows. The default is the watchlist (`_showWatchlistOnly = true`). There is no scope column, so a day
first opened in All Active would land in the same series.

The engine's "Delivery Risk (Critical Count)" trend reads this table for every caller. The morning brief, which runs
on all active projects, prints a latest point of 16 above a drilldown of 72 critical projects.

**Fix.**

1. **Store** (`Kor.Operations.Data/SqlFinancialPortfolioSnapshotStore.cs`):
   - `EnsureSchemaAsync` adds `Scope NVARCHAR(16) NOT NULL CONSTRAINT DF_FinancialPortfolioSnapshot_Scope DEFAULT N'Watchlist'`
     when it is missing, with a `CHECK (Scope IN (N'Watchlist', N'AllActive'))`. The default labels the 103 existing
     rows Watchlist, which matches the measurement above.
   - Replace `UQ_FinancialPortfolioSnapshot_SnapshotDate` with a unique `(SnapshotDate, Scope)`. Keep the whole thing
     idempotent, as the existing `IF OBJECT_ID` block is.
   - `TryInsertSnapshotAsync` and `LoadSnapshotsAsync` each take a scope. Load filters on it.
2. **One definition of the counts.** Move the bucket mapping from `FinancialsViewModel.RecalcPortfolio` (about
   739-800) and `PersistSnapshotAndLoadTrendAsync` into a pure `public static` function in
   `Kor.Operations.App/Financials/`, for example
   `PortfolioHealthCounts.From(IEnumerable<UtilizationRow>) → (Healthy, Watch, Critical, Total)`. The mapping is:
   - Healthy = High Confidence + Watch
   - Watch = At Risk
   - Critical = Critical

   The view model uses it. Its displayed counts must not change.
3. **The screen** writes and reads the scope it is showing: `Watchlist` or `AllActive`.
4. **The engine.** `ExecutiveSummaryService.GetExecutiveSummaryAsync` gains an optional trend scope, defaulting to
   Watchlist so existing callers keep their behaviour. When it loads the trend itself (`trend == null`, about 91-108),
   it loads that scope.
   - Read `FirmContextProvider` (it uses `watchlistOnly: false`, at about 74-77). If it lets the engine load the
     trend, pass AllActive.
   - Read `ExecutiveSummaryViewModel`, and pass the scope its snapshot was built with.
5. **The morning composer** (`tools/MorningBrief/MorningFacts.cs`, `GatherCooAsync`) becomes the daily writer of the
   AllActive series:
   - When the trend connection resolves, and before calling the engine, record today's AllActive row with
     `TryInsertSnapshotAsync` and `PortfolioHealthCounts` over the same utilization rows it passes to the engine.
   - Then call the engine with `existingTrend: null` and trend scope AllActive.
   - A failed insert becomes a coverage note. It must not stop the read.
   - Only that one insert is added; the composer stays read-only everywhere else.

   Until two AllActive days exist, the trend reads "Portfolio trend unavailable." That is the truth; do not fall back
   to the watchlist series.

**Gates.**
- **Pure counts:** one row per confidence level gives the same counts the view model showed before.
- **Store SQL, as text:** the schema block is idempotent, adds Scope with the Watchlist default, and makes the unique
  key `(SnapshotDate, Scope)`. Load and insert both carry `@Scope`.
- **Composer source:** the insert happens before the engine call, passes AllActive, and is wrapped so a failure is
  reported rather than thrown.

**None of these gates reads a database.** After apply, Claude checks three things:
- the schema change on KorTransmittals, confirming the 103 old rows read Watchlist;
- two composer runs on different days, giving 2 AllActive points;
- the Financials screen in both scopes, each showing its own trend.

## Read bound (about 45 KB)

| File | Range |
|---|---|
| `Kor.Operations.App/Financials/FinancialsService.cs` | 1195-1300 |
| `Kor.Operations.App/Crm/DeltekClientContextService.cs` | 295-325 |
| `Kor.Operations.Business/WipFinancialsService.cs` | 120-140 |
| `Kor.Operations.App/Financials/Loaders/RevenueLoader.cs` | 236-300, 420-475 |
| `Kor.Operations.Data/SqlFinancialPortfolioSnapshotStore.cs` | all (about 130 lines) |
| `Kor.Operations.App/Financials/FinancialsViewModel.cs` | 375-390, 610-630, 655-670, 735-870 |
| `Kor.Operations.App/Financials/ExecutiveSummaryService.cs` | 60-110 |
| `Kor.Operations.App/Services/FirmContextProvider.cs`, `Kor.Operations.App/Financials/ExecutiveSummaryViewModel.cs` | search `GetExecutiveSummaryAsync` and `GetSnapshotAsync`, 30 lines around each |
| `tools/MorningBrief/MorningFacts.cs` | `GatherCooAsync` (about 143-175) |
| `Kor.Operations.App/Kor.Transmittals.App.Tests/Financials/` | list the folder and open one test for conventions |

Do not open Deltek, live databases or APP01.
