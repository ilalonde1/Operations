# Module 05 — The AI layer: conversational "virtual CFO" (`Kor.Operations.Mcp`)

Audit date 2026-08-20. Rubric: `docs/audit-2026-08/RUBRIC.md`. Evidence tiers inline.

---

## 1. What I searched

**Prior art / doc check first (CLAUDE.md rules 1 and 2).** Before touching anything I read
`docs/audit-2026-08/RUBRIC.md` in full, then checked the doc dates against the code:
`git log -1 --format=%ad -- docs/architecture/Kor.Operations.Mcp.md` → **2026-05-10**;
`Kor.Operations.Ai.consolidation-roadmap.md` → **2026-05-13**;
`docs/runbooks/Kor.Operations.Mcp.deploy.md` → **2026-05-09**; latest code commit touching
`Kor.Operations.Mcp` → **2026-07-31**. All three docs are older than the code and are treated as
hypotheses below. I also loaded the `claude-api` skill before opening `AskService.cs` (its trigger
fires on any Anthropic/`claude-*` work) to get the current model/pricing table rather than answer
from memory.

**Source read (all local, no share walks):**
`Kor.Operations.Mcp/Program.cs` (433 L), `Ai/AskService.cs` (1,143 L), `Ai/McpToolRegistry.cs`,
`Ai/PromptToolParityValidator.cs`, `Auth/BasicAuthMiddleware.cs`, `Options/McpOptions.cs`,
`Tools/*.cs` (25 files, 3,326 L), `Vocabulary/candidates.md` + `extract_candidates.ps1`,
`Kor.Operations.Mcp.Smoke/Program.cs` + `SmokeCoverageValidator.cs` + `TestCases.cs` +
`Config/SmokeConfig.cs`, `Kor.Operations.Mcp.Tests/**`, `Kor.Operations.App/Services/AppAiService.cs`,
`Kor.Operations.App/Controls/AiQueryPanel.xaml.cs`,
`Kor.Operations.App/CompositionModules/CompositionHelpers.cs`, `Kor.Operations.App/App.config`,
`Kor.Operations.Business/{Billed,Cash,GlProfitLoss,Wip}*.cs`, `SharedOptions.cs`.

**Greps:** provider detection (`openai|langchain|genai|mistralai|cohere|ollama` → **zero hits**;
`anthropic|claude` → 5 files); `claude-|api.anthropic|x-api-key|ANTHROPIC`;
`TODO|FIXME|HACK|NotImplementedException|NotSupportedException` across the project;
`catch\s*(\([^)]*\))?\s*\{\s*\}` for swallowed exceptions; `AiQueryPanel` host windows;
`git ls-files` / `git show HEAD:` on `App.config`.

**Live state (read-only — GET and SELECT only, no POST to `/ask`, no writes anywhere):**
- `Test-NetConnection kor-app01 -Port 5500` → open.
- `GET http://kor-app01:5500/health` → 200.
- `GET http://kor-app01:5500/tools` unauthenticated → **401**; with Basic auth → 23 tool names.
- `Get-CimInstance Win32_Service -ComputerName kor-app01 -Filter "Name='Kor.Operations.Mcp'"`.
- `\\kor-app01\c$\Program Files\KorOperations\Mcp\` directory listing, `Logs\` listing,
  `appsettings.Production.json` (read with secrets redacted), `Get-Acl` on that file.
- Remote registry (`OpenRemoteBaseKey`) → `SOFTWARE\ODBC\ODBC.INI` for the Deltek DSN.
- **Byte-level UTF-16/UTF-8 scans of the deployed `Kor.Operations.Mcp.dll` and
  `Kor.Operations.Business.dll`** to prove which code is actually running (CLAUDE.md rule 5 —
  verify the artifact, not your reading of it; my first ASCII `grep` of the DLL gave three false
  ABSENT results because .NET string literals live in the UTF-16 `#US` heap, so I redid it).
- SQL `SELECT` against `KorMcp` and `KorOpportunitiesDb` on `KOR-APP01\SQLEXPRESS` as `mcp_app`:
  `Mcp.AuditLog` schema + usage rollups, `Mcp.CooCardItem` / `Mcp.CooBrief` / `Mcp.Alert`
  freshness, `sys.database_role_members` for the login's actual rights.

**Ran:** `dotnet test Kor.Operations.Mcp.Tests -c Debug` (targeted, per rubric rule 4 — never the
full suite) and `dotnet run --project Kor.Operations.Mcp.Smoke --no-build`. The smoke harness aborts
at its coverage gate before it can issue any `/ask` call (verified in `Program.cs` lines 39–56
before running it), so running it stayed inside the read-only rule.

**Not checked:** I did not execute a single `/ask` question. `/ask` is a POST that spends money on
KOR's Anthropic key, and the rubric is GET-only. Every claim below about a tool's *numeric output*
is therefore `[READ]` or inferred from the deployed binary — never `[RUN]`. The command that would
close that gap is in §7.

---

## 2. What this module is

`Kor.Operations.Mcp` is a Windows service on KOR-APP01 that turns plain-English questions from firm
leadership into answers computed from Deltek Vantagepoint. It is the same idea as a BI dashboard,
inverted: instead of picking a report, you type "why did February expenses jump?" and the server
runs a tool-using Claude conversation against a catalog of 23 read-only tools, each of which wraps
the *same* canonical C# service the WPF financial screens call. The architectural bet — and it is a
good one — is that the LLM never computes a financial number itself. It picks a tool, the tool runs
KOR's audited SQL, and the model's only job is to choose, narrate and compare. The Anthropic API key
lives on the server, never on a workstation, and every question and every tool call is written to
`Mcp.AuditLog` with the caller's UPN.

What a user sees: the AI panel docked in the Financials window (and in Historical Analytics, PM
Capacity, Staff Utilization, Workload Meeting, PdfToSafe). They type a question; the panel appends a
`[CURRENTLY VIEWING]` block describing the org filter, date range and on-screen KPIs, POSTs it to
`http://kor-app01:5500/ask`, and 4–70 seconds later gets 3–6 sentences of prose with real client and
employee names in them. The same service also runs three unattended weekly jobs off a Quartz cron
(Mondays 06:00 Pacific): an alert sweep, a COO Brief, and a COO Card — all of which route through
the identical `/ask` loop and land in tables the WPF `MondayBriefingWindow`, `CooCardWindow` and
`CollectionsWindow` read back over HTTP.

---

## 3. How you would demo it

**It is running right now.** `[QUERIED]` `GET http://kor-app01:5500/health` →
`{"status":"ok","service":"Kor.Operations.Mcp","version":"0.4.2+5b9535f7a1fbfcf64382adb03631279a6218620c",...}`.
`[QUERIED]` `Win32_Service` on kor-app01: `State=Running, StartMode=Auto, StartName=KOR\app-admin`.
`[QUERIED]` `Mcp.AuditLog` holds 2,158 rows, 427 of them `/ask`, most recent `/ask` 2026-08-17.

