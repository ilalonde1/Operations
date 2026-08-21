# Module 06 — PM Tools / Historical Analytics

Module: `Kor.Operations.App/PMTools` (27 .cs, 9 .xaml, 10,765 lines incl. XAML; last commit
`594a2cde` 2026-08-01). Computation lives in `Kor.Operations.Business/Analytics` (last commit
`1bc118c7` 2026-05-13). Audited 2026-08-20.

---

## 1. What I searched

**Files read in full:** `PMTools/AnalyticsAiService.cs`, `HistoricalAnalyticsService.cs`,
`EmployeeScoreSnapshotStore.cs`, `SQL/EmployeeScoreSnapshots.sql`, `CalendarHeatmapPanel.xaml.cs`,
`CalendarHeatmapModels.cs`, `StaffUtilizationWindow.AiContext.cs`;
`Business/Analytics/EmployeeSummaryRow.cs`, `PerformanceScoring.cs`, `EmployeePerformanceService.cs`,
`AnalyticsThresholds.cs`, `SharedOptions.cs`; `Services/AppAiService.cs`,
`Services/EnvironmentSecretOverrides.cs`, `Mcp/Options/McpOptions.cs`.
**Files read in part:** `HistoricalAnalyticsViewModel.cs` (lines 290–340, 571–632, 940–1080,
1090–1136), `StaffUtilizationWindow.xaml.cs` (120–210), `ProjectAnalyticsService.cs` (41–250,
315–365), `EmployeeAnalyticsService.cs` (29–204), `FinancialsService.cs` (255–320),
`HomeWindow.xaml.cs` (225–300), `Mcp/Program.cs` (80–92), `Mcp/Ai/AskService.cs` (1009–1028).

**Greps:** `AnalyticsAiService`, `IAiContextProvider`, `ProductivityScore|BillableRateScore|
EfficiencyScore|ProjectHealthScore`, `EngRate|DraftRate|TargetBillingRate`, `EmployeeSummaryExcludedIds`,
`new ProjectAnalyticsService`, `37\.5`, `TODO|FIXME|HACK|NotImplementedException|NotSupportedException`,
`catch`, `C:\\|\\\\KOR|http`, `openai|langchain|genai|mistralai|cohere|ollama` (suite-wide, to confirm
Anthropic is the only LLM provider in this path — only hits are `tools/AwardOllamaBackfill`, unrelated).

**Builds / tests [RUN]:** `dotnet build Kor.Operations.App.csproj -c Debug` → succeeded, 0 errors.
`dotnet test Kor.Operations.Mcp.Tests --filter FullyQualifiedName~Analytics` → 21 passed.
`dotnet test Kor.Operations.App.Tests --filter "~PMTools|~AiPanelContextProvider|~HistoricalMethodologyKeys"`
→ 24 passed.

**Live state [QUERIED]:**
- `Test-NetConnection kor-app01:5500` → open; `GET /health` → `200 {"status":"ok","version":"0.4.2+5b9535f7"}`.
- `GET /tools` (basic auth) → 23 tools; all 9 named in `AnalyticsAiService`'s footer exist.
- `\\KOR-APP01\C$\Program Files\KorOperations\Mcp\appsettings.json` + `.Production.json` (read, secrets redacted).
- Remote registry `HKLM\SYSTEM\...\Environment` on KOR-APP01 — machine env var names only.
- `HKLM:\SOFTWARE\ODBC\ODBC.INI\Deltek` on KOR-1001 — DSN target/driver.
- `KorTransmittals.dbo.EmployeeScoreSnapshots` — `SELECT COUNT(*)`, min/max date, top-5 lowest scores.

