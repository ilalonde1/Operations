# Session Handoff — Kor.Operations App

Point the next Claude Code conversation to this file for full context. Written 2026-04-14 after a major milestone commit (`2f90e0e`).

---

## Who I Am

**Ian Lalonde** — Senior Structural Engineer + IT at KOR Structural (Vancouver BC). Expert in both code and structural engineering. This is my ops platform built on top of Deltek Vantagepoint.

Workflow preference: I paste prompts via `C:\Users\ilalonde\Desktop\paste_here.txt` when they're long.

---

## My Non-Negotiable Tenets

These are not suggestions. Follow them or I will correct you.

1. **ZERO GUESSING.** Verify from source data before acting. Every time. No exceptions.
   - Deltek schema dump lives at `C:\Users\ilalonde\Desktop\Claude\columns.csv` — check it before adding any column reference.
   - If you're about to recommend something, prove it exists first.

2. **Senior Architect approach** — analyze, design, prompt, verify, commit. One thing at a time. No half-assed fixes.

3. **No code changes until I say "go ahead"** — deliver as plans/prompts first, I verify, then we commit.

4. **ZERO REGRESSION.** Don't break what works. If you touch a file, understand what else depends on it.

5. **Always check DI wiring in `CompositionModules/`** before touching any service. Real stores are in `Kor.Operations.Data` (SQL Server), not `Kor.Operations.Core`.

6. **Always implement `IAiContextProvider`** on new ViewModels/modules — the AI assistant must see all data.

7. **Commercial grade** — this is a paid product for engineers, not a toy. Polish matters.

8. **Grade-3 readable tooltips** — my users include people who aren't technical. Plain English, explain what + why.

---

## Architecture Quick Reference

**Solution**: `Kor.Operations.App/Kor.Operations.App.sln` — 10 projects including EmailFilerv2 (VSTO Outlook add-in, lives at `EmailFiler/EmailFilerv2/` in this repo)

**Key projects:**
- `Kor.Operations.App` — main WPF app (net8.0-windows10.0.19041.0)
- `Kor.Operations.Core` — shared domain code (netstandard2.0-ish patterns)
- `Kor.Operations.Data` — SQL Server stores (real persistence lives here)
- `Kor.Operations.Graph` — Microsoft Graph / Outlook integration
- `Kor.Operations.Rendering` — PDF/image rendering
- `Kor.EmailSearch.Core` — email search (uses Dapper + stored procs against KorEmailIndex)
- `Kor.EmailCommon` — shared email utilities
- `EmailFilerv2` — VSTO Outlook add-in (.NET Framework)

**Databases:**
- Deltek Vantagepoint — accessed via ODBC (DataDirect driver). **NOT** accessible via SSMS or linked servers. Credentials via `DeltekOdbcOptions`.
- `KorEmailIndex` — SQL Server on `KOR-APP01\SQLEXPRESS` — email search index
- Kor transmittals DB — SQL Server

**Catalog**: `C0000052267P_1_KOR00000000` (Deltek)

---

## Critical Deltek Knowledge

- **PR table** composite key: `WBS1 + WBS2 + WBS3`
  - WBS1-only rows (blank WBS2) = parent project
  - WBS2 = phases/elements
  - WBS3 = sub-elements within an extra
- **`pr.Fee` on parent row** INCLUDES fixed-fee extras (X-prefixed elements)
- **`PRSummaryMain.Revenue`** rolls up ALL WBS2/WBS3 elements — includes hourly/T&M revenue
- **`tkDetail`** holds timesheet entries at WBS2+WBS3 granularity — `RegHrs + OvtHrs`
- **Employee names**: `EMMain.FirstName + LastName` (NOT EMCompany — that only has HireDate)
- **HireDate**: lives in `EMCompany.HireDate` (not EMMain)
- **X-prefixed WBS2** (X.1, X.2...) = extras; numbered (1.PD, 2.SD...) = initial contract phases
- **Deltek budget hours are basically never entered** at KOR — don't assume PRLabor has real data