**Prerequisites.** On the KOR LAN or VPN (the service is `http://` on port 5500, LAN-only by
design per the deploy runbook — no TLS, no reverse proxy). The WPF app needs
`McpServer.ServiceUrl/Username/Password` in `App.config` (present). Deltek reachability is the
server's problem, not the client's — the DSN lives only on KOR-APP01.

**Click-path.** Launch `Kor.Operations.App` → **Financials** → the AI panel is docked in that
window → type a question → answer appears in the panel. Second surface: **Monday Briefing** and
**COO Card** windows, which show last Monday's generated content — `[QUERIED]` latest `CooCardItem`
generated 2026-08-17 13:06 UTC, `CooBrief` 13:05, `Alert` 13:00, so the next unattended run
(Monday 2026-08-24) lands before the MVE demo and will be fresh.

**Questions that are safe to ask on screen** (they route to structured tools whose deployed code
matches the dashboard): AR and aging, backlog, firm health / net multiplier, utilization,
billed P&L for a CAD-org period, at-risk projects, per-PM performance, project deep-dive,
BD pursuit lists. **Questions to avoid: anything about WIP or cash position** — see §5, both are
returning wrong numbers from the live build.

**Latency is the thing to rehearse.** `[QUERIED]` across 255 human-asked `/ask` calls:
min 4.0 s, mean 10.5 s, max 72.9 s. The seven most recent calls (all the automated Monday
generators, which are heavier) ran 26.7 s–109.4 s. A live question in front of MVE will sit on a
spinner for ten seconds and could sit for a minute. The code comment in
`AiQueryPanel.xaml.cs:77` still says "8-17s /ask calls" — that estimate is stale.

---

## 4. Completeness

### 4.1 The 23 live tools

`[QUERIED]` `GET /tools` with Basic auth returns exactly 23 names, matching the registry composed in
`Program.cs:134-160`. The task brief's "25 tools" counts 25 `.cs` files in `Tools/`: 23 registered
tools + `ServerInfoTool` (MCP-wire ping only, deliberately outside the `/ask` registry) +
`ToolErrorEnvelope` (a static helper, not a tool).

**Important caveat on this table.** No tool was executed end-to-end (see §1). "WORKING" here means:
registered live, wired to a real canonical service, and the deployed binary's configuration matches
the WPF dashboard's. It does **not** mean I saw the number come back.

| # | Tool | State | Evidence |
|---|------|-------|----------|
| 1 | `get_ar` | WORKING | `[QUERIED]` live in `/tools`; `[READ]` `ArTool.cs` → `ArFinancialsService.LoadAsync` |
| 2 | `get_at_risk_projects` | WORKING | `[QUERIED]` live; `[READ]` `ProjectAnalyticsService` |
| 3 | `get_backlog` | WORKING | `[QUERIED]` live; `[READ]` `BacklogService.LoadAsync` |
| 4 | `get_bd_action_status` | WORKING | `[QUERIED]` live; `[READ]` `IBdReportService.GetActionRollupAsync`; `[QUERIED]` `mcp_app` has `db_datareader` on `KorOpportunitiesDb`, 67 `opportunities.*` tables visible |
| 5 | `get_bd_call_sheet` | WORKING | `[QUERIED]` live; `[READ]` `GetCallSheetPoolAsync` + `GetSectorSummariesAsync` |
| 6 | `get_bd_pursuit_list` | WORKING | `[QUERIED]` live; `[READ]` `GetSectorPursuitsAsync` |
| 7 | `get_billed_pnl` | **PARTIAL** | `[RUN]` deployed `Business.dll` has **no** `UsdToCadRateByYear` → live MCP FX-converts USD orgs at a flat **1.36** while `App.config` uses per-year **1.378457** for 2026. CAD-org and firmwide-CAD answers are fine; any USD-org or blended number drifts ~1.4% from the dashboard |
| 8 | **`get_cash_position`** | **BROKEN — wrong number** | `[RUN]` UTF-16 scan of deployed `Kor.Operations.Mcp.dll`: string `CashAccountWhitelist` **ABSENT**. The deployed `Program.cs` never copies it into `FinancialsOptions`, and `CashFinancialsService.cs:54-56` filters only when the whitelist is non-empty → all **20** `CFGBanks` accounts are summed instead of the 3 Daler whitelisted. See §5.2 |
| 9 | `get_collection_exposure` | WORKING | `[QUERIED]` live; `[READ]` composes `ArFinancialsService` + `RecentBilledService` |
| 10 | `get_dm_performance` | WORKING | `[QUERIED]` live; `[READ]`; `[RUN]` 1 unit test in `DmPerformanceServiceTests` |
| 11 | `get_earned_vs_invoiced` | WORKING | `[QUERIED]` live; `[READ]` |
| 12 | `get_employee_performance` | WORKING | `[QUERIED]` live; `[RUN]` 5 unit tests |
| 13 | `get_employee_utilization` | WORKING | `[QUERIED]` live; `[READ]` |
| 14 | `get_firm_health` | WORKING | `[QUERIED]` live; `[READ]` `FirmHealthService.LoadAsync` |
| 15 | `get_firm_utilization_by_year` | WORKING | `[QUERIED]` live; `[READ]` |
| 16 | `get_gl_pnl` | WORKING | `[QUERIED]` live; `[READ]` `GlPnLTool.cs:114` hardcodes `flipSign: true`, matching `App.config` `Financials.PnL.GlFlipSign=true`; the GL group-type keys are absent from server config but `ParseGroupTypeSet` falls back to code defaults |
| 17 | `get_pm_performance` | WORKING | `[QUERIED]` live; `[RUN]` 4 unit tests |
| 18 | `get_project_detail` | WORKING | `[QUERIED]` live; `[READ]` |
| 19 | `get_project_yoy_trend` | WORKING | `[QUERIED]` live; `[RUN]` 4 unit tests in `YearTrendServiceTests` |
| 20 | `get_revenue_timeline` | WORKING | `[QUERIED]` live; `[READ]` |
| 21 | `get_utilization` | WORKING | `[QUERIED]` live; `[READ]` `UtilizationService.LoadAsync` |
| 22 | **`get_wip`** | **BROKEN — sign transposed** | `[RUN]` UTF-8 scan of deployed `Business.dll`: `LoadFirmwideWipProxyBalance` **PRESENT** (old name), `SplitWipNet` and `LoadFirmwideWipBalance` **ABSENT** → the 2026-07-31 fix is not deployed. Commit `818ebc19` states it outright: *"the MCP get_wip tool is live and has been answering /ask with the two transposed."* See §5.1 |
| 23 | `query_kor_data` | WORKING (fragile) | `[QUERIED]` live; `[RUN]` 15 gate unit tests pass; `[QUERIED]` **all 39 errors in the last 60 days are this tool hitting its 30 s SQL timeout** — 100% of the module's logged failures |
| — | `ServerInfoTool.Ping` | WORKING | `[READ]` MCP-wire only; not exposed to `/ask` by design |
| — | `ToolErrorEnvelope` | n/a | `[READ]` static helper; `[RUN]` 10 unit tests |

