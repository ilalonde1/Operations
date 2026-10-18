# BD Director 10d: the morning brief says each figure's scope, and prints the engine's trends

Bounded brief for Codex. Source edits only. You build nothing, run nothing, apply nothing and deploy nothing.
The response goes beside this file as `CODEX-BD-DIRECTOR-10D-MORNING-SCOPE-AND-TRENDS-RESPONSE.md`, with one section per
part: what changed, the gate authored and not run, and anything in the code that contradicted this brief.
`git diff --check` must be clean.

10c (commit 2e8e8fc4) made the COO section read the Executive Summary engine. Claude ran it live on 17 Oct and found
two gaps. Both came from the 10c brief, not from the build.

## The facts, measured 17 Oct

- **Scope.** Every `ExecutiveKpi` carries `Scope` (`ScopeKind.None | Firmwide | Scoped`,
  `ExecutiveSummaryService.cs:17-22`). The live run printed 13 KPIs:
  - 9 are `Firmwide`: Cash Position, Liquidity (Cash + AR), AR Outstanding, AR > 60 Days, Net Multiplier, Labor
    Margin (T12mo), Net Income (T12mo), Days Sales Outstanding and Utilization.
  - 4 are `Scoped`: Backlog, Billings To Date, Portfolio Delivery Risk and Projects Over Budget.

  10c heads the whole section "all active projects (812)", which is false for the nine. Measured in Deltek, the AR
  drilldown holds 375 invoice groups across all projects; only 281 are on active projects. The engine is right to make
  AR firmwide (`ExecutiveSummaryDeltekLoader.cs:138-140`). The label is what's wrong.
- **Trends.** `ExecutiveSummaryResult.Trends` holds 4 `ExecutiveTrend`s and the composer prints none of them:
  - Revenue (Earned) (latest 1 / 3 periods)
  - Billings (Invoiced) (latest 1 / 3 periods)
  - AR Outstanding (Recent Months)
  - Delivery Risk (Critical Count)

  The fourth needs the portfolio trend store. 10c passes `existingTrend: Array.Empty<PortfolioTrendPoint>()`, so it
  is always "Portfolio trend unavailable."
- **The store has data.** `KorTransmittals.dbo.FinancialPortfolioSnapshot` holds 103 rows, from 2025-11-23 to
  2026-10-17, 12 of them in the last 12 weeks. Its connection string is the machine environment variable
  `KOR_FILESYNC_KORTRANSMITTALSDB`, which is set on both KOR-1001 and KOR-APP01 (names checked; values never printed).
  `SqlFinancialPortfolioSnapshotStore(string?)` takes the string as given (`Kor.Operations.Data/SqlFinancialPortfolioSnapshotStore.cs:15-18`).
- **Trend series have no period labels.** `ExecutiveTrend.Values` is a bare `double[]` built from
  `revenue.Series.Periods` (`ExecutiveSummaryDeltekLoader.cs:347-349`). The labels do not survive into the trend.
- **WIP is hidden by the engine on purpose.** `hideUnbilledEarned = true` (`ExecutiveSummaryService.cs:394-398`) is
  pending finance sign-off, and the screen hides it too. Do not add WIP back anywhere.

## Part 1: each figure states its own scope

In `tools/MorningBrief/MorningFacts.cs`, `ExecutiveFacts`:

- Each KPI fact's name becomes `"{Title} · {scope}"`:
  - `Firmwide` → `firmwide`
  - `Scoped` → `all active projects ({activeProjects})`
  - `None` → `scope not stated by the engine`

  Read the value from `kpi.Scope` and from nothing else. Do not keep a list of titles.
- `ExecutiveAlert` has no Scope field. Alert fact names get `· scope not stated by the engine`.
- The section header must stop claiming one scope for everything. Change the coverage fact's `SectionScope` to
  `Executive Summary engine; project-scoped figures cover all active projects ({activeProjects})`.

## Part 2: print the trends, with the real trend store

In `GatherCooAsync`:

- Add a non-throwing lookup next to `Connection`, for example
  `OptionalConnection(string name) => machine ?? process ?? null`. Use the same two sources in the same order as
  `Connection`.
- Then:
  - **If `KOR_FILESYNC_KORTRANSMITTALSDB` resolves:** construct `new SqlFinancialPortfolioSnapshotStore(value)` and
    pass `existingTrend: null`, so the engine loads 12 weeks itself (`ExecutiveSummaryService.cs:91-108`).
  - **If it does not resolve:** keep the 10c placeholder store and `Array.Empty<PortfolioTrendPoint>()`. Pass the
    reason into `ExecutiveFacts` so coverage says `Portfolio trend not read: KOR_FILESYNC_KORTRANSMITTALSDB unset.`

  The Executive Summary read must never fail because this variable is missing.
