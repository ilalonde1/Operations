# Kor.Operations.Mcp — Architecture Proposal

**Status:** Draft, not built. For review before any code is written.
**Author:** Ian Lalonde + Claude (architecture sweep, 2026-05-07)
**Decision needed:** Approve / iterate / reject this shape, then build.

---

## 1. Purpose (one line)

A Windows Service on KOR-APP01 that hosts the AI tool catalog (read-only) and runs the COO Card nightly insights job, fronted by the Model Context Protocol so the WPF app — and any future Claude client — can talk to it through a single, stable interface.

## 2. Scope

| In scope | Out of scope |
|---|---|
| AI tool catalog for "virtual CFO" questions | Replacing existing Deltek ODBC connections used by the WPF app for non-AI work (project pickers, financials window load, PMTools refresh, etc.) |
| COO Card nightly analysis job (Ian-only insights) | Write/action tools (deferred per `project_ai_write_tools_deferred.md`) |
| Centralized AI audit log | Multi-tenant / external auth |
| Centralized FirmContextProvider cache | General-purpose Deltek HTTP gateway |
| Anthropic API key off workstations | FileSync-related context (excluded per `feedback_filesync_excluded_from_ai.md`) |

## 3. Project shape

```
Kor.Operations.Mcp/                      ← new project, .NET 8 worker service
├── Kor.Operations.Mcp.csproj
├── Program.cs                            ← Generic Host bootstrap
├── McpServerHost.cs                      ← MCP transport host (HTTP+SSE)
├── Tools/                                ← one file per tool (15 to start)
│   ├── GetFirmBaselineTool.cs
│   ├── GetActiveProjectsTool.cs
│   ├── GetProjectDetailTool.cs
│   ├── ...
├── CooCard/
│   ├── CooCardScheduler.cs               ← BackgroundService, runs nightly
│   ├── CooCardAnalyzer.cs                ← composes tool outputs into ranked insights
│   └── CooCardStore.cs                   ← writes results to SQL for WPF to read
├── Audit/
│   ├── AuditLogger.cs                    ← writes to Mcp.AuditLog SQL table
│   └── AuditMiddleware.cs                ← wraps every tool call
├── Services/                             ← REUSES existing libraries, does not duplicate
│   └── (DI registration of FinancialsService, VantagepointRepository, etc.)
├── appsettings.json                      ← connection strings, ports, Anthropic key
└── install.ps1                           ← Windows Service install script
```

**Reuses without rewrites:** `Kor.Operations.Core`, `Kor.Operations.Data`, `Kor.Operations.Services` (FinancialsService, etc.). The MCP server is a thin protocol layer over the existing service classes.

**NuGet:** `ModelContextProtocol` (official Anthropic-blessed .NET package) for the wire protocol. `Microsoft.Extensions.Hosting` for service lifetime. Existing `Serilog`, `Polly`, etc.

## 4. Transport + auth

- **Transport:** HTTPS + Server-Sent Events on a hostname behind the same `*.korstructural.com` reverse proxy WatchlistSync already uses (e.g. `mcp.korstructural.com`). One TLS/cert/proxy pattern across both services; no separate LAN port to manage.
- **Auth (v1) — mirror WatchlistSync exactly:** HTTP Basic (Username:Password, Base64-encoded `Authorization: Basic …` header). Per-workstation config in `App.config` keys `Mcp.ServiceUrl` / `Mcp.Username` / `Mcp.Password`. Same pattern as `WatchlistSync.ServiceUrl` + `Username` + `Password`. Reuses operator muscle memory; one secrets-rotation runbook covers both services.
- **Auth (later):** Windows Auth via Negotiate, so the audit log captures the real Windows identity instead of a shared service account. Defer until v1 is in use.
- **Per-user identity in v1:** since Basic Auth uses one shared service account, the MCP client sends the calling user's UPN as a custom header (`X-Kor-User-Upn`) the server logs into the audit table. Honour-system, but adequate inside the LAN.
- **Network exposure:** scoped to internal networks via the existing reverse proxy's allow-list (matching WatchlistSync). No open public access.

