# Financials Module — Forensic Audit

**Date:** 2026-07-26
**Scope:** `Kor.Operations.App\Financials` (71 files / 22,116 lines) + full blast radius
**Method:** Complete read of every file in the module and every consumer of it. No code changed.
**Verification:** 9 read-only queries run against the live Deltek catalog 2026-07-26 (see Part 3). **F1 confirmed. F2 corrected — original claim was wrong. F4 confirmed and sized. F12(f) withdrawn — disproven.**

---

## Part 1 — Component Map

### 1.1 Composition & lifetimes

`AppCompositionRoot.BuildServiceProvider()` → `.AddFinancialsServices()` (`CompositionModules\FinancialsModule.cs`).

| Registration | Lifetime | Notes |
|---|---|---|
| `DeltekOdbcOptions` | **Singleton** | Built once by `CompositionHelpers.GetDeltekOdbcOptions()`. **Mutable** (`EngRate`, `DraftRate`, `TargetBillingRate`, `UseTargetRateBudget` all have setters). |
| `FinancialsOptions` | **Singleton** | Immutable (`init` only). |
| `VpOdbcDsnFactory` | Singleton | Closure captures `deltekOdbcOptions`. |
| `GlProfitLossService` | Transient | |
| `BilledFinancialsService` | Transient | |
| `FinancialsService` | Transient | Holds its **own** `_cache` + `_cacheLock` → cache dies with each resolve. |
| `ExecutiveSummaryDeltekLoader` | Transient | Ctor writes the **static** `ExecutiveSummaryLoaderSupport.Catalog`. |
| `ActiveCollectionsInvoiceProvider` | Transient | Delegate bridging to internal `CollectionsClient`; returns `null` when MCP unconfigured. |
| `ExecutiveSummaryService` | Transient | Own 5-min TTL cache. |
| `ExecutiveSummaryViewModel` / `BillingManagerReportViewModel` / `FinancialsViewModel` | Transient | |

Config keys live in `Services\AppConfigKeys.cs`; values in `App.config`. **`DeltekOdbcOptions` and `FinancialsOptions` are physically defined in `Kor.Operations.Business\SharedOptions.cs` under namespace `Kor.Operations.App.Options`** — namespace/assembly mismatch, deliberate so MCP can share them.

### 1.2 The four data pipelines

```
                    ┌─ Deltek (ODBC, catalog C0000052267P_1_KOR00000000) ─┐
                    │                                                     │
 (A) Active grid    │  FinancialsService.LoadSnapshotAsync                │
                    │    11 parallel loaders → FinancialsSnapshot         │
                    │                                                     │
 (B) Exec Summary   │  ExecutiveSummaryDeltekLoader.TryLoadAsync          │
                    │    → CashFinancialsService                          │
                    │    → RevenueLoader                                  │
                    │    → WipFinancialsService                           │
                    │    → UtilizationService                             │
                    │    → ArFinancialsService                            │
                    │    → FirmHealthService                              │
                    │    → GlPnlT12moLoader                               │
                    │                                                     │
 (C) GL P&L tab     │  GlProfitLossService.BuildProfitLossAsync           │
                    │                                                     │
 (D) Billed P&L     │  BilledFinancialsService.BuildAsync                 │
                    └─────────────────────────────────────────────────────┘
```

Plus **(E)** `BillingManagerReportViewModel` which runs its **own** ODBC query directly (not through any service).

### 1.3 Section index → surface map (`FinancialsWindow`)

| `SectionIndex` | Surface | Backing |
|---|---|---|
| 0 | Command Center (active grid, capacity risk) | `FinancialsService` → `FinancialsProjectRow`, `UtilizationRow`, `DraftUtilizationRow` |
| 1 | Executive Summary | `ExecutiveSummaryService.Build()` → 13 KPIs, 4 Trends, 4 Alerts |
| 2 | P&L Report (GL) | `GlProfitLossPresenter` + `GlProfitLossService` |
| 3 | Billing Manager Report | `BillingManagerReportViewModel` (own SQL) |
| 4 | Clients | `FinancialsService.LoadClientPortfolioSync` → `ClientRollupRow` |
| 5 | Forecast | `FinancialsViewModel.RecomputeForecast` (Theil-Sen + seasonal) |

Sub-windows: `ProjectFinancialDetailWindow` (CFO metrics), `MetricDetailWindow` (drilldowns), `FinancialMetricDictionaryWindow`, plus `StaffUtilizationWindow` / `HistoricalAnalyticsWindow` / `CollectionsWindow` launched from here.

### 1.4 Canonical constants (single sources of truth)

- `AnalyticsThresholds` (Business) — 13 constants, all documented.
- `OrgFx` (Business) — `IsUsaOrg()` + `ParseUsdToCadRate()` (default 1.36).
- `FinancialsOverheadRate.Default = 1.65`; `App.config` sets it explicitly.
- `LaborCodes` (App, `FinancialsService.cs`) — 10/20/30/40/50/60/70/80.
- `DeltekCatalogValidator` — all catalog interpolation routes through here (injection-safe).
- `MathHelpers.SafeDiv` (Core) — `den == 0 ? 0 : n/d`.