**Summary: 19 of 23 WORKING, 2 PARTIAL/BROKEN with wrong live numbers, 1 WORKING-but-fragile,
0 STUBBED, 0 DEAD.** No tool is a stub. Every one is wired to a real service.

### 4.2 Debt markers

`[RUN]` `grep -rnE "TODO|FIXME|HACK|NotImplementedException|NotSupportedException"` across
`Kor.Operations.Mcp` excluding `obj/`: **0 hits.**
`[RUN]` `grep -rnE "catch\s*(\([^)]*\))?\s*\{\s*\}"`: **0 empty catch blocks.** Every `catch` in
`Tools/*.cs` (2 per file, consistently) routes to `ToolErrorEnvelope`. This is the cleanest module
in the suite by these measures — genuinely, not by omission.

### 4.3 Supporting capabilities

| Capability | State | Evidence |
|---|---|---|
| Service live on `kor-app01:5500` | WORKING | `[QUERIED]` `/health` 200, service Running as `KOR\app-admin` |
| Basic auth enforced | WORKING | `[QUERIED]` `/tools` unauthenticated → **401**; with creds → 200 |
| Audit trail (every question + tool call) | WORKING | `[QUERIED]` `Mcp.AuditLog` 2,158 rows, latest 2026-08-21 05:54 |
| Prompt/tool parity gate at startup | WORKING | `[READ]` `PromptToolParityValidator` throws at boot on drift; service is up, so parity holds |
| Prompt caching (`cache_control: ephemeral`) | WORKING | `[READ]` `AskService.cs:325` on system block; `GetCacheableToolDefinitions()` on tools |
| Per-user concurrency gate | WORKING | `[READ]` `AskService.cs:159-168`, 1 in-flight question per UPN, 30 s wait then clear message |
| Per-question 300 k input-token budget | WORKING | `[READ]` `AskService.cs:37`, `:404-419` |
| 429 retry honouring `Retry-After` | WORKING | `[READ]` `AskService.cs:355-380`, 3 retries, aborts if `Retry-After` > 35 s |
| Infra + timeout circuit-breakers | WORKING | `[READ]` `AskService.cs:544-620`, 2 consecutive all-fail iterations → plain-English abort |
| Multi-turn history + prior-tool-call trace | WORKING | `[READ]` `AskService.cs:270-296`, TTL 2 h, cap 200 conversations |
| Weekly alert / COO Brief / COO Card jobs | WORKING | `[QUERIED]` 136 alerts, 96 briefs, 75 card items; all generated 2026-08-17 |
| **End-to-end accuracy harness (Smoke)** | **DEAD** | `[RUN]` aborts at the coverage gate — see §7 |
| `Vocabulary/` | **DEAD** | `[RUN]` see §4.4 |

### 4.4 `Vocabulary/` — what it actually is

It is **not** a term/synonym map and the AI does not use it. `[READ]` `Vocabulary/candidates.md` is
a 4,142-line auto-generated dump of C# SQL string literals scraped out of the WPF app by
`extract_candidates.ps1`. Its own header says: *"Mark items KEEP / DROP / REWRITE as you review; the
keepers become the AI's vocabulary."*

`[RUN]` `grep -cE '^\s*(KEEP|DROP|REWRITE)' candidates.md` → **0**. The review never happened.
`[RUN]` `git log -1 -- Kor.Operations.Mcp/Vocabulary/` → **2026-05-08**, untouched in 3.5 months.
`[RUN]` `grep -rn "Vocabulary\|candidates.md" --include=*.cs` → **no code reference anywhere**;
it is not in the `.csproj` and not shipped.

So: a scratch artifact from the Phase-11b design week, abandoned. **Does the AI understand
firm-specific vocabulary? Yes — but from somewhere else entirely.** The real vocabulary is the
51,770-character system prompt hardcoded in `AskService.cs:610-1140`, which carries KOR's verified
Deltek column lists, the LaborCode map, the canonical revenue accounts (4001/4003/4210/4220/4240,
"4260 is INTERCOMPANY — exclude it", "4500 does NOT exist at KOR"), the org codes
(CAD/USA/BCC, "NEVER use 'KOR' or 'KORUSA'"), the `opportunities.*` schema, and personalisation
rules (JB / Jim / JM). That is where the firm's language lives. `Vocabulary/` should be deleted so
nobody mistakes it for the source of truth.

---

## 5. What is broken or risky

### 5.1 `get_wip` is answering with earned and overbilled transposed — LIVE, TODAY

**Severity: highest in this module.** This is a confidently-wrong financial number, already shipped.

`[RUN]` The deployed `\\kor-app01\c$\Program Files\KorOperations\Mcp\Kor.Operations.Business.dll`
(file date **2026-07-17 18:55**) contains `LoadFirmwideWipProxyBalance` and does **not** contain
`SplitWipNet` or `LoadFirmwideWipBalance` — the symbols the 2026-07-31 fix introduced.

`[RUN]` `git merge-base --is-ancestor 818ebc19 5b9535f7` → **not an ancestor**. Commit `818ebc19`
("fix(financials): WIP sign convention was inverted in both branches", 2026-07-31) is **not in the
deployed build**. Its message quantifies the error at period 202602 across 2,958 project rows:

```
              before          after
  earned      3,014,551.14    2,805,253.10
  overbilled  2,805,253.10    3,014,551.14
  net          +209,298.04     -209,298.04
```

and states plainly: *"The WPF tile has been hidden for months so users did not see this, but the
MCP get_wip tool is live and has been answering /ask with the two transposed."*

The same commit also records that `WipTool`'s methodology string claims KOR runs Revenue Generation
OFF, **verified false** — `PRSummaryMain.Unbilled` is populated (238 rows). Note the second-order
problem: the system prompt at HEAD *still* tells the model
`"Deltek Revenue Generation is OFF at KOR"` (`AskService.cs:1040`). Deploying the fix corrects the
tool but leaves that prompt line contradicting it.

### 5.2 `get_cash_position` is summing 20 bank accounts instead of 3 — LIVE, TODAY

