# Module Audit — Financials & the Deltek Vantagepoint Data Layer

**Audited 2026-08-20** · Scope: `Kor.Operations.App\Financials` (57 .cs / 16,957 LOC),
`Kor.Operations.Business` (26 .cs / 6,063 LOC), `Kor.Operations.Data` (22 .cs / 3,564 LOC).
Machine: KOR-1001, on the KOR LAN. Read-only throughout — SELECT only, no writes to any
server, DB, service or share.

---

## 1. What I searched

**Prior art read before running anything** (CLAUDE.md rules 1 & 2):

- `docs/audit-2026-08/00-INVENTORY.md` — the inventory artefact I was handed.
- `docs/KOR-Financials-Forensic-Audit-2026-07-26.md` (496 lines) — a complete prior forensic
  audit of this exact module, with 12 findings (F1–F12), a Codex adversarial pass (C1–C8) and
  F13. **Read in full.** Dated 2026-07-26; the code it describes last changed 2026-08-01, so
  per rubric rule 2 I treated every one of its claims as a hypothesis and re-checked the load-
  bearing ones. Several are now stale — see §5.
- `git log --since=2026-07-20 -- <the three scope paths>` → 7 commits, including
  `818ebc19` *"fix(financials): WIP sign convention was inverted in both branches"* (2026-07-31)
  and `594a2cde` *"delete the dead CfoMetrics subsystem"* (2026-08-01).

**Greps / file reads:** `1.36` across `*.cs|*.config|*.json`; `Dsn=|DSN=|ConnectionString|Uid=|Pwd=`;
`password=|pwd=|uid=|api[_-]?key=` (hardcoded-secret scan); `KOR_ODBC_*`; `dbo\.[A-Za-z0-9_]+`
per-file (built the lineage table); `ParseUsdToCadRateTable` vs `BilledUsdToCadRate`;
`TODO|FIXME|HACK|NotImplementedException|NotSupportedException`; `catch\s*(\(...\))?\s*\{\s*\}`;
`BillingManagerReportViewModel`; `SecurityGroup|FinancialsTileHost`. Full reads of
`VpOdbcDsnFactory.cs`, `DeltekCatalogValidator.cs`, `DeltekSchemaValidator.cs`, `OrgFx.cs`,
`WipFinancialsService.cs`, `RevenueLoader.cs`, `App.config`, `Kor.Operations.Mcp/Program.cs`.

**Live Deltek queries — 20 read-only SELECTs** over system DSN `Deltek` (DataDirect HDP 4.6),
credentials from Machine env vars `KOR_ODBC_USER` / `KOR_ODBC_PASSWORD`, catalog
`C0000052267P_1_KOR00000000`. Probe scripts in the session scratchpad (`probe1..7.ps1`,
`schemaval.ps1`, `wip.ps1`, `timing.ps1`). Covered: max period per table; `Unbilled` population;
`Billed`/`Revenue` sign storage; period→calendar-date mapping via `LedgerAR` and
`CFGAcctngCalendarData`; the full `DeltekSchemaValidator` column contract; the firmwide WIP
split under all three formulas; `PR.Stage`/`LostTo`/`Probability`/`OpportunityID` population;
`GLSummary` columns; query latency.

**Revenue-Generation resolution (follow-up round, 10 further read-only SELECTs):** firm-wide
`SUM(Revenue)`; row-level `Revenue` vs `Billed` equality; `Unbilled` bucketed by `PR.Org`; `Unbilled`
by `Period` across the full history; per-period `Revenue`-populated vs `Unbilled`-populated row
counts; the overlap between `Revenue<>Billed` rows and `Unbilled` rows; distinct `PR.Org` values.
Scripts `rg.ps1`, `rg2.ps1`, `rg3.ps1`.

**Deployed config + host security read:** `\\kor-app01\C$\Program Files\KorOperations\Mcp\appsettings.json`
and `appsettings.Production.json`; `icacls` on that file and its parent directory;
`net view \\kor-app01` for the non-administrative share list.

**Build / test:** `dotnet build Kor.Operations.App.Tests.csproj -c Debug` → 0 errors, 13 warnings.
`dotnet test --no-build --filter "FullyQualifiedName~Financials"` → **176 passed, 0 failed, 2s**.
Full suite deliberately not run (rubric rule 4).

---

## 2. What this module is

This is KOR's reporting and analysis layer over Deltek Vantagepoint, the firm's accounting and
project-management system. Deltek holds the ledgers but its own reporting is Crystal-report-shaped
and slow to change, so this module reads Deltek directly over ODBC and renders the numbers a
principal actually wants: what is billed and unbilled, who owes us money and for how long, what
the backlog is, whether staff are utilized, how each partner's book is performing, and a GL and
billed profit-and-loss. It is the single largest feature folder in the desktop app, and it is the
data foundation the conversational "virtual CFO" sits on — the MCP AI tools (`get_wip`, `get_ar`,
`get_cash`, `get_backlog`, `get_billed_pnl`, `get_utilization`, `get_firm_health`, and others)
call the *same* service classes in `Kor.Operations.Business`, so whatever is true of the desktop
numbers is true of the AI's answers.

A user opens the Financials window from a tile on the app's home screen and lands on a
six-section window: **Overview** (an active-project grid with budget burn and delivery-confidence
scoring), **Executive Summary** (13 KPI tiles, 4 trend series, 4 alerts, each with a click-through
drilldown and a formula tooltip backed by a built-in metric dictionary), **P&L Report** (the GL
income statement, org- and period-filterable), **Partner Financials** (per-partner billed rollup
with per-year FX), **Clients** (client portfolio rollup with collections exposure), and **Revenue
Forecast** (Theil-Sen trend plus seasonal adjustment). Three further windows launch from here:
Staff Utilization, Historical Analytics and Collections. Every tile is clickable down to the
invoice or project row behind it, which is the thing that demos well.

### 2a. Data lineage — Deltek ODBC → table → transformation → surface

