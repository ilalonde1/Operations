# C1 — Deltek Vantagepoint: Competitive Reality Check

**Prepared** 2026-08-20 · **Audience** KOR internal, ahead of the MVE technical demo
**Target** Deltek Vantagepoint (KOR is a Vantagepoint customer — some findings below are first-hand from KOR's own tenant)

**Prior art searched before writing this** (per repo rule 1): `docs/` for existing competitive material
(no `competitive/` directory existed; `docs/bd-ca-competitor-*` are market/firm maps, not vendor analysis),
`docs/audit-2026-08/00-INVENTORY.md` (mechanical inventory only, no vendor comparison), and the 19
`reference_deltek_*` / `project_deltek_*` memory files, which supplied the first-hand ODBC and
win/loss findings cited in §5 and §6. Nothing in the repo already answered this brief.

**Evidence tags**
- **VERIFIED** — primary source: deltek.com, help.deltek.com, Deltek release notes, Deltek press release, or KOR's own live system.
- **REPORTED** — secondary: Deltek partner/reseller, analyst, press, review site.
- **INFERRED** — my reasoning from the above, labelled as such.

---

## 1. Executive verdict

Deltek Vantagepoint in August 2026 is a **strong system of record with a shallow, deliberately-scoped
AI layer and a reporting stack that stops at the edge of the ERP.** Three things are simultaneously true:

1. **MVE's technical lead can truthfully say Vantagepoint already does a lot of the reporting.**
   WIP, AR aging, unbilled, earnings by project and office, utilization and forecast all exist as
   native standard reports, and dashparts can be built against project, employee, firm and account
   bases. Anyone who claims Vantagepoint "has no reporting" will be corrected in the room and lose
   the audience. *Do not make that claim.*

2. **The AI story is where the overselling lives.** Ask Dela is GA and included in the base licence,
   but Deltek's own help documentation states it cannot answer aggregate questions, works at WBS1
   only, holds a single record in scope, and remembers the context of only the last two questions.
   Dela Insights is a fixed catalogue of pre-built alerts, not a conversation. There is a live
   contradiction between that documentation and an August 2026 Deltek marketing article — see §3.4;
   it is the one claim I could not settle from the desk and the one to test live.

3. **Deltek's agentic roadmap puts Vantagepoint last.** On Deltek's own timeline, Deltek Proposals
   reaches GovWin IQ in Q1 2026, Costpoint in Q3 2026, and **Vantagepoint in Q3 2027**. Agentic
   Financial Close reaches Costpoint in Q3 2026, and **Maconomy and Vantagepoint in 2027**. There is
   no ProjectCon 2026 — the flagship event is now Deltek Elevate, **March 2027**. So the roadmap MVE
   might gesture at is 12–18 months out for their product line, and there is no conference between
   now and then at which it accelerates.

**The honest one-liner for the room:** *"Vantagepoint is our system of record and we're not replacing
it — we read from it. What we built is the layer Deltek has scheduled for 2027: conversational access
to the whole financial picture, and a BD pipeline that fills itself. Everything we'll show you runs
on top of Vantagepoint, not instead of it."*

**Biggest risk to KOR's positioning:** **Deltek PIM** (§7). It is a real, shipping product that files
Outlook email to projects with automatic metadata, searches across every project's emails, documents
and drawings, and keeps version history and an audit trail. If MVE already licenses PIM, the Newforma-
replacement demo needs a sharper framing than "we built email filing."

---

## 2. Current state

### 2.1 Version and cadence

| Fact | Detail | Tag |
|---|---|---|
| Newest release | **2026.3.5**, build **2026.3.5.712**, released **19 Aug 2026** (the day before this report) | VERIFIED — [2026.3 release notes index](https://help.deltek.com/product/Vantagepoint/2026.3/ReleaseNotes/) |
| Cadence | "Vantagepoint uses a quarterly release cadence for faster delivery of high-quality features and other software changes", with roughly biweekly patch builds between | VERIFIED — same page |
| Feature releases in the last 12 months | 2025.4 (29 Sep 2025, 2025.4.0.143) · 2026.1 (19 Dec 2025, 2026.1.0.163) · 2026.2 (13 Mar 2026, 2026.2.0.156) · 2026.3 (15 Jun 2026, 2026.3.0.152) | VERIFIED — [2025.4](https://help.deltek.com/product/Vantagepoint/2025.4/ReleaseNotes/), [2026.1](https://help.deltek.com/product/Vantagepoint/2026.1/ReleaseNotes/DeltekVantagepoint20261ReleaseNotes.htm), [2026.2](https://help.deltek.com/product/Vantagepoint/2026.2/ReleaseNotes/DeltekVantagepoint20262ReleaseNotes.htm), [2026.3](https://help.deltek.com/product/Vantagepoint/2026.3/ReleaseNotes/DeltekVantagepoint20263ReleaseNotes.htm) |
| Version naming | Deltek moved from 7.x to calendar versioning after 7.2; the release index runs 2.0 → 7.2 → 2025.1 → 2026.3 | VERIFIED — [release notes index](https://help.deltek.com/product/Vantagepoint/ReleaseNotes/) |

**Note on documentation drift:** much of the *feature* help on help.deltek.com is still published under
the old **7.0 / 7.2** trees. I could not locate a 2026.x-versioned copy of the Ask Dela usage page
(`/product/Vantagepoint/2026.3/Ask_Dela_UseAskDela.html` and the 2025.4 equivalent both return HTTP
404; learning.deltek.com bundle pages render client-side and returned no text). This matters for §3.4.

### 2.2 What shipped in the last ~12 months

**2025.4 — 29 Sep 2025** (VERIFIED, release notes)
- Dashpart Designer: Organization field for multi-company comparison; Grid Type field for Employee and Firm bases; Calculated Fields gain a "Calculation Type" (totaled / constant / averaged) on Account, Project and Employee bases.
- **PSA General Ledger API endpoints exposed** for the Accounting module, gated by a new "Allow Access to PSALedger API" role checkbox; GL summary endpoints added.
- Report loading improvements (selective reload, footer version removal).
- No new Ask Dela capability — the only Dela mention is a defect fix.

**2026.1 — 19 Dec 2025** (VERIFIED, release notes)
- **Chat with contract documents using Ask Dela** — upload/interact with PDF, Word, RTF, TXT from the Contracts grid.
- Dela moved from a standalone chat window to a **right-side slide-out panel**.
- **Ask Dela gains aggregate questions on Contacts and on Firm records**, including user-defined fields and UDF grids — "counts, lists, totals, averages, and year-over-year comparisons".
- **Dela Insights tab** introduced, organised by persona: Business Development, Project Management, Time & Expense, System Administration, Finance & Accounting. Timesheet Anomaly Detection for approvers.
- Three new PM Insights covering earned revenue, spending and revenue forecasting.
- New **Project Schedules** system dashpart; drill-to dashpart filter inheritance.
- **General ledger transaction API endpoints** for the Accounting module.

**2026.2 — 13 Mar 2026** (VERIFIED, release notes)
- **Harmony UI** — refreshed interface, opt-in at Settings > General > Opt-in Features. Left nav icons, right context menu carrying Ask Dela, Insights, Notifications, Help.
- **Policy Documents application** (Settings > Dela > Policy Documents) enabling "AI-driven compliance validation and approval workflows" via RAG.
- Company Policy Insights and Natural Language Insights as expense-approval workflow steps producing "AI-generated recommendations for each configured workflow step". Requires Ask Dela enabled plus user subscription.
- Ask Dela feedback (thumbs-up); Dela Insights filtering (All / This Application / This Record); consolidated **Settings > Dela** section.
- Employee Document Templates — export employee data into Word documents (resumes, bios).
- Billing Labor Rate Tables API endpoints (full CRUD).
- Reporting currency rework; new Compensation and Estimate Fee fields in PRSummary.
- **Legacy Web Services removed, replaced by webhooks.** UKG Pro integration.
- Dashboards: performance work, Employee Type column on Project Detail, comment columns, and a **"Performance Recommendation Message" when saving a dashboard with 8 or more dashparts**.

**2026.3 — 15 Jun 2026** (VERIFIED, release notes)
- **Dela Insights becomes a dedicated application** at Settings > Dela > Insights. Time & Expense insights for timesheet, absence and expense approvers. New insight: "GL Accounts (Non-Revenue), Trending Higher". Insights indicator in Project and Firm headers.
- **Resource Planning API** — verbatim: *"A new public API enables integrations, such as Vantagepoint-Replicon, to securely retrieve employee and generic resource planning assignments from project plans. The API provides detailed mappings, including assignment periods, planned hours, and project hierarchy data, while supporting advanced filtering, pagination, and delta queries."* Visibility restricted by role security.
- AP Payment Export POST endpoint; FileStore API replacing DeleteFile/DeleteFiles.
- Dashboards — verbatim: *"Dashparts now generate a dedicated scheduled refresh process job for each user/role with access to the dashpart, reducing the time required to load and display refreshed dashparts on dashboards."* Plus opt-in paged loading for Project and Project Detail table dashparts, and dashpart descriptions.
- Project Forecast Report revenue-allocation radio buttons; user audit reports now track Dela Insight subscription changes.

**2026.3.5 — 19 Aug 2026** (VERIFIED, release notes): one feature — **Draft Invoice Approval Workflows** (opt-in, extends the Approval Workflow Engine to invoice review with "multi-step, tiered, and conditional approvals") — plus three defect fixes. **No AI, dashboard, API or CRM content.**

**Reading of the arc (INFERRED):** the last four releases are overwhelmingly *approval workflow and
insight-alert* work — timesheets, expenses, invoices, policy compliance — plus incremental API
surface. That is Deltek hardening the ERP's control plane. It is not the analytics or BD-automation
arc, and it does not intersect what KOR built.

---

## 3. Dela — deep dive

### 3.1 What Dela is, precisely

Deltek uses "Dela" as an umbrella brand for **all** its AI, not a single feature. On Deltek's own
platform page it is "Your AI Orchestrator" and "an AI-powered intelligent business companion", split
into four capability groups: generate and automate smart content; enable intelligent exploration
(this is Ask Dela); predict project and organizational success; streamline and simplify task
execution. It spans GovWin IQ, Costpoint, Vantagepoint and Replicon.
*(VERIFIED — [deltek.com/products/platform/dela](https://www.deltek.com/products/platform/dela/), fetched 2026-08-20)*

Inside Vantagepoint the brand covers distinct things, and conflating them is how the overselling happens:

| Component | What it actually is | Status |
|---|---|---|
| **Ask Dela** | Conversational natural-language query + document chat + email drafting | **GA** since Vantagepoint 7.0 |
| **Dela Insights** | A fixed catalogue of pre-built AI-generated alerts, subscribed per persona. Not conversational. | **GA**, expanded each release since 2026.1 |
| **Smart Summaries / ICR** | Client and Project summary generation; image capture of business cards, receipts, AP invoices | **GA** |
| **Dela Agent Workforce** | Autonomous agents for BD, PM, billing, accounting | **Announced only** — see §8 |

### 3.2 GA status, licensing, enablement

- Ask Dela is **GA, not preview.** It shipped in Vantagepoint 7.0, replacing "Hey Deltek!". *(VERIFIED — [Deltek, "Introducing Ask Dela for Vantagepoint"](https://www.deltek.com/resources/articles/vantagepoint-ask-dela/), published 6 Jun 2024)*
- **Included in the base licence.** Deltek: the digital assistant for Costpoint and Vantagepoint "will come with a license for the core product", and "Many of Deltek Dela's capabilities will be embedded in existing Deltek products and will be part of the license fee." *(VERIFIED — [Dela platform page](https://www.deltek.com/products/platform/dela/))* Some Dela capabilities elsewhere in the portfolio (e.g. Replicon Zero Time) are charged separately.
- **Off by default.** Enabled at Settings > General > Options → "Enable Deltek Dela™ (A Generative AI Feature)", then separately **per security role**. *(VERIFIED — [Enable Deltek Dela](https://help.deltek.com/product/vantagepoint/7.0/Deltek_Dela_EnableDeltekDela.html))*
- **Dela Insights requires Ask Dela to be enabled first.** *(VERIFIED — 2026.3 release notes: "To use Dela Insights, the Ask Dela feature must be enabled in Settings > Dela > Options")*
- **LLM vendor:** not disclosed on any Deltek page I could reach. A search-index snippet of the Deltek help describes Ask Dela as an "Open AI-powered digital assistant", but I could not confirm that string on a live primary page. **REPORTED / could not verify.** Deltek publishes no statement I could find on whether customer data is used for model training. **Could not verify** — searched the Dela platform page, the Ask Dela article, the Enable Deltek Dela help page and the Nov 2025 AI-strategy blog; none address it.

### 3.3 Published limitations — the load-bearing evidence

Deltek's own "Use Ask Dela" help page lists these limitations **verbatim**:

1. *"You cannot query Ask Dela for aggregate data, such as information about the top projects by revenue."*
2. *"Ask Dela can provide information only for WBS1 (work breakdown structure level 1) in project summaries and hub data"* — you cannot query WBS2/WBS3 or "financials for specific phases or tasks".
3. *"Ask Dela currently supports conversations only about a single record."*
4. *"it remembers the context of only the last two questions."*
5. *"US English is currently the only supported language."*
6. Unsupported data: *"file grids, link grids, user-defined hubs, user-defined grids, and user-defined fields"*.
7. *"Ask Dela does not currently support 'how-to' inquiries"* — it has no access to Vantagepoint Help.

*(VERIFIED — [Use Ask Dela, 7.0](https://help.deltek.com/product/Vantagepoint/7.0/Ask_Dela_UseAskDela.html) and [7.2](https://help.deltek.com/Product/Vantagepoint/7.2/Ask_Dela_UseAskDela.html), both fetched 2026-08-20. Both trees carry the identical list.)*

**Known partial lifts (VERIFIED from release notes):** 2026.1 added aggregate questions on **Contacts**
and on **Firm records** — including UDFs and UDF grids — with "counts, lists, totals, averages, and
year-over-year comparisons". That lifts limitation 1 for two CRM hubs and lifts limitation 6 for UDFs.
**No release note in 2025.4, 2026.1, 2026.2 or 2026.3 extends aggregation to projects or to financials.**

### 3.4 The unresolved contradiction — read this before the demo

A Deltek marketing article dated **5 Aug 2026** ("Your AI Project Manager Has Arrived: What Dela Does
for PMs in Vantagepoint") gives as a live Ask Dela example: **"What are the top 10 projects by JTD
revenue?"** — and asserts *"Dela is live in Vantagepoint today. These aren't roadmap promises.
They're capabilities your team can use right now."*
*(VERIFIED as a quote from the page — [deltek.com/resources/articles/dela-vantagepoint-project-managers](https://www.deltek.com/resources/articles/dela-vantagepoint-project-managers/), fetched 2026-08-20)*

That example is **verbatim the thing the help page says is impossible** ("top projects by revenue").
Three possible explanations, none confirmed:

- (a) Project-level aggregation shipped and the help tree under 7.0/7.2 is stale — plausible, since the whole feature help tree predates calendar versioning.
- (b) The marketing copy is aspirational and outran the product.
- (c) It works for a narrow set of summary fields but not generally.

**I could not resolve this from public sources.** What I searched: help.deltek.com under 2026.3,
2026.2, 2026.1, 2025.4 and 7.2 for a current Ask Dela usage page (2026.x paths return 404); all four
release notes for project-aggregation language (absent); learning.deltek.com bundles (client-side
rendered, no text returned); Deltek's Dela, CRM and Vantagepoint product pages.

**Action before the demo — KOR is a Vantagepoint customer and can settle this in five minutes.**
Enable Ask Dela on a test role in KOR's tenant and ask it, in order:

1. "What are the top 10 projects by JTD revenue?"
2. "Which PMs have the most WIP right now?"
3. "Show me AR over 90 days by client."
4. "Which projects are earning ahead of invoicing this quarter?"

Screenshot the answers. Whatever it returns is the true baseline, and it is the only piece of this
report that is not already settled. **Do not stand in front of MVE asserting Ask Dela cannot do
aggregates until you have run 1–4 yourself.**

### 3.5 What Dela Insights actually is

Not a conversation — a **subscription to pre-built alerts**. The published catalogue as of 2026.3:

- Earned Revenue Ahead of Invoicing
- Spending Ahead of Earned Revenue
- Earned Revenue Ahead of Spending
- Expense Policy Violations
- Timesheet Anomaly Detection
- Expense Company Policy / Expense Natural Language insights
- GL Accounts (Non-Revenue), Trending Higher
- Time & Expense approval insights (timesheets, absence requests, expense reports)

*(VERIFIED — 2026.1, 2026.2, 2026.3 release notes; [Dela for PMs article](https://www.deltek.com/resources/articles/dela-vantagepoint-project-managers/))*

**INFERRED:** this is a curated, vendor-authored alert set. You subscribe; you cannot author a new
insight, define your own metric, or ask a follow-up question of one. It is genuinely useful and it is
a different category of thing from a conversational analyst.

---

## 4. Reporting and BI reality

### 4.1 What is genuinely native — concede this

Vantagepoint ships a substantial standard report library. Confirmed report names on help.deltek.com:
**AR Aged**, **Unbilled Detail and Aging**, **Office Earnings**, **Project Earnings**, **Project
Progress**, **Key Financial Metrics**, **Project Forecast Report**.
*(VERIFIED — help.deltek.com report pages: [AR Aged](https://help.deltek.com/Product/Vantagepoint/3.0/VP_rept_AR_Aging.html), [Unbilled Detail and Aging](https://help.deltek.com/product/Vantagepoint/2.0/VP_rept_Unbilled_Detail_Report.html), [Project Earnings](https://help.deltek.com/Product/Vantagepoint/3.5/VP_rept_Project_Earnings.html), [Key Financial Metrics](https://help.deltek.com/product/Vantagepoint/3.5/VP_rept_Key_Financial_Metrics.html), [Project Reporting overview](https://help.deltek.com/Product/Vantagepoint/7.2/con_pro_project_reporting.html))*

Dashboards are real too: dashparts built on Project, Project Detail, Employee, Firm and Account
bases, with calculated fields (totaled / constant / averaged), grouping, drill-to-dashpart with
filter inheritance, and a Project Schedules system dashpart added in 2026.1.

**So: yes, a Vantagepoint user can get WIP, AR aging, unbilled, utilization and earnings without a BI
tool.** Any KOR claim to the contrary is false and will be caught.

### 4.2 Where it stops — the real gaps

| Gap | Evidence | Tag |
|---|---|---|
| **Dashparts are refreshed on a schedule, not queried live.** 2026.3 verbatim: *"Dashparts now generate a dedicated scheduled refresh process job for each user/role with access to the dashpart."* | 2026.3 release notes | VERIFIED (quote) / INFERRED (implication: what you see is as fresh as the last refresh job, not as fresh as the ledger) |
| **Complexity is actively discouraged.** 2026.2 added a "Performance Recommendation Message" when a user saves a dashboard with **8 or more dashparts**. 2026.3 added opt-in paged loading for large project datasets. | 2026.2, 2026.3 release notes | VERIFIED |
| **No cross-hub custom metric authoring.** Calculated fields operate within a dashpart base. There is no documented way to write SQL, define a firm-wide derived metric (earned-vs-invoiced exposure, at-risk score, collections exposure), or blend Deltek data with a non-Deltek source inside Vantagepoint. | Searched help.deltek.com dashboard/dashpart docs and all four release notes; no such capability documented | INFERRED / could not verify any contrary evidence |
| **A whole partner industry exists to fill the gap.** Full Sail Partners sells Informer BI and Power BI enablement into Vantagepoint, positioning it as letting teams "explore data, drill into details, and make faster decisions without relying solely on Deltek for reporting", because "too often, that data lives in static reports, spreadsheets, or tools that don't fully connect." | [fullsailpartners.com/business-intelligence-solutions](https://www.fullsailpartners.com/business-intelligence-solutions) | REPORTED |
| **Customers say the reporting is hard.** "The reporting--it's very clunky and difficult to do, not intuitive at all." (reviewer, June 2025). Other 2025–2026 reviewers describe a dated UI and a steep learning curve. | [softwareadvice.com Vantagepoint profile](https://www.softwareadvice.com/reporting-tools/deltek-vantagepoint-profile/) | REPORTED |

### 4.3 The honest framing on reporting

The gap is **not** "Deltek can't show you WIP." It is:

- **Freshness** — a scheduled dashpart refresh versus a live read.
- **Authorship** — you consume Deltek's metrics; you don't define your own. Earned-vs-invoiced,
  collections exposure and at-risk scoring as *KOR defines them* are not dashparts you can build.
- **The last mile** — the moment the question crosses hubs, crosses a WBS level, or needs a metric
  Deltek didn't ship, the answer is a BI tool, a partner engagement, or an export to Excel.
- **Conversation** — a report answers the question you thought to run. KOR's MCP layer answers the
  question you thought of second.

---

## 5. Data access — what a customer can legitimately build on

This is the section that legitimises KOR's whole suite: **everything KOR built reads Deltek through
channels Deltek sells and documents.** Nothing here is a hack.

**REST API** *(VERIFIED)*

- Live, versioned, publicly documented at [vantagepointapi.deltek.com](https://vantagepointapi.deltek.com/) — currently **2026.3**. Postman collections published in [Deltek's public workspace](https://www.postman.com/deltekeng/deltek-s-public-workspace/documentation/omlh9j7/deltek-vantagepoint-2026-1-api).
- Purpose, per Deltek help: *"Use the Vantagepoint RESTful API to build custom applications that interact with Vantagepoint."* OAuth 2.0 with `password`, `refresh_token` and `authorization_code` grants.
- **Coverage has been expanding release by release, and the financial surface is recent:** PSA General Ledger endpoints in 2025.4 (Sep 2025), GL transaction endpoints in 2026.1 (Dec 2025), billing labor rate tables in 2026.2 (Mar 2026), Resource Planning + AP payment export + FileStore in 2026.3 (Jun 2026).
- **INFERRED, and worth saying out loud:** before December 2025 you could not have built a financial reporting layer on the REST API alone — the GL wasn't exposed. Any firm that built real financial tooling on Vantagepoint before then went through ODBC. That is exactly what KOR did.
- Legacy SOAP Web Services **removed** in 2026.2, replaced by webhooks.

**ODBC direct read** *(VERIFIED — first-hand from KOR's live tenant, not from a vendor page)*

- SYSTEM DSN `Deltek`, **DataDirect Hybrid Data Pipeline 4.6** → `vp-ca-hdp01.prd.mydeltek.com:443` over HTTPS.
- Catalog prefix `C0000052267P_1_KOR00000000`; SQL Server backend, so T-SQL works (`TOP`, `LTRIM/RTRIM`, `CAST`).
- Used read-only in KOR's architecture; the MCP `query_kor_data` path is gated to block DELETE/UPDATE.
- Known quirks worth not rediscovering: `OdbcType.Date` binds to nothing (silent zero rows — use `DateTime`); `Account` comparisons need `LEFT(LTRIM(RTRIM(...)),4)` normalisation.
- REPORTED (Full Sail): ODBC is "the most common and cost-effective setup for most firms", refreshing "a few times per day", with "full access to all tables".

**Data at Your Service (Snowflake)** *(announced ProjectCon 2025)*

- Deltek's new offering "connects Vantagepoint to a Snowflake data warehouse and then to any third-party tools". *(REPORTED — [Full Sail ProjectCon 2025 takeaways](https://www.fullsailpartners.com/fspblog/our-crews-top-takeaways-from-deltek-projectcon-2025), 11 Dec 2025)*
- Refreshes "about every 30 minutes", "provides a more structured dataset", "comes at a higher investment and is typically better suited for larger firms" — Full Sail frames it for firms of **250+ employees**. *(REPORTED — Full Sail)*
- **Pricing: could not verify.** Deltek publishes no price. Searched deltek.com, help.deltek.com, and third-party pricing aggregators.
- **INFERRED, and relevant to the room:** KOR is ~40 staff. MVE is larger. If MVE is a DaaS candidate and KOR is not, the correct KOR line is that its architecture achieves 30-minute-class freshness *without* the Snowflake tier — not that Snowflake doesn't exist.

**A structural data gap in Deltek, verified in KOR's own tenant** *(VERIFIED — first-hand)*

- `PR.Stage` has values `InPursuit`, `LOST`, `DNP`, blank — **there is no Won state.** The live Deltek pursuit sync produces 173 Pursuing, 83 Lost, 8 Declined, **0 Won**.
- `PR.LostTo` is populated on **3 of 79** lost pursuits. `ClosedReason` on **0** rows.
- **Deltek Vantagepoint structurally does not record who beat you.** Win rate and competitive displacement cannot be derived from Vantagepoint as shipped. This is a first-hand, demonstrable fact about a system MVE presumably also runs, and it is one of the strongest single points KOR can make.

---

## 6. CRM and BD reality

| Question | Answer | Tag |
|---|---|---|
| Does Vantagepoint CRM natively ingest public tender/procurement feeds? | **No.** The CRM & Pipeline Management product page describes managing client relationships, tracking pursuits and coordinating proposals. It does not mention tender feeds, third-party enrichment, deduplication, or AI research. | VERIFIED — [CRM & Pipeline Management](https://www.deltek.com/products/erp/vantagepoint/crm-and-pipeline-management/), fetched 2026-08-20 |
| Is there *any* opportunity feed? | **Yes — via GovWin IQ, a separate paid subscription.** Configured at Utilities > Integrations > GovWinIQ with a client ID and secret. Import runs from the Projects hub Actions menu; optional automatic update of imported opportunities **once every 24 hours**. | VERIFIED — [Connect to GovWin IQ](https://help.deltek.com/Product/Vantagepoint/7.2/CRM_Basics_ConnectToGWIQ.html), [GovWin IQ integration form](https://help.deltek.com/Product/Vantagepoint/6.0/ST_Util_Int_and_Imp_GovWinIQ_Tab.html) |
| What does the integration require? | **CRM or CRM Plus module must be activated**, plus a GovWin IQ subscription. Without implementation, "this option is not available". | VERIFIED — same |
| What does GovWin IQ cover, and what does it cost? | Federal, SLED **and Canada**; scoped by market, seats and add-on modules. No published list price. Third-party aggregators report entry around **$200/user/month**, real 2026 quotes roughly **$12k/yr single seat to $42k+/yr enterprise**, average deal ~$29k/yr. | REPORTED — [civiciq GovWin pricing 2026](https://civiciq.com/blog/govwin-iq-pricing-2026), [itqlick GovWin pricing](https://www.itqlick.com/govwin-from-deltek/pricing) |
| Entity resolution / dedup / firm enrichment? | **Not documented as a native capability.** No mention on the CRM page or in the 2025.4–2026.3 release notes. | VERIFIED absence / INFERRED |
| AI research or scoring on pursuits, shipping today? | **No.** AI Pursuit Scoring, AI Proposal Builder and always-on client-intelligence monitoring are described by Deltek in **future tense** — "AI *will* analyze your firm's historical win factors", "an always-on intelligence agent *will* monitor client news". | VERIFIED — [How the Dela Agent Workforce is Redefining Project Delivery](https://www.deltek.com/en/blog/how-the-dela-agent-workforce-is-redefining-project-delivery), 19 Nov 2025 |
| What CRM-adjacent AI *did* ship? | Employee Document Templates (resumes/bios into Word) in 2026.2; Ask Dela aggregate queries on Contacts and Firms in 2026.1; business-card capture via ICR. | VERIFIED — release notes |

**The Canada point matters.** GovWin IQ does cover Canada — so "Deltek can't see Canadian public work"
is **false** and must not be said. The accurate statements are: it is a separate five-figure annual
subscription; it is public-procurement only; it does not cover the architect-led private work that is
KOR's actual channel; and it delivers opportunity records, not researched, entity-resolved, scored
pursuits with generated dossiers.

---

## 7. Deltek PIM — the sleeper threat to the Newforma-replacement demo

Deltek sells **Project Information Management (PIM)**, listed under "delivery assurance", not as a
Vantagepoint module. Verbatim from Deltek's product page: it gives A&E firms "one secure place to
manage every email, document, and drawing", with

- *"File project emails directly from Outlook into Deltek PIM with a single click"*
- *"Automatically applies metadata — project name, sender, date — to emails and documents on filing"*
- *"Search instantly across all projects for any email, document, or drawing"*
- *"Full version history, check-in/check-out file locking, and a complete audit trail"*
- *"Index files stored on shared network drives, OneDrive, and Autodesk Docs"*
- *"Deltek PIM integrates with Deltek Vantagepoint and Deltek Ajera, connecting project information with project and financial data."*

*(VERIFIED — [Deltek PIM](https://www.deltek.com/products/delivery-assurance/project-information-management/), fetched 2026-08-20. Help documentation exists at [help.deltek.com/product/PIM/](https://help.deltek.com/product/PIM/) with versions 20.0 through **26.0**; topics include "Documents and Emails", "PIM Email Management Users", "PIM Teamwork". The PIM help index carries the note "The Deltek PIM online help is currently work in progress.")*

**What I could NOT verify about PIM** (searched the product page and the PIM 26.0 help index): a
client-facing **transmittal** feature, **download tracking / delivery receipts**, or **SharePoint** as
the document store — PIM indexes network drives, OneDrive and Autodesk Docs, with SharePoint not
mentioned. Deltek's own PIM help topic list contains no "Transmittals" entry.

**INFERRED positioning consequence:** PIM covers the *filing and search* half of KOR's Newforma
replacement credibly. It does **not**, on published evidence, cover the *outbound transmittal with a
self-hosted redirector that logs who downloaded what and when*, and it does not put the firm's
documents in SharePoint where the rest of the M365 tenant already lives. Lead the Newforma demo with
the **transmittal + download telemetry + SharePoint-native** story, not with email filing. And ask
MVE early and neutrally whether they run PIM — the answer reshapes that demo.

---

## 8. Roadmap — what Deltek has actually committed to

**ProjectCon 2025** — 10–12 Nov 2025, Gaylord Rockies, Denver. Press release 12 Nov 2025, "Deltek
Unveils Intelligent Platform Innovations that Elevate the Project Lifecycle": *"At the center of the
platform is Dela, which embeds intelligence into the flow of work."* Announced Deltek Proposals
("designed to help reduce proposal creation time by as much as 60–70%"), Deltek PPM Enterprise Risk,
Deltek Agentic Financial Close, Replicon/UKG Pro integrations, expanded Vantagepoint Payments, and
Data at Your Service. *(VERIFIED — [Deltek press release](https://www.deltek.com/company/news/deltek-unveils-intelligent-platform-innovations/))*

**There is no ProjectCon 2026.** The event rebranded to **Deltek Elevate, 8–10 March 2027, Nashville TN**
— Deltek's events page states "Deltek ProjectCon is Now Deltek Elevate". Full Sail reports the move
was to get out of a peak business period.
*(VERIFIED — [Deltek events](https://www.deltek.com/en/about/events), fetched 2026-08-20; REPORTED — [Full Sail ProjectCon 2025 takeaways](https://www.fullsailpartners.com/fspblog/our-crews-top-takeaways-from-deltek-projectcon-2025), 11 Dec 2025)*

**Deltek's published AI timeline** *(VERIFIED — [Deltek blog, "Elevated by Design: How Deltek is Driving the Future of AI"](https://www.deltek.com/en/blog/how-deltek-is-driving-the-future-of-ai), 13 Nov 2025; timing confirmed on two independent fetches of the same page)*

| Capability | GovWin IQ | Costpoint | Maconomy | **Vantagepoint** |
|---|---|---|---|---|
| Deltek Proposals | Q1 2026 | Q3 2026 | — | **Q3 2027** |
| Agentic Financial Close | — | Q3 2026 | 2027 | **2027** |
| PPM Enterprise Risk | "in or around Q1 2026" | — | — | — |

Verbatim: *"Expected to launch in Q1 2026 with GovWin IQ, followed by Q3 2026 for Costpoint and Q3
2027 for Vantagepoint"* (Deltek Proposals); *"The first release is planned to be available in Q3 2026
for Costpoint, with Maconomy and Vantagepoint following in 2027"* (Agentic Financial Close).

**Announced but undated (Dela Agent Workforce, ProjectCon 2025)** *(VERIFIED as future-tense on
[Deltek's blog](https://www.deltek.com/en/blog/how-the-dela-agent-workforce-is-redefining-project-delivery), 19 Nov 2025)*: Business Development agent (client news monitoring, outreach suggestions),
AI Pursuit Scoring, AI Proposal Builder, Project Management agent with predictive planning, autonomous
billing workflows, employee document automation. Shipped from that list since: Harmony UX (2026.2,
opt-in) and Employee Document Templates (2026.2).

**Reading (INFERRED):** Vantagepoint sits at the **back** of Deltek's agentic queue — GovWin IQ and
Costpoint (the government-contracting line) get everything first, and Vantagepoint's turn is Q3 2027
at the earliest, with no flagship conference until March 2027 to accelerate it. Deltek's 2026
Vantagepoint releases have been approval workflows and insight alerts, not agents.

**No 2025–2026 acquisition changes the picture.** Deltek's press-release index shows nothing about
acquisitions in the visible 2026 window; Replicon appears as an existing Deltek product
("Deltek Replicon", 18 Jun 2026 release) and UKG Pro as an integration partner (2026.1/2026.2).
Deltek's 2026 press activity is analyst recognition, executive appointments, and Costpoint/GovCon
product news. *(VERIFIED as absence — [Deltek press releases](https://www.deltek.com/en/about/media-center/press-releases), fetched 2026-08-20; 2024–2025 releases were not visible on that page, so pre-2026 M&A is **could not verify** from this source.)*

---

## 9. Licensing and cost signals

| Item | Signal | Tag |
|---|---|---|
| Vantagepoint list price | **Deltek publishes none.** Product page ends at "Complete this form and a Deltek representative will contact you." Software Advice: "Pricing available upon request", custom quote, no free trial. | VERIFIED |
| Reported range | Roughly **$30/user/month entry, commonly $75–200/user/month**, varying by seats, modules, and cloud vs on-premise. | REPORTED — third-party review/pricing aggregators |
| Dela / Ask Dela | **Included in the core Vantagepoint licence** per Deltek's own wording. Not an AI add-on tier. | VERIFIED — [Dela platform page](https://www.deltek.com/products/platform/dela/) |
| CRM | Requires the **CRM or CRM Plus** module to be activated (a separately licensed module) for the GovWin IQ integration to be available. | VERIFIED — help.deltek.com GovWin IQ integration pages |
| GovWin IQ | Separate subscription. ~$200/user/month entry; 2026 quotes ~$12k–$42k+/yr; average deal ~$29k/yr. | REPORTED — civiciq, itqlick |
| Data at Your Service (Snowflake) | "Higher investment", positioned at 250+ employee firms. **No published price — could not verify.** | REPORTED (positioning) / could not verify (price) |
| ODBC access | Offered as the standard, "most cost-effective" data path; KOR has it live today. Whether it carries a line-item charge — **could not verify** from public sources. | REPORTED / partly VERIFIED (KOR has it) |
| Informer BI / Power BI enablement | Sold by partners (Full Sail Partners and others), not by Deltek. Price not published. | REPORTED |

**INFERRED, and important for tone:** because Dela is included in the base licence, KOR must not imply
MVE would have to *buy* AI from Deltek. They already have it. The argument is about **what it can
answer**, not what it costs.

---

## 10. What MVE could truthfully claim / What's overselling / Honest rebuttal

| # | If MVE says… | Truthful? | What's overselling | KOR's honest rebuttal |
|---|---|---|---|---|
| 1 | "We already get WIP, AR, utilization and earnings out of Vantagepoint without a BI tool." | **Largely true.** AR Aged, Unbilled Detail and Aging, Office/Project Earnings, Project Progress, Key Financial Metrics and Project Forecast are all standard reports; dashparts cover project, employee, firm and account bases. | Nothing — concede it cleanly. | "Agreed, and we didn't rebuild any of that. We read the same tables. What we added is a layer that answers the question you think of *second* — the one that crosses a report boundary." |
| 2 | "Our dashboards are real-time." | **Partly.** They're current as of their refresh. | 2026.3 verbatim: dashparts "generate a dedicated scheduled refresh process job for each user/role". That is a scheduled cache, not a live read. Deltek also warns at 8+ dashparts and added opt-in paged loading for large project datasets. | "Ours query the ledger on open. Yours refresh on a job. For a Monday PM meeting that's the same; for a cash call at 4pm it isn't." |
| 3 | "Dela already does conversational AI over our financials." | **Partly true — Ask Dela is GA and included in the licence.** | Deltek's own help states Ask Dela cannot do aggregate queries, works at **WBS1 only** (no phase or task financials), holds **a single record** in scope, and remembers **only the last two questions**. Aggregation was extended in 2026.1 to **Contacts and Firms only** — no release note extends it to projects or financials. **Caveat: Deltek's 5 Aug 2026 marketing article claims "top 10 projects by JTD revenue" works. Test this in KOR's tenant before the demo (§3.4).** | "Ask Dela is genuinely good at *'who is the PM on this job, what's the AR on it.'* Ask it *'which five PMs have the most WIP and which of their jobs are earning ahead of invoicing'* and you're at the edge of what Deltek documents. Our layer holds a whole conversation across the firm's financials, not one record at a time." |
| 4 | "Dela Insights already flags our at-risk projects." | **True in kind.** Earned Revenue Ahead of Invoicing, Spending Ahead of Earned Revenue, Timesheet Anomalies, GL trending and expense-policy insights all ship. | It is a **fixed, vendor-authored catalogue you subscribe to**. You cannot author an insight, define your own risk metric, or ask a follow-up question of one. | "Those are good alerts and we'd keep them. But *our* definition of at-risk is ours — it weights collections exposure and PM history the way this firm actually loses money. You can't author that in Dela; we author it in an afternoon." |
| 5 | "Vantagepoint CRM covers our pipeline; GovWin IQ feeds it opportunities." | **True, with conditions.** The integration is real, needs the CRM or CRM Plus module plus a GovWin IQ subscription, imports opportunities into the Projects hub, and can auto-update **once every 24 hours**. GovWin covers federal, SLED **and Canada**. | It's a **separate five-figure annual subscription** (~$12k–$42k+/yr reported), it's **public procurement only**, and it delivers **opportunity records** — not researched, entity-resolved, scored pursuits. No native dedup, no firm enrichment, no research agent. | "GovWin is a good public-sector feed and we'd never claim Deltek can't see Canadian tenders. But most of our work — and most of yours — comes through architects and developers, not a portal. Our BD layer ingests the portals *and* resolves the org, enriches the people, scores the fit and writes the dossier. GovWin hands you a row." |
| 6 | "AI pursuit scoring and AI proposals are coming to Vantagepoint." | **True — as a roadmap.** | Deltek's own published timeline: **Deltek Proposals reaches Vantagepoint in Q3 2027** (GovWin IQ Q1 2026, Costpoint Q3 2026). Agentic Financial Close reaches Vantagepoint **in 2027**. AI Pursuit Scoring and the client-intelligence agent are undated future tense. And there is **no ProjectCon 2026** — the next flagship event is March 2027. | "We're not betting against Deltek — they'll get there. We're pointing at the date. Deltek published it: Vantagepoint gets Proposals in Q3 2027 and agentic close in 2027. We built ours in eight months and it's running today. When Deltek ships, we'll evaluate switching. Until then this is a two-year head start we already spent." |
| 7 | "Deltek tells us our win rate and who we lose to." | **False for competitive intelligence.** | Verified first-hand in KOR's own tenant: `PR.Stage` has **no Won value** (live sync: 173 Pursuing, 83 Lost, 8 Declined, **0 Won**); `PR.LostTo` is populated on **3 of 79** losses; `ClosedReason` on **0** rows. | "Pull your own numbers before you believe this one. In our tenant Deltek recorded zero wins and named the winning competitor on three of seventy-nine losses. It's not a data-entry failure — the stage model has no Won state. We had to own win/loss in our own CRM to get it at all." |
| 8 | "We already have PIM for email and documents." | **True and material — take this one seriously.** PIM files Outlook email in one click with automatic metadata, searches across every project's emails, documents and drawings, and keeps version history, check-in/check-out and an audit trail. It integrates with Vantagepoint. | It's a **separate product** from Vantagepoint. On published evidence it indexes network drives, OneDrive and Autodesk Docs — **SharePoint is not mentioned**, and Deltek's PIM 26.0 help topic list contains **no Transmittals topic** (could not verify either way). | "If you run PIM, filing and search are solved and I won't pretend otherwise. Ask a different question: when you send a client a drawing set, can you see who opened it and when? Ours logs every download against the project, and the files never leave the SharePoint tenant you already pay for." |
| 9 | "We could build what you built on the Deltek API." | **True in principle — Deltek publishes a real REST API.** OAuth 2.0, versioned to 2026.3, Postman collections public. | The financial surface is **new**: PSA GL endpoints Sep 2025, GL transactions Dec 2025, resource planning Jun 2026. Legacy Web Services were removed in 2026.2. Building a financial layer on REST alone was not possible before December 2025 — the practical path was and is ODBC. | "You absolutely could, and Deltek would support you. That's the point: everything we'll show you reads Deltek through channels Deltek sells — ODBC and the REST API, read-only. There's no reverse-engineering here. The question isn't whether it's allowed. It's whether you want to spend the eight months." |
| 10 | "AI in the ERP means we don't need any of this." | — | The four Vantagepoint releases since Sept 2025 delivered **approval workflows and alert subscriptions** — timesheets, expenses, invoices, policy compliance — plus incremental API surface. The most recent release, 2026.3.5 (19 Aug 2026), shipped **one** feature: Draft Invoice Approval Workflows. No AI. | "Look at what actually shipped, not what was announced. Deltek's last four releases hardened approvals and added alert types. That's the right work for an ERP vendor. It just isn't the work that answers 'why is cash tight this month' in one sentence." |

### Three rules for the room

1. **Concede the reporting immediately and warmly.** The credibility of everything in the table
   depends on KOR being visibly fair about what Vantagepoint already does well.
2. **Fight on freshness, authorship, and the last mile** — not on existence. "Deltek doesn't have X"
   is almost always wrong. "Deltek has X, and here's where X stops" is almost always right.
3. **Use dates, not adjectives.** Q3 2027 for Deltek Proposals in Vantagepoint; 2027 for agentic
   close; March 2027 for the next Deltek conference; 19 Aug 2026 for a release that shipped one
   invoice-approval feature. Dates end the argument; adjectives restart it.

---

## 11. Open items — verify before the demo

| # | Item | How to settle it |
|---|---|---|
| 1 | **Can Ask Dela answer project/financial aggregates today?** Deltek's help says no; Deltek's 5 Aug 2026 marketing says "top 10 projects by JTD revenue". | Enable Ask Dela on a test role in KOR's own tenant and run the four probe questions in §3.4. Screenshot. **This is the single unresolved claim in this report.** |
| 2 | Does MVE run **Deltek PIM**? | Ask, neutrally, early. It reshapes the Newforma-replacement demo. |
| 3 | Does PIM do **client transmittals with download tracking**? | Not in the PIM 26.0 public help topic list; not on the product page. Confirm with a partner or the PIM help itself if item 2 comes back yes. |
| 4 | Which **LLM** powers Dela, and is customer data used for training? | Not published on any Deltek page I could reach. If MVE raises data governance, KOR's own architecture (self-hosted MCP on KOR-APP01, LAN-only) is the stronger answer regardless. |
| 5 | **Data at Your Service** pricing and whether ODBC carries a charge. | Not published. Ask KOR's Deltek account rep if it becomes decision-relevant. |
| 6 | Pre-2026 Deltek M&A. | Deltek's press-release page showed only 2026 items; 2024–2025 not visible. Not material to this brief. |

---

## 12. Sources

**Deltek primary — release notes** (all fetched 2026-08-20)

- Vantagepoint release notes index — https://help.deltek.com/product/Vantagepoint/ReleaseNotes/
- 2026.3 release notes index (2026.3 → 2026.3.5, builds and dates) — https://help.deltek.com/product/Vantagepoint/2026.3/ReleaseNotes/
- 2026.3 (15 Jun 2026, 2026.3.0.152) — https://help.deltek.com/product/Vantagepoint/2026.3/ReleaseNotes/DeltekVantagepoint20263ReleaseNotes.htm
- 2026.3.5 (19 Aug 2026, 2026.3.5.712) — https://help.deltek.com/product/Vantagepoint/2026.3/ReleaseNotes/DeltekVantagepoint202635ReleaseNotes.htm
- 2026.2 (13 Mar 2026, 2026.2.0.156) — https://help.deltek.com/product/Vantagepoint/2026.2/ReleaseNotes/DeltekVantagepoint20262ReleaseNotes.htm
- 2026.1 (19 Dec 2025, 2026.1.0.163) — https://help.deltek.com/product/Vantagepoint/2026.1/ReleaseNotes/DeltekVantagepoint20261ReleaseNotes.htm
- 2025.4 index and main notes (29 Sep 2025, 2025.4.0.143) — https://help.deltek.com/product/Vantagepoint/2025.4/ReleaseNotes/ · https://help.deltek.com/product/Vantagepoint/2025.4/ReleaseNotes/DeltekVantagepoint20254ReleaseNotes.htm

**Deltek primary — product help** (fetched 2026-08-20)

- Use Ask Dela (limitations, verbatim) — https://help.deltek.com/product/Vantagepoint/7.0/Ask_Dela_UseAskDela.html · https://help.deltek.com/Product/Vantagepoint/7.2/Ask_Dela_UseAskDela.html
- Enable Deltek Dela — https://help.deltek.com/product/vantagepoint/7.0/Deltek_Dela_EnableDeltekDela.html
- Enable Ask Dela (role-level enablement, language requirement) — https://help.deltek.com/Product/Vantagepoint/7.2/Ask_Dela_EnableAskDela.html
- Connect to GovWin IQ — https://help.deltek.com/Product/Vantagepoint/7.2/CRM_Basics_ConnectToGWIQ.html
- GovWin IQ integration form / utility — https://help.deltek.com/Product/Vantagepoint/6.0/ST_Util_Int_and_Imp_GovWinIQ_Tab.html · https://help.deltek.com/product/Vantagepoint/3.5/util_GovWin_IQ_Integration_Utility.html
- REST API reference — https://help.deltek.com/Product/Vantagepoint/7.0/DPS_REST_API.html · https://vantagepointapi.deltek.com/ (v2026.3) · https://www.postman.com/deltekeng/deltek-s-public-workspace/documentation/omlh9j7/deltek-vantagepoint-2026-1-api
- Standard reports — AR Aged https://help.deltek.com/Product/Vantagepoint/3.0/VP_rept_AR_Aging.html · Unbilled Detail and Aging https://help.deltek.com/product/Vantagepoint/2.0/VP_rept_Unbilled_Detail_Report.html · Project Earnings https://help.deltek.com/Product/Vantagepoint/3.5/VP_rept_Project_Earnings.html · Key Financial Metrics https://help.deltek.com/product/Vantagepoint/3.5/VP_rept_Key_Financial_Metrics.html · Project Reporting overview https://help.deltek.com/Product/Vantagepoint/7.2/con_pro_project_reporting.html
- Deltek PIM help (versions 20.0–26.0) — https://help.deltek.com/product/PIM/ · https://help.deltek.com/product/PIM/26.0/DeltekPIMHelp.html

**Deltek primary — corporate and marketing** (fetched 2026-08-20)

- Dela platform page (licensing: "part of the license fee") — https://www.deltek.com/products/platform/dela/
- Vantagepoint product page (modules, add-ons) — https://www.deltek.com/products/erp/vantagepoint/
- Vantagepoint CRM & Pipeline Management — https://www.deltek.com/products/erp/vantagepoint/crm-and-pipeline-management/
- Deltek PIM product page — https://www.deltek.com/products/delivery-assurance/project-information-management/
- "Introducing Ask Dela for Vantagepoint" (6 Jun 2024) — https://www.deltek.com/resources/articles/vantagepoint-ask-dela/
- "Your AI Project Manager Has Arrived: What Dela Does for PMs in Vantagepoint" (5 Aug 2026) — https://www.deltek.com/resources/articles/dela-vantagepoint-project-managers/
- "Smarter Approvals, Cleaner Records: What's New in Vantagepoint" (7 Aug 2026) — https://www.deltek.com/resources/articles/vantagepoint-dela-insights-2026-3/
- "Elevated by Design: How Deltek is Driving the Future of AI" (13 Nov 2025) — **the AI timeline** — https://www.deltek.com/en/blog/how-deltek-is-driving-the-future-of-ai
- "How the Dela Agent Workforce is Redefining Project Delivery" (19 Nov 2025) — https://www.deltek.com/en/blog/how-the-dela-agent-workforce-is-redefining-project-delivery
- Press release: "Deltek Unveils Intelligent Platform Innovations that Elevate the Project Lifecycle" (12 Nov 2025) — https://www.deltek.com/company/news/deltek-unveils-intelligent-platform-innovations/
- Deltek events (ProjectCon → Deltek Elevate, 8–10 Mar 2027, Nashville) — https://www.deltek.com/en/about/events
- Deltek press-release index (2026 items; no acquisitions visible) — https://www.deltek.com/en/about/media-center/press-releases

**Secondary / REPORTED**

- Full Sail Partners, "Our Crew's Top Takeaways from Deltek ProjectCon 2025" (11 Dec 2025) — https://www.fullsailpartners.com/fspblog/our-crews-top-takeaways-from-deltek-projectcon-2025
- Full Sail Partners, "What's New in Deltek Vantagepoint" covering 2025.4/2026.1/2026.2 (30 Apr 2026) — https://www.fullsailpartners.com/fspblog/2whats-new-in-deltek-vantagepoint-2026-04
- Full Sail Partners, Business Intelligence solutions (ODBC vs DaaS, Informer, Power BI) — https://www.fullsailpartners.com/business-intelligence-solutions
- Software Advice, Vantagepoint reviews (reporting complaints, 2025–2026) — https://www.softwareadvice.com/reporting-tools/deltek-vantagepoint-profile/
- GovWin IQ pricing 2026 — https://civiciq.com/blog/govwin-iq-pricing-2026 · https://www.itqlick.com/govwin-from-deltek/pricing

**KOR first-hand (VERIFIED in KOR's own systems)**

- Deltek ODBC access path, DSN, DataDirect HDP endpoint, catalog, T-SQL behaviour, binding quirks — KOR memory `reference_deltek_odbc_access`, `reference_deltek_odbc_quirks`, `reference_deltek_vp_linked_server`, `reference_deltek_schema`.
- Win/loss data gap: `PR.Stage` has no Won value; live sync 173/83/8/**0 Won**; `PR.LostTo` on 3 of 79; `ClosedReason` on 0 — KOR memory `project_korpursuit_deltek_no_winloss` (verified 2026-06-25 against `opportunities.KorPursuits`, 1,068 rows).