**Timings [RUN]** — real Deltek ODBC SELECTs from KOR-1001, the exact SQL lifted from the source:
| Query | source | ms | rows |
|---|---|---|---|
| `LoadProjectRowsSync` | `ProjectAnalyticsService.cs:55` | **2,212** | 9,904 |
| `LoadEmployeeProjectHoursSync` | `EmployeeAnalyticsService.cs:36` | 1,646 | 5,946 |
| `LoadQuarterlyEmployeeHoursSync` | `EmployeeAnalyticsService.cs:182` | 2,381 | 28,293 |
| `LoadEmployeeWeeklyUtilizationSync` | `EmployeeAnalyticsService.cs:89` | 262 | 285 |

I did **not** POST to `/ask` (would spend API credit and violate the GET-only rule) and did not run the
full suite.

---

## 2. What this module is

PM Tools is the firm's operational-analytics surface over Deltek: five WPF windows that answer "how is
the portfolio actually running, and who is doing the running." **Historical Analytics** is the big one —
it pulls every project KOR has ever opened (9,904 rows, no date bound) with fee, billed fee, hours split
by labour code, subconsultant cost and AR aging, then re-slices that one dataset seven ways: Projects,
PM Summary, Drafting-Manager Summary, **Employee Summary**, Fee Bands, Construction Type, and
Year-over-Year Trend. **Staff Utilization** shows a trailing-12-week per-person load table with a
click-through calendar heatmap of one employee's daily timesheet. **Workload Meeting** is a bi-weekly
meeting board (P1–P5 priorities, per-project notes, Excel export) backed by KorTransmittals, and
**PM Capacity & Risk** shows active-project KPIs and eng/draft capacity risk. An AI chat panel is docked
at the bottom of four of the five windows.

What a user sees: a dense filterable grid with a KPI strip across the top (project count, total fee,
weighted billable %, a P25/median/P75 fee-per-hour distribution, budget accuracy), a right-hand detail
pane that explains every metric in prose, and — on the Employee Summary tab — **every named employee
with a 0–100 productivity score, an A+-to-F letter grade in a colour-coded cell, a peer comparison, and
a quarter-by-quarter grade history** (`B → B- → C+ → …`) read from a stored snapshot table. Entry is
gated by AD group membership (`KnownRoles.PMTools`, `KnownRoles.Financials`), and the two most sensitive
windows were deliberately moved out of the PM Tools chooser into the Financials window —
`FinancialsWindow.xaml.cs:210` calls them "sensitive-data launchers relocated from PM Tools."

---

## 3. How you would demo it

**Prerequisites** (all verified on KOR-1001 [QUERIED]):
1. Progress DataDirect Hybrid ODBC driver installed + System DSN named `Deltek`. The DSN targets
   `vp-ca-hdp01.prd.mydeltek.com:443` — **Deltek is over the internet, not the LAN**, so this part works
   from MVE's office. It does *not* work on a laptop without the driver and DSN.
2. Machine env vars `KOR_ODBC_USER` / `KOR_ODBC_PASSWORD` (`EnvironmentSecretOverrides.cs:27-34`).
   `App.config` ships `Vp.User`/`Vp.Password` empty; without the env vars every window fails to load.
3. LAN or VPN for the AI panel (`http://kor-app01:5500`, plain HTTP) and for the employee grade-trend
   panel + snapshot writes (`KOR-APP01\SQLEXPRESS`).

**Click path — safe version:** Home → **PM Tools** tile → *PM Capacity & Risk* (portfolio KPIs, delivery
health, eng/draft capacity) or *Workload Meeting* (priority board, notes, "Export" → Excel). Both are
clean, fast and have no employee-level scoring on screen.

**Click path — the analytics story:** Home → **Financials** tile → toolbar → *Historical Analytics*.
Loads in ~2–3 s over six parallel ODBC queries. Land on **Projects**; the KPI strip fills; click a row
and the right pane explains fee/hr, budget deltas, AR. Switch the view selector to **Year-over-Year
Trend** or **Fee Bands** — both are genuinely impressive and carry no personnel data. **Do not switch to
Employee Summary** (see §8).