### 1.5 Shared-with-MCP surface

`Kor.Operations.Business` is the shared assembly. MCP tools (`get_backlog`, `get_wip`, `get_ar`, `get_cash`, `get_utilization`, `get_firm_health`, `get_gl_pnl`, `get_billed_pnl`, `get_collection_exposure`, `get_earned_vs_invoiced`, `get_at_risk_projects`) wrap the **same service classes** the App uses — except `BacklogService` and `RecentBilledService`, which are **MCP-only** re-implementations of App logic.

---

## Part 2 — Findings Register

Ranked by (severity × blast radius). Every finding is a code-verifiable contradiction or divergence; where a claim depends on live Deltek data I say so explicitly.

---

### F1 — WIP sign convention is inverted relative to the revenue series ⚠ CRITICAL

**Files:** `Kor.Operations.Business\WipFinancialsService.cs:228, 318, 355-356` vs `Kor.Operations.App\Financials\Loaders\RevenueLoader.cs:453-456`

Two code paths compute the same unbilled-WIP proxy from the same two columns, and they are **exact negations of each other** — while both label "positive = earned-not-billed":

```csharp
// RevenueLoader.BuildSeries (App, Exec Summary revenue series)
var proxy = p.Revenue - p.Billed;
if (proxy > 0) unbilledEarned = proxy; else if (proxy < 0) overbilled = -proxy;

// WipFinancialsService (Business, WIP tile + MCP get_wip)
SUM(COALESCE(sm.Billed,0) - COALESCE(sm.Revenue,0)) AS Net
var earned = Math.Max(net, 0.0);
var over   = Math.Max(-net, 0.0);
```

`WipTool.cs:88` states the contract explicitly: *"Sign convention: positive Net = earned-not-billed; negative Net = overbilled."* With `Billed − Revenue`, positive means **billed exceeds earned** — i.e. overbilled. Earned and Overbilled are swapped.

**Corroborating evidence this is live:** `ExecutiveSummaryService.cs:394-402` hard-disables the WIP tile with the comment *"the resulting number renders as $0 because the per-project Unbilled column nets to overbilled across the watchlist."* That is precisely the symptom of a flipped sign.

**Blast radius:** the WPF tile is hidden so users don't see it, **but `get_wip` is live in MCP** and feeds `/ask`. Every WIP answer Claude gives has Earned and Overbilled transposed.

**✅ CONFIRMED against live Deltek 2026-07-26.** Both columns are stored **positive** at KOR — `Billed` positive on 17,110 rows vs negative on 239; `Revenue` positive on 17,138 vs negative on 328. The `WipFinancialsService.DetectRevenueGeneration` comment claiming Deltek's "credit-side sign convention (negative = recognized)" is **false for this catalog**. Therefore `Revenue − Billed` (RevenueLoader) is correct and `Billed − Revenue` (WipFinancialsService) is inverted.

> ⚠ **The dollar figures I first published here were wrong — corrected in verification round 2 (Part 6).** They were un-FX'd CAD+USD mixes, and they ignored that earned/overbilled are split **per project** before summing. The *sign* conclusion survives and is now better evidenced; the *amounts* below are the corrected ones.

Org-bucketed and FX-converted at 1.36:

| Basis | CAD | USA (raw) | **CAD-equivalent total** |
|---|---|---|---|
| `Revenue − Billed` (true unbilled WIP) | +2,699,485.64 | −1,940,071.10 | **+$60,988.94** |
| `Billed − Revenue` (WipFinancialsService proxy/firmwide) | −2,699,485.64 | +1,940,071.10 | **−$60,988.94** |
| raw `Unbilled` column | +2,440,887.53 | −1,922,573.99 | **−$173,813.10** |
| `−Unbilled` (WipFinancialsService RG branch) | −2,440,887.53 | +1,922,573.99 | **+$173,813.10** |

**The sign finding is confirmed, and more strongly than before.** Deltek's own `Unbilled` column agrees in sign with `Revenue − Billed` in both buckets (CAD both positive ≈ +2.4–2.7M; USA both negative ≈ −1.9M). Both therefore mean *positive = earned-not-billed*. `WipFinancialsService` **negates `Unbilled`** on the RG branch and computes **`Billed − Revenue`** on the proxy branch — **both branches are inverted**, in the same direction, relative to the column Deltek itself populates.

**What is NOT proven:** the reported `earned` / `overbilled` dollar amounts. Those are `Max(net,0)` / `Max(−net,0)` applied **per project** and then summed, so they cannot be derived from firmwide aggregates — they are strictly larger than |net|. Getting them requires a per-`WBS1` query that has not been run. Do not quote an earned/overbilled figure until it is.

**Two further defects surfaced by the same probe:**