`[RUN]` UTF-16 scan of the deployed `Kor.Operations.Mcp.dll`: the literal `CashAccountWhitelist`
is **absent** (as are `CashUsdAccounts`, `PnLOverheadRate`, `FiscalYearStartMonth`,
`UsdToCadRateByYear`). `[READ]` The deployed `Program.cs` (`git show 5b9535f7:...`) builds
`FinancialsOptions` and stops at `PnLExpenseGroupTypes` — it never reads those five keys.

`[QUERIED]` The key **is** correctly set in `appsettings.Production.json`
(`"CashAccountWhitelist": "1110.00,1120.00,1170.00"`) — it is simply dropped on the floor by the
code. `[READ]` `CashFinancialsService.cs:54-56`: `LoadBankAccounts(cn, whitelist, ct)` filters only
when the whitelist is non-empty, so empty means *no filtering at all*.

`[RUN]` Commit `9221ba1d` (2026-07-26) fixed this and is **not an ancestor of the deployed build**.
Its message: *"CFGBanks holds 20 bank accounts; App.config whitelists 3 (1110.00, 1120.00, 1170.00).
MCP was including all 20 — pulling in petty cash 1000.00 and USD savings 1175.00, which App.config
records Daler as having explicitly excluded from operating cash. get_cash is what /ask answers Cash
Position from, so the AI and the dashboard disagreed."*

**Ask "what's our cash position?" in front of MVE and the AI will state a figure that does not match
the Financials screen sitting next to it.**

### 5.3 The deployed build is 34 days behind HEAD — three fixes and a feature stranded

`[QUERIED]` `/health` reports version `0.4.2+5b9535f7a1fb...`; `[RUN]` that commit is dated
**2026-07-17**. `[QUERIED]` every DLL in the install directory is dated 2026-07-17 18:55–18:59.
`[RUN]` `git log 5b9535f7..HEAD -- Kor.Operations.Mcp` returns four commits, none deployed:

| Commit | Date | What is stranded |
|---|---|---|
| `818ebc19` | 07-31 | WIP sign fix (§5.1) |
| `1171411b` | 07-26 | Partner billing rollup + **per-year FX** (2026 = 1.378457) |
| `9221ba1d` | 07-26 | Five missing config keys incl. cash whitelist (§5.2) |
| `7da81ba1` | 07-17 | CRM neural-gap G3–G6; `/ask` taught the new tables |

One correction to my own first pass, recorded because it changes the conclusion: an ASCII `grep` of
the deployed DLL reported `TODAY'S DATE`, `IntelPersonRelation`, `vw_OrgWarmth` and `OrgFact` as
ABSENT, which would have meant the date-grounding fix and the whole CRM schema section were missing
from the live prompt. That was **wrong** — .NET string literals are UTF-16 in the `#US` heap and
ASCII grep cannot see them. `[RUN]` A proper UTF-16 byte scan finds all four **PRESENT**. So
`7da81ba1`'s prompt content *is* in the deployed binary even though the stamped version predates it
(the build was made later the same day than the revision it stamped). Date-grounding is live; the
CRM tables are live. **The two financial defects in §5.1 and §5.2 are confirmed by the same method
and stand.**

### 5.4 The live Basic-auth password is committed to git in cleartext

`[RUN]` `Kor.Operations.App/App.config:148` contains `McpServer.Password` with a real 20-character
value. `[RUN]` `git ls-files --error-unmatch` confirms the file is **tracked**; `.gitignore`
excludes `appsettings.Development.json` and `appsettings.smoke.json` but not `App.config`.
`[RUN]` SHA-1 of the value in `git show HEAD:Kor.Operations.App/App.config` is `1d0c6db905…` —
**byte-identical** to `Mcp.Password` in the live `appsettings.Production.json` on KOR-APP01.

The brief told me a suite-wide scan found no hardcoded credentials in any **C#** source and that
`McpServer.Password` is externalised via `AppConfigKeys.cs`. That is accurate and also misleading:
the key *name* is externalised into a constant, but the *value* is checked into the repository in
XML. The same file also carries `Financials.*`, `WatchlistSync.Password`, `Vp.Password`, and two
live SQL connection strings with passwords (`opportunities_app`, `transmittals_app`) at lines
162–170. **Flag as STALE-FINDING against the brief's "ALREADY ESTABLISHED" paragraph.**

Good news, verified separately: `[RUN]` `git grep -lE "sk-ant-[A-Za-z0-9_-]{20,}"` over tracked
files returns **nothing**. The Anthropic API key exists only in
`appsettings.Production.json` on the server, exactly as the architecture intends.

### 5.5 Secrets on the server are plaintext and world-readable to any logged-on user

`[QUERIED]` `Get-Acl "\\kor-app01\c$\Program Files\KorOperations\Mcp\appsettings.Production.json"`:

```
NT AUTHORITY\SYSTEM        : FullControl
BUILTIN\Administrators     : FullControl
BUILTIN\Users              : ReadAndExecute, Synchronize   <-- 
kor\app-admin              : FullControl
```

That file holds, in cleartext: the Anthropic API key, the `mcp_app` SQL connection string with
password, the Deltek ODBC password for user `52267.nucleus.prd`, and the MCP Basic-auth password.
Any account that can log on to KOR-APP01 interactively or over RDP can read all four. No DPAPI, no
`dotnet user-secrets`, no ACL restriction. (Reaching it over `\\kor-app01\c$` requires admin — the
exposure is local-logon, not open-share.)

### 5.6 Auth: who can call this, plainly

**It is authenticated — but with one shared password over plaintext HTTP.** `[QUERIED]` verified
401 without credentials. `[READ]` `BasicAuthMiddleware.cs`:

- `/health` is deliberately unauthenticated (fine — it leaks only version and timestamp).
- **Every other endpoint** requires HTTP Basic with a single shared service account
  (`kor-operations-app`). There is one credential for the whole firm; there is no per-user
  authentication.
- Identity is **honour-system**: `BasicAuthMiddleware.cs:88-93` takes the caller's identity from an
  unverified `X-Kor-User-Upn` header. The code says so: *"Honour-system identity for v1; will be
  replaced by Windows Auth in a later phase."* Anyone holding the shared password can attribute
  their questions to anyone else in the audit log.
- `BasicAuthMiddleware.cs:78-79` uses `string.Equals` rather than a constant-time comparison. The
  comment argues LAN-only makes it acceptable; over a LAN it is a fair call.
- `[QUERIED]` The runbook confirms plain HTTP with no TLS *by design*
  (`Kor.Operations.Mcp.deploy.md`), so the shared password crosses the wire in base64 on every call
  and is recoverable by anyone sniffing the LAN.

