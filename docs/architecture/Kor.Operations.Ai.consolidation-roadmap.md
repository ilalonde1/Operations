# KOR AI Consolidation Roadmap

> Status: drafted 2026-05-13 (post Batch 89). Not yet executed.
> Owner: Ian Lalonde. Author of plan: Claude.
> Predecessor: the Batches 80–89 arc that consolidated firmwide financial KPIs into `Kor.Operations.Business` services + auto-discovered MCP tools.

## TL;DR

Three of KOR's AI surfaces (MCP `/ask`, Executive Summary, Financials window) now flow through canonical Business services and auto-discovered MCP tools. Four more (PMTools, Historicals, Staff Utilization, CRM/BD) still rely on the older "push a big context blob into /ask" model, with the canonical SQL sitting in App and methodology duplicated across files.

The fix is **structural, not stylistic**: port the analytics SQL into Business, wrap each canonical answer as an MCP tool, let `AnalyticsAiService.BuildContext` shrink to current-view scope data + tool pointers (same shape as the post-Batch-89 system prompt). The auto-discovery + parity validator from Batch 89 catches drift for free.

Total estimated effort: **5–9 batches** (smaller than feared because `HistoricalAnalyticsService` is already a single well-designed service with most of the canonical SQL — we relocate and wrap it rather than rewrite).

## 1. Current state inventory

### 1.1 AI surfaces and where their computations live today

| Surface | AI panel? | Context provider | Canonical SQL today | Status |
|---|---|---|---|---|
| MCP `/ask` (standalone) | n/a (gateway) | n/a | `Kor.Operations.Business/*Service.cs` (Cash/Ar/FirmHealth/Utilization/Wip/Backlog/RecentBilled + Billed/Gl P&L) | ✅ Done (Batches 80–89) |
| Executive Summary window | Yes | `ExecutiveSummaryViewModel` | `ExecutiveSummaryDeltekLoader` → `*Service` (Batches 80–89) | ✅ Numbers canonical; prose in BuildContext could be trimmed |
| Financials window | Yes (per-row) | `FinancialsViewModel` + per-row | `Kor.Operations.App/Financials/FinancialsService.cs` (App-side, ~1500L) | ⚠ Per-row data flows through `FinancialsService`, NOT through Batch 80-89 services. Functions like Backlog drill-down were ported (Batch 86) but `FinancialsService` itself still owns the per-project rows |
| PMTools window | Yes | `PmToolsViewModel` → `AnalyticsAiService` (344L) | `Kor.Operations.App/PMTools/HistoricalAnalyticsService.cs` (App-side, ~700L) | ❌ All App-side; LLM gets a 200+ line context dump per call |
| Historical Analytics window | Yes | `HistoricalAnalyticsViewModel` (1497L) → `AnalyticsAiService` | Same as PMTools (one service powers both) | ❌ Same |
| Staff Utilization sub-window | Yes (inherits parent panel) | `HistoricalAnalyticsViewModel` (same VM) | Subset of `HistoricalAnalyticsService` (firm utilization, weekly per-employee, quarterly per-employee) | ❌ Same |
| CRM / Client Intel / Opportunities | Yes | `CrmViewModel` / `ClientIntelligenceViewModel` / `OpportunitiesViewModel` | Mix of App-side services + the Kor.Opportunities.* worker | ⚠ BD vertical (Phase 12); just shipped, lower priority |
| PdfToSafe | Yes | `PdfToSafeWindow.AiContext.cs` | Engineer-markup vision pipeline (different vertical) | ❌ Out of scope per `feedback_pdftosafe_scope.md` |
| FileSync | n/a | n/a | n/a | ❌ Out of scope per `feedback_filesync_excluded_from_ai.md` |

### 1.2 The HistoricalAnalyticsService surface (the workhorse)

`Kor.Operations.App/PMTools/HistoricalAnalyticsService.cs` is ~700 lines and exposes:

| Method | Returns | Powers |
|---|---|---|
| `LoadSync` (called via `LoadAsync`) | `List<HistoricalProjectRow>` per-project drill-down (fee, billed, hours by labor code, AR aging, inspections, type, phase, est budget) | PMTools "Projects" tab, the per-row AI context |
| `LoadRevenueTimelineSync` | `Dictionary<WBS1, List<PeriodRevenue>>` (per-WBS1 period series) | Project trend sparklines |
| `LoadFirmUtilizationSync` | `FirmUtilizationStats` (firm billable%, by-year) | YoY firm utilization trend |
| `LoadEmployeeProjectSync` | `List<EmployeeProjectHours>` (per-employee per-project hours + billable predicate) | "ALL EMPLOYEES" AI section + per-project breakdown |
| `LoadEmployeeWeeklyUtilizationSync` | `List<EmployeeWeeklyHours>` (last 12 weeks per employee) | Weekly utilization triggers ("3-week <65% streak") |
| `LoadEmployeeRatesSync` | `List<EmployeeRate>` (billing/cost rates, Partner imputed cost) | Per-employee margin/hr |
| `LoadQuarterlyEmployeeHoursSync` | `List<QuarterlyEmployeeHours>` (2020+ per-employee per-quarter) | Trend depth, drilldown |

### 1.3 The duplication picture (where drift can happen)

- **Methodology prose** lives in 3 places: `Definitions.*.cs` (UI dictionary + AI context source via `FinancialMetricDefinitions.BuildAiMethodologyBlock`), each `BuildContext()` method's hand-written prose, and the MCP system prompt. Post-Batch-89 the MCP prompt cites tools instead of restating formulas; the WPF context providers still embed methodology prose pulled from `Definitions.*.cs`. **Acceptable** as long as `Definitions.*.cs` stays the single source for those blocks.
- **Canonical computation** lives in 2 places: `Kor.Operations.Business/*Service.cs` (Batches 80–89) and `Kor.Operations.App/PMTools/HistoricalAnalyticsService.cs` (this roadmap). After this roadmap: single place.
- **AI context emission** is currently push-style (BuildContext dumps everything every call). Target: scope-only dumps + LLM uses tools for canonical numbers. Same shape as the MCP `/ask` standalone path.