- **F1a — the RG branch runs, and the tool says it doesn't.** `PRSummaryMain.Unbilled` **is** populated (238 rows, `SUM = +$518,313.54`), so `UnbilledColumnHasAny()` returns true and `useUnbilledAsOf = true` — the **Revenue-Generation path executes**, not the proxy. `WipTool`'s description ("KOR runs with Revenue Generation OFF so the proxy path is what produces these numbers") and the same claim in project memory are both **factually wrong**. That path computes `SUM(-Unbilled) = −$518,313.54` — also negative, so it *also* reports $0 earned. Both branches are flipped in the same direction.
- **F1b — firmwide and drilldown use different formulas.** `LoadFirmwideWipProxyBalance` **always** uses the proxy `Billed − Revenue` regardless of which branch the per-project drilldown took. So `WipTool`'s payload emits `firmwide.net = −$759,414.54` alongside `drilldownTotals.net = −$518,313.54` while its methodology string asserts *"Firmwide totals are independent of the per-project drilldown (separate SQL roll-up, **sums tie out**)."* They differ by **$241,101.00**.

---

### F2 — AR dedupe inconsistency — ⚠ **CORRECTED after verification: the dedupe is the bug, not the raw SUM** (MEDIUM)

**Files:** `FinancialsService.cs:795-820` vs `ArFinancialsService.cs:204, 130`

`FinancialsService.LoadInCollectionsByWbs1Sync` carries an explicit, emphatic comment:

> *"AR has one row per WBS sub-phase per invoice; `InvBalanceSourceCurrency` is replicated across those rows. Without deduping by (WBS1, Invoice) a 3-sub-phase invoice would contribute 3x its balance… Outer caller clamps at projectOutstanding (**which is itself over-counted the same way**)"*

…and dedupes via `HashSet<(Wbs1, Invoice)>`.

But every other AR consumer does a **raw `SUM`** with no dedupe:
- `ArFinancialsService.LoadFirmwideArTotals` → drives **AR Outstanding**, **Liquidity (Cash + AR)**, and the **DSO denominator**.
- `ArFinancialsService.LoadInvoiceArBalances` → drives the AR drilldown and the InCollections split.
- `FinancialsService.LoadClientPortfolioSync` `arSum` subquery → drives the entire **Clients tab**.

**✅ VERIFIED 2026-07-26 — and the result reverses my initial reading. The comment's premise is false, so the raw `SUM` is correct and the dedupe is the defect.**

| Measure | Value |
|---|---|
| AR rows with a live balance | 433 |
| Distinct `(WBS1, Invoice)` | 399 |
| Duplicate groups | 33 |
| …of which have **identical** balances (true replication) | **1** |
| …of which have **different** balances (genuine distinct lines) | **32** |

The balance is **not** replicated across sub-phase rows — 32 of 33 duplicate groups carry genuinely different amounts. They are real, separate AR lines: sub-phase splits (e.g. `31103-01` invoice `00038658` = 77,962.50 **+** 84,000.00 = 161,962.50) and credit/debit reversal pairs that legitimately net to zero (`30885-01` invoice `00039313` = −1,200.00 **+** 1,200.00 = 0.00).

**Consequences — the opposite of what I first wrote:**
- **AR Outstanding, Liquidity, DSO and the Clients tab are NOT inflated.** Raw `SUM` is the right operation. My original HIGH ranking was wrong; withdrawn.
- **`LoadInCollectionsByWbs1Sync` is the bug.** Its `HashSet.Add((wbs1, invoice))` keeps only the **first** row per invoice and silently drops the rest — and that query has **no `ORDER BY`**, so which row survives is storage-order-dependent. For `31103-01/00038658` it books either $77,962.50 or $84,000.00 instead of $161,962.50; for a reversal pair it books ±$1,200 of phantom balance against a true net of $0.
- Net effect: **in-collections is understated and non-deterministic between runs**, and the two collections figures (Clients tab vs Exec Summary AR tile — `ArFinancialsService` sums matched invoice rows without deduping) disagree by construction.

Fix direction is now the reverse of the original: **remove the dedupe**, don't add it elsewhere.

---

### F3 — Backlog **alert** drilldown contradicts the Backlog **KPI** drilldown ⚠ HIGH

**File:** `ExecutiveSummaryService.cs`

| Path | Line | Formula |
|---|---|---|
| Backlog **KPI** drilldown | 494 | `backlog = fee − (billedPosted + unposted)` |
| Backlog **Alert** drilldown | 1082 | `backlog = fee − billed` ← **posted only** |

Same window, same concept, two formulas. Only the KPI path reconciles to `Headline.TotalUnbilled`. `ReconciliationTests.Billings_IncludesUnpostedFeeBilledOverlay` pins the KPI path and explicitly asserts posted-only is wrong — the alert path is the exact bug that test forbids, and no test covers it. `AlertPathParityTests` only guards AR.

Same file, same class of defect: `OverByHours` is `-u.RemainingEngHours` in the KPI (line 729) but `Math.Abs(Math.Min(0, u.RemainingEngHours))` in the alert (line 1068) — the KPI's documented ability to show negative overage is silently clamped in the alert.

---

### F4 — "Net Income (T12mo)" tile cannot agree with the GL P&L tab it claims to mirror ⚠ HIGH