**So, stated plainly as the brief asks:** the service is not open to anonymous callers, but the
single shared credential guarding read access to KOR's complete Deltek financials — P&L, cash, AR,
per-employee compensation-adjacent performance data — is committed to git (§5.4), readable by any
user logged on to APP01 (§5.5), and transmitted unencrypted. Effective access control is
"anyone with the repo or a login on APP01". That is a real finding, and it is not one MVE would
need to look hard to find if they asked how auth works.

### 5.7 The SQL login can write; the read-only guarantee is application-level only

`[QUERIED]` As `mcp_app` on `KOR-APP01\SQLEXPRESS`:

- `KorMcp` → **`db_datareader, db_datawriter`** (write is legitimately needed for `Mcp.AuditLog`).
- `KorOpportunitiesDb` → `db_datareader` only. Correct.
- `IS_SRVROLEMEMBER('sysadmin')` = 0, `CONTROL SERVER` = 0. Correct.

But `query_kor_data` runs on the *same* connection that has `db_datawriter` on `KorMcp`. The
read-only property rests entirely on the keyword gate in `QueryKorDataTool.cs:84-113` — prefix
check after comment-stripping, batch-statement detection, pass-through rejection
(OPENQUERY/OPENROWSET/OPENDATASOURCE), write-keyword scan. `[RUN]` 15 unit tests exercise that gate
and pass. It is a well-built gate; it is also the *only* gate. A second, cheap layer would be a
dedicated read-only login for the query tool, separate from the audit-writer login.

### 5.8 Smaller items

- `[READ]` `AskService.cs:112` — `AuditContext.ClientApp` is hardcoded `null` with the comment
  *"client-app header is not wired here yet"*, so `Mcp.AuditLog.ClientApp` is always null.
- `[QUERIED]` `TrustServerCertificate=True` on the SQL connection string — the certificate is not
  validated.
- `[READ]` `Program.cs:196-199` — the Anthropic `HttpClient` has a 2-minute timeout; the WPF client
  (`AppAiService.cs:31`) allows 4 minutes. Correct ordering (server gives up first).
- `[RUN]` `NU1902`: `AngleSharp 0.17.1` has a known moderate-severity advisory, pulled in
  transitively via `Kor.Opportunities.Data`.
- `[READ]` `AskService.cs:75-80` — conversation traces are in-process only. A service restart
  silently loses every in-flight conversation's context; a follow-up "and vs Q1?" after a restart
  answers against nothing.

---

## 6. Dependencies

| Dependency | Detail | Reachable off the KOR LAN? |
|---|---|---|
| **Anthropic API** | `https://api.anthropic.com/v1/messages`, header `anthropic-version: 2023-06-01`, key from `Mcp:AnthropicApiKey` on the server `[READ] AskService.cs:346-351` | Yes — outbound from KOR-APP01 only |
| **The MCP service itself** | `http://kor-app01:5500` — plain HTTP, LAN-only, no TLS, no reverse proxy `[QUERIED]` | **No.** VPN or on-site required |
| **SQL Server** | `KOR-APP01\SQLEXPRESS`, catalogs `KorMcp` (audit, alerts, COO card/brief, collections) and `KorOpportunitiesDb` (BD tools), login `mcp_app` `[QUERIED]` | No — LAN/VPN |
| **Deltek Vantagepoint (linked server)** | `[DELTEK_VP].[C0000052267P_1_KOR00000000].dbo.*`, four-part naming only; OPENQUERY rejected by the gate | Via SQL only |
| **Deltek Vantagepoint (ODBC)** | `[QUERIED]` System DSN `Deltek` on KOR-APP01 → DataDirect Hybrid Data Pipeline driver → `vp-ca-hdp01.prd.mydeltek.com:443`, cloud, TLS, cert validation on. Structured tools use this path directly | The DSN exists **only on KOR-APP01** |
| **Quartz.NET** | In-process scheduler, `0 0 6 ? * MON` Pacific `[READ] Program.cs:181-190` | n/a |
| **Serilog** | `Logs\mcp-yyyyMMdd.log` next to the binary `[QUERIED]` latest `mcp-20260817.log` | n/a |

**Remote-demo verdict:** if the demo is given at MVE's office, **VPN is mandatory** — there is no
TLS and no internet-facing endpoint. Nothing here works from a laptop without it.

### 6.1 Which LLM, which model, whose key, what a question costs

- **Provider: Anthropic, and only Anthropic.** `[RUN]` `grep -rilE 'openai|langchain_openai|
  google.generativeai|genai|mistralai|cohere|ollama'` over the module → **zero hits**. No fallback
  provider exists; if the Anthropic API is down, `/ask` returns
  `"AI provider returned HTTP {status}. Try again in a moment."`
- **Model: `claude-sonnet-4-6`.** `[QUERIED]` `Mcp:AnthropicModel` in the live
  `appsettings.Production.json`; `[READ]` same value as the `McpOptions.cs:26` default.
  **This model ID is current and not deprecated** — verified against the `claude-api` skill's model
  table (Claude Sonnet 4.6, 1M context, $3/MTok in, $15/MTok out). **No demo-killer here.**
- **Key ownership:** one KOR-held Anthropic key, on the server only, never on a workstation
  `[QUERIED]` — the key is *not* in the repo `[RUN]`. This is the right design and worth saying
  out loud to MVE.
- **A trap worth knowing before someone "upgrades" the model.** `AnthropicModel` is a config value,
  so it is a one-line edit to point at a newer model. `[READ]` `AskService.cs:321` sends
  `temperature = 0` on every request. Sampling parameters (`temperature`/`top_p`/`top_k`) are
  **rejected with HTTP 400** on Claude Sonnet 5, Opus 5, Opus 4.8/4.7 and Fable 5 — they only
  remain valid on the 4.6 family. Changing that one config key to any 5-series model would hard-break
  `/ask` with a 400 on every question. **Do not touch `AnthropicModel` before the demo.**

**Cost per question — ESTIMATED, not measured.** `AskResponse` returns `InputTokens`/`OutputTokens`
`[READ]`, but `[QUERIED]` `Mcp.AuditLog` has no token columns, so nothing is persisted and I cannot
give a measured figure. Arithmetic from measured sizes:

- `[RUN]` System prompt = **51,770 characters ≈ 14,400 tokens**. Tool descriptions ≈ 16,090
  characters ≈ **4,500 tokens**. Cached prefix ≈ **19k tokens**, marked `cache_control: ephemeral`.
- `[QUERIED]` Mean human question payload (question + `[CURRENTLY VIEWING]`) = **370 characters**;
  mean answer = **897 characters**.
- Sonnet 4.6 at $3 / $15 per MTok; cache write 1.25× = $3.75, cache read 0.1× = $0.30.
- Cold prefix (first question in a 5-minute window): 19k × $3.75/M ≈ **$0.07**. Warm: ≈ **$0.006**.
- A typical 3-turn loop re-sends the growing message array; variable input ≈ 20–60k tokens
  ≈ $0.06–$0.18. Output ≈ 1–2k tokens ≈ $0.02–$0.03.