- Extend `ExecutiveFacts`:
  - **Signature.** It gains a parameter carrying the trend note, for example `string? trendNote = null`.
  - **Coverage.** Coverage adds `TrendLoaded: {r.TrendLoaded}`, plus `MaxPostedPeriod: {yyyy-MM}` (or `unknown`).
    When `TrendLoaded` is false, the trend note (or `Portfolio trend unavailable.`) joins the gaps.
  - **Trend facts.** Each `ExecutiveTrend` becomes one fact, named `"{Title} · {scope}"` by the same rule as Part 1.
    Its lines are:
    1. `ValueText`, then `StatusMessage`, each skipped when blank.
    2. When `Values` is non-null, one line:
       `Series, {n} periods, oldest first (period labels are not carried by the engine): v1; v2; …`, with money in
       `N0`. Delivery Risk counts also use `N0`.
    3. Up to 10 rows from `TrendPayerRows`, otherwise from `DeliveryRiskRows`, in the engine's order, with the true
       count as X of Y.
  - **Formats.** Format `TrendPayerRow` (`ExecutiveSummaryService.cs:1421-1429`) as
    `{Wbs1} {ProjectName}, PM {Pm}, payer {PayerName}: amount {N0}; earned {N0}; invoiced {N0}; AR {N0}.`
    `KpiDeliveryRiskRow` reuses the existing formatter.
  - **Unavailable.** A trend whose `ValueText` is `Data unavailable` sets `CouldNotRead` to its `StatusMessage`, as the
    KPI facts already do.
- Order: coverage, then the KPIs, then the trends, then the alerts. Keep the engine's order inside each group.

Do not change the engine, the store, the loader or any Financials file. Presentation only.

## Gates (authored, not run), in `MorningBriefTests.cs`

1. **Scope comes from the field.** Build a synthetic result with three KPIs, one per `ScopeKind`, given titles that
   suggest the opposite scope: for example "AR Outstanding" marked `Scoped` and "Backlog" marked `Firmwide`. Assert
   each name carries the label from its `Scope`, which proves no list of titles is consulted. Assert that alerts carry
   `scope not stated by the engine`, and that the header text matches the new `SectionScope` exactly. Update the 10c
   gate that expects `all active projects (N)` in the header.
2. **Trends print.** Use a synthetic result with:
   - one trend with `Values` and 12 `TrendPayerRows`, which prints `10 of 12` and the series line in order;
   - one with `DeliveryRiskRows`;
   - one `DataUnavailable`, which sets `CouldNotRead`;
   - `TrendLoaded = false` with a note, which puts the note in the coverage gaps.

   Also assert the group order: coverage, KPIs, trends, alerts.
3. **Store wiring, a source gate.** `GatherCooAsync` text contains `KOR_FILESYNC_KORTRANSMITTALSDB`,
   `existingTrend: null` and the `Array.Empty<PortfolioTrendPoint>()` fallback. It does not call `Connection(` with
   that name, which would throw when the variable is unset.

**Each gate's summary says what it does not cover.** None reads a database. A wrong store connection, an empty trend
table, or series values that don't match the screen's sparkline all pass them. After apply, Claude runs the composer
live and checks two things. First, the Delivery Risk trend has 12 points, against
`SELECT COUNT(*) … WHERE SnapshotDate >= today − 84 days`. Second, the Revenue and Billings `ValueText` match the
Executive Summary screen.

## Read bound (about 30 KB)

| File | Range |
|---|---|
| `tools/MorningBrief/MorningFacts.cs` | `GatherCooAsync` through `EmployeeWeekFacts` (about 140–300), and `Connection` (about 368) |
| `tools/MorningBrief/MorningPromptComposer.cs` | `Role` (about 55–70) |
| `Kor.Operations.App/Financials/ExecutiveSummaryService.cs` | 17–22, 85–110, 1236–1300, 1421–1430 |
| `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/MorningBriefTests.cs` | the five 10c gates (search `Executive`, `Coo_source`) |

Do not open Deltek, live databases or APP01. Touch only `MorningFacts.cs`, `MorningPromptComposer.cs` if the header
needs it, and `MorningBriefTests.cs`.