**Files:** `Loaders\GlPnlT12moLoader.cs:167-217`, `GlProfitLossPresenter.cs:87`, `App.config:67`

The tile caption says: *"This is the bottom-line P&L number - **same source as the GL P&L tab**."*

- **The tile** (`GlPnlT12moLoader`): **no Org filter**, buckets CAD/USA and FX-converts USA→CAD.
- **The tab** (`GlProfitLossPresenter.InitializeAsync`): `OrgFilter = FinancialsOptions.BilledDefaultOrg`, which `App.config` sets to **`CAD`** — so `convertUsaToCad = false` and USA rows are excluded entirely.

`App.config` documents the size of the gap for the sibling Billed P&L: *"Mar 2026: app +$77,620 over Daler = $57,073 USD × 1.36."* The tile and tab will differ by roughly the FX'd USA contribution, permanently, with the tile asserting they're the same number.

Also: the tile picks its GL table by an **independent scoring function** (`GlPnlT12moLoader.ScoreTable`) that is a near-copy of `GlProfitLossPresenter.PickBestDefaultTable` but **missing the `"grouped"+"expense" → +50` rule**. Two scorers, one table choice — they can diverge.

**✅ VERIFIED 2026-07-26 — both axes confirmed, and the table divergence is real, not hypothetical.**

KOR's catalog has **seven** tables matching `%Income Statement%`:

```
 1001  Income Statement Partnership WIP
 1010  Income Statement DMCL
 1012  Income Statement KOR vs KS V2
 1500  Income Statement Grouped Expenses
 8001  USA Income Statement & Retained Earnings
11111  Income Statement DMCL-ADJ JCV
32767  Income Statement Kerry Smithies
```

Every name scores 280 on the shared rules (`income statement` +200, `income` +60, `statement` +20). Only `1500 Income Statement Grouped Expenses` triggers the tab-only `+50`, giving it 330.

- **The tab** (`OrderByDescending(Score).First()`) → **TableNo 1500**.
- **The tile** (`if (s > best.Score)` over `ORDER BY TableNo`, strict `>` so the first 280 wins) → **TableNo 1001**.

**The tile and the tab read two different GL report definitions.** That is a larger discrepancy than the Org filter.

**Org-scope gap, sized** (T12 = 202503–202602, table 1001, `flipSign=true`): USA-org P&L rows total **$341,148.74 USD net** → **≈ $463,962 CAD-equivalent** that the tile includes and the tab (`Org=CAD`) excludes.

So the caption *"same source as the GL P&L tab"* is false on **two independent axes**: different table, and different Org scope.

*(Checked and cleared: `Financials.PnL.GlFlipSign=true` in App.config, so the `net = income + expense` arithmetic and the "Revenue − Expenses" caption are consistent. Not a bug.)*

---

### F5 — MCP `BacklogService` scope ≠ App backlog scope ⚠ MEDIUM-HIGH

**Files:** `Kor.Operations.Business\BacklogService.cs:150-152` vs `FinancialsService.cs:423-429`

`BacklogTool`'s description claims *"same canonical formula as the WPF Financials window."* The **formula** matches; the **population** does not:

| Filter | App (`LoadBaseProjectsSync`) | MCP (`LoadActiveProjectsFirmwide`) |
|---|---|---|
| Status | `IN ('A','ACTIVE')` | `= 'A'` only |
| Overhead WBS1 `[A-Z]%` | excluded | **not excluded** |
| Overhead WBS1 `9[A-Z]%` | excluded | **not excluded** |
| Overhead WBS1 `99%` | excluded | **not excluded** |
| WBS3 | not filtered | filtered |

Any overhead/admin project carrying a `PR.Fee` lands in the MCP backlog and not the App's. Every other financial surface in the codebase — `FirmHealthService`, `UtilizationService`, `LoadPeerProjectsSync`, `LoadClientPortfolioSync` — applies the three overhead-prefix exclusions. `BacklogService` is the sole exception.

---

### F6 — The Target Rate slider mutates a process-wide singleton ⚠ MEDIUM-HIGH

**File:** `FinancialsWindow.xaml.cs:72-84`

```csharp
_vm._odbcOptions.EngRate = _vm.EngRate;
_vm._odbcOptions.DraftRate = _vm.DraftRate;
_vm._odbcOptions.TargetBillingRate = _vm.TargetBilling;
_vm._odbcOptions.UseTargetRateBudget = _vm.IsTargetRateBudgetMode;
```

`DeltekOdbcOptions` is registered `AddSingleton`. Clicking **Recalculate** permanently rewrites the budget assumptions for every other consumer of that instance for the rest of the process — `StaffUtilizationWindow`, `HistoricalAnalyticsWindow`, `PeerBudgetEstimator`, and every later `FinancialsService` resolve. There is no reset, and nothing tells the user the change escaped the window. A VM field reaching through a public field (`_vm._odbcOptions`) into a shared singleton is also the reason this is easy to miss.

---

### F7 — Three different definitions of "% Hours Spent" ⚠ MEDIUM