Connection is built by `Kor.Operations.Data\VpOdbcDsnFactory.cs:37` as `DSN=Deltek;UID=…;PWD=…`
(no driver/server literals). Every query interpolates a **three-part** name `[catalog].dbo.Table`,
where catalog is validated by `DeltekCatalogValidator.ResolveCatalog` against `^[A-Za-z0-9_]+$`
(injection-safe). No `USE` statement anywhere. `LEFT JOIN … PR ON pr.WBS1 = sm.WBS1 AND (pr.WBS2
IS NULL OR '' ) → LEFT JOIN Clendor ON cc.ClientID = pr.ClientID` is the standard client path.

| # | Surface (UI) | Service / loader | Deltek tables | Key transformation | Data currency |
|---|---|---|---|---|---|
| 1 | Overview grid, budget burn, delivery confidence | `FinancialsService` (11 parallel loaders) | `PRSummaryMain`, `PR`, `AR`, `LedgerAR`, `tkDetail`, `apDetail`, `EMMain`, `Clendor`, `ProjectCustomTabFields`, `CFGAcctngCalendarData` | `FeeBilled = CASE WHEN BilledFee<>0 THEN BilledFee ELSE Revenue END`; overhead WBS1 prefixes `[A-Z]%`,`9[A-Z]%`,`99%` excluded; `Status IN ('A','ACTIVE')` | **Mixed — mostly Feb 2026** |
| 2 | Exec Summary — WIP tile *(hidden)* + MCP `get_wip` | `WipFinancialsService` | `PRSummaryMain`, `PR`, `EMMain`, `Clendor` | RG branch: `Net = SUM(Unbilled)`; proxy branch: `Revenue − Billed`. FX per Org bucket, **then** `SplitWipNet` → `Earned=Max(net,0)`, `Overbilled=Max(−net,0)` **per project** | **Feb 2026** |
| 3 | Exec Summary — AR Outstanding, DSO, collections exposure; MCP `get_ar` | `ArFinancialsService` | `AR`, `PR`, `Clendor`, `EMMain` | raw `SUM(InvBalanceSourceCurrency)`, aged buckets by `InvoiceDate`/`DueDate`, USA FX'd | **Live (today)** |
| 4 | Exec Summary — Cash / Liquidity; MCP `get_cash` | `CashFinancialsService` | `GLSummary`, `CFGBanks` | account whitelist `1110.00,1120.00,1170.00`; running balance from `SUM(Amount)`; USA bucket × `Cash.UsdToCadRate` | **Feb 2026** |
| 5 | Exec Summary — Utilization; MCP `get_utilization` | `UtilizationService` | `tkDetail`, `EMCompany` | `RegHrs` by `LaborCode` (10/20/30/40/50/60/70/80), billable ÷ available | **Live (today)** |
| 6 | Exec Summary — firm health; MCP `get_firm_health` | `FirmHealthService` | `LedgerAR`, `PR`, `tkDetail`, `EMCompany` | receipts vs billings by period | **Live (today)** |
| 7 | Exec Summary — Backlog; MCP `get_backlog` | `BacklogService` | `PR`, `PRSummaryMain`, `LedgerAR`, `Clendor`, `EMMain` | `backlog = Fee − (billedPosted + unpostedOverlay)`, incl. T&M `HourlyRevenue` | **Mixed — Feb 2026** |
| 8 | Exec Summary — revenue/invoiced trends | `Loaders\RevenueLoader` | `PRSummaryMain`, `PR`, `LedgerAR`, `CFGAcctngCalendarData` | revenue aliased `BilledFee else Revenue`; 12-point series; period-end from calendar table **or** YYYYMM fallback | **Feb 2026** |
| 9 | Exec Summary — Net Income (T12mo) tile | `Loaders\GlPnlT12moLoader` | `GLSummary`, `GLTable`, `GLGroupDetail`, `GLParentDetail`, `GLParentGroup` | picks GL table by `ScoreTable`; **no Org filter**, buckets CAD/USA and FX-converts | **Feb 2026** |
| 10 | P&L Report tab; MCP `get_gl_pnl` | `GlProfitLossService` + `GlProfitLossPresenter` | `GLSummary`, `GLTable`, `GLGroup*`, `GLParent*`, `Ledger AP/AR/EX/Misc`, `AR`, `Clendor`, `EMMain` | `OrgFilter = BilledDefaultOrg` (**`CAD`** in App.config); `GlFlipSign=true` | **Feb 2026** |
| 11 | Billed P&L; MCP `get_billed_pnl` | `BilledFinancialsService` | `GLSummary`, `Ledger AP/AR/EX/Misc`, `CA`, `AR`, `PR`, `Clendor`, `EMMain` | revenue accts `4001/4003/4210/4220/4240`; expense ranges 5xxx–7xxx less `7290,7970` plus `8200,8300`; USA FX'd only when Org filter is null | **Mixed** |
| 12 | Partner Financials tab | `PartnerFinancialsViewModel` | via `BilledFinancialsService` | **per-year FX** `2024:~1.3698, 2025:1.3985, 2026:1.378457` | **Mixed** |
| 13 | Clients tab | `FinancialsService.LoadClientPortfolioSync` | `PR`, `PRSummaryMain`, `AR`, `Clendor` | client rollup + `arSum` subquery | **Mixed — Feb 2026** |
| 14 | Revenue Forecast | `FinancialsViewModel.RecomputeForecast` | *(none — in-memory)* | Theil-Sen slope + seasonal index over the snapshot | derived |
| 15 | Portfolio trend history | `SqlFinancialPortfolioSnapshotStore` | **not Deltek** — `KorTransmittalsDb` SQL | app-written snapshot rows for YoY/trend | snapshotted |

---

## 3. How you would demo it

**Prerequisites:** (a) on the KOR LAN or VPN — the Deltek ODBC DSN is an internal endpoint;
(b) 64-bit system DSN `Deltek` (DataDirect HDP 4.6) installed on the demo machine;
(c) Machine env vars `KOR_ODBC_USER` / `KOR_ODBC_PASSWORD` set; (d) `Kor.Operations.App` running.
No service needs to be up for the desktop path. For the AI "virtual CFO" path the MCP service on
KOR-APP01 must be running (audited separately).