**≈ $0.05–$0.25 per question**, worst case ≈ **$0.90** if a question runs to the 300k input-token
cap. At the observed rate (427 questions in ~4 months) this is **well under $100 total to date**.
To make this measured rather than estimated, add `InputTokens`/`OutputTokens` columns to
`Mcp.AuditLog` — the values are already on `AskResponse` and thrown away.

---

## 7. Test reality

**Unit tests — `[RUN]` `dotnet test Kor.Operations.Mcp.Tests -c Debug`:**

```
Passed!  - Failed: 0, Passed: 67, Skipped: 0, Total: 67, Duration: 1 s
```

67 green in one second. What those 67 cover `[RUN]` (counting `[Fact]`/`[Theory]`/`[InlineData]`):

| File | Cases | Covers |
|---|---|---|
| `Tools/QueryKorDataToolGateTests.cs` | 15 | The read-only SQL gate. Genuinely valuable |
| `Tools/ToolErrorEnvelopeTests.cs` | 10 | Error classification / recoverability |
| `Analytics/PerformanceScoringTests.cs` | 7 | Composite scoring maths |
| `Alerts/AlertRuleSmokeTests.cs` | 6 | Per-rule alert smoke |
| `Analytics/EmployeePerformanceServiceTests.cs` | 5 | |
| `Analytics/PmPerformanceServiceTests.cs` | 4 | |
| `Analytics/YearTrendServiceTests.cs` | 4 | |
| `Smoke/SmokeCoverageValidatorTests.cs` | 4 | The validator, against synthetic inputs |
| `Analytics/DmPerformanceServiceTests.cs` | 1 | |

**Blunt assessment.** `[RUN]` `grep -rln "AskService"` across the test project → **no file
references `AskService`**. The 1,143-line LLM loop — retry logic, token budget, circuit breakers,
conversation trace, eviction, tool dispatch — has **zero test coverage**. So do 20 of the 23 tools
(only the three analytics services are tested, and at their service layer, not through the tool).
The 67 tests are real and well-targeted at the SQL gate and the error envelope; they simply do not
touch the parts of this module that would embarrass anyone.

**The accuracy harness is dead. `[RUN]`:**

```
--- Kor.Operations.Mcp.Smoke ---
Config: \\KOR-APP01\C$\Program Files\KorOperations\Mcp\appsettings.Production.json
Endpoint: http://kor-app01:5500/ask
Smoke coverage validation failed: Smoke coverage gap: tools get_bd_action_status,
get_bd_call_sheet, get_bd_pursuit_list are registered but have no calibrator in
TestCases.cs. Add a calibrator or add to the exempt list.
```

`Kor.Operations.Mcp.Smoke` is the only thing that asks `/ask` real questions and asserts the
numbers in the prose match what the canonical services compute independently — 26 calibrators over
19 tools. `[RUN]` `Program.cs:39-56` runs `SmokeCoverageValidator` **before** any test case and
returns 1 on failure. The three BD tools were added 2026-06-09 (`df00ce3e`); no calibrators were
ever written for them.

**Therefore: since 2026-06-09, no end-to-end verification that `/ask` produces correct numbers has
run at all.** That is precisely the window in which the WIP and cash defects were introduced and
shipped. The harness that existed to catch exactly this class of bug was disabled by an unrelated
feature addition, silently, and nobody noticed because it fails closed and nothing schedules it.

**Test drift, quantified.** `[RUN]` `git log -1 -- Kor.Operations.Mcp.Tests Kor.Operations.Mcp.Smoke`
→ **2026-05-14**; latest service commit → **2026-07-31**. What has landed in those 2.5 months and is
untested: the entire BD pursuit tool family (3 tools + `IBdReportService` catalog swap in
`Program.cs:117-130`), the CRM neural-gap prompt sections (~1,500 lines of system prompt describing
7 `Intel*` tables, `OrgFact`, `CrmTouchpoint`, `vw_OrgWarmth`), date-grounding, the per-year FX
work, the five config keys, and the WIP sign convention (which *does* ship with 3 new hermetic
tests in `Kor.Operations.Business` — but that project was outside my targeted run).

**The command that would close the gap** (do **not** run it during the audit — it POSTs to `/ask`
and spends money, and it is currently blocked anyway):

```
dotnet run --project Kor.Operations.Mcp.Smoke -c Debug
```

after adding three calibrators, or after adding the three BD tool names to
`SmokeCoverageValidator.DefaultExemptToolNames`.

---

## 8. Demo risk — ranked

1. **`get_cash_position` and `get_wip` return wrong numbers from the live service.** `[RUN]` Both
   confirmed by byte-scan of the deployed DLLs. "What's our cash position?" and "where's our WIP?"
   are among the most natural questions a CFO-facing demo invites, and the AI's answer will differ
   from the Financials screen. This is exactly the failure mode the brief flags as worst-possible —
   and the cause is not hallucination, it is a **stale deployment**. That makes it entirely fixable
   in an afternoon, and it makes it inexcusable if it is not.
2. **The accuracy harness has not run since June.** `[RUN]` If MVE's technical lead asks "how do you
   know the numbers are right?", the honest answer today is "a harness exists and it has been
   broken for ten weeks." The good answer is available cheaply — see §9.
3. **Latency on screen.** `[QUERIED]` Mean 10.5 s, max 72.9 s for human questions; 26.7–109.4 s for
   the heavier generator prompts. A minute of spinner in a live demo reads as "hung". Rehearse with
   questions that hit one structured tool, not multi-period comparisons.