**Staff Utilization** (same Financials toolbar) → double-click a person → month calendar heatmap of their
daily hours by project, with prev/next month navigation. Visually strong, but it is one named employee's
timesheet on a projector.

It can be demoed today. The constraint is not "does it work" — it is "which tab is on screen."

---

## 4. Completeness

| Capability | State | Evidence |
|---|---|---|
| Historical project load (9,904 rows, all history) | `WORKING` | `[RUN]` 2.2 s, 9,904 rows |
| Projects / Fee Band / Construction Type / YoY views | `WORKING` | `[READ]` VM lines 571–860 |
| PM & DM performance scoring | `WORKING` | `[RUN]` 21 MCP analytics tests pass |
| Employee Summary + productivity grade | `WORKING` | `[QUERIED]` 900 snapshot rows, 56 employees |
| Employee grade trend (quarterly) | `PARTIAL` | `[QUERIED]` newest snapshot = **2026-04-01**; nothing for Q3 |
| Staff Utilization (12-week) | `WORKING` | `[RUN]` 262 ms, 285 rows |
| Calendar heatmap | `WORKING` | `[READ]` renders one month at a time; not a full-year draw |
| Workload Meeting board + Excel export | `WORKING` | `[READ]` `PmToolsExportService.cs`, ClosedXML |
| PM Capacity & Risk | `WORKING` | `[READ]` `PmCapacityWindow.xaml.cs` |
| AI panel (4 of 5 windows) | `WORKING` | `[QUERIED]` `/health` 200, `/tools` returns all 9 referenced tools |
| Help window content accuracy | `STUBBED` | `[READ]` describes a code path that is no longer the default |

**Debt markers in `PMTools/`:** `TODO` 0 · `FIXME` 0 · `HACK` 0 · `NotImplementedException` 0 ·
`NotSupportedException` **3** — `ComparisonBrushConverter.cs:37`, `ComparisonConverters.cs:19`,
`ComparisonConverters.cs:33`, all idiomatic WPF `ConvertBack` stubs, **not defects**. Empty catch blocks:
0. Of 32 `catch` blocks, 30 log via Serilog/ILogger and surface a message to the UI; the 2 bare ones are
`catch (OperationCanceledException) { }` (`WorkloadMeetingPanelViewModel.cs:692,785`) and are correct.
This is the cleanest module I have looked at by marker count. The problems here are semantic, not sloppy.

---

## 5. What is broken or risky

### 5.1 The AI and the screen compute employee grades from different inputs — two ways

The task brief asked whether `AnalyticsAiService` is a second AI implementation. **It is not.** It is a
~90-line prompt-context builder (`AnalyticsAiService.cs:21`) that serialises what is on screen and then
tells the model to fetch everything else from MCP tools. There is exactly one LLM path from these
windows: `AppAiService.AskAsync` → `POST http://kor-app01:5500/ask` → the MCP server's own Anthropic
loop. The app's second path, `AskWithToolsAsync`, hardcodes `model = "claude-sonnet-4-6"`
(`AppAiService.cs:222`) against `api.anthropic.com` directly — but it is only used by PdfToSafe, not by
PM Tools, and it names the **same model** the MCP server uses
(`McpOptions.cs:26`, and the deployed `appsettings.Production.json` sets `"AnthropicModel":
"claude-sonnet-4-6"` explicitly `[QUERIED]`). Same provider, same model, one key per host. **The
"two disagreeing AI layers" hypothesis is wrong.**

The real divergence is worse, because it is invisible. The MCP tools wrap the *same* C# services as the
WPF window — but wire them up differently:

**(a) Budget basis diverges.** `HistoricalAnalyticsService.cs:28` constructs
`new ProjectAnalyticsService(opts, financialsOpts, EstimatePeerBudget)` — passing a peer estimator. In
`ProjectAnalyticsService.cs:338-352`, that estimator **overwrites** the formula budget whenever a project
has ≥3 peers. `Kor.Operations.Mcp/Program.cs:83` constructs the same class with the third argument
**omitted**, so `AttachPeerBudgets` returns early at line 315 and every project keeps
`EstEngBudget = TotalFee / 185 × 0.58` (`ProjectAnalyticsService.cs:211-213`). `EstEngBudget` drives
`isHealthy` in `EmployeePerformanceService.cs:66`, which is 30 % of every employee's productivity score,
and it drives the entire `get_at_risk_projects` watchlist. **Ask the AI panel "who is our most productive
engineer" or "which projects are at risk" while the Employee Summary grid is on screen, and the two can
give different answers about the same people and the same projects.** [READ]

**(b) The exclusion list is not deployed.** `App.config:149` sets
`EmployeeSummaryExcludedIds = IANLALONDE,DALERSINGH`, consumed at
`HistoricalAnalyticsViewModel.cs:1098`. `McpOptions.cs:35` defaults
`EmployeeSummaryExcludedIds` to empty, and **the live server sets it nowhere** — not in the deployed
`appsettings.json`, not in `appsettings.Production.json`, not in any machine env var on KOR-APP01
`[QUERIED]`. Because `EfficiencyScore` is a *percentile rank across the population*
(`PerformanceScoring.cs:60-61`), adding two people changes **every** employee's score and can move letter
grades. So `get_employee_performance` ranks Ian and Daler — who are deliberately hidden from the
screen — and shifts everyone else's grade while doing it.

**(c) Three code comments assert parity that does not hold.** `AskService.cs:1019`
("so rows and scores match the WPF Historical Analytics Employee Summary tab"),
`EmployeePerformanceTool.cs:15` and `:44` ("same row construction and scoring as the WPF Employee Summary
tab"). Those sentences are in the **system prompt the model reads**, so the model will assert parity to
the user. Fix (a) and (b) and the sentences become true again; that is the cheap direction.

### 5.2 `$185` is on screen, and live Deltek says the portfolio median is roughly double

The reported context checks out and is **configurable, not hardcoded** — `App.config:17-19` sets
`Vp.EngRate=474`, `Vp.DraftRate=655`, `Vp.TargetBillingRate=185`, read at
`CompositionHelpers.cs:45-47` with the same values as compiled fallbacks
(`SharedOptions.cs:14-16`, `AnalyticsThresholds.cs:60`). The 58/42 split is arithmetic, not a stored
constant: `ProjectAnalyticsService.cs:41-43` computes `u3 = 1/(1/474 + 1/655) = 275.0`, then
`u3/u1 = 0.580` and `u3/u2 = 0.420` at lines 212–213. All three carry the comment "calibrated Apr 2026."

Whether it is **still current** is the problem. Reproducing the app's own default view ("Has Hours + Fee",
`HistoricalAnalyticsViewModel.cs:24`) against live Deltek `[RUN]`:

| Filter | n | P25 | **median** | P75 |
|---|---|---|---|---|
| Has Hours + Fee (app default) | **1,127** | $167 | **$380** | $1,266 |
| Fee ≥ $25K + Hours | 621 | $136 | $332 | $2,808 |

n = 1,127 matches what the window will show. My figures **exclude** `HourlyRevenue` and the USD→CAD
uplift, both of which only raise fee/hr — so the number the app renders will be **≥ $380**. Meanwhile:

- `HistoricalAnalyticsWindow.xaml:395` — tooltip: *"The typical project earns about $185/hr."*
- `HistoricalAnalyticsWindow.xaml:1087` — tooltip: *"Compare to the $185/hr portfolio median."*

**The KPI strip and the tooltip contradict each other on the same screen.** Note the distribution is
violently skewed (P75 = $1,266 — many projects carry fee with negligible logged production hours), so I
will not claim "$185 was always wrong"; I will claim, with the numbers above, that it is **not the
current median** and that the tooltip text is falsifiable in one glance.