**Click path:** Launch `Kor.Operations.App` → Home → **Financials** tile → the window opens on
**Overview**. `[RUN]` A cold connect measured **960 ms**, and the four representative loader
queries **252 / 82 / 180 / 77 ms** — total wall **1,559 ms** — so the window populates in about a
second and a half. Then: **Executive Summary** for the KPI wall (tiles show a scope badge —
Firmwide vs Scoped — and a formula tooltip); click any tile for its drilldown grid; **P&L Report**
for the GL income statement; **Partner Financials** for the per-partner rollup; **Clients**;
**Revenue Forecast**. `Ctrl`-level extras: **Export to Excel**, and **Open Collections Case**
from the AR drilldown.

**This is demoable today.** It builds, it runs, it connects, it is fast, and the drill-through is
genuinely impressive. The caveat is not mechanical but factual: see §8 risk 1 — most headline
figures are as of **February 2026**, and the window does not say so prominently.

---

## 4. Completeness

| Capability | State | Evidence |
|---|---|---|
| Deltek ODBC connectivity (DSN, injection-safe catalog) | `WORKING` | `[RUN]` connected, 20 SELECTs returned |
| Schema-drift detection (`DeltekSchemaValidator`) | `WORKING` | `[RUN]` all **34** expected (table,column) pairs present against 676 live columns → **CLEAN, would pass** |
| WIP sign convention | `WORKING` | `[RUN]`+`[QUERIED]` fixed in `818ebc19`; live split reproduces exactly |
| WIP **branch selection** (RG vs proxy) | `PARTIAL` — wrong branch | `[QUERIED]` RG is off, but `UnbilledColumnHasAny()` sees 238 stray rows and takes the RG branch (§5.2) |
| Firm-wide WIP as a *meaningful number* | `UNKNOWN` | `[QUERIED]` both branches derive from the same 0.5% of rows; `PRSummaryMain` carries no usable WIP with RG off (§5.2) |
| WIP tile on screen | `STUBBED` *(deliberately hidden)* | `[READ]` `ExecutiveSummaryService.cs:398` `var hideUnbilledEarned = true;` |
| AR outstanding / ageing / DSO / collections | `WORKING` | `[QUERIED]` `AR` current to 2026-08-20 |
| Utilization | `WORKING` | `[QUERIED]` `tkDetail` current to 2026-08-23 |
| Cash / liquidity | `PARTIAL` | `[QUERIED]` correct logic, but `GLSummary` ends 202602 |
| Backlog | `PARTIAL` | `[READ]`+`[QUERIED]` formula sound; `PRSummaryMain` ends 202602 |
| GL P&L tab | `PARTIAL` | same staleness; tile-vs-tab divergence below |
| Billed P&L / Partner Financials | `PARTIAL` | works; two FX regimes (§5.3) |
| Revenue forecast (Theil-Sen + seasonal) | `WORKING` | `[READ]` in-memory, no Deltek dependency |
| Metric dictionary + tooltips | `WORKING` | `[RUN]` 176 tests incl. dictionary presence/format |
| MCP/AI parity with the desktop | `PARTIAL` | `[QUERIED]` deployed `BilledDefaultOrg=""` ≠ App.config `CAD` (§5.4) |
| Billing Manager Report | `DEAD` — **removed** | `[RUN]` no `BillingManagerReportViewModel` in tree; superseded by Partner Financials (`10d14ce3`) |
| `CfoMetrics` subsystem | `DEAD` — **removed** | `[RUN]` deleted in `594a2cde` |

**Markers in scope** — `TODO` **0** · `FIXME` **0** · `HACK` **0** · `NotImplementedException` **0** ·
`NotSupportedException` **5** · empty `catch {}` **44**.

The 5 `NotSupportedException` are all WPF `IValueConverter.ConvertBack` stubs
(`BillingManagerConverters.cs:35,49,76`, `BoolToActualLabelConverter.cs:15`,
`RevenueBarHeightConverter.cs:27`) — idiomatic, not defects. Of the 44 empty catches, the large
majority are `ct.Register(() => { try { cmd.Cancel(); } catch { } })` best-effort cancellation
callbacks and `catch (OperationCanceledException) { }` — also benign. The ones that matter are in
§5.5, and they are *not* empty catches but logged catches that then report success.

---

## 5. What is broken or risky

### 5.1 The summary ledgers stop at February 2026 — six months stale ⚠ **HIGHEST**

`[QUERIED]` `MAX(Period)` is **202602** in both `PRSummaryMain` and `GLSummary`. There are **zero**
rows for 202603–202608. Meanwhile `AR` carries invoices to **2026-08-20** (today), `tkDetail` to
**2026-08-23**, and `LedgerAR` to period **202608**.

I verified `Period` is calendar `YYYYMM` and not a fiscal counter, because `LedgerAR` period
**202608** spans `08/03/2026`–`08/25/2026`. So 202602 is unambiguously **February 2026**.

Everything in lineage rows 1, 2, 4, 7, 8, 9, 10, 13 is therefore as-of Feb 2026: **WIP, Cash,
Backlog, Net Income (T12mo), the GL P&L, the revenue trends, the Clients tab and the Overview
grid's billed-to-date.** The "Net Income (T12mo)" tile is a trailing twelve months
**ending six months ago** (202503–202602) while captioned as current.

This is a Deltek-side posting/Revenue-Generation gap, not a code bug — but the app presents the
numbers without a prominent staleness banner, and "real-time" is the claim KOR intends to make.
The honest framing: **the pipeline is real-time (1.5 s end-to-end), the ledger behind it is not.**
Rows 3, 5, 6 (AR, utilization, firm health) *are* genuinely current.

### 5.2 Revenue Generation is **OFF** — I got this wrong in my first pass, and so did the prior audit

**This section supersedes my earlier finding. The standing internal record is correct: RG is OFF.**
Both I and `KOR-Financials-Forensic-Audit-2026-07-26.md` (F1a) concluded "RG is ON" by trusting the
application's own detector. The detector is wrong, and here is the proof.

**The decisive test** `[QUERIED]` — is `PRSummaryMain.Revenue` an independent revenue-recognition
figure, or a mirror of `Billed`?

```sql
SELECT SUM(CASE WHEN ABS(COALESCE(Revenue,0)-COALESCE(Billed,0))<0.01 THEN 1 ELSE 0 END) AS RevEqualsBilled,
       SUM(CASE WHEN ABS(COALESCE(Revenue,0)-COALESCE(Billed,0))>=0.01 THEN 1 ELSE 0 END) AS RevDiffers
FROM [C0000052267P_1_KOR00000000].dbo.PRSummaryMain;
```