**Labor codes** (`LaborCodes.*`):
- 10 = Engineering
- 20 = Drafting
- 30 = Checking (merged into Engineering for metrics)
- 40 = Inspection
- 50 = DocPrep
- 60 = General
- 70 = Admin
- 80 = NonBillable

---

## The Hourly Extras Model (MILESTONE 2f90e0e)

**Problem we solved**: `pr.Fee` was fixed-fee only, but `FeeBilled` (from `PRSummaryMain.Revenue`) already included hourly/T&M revenue. Every ratio involving Fee was wrong.

**Solution rule**: `TotalFee = Fee + HourlyRevenue` is used EVERYWHERE in metrics/calculations/filters/summations/peer matching/displays. The `Fee` property stays as the fixed-fee reference for display only.

**Identifying hourly extras**: WBS3 elements where `pr.Fee = 0` AND `PRSummaryMain.Revenue > 0`. Elements with Fee=0 and Revenue=0 but hours>0 are **absorbed** contract work (tracked separately but covered by fixed fee).

**Budget mode toggle** (`DeltekOdbcOptions.UseTargetRateBudget`):
- **Peer-Based** (default): Deltek actuals → peer median (3+ matches) → formula fallback
- **Target Rate**: `TotalFee / TargetBillingRate` for every project

**Peer matching range**: ±15% tight, widens to ±30% fallback (tightened from ±30%/±50%)

**Where to NOT use TotalFee:**
- SQL queries reading `pr.Fee` from Deltek (correct source)
- SQL WHERE clauses filtering `pr.Fee = 0` to identify hourly elements (classification logic)
- The `Fee` property itself on row models (stores the fixed-fee component for reference)
- Fee breakdown card's "Fixed Fee" / "Initial Contract" sections

---

## Fee Breakdown (Project Detail Window)

Three-level drill-down in `ProjectFinancialDetailWindow.xaml(.cs)`:

1. **Fee breakdown card** sections:
   - INITIAL CONTRACT (numbered phases: 1.PD, 2.SD, 3.DD, 4.CD, 5.CA)
   - FIXED FEE EXTRAS (X-prefixed WBS3 with Fee > 0)
   - TRACKED WORK (FIXED FEE) — X-prefixed WBS3 with Fee=0, Revenue=0, Hours>0 (absorbed contract work)
   - HOURLY EXTRAS — WBS3 with Fee=0, Revenue>0 (true T&M)
   - Columns: Name | Fee | Hours | $/Hr (red if below $185 target)

2. **Click a row** → employee breakdown (name + category + date range + hours)
3. **Click an employee** → timesheet entries grouped by month, day pills with tooltips showing date/hours/comment

---

## Key Files Reference

### Financial metrics chain
- `Financials/FinancialsService.cs` — main query + row assembly, hourly rev query, budget tier logic
- `Financials/FinancialsHeadlineCalculator.cs` — portfolio KPIs
- `Financials/DeliveryConfidenceCalculator.cs` — Critical/AtRisk/Watch/HighConfidence
- `Financials/FinancialMetricDefinitions.cs` — centralized metric descriptions (tooltip source)
- `Financials/AnalyticsThresholds.cs` — ALL magic numbers live here (OverBudgetFactor, DeliveryGapThreshold, TargetBillingRate etc.)
- `Financials/ProjectFinancialDetailWindow.xaml(.cs)` — detail window with fee breakdown
- `Shared/PeerBudgetEstimator.cs` — peer median budget estimator

### PM Tools pipeline
- `PMTools/HistoricalAnalyticsService.cs` — separate query pipeline (mirrors FinancialsService)
- `PMTools/HistoricalAnalyticsViewModel.cs` — filters, aggregations, peer matching, employee attribution
- `PMTools/HistoricalProjectRow.cs` — row model with TotalFee computed props
- `PMTools/PmProjectRow.cs` — PM Tools row wrapper (Fee = TotalFee from source)
- `PMTools/EmployeeSummaryRow.cs` — employee scoring (peer comparison, tenure, consistency)