Downstream: if the real rate is ~2× the constant, `Fee / 185` over-estimates every budget by ~2×, every
project looks healthier than it is, `ProjectHealthScore` inflates, and grades inflate with it — on the
MCP side for *all* projects, and on the WPF side for the <3-peer ones.

### 5.3 Opening the Employee Summary tab writes to production SQL — on every filter change

`HistoricalAnalyticsViewModel.cs:628` calls `RecomputeEmployeeSummary(list)` from `ApplyFilter()`, and
`ApplyFilter()` fires on **all nine** filter setters (lines 100–148). `RecomputeEmployeeSummary` ends with
a fire-and-forget `Task.Run` (lines 969–991) that MERGEs a snapshot row per employee for the current
quarter into `KorTransmittals.dbo.EmployeeScoreSnapshots`.

Three consequences:
1. **The stored quarterly score depends on whatever filter the user last touched.** Filter to one
   construction type and the quarter's official snapshot is silently overwritten with scores computed
   over that slice. `groups` comes from `visible`, not `_allRows`. This corrupts the trend history the
   detail pane presents as a career record.
2. **A write happens during the demo.** `[QUERIED]` the newest snapshot is `2026-04-01` and the last
   write was `2026-04-12`; today is 2026-08-20, so the current quarter start is **2026-07-01** and no row
   exists for it. Opening the tab on stage inserts a fresh Q3 snapshot for all ~40 staff.
3. **Then it may backfill.** Lines 981–986: if the trend probe returns ≤1 row it runs
   `BackfillFromQuarterlyHoursAsync`, which issues an extra 2.4-second ODBC query (28,293 rows) and then
   loops **one `ExecuteNonQueryAsync` per employee per quarter** (`EmployeeScoreSnapshotStore.cs:27-60`)
   over `TransDate >= '2020-01-01'` — roughly 26 quarters × ~40 people ≈ 1,000 round trips. It is
   guarded by the ≤1-row probe and the table already holds 900 rows, so it should not fire — but the
   probe checks `groups[0]`'s employee only.

Failures are caught and logged (line 989), so none of this is visible; it just quietly happens.

### 5.4 "Utilization" has three definitions in one application

| # | Definition | Where | Denominator |
|---|---|---|---|
| A | `twelveWkAvg / 37.5` | `StaffUtilizationWindow.xaml.cs:169` | a fixed 37.5-hr week |
| B | `BillableHours / TotalHours` | `UtilizationService.cs:233` (Financials tile **and** MCP `get_utilization`) | all hours incl. PTO/holiday/admin |
| C | `TotalBillableHrs / TotalAllHrs` | `EmployeeSummaryRow.cs:23` (PM Tools Employee Summary) | all hours, over the **filtered** project set |

A and B are not the same quantity and will not produce similar numbers — A can sit near 100 %, B caps far
below it by construction. Both are documented (`Definitions.StaffUtilization.cs:134-141` for A; the MCP
system prompt at `AskService.cs:1009` for B), which is better than most codebases manage — but the word
on the two screens is identical. `37.5` is also a bare literal at `StaffUtilizationWindow.xaml.cs:169`
while `AnalyticsThresholds.HoursPerDay = 7.5` sits unused two projects away.