**`Revenue` equals `Billed` on 47,246 of 47,366 rows — 99.75%.** Only 120 rows differ. A tenant
running Revenue Generation would show recognized revenue diverging from billings on most active
projects, every month. KOR's does not. Deltek is simply mirroring billings into `Revenue`.

Three supporting results, all `[QUERIED]`:

| Test | Result | Reading |
|---|---|---|
| `Unbilled` population | **238** of 47,366 rows = **0.5%** | Not a working WIP column |
| `Unbilled` by period | **1–3 rows per month**, scattered continuously from **201901 to 202512** | **Not a toggle.** No date where it switches on — this is a 7-year residue of manual/legacy entries |
| `Unbilled` by Org | CAD **139** rows (+$2,440,887.53), USA **99** rows (−$1,922,573.99), BCC **0** | **Not per-Org either.** Both trading orgs show the same sparse pattern (BCC has only 8 projects total) |
| Overlap | Rows where `Revenue<>Billed` **and** `Unbilled` populated: **120**. `Revenue<>Billed` **without** `Unbilled`: **0** | Outside those 238 rows, `Revenue` is a byte-for-byte copy of `Billed` |

So: **RG is off firm-wide, it was not toggled, and it is not per-Org.** The "was off, now on"
reconciliation is not available — the evidence does not support it.

**The battlecard claim was also wrong, in the opposite direction — good catch pulling it.**
`[QUERIED]` `SUM(Revenue)` firm-wide across all 47,366 rows returns **$69,061,768.57** over 17,466
non-zero rows — **not $0**. Precisely *because* RG is off, Deltek mirrors billings into `Revenue`,
so the column is fully populated. Saying "Revenue Generation is off here, so `SUM(Revenue)` returns
$0 firm-wide" would have been falsifiable in about fifteen seconds by anyone with catalog access.

> **The sentence the owner can say out loud:**
> *"Revenue Generation is off in our Vantagepoint tenant — Deltek mirrors billings into the Revenue
> column, so `Revenue` equals `Billed` on 99.75% of rows and the Unbilled column is populated on
> half a percent. That's why we derive unbilled WIP ourselves rather than reading Deltek's WIP."*
>
> Behind it: `SELECT SUM(CASE WHEN ABS(COALESCE(Revenue,0)-COALESCE(Billed,0))<0.01 THEN 1 ELSE 0 END),
> COUNT(*) FROM [<catalog>].dbo.PRSummaryMain;` → **47,246 / 47,366**.

**The consequence, which is a new defect** ⚠ — `WipFinancialsService.cs:140-147`
`UnbilledColumnHasAny()` is `SELECT COUNT(*) … WHERE ABS(COALESCE(Unbilled,0)) > 0.01`, a bare
non-zero test with no threshold and no ratio. 238 stray rows make it return true, so
`WipFinancialsService` takes the **Revenue-Generation branch** (`Net = SUM(Unbilled)`) on a tenant
where RG is off. It is reading the wrong column.