## 5. Tool catalog (24-tool catalog)

All read-only. Inputs are typed JSON; outputs are dense JSON records (one rich call beats five narrow ones). Catalog defined after a full sweep of every KPI/metric/computation in the WPF app + AEC-firm gap analysis.

**Design principles:**
- Each tool returns a **rich record**, not a single scalar. `get_project_detail` includes budget + hours + fee + AR + hotlist + delivery confidence + peer reference + subconsultants + GFA in one call.
- Every list/aggregate tool accepts **filter inputs** (PM, client, phase, date range, org/office, construction type, watchlist-only, etc.).
- New AEC-firm metrics not in the WPF app today (DSO trend, multiplier, concentration risk, BD win rate, value-weighted pipeline, AR aging trend, average project size trend, utilization-by-office) are surfaced as first-class tools — NOT as gaps.

### Firm-level (always-on baseline)

| # | Tool | Returns |
|---|---|---|
| 1 | `get_firm_baseline` | Top clients by lifetime fee, firm AR 90+ + top 5 AR clients, trailing 12-mo billed, blended fee/hr, top PMs by load, over-budget count, budget-source breakdown, concentration index. (Cached 5 min — replaces today's `FirmContextProvider`) |
| 2 | `get_firm_headline_kpis` | Total active fee, billed, unposted-billed, backlog, hours remaining, team-days runway, GFA ratios, FX-adjusted across BC/LA/SD orgs |
| 3 | `get_firm_dso_trend` | **NEW** — Days Sales Outstanding monthly trend (12-24 mo); cash-flow velocity |
| 4 | `get_firm_multiplier` | **NEW** — Revenue / Direct Labor Cost (project + effective firm-wide); core profitability benchmark |
| 5 | `get_firm_concentration_risk` | **NEW** — % revenue from top N clients + Herfindahl index + trend; client-dependency early warning |
| 6 | `get_firm_utilization_by_office` | **NEW** — BC vs LA vs SD billable %, by labor code; regional capacity view |

### Project-level

| # | Tool | Returns |
|---|---|---|
| 7 | `get_active_projects` | Filtered list with rich row data: PM, phase, fee, % billed, % budget used, delivery confidence, hotlist, AR exposure |
| 8 | `get_project_detail` | One project full snapshot: budget (eng+draft+source), hours, fee + billed + unposted + backlog, AR aging buckets, hotlist, GFA, phase, subconsultant cost, delivery confidence + reasoning, peer-budget reference |
| 9 | `get_project_ar_and_wip` | Per-project AR aging (current/31-60/61-90/90+) + WIP detail (unbilled fee + age) |
| 10 | `estimate_budget_from_peers` | For a hypothetical or actual project: peer-median eng/draft budget by phase + construction type + fee band, with peer count and confidence |

### Client-level

| # | Tool | Returns |
|---|---|---|
| 11 | `get_clients` | Filtered list with rollup: lifetime fee, project count, active count, AR outstanding, AR 90+, tenure, repeat flag, cold flag, AR-risk flag |
| 12 | `get_client_detail` | One client full profile: lifetime fee + billed + unposted, project list (active + closed), AR aging, tenure, intelligence badges (govt/competitor/prior-work), recent activity |
| 13 | `get_client_ar_aging_trend` | **NEW** — Per-client historical AR by month; collection pattern + early warnings on slow-pay shifts |

### PM / DM / Employee

| # | Tool | Returns |
|---|---|---|
| 14 | `get_pm_scorecard` | One PM: delivery health %, estimation accuracy, revenue efficiency, AR management, peer rank, **risk-adjusted fee exposure** (NEW gap merged in) |
| 15 | `get_pm_portfolio` | One PM's full active-project list with risk distribution (Critical/AtRisk/Watch/HighConfidence counts + fee) |
| 16 | `get_employee_performance` | One employee: productivity score, peer rank by construction type, billable %, recent projects, fee/hr percentile |
| 17 | `get_team_utilization` | **NEW** rollup — Group view by office, by labor code (Eng/Draft/Insp/Admin/etc.), monthly trend |

### BD / Opportunities

| # | Tool | Returns |
|---|---|---|
| 18 | `get_opportunities` | Filtered pipeline list: stage, score, owner, source, buyer-type, discipline, deadline, location |
| 19 | `get_opportunity_detail` | One opportunity full record + observation/history trail + linked Deltek client (if any) |
| 20 | `get_bd_pipeline_metrics` | **NEW** — Win rate by filter (cohort/source/owner/discipline), value-weighted pipeline by stage, conversion ratios, average proposal cycle time, average project size trend |

### Historical / Benchmarking

| # | Tool | Returns |
|---|---|---|
| 21 | `query_historical_analytics` | Flexible aggregation: by year, PM, construction type, fee band, project category. Returns weighted KPIs (fee/hr, eng%, draft%, overhead, billable%, subconsultant %) |
| 22 | `get_year_over_year_trend` | Multi-year fee, hours, margin, average project size trends; flags structural shifts |
| 23 | `get_construction_type_benchmarks` | Fee/hr, hours-per-dollar, peer budget defaults by construction type (TallTimber, Concrete, etc.) |

### Financial reporting (P&L surface)

| # | Tool | Returns |
|---|---|---|
| 24 | `get_pl_summary` | GL P&L (Daler's accounts 4001/4003/4210/4220/4240, excl. 4260 intercompany): revenue, direct labor, overhead, gross margin %, net margin %, by month/quarter/year, by org. Includes both Billed view (LedgerAR, real-time) and Posted view (GLSummary, ~3mo lag) so user can compare. |

### Catalog notes

- Tools 3-6, 13, 17, 20 are **net-new analytics** not currently exposed in the WPF UI. Implementation cost ranges from Easy (DSO trend, concentration risk) to Medium (utilization-by-office, BD pipeline metrics). All judged worth the lift.
- A backlog-coverage tool was considered and **dropped** (no firm-set annual revenue target to divide against). Raw backlog $ and trailing-12mo billed remain in `get_firm_headline_kpis` (#2) for the AI to reason against directly.
- Two AEC gaps explicitly **deferred** as Hard: change-order/extras tracking (needs Deltek schema work), phase-level budget variance (needs new data capture). Can be added later as tools 25+ without protocol changes per the additive-only versioning rule (§ 12.4).
- Each tool spec is one file in `Tools/` (JSON Schema input + typed output record + handler that calls existing service classes). Adding a 25th tool is one file + one DI registration; no existing tool changes.

## 6. Audit log

```sql
CREATE TABLE Mcp.AuditLog (
    Id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    OccurredAt    DATETIME2(0)   NOT NULL DEFAULT SYSUTCDATETIME(),
    UserUpn       NVARCHAR(254)  NOT NULL,
    ClientApp     NVARCHAR(64)   NOT NULL,    -- "Kor.Operations.App", future: "ClaudeDesktop", "Outlook", etc.
    ToolName      NVARCHAR(64)   NOT NULL,
    InputJson     NVARCHAR(MAX)  NULL,
    ResultStatus  NVARCHAR(16)   NOT NULL,    -- Ok / Error / TimedOut
    DurationMs    INT            NOT NULL,
    ErrorMessage  NVARCHAR(2000) NULL,
    INDEX IX_AuditLog_OccurredAt (OccurredAt DESC),
    INDEX IX_AuditLog_UserTool   (UserUpn, ToolName, OccurredAt DESC)
);
```

Lives in the same SQL Server instance as the existing portfolio store. Retention: keep forever for now (rows are tiny). Revisit if it ever crosses ~10M rows.

## 7. COO Card scheduling hook

Two cadences, distinct scopes — daily quick-check + weekly deep dive. Both gated to Ian only via SecurityGroup, per `project_ai_coo_card_scope.md`.

### 7a. Daily Brief (every morning, ~06:00 PT)

- Quick-scan: what changed in the last 24 hours that needs Ian's attention today.
- Pulls: AR aging deltas (any client crossed 90+ since yesterday), new Critical-tier projects (delivery confidence flipped), opportunities approaching submission deadlines (≤ 5 days), AR outstanding deltas > $25k, unposted-billing surprises.
- Output: ~5-10 ranked items with one-line rationale + drill-down link.
- Storage: `Mcp.CooCardDailyBriefs` table (one row per day, JSON payload).

### 7b. Weekly Deep Dive (every Monday morning, ~05:00 PT)

- Full analysis across the entire tool catalog: firm KPI trend deltas, PM portfolio risk shifts, client concentration changes, BD pipeline movement, win-rate cohort updates, multiplier/DSO/coverage-ratio trends, employees off-trend.
- Output: structured "state of the firm" — ~20-30 ranked items grouped by domain (Finance / Delivery / Clients / BD / People), each with trend direction + magnitude + suggested action.
- Storage: `Mcp.CooCardWeeklyDeepDives` table.

### Implementation

- `BackgroundService` in the same MCP process (`CooCardScheduler`). Two cron triggers: `0 6 * * *` and `0 5 * * MON`.
- Each scheduler run calls the appropriate curated tool subset, composes results via Anthropic API (using a "COO advisor" system prompt), writes structured output to SQL.
- WPF Home tile (gated to Ian only) shows a stacked card: top of card = today's Daily Brief headline + count; below = link to most recent Weekly Deep Dive.
- Manual "Refresh now" button on the card re-runs the matching cadence on demand (rate-limited to once per hour to avoid token-burn loops).

This is the decisive structural reason for MCP: both cadences need a server-side scheduler regardless. Co-locating with the AI tool host means one process, one deploy, one SQL connection pool, and the schedulers re-use the exact tool catalog the WPF app uses for ad-hoc questions — no duplicate logic.

## 8. Deploy runbook

Mirrors `Kor.Operations.FileSync` (per `reference_filesync_deploy_runbook.md`). Lives on KOR-APP01 alongside it.

```
1. Build:    dotnet publish -c Release -r win-x64 --self-contained false
2. Stage:    publish output to \\KOR-1001\publish\Kor.Operations.Mcp\
3. Stop:     sc \\KOR-APP01 stop Kor.Operations.Mcp
4. Copy:     robocopy from staging to D:\Services\Kor.Operations.Mcp\ /MIR
5. Start:    sc \\KOR-APP01 start Kor.Operations.Mcp
6. Verify:   curl http://kor-app01:8443/health → 200 OK + version
7. Smoke:    invoke get_firm_baseline tool, check audit row appears
```

Service name: `Kor.Operations.Mcp` (no `.Service` suffix, matching the FileSync convention).

## 9. WPF app refactor

The existing WPF AI plumbing keeps its shape; only the data source changes.

| Layer | Today | After |
|---|---|---|
| `AppAiContextBuilder` | Concatenates `IAiContextProvider` outputs into the system prompt | **Deleted.** Context is no longer pushed; tools are pulled. |
| `IAiContextProvider` impls (15 of them) | Format VM data as text for system prompt | **Kept** but only used to build "CURRENTLY SELECTED" local context (the row/project the user has highlighted) |
| `AppAiService.AskAsync` | Calls Anthropic API with full system prompt + history | Calls Anthropic API with `tools` array; tool calls dispatched to the **MCP client**, which forwards to KOR-APP01 |
| `FirmContextProvider` | Synchronous bridge with `.GetAwaiter().GetResult()` (deadlock risk) | **Deleted.** Replaced by `get_firm_baseline` tool — cleaner, async, no UI-thread bridge. |
| `KOR_ANTHROPIC_KEY` env var | Required on every workstation | Required only on KOR-APP01 |

The `AskWithToolsAsync` plumbing already exists in `AppAiService.cs:136` — minimal new code on the WPF side, mostly deletions.

## 10. What does NOT change

- WPF app's existing Deltek ODBC connections for non-AI work (project pickers, financials load, PMTools, Historical Analytics, etc.). Unchanged.
- `VantagepointRepository`, `FinancialsService`, etc. used by the WPF app directly. Unchanged.
- Workstation onboarding still includes Deltek ODBC config.
- Deployment of the WPF app itself. Unchanged.

## 11. Effort estimate

| Phase | Work | Days |
|---|---|---|
| 11a | Bootstrap project, HTTPS+SSE transport, HTTP Basic auth, audit middleware, `health` endpoint, deploy runbook mirroring FileSync | 2 |
| 11b | Implement existing-data tools (1, 2, 7-12, 14-16, 18-19, 21-24): ~17 tools, mostly thin wrappers over existing services | 5 |
| 11c | Implement net-new analytics tools (3-6, 13, 17, 20): 7 tools, requires new SQL aggregations | 3.5 |
| 11d | COO Card: scheduler + Daily Brief composer + Weekly Deep Dive composer + SQL stores + WPF tile (Ian-only) + token-budget guardrail (per §12.8) | 4 |
| 11e | WPF refactor: switch `AppAiService` from in-app context to MCP client; delete `AppAiContextBuilder` and `FirmContextProvider`; preserve "CURRENTLY SELECTED" local context plumbing | 2 |
| 11f | End-to-end smoke, audit log verification, secrets rotation drill, cutover | 1 |
| | **Total** | **~17.5 working days (~3.5 calendar weeks)** |

The 4.5-day jump from the original 13-day estimate comes from: expanded catalog (15→24 tools, +4 net-new analytics costing ~3.5 days), and dual COO Card cadences (+1 day for the Weekly Deep Dive composer).

## 12. Decisions resolved + remaining

### Resolved (2026-05-07 review)

| # | Question | Decision |
|---|---|---|
| 12.1 | Tool catalog completeness | **24 tools** (§5) covering every WPF metric + 7 net-new AEC analytics. Backlog-coverage tool dropped (no firm revenue target). Two further AEC gaps explicitly deferred (change orders, phase-level budget) — addable later under additive-only versioning. |
| 12.2 | COO Card cadence | **Both** — Daily Brief (every morning, ~06:00 PT) + Weekly Deep Dive (Monday ~05:00 PT). Two distinct schedulers, one MCP process. Detail in §7. |
| 12.3 | Auth v1 | **HTTP Basic mirroring WatchlistSync** (`Username`/`Password` in App.config). Shared service account; per-user UPN sent as audit header. Windows Auth deferred. Detail in §4. |
| 12.4 | Tool versioning | **Additive-only.** Tools may be added; existing tool input schemas and output records may grow new optional fields but never remove or rename existing ones. Old WPF builds keep working after every server redeploy. |
| 12.5 | Failure UX when MCP down | **Simple "AI unavailable" message** in the WPF app. No fallback to the deleted in-app `FirmContextProvider`. Keeps the cutover clean and the failure mode obvious. |

| 12.6 | Annual revenue target / backlog coverage | **Dropped.** No configured firm target. Raw backlog $ and trailing-12mo billed remain in `get_firm_headline_kpis` (#2); the AI can reason about runway without a hard target. |
| 12.7 | Hostname / proxy pattern | **Mirror WatchlistSync** — use the existing `*.korstructural.com` reverse proxy (e.g. `mcp.korstructural.com`). One TLS/cert/proxy operations footprint across both services. |
| 12.8 | Anthropic token budget cap | **Yes, configurable hard-stop.** Two `App.config` keys: `Mcp.AnthropicMonthlyTokenBudget` (server-side, hard-cap; further composition disabled when breached) and `Mcp.AnthropicMonthlyTokenWarnAt` (~80% threshold for warning logs). COO Card composers respect the cap; ad-hoc user questions also count against it. Default budget set conservatively for first month, then tune from real audit-log data. |

### Remaining

None. Spec is locked. Ready to build on approval.

## 13. Recommendation

Spec locked. All 8 questions resolved. **Approve and execute in phase order.** Fastest path to "all-seeing virtual CFO" — clean architecture, centralized audit trail, natural home for both COO Card cadences, hard-capped token spend, room to grow tools without breaking changes, and aligned with KOR's existing service patterns (FileSync deploy, WatchlistSync auth + reverse proxy).

---
*Iterate on this doc directly — it's a working spec, not a final artefact.*
