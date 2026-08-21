# Module Audit — BD Brain (Opportunities ingestion, resolution, enrichment, scoring, research)

Audited 2026-08-20. Evidence tiers inline: `RUN` `QUERIED` `READ` `DOC`.

---

## 1. What I searched

**Repo paths read/grepped** (all under `C:\VIsual Studio Projects\Operations`):
`Kor.Opportunities.Data` (233 .cs / 48,832 LOC), `.Core` (54/3,684), `.Worker` (76/12,995),
`.Capture` (1/177), `.ApcImport`, `.Data.Tests` (9 files), `Kor.Operations.App/BusinessDevelopment/**`,
`Kor.Operations.App/Opportunities/**`, `tools/BdCanonicalDedup/Program.cs`.

**Greps**: `TODO|FIXME|HACK`; `NotImplementedException|NotSupportedException`; `catch\s*(\([^)]*\))?\s*\{\s*\}`;
`"[A-Z]:\\..."` and UNC literals; `(apikey|password|secret|token)\s*=\s*"…"`; `DeltekClientId`;
`OpportunityDuplicateScorer`; `WonLostOutcome.NoBid|OpportunityStatus.Lost`; `NaturalKey`; `new HttpClient()`;
`\.Timeout\s*=`; `SamGov.*key`.

**Builds / tests** `[RUN]`: `dotnet build Kor.Opportunities.Data.Tests -c Debug` → **succeeded, 0 errors, 6 warnings**.
`dotnet test … --no-build` → **96 passed / 0 failed, 1.0s**.

**Live state** `[QUERIED]`:
- `Get-CimInstance -ComputerName KOR-APP01 -ClassName Win32_Service` → service inventory.
- `StdRegProv.GetStringValue` on `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` → read
  `KOR_OPPORTUNITIES_OPPORTUNITIESDB`.
- ~25 read-only `SELECT`s against `KorOpportunitiesDb` on `KOR-APP01\SQLEXPRESS` (schema `opportunities`)
  covering `ServiceHeartbeat`, `IngestionRuns`, `JobRuns`, `JobSchedules`, `OpportunitySources`,
  `Opportunities`, `CanonicalOrg`, `IntelPerson`, `KorPursuits`, `RelevanceGateRejects`, `BdUiOpens`,
  `INFORMATION_SCHEMA`. **SELECT only — no writes, no service actions.**