`[QUERIED]` The two branches disagree on the raw firm-wide basis: proxy `Revenue − Billed` =
**+$759,414.54** vs `Unbilled` = **+$518,313.54** — a **$241,101** gap. (That is the same $241,101
the prior audit's F1b saw between firmwide and drilldown; same two formulas, now explained.)

But the deeper point is that **neither branch yields a trustworthy firm-wide WIP**, because both
draw their entire signal from the same 238 rows — the proxy is zero wherever `Revenue = Billed`,
which is 99.75% of the table. With RG off, `PRSummaryMain` simply does not carry a usable unbilled-
services figure; a real WIP would have to be built from uninvoiced labour/expense detail
(`tkDetail`) instead. **Keeping the WIP tile hidden is therefore the correct call, not a gap** — but
MCP `get_wip` is still live and answering `/ask` from this residue. See §8 and §9.

*(The second half of the stored note stands: `BilledFee` is used, via the canonical earned-revenue
expression `CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END`. That is a definition choice
rather than an RG fallback, and it is unaffected by any of the above.)*

### 5.3 FX: configurable, but two regimes run side by side — **file:line answer to "is 1.36 hardcoded?"**

**It is not hardcoded — it is configurable**, and the single source of truth is
`Kor.Operations.Business\OrgFx.cs:18` (`DefaultUsdToCadRate = 1.36`) with the live value in
`Kor.Operations.App\App.config:60` (`Financials.Billed.UsdToCadRate = 1.36`) and
`App.config:76` (`Financials.Cash.UsdToCadRate = 1.36`).

Residual hardcoded literals, all *fallbacks* that only fire if config is missing:
`ExecutiveSummaryService.cs:392` and `:1071` (`snap?.UsdToCadRate ?? 1.36`),
`FinancialsService.cs:1479` (`UsdToCadRate { get; set; } = 1.36`),
`BilledFinancialsPresenter.cs:921` (`?? "1.36"` — in a *caption string* shown to the user).

**The real risk is not the hardcode, it is the inconsistency.** `App.config:64` supplies
per-year rates `2024:~1.3698, 2025:1.3985, 2026:1.378457`, verified against Daler's deck — but
`[READ]` **only `PartnerFinancialsViewModel.cs:585` consumes them.** WIP, AR, Backlog, Billed P&L,
Cash, Compensation and Utilization all use the flat **1.36**. So in the same window, Partner
Financials converts USA work at **1.378457** and the Billed P&L converts it at **1.36**. They
cannot reconcile, and 1.36 is demonstrably not KOR's 2026 reporting rate. `[RUN]` My live WIP run
at 1.36 produced net **−$173,813.10**, where commit `818ebc19` quotes **−$209,298.04** at
1.378457 — the same code, two rates, a $35k gap.

### 5.4 The AI layer answers the P&L from a different Org scope than the screen ⚠ **demo-critical**

`[QUERIED]` deployed `\\kor-app01\C$\Program Files\KorOperations\Mcp\appsettings.Production.json`
sets `"BilledDefaultOrg": ""`. `[READ]` `BilledFinancialsService.cs:758`
`NormalizeOrgFilter("") → null`, and `:107` `convertUsaToCad = (org == null)` → **true**. The WPF
app sets `BilledDefaultOrg=CAD` (`App.config:73`), which excludes USA rows entirely.

So `get_billed_pnl` / `get_gl_pnl` via `/ask` include FX-converted USA rows that the P&L tab on
screen excludes. App.config's own comment sizes this: *"Mar 2026: app +$77,620 over Daler =
$57,073 USD × 1.36."* Ask the virtual CFO for the P&L next to the P&L tab and you get two
different numbers on stage.

**Cleared:** the prior audit's **F13** (MCP missing `CashAccountWhitelist`) is **FIXED** — the
deployed file now carries `"CashAccountWhitelist": "1110.00,1120.00,1170.00"` and `Program.cs:66`
reads it. The empty `BilledRevenueAccounts`/`ExpenseAccountRanges`/`Excludes`/`Includes` in that
same file are **harmless**: `ParseAccounts(config, DefaultRevenueAccounts)` falls back to
hardcoded defaults (`BilledFinancialsService.cs:39-67`) that match App.config exactly.

### 5.5 Loaders report success after partial failure

`[READ]` `WipFinancialsService.cs:84-117` — the project-breakdown loader catches and substitutes
`Array.Empty`, the firmwide loader catches and substitutes `(0,0,0)`, and the method then returns
`RevenueGenerationDetected: true, DataLoaded: true` **unconditionally** (`:117-118`).
`BacklogService.cs:53,61,69,77` catches four loaders independently and returns
`DataLoaded: true` at `:131`. A transient Deltek hiccup yields a confident **$0** rather than an
error state the UI can badge. (Prior audit C7/C8 — **still live**.)

### 5.6 Access control fails OPEN when it cannot reach a domain controller — **affects how the demo is run**

**The gate itself is correct.** `[READ]` `App.config:121` restricts
`SecurityGroup.Financials.Members` to four named users (Markulin, Lalonde, Desroches, Singh), and
`HomeWindow.xaml.cs:235-236` collapses the tile for anyone else. *(The commented-out example at
`App.config:110` is not the live key — I initially misread it as the only one.)*

**The failure mode is the defect.** The whole gating block sits in one `try`, and its bare `catch`
at `HomeWindow.xaml.cs:295-308` does not re-apply the gate — it force-shows the surfaces:

```csharp
catch
{
    FinancialsTileHost.Visibility = Visibility.Visible;
    CompensationTileHost.Visibility = Visibility.Visible;
    PmToolsTileHost.Visibility = Visibility.Visible;
    StandardDetailsTileHost.Visibility = Visibility.Visible;
    GeneralToolsCard.Visibility = Visibility.Visible;
    FeeProposalBuilderCard.Visibility = Visibility.Visible;
    EngineeringToolsTileHost.Visibility = Visibility.Visible;
    FileSyncCommandCenterTileHost.Visibility = Visibility.Collapsed;   // BD surfaces fail CLOSED
    MondayBriefingCard.Visibility = Visibility.Collapsed;
    CooCardCard.Visibility = Visibility.Collapsed;
    OpportunitiesTileHost.Visibility = Visibility.Collapsed;
    BusinessDevelopmentTileHost.Visibility = Visibility.Collapsed;
    BdReportsTileHost.Visibility = Visibility.Collapsed;
}
```

`SecurityGroupAccess.IsUserInGroup` resolves AD group membership. Off the KOR LAN with no VPN, that
lookup cannot reach a domain controller and throws — landing in this `catch`.

**Which surfaces fail which way** `[READ]` — **seven fail OPEN**: Financials (firm P&L, AR, cash,
backlog), **Compensation** (salary and bonus data — the most sensitive surface in the suite), PM
Tools, Standard Details, General Tools, Fee Proposal Builder, Engineering Tools. **Six fail
CLOSED**: FileSync Command Center, Monday Briefing, COO Card, Opportunities, Business Development,
BD Reports. The inversion is worst exactly where it matters: the two money surfaces open up, while
the harmless BD surfaces lock down.

**What this means for the demo, concretely.** The demo is being run **off the KOR LAN, at MVE's
office in Southern California** — precisely the trigger condition. Two scenarios:

- *On VPN* — AD is reachable, the gate evaluates normally, tiles behave as configured. Fine.
- *VPN drops, or the app is launched before the tunnel is up* — the lookup throws and Home renders
  with **Financials and Compensation visible**. If the demo is driven from Ian's own account this
  changes nothing he can see (he is in both groups); the real exposure is that the failure is
  **silent**, and the app is now in a state where those tiles show for whoever is logged in.

**Recommendation before travelling** — in order of preference:

1. **Fix the `catch`** (S, ~30 min): on exception, collapse `FinancialsTileHost` and
   `CompensationTileHost` instead of showing them — fail closed on the two sensitive surfaces while
   keeping today's fail-open behaviour for the harmless ones (Engineering Tools, Fee Proposal
   Builder) so an offline laptop stays useful. This is the correct fix and it is small.
2. **If no code change ships:** bring up the VPN *before* launching the app, and pre-flight the Home
   screen to confirm the expected tile set. Do not launch cold on hotel or MVE guest wifi and assume
   the gate held.

This is a *confidentiality* risk, not a demo-breakage risk — nothing crashes, and the Financials
window works either way.

### 5.7 Plaintext secrets in the deployed config — **highest-severity security finding**

**File:** `\\kor-app01\C$\Program Files\KorOperations\Mcp\appsettings.Production.json`
(1,246 bytes, last modified 2026-05-14).

`[QUERIED]` It stores four live secrets in cleartext: the **Deltek ODBC username and password**, a
**live Anthropic API key** (`sk-ant-api03-...`), the **`KorMcp` SQL connection string** including
the `mcp_app` password, and the **MCP basic-auth password**. Values deliberately redacted from this
report; they are in the file itself.

**NTFS permissions** `[QUERIED]` via `icacls` — every ACE is inherited (`(I)`) from
`C:\Program Files`; the file has **no hardening of its own**:

| Principal | Rights |
|---|---|
| `NT AUTHORITY\SYSTEM` | Full |
| `BUILTIN\Administrators` | Full |
| `kor\app-admin` | Full |
| **`BUILTIN\Users`** | **Read & Execute** |
| `ALL APPLICATION PACKAGES` / `ALL RESTRICTED APPLICATION PACKAGES` | Read & Execute |