Duplicated metrics between this module and Financials, named explicitly:
**Est Eng/Draft Budget** (peer-first here vs. target-rate-first in `FinancialsService.cs:270`),
**Fee/Hr** (production hours here; `FinancialsService.cs:296` states outright *"This differs from PM Tools
BillableRateScore which uses all non-admin labor"*), **utilization** (A vs B above), and
**over-budget/at-risk status**, which inherits the budget divergence.

`SharedOptions.cs:34-39` records that peer-median was **retired as the default on 2026-06-28** because it
"produc[es] garbage-low budgets and false 'Critical' delivery flags." That retirement was applied in
`FinancialsService` and never in `ProjectAnalyticsService`. PM Tools still prefers peers.

### 5.5 Smaller, concrete

- **`CalendarHeatmapPanel.xaml.cs:202`** — `return name.Substring(0, max - 1) + "";` — truncates a project
  name by one character and appends an **empty string** where an ellipsis clearly belongs (verified at the
  byte level with `od -c`; a lost Unicode `…`, from `b942b06c` 2026-05-07). Long project names are
  silently chopped with no indicator. Isolated — no other occurrence in the app.
- **`HomeWindow.xaml.cs:295` fails open.** The AD group check that hides the Financials / Compensation /
  PM Tools tiles is wrapped in a `try`, and the `catch` sets every tile to `Visibility.Visible`. If the DC
  is unreachable — precisely the off-LAN case at MVE's office — the gate opens instead of closing. The
  gate is UI-visibility only; no window re-checks membership on open.
- **`App.config:148` ships a plaintext service password** (`McpServer.Password`) committed to the repo.
  The suite-wide "no hardcoded credentials in C# source" scan is correct and this does not contradict it —
  it is XML, not C#. Flagging for whoever owns the app shell.
- **The project query is unbounded** (`ProjectAnalyticsService.cs:55`): no date predicate, aggregates all
  of `tkDetail` / `apDetail` / `AR` / `PRSummaryMain` per WBS1, `CommandTimeout = SqlTimeouts.Batch` =
  **300 s**. It runs in 2.2 s today from KOR-1001, so this is a scaling note, not a live risk — but a
  5-minute ceiling means a slow link degrades into a hang, not an error.
- **Named employee data is sent to Anthropic.** `StaffUtilizationWindow.AiContext.cs:70-80` pushes up to
  150 rows of `EmployeeName | hrs/wk | utilization | OT | cost-per-billable-hour`, and
  `AnalyticsAiService.cs:64-75` pushes the selected employee's grade and sub-scores. Deliberate and
  bounded, and it goes via KOR's own MCP host — but it is worth the owner knowing it is in the prompt.

---

## 6. Dependencies

| System | Reachable off the KOR LAN? | Notes |
|---|---|---|
| **Deltek Vantagepoint (ODBC)** | **Yes** `[QUERIED]` | `vp-ca-hdp01.prd.mydeltek.com:443` via Progress DataDirect Hybrid. Needs the driver + System DSN `Deltek` + `KOR_ODBC_USER`/`KOR_ODBC_PASSWORD` machine env vars on the demo machine. |
| **`KorTransmittals` on `KOR-APP01\SQLEXPRESS`** | **No** — LAN/VPN | Employee grade trend, snapshot writes, Workload Meeting board. |
| **MCP `/ask` — `http://kor-app01:5500`** | **No** — LAN/VPN, internal hostname, plain HTTP | Basic auth; live and healthy, v0.4.2 `[QUERIED]`. |
| **Anthropic API** | via MCP host | `claude-sonnet-4-6`, key on KOR-APP01 (`KOR_ANTHROPIC_KEY`) and in the deployed `appsettings.Production.json`. |
| **Active Directory** | LAN | Tile gating only, and it fails open (§5.5). |
| ClosedXML (Excel export) | in-proc | No Office install needed. |

Off-LAN, the grids still populate (Deltek is internet-facing) but the AI panel, the employee grade trend
and the Workload Meeting board go dark, and the AD tile gate fails open.

---

## 7. Test reality

| Project | Tests run | Result |
|---|---|---|
| `Kor.Operations.Mcp.Tests` `--filter ~Analytics` | 21 | **all pass**, 26 ms `[RUN]` |
| `Kor.Operations.App.Tests` `--filter ~PMTools\|~AiPanelContextProvider\|~HistoricalMethodologyKeys` | 24 | **all pass**, 38 ms `[RUN]` |

What is genuinely covered: the scoring arithmetic. `PerformanceScoringTests` pins percentile ranking, the
0.30/0.40/0.30 composite, the single-row median default, peer comparison and `Median()` edge cases;
`EmployeePerformanceServiceTests` pins fee attribution, primary-construction-type selection, and — with
some irony — `Build_FiltersExcludedEmployeeId`, a test that proves the exclusion mechanism works while the
live server passes it an empty list. `AnalyticsAiServiceTrimTests` is a genuinely good static gate: it
asserts the prompt context stays small and does not dump bulk lists.

What is not covered, and matters more: **nothing tests that two callers of the same service are wired the
same way.** There is no test that `ProjectAnalyticsService` gets the same estimator in both hosts, none
that the MCP exclusion list matches `App.config`, none that `$185` still resembles the live median, and
none over `EmployeeScoreSnapshotStore` at all — the component that writes personnel records to production
SQL from a fire-and-forget task has zero tests. Coverage here is not theatre; it is real but aimed one
level below where the defects live. Per the repo's own `feedback_checks_go_in_the_build_not_my_head`
rule, the parity checks in §9 belong in the build.

---

## 8. Demo risk — ranked

1. **The Employee Summary tab.** 56 named KOR staff `[QUERIED]`, each with a 0–100 score and a
   colour-coded letter grade — and in the latest stored quarter, five real people carry an **F** with
   scores of 33, 36, 41, 46, 46. Sortable, so one click puts the firm's worst-graded employees at the top
   of the screen in front of an outside architecture partner. This is HR-sensitive and, in BC,
   employee personal information. It is also *forced ranking* by construction — `EfficiencyScore` is a
   percentile, so somebody is always at 0 no matter how well the firm performs, which is exactly the
   question a sharp technical lead will ask. **Recommendation: do not open this tab. If the productivity
   story must be told, tell it from PM Summary / DM Summary (role-level, not person-level), or ship a
   demo-mode toggle that replaces `EmployeeName` with "Engineer 1…n"** — one bound property, ~1 h.
2. **The AI panel contradicting the grid.** §5.1. If anyone asks the chat panel a
   who's-most-productive or which-projects-are-at-risk question with the analytics window open, the
   answer can disagree with the visible numbers — and the model has been told in its system prompt that
   they match, so it will state the parity confidently. This is the single worst thing that can happen on
   stage: the tool disagreeing with itself while asserting it doesn't.
3. **`$185` vs the KPI strip.** §5.2. Hovering the Fee/Hr column shows *"Compare to the $185/hr portfolio
   median"* while the strip above it displays a median around $380. Anyone reading tooltips finds this in
   under a minute, and it undermines every other number on the screen.
4. **The help window is stale.** `HistoricalAnalyticsHelpWindow.xaml:81` says the target-rate formula is
   *"Only used when fewer than 3 peers are found."* In `ProjectAnalyticsService` the peer path is the one
   that wins when peers exist, and in Financials the formula is the default for everything since
   2026-06-28. The help text describes neither code path correctly.
5. **Silent writes on stage.** §5.3. Nothing visible goes wrong, but the app writes personnel scores to
   production SQL while being demoed, and the value written depends on the filter last clicked.
6. **Off-LAN degradation.** Without VPN the AI panel returns "Unable to reach AI service", the grade-trend
   pane shows nothing, the Workload Meeting board fails to load — and the AD gate fails open so every
   tile appears. A demo laptop that has not been rehearsed off-LAN will surprise you.
7. **Looks-unfinished:** truncated project names with no ellipsis in the heatmap (§5.5), and the
   "Latest Quarter" label reading **Q2 2026** in late August.

---

## 9. To-do register

| # | Item | Size | Tag | Why it matters |
|---|---|---|---|---|
| 1 | Do not open Employee Summary in the MVE demo; agree the click-path in advance | S | `BEFORE-DEMO` | Named staff + F grades in front of an outside firm |
| 2 | Pass `EstimatePeerBudget` in `Mcp/Program.cs:83` **or** drop it from `HistoricalAnalyticsService.cs:28` — pick one basis | S | `BEFORE-DEMO` | AI and screen currently disagree on grades and at-risk projects |
| 3 | Set `Mcp:EmployeeSummaryExcludedIds` on KOR-APP01 to match `App.config:149` | S | `BEFORE-DEMO` | AI ranks two people the screen hides, and shifts everyone's percentile |
| 4 | Fix or remove the two `$185` tooltips (`HistoricalAnalyticsWindow.xaml:395,1087`) | S | `BEFORE-DEMO` | Contradicts the KPI strip on the same screen |
| 5 | Re-run the Apr-2026 calibration against Aug-2026 data and reset `Vp.TargetBillingRate` | M | `SOON` | Drives every budget, health score and at-risk flag |
| 6 | Snapshot from `_allRows`, not `visible`; move the write behind an explicit button | M | `SOON` | Stored quarterly scores currently depend on the user's filter |
| 7 | Add a parity test: both hosts construct `ProjectAnalyticsService` identically | S | `SOON` | Locks fix #2 so it cannot silently regress |
| 8 | Add a parity test: MCP exclusion list == `App.config` list | S | `SOON` | Locks fix #3 |
| 9 | Demo-mode toggle to anonymise `EmployeeName` across PM Tools | M | `SOON` | Makes the strongest analytics screen showable at all |
| 10 | Make `HomeWindow.xaml.cs:295` fail **closed** | S | `SOON` | Off-LAN, the gate currently opens instead of closing |
| 11 | Correct `HistoricalAnalyticsHelpWindow.xaml:81` and the three parity claims in `AskService.cs:1019`, `EmployeePerformanceTool.cs:15,44` | S | `SOON` | The prompt text makes the model assert a parity that does not hold |
| 12 | Restore the ellipsis at `CalendarHeatmapPanel.xaml.cs:202` | S | `LATER` | Cosmetic, one character |
| 13 | Reconcile "utilization" A/B/C, or rename the columns so they cannot be confused | M | `LATER` | Three definitions, one word, one app |
| 14 | Batch `SaveSnapshotsAsync` into a table-valued MERGE | M | `LATER` | ~1,000 round trips if the backfill ever fires |
| 15 | Add a date bound (or a "since year" filter) to the project query | M | `LATER` | Unbounded today; 300 s timeout is a hang, not an error |

---

## 10. Verdict

**Demo-able with care — with one tab firmly off the screen.** This is the most finished module I have
looked at: it builds clean, has zero TODO/FIXME/HACK markers, no empty catch blocks, no hardcoded paths,
45 passing tests, and it answers its six ODBC queries in about two seconds against live Deltek. The
Projects, Fee Band, Construction Type and Year-over-Year views are genuinely strong material and carry no
personnel data at all.

Two things stop it being unconditionally demo-ready. The first is the Employee Summary tab: 56 named
colleagues with letter grades, five of them currently F, on a sortable grid — that must not appear in
front of MVE, and hiding it costs nothing because the same story can be told at PM/DM level. The second
is that the AI panel and the grid are wired to the same analytics code through **different constructor
arguments** (`Mcp/Program.cs:83` omits the peer estimator that `HistoricalAnalyticsService.cs:28` passes)
and a **different exclusion list** (deployed as empty), so they can give different answers about the same
employees and the same at-risk projects — while the MCP system prompt tells the model the two match.

The single most important thing to fix is that wiring divergence — items 2 and 3, both a
one-line change plus a config key. Fix those and the module's biggest liability becomes a
demo-choreography question rather than a correctness one. The `$185` tooltip (item 4) is the cheapest
credibility save on the list: it is contradicted by the app's own KPI strip, live, in one hover.