| Source | Numerator | Denominator |
|---|---|---|
| `FinancialsHeadlineCalculator.cs:33` | `EngHrs + DraftHrs` | `EngBudget + DraftBudget` |
| `DeliveryConfidenceCalculator.cs:25` | `EngHrs + DraftHrs` | `EngBudget + DraftBudget` |
| **`CfoMetrics\ProjectData.cs:91-93`** | **all 7 labor codes** incl. Admin + NonBillable | `EngBudget + DraftBudget` |
| Dictionary (`Definitions.Core.cs:109-121`) | "TotalHoursSpent" / *"planned **engineering** hours"* | `EngBudget + DraftBudget` |

`ProjectData` is what `ProjectFinancialDetailWindow` shows. Open a project's detail window and its "% Hours Spent" and "Budget Burn Rate" read materially higher than the same project's number on the grid it was opened from — the numerator includes admin and non-billable time the denominator never budgeted for.

The dictionary entry compounds it: `Description` says *engineering* hours, `Formula` says `TotalHoursSpent`, implementation (headline) says eng+draft. Three statements, three meanings.

`ProjectData.FromProject` also re-runs `DeliveryConfidenceCalculator.Compute(p)` instead of reading `p.DeliveryResult` — defeating the documented "compute once, 3× → 1×" optimisation that `FinancialsProjectRow.DeliveryResult` exists for.

---

### F8 — Metric Dictionary formulas contradict the code (and the drift gate can't see it) ⚠ MEDIUM

`ToolDefinitionDriftTests` is a **substring-presence gate over prose**. It proves an account number is *mentioned* on both sides. It cannot detect a wrong formula. Confirmed drift it does not catch:

| Key | Dictionary says | Code does |
|---|---|---|
| `Forecast_Backlog` | `SUM(MAX(0, Fee − FeeBilled))` | `Max(0, TotalFee − FeeBilled**WithUnposted**)` (`FinancialsViewModel.cs:261-265`) |
| `TotalUnbilled` | "minus amount billed to date" | minus `FeeBilled + UnpostedFeeBilled` |
| `Backlog` | "Subtracts billed-to-date from contracted fee" | includes T&M `HourlyRevenue` on the fee side **and** the unposted overlay |
| `PercentHoursSpent` | "planned engineering hours" | eng+draft (headline) / all-codes (CFO metric) |

Also unguarded by the gate: `Forecast_*` (6 keys), all `Portfolio*` keys, and the entire `Definitions.BillingManager` set have no MCP counterpart row at all.

---

### F9 — Two different columns are both called "Billed" ⚠ MEDIUM

`PRSummaryMain` has both `BilledFee` and `Billed`, and the module uses them for different surfaces under the same word:

| Surface | Column |
|---|---|
| Active grid `FeeBilled`, Backlog, Billings To Date, Clients tab | `CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END` |
| **Billing Manager Report** (`BillingManagerReportViewModel.cs:617`) | `SUM(COALESCE(Billed, 0))` |
| Exec Summary "Invoiced" trend, `RecentBilledService` | `SUM(COALESCE(Billed, 0))` |

Nothing is necessarily wrong — earned vs invoiced are genuinely different — but the **Billing Manager Report's "% Fee Billed" column divides `Billed` by `TotalFee`**, while the grid's `% Billed` divides `BilledFee-else-Revenue` by the same `TotalFee`. Same label, same window, different numerator. There is no in-app disclosure of the difference.

Separately: the report groups by `r.BillingManager`, which `LoadBaseProjectsSync` populates from **`pr.Principal`** (`em3` join, line 421). The "Billing Manager Report" is a *Principal* report.

---

### F10 — `ExecutiveSummaryLoaderSupport.Catalog` is process-global mutable state ⚠ LOW-MEDIUM

**File:** `ExecutiveSummaryDeltekLoader.cs:116-117, 376`

```csharp
internal static string Catalog { get; set; } = "C0000052267P_1_KOR00000000";
```

Set from a **transient** constructor. Harmless with one catalog, but `RevenueLoader`, `GlPnlT12moLoader` and `BillingManagerReportViewModel` all read it as ambient state rather than receiving it — while `ArFinancialsService`, `CashFinancialsService`, `WipFinancialsService`, `UtilizationService`, `FirmHealthService`, `BacklogService` and `RecentBilledService` each resolve their **own** `_catalog` field via `DeltekCatalogValidator.ResolveCatalog`. Two catalog-resolution regimes coexist. The loader also uses `ValidateCatalog` (throws) where everything else uses `ResolveCatalog` (falls back).

---

### F11 — `FinancialsService` cache is per-instance on a transient registration ⚠ LOW-MEDIUM

`FinancialsService` maintains `_cache` / `_cacheWatchlistOnly` behind a lock, but is registered `AddTransient`. Every `sp.GetRequiredService<FinancialsService>()` gets an empty cache. In practice `FinancialsViewModel` is also transient and holds one instance, so the window works — but any second resolver silently re-runs 11 parallel Deltek queries. `ExecutiveSummaryService` (also transient) has the same shape with a 5-minute TTL.

---

### F12 — Smaller divergences worth a pass