**Who can actually read it.** `BUILTIN\Users` on a domain-joined server includes **Domain Users**,
so the file's own ACL grants read to effectively every KOR employee account. What currently
prevents remote mass access is *not* the ACL but the path: it sits under the `C$` administrative
share, which requires local-admin membership, and `[QUERIED]` `net view \\kor-app01` shows the only
non-administrative share on the host is **`QueueDrain`** (a BD research queue directory, unrelated
to this path). **The secrets are protected by an accident of file layout, not by design.** Anyone
who obtains any interactive or remote session on KOR-APP01 — RDP, local logon, a scheduled task, or
any process running under any domain account — reads all four secrets with no privilege escalation.

**What an attacker on the LAN gains:** the Deltek credential is read access to the firm's *entire*
accounting catalog — every project, invoice, client and salary-bearing table (I used exactly this
credential for all 20 audit queries, so its reach is not theoretical). The Anthropic key is
billable spend against KOR's account plus the ability to impersonate the firm's AI services. The
SQL credential opens `KorMcp`. One file, readable by anyone with a foothold on that host, yields
the firm's financial data and its AI spend.

**Source and git history are clean — and that distinction matters.** `[RUN]` The hardcoded-secret
grep across all three scope projects returned **zero** hits; the application resolves credentials
from `KOR_ODBC_USER` / `KOR_ODBC_PASSWORD` Machine environment variables via
`Services\EnvironmentSecretOverrides.cs:17-34`. The repo copy of
`Kor.Operations.Mcp/appsettings.Production.json` is sanitized to empty values and carries the note
*"Real secrets live on KOR-APP01 only. Never commit."* `[RUN]` `git grep` at HEAD for the Anthropic
key, the Deltek password and `nucleus.prd` found **no** secret in any tracked file, and `git log -p`
over that file's full history found no `sk-ant-api03`. **This is a deployment-hygiene problem, not a
source-control leak** — nothing needs purging from history.

**Smallest change that fixes it**, in order. All are server changes, so **Ian runs these on
KOR-APP01** — not this session — and step 1 restarts the MCP service:

1. **Rotate all four secrets.** They have sat in cleartext and are now in this audit's evidence
   chain. The Anthropic key and the `mcp_app` SQL password are self-service; the Deltek credential
   goes through Deltek support.
2. **Strip the inherited `BUILTIN\Users` read ACE from that one file**, leaving SYSTEM,
   Administrators and the `kor\app-admin` service account — all the service needs:
   `icacls "<path>" /inheritance:d` then `icacls "<path>" /remove:g "BUILTIN\Users"`.
   *Side effect to confirm first:* the MCP service runs as `kor\app-admin`, which keeps Full
   control, so this should not disturb it — but verify the service account before applying and
   restart the service afterwards to confirm it still reads its config.
3. **Then migrate to the pattern the WPF app already uses** — Machine environment variables on
   KOR-APP01 read through the existing `EnvironmentSecretOverrides` path, so no secret sits on disk
   at all. That is the durable fix; steps 1-2 are the tourniquet.

One minor, separate leak: the Deltek **username** `52267.nucleus.prd` (no password) is committed in
`Kor.Operations.App/Scripts/20260807_filesync_kormapsync.sql:36`.

### 5.8 Smaller, real

- `[QUERIED]` **`CFGAcctngCalendarData` is empty — 0 rows.** `RevenueLoader.cs:209` reads it for
  period→date mapping. Benign today because `BuildSeries`/`PeriodEnd` (`:427-440`) falls back to
  parsing `YYYYMM` into a month-end, but it is an undetected empty dependency, and
  `FinancialsService` reads the same table.
- `[READ]` `DeltekSchemaValidator.cs:84-86` memoizes a `Lazy<Task>` per catalog that closes over
  the **first** `OdbcConnection`; a later caller can await a task bound to a disposed connection.
- `[READ]` `ExecutiveSummaryDeltekLoader.cs:116` — `internal static string Catalog { get; set; }`,
  process-global mutable state written from a *transient* constructor, read ambiently by
  `RevenueLoader` and `GlPnlT12moLoader`, while every `Business` service resolves its own
  `_catalog`. Two catalog regimes coexist. Harmless with one catalog.
- `[READ]` `FinancialsService` holds `_cache` but is registered **transient**
  (prior audit F11) — a second resolve silently re-runs 11 Deltek queries.