4. **`query_kor_data` timeouts are 100% of logged failures.** `[QUERIED]` 39 of 39 errors in 60 days.
   Any question that falls off the structured-tool catalog reaches for ad-hoc SQL against Deltek and
   has a real chance of hitting the 30 s cap. The user-facing message is good ("try narrowing the
   scope…") but it is visibly a failure.
5. **Auth will not survive a serious question.** `[QUERIED]`/`[RUN]` One shared password, in git,
   over plain HTTP, with client-asserted identity. A technical lead who asks "how is this secured?"
   gets an answer that undercuts the whole financial-data story. Have a two-sentence roadmap answer
   ready (Windows Auth + TLS + per-user identity) rather than improvising.
6. **The deployed build is 34 days behind HEAD.** `[RUN]` If anyone looks at `/health` and compares
   the version to the repo, the gap is visible. Also: `1171411b`'s per-year FX means USD-org numbers
   from the AI differ ~1.4% from a current WPF build.
7. **"Show me the SQL you ran" on a follow-up turn may fall flat.** `[READ]` `AskService.cs:255-262`
   documents this: prior turns are replayed as flat text, not `tool_use` blocks. The Finding-10
   trace mitigates it, but it is in-memory and lost on restart.
8. **`Vocabulary/` looks unfinished if anyone browses the repo.** `[RUN]` A 4,142-line file whose
   header instructs a review that never happened, unreferenced by any code.
9. **Conversation state is per-process.** `[READ]` A service restart mid-demo silently drops
   context; the next follow-up answers against nothing, with no error.

### 8.1 The hallucination question, answered directly

The brief asks explicitly whether this system can state a confidently wrong financial figure. **The
architecture is unusually well defended, and the answer is: not by inventing one — but yes, by
faithfully reporting a wrong one.**

**What stops invention** `[READ]`:

- **The model does not compute.** 22 of 23 tools wrap canonical `Kor.Operations.Business` services —
  the identical code the WPF screens call. The model chooses a tool and narrates its JSON.
- **`temperature = 0`** (`AskService.cs:321`) with a documented rationale.
- **The system prompt names three real incidents and forbids repeating them** (`AskService.cs`,
  HARD RULES section): the 2026-05-10 Net Multiplier invention (model derived NSR/DLC from scratch,
  got 0.12×), the 2026-05-12 Feb-2026 expense re-derivation ($380K vs canonical $260K, because a raw
  SUM included account 7976 employee income-tax withholding), and the 2026-05-13 empty-string-org
  bug (firmwide $752K narrated as CAD-only $362K). Each is paired with the fix.
- **Explicit anti-inference rules:** *"Never narrate per-account / per-period numbers that didn't
  come back in a tool response"*; *"For a comparison across MULTIPLE periods, call the tool MULTIPLE
  times… Do not infer an off-screen period's number from on-screen totals"*; *"If a query result is
  unexpected (zero rows, very old date, suspiciously round number), call it out rather than
  presenting it as final"*; *"If the question can't be answered from the data available, say so
  plainly. Don't invent numbers."*
- **A startup parity gate.** `PromptToolParityValidator` refuses to boot if the prompt and the tool
  registry disagree in either direction. The service is up, so parity currently holds.
- **Structured refusal on every failure path.** `ToolErrorEnvelope` gives the model
  `errorClass` + `recoverable`, so it can distinguish "rewrite your SQL" from "the DSN is broken".
  Both circuit breakers exit with a specific, honest, plain-English message rather than a guess.

**How each demo failure mode actually behaves** `[READ]`:

| Failure mode | Behaviour |
|---|---|
| Ambiguous question | Model asks or picks a tool; `[CURRENTLY VIEWING]` supplies org/period scope |
| Period with no data | Prompt requires calling out zero rows rather than presenting as final |
| Question the tools cannot answer | Falls back to `query_kor_data`; if that fails twice, hard-stop with *"Try narrowing the scope…"*. For conceptual questions (satisfaction, loyalty) the prompt supplies a **proxy menu** and requires labelling proxies as proxies up front |
| LLM timeout / rate limit | 429 → up to 3 retries honouring `Retry-After`, abort if > 35 s, message *"The AI service is busy (firm-wide rate limit). Wait 30 seconds… your question wasn't lost."* Other HTTP → *"AI provider returned HTTP N."* |
| DB infrastructure failure | 2 consecutive all-fail iterations → *"…the MCP service's data wiring needs fixing. This isn't a question you can rephrase."* |
| Runaway loop | 300k input-token cap and 16-iteration cap, both with honest partial-answer messages |
| Two questions at once | Per-UPN semaphore → *"Your previous question is still running."* |

**The residual risk is not the LLM — it is the pipe.** Every guard above assumes the tool's number is
canonical. When the tool itself is wrong, the model reports it with full confidence and every
anti-hallucination rule working exactly as designed. That is the situation today for `get_wip` and
`get_cash_position` (§5.1, §5.2), and to a lesser degree for USD-org figures from `get_billed_pnl`
(§5.3). **Redeploying the service converts this module from "can state a wrong number" to
"structurally very hard to state a wrong number."** That single action is worth more than any other
item in this report.

### 8.2 Duplication with the second AI service (noted, not audited)

Per the brief I did not audit `Kor.Operations.App/PMTools/AnalyticsAiService.cs`. What I observed
from this side:

- Inside `Kor.Operations.Mcp` there is **no duplication**: `CooCardGenerator` and
  `CooBriefGenerator` both take `AskService` by constructor injection and call `AskAsync`
  `[READ]` — one LLM loop, one system prompt, one key.
- `Kor.Operations.App/Services/AppAiService.cs` is a thin HTTP client over `/ask` for the panel path
  and holds no LLM logic — but its constructor still takes an `apiKey`, and `IsConfigured` returns
  true if *either* the MCP config *or* a bare API key is present `[READ] AppAiService.cs:39-41`.
  That is a live second path (`AskWithToolsAsync`, used by PdfToSafe) that could bypass the
  server-side key custody the architecture is built on.
- `docs/architecture/Kor.Operations.Ai.consolidation-roadmap.md` (2026-05-13) is the plan to fold
  `AnalyticsAiService` into this module. **`[READ]` Its own status line reads "Not yet executed"**,
  and nothing in the 2026-05-14→07-31 commit range executes it. It remains an accurate plan and an
  unstarted one.

### 8.3 Stale documents (flag to orchestrator)

| Document | Doc date | Code date | Problem |
|---|---|---|---|
| `docs/architecture/Kor.Operations.Mcp.md` | 2026-05-10 | 2026-07-31 | Says *"Phase 11a built… Phase 11b in progress"* and describes a near-single-tool service. There are now 23 tools, a Quartz job trio, alerts, COO card/brief and collections endpoints |
| `docs/architecture/Kor.Operations.Ai.consolidation-roadmap.md` | 2026-05-13 | — | *"Not yet executed"* — still true, so not wrong, but do not read it as current state |
| `docs/runbooks/Kor.Operations.Mcp.deploy.md` | 2026-05-09 | 2026-07-31 | The mechanics still hold, but it predates the config keys added in `9221ba1d` and does not mention that a redeploy must also refresh `Kor.Operations.Business.dll` |
| `Vocabulary/candidates.md` | 2026-05-08 | — | Instructs a KEEP/DROP/REWRITE review that never happened; unreferenced by code |
| Task brief "ALREADY ESTABLISHED" | 2026-08-20 | — | *"config keys including `McpServer.Password` are externalised"* — the key **name** is; the **value** is committed in `App.config` (§5.4) |

---

## 9. To-do register

| # | Item | Size | Tag | Why it matters |
|---|---|---|---|---|
| 1 | **Redeploy the service from HEAD** (rebuild + robocopy per `Kor.Operations.Mcp.deploy.md`, from KOR-1001, Ian runs it). Must include `Kor.Operations.Business.dll`, not just `Kor.Operations.Mcp.dll` | S | **BEFORE-DEMO** | Single action that fixes `get_wip` (§5.1), `get_cash_position` (§5.2) and per-year FX (§5.3). Converts the module's worst risk into a non-issue |
| 2 | Verify the redeploy landed: `GET /health` shows a HEAD-derived version, **and** re-run the UTF-8 DLL scan for `SplitWipNet` and the UTF-16 scan for `CashAccountWhitelist` | S | **BEFORE-DEMO** | CLAUDE.md rule 5 — verify the artifact. A `/health` version string alone would not have caught the stamped-vs-content mismatch in §5.3 |
| 3 | Unblock the smoke harness: add 3 BD calibrators, or add the 3 names to `DefaultExemptToolNames`; then run it once against the redeployed service and record the pass rate | M | **BEFORE-DEMO** | The only evidence that `/ask` numbers are right. Also the answer to MVE's inevitable "how do you know?" |
| 4 | Add a smoke calibrator for `get_wip` and `get_cash_position` specifically | S | **BEFORE-DEMO** | The two tools that just shipped wrong. A ratchet, not a threshold (per `feedback_checks_go_in_the_build_not_my_head`) |
| 5 | Rehearse the demo script against the redeployed service; time each question; drop anything over ~20 s | S | **BEFORE-DEMO** | §8 risk 3. Mean is 10.5 s but the tail reaches 73 s |
| 6 | Prepare the two-sentence auth answer (shared credential today; Windows Auth + TLS + per-user identity next) | S | **BEFORE-DEMO** | §8 risk 5. Improvising this in the room is worse than owning it |
| 7 | Do **not** change `Mcp:AnthropicModel` before the demo; add a comment in `appsettings.Production.json` saying why | S | **BEFORE-DEMO** | §6.1 — any 5-series model 400s on `temperature = 0`. A well-meant "let's use the newest model" breaks every question |
| 8 | Rotate the MCP Basic-auth password; move it out of `App.config` into a per-machine store; add `App.config` to `.gitignore` with a `.template` committed instead | M | SOON | §5.4. Rotation alone is insufficient while the file stays tracked. Ian's call and Ian's hands — infra change, warn first |
| 9 | Tighten the ACL on `appsettings.Production.json` — remove `BUILTIN\Users` | S | SOON | §5.5. One `icacls` change; brief Ian on the side effect (any non-admin service tooling reading that file loses access) |
| 10 | Rotate the Deltek ODBC and `opportunities_app` / `transmittals_app` credentials also exposed in `App.config` | M | SOON | §5.4 — same blast radius, wider than MCP |
| 11 | Give `query_kor_data` its own read-only SQL login, separate from the audit-writer | M | SOON | §5.7 — makes read-only a database guarantee, not just an application one |
| 12 | Persist `InputTokens`/`OutputTokens` to `Mcp.AuditLog` | S | SOON | §6.1 — turns the cost estimate into a measurement; values already exist and are discarded |
| 13 | Reconcile the system prompt's *"Revenue Generation is OFF at KOR"* line with `818ebc19`'s finding that the RG path is what runs | S | SOON | §5.1 — after the redeploy the tool is right but the prompt still teaches the old claim |
| 14 | Write tests for `AskService`: token budget, circuit breakers, trace eviction, unknown-tool dispatch | L | SOON | §7 — 1,143 lines, zero coverage, and it is the centrepiece |
| 15 | Wire `AuditContext.ClientApp` (header already exists client-side) | S | LATER | §5.8 — column is always null |
| 16 | Delete `Vocabulary/` | S | LATER | §4.4 — abandoned scratch artifact that reads as unfinished work |
| 17 | Rewrite `docs/architecture/Kor.Operations.Mcp.md` to describe the 23-tool service that exists | M | LATER | §8.3 — 2.5 months stale, describes Phase 11b as in progress |
| 18 | Persist conversation traces (or accept and document the restart behaviour) | M | LATER | §5.8 — silent context loss on restart |
| 19 | Upgrade `AngleSharp` past 0.17.1 | S | LATER | `NU1902` moderate advisory, transitive via `Kor.Opportunities.Data` |
| 20 | Execute the AI consolidation roadmap (fold `AnalyticsAiService` into MCP tools) | L | LATER | §8.2 — the plan is sound and unstarted; not a two-week item |

---

## 10. Verdict

**Demo-able with care — and demo-ready after one afternoon's work.** This is the strongest-built
module I have looked at on structural grounds: 7,897 lines with **zero** TODO/FIXME/HACK/
`NotImplementedException` and **zero** empty catch blocks `[RUN]`; a startup gate that refuses to
boot on prompt/tool drift; a layered read-only SQL gate with 15 passing tests; a system prompt that
names three real past hallucinations and forbids each; per-user concurrency limits, token budgets,
429 handling and two circuit breakers, every one of which fails with an honest plain-English
message. The model is `claude-sonnet-4-6`, current and not deprecated. The service is live, has
answered 427 questions, and its weekly COO Card / Brief / Alert pipeline ran successfully four days
ago. The architecture — LLM chooses, canonical C# computes — is the right one and it is genuinely
implemented, not aspirational.

**The single most important thing to fix: redeploy the service from HEAD.** The deployed binaries
are 34 days stale, and that staleness is currently causing `get_wip` to report earned and overbilled
transposed (a $209,298 net sign flip at period 202602, confirmed in the fix commit's own message and
by byte-scanning the deployed DLL) and `get_cash_position` to sum all 20 bank accounts instead of
the 3 Daler whitelisted. Both are precisely the "confidently wrong financial number in front of MVE"
outcome the brief names as worst-possible — and the sharp irony is that they are **not**
hallucinations. Every anti-hallucination guard is working perfectly; it is faithfully reporting a
number that a fixed-but-undeployed tool computes wrongly. A build and a robocopy resolves all three
financial defects at once.

Two things should be squared away alongside it. The end-to-end accuracy harness — the only thing
that checks `/ask`'s numbers against independently computed canonical values — has been dead since
2026-06-09, failing closed at a coverage gate because three BD tools were added without
calibrators `[RUN]`. That is the mechanism that should have caught both defects, and unblocking it
is a half-day. And the shared Basic-auth password guarding read access to KOR's complete Deltek
financials is committed to git in cleartext, byte-identical to the live server value `[RUN]`,
readable by any user logged on to APP01, and sent unencrypted over LAN HTTP. It does not break the
demo, but it will not survive a serious question about it, so own it with a roadmap answer rather
than improvising.

Keep WIP and cash questions off the screen until item 1 ships. After it ships, this module is the
strongest thing KOR can put in front of MVE.