| # | Item | Location |
|---|---|---|
| a | `BacklogService.SafeDiv` uses `d > 0.004`; `MathHelpers.SafeDiv` uses `d == 0`. Negative denominators return 0 in one, a real quotient in the other. | `BacklogService.cs:287` |
| b | `ClientsHasCollectionsExposure` threshold `> 0.5`; `ClientRollupRow.HasCollectionsExposure` uses `> 0.004`. Same concept, two thresholds. | `FinancialsViewModel.cs:107` |
| c | `FinancialsService.LoadFeeBilledSync` omits `COALESCE(Revenue,0)` where all four sibling copies of the same expression include it. | `FinancialsService.cs:473` |
| d | `ICfoMetric` has no `Formula` member; all four implementations define one and `ProjectFinancialDetailWindow.GetFormulaIfPresent` reads it reflectively. | `ICfoMetric.cs` |
| e | `DeltekSchemaValidator` caches a `Lazy<Task>` keyed by catalog that closes over the **first** `OdbcConnection` — if the first validation is still pending when that connection is disposed, later callers await a task bound to a dead connection. | `DeltekSchemaValidator.cs:84-86` |
| f | ~~`LoadInspectionCountsSync` uses `OdbcType.Date` where everything else uses `DateTime`.~~ **WITHDRAWN — disproven 2026-07-26.** Probed directly: `OdbcType.Date` returned **288** rows, exactly matching a literal-date control query; `OdbcType.DateTime` **threw** `ERROR [HY000] [DataDirect][ODBC Hybrid driver][Service] Protocol error. Unexpected token type: 32`. The existing code is correct as written. See §3.4 — **do not "fix" this.** | `FinancialsService.cs:1313-1314` |
| g | Utilization headline is firmwide but the tile sits among Scoped tiles; correctly badged `ScopeKind.Firmwide`, but `deltek.ArScopedOutstanding` (scoped) feeds the "Collection exposure" line on a `ScopeKind.Scoped` trend whose AR drilldown rows are firmwide. | `ExecutiveSummaryService.cs:858` |

---

## Part 3 — Verification queries — **RUN 2026-07-26, results inline above**

Nine read-only `SELECT`s executed against the live catalog from KOR-1001 via system DSN `Deltek` (creds from Machine env vars `KOR_ODBC_USER` / `KOR_ODBC_PASSWORD`). Nothing was mutated. Scripts + raw output retained in the session scratchpad (`deltek-audit-probe.ps1`, `deltek-audit-probe2.ps1`, `probe-out.txt`, `probe2-out.txt`).

**Outcomes:** F1 **confirmed** (+ two new sub-findings F1a/F1b) · F2 **reversed** · F4 **confirmed and sized** · F12(f) **withdrawn**.

### 3.4 — The `OdbcType.Date` vs `DateTime` result (read before touching any date binding)

| Binding | Result on `tkDetail.TransDate` |
|---|---|
| `OdbcType.Date` | **288 rows** — matches literal-date control exactly |
| `OdbcType.DateTime` | **Throws:** `ERROR [HY000] [DataDirect][ODBC Hybrid driver][Service] Protocol error. Unexpected token type: 32` |
| Literal dates, no parameters | 288 rows |

`INFORMATION_SCHEMA` reports `tkDetail.TransDate` as `datetime` — so the column type does **not** explain the split.

This is the **opposite** of the stored `reference_deltek_odbc_quirks` note ("`OdbcType.Date` binds to nothing; use `DateTime`"), which was verified against `LedgerAR/AP/EX/Misc` through the MSDASQL linked-server path. Meanwhile production C# in `FirmHealthService`, `UtilizationService`, `ArFinancialsService` and `CashFinancialsService` binds `DateTime` against these same tables and demonstrably works.

I could not reconcile all three observations from here, and I am not going to guess. **Conclusion: leave every existing date binding alone.** Both `Date` and `DateTime` appear in working production paths; the failure mode is environment/driver-path-specific, not a simple "always use X" rule. If a date-filtered loader ever returns a suspicious zero, this table is the first thing to check — but nothing here justifies a preemptive change, and the stored memory should not be trusted as universal.

---

### Original query definitions (for re-running)

**3.1 — WIP sign convention**
```sql
SELECT TOP 20 WBS1, Period, Billed, Revenue, BilledFee, Unbilled
FROM PRSummaryMain
WHERE ABS(COALESCE(Billed,0)) > 0 AND ABS(COALESCE(Revenue,0)) > 0
ORDER BY Period DESC;
```
Establishes whether `Revenue` and `Billed` are stored positive or credit-negative → tells us which of `Revenue − Billed` / `Billed − Revenue` is Earned-minus-Invoiced.

**3.2 — AR sub-phase multiplicity**
```sql
SELECT COUNT(*) AS ArRows,
       COUNT(DISTINCT CONCAT(WBS1,'|',Invoice)) AS DistinctWbs1Invoice,
       SUM(COALESCE(InvBalanceSourceCurrency,0)) AS RawSum
FROM AR
WHERE ABS(COALESCE(InvBalanceSourceCurrency,0)) > 0.004;
```
If `ArRows > DistinctWbs1Invoice`, F2 is live and `RawSum` is the inflated figure currently on the AR tile.