- `\\KOR-APP01\C$\ProgramData\KorOperations\DataHealthAudit\latest.md` (the system's own weekly self-audit).
- `\\KOR-APP01\C$\ProgramData\KorOperations\Opportunities\sessions\` (empty), and deployed binary timestamps
  in `C:\Program Files\KorOperations\Opportunities\`.
- `GET http://kor-app01:5500/health` → **200**, `{"status":"ok","service":"Kor.Operations.Mcp","version":"0.4.2"}`.

**Prior audits read as `DOC` only — eleven documents, 2,361 lines total.**
In `docs/`: `BD-Audit-2026-06-09.md`, `-06-19.md`, `-07-01.md`, `BD-Module-GapAnalysis-2026-06-21.md`,
`BD-Completeness-Audit-2026-07-01.md`.
At the **repository root** (not `docs/`): `BD-AUDIT-20260530.md` (284), `BD-AUDIT-20260530-R2.md` (271),
`BD-PM-AUDIT-20260530-R3.md` (42), `-R4.md` (78), `-R5.md` (42), `-R6.md` (47) — 764 lines, all committed
2026-05-30 `[RUN: wc -l, git log]`. These six are an iterative adversarial-review series (Rounds 37–42)
covering BD **and** PM Tools; roughly half their findings are in `Kor.Operations.App/PMTools/` and belong to
a different module. Triage of all of them is in the Appendix.

Every one of these documents predates the code it describes (`Kor.Opportunities.Data` last touched
2026-08-02, `.Worker` 2026-07-18), so per rubric rule 2 each claim below was re-verified against live state
rather than repeated.

---

## 2. What this module is

The BD Brain is KOR's automated business-development pipeline. A Windows service on KOR-APP01 continuously
scrapes and polls ~111 public procurement sources — BC Bid, Alberta Purchasing Connection, CanadaBuys, MERX,
~40 Bonfire tenants, ~25 bids&tenders tenants, CivicInfo BC, SAM.gov, LA City RAMP — plus an alerts mailbox via
Microsoft Graph. Each posting is scored by a structural-relevance gate, matched to a canonical organization,
and filed as an opportunity. Alongside the live feed it maintains a large historical corpus: 139,472 contract
awards, 50,811 building permits, 10,286 major-project records, and an entity graph of ~9,641 live
organizations with 12,974 people and their affiliations `[QUERIED]`.

What a user sees is the **BD Workspace** inside the Kor.Operations WPF app: a dashboard, an Opportunities Hub
list, plus Overwatch, Relationships, Bazaar, Events, Attribution and Admin views, with a Pursuit Brief window
that exports a branded PDF `[READ: Kor.Operations.App/BusinessDevelopment/Workspace/*.xaml]`. Behind it, 32
Quartz jobs run ingestion, enrichment, cleanup, reporting and AI research on cron schedules `[QUERIED:
JobSchedules]`. The intended loop is: ingest → gate → resolve entity → enrich → score → a human picks up a
pursuit → outcome feeds back into scoring. **The first five steps run continuously and well. The last two do
not happen** — see §5.

---

## 3. How you would demo it

**Prerequisites**: on the KOR LAN or VPN (SQL on `KOR-APP01\SQLEXPRESS` is LAN-only, no public endpoint);
Kor.Operations.App installed. The Worker service does *not* need to be running for the UI to read data.

**Click path**: launch Kor.Operations.App → **Business Development → BD Workspace** →
**Opportunities Hub**. Then **Admin → Job Run History** (`JobRunHistoryWindow.xaml`) and
**Ingestion Runs** (`Opportunities/IngestionRunsWindow.xaml`) to show the machinery `[READ]`.

**What genuinely lands** — and this is the strongest part of the demo:
- **Ingestion is live right now.** Worker heartbeat `2026-08-20 22:40`, most recent ingestion run
  `22:37`, 33,039 ingestion runs recorded, 222,675 job runs `[QUERIED]`.
- **The system audits its own data health.** `DataHealthAuditJob` writes a weekly markdown report that
  classifies each source `DEAD-GREEN` / `NEVER-PRODUCED`, tracks enrichment coverage by org kind, FK orphan
  rates, and identity-drift sentinels `[QUERIED: latest.md, 2026-08-16]`. Showing this file to a sceptical
  technical lead is more persuasive than the UI.
- **Entity resolution is genuinely correct**: 9,641 live canonical orgs, **9,641 distinct normalized names,
  zero duplicate groups** `[QUERIED]`.

**What will not demo**: any won/lost or pipeline-conversion story. There are **0 Won and 0 Submitted**
opportunities; exactly **1** is in Pursuing `[QUERIED]`. Do not open a "pipeline funnel" view.

---

## 4. Completeness

| Capability | State | Evidence |
|---|---|---|
| Multi-source ingestion (111 sources, 101 producing) | `WORKING` | `QUERIED` — runs today, 0 failures except SamGov |
| Quartz scheduler / job host (32 jobs, 30 enabled) | `WORKING` | `QUERIED` JobSchedules + JobRuns |
| Structural relevance gate + persisted rejects | `WORKING` | `QUERIED` 11,535 RelevanceGateRejects, latest today |
| Canonical org resolution + archive/resurrect | `WORKING` | `QUERIED` 9,641 live, 0 dupes |
| Awards / permits / MPI historical corpus | `WORKING` | `QUERIED` 139,472 / 50,811 / 10,286 rows |
| Self-diagnostic data-health audit | `WORKING` | `QUERIED` latest.md |
| Org & people enrichment | `PARTIAL` | `QUERIED` CanonicalOrgEnrichment fresh (08-16); IntelPerson stale (08-07) |
| Opportunity scoring | `PARTIAL` | `READ`+`QUERIED` — Deltek half of the rule set is unreachable (§5) |
| **AI research agents (org/project/person)** | **`DEAD`** | `QUERIED` — `considered=0; executed=0` every day |
| **Cross-source dedup at ingest** | **`STUBBED`** | `READ` — scorer exists, wired to manual-entry UI only |
| **Deltek ↔ opportunity linkage** | **`STUBBED`** | `QUERIED` — dry-run only, `DeltekClientId` NULL on 2,599/2,599 |
| **SAM.gov (only US federal source)** | **`DEAD`** | `QUERIED` — HTTP 401 daily since 2026-08-02 |
| Pursuit lifecycle (human workflow) | `PARTIAL` | `QUERIED` — UI exists; 1 opp ever owned |
| `Kor.Opportunities.Capture` | `DEAD (unused)` | `QUERIED` — sessions dir empty since 2026-05-21 |

**Marker counts** `[RUN: grep]` — genuinely clean, and unusually so:
- `TODO` / `FIXME` / `HACK` across Data + Core + Worker + Capture + ApcImport: **0**
- `NotImplementedException` / `NotSupportedException`: **2**
- **Empty `catch` blocks: 27**, *all* in `Ingestion/Scraping` (see §5).

---

## 5. What is broken or risky

**5.1 — The AI research layer is dead but reports success.** `[QUERIED]` The three executors ran today
(07:00 / 07:30 / 08:00) and returned `Success=1` with summary
`considered=0; executed=0; ok=0; failed=0; inputTok=0; outputTok=0` — **identical every day back to at least
2026-08-17**. Cause: their feeder, `BdResearchQueueBuilderJob`, is `Enabled=1` in `JobSchedules` but its
`NextFireAtUtc` is stuck at **2026-07-19 04:30** (every other enabled job has a future fire time and an
`UpdatedAtUtc` of today), and it has **zero rows in `JobRuns`** — it has never fired. Downstream, the last
`BdResearchTriggers` row was created **2026-06-21**, the last `IntelNarrative` **2026-06-27**, the last
`IntelProject` **2026-06-25**. Two months of green dashboards over an empty queue.

**5.2 — `DeltekClientId` is NULL on all 2,599 opportunities, disabling the best scoring signal.** `[QUERIED]`
`RuleBasedOpportunityScoringService.cs:101` gates a whole block on it — "Deltek-linked (repeat developer)",
`PriorWorkBonus`, `RecommendBonus`, `LifetimeFeeBonus` (lines 101–127). Nothing in the ingestion path or Worker
ever *writes* the column (`grep "DeltekClientId\s*=" Kor.Opportunities.Data/Ingestion Kor.Opportunities.Worker`
→ **no matches**) `[RUN]`. So KOR's single most differentiating signal — its own client history — never
contributes a point. `BdDeltekLinkDryRunJob` runs nightly and is **permanently a dry run**: identical output
every day, `targets=3218 auto-link=8 review=136 dedup=4 no-match=3070` `[QUERIED]`.

**5.3 — No cross-source dedup at ingest; the top of the demo list is visibly duplicated.** `[QUERIED]+[READ]`
`FindPossibleDuplicatesAsync` (`SqlOpportunityStore.cs:151`, using `OpportunityDuplicateScorer` at lines
254–255) is reached **only** through `Kor.Operations.App/Opportunities/IOpportunityDuplicateFinder.cs:30`, a
self-described *"thin UI-facing seam"* for manual entry. Ingestion dedups on exact `OpportunityKey` only.
Result, in the actual top-5 of the active board sorted by relevance:

| Score | Name | Buyer | Key |
|---|---|---|---|
| 40 | WR26-021 … Marine Structural Engineering | City of White Rock | `BCBID-232887` |
| 35 | WR26-021 … Marine Structural Engineering | City of White Rock | `BCBIDENG-233209` |
| 30 | WR26-021 … Marine Structural Engineering | **Unknown** | `BCBIDENG-232887` |

The same tender, three times, at three different scores, one with buyer `Unknown`. Across the 921 active
opportunities there are **69 duplicate name-groups / 112 redundant rows (~12%)**.

**5.4 — Alert emails are ingested as opportunities.** `[QUERIED]` 55+ active rows named
`APC – Notification of New Postings for "KOR Structural AB - daily" – Wed, Aug 19` with
`BuyerName = "APC – Notification of New…"`, `Status=1 (New)`, score 12, keys `BDALERTS-*`. The GraphEmail
provider files the digest email itself rather than parsing the postings out of it. New ones arrive daily.

**5.5 — 2018–2019 already-awarded contracts sit in the "New" pile.** `[QUERIED]` All 80 `COVAWARD-*` rows are
`Status=1` with NULL deadline, e.g. `Consultant for Cambie Bridge Rehabilitation` (`COVAWARD-PS20181561`) and
`Contractor for PNE Garden Auditorium: Re-roof` (`COVAWARD-PS20190609`). Several also carry mangled text
(`EFL Area 2 # Phase 1`), i.e. an encoding defect on intake.

**5.6 — SAM.gov has been 401 Unauthorized for 19 straight days.** `[QUERIED]` Last success **2026-08-01**; every
run since (12 successes / 19 failures lifetime) errors
`Response status code does not indicate success: 401 (Unauthorized).` The key comes from
`OpportunitiesWorkerOptions.SamGovApiKey` (machine env var, not hardcoded) `[READ:
Worker/Options/OpportunitiesWorkerOptions.cs:30]` — SAM.gov keys expire ~90 days. **This is the only US federal
source, and MVE is a SoCal firm.**

**5.7 — Production is running two-week-old code.** `[QUERIED]` Deployed
`Kor.Opportunities.Worker.exe` / `Kor.Opportunities.Data.dll` are both dated **2026-07-18 11:19**, but
`Kor.Opportunities.Data` has commits through **2026-08-02** (`fb73e467`). Anything fixed in Data after Jul 18
is not live.

**5.8 — 27 empty catch blocks, all in the scrapers.** `[RUN: grep]` Concentrated in
`BcBidScraper.cs` (158, 637, 658, 674, 780), `BcBidHistoricalScraper.cs` (647, 761, 781, 797),
`BcBidUnverifiedBidResultsScraper.cs` (843, 959, 979, 995), `BcBidAwardsScraper.cs` (118, 275, 328),
`PlaywrightScraperBase.cs:81`, `BcBidPlanTakerExtractor.cs:104`. This is precisely the "pagination interprets
any anomaly as done" class the 2026-07-01 audit named `[DOC]`, and it is **still present**. It is also the
mechanism by which a portal markup change becomes silent under-collection rather than an error.

**5.9 — Security: an over-privileged SQL login stored as plaintext in a machine-wide location.**

*What is exposed.* The machine environment variable `KOR_OPPORTUNITIES_OPPORTUNITIESDB` on KOR-APP01 holds a
complete connection string with an embedded plaintext password for the SQL login **`opportunities_app`**
`[QUERIED]`. Machine env vars live in `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`
and are inherited by every process on the host. I read it remotely from a workstation over RPC via
`Invoke-CimMethod … StdRegProv GetStringValue` — no interactive logon to APP01, no file access, one call.

*What the credential actually reaches* `[QUERIED: fn_my_permissions, sys.database_role_members, HAS_DBACCESS]`:

| Database | Access | Consequence |
|---|---|---|
| `KorOpportunitiesDb` | **`db_owner`** (CONTROL) | Read/modify/delete all 67 tables; `DROP`/`ALTER` any object; `BACKUP DATABASE` (whole-corpus exfiltration to a file); `TAKE OWNERSHIP`; `CREATE USER`/`ALTER ANY USER` (persistence); `KILL DATABASE CONNECTION` |
| **`KorStandards`** | **`db_datareader` + `db_datawriter`** | SELECT/INSERT/UPDATE/DELETE on all 20 tables of the **production engineering-rules database**. Per `CLAUDE.md` a missing rule stops a production run by design — a writer here can silently alter or delete structural design rules consumed by the DXF→ETABS generator |
| `master`, `msdb`, `tempdb` | connect | Standard; enumeration only |
| The other 11 DBs on the instance (`KorTransmittals`, `KorEmailIndex`, `KorInspections`, `KorMcp`, `ContractRadarDb`, `NewformaMirror`, …) | **no access** (`HAS_DBACCESS=0`) | Blast radius genuinely bounded here |

*What boundary protects it.* Only that the login is **not** `sysadmin`, `securityadmin` or `dbcreator`
`[QUERIED]`, and cannot reach the other 11 databases. There is no secret store, no encryption at rest, and no
rotation. SQL auth also means the credential is replayable from any host that can reach the instance —
possession is sufficient, no domain context needed.

*Neither privilege level is actually required.* `[RUN: grep]` The Worker's only DDL is a **temp table**
(`CanonicalOrgKorProjectSignalRefreshJob.cs:77`, `CREATE TABLE #KorProjectSignal`), which needs tempdb, not
`db_owner`; nothing runs migrations at startup. And **no file in `Kor.Opportunities.Data`, `.Core` or
`.Worker` references `KorStandards` at all** — that grant is surplus to the service's entire function.

*Smallest change that fixes it.* The Worker already runs as the domain account `KOR\app-admin` `[QUERIED:
Win32_Service.StartName]`. Switch the connection string to `Trusted_Connection=True`, grant `KOR\app-admin`
the roles below, and drop the `opportunities_app` SQL login — that **removes** the secret rather than hiding
it, so there is nothing left to store, rotate or leak. Two smaller interim steps if that is too much before
the demo: (a) `ALTER ROLE db_owner DROP MEMBER opportunities_app` on `KorOpportunitiesDb`, leaving
`db_datareader` + `db_datawriter` + `EXECUTE`, and revoke both `KorStandards` roles outright; (b) if the SQL
login must survive, move the string into Windows Credential Manager or a DPAPI-protected file ACL'd to
`KOR\app-admin` only. **(a) is free of code changes and shrinks the blast radius the most per minute spent.**

*Unverified, with the check.* I read the variable as `ilalonde`, who holds administrative rights on APP01, so
I have **not** established that an unprivileged domain user could do the same — do not claim that. To settle
it, from a workstation logged in as a non-admin domain account:
`Invoke-CimMethod -ComputerName KOR-APP01 -Namespace root\default -ClassName StdRegProv -MethodName GetStringValue -Arguments @{hDefKey=[uint32]2147483650; sSubKeyName='SYSTEM\CurrentControlSet\Control\Session Manager\Environment'; sValueName='KOR_OPPORTUNITIES_OPPORTUNITIESDB'}`
— `ReturnValue=0` means any authenticated user can read it and this becomes urgent; access denied means the
exposure is limited to administrators and processes on the host, and it stays `SOON`.

*Clean by comparison.* No secrets are hardcoded in source (`grep` for
`(apikey|password|secret|token)\s*=\s*"…"` → **no matches**) `[RUN]`; the SAM.gov key is correctly read from
config rather than embedded; and all `HttpClient`s are typed clients with explicit `.Timeout`
`[READ: Worker/Program.cs:211,284,297,316,349,375,468,486]`. This one credential is the sole handling gap.

**5.10 — Nobody uses it.** `[QUERIED]` `OwnerStaffId` is set on **1** of 2,599 opportunities. `UpdatedBy`
tallies: `ingestion` 1,498, `DataRetirementJob` 1,017, `live-pursuits-sweep` 80, **`ilalonde@…` 3**. The BD
workspace was last opened **2026-07-13** (`BdUiOpens`, 65 rows lifetime). This is not a defect in the code,
but it is the honest context for every "is this used?" question MVE may ask.

*Note on data volume that looks alarming and is not*: `CanonicalOrg` holds 778,931 rows of which **769,290 are
retired tombstones** with reasons like *"Born-archived on intake: orphan procurement vendor; resurrects on any
future reference"*. That is deliberate design, and the live set is clean `[QUERIED]`.

---

## 6. Dependencies

| Dependency | Detail | Off-LAN? |
|---|---|---|
| **SQL Server** | `KOR-APP01\SQLEXPRESS`, db `KorOpportunitiesDb`, schema `opportunities`, 67 tables | ❌ LAN/VPN only |
| **Worker service** | `Kor.Opportunities.Worker`, Auto, **Running**, as `KOR\app-admin` | ❌ |
| **Microsoft Graph** | BD alerts mailbox ingestion (`GraphEmailIngestionJob`, every 15 min) | ✅ cloud |
| **Playwright / Chromium** | ~21 scrapers; browser must be installed on APP01 | ❌ server-side |
| **Public portals** | BC Bid, APC, CanadaBuys, MERX, Bonfire ×~40, bids&tenders ×~25, CivicInfo, LA RAMP | ✅ |
| **SAM.gov API** | API key via machine env var — **currently 401** | ✅ (broken) |
| **Deltek ODBC** | `KorPursuitDeltekSyncJob`, `BdDeltekLinkDryRunJob` | ❌ LAN only |
| **AI providers** | Research executors — currently no-ops | ✅ |
| **MCP service** | `kor-app01:5500`, `/health` → 200, v0.4.2 `[QUERIED]` | ❌ LAN only |
| **Kor.Operations.App** | WPF host for the entire BD UI; Windows-only | ❌ |

**Demoing from MVE's office requires VPN.** SQL, Deltek and MCP are all LAN-bound. If VPN is not certain,
demo from a cached/screenshot deck or a laptop already holding a live app session.

---

## 7. Test reality

`Kor.Opportunities.Data.Tests` — 9 files, 56 `[Fact]`/`[Theory]` attributes, **96 test cases, all passing in
1.0 s** `[RUN]`. Passing in one second means **zero integration coverage**: no test touches SQL, Playwright,
Graph, Deltek or an HTTP endpoint.

Ratio is **1,519 test LOC against 48,832 source LOC ≈ 3.1%** `[RUN: wc -l]`.

What *is* covered is well chosen — the pure-logic core:
`CanonicalOrgResolverTests` (14), `OpportunityDuplicateScorerTests` (9), `DisciplineClassifierTests` (9),
`MerxDcc`/`BcBid`/`BidsAndTenders` detail-extractor tests (13), `DoctrineTests` (7),
`StructuralRelevanceGateTests` (2), `PursuitLifecycleIntegrationTests` (2).

**What is unprotected is exactly what touches production data** — the answer to lead #1 is *yes*:

| Untested subsystem | LOC | Why it matters |
|---|---|---|
| `Awards/` | 8,073 | Writes the 139,472-row awards corpus + the canonical org graph |
| `Ingestion/Providers/` | 6,791 | Every source parser; all 27 empty catches' callers |
| `Ingestion/Scraping/` | 6,634 | Playwright scrapers — **the code most likely to break, since portals change markup constantly** |
| `BdReports/Generators/` | 3,574 | Generates what a human/MVE actually reads |
| `Intel/` | 2,726 | The (currently dead) research persistence layer |
| `Crm/` | 1,959 | Ships with 3 live nullable-warnings (§ below) |
| `MajorProjects/` | 1,759 | 10,286-row MPI corpus |

Coverage is not theatre — the tested classes are the right ones and the assertions are real — but it is
**thin in exactly the wrong place**. The scrapers have unit tests for parsing *fixture HTML*, and nothing that
would notice a portal changing its markup. The system's compensating control is the runtime
`DataHealthAuditJob` staleness sentinel, which is genuinely the right mitigation and does work.

Build warnings worth fixing: `Crm/SqlCrmEngagementStore.cs:107` (CS8629 nullable value may be null), `:108`
and `:109` (CS8604 possible null argument) `[RUN]`. Also `AngleSharp 0.17.1` carries a known moderate
advisory (GHSA-pgww-w46g-26qg) `[RUN: NU1902]`.

---

## 8. Demo risk (ranked)

1. **The #1-scored item on the board is the same tender three times, one with buyer "Unknown."** Anyone who
   looks at the Opportunities Hub for ten seconds sees it. This is the single most likely embarrassment.
2. **"APC – Notification of New Postings for 'KOR Structural AB - daily' – Wed, Aug 19"** appearing as a
   live opportunity. It reads as obviously broken to a technical lead, and a new one lands every weekday.
3. **Zero Won, zero Submitted, one Pursuing.** Any question about conversion, win rate or pipeline value has
   no good answer. Avoid the funnel/dashboard framing entirely.
4. **"Is the AI research actually running?"** If asked to show it live, the honest answer is that it has
   produced nothing since 2026-06-27. The job history is visible in the app's own Job Run History window, so
   this is discoverable on screen.
5. **US coverage is dead.** MVE is SoCal. If asked "what do you see in California?", SAM.gov has been 401 for
   19 days; only LA City RAMP (84 rows) and 4 CA MPI feeds (0 rows inserted, ever) remain.
6. **1980s-era awarded contracts in the New pile** — `Cambie Bridge Rehabilitation`, `PS20181561` — plus
   mangled `#` characters where a real character should be.
7. **97% of opportunities have no estimated value, 95% no city.** Any attempt to sort or map by value or
   geography will look empty.
8. **"Looks unfinished"**: 472 of 921 active opportunities have no submission deadline at all, and 19 are
   past deadline but still `New`.

---

## 9. To-do register

| Item | Size | Tag | Why it matters |
|---|---|---|---|
| Hide or filter `BDALERTS-*` digest rows from the Hub default view | S | `BEFORE-DEMO` | Risk #2; a view-level filter is enough, no ingest change needed |
| Collapse `BCBID-*` / `BCBIDENG-*` same-tender duplicates in the Hub view | S | `BEFORE-DEMO` | Risk #1, the most visible defect on screen |
| Set the Hub default filter to `Status=New AND deadline in future` | S | `BEFORE-DEMO` | Kills risks #6 and #8 in one change |
| Rotate the SAM.gov API key on KOR-APP01 | S | `BEFORE-DEMO` | Restores the only US federal source before a SoCal demo |
| Decide and rehearse the answer to "is the AI research live?" | S | `BEFORE-DEMO` | Discoverable on screen; must not be discovered by MVE first |
| Fix `BdResearchQueueBuilderJob` (stuck `NextFireAtUtc` 2026-07-19) | M | `SOON` | Root cause of the dead research layer |
| Deploy current `develop` to APP01 (prod is at 2026-07-18) | S | `SOON` | Post-Jul-18 Data fixes are not live |
| Call `FindPossibleDuplicatesAsync` on the ingest path, not just the entry UI | M | `SOON` | Fixes duplication at source rather than hiding it |
| Populate `DeltekClientId` — promote `BdDeltekLinkDryRunJob` past dry-run for the 8 auto-link matches | M | `SOON` | Unlocks the dormant scoring block at `RuleBasedOpportunityScoringService.cs:101` |
| Link `KorPursuits` (177 Won / 85 Lost) to `Opportunities` — currently 0 of 1,075 linked | L | `SOON` | Closes the outcome→scoring feedback loop; the real prize |
| Reclassify `COVAWARD-*` as historical awards, not New opportunities | S | `SOON` | 80 stale rows in the active list |
| Replace the 27 empty catches in `Ingestion/Scraping` with logged degradation | M | `SOON` | Still-open item from the 2026-07-01 audit; silent under-collection |
| Drop `opportunities_app` from `db_owner`; revoke its `KorStandards` reader/writer roles entirely | S | `SOON` | No code change; the service needs neither (only DDL is a temp table, and nothing references `KorStandards`). Biggest blast-radius reduction per minute — §5.9 |
| Then move to `Trusted_Connection=True` as `KOR\app-admin` and drop the SQL login | M | `SOON` | Removes the plaintext secret rather than hiding it — nothing left to store, rotate or leak |
| Backfill historical `~WDEF~` (won) pursuits from Deltek | M | `SOON` | The won-transition sweep only catches *future* conversions, so live win history is still 0; would replace reliance on the frozen May import |
| Fix CS8629/CS8604 in `Crm/SqlCrmEngagementStore.cs:107-109`; upgrade AngleSharp | S | `LATER` | Warning hygiene + known advisory |
| Integration tests for `Awards/` and `Ingestion/Providers/` | L | `LATER` | The 3% coverage gap where it actually matters |
| Retire or document `Kor.Opportunities.Capture` | S | `LATER` | Unused since May; APC works without it |

---

## 10. Verdict

**Demo-able with care — and it is a stronger module than its data quality suggests.** The machinery is real
and running: 111 sources polled continuously, the Worker heartbeating as I write this, 139,472 awards, a
canonical org graph that is provably duplicate-free, and a self-diagnostic health audit that catches
dead-but-green sources — an unusually mature control that most teams never build. Build is clean, 96 tests
pass, and there is not a single `TODO` or hardcoded secret in 66,000 lines.

The risk is not that it will crash; it is that **the first screen MVE sees is the worst artifact in the
system**. The top-scored opportunity on the board is the same White Rock tender listed three times at three
different scores, one attributed to buyer "Unknown," directly beneath a row titled "APC – Notification of New
Postings." Those are view-level problems with view-level fixes, and all four `BEFORE-DEMO` display items are
S-sized.

**The single most important thing to fix is the Opportunities Hub default view** — dedup by tender, exclude
`BDALERTS-*`, and filter to open future deadlines. That one change removes demo risks #1, #2, #6 and #8. Rotate
the SAM.gov key in the same sitting, since a SoCal audience will ask about US coverage.

Two things must not be claimed on screen: **the AI research layer** (silently producing nothing since
2026-06-27, `considered=0` daily) and **any win-rate or pipeline-conversion number** (0 Won, 0 Submitted, 1
Pursuing, 1 opportunity ever assigned an owner). Both are visible in the app's own Job Run History window, so
they are better pre-empted than discovered. Demo the ingestion breadth, the awards corpus and the entity
graph — those are genuinely strong and genuinely working.

---

## Appendix — reported facts checked (lead #4) and prior-audit items (lead #5)

**Reported facts — verify, do not assume:**

| Claim | Verdict | Evidence |
|---|---|---|
| Schema is `opportunities.*` | ✅ **CONFIRMED** | `QUERIED` — sole schema, 67 base tables |
| Canonical dedup job is disabled | ✅ **CONFIRMED, and for good reason** | `READ: Worker/Services/CanonicalOrgDedupJob.cs:10-60` — retired 2026-06-15, registered no-op, gated by `CanonicalOrgDedupEnabled` (default false). Not in `JobSchedules` `[QUERIED]`. Dedup is supervised CLI-only via `tools/BdCanonicalDedup` (1,765 LOC). Deliberate: the old job's FK list had drifted ~10 FKs behind schema |
| `IntelPerson` key = `SHA1(email)` since migration 255 | ✅ **CORRECT — and the cascade is correctly implemented; the *data* is the problem** | `READ: Intel/IntelNaturalKey.cs:15-45` — `ComputePerson` is a 4-tier cascade (email → LinkedIn URL → `name\|org:{id}` → name), matching the documented contract. The new information is a **data-quality measurement**, not a code correction: `QUERIED` — **7,578 of 12,974 rows (58%) have no email at all**, so most rows never reach tier 1; and the identity guarantee leaks in practice — **211 email addresses map to more than one `NaturalKey` across 444 rows**, plus 469 duplicate normalized-name groups (513 redundant rows). Correct cascade, colliding data |
| Deltek holds no won/loss signal | ✅ **CORRECT — I initially reported this as wrong and I was wrong; retracted** | `QUERIED`, segmented by `ExternalSource`, which is what settles it: **`Deltek.PR`** (the live recurring sync) = Pursuing 178 / Lost 85 / Declined 8 / **Won 0**; **`Deltek.PRProposals`** (live) = Considering 350, zero outcomes; **`Deltek.CustomProposal`** = Won 177 / Submitted 259 / Pursuing 18 — and all 454 of its rows were created inside a 28-second window on **2026-05-23** with `UpdatedAtUtc` never advancing since, i.e. a **one-time hand-curated import, frozen**. `LostToName` is populated on **3** of 85 losses. So: **the live Deltek feed has produced zero wins; the entire 177-win history is a one-off manual import.** My earlier "STALE-MEMORY" flag on this was incorrect |
| KPIs join via `DeltekClientId` | ❌ **WRONG in practice** | `QUERIED` — **NULL on 2,599 of 2,599** opportunities; no writer exists in Data/Ingestion or Worker `[RUN: grep]`. Separately, **0 of 1,075 `KorPursuits` link to an `Opportunity`** — the won/loss data exists but is fully disconnected from the live pipeline |

**One refinement on the Deltek retraction, because the usual explanation is now itself out of date.** The
standing account of *why* the live feed yields no wins is that `MapStage` has no `Won` branch. **That is no
longer true.** `MapStage` has moved to `KorPursuitDeltekSyncJob.cs:254-266` and **line 263 does map to Won**:
`if (string.Equals(stage, "~WDEF~", …)) return PursuitStages.Won;` — added **2026-07-11** in commit
`325c00e2` *"one pursuit = one row + won-transition sweep for Deltek sync (audit-v2 #11)"* `[RUN: git log -S]`,
with a comment recording that this Deltek has no `Won` stage code at all: a won pursuit's `Stage` becomes
`~WDEF~` when it converts to a project (*"verified live: 9,296 ~WDEF~ rows, zero 'Won' rows"*). A
won-transition sweep at lines 81–95 promotes them.

That fix **is deployed** (added 2026-07-11; the running binary is 2026-07-18) and the job has succeeded daily
since — yet `Deltek.PR` still shows **0 Won** after ~6 weeks `[QUERIED]`. The reason is the sweep's shape: it
only promotes pursuits *already tracked locally as open* whose Deltek stage later flips to `~WDEF~`, and the
two pull queries **exclude** `~WDEF~`, so pursuits that had already converted before 2026-07-11 were never
ingested to be promoted. It captures future conversions only. **Conclusion unchanged — the live feed has
produced no wins — but the cause is a backfill gap, not a missing code branch.** A one-time backfill of
historical `~WDEF~` rows would populate real win history from the live source and reduce dependence on the
frozen May import. Size: M.

### A. The 2026-05-30 series (Rounds 37–42) — six documents, 764 lines `[DOC]`

`BD-AUDIT-20260530.md` (T1=4, T2=2, T3=2, T4=3, T5=1, T6=1) and `-R2.md` (T1=5, T2=1, T3=2, T4=3, T6=1, T7=1)
are full findings sets; `-R3` … `-R6` are verification rounds that re-check the previous round and add new
findings. Roughly half of all findings sit in `Kor.Operations.App/PMTools/` (PM Capacity / Workload Meeting)
and belong to a different module — I triaged only the BD-Brain-relevant ones below.

**The series is essentially closed.** R3 verified 13/13 of R2's anchors; R4, R5 and R6 each re-verified all
prior rounds as clean. Its two named "single biggest risks" are both fixed, and I confirmed the substantive
BD findings directly in current code:

| Finding | Status Aug 2026 | Evidence |
|---|---|---|
| **R1-T1.001** (*"single biggest risk"*) `BdCanonicalDedup` merges explicit pairs with no name-similarity gate — the Abbotsford-SD / Alterra class of bad `SurvivorId` | ✅ **FIXED** | `RUN: grep` — `tools/BdCanonicalDedup/Program.cs:316` carries the comment *"T1.001 similarity-gate helpers (post 2026-05-30 Abbotsford incident)"*, with `IsAllowlistedNonSimilar()` (325) and a source-controlled `dedup-non-similar-allowlist.csv` — precisely the suggested fix shape |
| **R2-T1.002** (*"single biggest risk"* of R2) migration 48's BD-tracking CanonicalOrg FK missing from the dedup repoint list | ✅ **FIXED** | `DOC`-verified in R3/R4/R6 and re-checked: `FkTargets` includes `("CrmEngagements","BuyerCanonicalOrgId")` |
| **R1-T1.003** Relationships view ignores MPI `StructuralEngineerCanonicalOrgId` / `GeneralContractorCanonicalOrgId` | ✅ **FIXED** | `RUN: grep` — `SqlCanonicalOrgStore.cs:244-245` now has a separate `EXISTS` per column (the recommended shape), each with `RetiredAtUtc IS NULL` |
| **R1-T1.004** MPI dashboard health counts omit `RetiredAtUtc IS NULL`, drifting upward with retired rows | ✅ **FIXED** | `RUN: grep` — `SqlBdDashboardStore.cs` now filters all three branches and carries the comment *"Audit T1.004: every MPI health metric must filter RetiredAtUtc IS NULL"* |
| **R6-T3.001** `BdCanonicalDedup` default output path cwd-relative → nested `dedupe-plan.csv`, reviewers inspect the wrong artifact | ✅ **FIXED** | `RUN: grep` — `Program.cs:13` header cites *"Round 45 (R6-T3.001)"* and now resolves the output dir by walking up to the repo root |
| **R6-T3.002** pair-merge discards `FkRepointsByTable`, so the summary reports zero FK repoints | ✅ **FIXED** | `RUN: grep` — `RunPairsMergeAsync` aggregates `result.FkRepointsByTable` into the summary at `Program.cs:491-494` |
| **R1-T1.002** `NormalizeName` only strips punctuation — `Acme Ltd.` vs `Acme`, `SD68` vs `School District 68`, `City of Vancouver` vs `Vancouver (City of)` still split | ⚠️ **STILL OPEN for live matching — and I measured its cost** | `RUN: grep` — `CanonicalOrgResolver.cs:577-591` is **byte-identical** to what the May audit quoted. A `NormalizeAggressiveKey` (608) *was* added with suffix stripping, but its own doc comment is candid: *"the resolver's own live-match tiers do NOT use this key … it does not by itself prevent the create-then-merge cycle."* **This is the mechanism behind the 769,290 retired tombstones in §5's note** — intake creates a near-duplicate, the supervised CLI retires it later. Detection and cleanup work; prevention does not |

Remaining PM-Tools findings (R3-T1.001 window leak, R4-T2.001/T2.002 priority-save races, etc.) are all
recorded as closed by R5/R6 and are out of scope for this module.

### B. The June–July series `[DOC]`

Baseline `BD-Completeness-Audit-2026-07-01.md`; all predate the code (Data 2026-08-02, Worker 2026-07-18), so
per rubric rule 2 every claim below was re-verified against live state.

| Their item | Status Aug 2026 | Evidence |
|---|---|---|
| #1 rec: source-staleness sentinel ("kills the dead-but-green class") | ✅ **SILENTLY FIXED** | `QUERIED` — `DataHealthAuditJob` §I emits `DEAD-GREEN` / `NEVER-PRODUCED` verdicts per source with last-produced vs last-run |
| #2 rec: persist relevance-gate rejects (were log-only) | ✅ **SILENTLY FIXED** | `QUERIED` — `RelevanceGateRejects` table, 11,535 rows, latest 2026-08-20 22:42, with reason breakdown |
| #3 rec: Surrey / Vernon / Cochrane fake-green tenants | ✅ **SILENTLY FIXED** | `QUERIED` — all six `BidsTenders_*`/`BidsTendersAwards_*` for those three are now `IsEnabled=0` |
| APC dead-but-green (portal moved to `/search`) | ✅ **FIXED, holding** | `QUERIED` — `APC_AllBuyers` succeeding, 76 inserts lifetime / 74 in 30d |
| Empty catches / silent pagination truncation in 6 scrapers | ❌ **STILL OPEN** | `RUN: grep` — 27 empty catches remain, all in `Ingestion/Scraping` (§5.8) |
| `Success=true, Inserted=0` ambiguity per-source | ⚠️ **MITIGATED, not fixed** | Detection now exists (sentinel), but the underlying run-status semantics are unchanged, and the audit still reports **21 stale sources** incl. `NEVER-PRODUCED` (`BidsTenders_PortCoq`, `Bonfire_Burnaby`, `Bonfire_CapilanoU`, `BidsTendersAwards_Coquitlam`) |
| MPI providers always record 0 inserted | ❌ **STILL OPEN** | `QUERIED` — all 6 `*MajorProjectsInventory` sources show `ins_total=0` across every run |
| BidsTendersAwards pinned at 100/run | ❌ **STILL OPEN** | `QUERIED` — award streams remain low-yield; no config change evident |
| Relevance-gate false negatives (French, proper-noun collisions) | ⚠️ **MEASURABLE NOW, NOT TUNED** | `QUERIED` — rejects persisted (so the bleed is finally reviewable); top reason `no building/structural/design signal` = 9,292 of 11,535. No evidence of a tuning pass |

**Lead #7 — is `Kor.Opportunities.Capture` dead code?** **It is unused, but it is not junk.** `[READ]` It is a
deliberate one-shot *operator* tool: launches headed Chromium so a human can clear Cloudflare Turnstile on
Alberta Purchasing Connection, then saves the storage-state JSON to
`\\KOR-APP01\C$\ProgramData\KorOperations\Opportunities\sessions`. It is referenced from no other project by
design (operator-invoked exe). **Evidence it is unused**: that sessions directory is **empty and has been since
it was created on 2026-05-21** `[QUERIED]`, while `APC_AllBuyers` ingests successfully anyway (76 rows) — i.e.
APC never ended up needing the captured session. Recommend documenting it as a break-glass tool rather than
deleting it, since the anti-bot problem it solves is likely to recur. Size: 177 LOC, one file.