### AI integration
- `Services/AppAiService.cs` — wraps Anthropic API (claude-sonnet-4-6, retry on 429)
- `Services/AppAiContextBuilder.cs` — DI singleton, collects from `IAiContextProvider`s
- `Controls/AiQueryPanel.xaml` — "What The Heck Does All This Mean?!" panel
- `PMTools/AnalyticsAiService.cs` — builds AI context from ViewModel data

### Email search
- `EmailSearchWindow.xaml(.cs)` — UI with autocomplete TextBox + filtered popup
- `Kor.EmailSearch.Core/EmailSearchService.cs` — Dapper call to `dbo.SearchEmailsPaged` (120s timeout)
- Stored proc `dbo.SearchEmailsPaged` lives in KorEmailIndex DB (not in repo)
- Full-text index on `dbo.Emails` covers: Subject, BodyText, FromEmail, ProjectNumber

### Shared controls
- `Controls/LoadingOverlay.xaml(.cs)` — standard spinner, use `Show("msg")` / `Hide()`
- `Controls/KorHeader.xaml` — standard window header with name/avatar

---

## UI Patterns

**TextBox + Popup autocomplete** (Email Search, Transmittals Dashboard, Create Transmittal, Preferences):
- Use `PreviewKeyDown` on the TextBox (NOT KeyDown — TextBox swallows arrow keys in KeyDown)
- Popup with filtered ListBox inside
- Arrow Down/Up enters/navigates list, Enter selects, Escape closes, single-click picks
- Arrow Up from top of list returns focus to TextBox

**Row health coloring** (Financials grid):
- Critical = `#FEE2E2` (red)
- At Risk = `#FEF3C7` (amber)
- Watch = `#FEF9C3` (yellow)
- High Confidence = `#F0FDF4` (green)

**Budget mode pill** (Financials + PM Tools headlines):
- Blue (#DBEAFE bg, #1D4ED8 text) = "Peer Median"
- Amber (#FEF3C7 bg, #92400E text) = "Target Rate"

**$/Hr highlighting**:
- Below $185 target → red text + light red row background (#FEE2E2)

---

## Things That Are Stubbed / Missing

- **Exec Summary KPIs** have `DataUnavailable` fallbacks for not-yet-sourced items (Cash Position needs CFGBanks, etc.) — this is intentional, handles gracefully
- **VSTO signing cert** expired 2026-04-14 — renew via VS → EmailFilerv2 → Properties → Signing → Create Test Certificate

---

## Recent Major Decisions (Don't Re-litigate)

- **TotalFee everywhere** — every Fee-based metric uses TotalFee. Rule: "Hours Spent and FeeBilled already include hourly components. Fee must too, or every ratio involving Fee is wrong."
- **Peer range tightened to ±15%/±30%** — was ±30%/±50%, too wide
- **Delivery Trend card removed** — was stub data
- **CFO Metrics card removed** — duplicated info shown elsewhere
- **Total GFA + Avg Fee per ft² removed** — inconsistently entered in Deltek
- **Budget mode toggle added** — peer vs target rate comparison is a feature, not a confusion
- **Email search**: added FromEmail + ProjectNumber to FT index, rewrote stored proc to use `CONTAINS` only (no `LIKE '%...%'` scans)

---

## Commit Style

Match the existing commit message style (see `git log`):
- Short imperative subject line with prefix (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`)
- Blank line
- Body explains WHY, not just what
- Include `Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>` trailer

---

## Auto-Memory Location

`C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Operations\memory\` — user preferences and session-level memory persists here automatically. Read `MEMORY.md` there for the index.

---

## Where To Start Reading In A New Session

1. This file (`SESSION_HANDOFF.md`) — overview
2. `git log --oneline -20` — recent direction
3. `Financials/FinancialsService.cs` (lines 130–250) — the central row assembly, shows the whole metric chain
4. `Financials/AnalyticsThresholds.cs` — all the magic numbers with rationale
5. Whatever specific file the current task touches

Then **ask me before writing code**. Every time.