**3.3 — GL P&L tile vs tab gap**
```sql
SELECT CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END AS Bucket,
       SUM(Amount) AS Total
FROM GLSummary
WHERE Period >= <maxPosted-11> AND Period <= <maxPosted>
GROUP BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Org,'')))) = 'USA' THEN 'USA' ELSE 'CAD' END;
```
Sizes F4 exactly.

---

## Part 4 — Test coverage map

**87 test cases** across 23 files. Hermetic (no Deltek) except `LoaderIntegrationTests` (3 cases, `[Trait("Category","Integration")]`, requires `DELTEK_DSN/USER/PASSWORD` env vars — **excluded from CI, so effectively never run**).

| Layer | Covered by | Verdict |
|---|---|---|
| Headline KPI arithmetic | `FinancialCalculationTests` (25) | **Good** |
| Delivery confidence | `DeliveryConfidenceCalculatorTests` (9) | **Good** |
| KPI ↔ drilldown reconciliation | `ReconciliationTests` (5) | **Good** — but KPI paths only |
| Metric dictionary presence/format | 5 files (26) | **Good** for prose, blind to formulas (**F8**) |
| Alert ↔ KPI parity | `AlertPathParityTests` (2) | **AR only** — Backlog/Over-Budget unguarded (**F3**) |
| FX plumbing | `FxPlumbingTests` (1) | **Thin** — asserts caption strings, not converted values |
| **Deltek loaders (SQL)** | integration-only, CI-excluded | **NAKED** |
| **`Kor.Operations.Business\*` services** | — | **NAKED** — no unit tests at all |
| **WIP sign convention** | — | **NAKED** (**F1**) |
| **AR dedupe** | — | **NAKED** (**F2**) |
| **App↔MCP scope parity** | — | **NAKED** (**F5**) |
| Forecast (Theil-Sen / seasonal) | — | **NAKED** |
| Billing Manager Report | — | **NAKED** |
| GL P&L / Billed P&L builders | — | **NAKED** |

The pattern: **everything that computes in C# from a synthetic snapshot is well tested; everything that computes in SQL is not tested at all.** Every finding above F7 lives in the untested half.

---

## Part 5 — Suggested edit order

Re-ordered after verification. All directions are now known — nothing below is blocked on further investigation except where noted.

1. **F1 + F1a + F1b (WIP)** — highest value, fully diagnosed. Flip the sign in `WipFinancialsService` (both the RG branch `SUM(-Unbilled)` and the proxy branch `SUM(Billed − Revenue)`), make `LoadFirmwideWipProxyBalance` follow the same branch the drilldown took so firmwide and drilldown tie out, and correct the `DetectRevenueGeneration` comment + `WipTool` description (RG is **ON** at KOR). Then the ~$759k currently reported as overbilled reads correctly as earned, and the WPF tile can be un-hidden (`hideUnbilledEarned = false`). Ships to MCP `get_wip` immediately — this is what `/ask` is answering from today.
2. **F3 (Backlog alert)** — one-line change to match the KPI expression, plus the missing parity test. Zero risk.
3. **F5 (MCP backlog scope)** — add the three overhead-prefix exclusions + `'ACTIVE'` to `BacklogService`.
4. **F2 (AR collections dedupe)** — **remove** the `HashSet` dedupe in `LoadInCollectionsByWbs1Sync` (verified: balances are genuinely distinct, not replicated). Do **not** add dedupe elsewhere. Add an `ORDER BY` regardless, so behaviour is deterministic.
5. **F4 (GL tile vs tab)** — needs your call. The two read **different GL tables** (1001 vs 1500) *and* different Org scopes (~$464k CAD-equiv). Decide the canonical pair, then either align both or rewrite the caption.
6. **F6 (singleton mutation)** — make the Recalculate path pass a per-run options copy instead of writing the DI singleton.
7. **F7 / F8 / F9** — definition alignment. Needs a decision from you on which definition is canonical before any code moves.
8. **F10, F11, F12a–e, g** — hygiene, batch them. **F12(f) is withdrawn — leave the date bindings alone (§3.4).**

Nothing above requires a schema change or a Deltek write.

---

## Part 6 — Independent verification, round 2 (Codex adversarial pass + follow-up queries)

An independent read-only adversarial review was run against this document, followed by four more
read-only Deltek queries (`deltek-audit-probe3.ps1`). Net result: **the audit's direction held on
every finding, but three claims were overstated and one was materially wrong on amounts.**

### Accepted corrections