## 2. Target state architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   Kor.Operations.Business                                       │
│                                                                 │
│   *Service.cs (canonical SQL — single source per metric)        │
│   - CashFinancialsService                                       │
│   - ArFinancialsService                                         │
│   - FirmHealthService                                           │
│   - UtilizationService                                          │
│   - WipFinancialsService                                        │
│   - BacklogService                                              │
│   - RecentBilledService                                         │
│   - BilledFinancialsService / GlProfitLossService               │
│   ── new (this roadmap) ──                                      │
│   - ProjectAnalyticsService    (per-project drill, peer budget) │
│   - EmployeeAnalyticsService   (scoring, rates, util by employee│
│   - FirmAnalyticsService       (YoY firm util, revenue timeline)│
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                              ▲                ▲
        consumed by           │                │  consumed by
                              │                │
                ┌─────────────┘                └──────────────┐
                │                                             │
                ▼                                             ▼
┌──────────────────────────────────┐    ┌──────────────────────────────────┐
│ Kor.Operations.App (WPF)         │    │ Kor.Operations.Mcp (MCP gateway) │
│                                  │    │                                  │
│ ExecutiveSummaryDeltekLoader     │    │ Tools/*Tool.cs (one per concern) │
│ FinancialsService                │    │  - get_cash_position             │
│ HistoricalAnalyticsViewModel ────┤    │  - get_ar                        │
│                                  │    │  - get_firm_health               │
│ Each VM still implements         │    │  - get_utilization               │
│ IAiContextProvider, but its      │    │  - get_wip                       │
│ BuildContext() emits CURRENT     │    │  - get_backlog                   │
│ SCOPE data only + tool pointers, │    │  - get_collection_exposure       │
│ NOT methodology prose.           │    │  - get_earned_vs_invoiced        │
│                                  │    │  ── new ──                       │
│ The canonical numbers are        │    │  - get_pm_performance            │
│ identical to what the MCP tools  │    │  - get_dm_performance            │
│ return — same Business service.  │    │  - get_employee_performance      │
│                                  │    │  - get_employee_utilization      │
│                                  │    │  - get_project_detail            │
│                                  │    │  - get_project_yoy_trend         │
│                                  │    │  - get_at_risk_projects          │
│                                  │    │  - get_firm_utilization_by_year  │
│                                  │    │  - get_revenue_timeline          │
│                                  │    │                                  │
│                                  │    │ McpToolRegistry auto-discovers,  │
│                                  │    │ PromptToolParityValidator catches│
│                                  │    │ drift at startup.                │
└──────────────────────────────────┘    └──────────────────────────────────┘
```

**Key principles:**
1. Canonical SQL lives in ONE place per metric (Business).
2. WPF tiles + MCP tools BOTH consume Business services. Numbers are equal by construction.
3. AI context providers emit SCOPE ("what the user is looking at right now"), not METHODOLOGY.
4. Methodology lives in tool `[Description]` attributes. The system prompt cites tools. `Definitions.*.cs` continues to be the UI dictionary's source and pulls Formula strings from tool descriptions (so it can't drift).
5. The parity validator from Batch 89 catches drift on every service start.

## 3. Proposed new tool catalog

All tools are firmwide by default unless an ID parameter is provided. All return `Task<string>` (JSON payload). All decorate with `[McpServerToolType]` + `[McpServerTool(Name="get_...")]` + per-parameter `[Description]` — the auto-discovery registry from Batch 89 picks them up with zero new wiring code.

### 3.1 People-focused

| Tool | Parameters | Returns |
|---|---|---|
| `get_pm_performance` | `pmId?` (optional drill) | Firmwide PM scoreboard (top 25 sorted by performance score) OR single PM detail. Fields: project count, total fee, delivery health, estimation accuracy, revenue efficiency, AR mgmt, unique/repeat clients, repeat rate, months to first bill, % billed in 6mo, AR 90+, booked fee T12/T24/T36. |
| `get_dm_performance` | `dmId?` | Firmwide DM scoreboard. Fields: project count, total fee, performance score + grade. (Simpler than PM scoring — DMs have less direct accountability for AR/revenue.) |
| `get_employee_performance` | `employeeId?` | Firmwide employee scoreboard OR single-employee detail. Fields: productivity score + grade, billable rate score, efficiency score, project health score, fee/hr, peer comparison percentage, consistency label, tenure, billing/cost rate, margin/hr (Partner imputed cost applied). |
| `get_employee_utilization` | `employeeId?` | Per-employee 12-week weekly billable% history + longest <65% streak + 3-week trigger flag. If `employeeId` is null, returns firmwide low-utilization triggers (employees with active 3-week trigger or longest streak ≥ 4). Distinct from firmwide `get_utilization` (which is 30-day rollup). |

### 3.2 Project-focused

| Tool | Parameters | Returns |
|---|---|---|
| `get_project_detail` | `wbs1` (required) | Per-project full snapshot: fee (Fixed + Hourly Extras), billed (posted + unposted), hours by labor code, est budget vs actual (formula + peer-based), AR aging breakdown, subconsultant cost + %, fee/hr, inspections (total + last month), type/phase/PM/DM, FX-normalized to CAD. |
| `get_project_yoy_trend` | `sinceYear?` (default 2020) | Year-over-year rollup across visible/active projects: project count, total fee, avg fee/hr, weighted eng%, weighted billable%, avg sub%, total AR outstanding, firm billable%. |
| `get_at_risk_projects` | `feeMin?` (default $10k), `overBudgetFactor?` (default 1.35) | Projects where `EngHrs > EstEngBudget × factor` AND `Fee >= feeMin`. Sorted by hours-over-budget. Returns top 25 with PM, fee, eng vs est, AR 90+. |

### 3.3 Firm-level analytics

| Tool | Parameters | Returns |
|---|---|---|
| `get_firm_utilization_by_year` | none | Firmwide billable% per year (full history). Companion to `get_utilization` which is 30-day. |
| `get_revenue_timeline` | `sincePeriod?` (default earliest) | Firmwide period-by-period revenue + billed series (months from earliest PRSummaryMain to latest). Companion to `get_recent_billed` which is the latest 3 periods. Useful for "revenue trend over the last 5 years" type questions. |

### 3.4 Client/BD-focused (optional Arc 7)

| Tool | Parameters | Returns |
|---|---|---|
| `get_top_clients` | `sinceYear?`, `topN?` | Top N clients by lifetime fee with project count, repeat rate, last-project date. Pulls from existing CRM/ClientIntelligence App-side logic. |
| `get_client_concentration` | none | Top 10 clients as % of firmwide fee. Standard concentration risk metric. |

Total new tools: **8 (Arcs 1–4) + 2 (Arc 7, optional) = 8–10**.

## 4. Arc-by-arc plan

Each arc is one Codex batch following the Batch 80–89 pattern (Service in Business → Tool wrapping → DI + Program.cs registration → system prompt KPI line citing the tool → version bump → smoke test).

### Arc 1: Port HistoricalAnalyticsService → Business (foundation)

**Scope:** Move `Kor.Operations.App/PMTools/HistoricalAnalyticsService.cs` (~700L) into `Kor.Operations.Business` as three smaller services. No SQL changes. WPF callers (`HistoricalAnalyticsViewModel`, `PmToolsViewModel`) update their `using`s and DI registration.

**Output:**
- `Kor.Operations.Business/ProjectAnalyticsService.cs` — owns `LoadProjectsAsync` (the big project-row query + peer budget estimation) and `LoadRevenueTimelineAsync`.
- `Kor.Operations.Business/EmployeeAnalyticsService.cs` — owns `LoadEmployeeProjectAsync`, `LoadEmployeeWeeklyUtilizationAsync`, `LoadEmployeeRatesAsync`, `LoadQuarterlyEmployeeHoursAsync`.
- `Kor.Operations.Business/FirmAnalyticsService.cs` — owns `LoadFirmUtilizationAsync`.
- `Kor.Operations.App/PMTools/HistoricalAnalyticsService.cs` — deleted, replaced by a thin App-side composer that calls all three services (preserves the existing `LoadAsync(...)` tuple-returning shape for WPF).

**Acceptance:**
- WPF Historical Analytics window opens and renders identical numbers (visual smoke).
- No SQL change (`git diff` on the moved SQL strings should be only catalog-substitution differences — `[{_catalog}]` instead of `[{catalog}]`).
- Build clean across Business / Mcp / App / Tests.

**Effort:** 1 batch (mechanical port). Lower risk than it sounds because the SQL doesn't change; only the file location does.

### Arc 2: People tools

**Scope:** Build 4 MCP tools wrapping `EmployeeAnalyticsService` methods (and a small scoring helper).

**Output:**
- `get_pm_performance` (PmPerformanceTool.cs)
- `get_dm_performance` (DmPerformanceTool.cs)
- `get_employee_performance` (EmployeePerformanceTool.cs)
- `get_employee_utilization` (EmployeeUtilizationTool.cs)

Plus the scoring logic (currently in `HistoricalAnalyticsViewModel`, e.g. `PmPerformanceSummaryRow` computation) gets extracted into the Business service so both WPF and MCP produce identical scores.

**Acceptance:**
- Each tool smoke-tests via /ask end-to-end on KOR-APP01.
- Parity validator passes (auto: tool names cited in prompt).
- Numbers from each tool match the corresponding rows in PMTools window (spot-check 3 PMs, 3 employees).

**Effort:** 1 batch.

### Arc 3: Project tools

**Scope:** Build 3 MCP tools wrapping `ProjectAnalyticsService` methods.

**Output:**
- `get_project_detail` (per-project, parameterized by wbs1)
- `get_project_yoy_trend` (YoY portfolio rollup)
- `get_at_risk_projects` (over-budget filter)

**Acceptance:** /ask smoke for each. Spot-check: at-risk count matches PMTools "Projects" tab with the same filter.

**Effort:** 1 batch.

### Arc 4: Firm-level tools

**Scope:** Build 2 MCP tools.

**Output:**
- `get_firm_utilization_by_year` (FirmUtilizationTool — historical companion to `get_utilization`)
- `get_revenue_timeline` (firmwide period-by-period revenue + billed — extends `RecentBilledService` to full history; could be a method on the same service or a new `RevenueTimelineService` — Codex decides based on what reuses well).

**Acceptance:** Smoke. Compare year totals against PMTools YoY view for 3 years.

**Effort:** 1 batch.

### Arc 5: Trim AnalyticsAiService.BuildContext

**Scope:** `AnalyticsAiService.BuildContext` is currently a 344-line dump of every signal at every level. After Arcs 1–4, the LLM has tools for every signal. The BuildContext shrinks to:
- Currently-selected project / employee / PM (scope)
- A short pointer block: "For firmwide rollups call `get_pm_performance`, `get_employee_performance`, `get_project_yoy_trend`, etc."

This is the same architectural move we made for the MCP system prompt in Batch 89: prose → tool pointers. Token usage per AI call drops dramatically. The LLM's behavior should be unchanged or better (it can drill into tools rather than parsing a 200-row dump).

**Output:** `AnalyticsAiService.cs` reduced from 344L to ~80–100L. `Definitions.*.cs` referenced for any prose methodology that doesn't have a tool equivalent (yet).

**Acceptance:** Smoke test PMTools AI panel with the same 5 questions before/after. Answers should be equivalent in correctness (may differ in style — that's fine).

**Effort:** 1 batch.

### Arc 6 (optional): Definitions.*.cs unification

**Scope:** Make `Definitions.*.cs` Formula strings derive from the canonical sources (tool `[Description]` attribute or a shared constant). Result: Metric Dictionary window cannot drift from tool methodology even by accident.

**Output:** Either a partial class generated at build time, or a runtime indexer that pulls from `McpToolRegistry`. Probably the former for simplicity.

**Acceptance:** A test asserts every `Formula` string in `Definitions.*.cs` either (a) corresponds to a tool, in which case it matches the tool's methodology substring, or (b) is for a metric without a tool (and is allowed to be free-form).

**Effort:** 1 batch.

### Arc 7 (optional, lower priority): BD vertical tools

**Scope:** Wrap CRM / Client Intelligence / Opportunities canonical computations as MCP tools.

**Output:**
- `get_top_clients`
- `get_client_concentration`
- Possibly `get_opportunities_pipeline` (depends on Phase 12 readiness)

**Acceptance:** Smoke + parity. BD module just shipped (per memory `project_opportunities_module.md`, Phase 12 closed 2026-05-03), so this can wait until the data is mature.

**Effort:** 1–2 batches.

## 5. Sequencing and rationale

Recommended order: **Arc 1 → Arc 2 → Arc 3 → Arc 4 → Arc 5 → (Arc 6 → Arc 7 as desired).**

- Arc 1 is the **structural prerequisite**. Everything else builds on the relocated service.
- Arc 2 (People) is **highest daily value**. PM/employee performance is what Ian and the partners look at most. Drift here is most visible.
- Arc 3 (Projects) extends per-project drill-down into LLM-callable form. Useful for "tell me about WBS1 X" questions where today the LLM has to either find the row in a 200-line dump or invent SQL.
- Arc 4 (Firm-level) is low-risk filler. Could be merged with Arc 3 if both end up small.
- Arc 5 (BuildContext trim) **must come after Arcs 1–4** — can't shrink the context dump before the tools exist to replace it.
- Arc 6 (Definitions unification) is belt-and-suspenders. Real-world cost of skipping it: low (cosmetic drift in Metric Dictionary). Worth doing only if Ian wants the airtight version.
- Arc 7 (BD) is independent. Slot in whenever BD becomes priority again.

## 6. Open questions / risks

### 6.1 Performance scoring is in the ViewModel today, not the service

`PmPerformanceSummaryRow`, `EmployeeSummaryRow`, `PerformanceGrade`, `ProductivityScore`, `BillableRateScore`, etc. — the scoring rules live in `HistoricalAnalyticsViewModel` (~1500L). Some of this is presentation logic (colors, format strings), but some is real domain logic (the weighting / grading rules).

**Decision needed before Arc 2:** Does the scoring weight live in the Business service (so MCP tools return scored rows directly) or in a separate scoring layer the WPF VM also uses? Recommendation: **extract a `Kor.Operations.Business/Scoring/PerformanceScoring.cs` static class** that both the VM and the tools consume. Keeps SQL and scoring in Business; keeps presentation in the VM.

### 6.2 The Financials window per-row drill-down

`FinancialsService.cs` (~1500L App-side) is parallel to `HistoricalAnalyticsService` — both load per-project rows but with different shapes for different consumers. After this roadmap, the canonical answer for "what's project X's status?" should come from `get_project_detail` (Arc 3). Should `FinancialsService` also be ported? My read: not in this roadmap. It's a different vertical (Financials window per-row UI) and porting it has no AI benefit. Leave it alone unless future refactors say otherwise.

### 6.3 Scope of "Historical" — does Quarterly Employee Hours need a tool?

`LoadQuarterlyEmployeeHoursSync` exists but is only used in one specific drilldown window. Not currently in the AI context. Worth wrapping? **Skip for now** unless Ian uses it heavily.

### 6.4 Tool catalog growth

After this roadmap the MCP tool count goes from 10 → ~18. The system prompt's KPI METHODOLOGY block grows by ~8 lines. The auto-discovery registry handles it; tokens for the tool catalog grow ~2× but the saved tokens from shrunk BuildContext (Arc 5) more than offset.

Anthropic's prompt cache means the static tool catalog + system prompt cost ~10% on the wire after the first call in a 5-minute window. Not a real concern.

## 7. Out of scope

- **PdfToSafe AI** — engineer-markup vision, different vertical (`feedback_pdftosafe_scope.md`).
- **FileSync** — out of AI scope (`feedback_filesync_excluded_from_ai.md`).
- **Alert system rule changes** — separate concern (`project_alert_system_design.md`).
- **Anthropic model upgrades** — orthogonal.
- **Multi-tenant isolation** — orthogonal; KOR is single-tenant.

## 8. Acceptance criteria for "roadmap complete"

After Arcs 1–5:
1. `Kor.Operations.App/PMTools/HistoricalAnalyticsService.cs` is deleted; equivalent canonical SQL lives in Business.
2. PMTools, Historicals, Staff Utilization AI panels return correct answers using tool calls (not 200-line context dumps).
3. Parity validator (Batch 89) passes at startup for all ~18 tools.
4. New runbook section in `Kor.Operations.Mcp.add-or-change-tool.md` covers the people/project/firm tool patterns.
5. Spot-check: 5 questions ("how is JM doing?", "which projects are over budget?", "what's our YoY billable trend?", "show me WBS1 30862-CA", "who has been under 65% billable for 3+ weeks?") return identical narrative answers before/after Arc 5.

## 9. Estimated total effort

| Arc | Batches | Lines of code (rough) | Risk |
|---|---|---|---|
| 1: Port HistoricalAnalyticsService | 1 | ~700 moved + small composer | Low (mechanical) |
| 2: People tools | 1 | ~400 new (4 tools + scoring extract) | Medium (scoring logic clarification) |
| 3: Project tools | 1 | ~300 new (3 tools) | Low |
| 4: Firm-level tools | 1 | ~200 new (2 tools) | Low |
| 5: Trim BuildContext | 1 | –250 net (delete > add) | Medium (behavior may shift on style) |
| 6: Definitions unification | 1 | ~150 new + 1 test | Low |
| 7: BD vertical | 1–2 | ~400 new | Medium (depends on BD data maturity) |
| **Total (Arcs 1–5 core)** | **5** | **+1,250 lines net** | |
| **Total with optional 6+7** | **7–8** | | |

Each batch follows the established cadence: Codex prompt → Codex implements → Claude verifies + builds + tests → smoke test → commit → deploy via runbook. Roughly 30–60 minutes wall-clock per batch.

## 10. Decision log (for future-Ian)

- **2026-05-13** — Roadmap drafted post-Batch-89. Ian asked for uniformity across PMTools/Historicals/StaffUtil following the Batch 80–89 financial pattern. Plan calls for 5 core batches + 2 optional. Foundation arc (port `HistoricalAnalyticsService`) is the prerequisite; recommend executing arcs in order Arc 1 → 2 → 3 → 4 → 5.
