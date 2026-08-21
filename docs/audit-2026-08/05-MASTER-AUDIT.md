# 05 — Master Audit: the KOR suite, 2026-08-20

**Scope:** the Operations Brain and the engineering tooling that feeds it — ~351,000 lines of C#
across four repositories, built in eight months, largely by one person.
**Built from:** eleven module audits, `00-INVENTORY.md`, `01-DOC-TRUST.md`, `02-CROSS-CUTTING-SCAN.md`,
`03-COMPETITIVE-BATTLECARD.md`. Nothing here is new investigation; every claim carries the evidence
tier the module report gave it. **Tiers are never promoted.**

`[RUN]` executed and observed · `[QUERIED]` live state read · `[READ]` source read and reasoned
about · `[DOC]` a document asserts it — lowest trust.

**Demo to MVE in under two weeks.** The action list is `04-TODO-REGISTER.md`.

---

## 1. What this suite actually is

The story told about this suite is three layers. The story the evidence tells is three layers built
at three different levels of finish, plus a fourth that nobody planned.

**Layer 1 — the Newforma replacement. This is the most finished thing here and it is genuinely
finished.** An Outlook VSTO add-in files project mail to `\\Kor-fs01\Projects\Projects\…\Newforma\email\`,
hashes it, parses it and indexes it into `KorEmailIndex`. The index holds **372,370 emails across 955
projects, from 2014 to a message that landed at 23:48 on the night of the audit, 183,745 of them with
attachments, in a 21 GB database whose full-text catalog is fully populated and current** `[QUERIED]`.
Two writers are live; three different staff appear in today's shared filing log `[QUERIED]`. Alongside
it, the transmittals half has been in **continuous production with real clients for nine months**: 829
transmittals, 4,284 per-recipient tracking links to 741 distinct external addresses at Arcadis,
Greystar, Wesbild, Anthem and JWDA, and 2,682 click events with **zero** missing IP, user-agent or
recipient email `[QUERIED]`. That is the competitive claim, and it is real.

But this layer is also where the seams are widest. The tracking server that logs all of it —
`Kor.Transmittals.Redirector`, public at `tracking.korstructural.com` — **is not in any git repository
and has not compiled from its own source since 2026-03-17** `[RUN]`, when a refactor in a repo it
consumes by `bin\Release` HintPath removed `GraphFacade.Instance`. The running binary is fine; it just
cannot be rebuilt. And the layer is narrower than "Newforma replacement" implies: there is **no RFI and
no submittal record type anywhere in the suite** `[READ]` — the `Type` column has exactly three values,
Transmittal / Transfer / Upload. Transmittal "numbering" is `{project}-{yyyyMMdd-HHmmss}`, a UTC
timestamp, not a sequence a client can cite `[READ: GraphFacade.cs:352]`.

**Layer 2 — Deltek reporting and the virtual CFO. The engineering is the best in the suite; the data
underneath it is six months old.** The Financials window paints in about 1.5 seconds against a live
ODBC connection `[RUN]`, 176 hermetic tests pass in two seconds `[RUN]`, the schema-drift validator
comes back **clean across all 34 expected columns** against 676 live ones `[RUN]`, and there are zero
`TODO`/`FIXME`/`NotImplementedException` in 26,500 lines. On top of it sits `Kor.Operations.Mcp`: a
service on KOR-APP01 that turns plain-English questions into 23 read-only tools, each wrapping the
*same* canonical C# service the WPF screens call. The LLM never computes a number — it chooses a tool
and narrates its JSON. That architecture is right and it is genuinely implemented, not aspirational.

Two facts spoil it. `MAX(Period)` in both `PRSummaryMain` and `GLSummary` is **202602 — February
2026** `[QUERIED]`, with zero rows for 202603–202608, while `AR` carries invoices to today and
`tkDetail` to 2026-08-23. So WIP, Cash, Backlog, the GL P&L, "Net Income (T12mo)", the revenue trends,
the Clients tab and the Overview grid's billed-to-date are all half a year stale on a screen KOR
intends to call real-time. And the MCP service's deployed binaries are **34 days behind HEAD**
`[RUN]`, which is currently causing `get_wip` to report earned and overbilled transposed and
`get_cash_position` to sum all 20 bank accounts instead of the 3 whitelisted `[RUN: byte-scan of the
deployed DLLs]`.

**Layer 3 — the BD Brain. The machinery is real and running; the last two steps of the loop are not.**
111 public procurement sources polled continuously, the Worker heartbeating during the audit, 139,472
contract awards, 50,811 building permits, 10,286 major-project records, and an entity graph of 9,641
live canonical organisations with **9,641 distinct normalised names and zero duplicate groups**
`[QUERIED]`. It audits its own data health weekly and classifies each source `DEAD-GREEN` /
`NEVER-PRODUCED` — an unusually mature control most teams never build. But the AI research executors
have returned `Success=1; considered=0; executed=0` **every day since at least 2026-08-17** `[QUERIED]`,
their feeder job stuck at `NextFireAtUtc = 2026-07-19` and never once fired; `DeltekClientId` is NULL
on **all 2,599** opportunities, so KOR's single most differentiating scoring signal contributes nothing
`[QUERIED]`; and the human end of the loop is not used — `OwnerStaffId` is set on **1** opportunity, and
the BD workspace was last opened **2026-07-13** `[QUERIED]`.

**Layer 4 — the engineering tools, which nobody planned as a layer and which contain the rarest thing
in the suite.** PDF→SAFE, structural quantity takeoff, rebar change detection, a vector takeoff engine,
DXF→ETABS, and 137 Revit tools on one ribbon. DXF→ETABS is the standout: a full 63-storey run
reproduces its recorded baseline **exactly, in 50.7 seconds**, and a PowerShell renderer draws the
result in **9.2 seconds** without ETABS `[RUN]`. All 35 thresholds it applies are database rows in
`KorStandards`, not constants in C#. The vector takeoff engine prices a whole 40-storey building for
**$0 with zero AI calls** and produces byte-identical totals from a 40-page range and the full 73-page
set `[RUN]`. Neither has a button in the app.

**The honest one-sentence version:** this is a well-built application layer over an ageing ledger and
an under-used pipeline, with two genuinely rare engineering capabilities stranded outside the product,
and a deployment story that cannot prove what is running.

---

## 2. Module scorecard

Completeness counts the WORKING rows in each module's own §4 table. The tables are not commensurate
with each other — read the count as a shape, not a score.

| # | Module | What it does | Verdict | Complete | Single biggest issue |
|---|---|---|---|---|---|
| 01 | Email filing & search | Files Outlook mail to project folders; searches it | **demo-ready** | 11 / 16 | `4501-01-01` prefix on **39%** of filenames filed in the last 30 days `[QUERIED]` |
| 02 | Transmittals & tracking | Sends tracked transmittals; logs per-recipient opens/clicks | demo-able with care | 10 / 18 | Redirector is untracked **and** uncompilable since 2026-03-17 `[RUN]` |
| 03 | FileSync | Unattended robot moving documents share↔SharePoint↔Deltek | demo-able with care | 11 / 15 | `KorMapSync` cron never registered with Quartz; UI shows a countdown to a fire that cannot happen `[QUERIED]` |
| 04 | Financials / Deltek | Live WIP, AR, cash, backlog, P&L, utilization | demo-able with care | 7 / 17 | Summary ledgers stop at **Feb 2026** on a screen described as real-time `[QUERIED]` |
| 05 | AI virtual CFO | Plain-English questions answered from Deltek by tool-using Claude | demo-able with care *(demo-ready after one redeploy)* | 19 / 23 tools | Deployed build 34 days stale → `get_wip` and `get_cash_position` answer **wrong** `[RUN]` |
| 06 | PM Tools / analytics | Portfolio and staff analytics over all Deltek history | demo-able with care | 9 / 11 | Employee Summary: 56 named staff, letter grades, **five current Fs**, sortable `[QUERIED]` |
| 07 | BD Brain core | Ingests 111 procurement sources; resolves, enriches, scores | demo-able with care | 6 / 14 | AI research layer dead since 2026-06-27 while reporting success daily `[QUERIED]` |
| 08 | BD desktop surface | Dossiers, reports, pursuits over the BD data | demo-able with care **— navigation-gated** | 13 / 21 | Default BD screen renders **25 named architecture firms + KOR's displacement plan, at load, no click** `[QUERIED]` |
| 09 | Engineering tools | PDF→SAFE, takeoff, rebar change, vector takeoff | demo-able with care | 11 / 17 | The differentiator (vector takeoff) has **no button** — CLI only `[RUN]` |
| 10 | DXF → ETABS | Plan DXFs → a geometry-complete ETABS model | demo-able with care | 9 / 15 | No offline mode: `RequireRuleSettings=true` means every run needs SQL `[READ]` |
| 11 | Revit tools / Drafter | 137-tool Revit ribbon; agent bridge; standards rules DB | RevitTools **demo-ready**; Drafter **keep off screen** | 6 / 10 | Revit→DXF→ETABS layer contract does not line up: exports `A-WALL`/`S-COLS`/`A-FLOR`, rules expect `WALL`/`_COL`/`SLABEDG` `[RUN]` |

**Suite total: ~112 of 177 capability rows WORKING (~63%).** Six modules are demo-able with care, two
are demo-ready, one component (KOR.Drafter) should stay off screen entirely.

**Four scorecard corrections carried in from module 08**, which re-measured its own brief `[RUN]`:
the deferred register is **D1–D12; D13 does not exist**; "BD Reports A–C are done" is a category
error — **11 analytical + 11 sector reports ship and none is a stub**, A/B/C was phase vocabulary; the
in-app visuals recorded as "PENDING COMMIT" are **shipped and unit-tested**; and the BD surface is
**13,139 / 14,127 / 6,625 LOC (~34k)**, not the ~25k in circulation.

---

## 3. The five cross-cutting themes

Derived from the eleven module reports, not from a template. Each is stated with the number of
independent instances behind it.

### T1 — The system reports success it has not earned. *(16 instances, 8 modules)*

This is the most consistent pattern in the audit and the one with the highest chance of appearing on
screen. In sixteen separate places, a failure is rendered as a success, or a healthy component is
rendered as broken, and in every case the code is doing exactly what it was written to do.

- `AppAiService.AskAsync` **never throws** — it returns `"Unable to reach AI service: …"` *as the
  answer*. The caller checks only for whitespace and paints it into the Approach card under a green
  **"Drafted 14:32 from live intel"** `[READ: AppAiService.cs:72,135,149; PursuitBriefWindow.Approach.cs:67]`.
- The FileSync Command Center shows *"Sync project map — Daily at 3:00 AM — next fire in 4 h"* for a
  job **Quartz was never told about**. `NextFireAt` is computed client-side from the DB cron; the UI
  has no idea what was actually registered `[QUERIED + READ: FileSyncRows.cs:83-113]`.
- The same panel reports `GlobalMode = Shadow` while **all seven jobs are `Live`** and moving client
  files `[QUERIED]`.
- Its log viewer renders a **blank grid for the current day** on a healthy service: `FileInfo.Length`
  reads 0 over SMB while `Stream.Length` on the same file at the same instant reads 43,165 `[RUN]`.
- A failed FileSync run stores the run **summary** in `ErrorMessage`, so the Error column reads
  `"Synced bucket=Stickfile … failed=1"` — a sentence that looks like success `[READ: JobDispatcher.cs:115]`.
- `WipFinancialsService` catches both loader failures, substitutes zeros, and returns
  `DataLoaded: true` **unconditionally**; `BacklogService` does the same across four loaders. A
  transient Deltek blip renders a confident **$0** `[READ: :117-118, :131]`.
- The MCP smoke harness — the only thing that checks `/ask`'s numbers against independently computed
  values — has been **dead since 2026-06-09**, failing closed at a coverage gate, while `/health`
  returned 200 throughout `[RUN]`.
- The DXF publish one-pager gate parses the **source HTML** while the file it ships is an Edge
  *"File not found"* page. The gate that exists to catch this looks at the wrong artifact `[RUN]`.
- DXF Core tests that touch a real building return `null` and **pass** when the share is unreachable.
  `Skipped: 0` is misleading; off-LAN the suite is green while proving nothing `[RUN]`.
- The three BD AI research executors return `Success=1` with `considered=0; executed=0` **every day
  for two months** `[QUERIED]`.
- `BdDeltekLinkDryRunJob` succeeds nightly with identical output and is **permanently** a dry run
  `[QUERIED]`.
- An inbound file drop can succeed with **no database record at all** and nobody is told
  `[READ: InboundUploadService.cs:127,158]`.
- A Revit CSV import that drops a whole element or row increments **no counter**, so a partial import
  reports as clean `[READ: CsvImportCommand.cs:231,234]`.
- A sheet that silently did not receive its revision, and a rebar element where one parameter write
  failed, both report as updated `[READ: RevisionCommands.cs:121; RebarCommands.cs:57]`.
- A Deltek failure in the Pursuit Brief nulls the fields, so the UI shows *"No prior KOR work on record
  with this owner."* — **indistinguishable from a failed query** `[READ: PursuitBriefViewModel.cs:244-248]`.
- 19 of 23 catches in the Outlook add-in swallow silently by design ("never block Outlook startup"),
  which is why filing failures are invisible until someone reads the share log `[READ]`.

**Why it matters in two weeks:** four of these are visible on screen during the most likely
spontaneous demo actions — "show me the logs", "ask the AI", "what runs at 3 a.m.", "is that job
healthy". None of them crashes. All of them lie.

### T2 — Deployed artifacts cannot be traced back to source. *(6 modules)*

There is no case in this suite where a running binary can be proved to correspond to a commit.

- **MCP.** `/health` reports `0.4.2+5b9535f7` (2026-07-17) but a UTF-16 scan of the deployed DLL finds
  prompt content from a **later** commit present — the stamped version is not the content `[RUN]`.
  Three fixes are stranded, two of which are producing wrong financial numbers today.
- **FileSync.** The exe stamps `1.0.12+3b5150eb`, but `git ls-tree -d 3b5150eb -- Jobs/` shows **no
  KorMapSync directory**, while the running service executed KorMapSync successfully and reports
  `JobsRegistered = 7`. It was published from a dirty tree `[RUN + QUERIED]`.
- **Redirector.** Not in git anywhere. The deployed DLL hash-matches one developer's local publish
  folder exactly, and the source has not compiled since 2026-03-17 `[RUN]`.
- **BD Worker.** Deployed 2026-07-18; `Kor.Opportunities.Data` has commits through 2026-08-02
  `[QUERIED]`.
- **KOR.Drafter bridge.** The `artifacts/<year>/` DLLs the deployment docs tell you to copy **do not
  contain `exportdxf`** — only a hand-placed DLL on one workstation does, and that build predates the
  dialog fix committed the same evening `[RUN: byte-scan of four DLLs]`.
- **DXF→ETABS.** The artifacts sitting in two live job folders are five commits behind; 31138's
  questionnaire is missing a whole sheet and 31168 has no summary PDF at all `[QUERIED + RUN]`.

`publish.ps1` ends by telling you to run `deploy.ps1`, which does not exist `[RUN]`, and there is no
FileSync runbook — the deploy exists only in the owner's head. **Nothing in the pipeline refuses to
publish from a dirty tree, and nothing records what went where.**

### T3 — One computation, two wirings — and there is no arbiter. *(17 instances)*

The suite is built on a good principle: one canonical service, many callers. The principle is
undermined everywhere by the callers wiring the same service differently, and by nothing checking that
they agree.

- `Mcp/Program.cs:83` constructs `ProjectAnalyticsService` **without** the peer estimator that
  `HistoricalAnalyticsService.cs:28` passes. `EstEngBudget` drives `isHealthy`, which is 30% of every
  employee's productivity score and the whole at-risk watchlist. **The AI panel and the grid beside it
  can disagree about the same people and the same projects** `[READ]`.
- `Mcp:EmployeeSummaryExcludedIds` is **empty on the live server**; `App.config:149` excludes two
  people. Because `EfficiencyScore` is a percentile rank, adding them shifts **everyone's** grade
  `[QUERIED]`.
- Deployed MCP `"BilledDefaultOrg": ""` vs App.config `CAD` — so `/ask` includes FX-converted USA rows
  the P&L tab on screen excludes. App.config's own comment sizes the gap at **+$77,620** for one month
  `[QUERIED]`.
- **Two FX regimes in one window**: Partner Financials converts USA work at **1.378457**, everything
  else at **1.36**. A live WIP run at 1.36 produced −$173,813.10 where the fix commit quotes
  −$209,298.04 at 1.378457 — same code, **$35k apart** `[RUN]`. The same split recurs between CRM and
  Financials `[READ]`.
- The CRM win rate **contradicts the app's own metric dictionary**. `Definitions.Bd.cs:46-47` states in
  bold that a loss counts *only* where `WonLostOutcome = Lost`, and that including No-bid or Withdrawn
  *"drastically understates the real win rate."* `CrmAnalyticsService.cs:76` ignores `WonLostOutcome`
  entirely. The module **collects** the distinction and then folds all three into `Lost = 7`. **BD
  Scorecard displays a win rate the app itself says is wrong and low** `[READ]`.
- **"Client Lifetime Fee" computes three ways** — `Σ PR.Fee` excluding hourly (CRM), `Σ (PR.Fee +
  HourlyRevenue)` (Financials Clients tab), `Σ PRSummaryMain.BilledFee` (the tooltip explaining it) —
  two of them on screen `[READ]`.
- **Two win ledgers one tab apart**: `KorPursuits` vs `CrmEngagements`. The app's own AI prompt names
  the hazard verbatim — *"NEVER add CRM win counts to Deltek/KorPursuits win counts"* — and one client
  can read `Won 5 / Lost 0` on one tab and `0W / 0L` one click away `[READ: AskService.cs:1029]`.
- The backfill marker is matched **untrimmed** in C# and **trimmed** in T-SQL, so
  `" Deltek.CustomProposal"` counts as live in one and backfill in the other `[READ]`.
- **"Utilization" has three definitions in one application** — `twelveWkAvg / 37.5`,
  `BillableHours / TotalHours`, and `TotalBillableHrs / TotalAllHrs` over the *filtered* project set —
  under one word on two screens `[READ]`.
- The Fee/Hr tooltip says *"Compare to the $185/hr portfolio median"* while the KPI strip on the same
  screen shows a median around **$380** `[RUN]`.
- The GL "Net Income (T12mo)" tile and the GL P&L tab pick their source table by **two different
  scorers** and apply different Org scopes, under a caption saying *"same source as the GL P&L tab"*
  `[READ]`.
- Peer-median budgeting was retired as the default on **2026-06-28** for producing *"garbage-low
  budgets and false Critical flags."* The retirement landed in `FinancialsService` and **never** in
  `ProjectAnalyticsService` `[READ]`.
- **Two independent `.e2k` writers** in the same solution — `EtabsE2kExporter.cs` (351 L) and
  `Core/Dxf/E2kDocument.cs` (681 L) — sharing no code, no model and no tests `[READ]`.
- **Two email parsers**: the tested one has **zero production consumers**; the one that parses every
  email the firm files has **no tests**. The two filing paths disagree about the sender (**6.5% of
  recent rows carry an Exchange X.500 DN**) and one of them hardcodes `MessageId = null` for all 8,378
  rows it wrote `[QUERIED]`.
- **Two AI postures on the same boundary**: the takeoff engine bans AI from the measurement path and
  flags the three places it influences a priced quantity; PdfToSafe lets an LLM set a slab thickness
  and export the model, with no orange-flag equivalent `[READ]`.
- **Two standards registers with colliding IDs**: `E1` means one thing in `RULINGS.md`, another in the
  markup lexicon, a third as a sheet-size token `[READ]`.

**The sharpest form of this theme:** three code comments asserting parity that does not hold sit
**inside the MCP system prompt** — `AskService.cs:1019`, `EmployeePerformanceTool.cs:15,44` — so the
model reads them and states the parity to the user with confidence `[READ]`.

### T4 — Verification points away from the risk, and fails closed when it fails. *(11 modules)*

Test coverage is not theatre by padding; it is theatre by aim. Every module tests the half that is
easy to test, and the gates that were built to catch the hard half are miswired, unscheduled, or
checking the wrong artifact.

**Coverage inverted against risk:**

| Component | What it does | Tests |
|---|---|---|
| `Kor.Operations.FileSync.Service` | Moves and **deletes** client files on SharePoint; sends firm email | **0** — no test project exists `[RUN]` |
| `Kor.Transmittals.Redirector` | Internet-facing; accepts unauthenticated uploads and writes | **0**, and it cannot be referenced because it does not compile `[RUN]` |
| `KOR.Drafter.Bridge` | 3,489 LOC editing live structural models unattended | **0** `[RUN]` |
| KOR.RevitTools ribbon | 137 commands, several destructive | **0** — all 79 tests are on `Core`, which references no Revit API `[RUN]` |
| `AskService` | The 1,143-line LLM loop: retries, budgets, circuit breakers | **0** — no test file references it `[RUN]` |
| BD desktop (`Crm`/`Opportunities`/`Workspace`) | ~34,000 LOC of UI incl. every contradiction in T3 | **0** dedicated files `[RUN]` |
| `EmployeeScoreSnapshotStore` | Writes personnel scores to production SQL from a fire-and-forget task | **0** `[RUN]` |
| Email module | The parser every filed email goes through | **0**; the 3 tests that exist cover a class with **no production consumers**, and one is red `[RUN]` |

Against that: Financials has **176 hermetic tests passing in 2 seconds** `[RUN]` — all C# arithmetic,
with the SQL half naked and **nothing anywhere asserting data freshness**, which is exactly how the
six-month gap went unnoticed. BD Brain has 96 tests in 1.0 s — **1,519 test LOC against 48,832 source
LOC (3.1%)**, with the Playwright scrapers, the most fragile code in the system, tested only against
fixture HTML `[RUN]`.

**Gates that cannot fail on the defect they exist for:**

- `DxfToEtabsService.cs:350` passes `builtIn.Keys` — the **32 numeric** rule keys — where
  `RequiredRuleKeys` holds **35**. The three layer-pattern keys that decide *what counts as a wall*
  fall through to C# defaults. The "a missing rule stops the run" guarantee does not cover them, and
  the test suite checks a different key list from the one production enforces `[READ]`.
- The DXF publish count-gate **silently no-ops** if winget Poppler is absent — no message `[READ]`.
- `LiveProjectBaselineTests` tolerance is **±10%**: on 31168 that is ±112 walls and ±246 columns. A
  regression that loses 100 walls is green `[READ]`.
- `PortfolioRuleTests` is gated on `KOR_PORTFOLIO_CHECK`, a variable set by **nothing in the repo**
  `[RUN]`.
- `OrphanedPublicTypeTests` counts a lone DI registration as a consumer, which is why **~1,409 LOC of
  four dead windows pass the gate** `[RUN]`.
- The cross-cutting scan's own empty-catch regex undercounts by roughly **4×** (19 vs 83 minimum in
  KOR.RevitTools), and its secrets scan looked at `.cs` files only — the secrets are in `.config`,
  `.json`, `.ps1` and one `.md` `[RUN]`.
- The DXF Core suite is **RED at HEAD** — 3 failures, all tests left stale by `72c1a2ca` `[RUN]`. The
  BD app-surface detectors are **red on 2 of 20** `[RUN]`. Neither was noticed.

**What is genuinely good here and should not be lost:** the nine repo-wide architectural detectors
(`UnboundCommandPropertyTests`, `XamlBindingPathTests`, `EmptyCatchBlockTests`, `AsyncVoidTests`,
`SilentBroadCatchTests`, …) are unusually good and are the reason it can be stated with `[RUN]`
evidence that **there are no unbound commands and no broken bindings anywhere in the app**. The DXF
module's hard ratchets (`LangaraLostCeiling = 7`, `MissizedCeiling = 0`) are the right shape. The
`FileSyncExcludedFromAiTests` guard that keeps FileSync data out of AI prompts is a real architectural
control. And FileSync's **Shadow mode** — every job computes what it *would* do and writes the plan
without touching anything — is the best compensating control in the suite.

### T5 — The written record of the system is wrong, and some of it is machine-read.

**The headline staleness number needs correcting before it is repeated.** `01-DOC-TRUST.md` stamps 434
documents, 279 of them (64%) older than 60 days `[RUN]` — but that count includes NuGet package
README, LICENSE and `useSharedDesignerContext.txt` files, which are not KOR documentation. Recomputed
from the same file: **excluding `packages/`, 194 of 349 (55%); for `docs/` alone, 120 of 253 (47%)**
`[RUN: recomputed]`. The theme holds; the number is 55%, not 64%.

The severity is not the age. It is that the stale parts are load-bearing:

- **`AGENTS.md` names two projects as known-broken and repeats the claim a third time at `:31`. All
  three assertions are false** `[RUN]`. `EmailFilerv2` builds clean in one MSBuild invocation, ships as
  a signed ClickOnce VSTO and has 8,378 live rows in production. `Kor.Transmittals.App.Tests` compiles
  and passes **7 of 7 in 255 ms**. This is the first document every session reads, and it cost this
  audit real time in two separate modules.
- `docs/Takeoff-RESUME.md` is labelled *"AUTHORITATIVE… read this FIRST"* and is wrong on **five
  counts**, including a `pdftoppm` pre-step that is obsolete and scratchpad paths that no longer exist
  `[RUN]`.
- `docs/PROTOCOL.md` documents **29 verbs**; the code dispatches **56**, and `exportdxf` appears
  nowhere in it `[RUN]`.
- `DEMO-PLAYBOOK.md` says **28 tools**, `BUILD-STATUS.md` says **79**, the catalog has **137** — any of
  which can be contradicted by the ribbon on screen `[RUN]`.
- `BRIDGE-READY.md` says the active bridge is Revit 2025; the log says Revit 2020, and its deployment
  steps install artifacts that lack `exportdxf` `[QUERIED]`.
- `PLAN-AND-GAPS.md` entries C2 and C4 assert defects the code has already fixed and documents in its
  own comments `[READ]`.
- The shipped accuracy brief claims columns **−4%** on 31065; today's free-mode run reports *"no
  readable column schedule"* and **+135%** `[RUN]`. Unresolved.
- `Vocabulary/candidates.md` is a 4,142-line file whose header instructs a KEEP/DROP/REWRITE review
  that **never happened** (`grep` → 0 marks), unreferenced by any code `[RUN]`.
- `LEXICON.md` names a SQL home in a database and migration that **do not exist**, under a status
  banner 19 days and 40 migrations out of date `[READ]`.

**And some of it is not read by people.** The MCP system prompt is 51,770 characters of hand-written
firm knowledge that the model treats as fact — including the parity claims in T3, and a line still
teaching *"Deltek Revenue Generation is OFF at KOR"* which is now correct but was written when the
tool believed the opposite. UI tooltips are in the same category: the `$185` median, *"same source as
the GL P&L tab"*, a help window describing **neither** of the two code paths that exist. When the
document is a prompt or a tooltip, "stale doc" becomes "the product states something false."

---

*A sixth pattern holds just as strongly — **credentials have no shared home** — and is set out in §6
so it can be ranked by real exposure rather than described twice.*

---

## 4. What is genuinely excellent

Eight months, one person, and the following are not "good for the circumstances" — they are good.

**The email corpus.** 372,370 emails, 955 projects, back to 2014-10-28, full-text catalog fully
populated and current, ~50–100 new every working day from real staff `[QUERIED]`. This is not a demo
fixture. Against Egnyte's two-month-old Email Capture and Newforma's twenty years, **the corpus is the
argument** — and search runs over message *bodies*, returning 7,216 hits across the firm's whole
history in under a second `[QUERIED]`. Filing awaits the index insert before returning, so a message
filed on stage is findable immediately. That immediacy is a designed detail and it works.

**The transmittal telemetry.** 4,284 per-recipient links, 741 distinct external recipients, **2,682
click events with zero null IP, zero null user-agent, zero null recipient email**, and 730 of 829
transmittals carrying at least one recorded open `[QUERIED]`. Newforma's current product logs a
download *count* against a share with a two-week link; Konekt's own docs say *"history is only
recorded for sharing."* KOR logs a named person, at a named IP, at a named minute, and keeps it. The
competitive claim survives contact with the database, which is rare.

**FileSync's operational maturity.** Eight days of continuous uptime, 2,108 recorded runs, **97.8%
success over the last 7 days**, **zero empty catch blocks in 6,919 lines** (84 catches, every one logs
or rethrows), a clean Debug build with `TreatWarningsAsErrors` and StyleCop active, failure alerts that
demonstrably landed in the owner's inbox during the audit, and **Shadow mode on every job** — compute
the plan, write it to disk, touch nothing `[QUERIED + RUN]`. Startup fails fast and names any missing
secret; it logs a redacted 4-character tail of each so an operator can confirm a rotation landed. That
is a designed system, not an accreted one.

**The DXF→ETABS engine's hygiene.** Zero TODOs, zero `NotImplementedException`, zero empty catches, no
hardcoded job numbers or paths outside comments, **35 rules in a database rather than constants**, and
a report that volunteers what it could not do rather than hiding it `[RUN]`. A 63-storey rebuild
reproduces its baseline **exactly** in 50.7 seconds. Three independent sources — a live run, the
shipped report, and the baseline test — agree to the unit on 63 storeys / 1,119 walls / 2,462 columns /
82 plates `[RUN + QUERIED + READ]`. Against a market where CSI ships zero AI, ETABS's own DXF import is
manual tracing, and no venture-funded equivalent could be found, this is the rarest thing in the suite.

**The takeoff engine's honesty model.** Every plate is green (measured), orange (assumed, with the
reason printed), or named as a residual it refuses to price. 19 of 54 plates flagged with their own
reasons, including two `[Critical] SLAB_TOO_THICK`. It is **exactly reproducible** — a 40-page range
and the full 73-page set both total 19,545 cy — and it prices a 40-storey building for **$0 with zero
AI calls** `[RUN]`. `docs/Takeoff-Doctrine.md` names the single calibrated constant in a
fitted-parameter register with an explicit anti-overfit rule. That is stronger governance than most
commercial takeoff tools publish.

**The BD dedup and entity-resolution discipline.** 9,641 live canonical organisations, **9,641
distinct normalised names, zero duplicate groups** `[QUERIED]`. The 769,290 retired rows are not rot —
they are deliberate tombstones (*"born-archived on intake: orphan procurement vendor; resurrects on any
future reference"*). The dedup job was **deliberately retired** in favour of a supervised CLI after its
FK list drifted, and the CLI carries a similarity gate written in response to a specific named
incident. Six adversarial-audit findings from May were checked in current code and **all six are
fixed**, several with the fix shape the audit recommended and a comment citing the finding ID `[RUN]`.

**The AI layer's architecture.** 7,897 lines with **zero** TODO/FIXME/HACK/`NotImplementedException`
and **zero** empty catch blocks `[RUN]`. A startup gate that refuses to boot on prompt/tool drift. A
layered read-only SQL gate with 15 passing tests. `temperature = 0` with a documented rationale. A
system prompt that names **three real past hallucination incidents with dates and dollar figures** and
forbids each. Per-user concurrency limits, a 300k token budget, 429 handling honouring `Retry-After`,
and two circuit breakers — every one failing with an honest plain-English message. The API key lives on
the server, never on a workstation, and is **not in the repo** `[RUN]`. When this module produces a
wrong number it is not hallucinating; every guard is working perfectly and it is faithfully reporting a
stale tool. That distinction is worth a great deal.

**The self-diagnostic health audit.** `DataHealthAuditJob` writes a weekly report classifying every
source `DEAD-GREEN` / `NEVER-PRODUCED`, tracks enrichment coverage by org kind, FK orphan rates and
identity-drift sentinels `[QUERIED]`. It silently fixed three of the top recommendations from the
July audit. Showing that file to a sceptical technical lead is more persuasive than the UI.

**The Revit continuity work.** 137 tools on one ribbon across seven Revit versions from one codebase,
replacing **195 obfuscated DLLs with no source** left by a departed developer — and it builds for both
Revit 2023 (net48) and 2025 (net8) **on a machine with no Revit installed**, because the API comes from
NuGet `[RUN]`. Firm-wide deployment is a copy to a share with **27 rollback snapshots** and documented
install/remove/restore scripts. The thing that made the old estate un-inheritable has been designed
out. That is the single best continuity story here and it should be said out loud.

**Marker cleanliness across the whole suite.** Zero `TODO`/`FIXME`/`HACK` in the BD Brain's 66,000
lines, in the BD surface's 34,000, in the MCP's 7,897, in Financials' 26,500, in the DXF engine, in
both Revit repos. Of the 33 `NotImplementedException`/`NotSupportedException` in the entire suite,
**30 are idiomatic WPF `ConvertBack` stubs and the 31st is a deliberate DI guard carrying an
explanatory message** `[RUN]`. **Zero of the 33 represent unfinished functionality.** For 351,000 lines
written in eight months, that is genuinely unusual.

---

## 5. Bus factor and continuity

The battlecard names *"What happens when you leave?"* as the most dangerous objection and correctly
observes that no cost table answers it. Here is what the evidence supports.

**Where continuity is real and demonstrable:**

- **KOR.RevitTools is not a one-person artifact.** Anyone with the repo and a .NET SDK can build it,
  proven by building both Revit-year targets on a machine with **no Revit installed** `[RUN]`.
  Deployment is a documented script to a share with 27 rollback snapshots, plus
  `install-loader.ps1`, `Remove-LegacyTools.ps1` and `Restore-LegacyTools.ps1` for reversal
  `[QUERIED]`. This is the strongest continuity story in the suite, and it is stronger *because* it was
  built to replace an estate that had none.
- **The record survives the software.** Filed email lands in the project folder on KOR's own file
  server; transmittal payloads land in KOR's own SharePoint tenant. If every line of this code vanished
  tonight, the project files are exactly where they are this morning. That claim holds `[READ]`.
- **The rules are in a database, not in someone's head.** All 35 DXF→ETABS thresholds are rows in
  `KorStandards`, and `db/036_EveryRuleLivesHere.sql:154` enforces it: *"None is compiled in, and there
  is no fallback value."* An engineer answers a workbook cell, runs an import, and the next model uses
  it — no code change `[READ + QUERIED]`.
- **The architectural detectors are institutional memory in executable form.** They will keep failing
  on the same class of mistake long after anyone remembers why they were written.

**Where the bus factor is real and currently unmitigated:**

- **The Outlook add-in can be rebuilt by exactly one person on exactly one machine.**
  `SignManifests=true` with a thumbprint pointing at `CN=kor\ilalonde` in Ian's personal certificate
  store, expiring **2027-04-14**, and the referenced `.pfx` is **not in the repo and not in git**
  `[QUERIED]`. No other machine can produce a loadable build. A proper `CN=KOR Structural Code Signing`
  certificate valid to 2031 exists in the same store and is not the one being used.
- **The redirector has no history at all.** No git, no branch, no diff, no way to answer "what changed
  and when" about a service that has been logging client evidence for nine months `[RUN]`.
- **KOR.Drafter is one person and one machine.** The bridge is installed on KOR-302N only,
  `Dialog-Watchdog.ps1` — referenced in `PROTOCOL.md` and five process documents — **exists nowhere in
  the repo**, only on that workstation, and the repo's own README forbids it going anywhere else
  `[READ + QUERIED]`.
- **The deploy procedure exists only in the owner's head.** No `deploy.ps1`, no FileSync runbook, and
  the evidence on the server is *consistent with* a stop/robocopy/start pattern that could not be
  confirmed `[QUERIED]`.
- **The MCP smoke harness, the one artifact that proves the AI's numbers are right, has been broken
  for ten weeks and nothing schedules it** `[RUN]`.
- **The knowledge that is written down is 55% older than 60 days and demonstrably wrong in the first
  document a newcomer reads** (T5).

**The honest framing for the room.** The parts of this suite a successor would inherit cleanly are the
ones where the design forced it — rules in SQL, records in SharePoint, add-ins from NuGet, deploys
from a share with rollback. The parts they would not are the ones that were never deployed twice: the
redirector, the signing certificate, the bridge workstation, the runbooks. **That is a two-week list,
not a two-year one**, and every item on it is in `04-TODO-REGISTER.md`. What cannot honestly be said
today is *"it's all in source control"* — one internet-facing production service is not, and the
battlecard already flags that the answer to objection 1 depends on fixing it (`VI-1`).

---

## 6. The security picture, ranked by real exposure

Separated as asked: what is in git forever, what is one server's file permissions, and what is
neither. **This is the sixth cross-cutting theme — there is no shared way to hold a secret in this
suite. Six different mechanisms are in use across ten locations, and no two modules agree.**

### Tier 1 — In git history. Rotation is necessary but not sufficient.

| What | Where | Evidence |
|---|---|---|
| SQL password for `transmittals_app` — reaches `KorTransmittals` **and** `KorEmailIndex` | `Kor.Operations.App/App.config:162` and `EmailFiler/EmailFilerv2/{app.config,EmailFilerv2.dll.config}`, all **tracked** | `[RUN: git ls-files]` `[QUERIED: the auditor connected with it and read the whole 372k-row index]` |
| SQL password for `opportunities_app` | `App.config:170` | `[RUN]` |
| `McpServer.Password` — the shared Basic-auth credential guarding read access to KOR's complete Deltek financials | `App.config:148`. SHA-1 of the committed value is **byte-identical** to `Mcp.Password` in the live server config | `[RUN]` |
| `WatchlistSync.Password`, `Vp.Password` | `App.config` | `[RUN]` |
| Live SQL password for `standards_reader` | `KOR.RevitTools/PALETTE-README.md:20` — **only on the unmerged `feature/details-palette` branch**, so it can still be scrubbed. Merging makes it permanent | `[READ]` `[QUERIED: confirmed live, and correctly scoped — a `SELECT` on `analysis.vw_RuleSetting` was refused]` |
| Deltek **username** `52267.nucleus.prd` (no password) | `Kor.Operations.App/Scripts/20260807_filesync_kormapsync.sql:36` | `[READ]` |

The `transmittals_app` password is still the literal scaffold placeholder `‹REDACTED — the unmodified scaffold placeholder shipped by the project template›`.
Two aggravating facts: `EmailFilerv2.dll.config.deploy` **ships inside the app zip to every one of ~40
staff machines**, and the VSTO add-in has **no secret-override path at all** — it reads
`ConfigurationManager` directly — so rotating the password silently breaks filing for the whole firm
while the desktop app keeps working `[READ: ItemsToFileProcessor.cs:66-71]`.

**Clean, and the distinction matters:** `git grep` over tracked files finds **no** Anthropic API key
anywhere, and `git log -p` over the MCP production config's full history finds no `sk-ant-api03`
`[RUN]`. The repo copy of that file is sanitised to empty values with the note *"Real secrets live on
KOR-APP01 only. Never commit."* The AI key custody design is correct and was followed.

### Tier 2 — One server's file permissions.

`\\kor-app01\C$\Program Files\KorOperations\Mcp\appsettings.Production.json` holds **four live secrets
in cleartext**: the Anthropic API key, the Deltek ODBC username and password, the `mcp_app` SQL
connection string, and the MCP Basic-auth password `[QUERIED]`. Every ACE is **inherited** from
`C:\Program Files` — the file has no hardening of its own — and includes **`BUILTIN\Users: Read &
Execute`**, which on a domain-joined server means effectively every KOR employee account.

What prevents mass remote access is **not** the ACL but the path: it sits under the `C$`
administrative share. **The secrets are protected by an accident of file layout, not by design.**
Anyone who obtains any interactive session, RDP session, scheduled task or process on KOR-APP01 reads
all four with no privilege escalation. What that yields: read access to KOR's **entire** accounting
catalog — every project, invoice, client and salary-bearing table (the auditor used exactly this
credential for 20 queries, so the reach is not theoretical) — plus billable spend against KOR's
Anthropic account.

Smallest fix, in order, **all of which Ian runs on the server**: (1) rotate all four; (2)
`icacls /inheritance:d` then `/remove:g "BUILTIN\Users"` — the service runs as `kor\app-admin`, which
keeps Full control, but verify before applying and restart afterwards; (3) migrate to Machine
environment variables read through the `EnvironmentSecretOverrides` path the WPF app already uses.

### Tier 3 — Plaintext registry and a staff-readable share.

- **`KOR_OPPORTUNITIES_OPPORTUNITIESDB`** — a Machine environment variable on KOR-APP01 holding a
  connection string with the `opportunities_app` password `[QUERIED]`. Machine env vars live in
  `HKLM\…\Session Manager\Environment`, are inherited by every process on the host, and the auditor
  read it **remotely over RPC from a workstation with no interactive logon**. The login is
  **`db_owner` on `KorOpportunitiesDb`** — `BACKUP DATABASE` (whole-corpus exfiltration), `DROP`,
  `CREATE USER` — **and `db_datareader` + `db_datawriter` on `KorStandards`**, the production
  engineering-rules database the DXF→ETABS generator reads every rule from. **Neither grant is needed:**
  the Worker's only DDL is a temp table, and no file in Data, Core or Worker references `KorStandards`
  at all `[RUN: grep]`. Blast radius is genuinely bounded — `HAS_DBACCESS = 0` on the other 11
  databases, and the login is not `sysadmin`.
  *Unverified, with the check:* the auditor read it as an account with admin rights on APP01, so it is
  **not** established that an unprivileged domain user could. The one-line `Invoke-CimMethod` that
  settles it is in module 07 §5.9. If it returns `ReturnValue=0`, this jumps to Tier 1 urgency.
- **13 `KOR_FILESYNC_*` machine env vars** including four `KorMapSync` credentials and an Entra client
  secret — **plaintext, not DPAPI-protected** `[QUERIED]`.
- **`KOR_ENGINEERINGTOOLS_STANDARDSDB`** — a **User**-scope *persisted* env var containing the
  `opportunities_app` password in plaintext, reusing the BD application login for `KorStandards`. The
  project's own audit document says explicitly *"Set process-local. Never setx it"* `[QUERIED]`.
- **`\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\SetEnvironmentVariables.ps1`** — a plaintext
  script on the **staff-readable deploy share** holding **two Entra client secrets, the Deltek ODBC
  password, and an Anthropic API key** `[QUERIED]`. This is the single worst location in the list: no
  admin rights required, no server access required, and it sits on the path everyone uses to install
  the app.
- **`set-filesync-env*.ps1`** in the repo root — correctly gitignored and **verified not in git
  history**, but sitting unencrypted in the folder that would be screen-shared, holding a live Entra
  client secret and a SQL password `[QUERIED]`.

### Tier 4 — Compiled into a deployed binary, source untracked.

`Redirector/Kor.Transmittals.Redirector/Program.cs:33` embeds the **Azure AD tenant ID, client ID and
client secret** as `??` fallbacks, plus the **reCAPTCHA server-side secret key** `[READ]`. No `Graph:*`
key exists in any appsettings on disk or on the server, **so the fallback is what production uses**
`[QUERIED]`. This is an app-only Graph credential that uploads to and shares from KOR's SharePoint
tenant. It is not in a repo only because the directory is not in a repo. **Treat as disclosed.**

### Application-layer exposures that are not credentials

- **The Home screen's authorization gate fails OPEN.** `HomeWindow.xaml.cs:295-308` — a bare `catch`
  around the whole AD-group block force-*shows* **seven** surfaces including **Financials and
  Compensation** (salary and bonus data, the most sensitive screen in the suite), while six harmless BD
  surfaces fail **closed** `[READ]`. The trigger condition is an unreachable domain controller — which
  is precisely what launching the app at MVE's office before the VPN is up produces. Silent. Nothing
  crashes; the inversion is worst exactly where it matters.
- **Anyone on the internet can insert rows into the transmittal evidence log.**
  `Program.cs:119` maps `GET /o/{linkId:guid}/{email}` with no auth, no rate limit, no recipient
  validation and no dedup; the `linkId` need not even exist `[READ]`. Anyone who has ever received a
  KOR transmittal holds a valid URL. The endpoint was **not exercised** — the defect is read from
  source.
- **Raw .NET stack traces are returned to external users** on the partner-facing file-drop page —
  `WriteAsync("FileDrop POST error:\r\n\r\n" + ex)`, unconditional, no `UseExceptionHandler`, and no
  body-size limit so the ~28 MB framework default fires on a realistic drawing set `[READ]`.
- **No rate limiting, no auth, no HSTS, no antiforgery anywhere** in the redirector; `AllowedHosts` is
  `"*"`. reCAPTCHA is the only gate between the internet and unbounded writes into KOR's SharePoint
  `[RUN: 10 greps, all zero]`.
- **MCP identity is honour-system.** One shared password for the whole firm, over plaintext HTTP on the
  LAN, with the caller's identity taken from an **unverified `X-Kor-User-Upn` header**. The code says
  so: *"Honour-system identity for v1"* `[READ]`. Anyone holding the shared password can attribute
  questions to anyone else in the audit log.
- **Named employee data leaves the building.** Staff Utilization pushes up to 150 rows of
  `EmployeeName | hrs/wk | utilization | OT | cost-per-billable-hour` into the AI prompt, and the
  Pursuit Brief transmits **architect contact email addresses** and competitor capacity reads over
  plaintext HTTP `[READ]`. Deliberate and bounded, routed through KOR's own host — but the owner should
  know it is in the prompt.
- **`query_kor_data` runs on a connection with `db_datawriter`.** The read-only property rests
  entirely on a keyword gate in application code — a well-built gate with 15 passing tests, and the
  only gate `[QUERIED + RUN]`.
- **Developer Edition SQL in production.** The instance named `SQLEXPRESS` reports **Developer Edition
  16.0.1190.2** `[QUERIED]`, which is not licensed for production. If licensing ever forced it to real
  Express, `KorEmailIndex` at 10.8 GB is **already over** the 10 GB cap and filing would stop dead.

**Ranking, if only three things get done:** (1) the deploy-share PowerShell script with four secrets
in it — no privilege required to read it; (2) the MCP production config ACL plus rotation — one file
yields the firm's financials and its AI spend; (3) drop `opportunities_app` from `db_owner` and revoke
its `KorStandards` roles outright — free, no code change, and the largest blast-radius reduction per
minute spent. Everything else, including purging git history, is `SOON`.

---

## 7. Where the module reports disagree with each other or with the record

Named, with which is better evidenced. Nothing here is resolved by assertion.

| Contested claim | Resolution |
|---|---|
| **Is Deltek Revenue Generation ON or OFF?** Module 05 carries commit `818ebc19`'s statement that RG-off was *"verified false"* because `Unbilled` is populated on 238 rows. Module 04's first pass agreed, as did the July forensic audit (F1a), as does the battlecard's `VI-3a`. | **Module 04 supersedes all of them, and is the better evidence.** It ran the decisive test: `Revenue` equals `Billed` on **47,246 of 47,366 rows (99.75%)**; `Unbilled` is populated on **0.5%**, 1–3 rows a month scattered from 201901 to 202512 — a seven-year residue, not a toggle, and not per-Org `[QUERIED]`. **RG is OFF.** Consequence: `UnbilledColumnHasAny()` is a bare non-zero test, so 238 stray rows flip `WipFinancialsService` onto the Revenue-Generation branch on a tenant that has none. Also: the battlecard's *"`SUM(Revenue)` returns $0"* was false in the other direction — it returns **$69,061,768.57** `[QUERIED]`. Correct the battlecard, `WipTool`'s description, and the July audit's F1a. |
| **Is the MCP cash whitelist fixed?** Module 04 §5.4 records prior finding F13 as **FIXED** — the deployed `appsettings.Production.json` now carries `"CashAccountWhitelist": "1110.00,1120.00,1170.00"`. Module 05 says `get_cash_position` is **broken** and summing all 20 accounts. | **Both are right about different halves, and module 05 is the load-bearing one.** The config key is present; the deployed **code never reads it** — a UTF-16 scan of the shipped DLL finds the literal `CashAccountWhitelist` **absent**, and `git show` of the deployed commit confirms `Program.cs` stops building `FinancialsOptions` before that key `[RUN]`. Module 05's byte-scan of the artifact beats module 04's read of the config. F13 is **not** closed until the redeploy lands. |
| **Empty catch counts.** `02-CROSS-CUTTING-SCAN.md` reports 1 for EmailFiler, 19 for KOR.RevitTools, ~69 suite-wide. | **The module hand-counts win, and the scan says so itself.** Module 01 counts **23** in the email module (comment-only bodies swallow just as silently); module 11 counts **83 minimum, ~129 including comment-only and bare-return bodies** in KOR.RevitTools. The scan's regex only matched literally-empty braces. **Treat every number in that table as a floor.** |
| **Deltek naming: three-part or four-part?** The environment facts say four-part. Module 04 says **three-part** `[RUN]`; module 05 says four-part `[READ]`. | **Not a contradiction — two different paths.** The ODBC path every Financials service uses is `[catalog].dbo.Table`, three-part, verified by grep returning nothing for four-part or `OPENQUERY`. The linked-server path `query_kor_data` uses is `[DELTEK_VP].[catalog].dbo.*`, four-part. Module 04 states this explicitly. Both stand. |
| **Transmittal numbering.** The battlecard's `VI-2` asks whether `ReserveTransmittalNumberAsync` reserves a real sequence. | **It does not.** `GraphFacade.cs:352-358` returns `$"{projectNumber}-{DateTime.UtcNow:yyyyMMdd-HHmmss}"` — three lines, no DB read, no counter, no collision check `[READ]`. `CoverSheetRenderer.cs:684` exists solely to re-render that UTC stamp in Pacific time, confirming it is understood as a timestamp. **Correct the battlecard before anyone repeats it to a partner.** |
| **Win/loss figures.** Battlecard `VI-9` quotes 173 Pursuing / 83 Lost / `LostTo` on 3 of 79, measured 2026-06-25. | **Module 07 re-measured on 2026-08-20**: `Deltek.PR` = 178 Pursuing / 85 Lost / 8 Declined / **0 Won**; `LostToName` on **3 of 85** `[QUERIED]`. Use the newer figures. Module 07 also **retracts its own earlier flag** that "Deltek holds no won/loss signal" was wrong — the standing record is right, and the refinement is that `MapStage` *does* now map `~WDEF~ → Won` (added 2026-07-11, deployed), but the sweep only catches **future** conversions, so the cause of 0 Won is a **backfill gap, not a missing code branch**. |
| **DXF→ETABS output counts.** The figures in circulation — "68: 63/917/2469/83" and "38: 24/87/172/11" — appear in `project_dxf_to_etabs_generator.md`. | **Stale and not present anywhere in the current system.** Three independent sources agree exactly: **31168 = 63 storeys / 1,119 walls / 2,462 columns / 82 plates**; **31138 = 29 / 242 / 390 / 13** `[RUN + QUERIED + READ]`. "87" was the count of *the engineer's own* columns quoted in a dossier. The memory note also carries superseded 925 / 2,464 / 83 figures. |
| **Takeoff accuracy on 31065.** The shipped `Results Brief` (2026-07-04) claims columns **−4%**. Today's free-mode run reports *"no readable column schedule — footprint fallback"* and **+135%** `[RUN]`. | **Unresolved, and the module says so.** It could not be established whether that is a regression, a different invocation, or the answer key's zeros (107 of 322 column rows in the Revit export are `0 m³`). The paid-vision run that would settle it costs ~$2 and needs spend sign-off. **Do not quote the brief's category numbers until it is resolved.** The whole-building −7.0% is also **error-cancelling** — a −2,254 cy slab shortfall partly masked by a +1,437 cy column over-count — and must not be quoted as "−7% accuracy". |
| **`CrmAnalyticsService` as "a third analytics implementation".** | **Off by one axis** `[READ]`. `PMTools/HistoricalAnalyticsService` computes nothing — it is a 73-line facade — and **neither PMTools nor Financials contains any win-rate, won-count or proposed-fee math**. The real duplication is *inside* the BD surface (T3). |
| **`AnalyticsAiService` as "a second AI implementation".** | **It is not** `[READ]`. It is a ~90-line prompt-context builder; there is exactly one LLM path from those windows, through the MCP server. The *real* second path is `AppAiService.AskWithToolsAsync`, which hits `api.anthropic.com` directly and is used only by PdfToSafe — a live route that could bypass the server-side key custody the architecture is built on. |
| **AGENTS.md's build warnings.** | **0 for 3** `[RUN]`. Two projects named as known-broken, the claim repeated a third time at `:31`; all three assertions false. |

---

## 8. The bottom line

**What is finished:** email filing and search, transmittal issue and per-recipient tracking, FileSync's
seven jobs, the Deltek reporting surface, the MCP tool architecture, the BD ingestion and entity graph,
the DXF→ETABS engine, the takeoff engine, and 137 Revit tools. Roughly 63% of catalogued capabilities
are `WORKING` and verified by someone who ran them.

**What is half-built:** the BD loop's last two steps (research, and outcome→scoring feedback), WIP as a
meaningful number, the Revit→DXF→ETABS chain's layer contract, the vector takeoff engine's route into
the product, RFI/submittal record types (absent entirely), and link expiry.

**What will break on screen:** nothing crashes. What breaks is credibility — the AI stating a cash
figure that differs from the screen beside it, a live countdown to a job that never fires, a blank log
viewer on a healthy service, a `4501-01-01` date in a filename, a tooltip contradicting the KPI above
it, and the BD workspace's default screen listing 25 architecture firms next to KOR's plan to displace
their engineers, in front of an architecture firm.

**What to fix first** is in `04-TODO-REGISTER.md`, ordered by risk × cheapness, with the five that
matter most at the top. Four of the five are configuration or a redeploy, not code.