| # | Correction | Effect |
|---|---|---|
| C1 | **F1 dollar figures were wrong.** Published as ±$759,414.54; that was an un-FX'd CAD+USD mix. FX-corrected firmwide net is **±$60,988.94** (proxy basis). Worse, `earned`/`overbilled` split **per project** before summing, so no firmwide aggregate can produce them at all. | Sign finding stands and is now better evidenced (see F1). All amounts restated. **Do not quote earned/overbilled until a per-WBS1 query is run.** |
| C2 | **F1's "strict negation" was overstated.** `RevenueLoader` aliases revenue as `BilledFee else Revenue`; `WipFinancialsService` uses raw `Revenue`. Verified delta: **$190,001.99 across 1,485 rows** where the two disagree. | The two paths are *not* exact negations. Any fix must first decide which revenue definition is canonical — flipping a sign alone will not reconcile them. |
| C3 | **F7's user-visible impact was wrong.** `CfoMetricRegistry` is instantiated **only in tests**; `ProjectData.FromProject` has **no production caller**; `ProjectFinancialDetailWindow` references `ProjectData` only as a parameter type on two private helpers. | The whole `CfoMetrics` subsystem is **dead code**. F7 downgrades from "users see a wrong number" to "dead code carrying a divergent definition." Severity MEDIUM → LOW. Still worth deleting or wiring up — but it is not a live defect. |
| C4 | **F6's blast radius was over-scoped.** MCP builds its **own** `DeltekOdbcOptions` in `Kor.Operations.Mcp\Program.cs:43-67`, so the WPF singleton mutation does **not** cross into MCP. | F6 is process-wide **within the WPF app only**. Still real, smaller blast radius. |
| C5 | **F5 is mostly inert at KOR.** Verified: `Status='A'` and `IN ('A','ACTIVE')` both return **851** projects (zero rows use `'ACTIVE'`). The 60 overhead-prefix projects MCP wrongly includes carry **$0.00 total Fee**. | Fee impact is **zero**. Residual real impact: `ActiveProjectCount` overstated by **60**, and any billings booked to overhead WBS1 leak into the billed side. Severity MEDIUM-HIGH → LOW-MEDIUM. |

### Findings the review contributed

| # | Finding |
|---|---|
| C6 | **`Exec_Backlog` dictionary formula contradicts the code, and the drift gate passes it.** `Definitions.Executive.cs` states `SUM(PRSummaryMain.Billed)`; `BacklogService.cs:182-184` uses `SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END)`. The gate for `Exec_Backlog` only checks the substrings `PR.Status='A'`, `HourlyRevenue`, `PRSummaryMain`, `LedgerAR` — none of which detect a wrong aggregate. Concrete instance of **F8**. |
| C7 | **`BacklogService` returns `DataLoaded: true` after partial loader failure.** Four loaders are caught independently and substituted with empty collections, then the result reports success. A Deltek hiccup yields a confidently-wrong backlog rather than an error. |
| C8 | **`WipFinancialsService` has the same shape** — drilldown failure → `Array.Empty`, firmwide failure → `(0,0,0)`, and both `RevenueGenerationDetected` and `DataLoaded` still return `true`. |

### Finding this pass added (missed by both the original audit and the review)

**F13 — MCP's `FinancialsOptions` is missing five keys, and one of them silently disables the cash-account whitelist. ⚠ HIGH**

`Kor.Operations.Mcp\Program.cs:51-64` populates 13 `FinancialsOptions` properties. It never populates
`CashAccountWhitelist`, `CashUsdAccounts`, `CashUsdToCadRate`, `PnLOverheadRate`, or
`FiscalYearStartMonth`. Four of those degrade to defaults that happen to match `App.config` (1.36,
1.65, month 1) — harmless. **`CashAccountWhitelist` does not.**

`CashFinancialsService.LoadBankAccounts` filters only when the whitelist is non-empty:
`if (whitelist.Count > 0 && !MatchesAccountSet(account, whitelist)) continue;`
An empty whitelist therefore means **no filtering at all**.

Verified against `CFGBanks`: **20 bank accounts exist, `App.config` whitelists 3.**

| WPF (whitelisted) | MCP additionally includes |
|---|---|
| 1110.00 CAD, 1120.00 CAD, 1170.00 USA | 1000.00, 1100.00, 1115.00, 1130.00, 1135.00, 1150.00, 1155.00, 1156.00, 1157.00, 1162.00, 1163.00, 1498.00, 1499.00 (CAD); 1175.00, 1497.00, 1499.50 (USA); 1190.00 (BCC) |

`App.config` documents the intent explicitly — *"Operating cash accounts (Daler 2026-05-14): CAD entity
= 1110+1120, USA entity = 1170 only. Excludes petty cash (1000) and USD savings (1175)."* MCP applies
none of it. **`get_cash` — which is what `/ask` answers Cash Position from — reports a different and
larger figure than the WPF Cash tile, including petty cash and USD savings that Daler explicitly
excluded.** This is code-certain and does not depend on what `appsettings.Production.json` contains,
because `Program.cs` never reads those keys into the options object.

### Verdict

Of the original 12 findings: **7 stand as written, 3 corrected in scope or amount (F1, F5, F6),
1 downgraded to dead code (F7), 1 previously withdrawn (F12f).** Two new findings added (C6/C7/C8
cluster, and F13). The direction of every fix in Part 5 is unchanged except that **F1 must resolve the
revenue-definition question (C2) before the sign flip**, which is also the independent review's
ship-blocker.