- `[READ]` GL tile vs GL tab still pick their table by two different scorers
  (`GlPnlT12moLoader.ScoreTable` lacks the tab's `"grouped"+"expense" → +50` rule), and the tile
  applies no Org filter while the tab applies `CAD` (prior audit F4 — I did not re-verify the
  seven-table scoring live, so treat as `[READ]`).

### 5.9 Corrections to reported environment facts

| Reported | Verdict | Evidence |
|---|---|---|
| Deltek reached over ODBC via a DSN | **CONFIRMED** | `[RUN]` `VpOdbcDsnFactory.cs:39` `DSN=Deltek;UID;PWD`; connected live |
| Uses **four-part** catalog names | **WRONG — three-part** | `[RUN]` every query is `[catalog].dbo.Table`; grep for four-part / `OPENQUERY` / `MSDASQL` in scope returned **nothing**. Four-part belongs to the SQL-Server-linked-server path used elsewhere, not this module |
| Never issues `USE` | **CONFIRMED** | `[READ]` no `USE` in scope |
| Revenue Generation is OFF | **CONFIRMED — the record is right** | `[QUERIED]` `Revenue` = `Billed` on **47,246 / 47,366** rows (99.75%); `Unbilled` on 0.5%. My first pass said "ON" by trusting the app's own detector — see §5.2, which supersedes it |
| `BilledFee` used instead | **CONFIRMED (different reason)** | `[READ]` `CASE WHEN BilledFee<>0 THEN BilledFee ELSE Revenue END` |
| Deltek holds **no** won/loss signal | **REFINED — Deltek carries a loss signal but no win signal** | `[QUERIED]` `PR.Stage`: `LOST` **85**, `InPursuit` **176**, `DNP` **8**, `~WDEF~` 36,139 (unset); `Probability` on 2,053 rows; `LostTo` on **3**; `OpportunityID` on **0**. There is **no WON value at all** — confirmed independently by a parallel audit and a live Vantagepoint tenant query. The 177 wins in `KorPursuits` come from a one-time hand-curated `CustomProposal` import, **not the live feed** |
| FX 1.36 for USD work | **CONFIRMED but stale/inconsistent** | see §5.3 |
| `DateTime` used, not `OdbcType.Date` | **MOSTLY — do not "fix"** | `[READ]` prior audit §3.4 probed both: `OdbcType.Date` returned 288 rows on `tkDetail.TransDate` while `OdbcType.DateTime` **threw** a DataDirect protocol error; both forms appear in working production paths. Leave every date binding alone |
| Client join WBS1 → PR → Clendor | **CONFIRMED** | `[READ]` `WipFinancialsService.cs:304-309` and every sibling service |

---

## 6. Dependencies

| Dependency | Detail | Reachable off the KOR LAN? |
|---|---|---|
| **Deltek Vantagepoint over ODBC** | System DSN `Deltek`, DataDirect HDP 4.6 (64-bit), catalog `C0000052267P_1_KOR00000000`, backend **SQL Server 2019 15.0.4415** | **No** — internal endpoint. DSN + Machine env creds must exist on the demo box. **VPN required at MVE.** |
| `KorTransmittalsDb` (SQL Server) | `SqlFinancialPortfolioSnapshotStore` — portfolio trend snapshots | No — LAN/VPN |
| MCP AI service (KOR-APP01:5500) | only for the `/ask` virtual-CFO path | No — LAN/VPN |
| `deltek-webhook` service | `WatchlistSyncClient`, 15 s HTTP timeout, basic auth; writes watchlist toggles back to Deltek REST | No — LAN/VPN |
| Active Directory | security-group gate for the Financials tile | No — and it **fails open** (§5.6) |
| Microsoft Graph / SharePoint | not used by this module | n/a |

No licensed desktop software. The desktop Financials path needs **no service running** — just the
DSN, the credentials and network line-of-sight to Deltek.

---

## 7. Test reality

Test project: `Kor.Operations.App\Kor.Transmittals.App.Tests\Kor.Operations.App.Tests.csproj`
(78 .cs / 10,737 LOC overall).

`[RUN]` `dotnet test --no-build --filter "FullyQualifiedName~Financials"` →
**176 passed, 0 failed, 0 skipped, 2 seconds.**

Two seconds for 176 tests tells the real story: **they are all hermetic.** They exercise C#
arithmetic over synthetic snapshots — headline KPI math, delivery confidence, KPI↔drilldown
reconciliation, metric-dictionary presence and format, `OrgFx` parsing (including the `~`
provisional marker), and — new since the July audit — `WipFinancialsSignConventionTests`, which
pins the sign convention that was inverted. That is genuinely good coverage of the half that
computes in C#.

The half that computes in **SQL is close to naked.** The only Deltek-touching tests are
`Financials\Executive\Integration\LoaderIntegrationTests` (3 cases, `[Trait("Category","Integration")]`,
requiring `DELTEK_DSN`/`USER`/`PASSWORD`) — excluded from CI and, on the evidence of the env-var
names not matching the app's own `KOR_ODBC_*`, effectively never run. Nothing covers: the Org-scope
and account-set divergence between MCP and WPF (§5.4), the two FX regimes (§5.3), the
success-after-failure paths (§5.5), the GL table-scoring divergence, or the Billed/GL P&L builders.
**Nothing anywhere asserts data freshness** — which is precisely how §5.1 went unnoticed. The
coverage is not theatre, but it is pointed at the safest half of the module.

---

## 8. Demo risk — ranked

1. **"Is this real-time?" → the numbers are from February.** `[QUERIED]` The single most likely
   awkward question, and the answer on screen today is bad. MVE's technical lead only has to
   notice the P&L's last column, or ask what period the WIP is as-of. Mitigation is cheap: state
   the as-of period on the tile, and lead with AR/utilization/collections, which *are* live.
2. **The AI gives a different P&L than the screen.** `[QUERIED]` §5.4. Asking the virtual CFO to
   confirm what the tab shows is the natural demo flourish, and it produces a different number.
3. **Two FX rates in one window.** `[READ]`+`[RUN]` §5.3. Partner Financials at 1.378457 next to
   Billed P&L at 1.36. If anyone adds up USA work across the two tabs, it will not tie.
4. **`get_wip` is live in the AI and its number is not defensible.** `[QUERIED]` §5.2. With
   Revenue Generation off, both WIP branches draw from 238 residual rows (0.5% of the table), so
   any WIP figure `/ask` returns is a residue, not a measurement. The WPF tile is already hidden
   (`ExecutiveSummaryService.cs:398`) — the MCP tool is the exposed edge. If MVE's lead asks the
   virtual CFO "what's our WIP?", it answers with a number no one can defend.
5. **"Why is there no WIP tile?"** `[READ]` The hiding is correct (§5.2), but in a suite pitched at
   project accounting its absence invites the question. Answer it deliberately — *"Deltek isn't
   running revenue recognition, so we don't publish a WIP number we can't stand behind"* is a
   strong answer that shows rigour. Being caught without it is not.
6. **A transient Deltek blip renders a confident $0** rather than an error. `[READ]` §5.5. Low
   probability, high embarrassment — a $0 backlog on stage reads as a broken product.
7. **Access control fails OPEN off-LAN — and the demo *is* off-LAN.** `[READ]` §5.6. The gate is
   correctly set to four users, but an AD lookup failure is exactly what launching at MVE's office
   without the VPN up produces, and it force-shows Financials **and Compensation** (salary data).
   Silent. Confidentiality risk rather than a crash, but it is the one defect the venue guarantees
   will be triggered if the tunnel is not up first.
8. **Looks-unfinished:** the GL "Net Income (T12mo)" tile is captioned *"same source as the GL P&L
   tab"* while reading a different GL table and Org scope `[READ]`.

---

## 9. To-do register

| Item | Size | Tag | Why it matters |
|---|---|---|---|
| Put the as-of period on every stale-sourced tile/tab ("Deltek posted through **Feb 2026**") | **M** | `BEFORE-DEMO` | Turns the worst question into a demonstration of rigour. Cheaper and more honest than hiding it |
| Ask Daler/DMCL why GL + Revenue Generation posting stopped after 202602, and whether it can be run | **S** (the ask) | `BEFORE-DEMO` | If it can be posted before the demo, risk 1 evaporates entirely. This is a business action, not code |
| **Correct the battlecard and the RG record**: RG is OFF, but `SUM(Revenue)` = **$69.06M**, not $0 | **S** | `BEFORE-DEMO` | The pulled claim was false in both directions; the replacement sentence is in §5.2 and is checkable in one query |
| Disable or caveat MCP `get_wip` until a real WIP source exists | **S** | `BEFORE-DEMO` | `/ask` will answer "what's our WIP?" from 238 residual rows; the WPF tile is already correctly hidden |
| Fix the fail-open `catch` (`HomeWindow.xaml.cs:295-308`) to collapse Financials + Compensation | **S** | `BEFORE-DEMO` | Demo is off-LAN at MVE; this is the trigger condition. Fallback if it does not ship: bring up VPN before launching, pre-flight the Home tile set |
| Rotate the four secrets in the deployed `appsettings.Production.json` + strip the `BUILTIN\Users` ACE | **M** | `BEFORE-DEMO` | Cleartext Deltek + Anthropic + SQL credentials readable by any session on KOR-APP01. **Ian runs on KOR-APP01**; restarts the MCP service |
| Raise `UnbilledColumnHasAny()` above a bare non-zero test (ratio or row-count threshold) | **S** | `SOON` | 238 stray rows out of 47,366 currently flip the whole service onto the wrong branch |
| Build WIP from uninvoiced `tkDetail` labour/expense rather than `PRSummaryMain` | **L** | `LATER` | The only route to a real WIP number while Revenue Generation stays off |
| Set `"BilledDefaultOrg": "CAD"` in the deployed MCP `appsettings.Production.json` | **S** | `BEFORE-DEMO` | One config value; makes `/ask` agree with the screen. **Ian runs the change on KOR-APP01** — config edit + service restart, not autonomous |
| Decide the canonical FX regime and point every surface at it | **M** | `BEFORE-DEMO` | Two rates in one window is the kind of detail a sharp technical lead finds |
| Decide: publish the WIP tile, or have a one-line answer ready for why it is hidden | **S** | `BEFORE-DEMO` | Math is verified correct; this is a finance-sign-off decision, not an engineering one |
| Migrate MCP secrets to Machine env vars on KOR-APP01 (the pattern the WPF app already uses) | **M** | `SOON` | Durable fix once the rotate + ACL tourniquet above is applied |
| Return `DataLoaded: false` when a loader falls back (`WipFinancialsService.cs:117-118`, `BacklogService.cs:131`) + badge it in the UI | **M** | `SOON` | Stops a Deltek blip rendering as a confident $0 |
| Align the GL tile's `ScoreTable` with the tab's `PickBestDefaultTable`, or rewrite the "same source" caption | **S** | `SOON` | Removes a visibly false claim |
| Add a freshness assertion to the test suite (max period vs today, as a ratchet) | **S** | `SOON` | The gap that hid §5.1. Per house rule, checks belong in the build |
| Integration tests: rename env vars to `KOR_ODBC_*` so they can actually run; add MCP↔WPF parity tests | **L** | `SOON` | The SQL half of the module is untested |
| Correct `WipTool`'s description and the prior audit's F1a: RG is **OFF**; also three-part not four-part naming | **S** | `SOON` | Two documents currently assert RG is ON. Wrong facts propagate into every future session |
| Populate `CFGAcctngCalendarData`, or assert the fallback deliberately | **S** | `LATER` | Silent empty dependency |
| `DeltekSchemaValidator` connection-capture (`:84-86`); `ExecutiveSummaryDeltekLoader.Catalog` static; `FinancialsService` transient-cache | **M** | `LATER` | Hygiene; none currently biting |
| Remove the committed Deltek username in `20260807_filesync_kormapsync.sql:36` | **S** | `LATER` | Username only, no password |

---

## 10. Verdict

**Demo-able with care — and it should be on screen, because it is the most impressive thing here.**
The engineering is in good shape: it builds clean, 176 hermetic tests pass in two seconds, the
schema validator comes back **CLEAN** against the live catalog, the WIP *sign* bug that blocked a
deliverable was fixed on 2026-07-31 and I reproduced the corrected split against live Deltek, there
are zero `TODO`/`FIXME`/`NotImplementedException` in 26,500 lines, no hardcoded credentials in
source, and the whole window paints in about 1.5 seconds against a live ODBC connection.

The single most important thing to fix is not code — it is the **six-month data gap**.
`PRSummaryMain` and `GLSummary` stop at February 2026 while AR and timesheets are current to today,
so WIP, Cash, Backlog, the GL P&L and "Net Income (T12mo)" are all half a year old on a screen KOR
intends to describe as real-time. Either get the periods posted before the demo, or label the as-of
date on every affected tile and lead with the surfaces that genuinely are live.

Second, **Revenue Generation is off** — the standing internal record was right and my own first
pass was wrong, as was the prior forensic audit's F1a. `Revenue` mirrors `Billed` on 99.75% of
rows. Two consequences: the pulled battlecard line was false in *both* directions (`SUM(Revenue)`
returns **$69.06M**, not $0), and `WipFinancialsService` is taking the Revenue-Generation branch on
a tenant that has none, because its detector trips on 238 stray rows. Keeping the WIP tile hidden is
correct; MCP `get_wip` is the exposed edge and should be muted before the demo.

Third, close the MCP `BilledDefaultOrg` gap — a one-value config change on KOR-APP01 that stops the
virtual CFO contradicting the tab beside it.

Two security items sit outside the demo narrative but should not wait on it: the deployed
`appsettings.Production.json` holds four live secrets in cleartext, readable by any session on
KOR-APP01 and protected only by an accident of file layout (**source and git history are clean** —
I verified that specifically); and the Home screen's authorization `catch` **fails open** on
Financials and Compensation exactly when AD is unreachable, which is the condition demoing from
MVE's office in Southern California creates. Fix the `catch` before travelling, or bring the VPN up
before the app and pre-flight the tile set.

Do those and this module is the strongest asset in the suite; skip them and the sharpest person in
the room finds the first two inside ten minutes.
