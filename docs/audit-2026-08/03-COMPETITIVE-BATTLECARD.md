# 03 — Competitive Battlecard: the MVE technical demo

**For:** the owner, in the room. **Built from:** C1 (Deltek), C2 (Newforma + adjacent), C3 (Microsoft),
C4 (the field), all compiled 2026-08-20 against live primary sources. **Written:** 2026-08-20.

**Evidence tiers are carried forward unchanged from those reports and are never promoted.**
**VERIFIED** = the vendor's own page, docs, release notes or KOR's own live system.
**REPORTED** = press, analyst, partner, review site. **INFERRED** = labelled reasoning.
**[VERIFY-INTERNAL]** = the claim depended on KOR's own code or systems, not a competitor's.
**All eleven module audits have now returned (2026-08-20) and every marker below is resolved.** Internal
claims carry their own tier — `[RUN]` executed · `[QUERIED]` live state read · `[READ]` source read.
Several claims were **corrected, not confirmed** — those are marked ⚠ **CORRECTED** and the old wording
must not be used. Full register in the Appendix.

**Three rules that decide whether this goes well.**
1. **Concede first, and warmly.** Every rebuttal below only works because the concession before it was
   accurate. "Deltek has no reporting" / "Newforma has no AI" are both false and will lose the room in
   one sentence.
2. **Fight on freshness, authorship, scope and the last mile — never on existence.** "X doesn't have it"
   is usually wrong. "X has it, and here is where it stops, in their words" is usually right.
3. **Use dates, not adjectives.** Q3 2027. 2026-08-20. Two weeks. Five OData functions. Dates end an
   argument; adjectives restart it.

---

## 1. The one-paragraph position

KOR runs Deltek Vantagepoint as its system of record and Microsoft 365 as its tenant, and is replacing
neither. Over eight months it built a layer on top of both, in four parts: the three things it actually
used Newforma for — filing project email, searching it, and issuing client transmittals — rebuilt so the
files live in KOR's own SharePoint and every download is attributed to a named recipient; a reporting and
conversational layer that reads Deltek through the two channels Deltek sells and documents, read-only
ODBC and the REST API; a BD pipeline that ingests Canadian and US public procurement, resolves the
organisations, researches them and writes the dossier; and a set of engineering tools, of which the one
worth the trip is DXF-to-ETABS. Nothing here reverse-engineers a vendor, and nothing here is a bet against
one — the parts Deltek and Newforma do well are still doing them. It exists because a 40-person firm's
questions cross the boundaries that vendor products stop at: a transmittal, the project's margin, and the
pursuit it came from are three products at any vendor and one application here. **What that bought, and
what it cost, are both in this document.**

---

## 2. Objection → response

Rows are ordered by how dangerous the objection is, not how likely. Every "where it breaks" cell cites the
vendor's own documentation.

| # | Objection (their words) | What is TRUE in it — concede this first | Where it breaks (their source) | Your line |
|---|---|---|---|---|
| **1** | *"What happens when you leave?"* | **All of it.** This is the strongest thing they can say and no cost table answers it. Do not bluff, do not counter-attack, do not reach for a metric. | It doesn't break. What is true alongside it: Newforma is on its second PE owner (Battery → **Ethos Capital, 2023-04-03**), hired a SaaS growth CEO (**Peter Cannone, 2025-04-24**), shipped **PC→Konekt migration tooling in 2025.2**, publishes a customer PC→Konekt migration case study, and puts **every 2026 AI feature in Konekt only** (Newforma's own release notes + blog). A vendor choice is a different risk, not the absence of one. | "It's real and I'm not going to talk you out of it. What we do about it: it's tested, the surface is small, and the record lives in storage we own — if every line of my code vanished tonight, the project files are exactly where they are this morning. Then I'd ask you the same question back about Project Center." ⚠ **VI-1 CORRECTED — DROP "it's all in source control".** `[RUN]` `git rev-parse --show-toplevel` fails in `C:\VIsual Studio Projects\Redirector` and every parent; there is no `.git` on that path. Worse: `dotnet build` on it yields **5 compile errors and has since 2026-03-17**, when commit `981907f5` removed `GraphFacade.Instance` from a repo it consumes by `bin\Release` HintPath. **A public production service that has logged nine months of client evidence cannot be rebuilt from its own source.** The one mitigation, and it is real `[RUN]`: SHA-256 of the deployed `Kor.Transmittals.Redirector.dll` on KOR-APP01 is byte-identical to the local publish folder (`_Publish\_Redirector`), as are `Kor.Operations.{Core,Data,Graph}.dll` — **the source on disk provably is what serves live links.** Say "the deployed binary hashes to the source we hold" — never "it's in git". `git init` plus a locally-constructed `GraphFacade` is under a day's work. |
| **2** | *"Egnyte does email filing and secure sharing now."* | **True, and it is the closest product on the market.** Egnyte GA'd **Email Capture on 2026-05-13** — Outlook add-in filing threads into project folders, searchable by date/sender/subject/doc type — plus Secure Sharing and an Audit Trail tracking *"every external access and download event."* Published per-seat pricing: **$10 / $22 / $39 / $48 per user/month.** (Egnyte press release 2026-05-13; egnyte.com/industries/aec.) | Egnyte's own migration material: it sells a wizard to move you **off SharePoint into Egnyte storage**, with documented limits — **5 TB / 5 M objects, permissions not migrated** (egnyte.com blog + helpdesk SPO migration article). And there is no numbered transmittal register anywhere in the product. | "They do, and it shipped in May — I'd have looked hard at it. Three differences: adopting it means migrating off the tenant we already pay Microsoft for, it's per seat forever, and there's no transmittal record tied to a project at all. We stayed in our own tenant." ⚠ **VI-2 CORRECTED — do NOT claim a numbered register.** `[READ]` `GraphFacade.ReserveTransmittalNumberAsync` (`GraphFacade.cs:352-358`) returns `$"{projectNumber}-{DateTime.UtcNow:yyyyMMdd-HHmmss}"` — three lines, **a UTC timestamp suffix**, no database read, no counter, no collision check. So `31195-01-20260820-230647`, not `31195-01-0042`. (`CoverSheetRenderer.cs:684` exists purely to re-render that UTC stamp in Pacific for the cover sheet, which confirms it is understood as a timestamp.) A client-citable sequence number is a Newforma behaviour KOR does **not** have. If asked "how do you number transmittals", answer "project number plus timestamp" — give it, don't let it be discovered. |
| **3** | *"Deltek PIM files our email already."* | **True and material.** Deltek's PIM page: *"File project emails directly from Outlook into Deltek PIM with a single click"*, *"Automatically applies metadata"*, *"Search instantly across all projects for any email, document, or drawing"*, *"Full version history, check-in/check-out file locking, and a complete audit trail"*, and it integrates with Vantagepoint. If they run it, filing and search are solved. | PIM indexes *"shared network drives, OneDrive, and Autodesk Docs"* — **SharePoint is not named** on the product page. Deltek's own **PIM 26.0 help topic list contains no Transmittals entry**, and the PIM help index carries the note *"The Deltek PIM online help is currently work in progress."* Per-recipient download granularity **could not be verified either way** — say that, don't claim absence. | "If you run PIM, I won't pretend we file email better. Different question: when you issue a drawing set, can you see which individual opened it, when and from where — and do those files sit in your own M365 tenant?" **Ask whether they run PIM before you build this segment (§6).** |
| **4** | *"We'd just build it in Power BI / Fabric / Copilot."* | **The strongest version of the Microsoft case, and it's cheap: F2 reserved + 40 Power BI Pro ≈ $8,600/yr** (Azure Retail Prices API + Microsoft pricing page, 2026-08-20). Fabric data agents are documented as *"a generally available feature"*, do NL2SQL/NL2DAX, enforce the caller's permissions and honour Purview DLP. 40,000+ paid Fabric customers (Nadella, FY26 Q4, 2026-07-29). Price this path first and say so. | **(a) The data can't arrive live.** Microsoft's Power Query **ODBC connector** page (updated 2026-07-31) lists Capabilities Supported as **"Import"** — DirectQuery is not on the list. Deltek is reachable only over a DataDirect Hybrid Data Pipeline ODBC endpoint on 443. Import caps at **8 refreshes/day** shared, **48/day** on capacity (learn.microsoft.com refresh-data, updated 2026-08-11). **(b)** A Fabric data agent accepts Lakehouse / Warehouse / Fabric SQL DB / Mirrored / semantic model / KQL / Graph — **an external ODBC source is not on the list**, max **five** sources (data-agent-sql-sources, ms.date 2026-06-03). **(c)** Microsoft on its own tool: *"the underlying model … is nondeterministic and isn't guaranteed to produce a correct answer, or the same answer with the same prompt, model, and data"*, and *"you might want to consider advising users not to use Copilot to consume your semantic model"*, and it warns that *"complex patterns like currency conversion or disconnected tables … might cause unexpected or incorrect results"* (copilot-semantic-models, updated 2026-07-21). | "You could build the dashboards in a quarter and you should price it — it's about $8,600 a year. What you can't buy is the Deltek traps that return a **wrong number instead of an error**. Account codes are stored padded — `'4001.00'` — so the obvious `IN ('4001','4003')` matches zero rows and your Net Multiplier renders 0.00 with no error anywhere. Labour dollars sit in the *project's* currency, not the employee's, so a cross-border rollup is out by about a third. And the nondeterminism isn't my characterisation — Microsoft's own page says test your model, and if it isn't consistently right, tell your people not to use it." **VI-3 CONFIRMED — the traps are still encoded and current** (detail in §3.3). The account-padding trap is live and demonstrable: KOR's own cash whitelist is stored `1110.00,1120.00,1170.00` `[QUERIED]`, and the MCP service shipped a real defect from exactly this shape — it summed **all 20 `CFGBanks` accounts instead of the 3** because a config key was dropped on the floor, pulling in petty cash `1000.00` and USD savings `1175.00` `[RUN]`. That is not a hypothetical; it is this month. |
| **5** | *"Copilot can already search our email."* | True in scope: M365 Copilot at **$30/user/month**, and Office 365 E1/E3/E5 and Business Standard all qualify as base plans — **no licence upgrade needed** (microsoft-365-copilot-licensing, updated 2026-08-18). | Microsoft's requirements page, **ms.date and updated 2026-08-20 — the day this was checked**: *"Microsoft Copilot is only supported on primary mailboxes that are hosted on Exchange Online. **It isn't available on a user's archive mailbox, group mailboxes, or shared and delegate mailboxes that they have access to.**"* Filed project correspondence is archive, shared and group mail by construction — and Copilot only ever sees what the calling user could already open. | "Copilot answers *what did I say about this*. A project record has to answer *what did the firm say about this*, two PMs later, when a claim lands. Microsoft's own page — updated the day I looked — excludes archive, shared and group mailboxes." **This is the single cleanest rebuttal in the deck.** |
| **6** | *"Ask Dela already answers that."* | **True and free.** Ask Dela is **GA since Vantagepoint 7.0**, included in the base licence (*"part of the license fee"*, Deltek Dela platform page) — there is no AI upsell to point at. **2026.1 (2025-12-19)** added aggregate questions on **Contacts and Firms**, including UDFs. Dela Insights ships a real alert catalogue (earned-vs-invoiced, spend-vs-earned, timesheet anomalies, GL trending). | Deltek's own *Use Ask Dela* help page, 7.0 and 7.2 trees, fetched 2026-08-20, verbatim: *"You cannot query Ask Dela for aggregate data, such as information about the top projects by revenue"*; **WBS1 only** — no phase or task financials; *"supports conversations only about a single record"*; *"it remembers the context of only the last two questions."* No release note in 2025.4 / 2026.1 / 2026.2 / 2026.3 extends aggregation to projects or financials. Dela Insights is a **vendor-authored catalogue you subscribe to** — you cannot author an insight or ask one a follow-up. | ⚠ **DO NOT SAY THIS UNTIL YOU HAVE TESTED IT (§7.1).** A Deltek article dated **2026-08-05** gives *"What are the top 10 projects by JTD revenue?"* as a live example — verbatim the thing the help page calls impossible. Test it in KOR's tenant first. **If the limitation holds:** "Dela's good at *who's the PM on this job, what's the AR on it*. Ask it which five PMs have the most WIP and which of their jobs are earning ahead of invoicing, and you're past what Deltek documents." **If it doesn't hold:** pivot — single-record scope, WBS1, two-question memory, and the fact that it cannot join Deltek data to anything that isn't Deltek. |
| **7** | *"Deltek already does this reporting."* | **Largely true — concede it immediately.** AR Aged, Unbilled Detail and Aging, Office Earnings, Project Earnings, Project Progress, Key Financial Metrics and Project Forecast are all standard reports; dashparts build on Project, Project Detail, Employee, Firm and Account bases with calculated fields and drill-through (help.deltek.com report pages, fetched 2026-08-20). | Deltek's **2026.3 release notes (2026-06-15)**, verbatim: *"Dashparts now generate a dedicated scheduled refresh process job for each user/role with access to the dashpart"* — a scheduled cache, not a live read. **2026.2 (2026-03-13)** added a *"Performance Recommendation Message"* when you save a dashboard with **8 or more dashparts**. No documented way to author a firm-wide cross-hub metric. | "Agreed — we rebuilt none of it, we read the same tables. Ours query the ledger when you open them; yours refresh on a job. For a Monday PM meeting that's the same thing. For a cash call at 4pm it isn't. And the metric definitions are ours — earned-versus-invoiced and collections exposure the way this firm actually loses money." **VI-4 CONFIRMED, with one caveat you must say first.** `[RUN]` The reads are genuinely live on open — cold ODBC connect **960 ms**, four representative loader queries **252 / 82 / 180 / 77 ms**, total wall **1,559 ms**. Nothing is scheduled, nothing is cached, except one app-written snapshot table used only for the YoY trend line. ⚠ **But the ledger behind it is not current.** `[QUERIED]` `MAX(Period)` in both `PRSummaryMain` and `GLSummary` is **202602 — February 2026**, zero rows for 202603–202608, while `AR` carries invoices to today and `tkDetail` to 2026-08-23. So WIP, Cash, Backlog, the GL P&L, Net Income (T12mo), the revenue trends and the Clients tab are all **six months old**. **The honest sentence, and it is still a good one: "the pipeline is real-time — a second and a half end to end; the ledger behind it is only as fresh as our accountant's last posting. AR, utilisation and collections are current to this morning; the summary ledgers stop at February."** Lead with AR, utilisation and collections. |
| **8** | *"Newforma does that now."* | **True, and in several places ahead of us.** Newforma files email from Outlook and has for twenty years, files an email **directly as an RFI, submittal or issue**, and Konekt gives *"smart project suggestions … based on email content and sender."* Newforma's own AI blog says **Smart Email Filing is "deployed and in your hands today."** Info Exchange genuinely does who-downloaded-what. **Concede all of it.** | It is split across two products and the investment is all in one. **Project Center 2026.2** shipped Autodesk 2027 support, a .NET 10 Revit add-in, a MySQL upgrade and an Outlook add-in UI refresh — **no AI** (projectcenter.help.newforma.com release notes). **Info Exchange's own overview help topic is stamped "last updated 3 years ago"**, and it appears on neither the current Project Center product page nor the 2025.2/2026.2 release notes. The current-generation replacement is lighter: Konekt's own file-server-connector help says *"The download link will be available for **2 weeks**"*, the Sharing Centre logs *"the **number of times** it was downloaded"*, and *"History is only recorded for sharing; other actions to files and folders are not recorded."* **Smart Search and Vojo are *"in active development" / "coming soon"* on Newforma's own AI page**, and the Konekt packages page still lists **"AI Assist — Coming Soon."** | "Their filing is ahead of ours and I'll show you exactly where. But their current product logs a download *count* against a share with a two-week link, and the product that does per-recipient tracking hasn't been touched in three years. Ours issues a distinct link per recipient and records the email address, IP and user agent on every download." **VI-5 CONFIRMED END TO END, in production, `[QUERIED]`:** **829 transmittals** running continuously since 2025-11-28 with one sent hours ago; **4,284 per-recipient tracking links** to **741 distinct external addresses** at firms including Arcadis, Greystar, Wesbild, Anthem and JWDA; **2,682 click events with zero null IP, zero null user-agent, zero null recipient email**; **730 of 829 (88%) carry at least one recorded open**. The dashboard's own SQL was re-run live and populates. ⚠ **Lead with clicks, not opens.** There are 8,947 opens to 2,682 clicks — 3.3:1 — and much of that is corporate mail-scanner and image-proxy prefetch, not humans. Quote 8,947 as "times clients opened our transmittals" in front of anyone who knows email tracking and the whole evidence claim gets discounted. **Clicks are the defensible evidence; the named Activity list is the moment.** |
| **9** | *"Isn't this just an MCP wrapper?"* | **Yes, MCP is table stakes — concede the whole board.** GA in Copilot Studio (2025-05-29), Foundry Agent Service, Azure API Management, Visual Studio (2025-08-19), M365 Copilot declarative agents (2025-12-15); Microsoft and GitHub sit on the MCP Steering Committee. **Revizto ships an MCP server**; **Bluebeam Revu connects to Claude**. And **Claude is a GA model on Microsoft Foundry** — "we're a Microsoft shop" is no longer an argument against either. | Microsoft's own Fabric doc (data-agent-mcp-server, ms.date 2026-06-30): *"A published Fabric data agent exposes a **single MCP tool**. That tool represents the data agent itself, so an MCP client sends a question to the tool and gets back an answer."* And on the other route, the Power Platform **SQL Server connector** supports exactly five OData aggregations — `average, max, min, sum, countdistinct` — with **no GROUP BY and no multi-table join** (learn.microsoft.com/connectors/sql, updated 2026-07-11), while the deterministic code-interpreter that would fix it reaches **CSV/XLSX and SharePoint libraries only, not SQL** (2026-08-04). WIP and utilisation-by-office are multi-table joins with GROUP BY. | "The protocol isn't the achievement and I'd never sell it as one. What's behind it is: typed functions that return the same record for the same inputs, over 29 Deltek tables. Microsoft's docs push you to exactly that shape — compute the number in code, let the agent narrate it — because their SQL connector can't express a GROUP BY. And because everyone standardised on MCP, you can call ours from your Teams tomorrow — over VPN. That's a URL and a token." **VI-6 SETTLED: the number is 23, not 24 or 25.** `[QUERIED]` `GET /tools` with Basic auth on the live service returns **exactly 23 registered tools**. The 25 `.cs` files in `Tools/` are 23 tools + `ServerInfoTool` (an MCP-wire ping, deliberately outside the `/ask` registry) + `ToolErrorEnvelope` (a static helper). **Say 23.** Depth is real: 22 of the 23 wrap the *same* canonical `Kor.Operations.Business` service the WPF screens call, `temperature = 0`, a startup gate that refuses to boot if the prompt and the registry disagree, and 15 passing unit tests on the read-only SQL gate. ⚠ **"Confirm every tool still answers" — 19 of 23 do.** `[RUN]` The deployed build is **34 days behind HEAD**, which strands three fixes: `get_wip` returns earned and overbilled **transposed**, `get_cash_position` sums 20 bank accounts instead of 3, and `get_billed_pnl` uses a flat 1.36 FX where the app uses 1.378457. **All three are a rebuild-and-robocopy away. Until that ships, keep WIP and cash questions off the screen.** |
| **10** | *"Bluebeam Max already does change detection."* | **True, and Trunk Tools is further ahead — concede both.** Bluebeam Max launched globally **2026-05-19**; **Smart Overlay** detects *"design changes across disciplines and drawing scales"*, at **$590/user/yr**, inside the tool every architect already owns. Trunk Tools' **TrunkReview** uses vision-language models to catch **clouded and unclouded** changes in a 20-sheet bulletin in ~5 minutes, on 500+ jobsites, on **$70M raised**. | Bluebeam's own page (bluebeam.com/bluebeam-max, fetched 2026-08-20): **Smart Review and Smart Overlay are in preview**, and Smart Review is *"optimized for US-based vertical commercial construction."* Neither Bluebeam nor Trunk Tools claims a **bar-list steel-weight delta** — the number a rebar detailer prices. | "Generic change detection will be a checkbox in software you already own, probably within a year, and I'm not going to argue with that. Ours isn't a change list — it's a steel-weight delta off the bar list. If you're on Max, compare us on that, not on 'what changed'." **VI-7 CONFIRMED — the steel-weight delta is real and shipped.** `[RUN]` `RebarChangeService`, `RebarGridPricer`, `RebarWeightEstimator` and `RebarOverlayGenerator` are all covered inside a 392-test green run; `[QUERIED]` the delivered artifacts sit on disk for job 31065 — a full change workbook, a **2.3 MB on-drawing markup PDF** (added work green, removed red), and a v5 change ledger dated 2026-07-24. ⚠ **One correction to §4.1 below: it is not "blind to scanned sets by design".** `[READ]` `PdfTextWithOcr.cs` OCRs any image-only page via WinRT `Windows.Media.Ocr` and flags the result "verify" before diffing. The accurate line is *"we read the drawing's own call-out text, and OCR a flattened sheet and flag it — we don't read pixels the way a vision model does."* That OCR path has **no test coverage at all**, so do not stress it live. |
| **11** | *"Conversational ERP AI isn't new — Unanet shipped it, Deltek ships it free."* | **True as of this year.** Unanet's **Champ for ERP went GA 2026-02-18** for AE ERP customers — cash, margin, utilisation, 90-day revenue forecast, plus scheduled "Chores" and agentic "Round Ups" (unanet.com newsroom). Ask Dela ships in Vantagepoint and Ajera Cloud at no extra charge. *"We built an AI that answers questions about our financials"* is a 2025 claim in an August 2026 room. | Unanet's own still-live **Wyatt Beta Terms page** (checked 2026-08-20) describes a 90-day approval-gated beta and *"early access stage"* — read the GA as vendor-declared productisation. And **no vendor in the scan discloses anything resembling a typed multi-tool analytical architecture**; where depth is claimed, vendors describe outcomes, not architecture (C4 §A, VERIFIED absence across Unanet, BST, Monograph, Total Synergy, Rapport3). | "You're right that it stopped being novel this year — don't judge it on the chat box. Judge it on a question their box can't answer, and I'll bring one." |
| **12** | *"Thornton Tomasetti already built the drawing-to-model thing."* | **Asterisk is real and is the nearest published thing.** Thornton Tomasetti's own page: it *"generates structural solutions – in seconds – from a simple building **massing model**"*, with *"custom ML and generative AI models"* for steel, concrete and mass timber, returning embodied carbon, cost per square foot, floor placement and weight. CORE studio is 21 people, 10 web apps, 150+ plugins, running since 2011. | Same page: the input is a **massing model** and the output is a **scheme**. It is not the architect's issued plan set, and it is not a runnable model of a specific building. | "Asterisk answers *what structure could this massing take*. Ours answers *here is a runnable model of the building you actually issued last week*. Different question — and the second one is the one that costs a graduate a week." **Say this before they do. See §5.** |
| **13** | *"Per-seat licensing isn't a problem — Procore charges by construction volume."* | **True. Do not argue it.** Procore prices on Annual Construction Volume with *"unlimited users"* and *"we'll never charge you for adding more users"* (procore.com/pricing). OpenSpace prices the same way. | Nothing to break — this is a trap, not an objection. The per-seat line lands against Newforma, Egnyte and Bluebeam; it does not land against Procore. | "Fair point — volume-based changes that maths. For us it was never mainly cost anyway. It was where the record lives and what it connects to." |

### Have these five open on your phone

1. Deltek — *Use Ask Dela* limitations: `help.deltek.com/Product/Vantagepoint/7.2/Ask_Dela_UseAskDela.html`
2. Microsoft — M365 Copilot requirements (mailbox limitation, updated 2026-08-20):
   `learn.microsoft.com/en-us/microsoft-365/copilot/microsoft-365-copilot-requirements`
3. Microsoft — Power Query ODBC connector, "Capabilities Supported: Import":
   `learn.microsoft.com/en-us/power-query/connectors/odbc`
4. Newforma — Konekt file server connector, two-week link expiry + download count:
   `konekt.help.newforma.com/4408494681869-integrations-api/file-server-connector/`
5. Bluebeam Max — Smart Overlay / Smart Review "preview": `bluebeam.com/bluebeam-max`

---

## 3. Where KOR is genuinely ahead

Ranked by how safe each is to say out loud. **The rarest capability is DXF→ETABS and it has its own
section (§5); it is not repeated here.**

**1. Per-recipient transmittal attribution — evidentiary quality, not features.**
*Evidence:* Konekt's own docs record a download **count** against a share, with a two-week link, and state
*"history is only recorded for sharing."* Info Exchange does who-downloaded-what but publishes no IP or
user-agent capture — and hasn't been updated in three years. Egnyte logs *"every external access and
download event"* but has no numbered transmittal register. SharePoint's native answer is Purview
`Search-UnifiedAuditLog` — an admin compliance surface, not a PM's screen. KOR issues a distinct
`LinkId` per recipient and records `RecipientEmail`, `ClientIp`, `UserAgent` per download —
**VI-5 CONFIRMED in production `[QUERIED]`: 829 transmittals, 4,284 per-recipient links, 741 external
addresses, 2,682 click events with zero nulls in IP, user agent or recipient email, 88% of transmittals
with at least one recorded open.**
*The sentence:* **"For a delivery date defended in a dispute, 'downloaded three times' isn't evidence. 'Ravi, 10:42, from this IP' is."**
⚠ **The honest boundary, and say it if pressed:** the redirect GUID is a *tracker*; the thing that grants
access is the SharePoint sharing link behind it. On an external send that link is `Scope="anonymous"`,
so a **forwarded email gives a stranger the files — and the redirector will log that stranger's click
under the original recipient's email.** Per-recipient attribution is evidence of *delivery*, not proof of
*identity*. Volunteer that before a sharp lead finds it; it costs nothing and it is exactly the kind of
precision that makes the rest of the claim believable.

**2. The record lives in KOR's own storage, not in a vendor's.**
*Evidence:* in both Newforma products SharePoint is a **connector** — Newforma holds the record (Newforma
product pages, 2026-08-20). Egnyte markets a wizard to migrate you off SharePoint.
*The sentence:* **"If our software vanished tonight, the project files are exactly where they are this morning."**

> ⚠ **VI-8 CORRECTED — "SharePoint-native" is true of transmittals and FALSE of filed email. Do not say
> "it's all in SharePoint".** `[READ]` A grep for `SharePoint|GraphServiceClient|graph.microsoft` across
> **every** email filing and search path returns **zero hits**. Filed email is written to the **file
> server** — `\\Kor-fs01\Projects\Projects\<Category>\<Project>\Newforma\email\<yyyy-MM>\` — as `.msg`
> files, and indexed into `KorEmailIndex` on `KOR-APP01\SQLEXPRESS`. FileSync's Watcher, which mirrors
> project folders to SharePoint, **explicitly excludes** `\Newforma\email` (`WatcherHostedService.cs:44`).
> Transmittal payloads *are* SharePoint-native (site `bmzse.sharepoint.com/sites/NewerForma`, via Graph).
>
> **The underlying argument survives intact and is arguably stronger — two open stores, both KOR's, neither
> a vendor's.** The correct sentence: *"Client transmittals land in our own M365 tenant; filed project
> email lands as .msg files in the project folder on our own file server. Two places we already own, both
> readable with Explorer and Outlook. Turn our software off tonight and nothing moves."*

**3. Firm-scale answers over Deltek that neither vendor's AI gives.**
*Evidence:* Deltek's own AI is documented at WBS1, single-record, two-question memory (pending §7.1);
Microsoft's cannot reach the Deltek ODBC endpoint live at all, and warns its own answers are
nondeterministic on exactly this class of model. Underneath ours: **29 Deltek tables**, with **28 JOINs in
`FinancialsService.cs` alone**, and a set of traps that fail **silently** — they return a plausible wrong
number, never an error. **Lead with these three; they are the best-evidenced:**

- **Account codes are stored padded** — `'4001.00'`, not `'4001'` — so the obvious `Account IN ('4001','4003')`
  matches **zero rows**, with no error. This bit KOR in production: Net Multiplier showed 0.00 and DSO
  showed "Data unavailable." The correct form is `LEFT(LTRIM(RTRIM(Account)),4) IN (…)`.
- **Labour dollars are denominated in the *project's* currency, not the employee's** (`tkDetail`, by
  `pr.Org`), so any cross-border rollup is wrong by roughly a third. Independently corroborated: the
  internal Deltek audit found **two different FX regimes live in the same window — 1.378457 in Partner
  Financials, 1.36 everywhere else**. Currency handling here is a demonstrated trap, not a theoretical one.
- **Columns move between Deltek releases, and the code defends against it.** KOR's accessors probe
  `INFORMATION_SCHEMA` before building SQL, and **`DeltekSchemaValidator` runs clean today across all 34
  expected columns**. Microsoft states the consequence of having no equivalent, verbatim: *"Data refresh in
  the Power BI service will fail when the source column or table is renamed or removed"* (refresh-data,
  updated 2026-08-11). **A Deltek upgrade that renames a column breaks a Power BI refresh; this survives it —
  and the guard is running and demonstrable.** **VI-3 CONFIRMED `[RUN]`: the validator was executed against
  the live catalog today — all 34 expected (table, column) pairs present against 676 live columns, CLEAN,
  would pass.** That is the single most demonstrable claim in this section: run it on screen.

*Also on the list, uncontested but lower-voltage — VI-3, all re-verified `[QUERIED]`/`[READ]` unless noted:*
three trading orgs (**CAD / USA / BCC**, with BCC carrying only 8 projects — the shell); client names via
`WBS1 → PR → Clendor`, not `CL`; the raw GL unusable for breakdowns so the curated sub-ledgers
(`Ledger AP/AR/EX/Misc`) are the only clean path; FX applied **per `PR.Org` bucket**, so a cross-border
rollup that ignores the bucket is wrong by roughly a third.

> ⚠ **VI-3 — two corrections to this list. Do not use either of the old versions.**
> **(a) `OdbcType.Date` is NOT the trap. It is the reverse, and it is a live landmine either way.**
> `[READ]` Both bindings were probed: `OdbcType.Date` returned **288 rows** on `tkDetail.TransDate`, while
> **`OdbcType.DateTime` threw a DataDirect protocol error.** Both forms appear in working production paths.
> **Delete "OdbcType.Date binds to nothing and silently returns zero rows" from the script — it is false —
> and do not let anyone "fix" a date binding before the demo.**
> **(b) A better trap is available, and it is from this month.** `[RUN]` The MCP service shipped
> `get_cash_position` summing **all 20 `CFGBanks` accounts instead of the 3 the accountant whitelisted**,
> because the config key naming them was silently dropped on the floor by the deployed build. It pulled in
> petty cash `1000.00` and USD savings `1175.00`. **The AI and the dashboard disagreed, and nothing errored.**
> That is the same failure family as the padded account codes, it is ours, it is dated, and it is fixed by a
> redeploy — which makes it a story about a working control, not an excuse.

> ⚠ **VI-3 — the one thing on this screen you must say before they read it.** `[QUERIED]` `MAX(Period)` in
> `PRSummaryMain` and `GLSummary` is **202602 — February 2026**, with **zero rows for March through August**.
> `AR` is current to today and `tkDetail` to 2026-08-23. So **WIP, Cash, Backlog, the GL P&L, Net Income
> (T12mo), the revenue trends and the Clients tab are all six months old**, on a screen KOR intends to call
> real-time, and **there is no staleness banner on any tile.** This is a Deltek-side posting gap, not a code
> bug. **Put the as-of period on the affected tiles before the demo, or open on AR / utilisation /
> collections — which genuinely are current to this morning — and say the boundary out loud.**

> ✅ **VI-3a RESOLVED — and the old line was false in BOTH directions. Never say it again.**
>
> **Revenue Generation is OFF in KOR's Vantagepoint tenant.** `[QUERIED]` `Revenue` equals `Billed` on
> **47,246 of 47,366 rows — 99.75%**. `Unbilled` is populated on **238 rows — 0.5%**, scattered 1–3 per
> month continuously from 201901 to 202512, so it is **a seven-year residue of manual entries, not a
> toggle** — there is no date where it switches on, and it is not per-Org (CAD 139 rows, USA 99, BCC 0).
>
> ⚠ **But `SUM(Revenue)` is $69,061,768.57 across 17,466 non-zero rows — NOT $0.** Precisely *because* RG
> is off, Deltek mirrors billings into the `Revenue` column, so it is fully populated. "RG is off, so
> `SUM(Revenue)` returns $0" was **falsifiable in about fifteen seconds by anyone with catalog access.**
>
> **The sentence to say instead, checkable on the spot:**
> *"Revenue Generation is off in our Vantagepoint tenant — Deltek mirrors billings into the Revenue column,
> so Revenue equals Billed on 99.75% of rows and Unbilled is populated on half a percent. That's why we
> derive unbilled WIP ourselves rather than reading Deltek's WIP."*
> Behind it: `SELECT SUM(CASE WHEN ABS(COALESCE(Revenue,0)-COALESCE(Billed,0))<0.01 THEN 1 ELSE 0 END),
> COUNT(*) FROM [<catalog>].dbo.PRSummaryMain;` → **47,246 / 47,366.**
>
> **Two consequences that follow, and both are usable.** `[QUERIED]` (1) `WipFinancialsService`'s detector
> is a bare non-zero test, so those 238 stray rows flip it onto the **Revenue-Generation branch on a tenant
> that has none** — it is reading the wrong column, and neither branch yields a defensible firm-wide WIP,
> because both draw their entire signal from 0.5% of the table. (2) **The WPF WIP tile is already
> deliberately hidden, and that is the correct call.** If asked why there is no WIP number:
> *"Deltek isn't running revenue recognition, so we don't publish a WIP figure we can't stand behind."*
> That is a strong answer that shows rigour. ⚠ **MCP `get_wip` is still live and will answer `/ask` from
> that residue — mute it or keep WIP questions off the screen.**
>
> **Also settled: the FX regime split is real and it is ours to fix, not a Deltek trap.** `[READ]`+`[RUN]`
> `App.config` supplies per-year rates (2026 = **1.378457**) but **only Partner Financials consumes them**;
> WIP, AR, Backlog, Billed P&L, Cash, Compensation and Utilization all use a flat **1.36**. Same window,
> two rates — a live WIP run at 1.36 gave **−$173,813.10** where the same code at 1.378457 gives
> **−$209,298.04**, a $35k gap. **Pick one rate before the demo. Do not volunteer this one; if USA numbers
> are compared across the Partner and P&L tabs they will not tie.**

*The sentence:* **"Anyone can buy an F2 capacity. Nobody can buy the year it took to find these — and they hand you a wrong number, not an error."**

**4. Win/loss, which Vantagepoint structurally cannot record.**
*Evidence, first-hand in KOR's own tenant.* **VI-9 RE-MEASURED 2026-08-20 — use these figures, not the
June ones.** `[QUERIED]` against live Deltek, and independently confirmed by a second module audit
segmenting on `ExternalSource`: `PR.Stage` = **`InPursuit` 176 · `LOST` 85 · `DNP` 8 · Won 0**; there is
**no WON value in the stage model at all**. `LostTo` populated on **3 of 85** losses. `ClosedReason` and
`OpportunityID` on **0** rows. `Probability` on 2,053.
*The sentence:* **"Pull your own numbers before you trust your win rate. Ours recorded zero wins and named the winner on three of eighty-five losses — it's not data entry, the stage model has no Won state."**

> ⚠ **VI-9 — refined, and the refinement is worth having ready.** Deltek carries a **loss** signal but no
> **win** signal. A won pursuit's `Stage` silently becomes `~WDEF~` when it converts to a project — there
> are **36,139 such rows** and zero `Won` rows `[QUERIED]`. KOR's sync now maps `~WDEF~` → Won (added
> 2026-07-11), but the pull queries *exclude* `~WDEF~`, so pursuits that converted before that date were
> never ingested to be promoted — **the live feed still shows 0 wins, and the cause is a backfill gap, not
> a missing branch.** KOR's own 177-win history is a **one-time hand-curated import frozen on 2026-05-23**,
> not the live feed. **Say "the stage model has no Won state" — that is exactly true. Do not claim KOR has
> solved win/loss capture; we have the same hole and we can show you where it is.** That framing is
> stronger than the boast, and it is the one a peer will believe.

**5. The BD pipeline's full combination.**
*Evidence:* no AEC vendor ingests public tender feeds at all — OpenAsset's Shred and Unanet's ProposalAI
work only on the firm's own content (VERIFIED absence, C4 §B). The vendors who do ingestion are GovCon
platforms (GovDash, $30M Series B 2026-01-15; Sweetspot; Procurement Sciences; pWin.ai) shaped around
NAICS, set-asides and recompetes — and **not one of them publishes entity resolution as a capability.**
KOR's sources (BC Bid, CanadaBuys, Bonfire, bids&tenders, CivicInfo, MERX) are in nobody's product.
**VI-10 CONFIRMED on the numbers `[QUERIED]`: 111 registered sources, 101 producing** — BC Bid, Alberta
Purchasing Connection, CanadaBuys, MERX, ~40 Bonfire tenants, ~25 bids&tenders tenants, CivicInfo BC,
SAM.gov, LA City RAMP — plus 139,472 contract awards, 50,811 building permits and 10,286 major-project
records. The Worker was heartbeating during the audit. **Say 111, not "100+".**
*The sentence:* **"The people who do ingestion are built around federal contract data. Ours is built around the portals our work actually comes through — and it resolves the organisation before it scores it."**

> ⚠ **VI-10 — three corrections, and they change what you may claim.**
> **(a) "Six-tier resolver" is unverified — drop the number.** What *is* verified `[QUERIED]` is better and
> checkable: **9,641 live canonical organisations, 9,641 distinct normalized names, zero duplicate groups.**
> Say *"provably duplicate-free on the live set"* and quote those three numbers. (The 4-tier cascade that
> was verified is the **person** key — email → LinkedIn → name+org → name — and its data is not clean:
> **211 email addresses map to more than one key**, and 58% of people rows have no email at all.)
> **(b) "Dedup — live and demoable today" is FALSE at the point that matters.** `[READ]` The duplicate
> scorer is wired **only to the manual-entry UI**; ingestion dedups on exact key alone. Consequence, live on
> the first screen: **the top eight rows by relevance score contain the same White Rock tender `WR26-021`
> four times**, and across 921 active opportunities there are **69 duplicate name-groups / 112 redundant
> rows (~12%)**. **In a demo whose headline is entity resolution, that is the most on-the-nose possible
> failure. Fix the Hub default view or do not open the Hub.**
> **(c) US coverage is dead, and MVE is in SoCal.** `[QUERIED]` **SAM.gov — the only US federal source —
> has returned HTTP 401 every run since 2026-08-02** (expired API key). What remains for California is LA
> City RAMP (84 rows) and four CA feeds that have inserted nothing, ever. **Rotate the key, or have the
> answer ready before "what do you see in California?" is asked.**
> **Also do not claim the AI research layer.** `[QUERIED]` Its three executors report `Success=1` with
> `considered=0; executed=0` every single day; their feeder job has never fired; **nothing has been produced
> since 2026-06-27.** It is visible in the app's own Job Run History window — pre-empt it, don't be caught.

**6. Four domains, one data estate.**
*Evidence:* every competitor in all four reports is a point solution — Trunk Tools never touches finance,
Unanet never touches drawings, Togal never touches BD, Higharc never touches commercial structural.
*The sentence:* **"No vendor sells this because no vendor has all four data sets. That's not a feature claim, it's an accident of being the firm."**

**One number worth having.** From the vendor with the loudest AI marketing in the sector: **only 1% of AEC
firms have achieved widespread adoption of AI-enabled processes**, and fewer than a quarter claim mature
or advanced AI readiness (BST Global, *AI + Data Insights 2026*, published 2026-05-04, VERIFIED). That is
the answer to *"isn't everyone doing this?"*

---

## 4. Where KOR is genuinely behind — concede on sight

**Getting caught hiding one of these is worse than all of them combined.** Say them before they are found.
Several are best said unprompted, early, as evidence you are being straight.

| # | The gap | The evidence against us | How to say it |
|---|---|---|---|
| 1 | **Rebar change detection.** Trunk Tools is ahead on the general problem. | TrunkReview: vision-language models, **clouded and unclouded** changes, 20-sheet bulletin in ~5 min, visual overlay + written narrative, 500+ jobsites, $70M raised. Ours reads callout **text**; a flattened or scanned sheet is **OCR'd and flagged "verify"**, not read as pixels. A VLM reads pixels. ⚠ **VI-7 CORRECTED** — the old wording *"blind to flattened or scanned sets by design"* is wrong: `[READ]` `PdfTextWithOcr.cs` OCRs image-only pages via WinRT before diffing. It has **zero test coverage** — do not stress it live. | "If your contractors run Trunk Tools, ours looks like a subset of it — because on the change list, it is. Ours goes deeper on one axis: the steel-weight delta." |
| 2 | **Bluebeam Max Smart Overlay** will commoditise generic change detection. | Cross-discipline, cross-scale, **in preview today at $590/user/yr**, in software MVE probably already owns. | "When that leaves preview, the generic half of our tool is a checkbox you already paid for. We're not investing further down that path." |
| 3 | **No RFI or submittal record type.** | **VI-11 CONFIRMED — the absence is total.** `[READ]`+`[QUERIED]` A suite-wide grep for `\bRFI\b`/`submittal` hits only FileSync **folder names** and unrelated BD prose, and the transmittal `Type` column carries **exactly three values: Transmittal / Transfer / Upload**. Newforma files an email **directly as** an RFI or submittal in **both** products. | "Clearest thing Newforma has that we don't. We file to a project; they file to a typed record. If we needed full CA workflow tomorrow we'd have a decision to make." **Pitch this as transmittals-and-transfer-tracking, never as a full Info Exchange replacement — otherwise the first question is 'where are the RFIs?'** |
| 4 | **No AI filing suggestion.** | **VI-12 CONFIRMED — filing is user-click only.** `[READ]` No sender→project mapping, no subject parsing, no thread memory, no last-used default anywhere; `SetSelectedProject` is only ever called from a user click. The add-in's "automatic" route just infers the project from **which Outlook folder the user dragged the mail into** — a manual choice made earlier. Konekt does *"smart project suggestions … based on email content and sender"*; Newforma says Smart Email Filing is *"deployed and in your hands today."* | "Their AI filing assistant is ahead of ours. Ours is deterministic, which I like for auditability, but it is not smarter." **Then pivot to the corpus — favourites make it two clicks, and 372,370 filed emails say the workflow is actually used.** |
| 5 | **Keyword search, not semantic.** | **VI-13 CONFIRMED — and there is no semantic tier hiding anywhere.** `[QUERIED]`+`[READ]` `dbo.SearchEmailsPaged` is SQL Server full-text over `ProjectNumber, Subject, FromEmail, BodyText`; tokens are wrapped `"tok*"` and ANDed. A grep for `embedding\|vector\|semantic\|cosine` across every email path returns **zero hits**. Newforma's Smart Search *"goes beyond keyword matching to understand the intent"* — **not GA**, funded on a 7-year AWS/Bedrock agreement (2026-07-28). | "Ours is fast and exact, not semantic. Their semantic search isn't out yet — and when it ships, they're ahead on search. I'd rather say that now than deny it." **Then run it: `seismic review` returns 7,216 hits in under a second across 372,370 emails and 955 projects going back to 2014-10-28, with a fully-populated, current full-text catalog `[QUERIED]`. That corpus is the argument — it searches message bodies, which Egnyte's date/sender/subject/type filtering does not.** |
| 6 | **No configurable link expiry, no reminders, no revision-issue register.** | **VI-14 CONFIRMED in both places `[QUERIED]`+`[READ]`:** no expiry column on `RedirectTargets`, **and no `ExpirationDateTime` set on the Graph sharing link** (`GraphFacade.cs:606-624`); no reminder code anywhere. Info Exchange has had all three for years. **Worse than "no expiry": on an external send the SharePoint link is `Scope="anonymous"`, so a forwarded email grants a stranger the files forever — and the click is logged under the original recipient's email.** Whether the tenant enforces a default anonymous-link expiry **could not be verified** — run `Get-SPOTenant \| Select ExternalUserExpirationRequired, ExternalUserExpireInDays` before the demo. | "Info Exchange does automated reminders, expiry and a revision register. We don't — and I'd rather tell you that a link of ours doesn't expire than have you find out. Retention is on my list, not shipped." |
| 7 | **Contract-administration depth generally.** | Newforma Project Center: RFIs, submittals, change orders, document control registers, field management, three editions. We built email filing, search and transmittals. | "Different scope, and theirs is larger. We rebuilt the 20% we actually used." |
| 8 | **Drawing review against standards / code compliance — nothing in this lane.** | Nomic is deployed inside **Arcadis** (strategic investment 2026-08-03, ~150 engineers across 12 countries, 86% said it changed their workflow) and **Aurecon**, with a self-serve tier at **$20/month**. | "Not yet, and there's a good product for it. I'd rather point you at Nomic than pretend." |
| 9 | **Structural quantity takeoff has a cheap near-neighbour.** | Kreo names Concrete, Framing, Steel and Masonry as trades with AI auto-measure at **$175/month**. ⚠ **VI-15 CORRECTED — "field-slab only" is stale and undersells it.** `[RUN]` The vector engine now prices **slabs incl. mats, walls, columns and footings** — a whole-building takeoff from the issued PDF alone, no Revit model and no CSV. A full 73-page set on job 31065 ran to **19,545 cy across 54 plates, exit 0, zero AI calls, $0** in `--deterministic` mode, and pages 12–51 and the whole set produce **byte-identical totals**. The honesty model is real: every plate is green (measured), orange (assumed, with the reason printed) or a named residual it refuses to price — **19 of 54 flagged, two `[Critical]`**. **The real gap is different: it has no button. It runs in a terminal.** | "A $175/month tool that's 80% right beats an internal one that's 95% right, to anyone who hasn't read the methodology." ⚠ **Accuracy, stated carefully:** the shipped brief (2026-07-04) records **−15% whole-building** against the full Revit model on 31065; a free-mode re-run on 2026-08-20 gives **−7.0% whole-building — but that total is error-cancelling** (slab −17.0%, walls −5.7%, **columns +135%**, foundations −17.7%), and the Revit answer key itself has 107 of 322 column rows at 0 m³. **Never quote −7% as accuracy.** The two facts worth saying are the ones that hold: **it is exactly reproducible, and it prices a 40-storey building for $0 with zero AI calls.** |
| 10 | **Institutional durability, and one untracked component.** | Newforma: 1,500+ firms, a support org, training, FedRAMP Moderate initiative. Ours: a bus factor, and `Kor.Transmittals.Redirector` **outside version control and uncompilable since 2026-03-17** `[RUN]` — **VI-1**. Two honest counterweights, both verified: the deployed redirector binary **hashes byte-identical to the source on disk** `[RUN]`, and the Revit estate — the thing that actually broke when a developer left — has been **designed out of that failure**: 137 tools built from one codebase across seven Revit versions, buildable on a machine with **no Revit installed** (API comes from NuGet), deployed from a share with **27 rollback snapshots** `[RUN]`+`[QUERIED]`. | Covered by objection row 1. **Fix the git gap before the demo — it is ~30 minutes.** And **do** tell the Revit continuity story unprompted: it is the strongest answer to "what happens when you leave" that KOR actually owns. |
| 11 | **Mobile / browser access.** | **VI-16 ANSWERED — and the answer splits cleanly.** `[QUERIED]` **KOR staff: none.** Everything is a WPF Windows desktop app that needs LAN or VPN for SQL — no mobile app, no browser client, no offline mode. **Recipients: yes, and it is good.** The transmittal link, the open pixel and the inbound file-drop page are served by a real internet-facing TLS service — `https://tracking.korstructural.com`, IIS, valid Let's Encrypt cert to 2026-10-09, `/health` → 200 from anywhere — and the file-drop page is **mobile-responsive (breakpoint at 640px)** with a client-side progress bar and reCAPTCHA. Konekt has a mobile app and browser access; Project Center supports filing from a phone or OWA. | **Concede the internal half plainly, then re-frame:** "Our people work from Windows desktops on the LAN, so there's no mobile client and I'm not going to pretend otherwise — that's a real gap against Konekt. **Where it mattered to us, we built for the phone: the person receiving your drawing set opens it from anywhere, and so does anyone sending files back in.** The client-facing half is on the public internet; the staff half deliberately isn't." |

---

## 5. The rare thing — DXF → ETABS with structural intent inference

**This is the one capability in the whole suite for which no commercial equivalent was found, and the
search for one was deliberate and repeated.**

**The incumbents have not moved. All VERIFIED against their own current release notes on 2026-08-20:**
- **CSI ships zero AI** in any 2025 or 2026 release — ETABS v23.3.0 / v23.3.1 (2026-07-02) and the
  csiamerica.com news feed through 2026-08-17 are code compliance (ACI 318-25, CSA A23.3-2024) and
  analysis mechanics. Zero mentions of AI or machine learning anywhere.
- **ETABS's own DXF import is manual tracing**, and CSI's documentation says so: the file must be
  pre-cleaned to exactly two layers, blocks exploded, curves manually pre-segmented into straight chords —
  and **ETABS does not detect walls versus columns versus openings**. It imports lines for a human to
  trace and assign (docs.csiamerica.com, DXF import).
- **Autodesk Robot Structural Analysis 2026**: the official What's New is entirely regional code updates.
  Zero AI, zero drawing import, zero model generation.
- **Bentley STAAD.Pro and RAM**: no AI, "basic DXF" only. **Tekla Structures 2026**: no AI or 2D-to-model
  features named on the product page.
- **Every mid-tier tool requires structured input** — IDEA StatiCa, Speckle, Karamba3D, RISA, Dlubal.
  None reads a raw architectural DXF and infers wall-versus-column, openings, headers or storey stacking.
- **The AI-native startups stop short**: Togal, Snaptrude, Skema, Swapp and Bild stop at geometry, BIM or
  takeoff. None claims loads, diaphragms or releases.
- **The venture money went elsewhere**: architectural massing (Motif $46M, Qbiq $16M), MEP (Endra $50M,
  Augmenta), GC document intelligence (Trunk Tools $70M, Document Crunch, Nomic), homebuilding (Higharc
  $95M Series C, 2026-06-30). A targeted search for a venture-funded structural-analysis-AI startup
  returned nothing across three engines.

**The Thornton Tomasetti distinction — say it before a briefed person raises it.**
Asterisk is real, it is the nearest published thing, and it is a different problem. Thornton Tomasetti's
own page: Asterisk *"generates structural solutions – in seconds – from a simple building **massing
model**"*, using *"computational geometry and machine learning built on our 70 years of structural
engineering experience"*, returning embodied carbon, cost per square foot, floor placement and weight.
**Massing model in, scheme out. Not the architect's issued plan set, and not a runnable model of a
specific building.**

> **"Asterisk answers *what structure could this massing take*. Ours answers *here is a runnable model of
> the building you issued last week*. The hard part isn't the analysis — it's classifying a ribbon of
> lines into wall panels with real centrelines and thickness, reading a sheet titled 'LEVEL 29 PLAN
> (L29-35)' as seven storeys, treating a ring inside a ring as an opening, and writing an `.e2k` with the
> geometry already entered. That's the part nobody is attempting."**

**VI-17 RESOLVED — what it produces today, on which buildings, and the one claim you must drop.**

*What it produces `[RUN]`, verified end to end today:* four files back into the job folder — the `.e2k`,
a plain-text report saying location by location what it could **not** do, an `.xlsx` of judgement calls
each answerable in one cell, and a PDF summary. **Job 31168 (YMCA Langara): 62 of 62 DXF sheets placed →
63 storeys / 1,119 wall panels / 2,462 columns / 82 floor plates / 4,233 joints, in 50.7 seconds wall
clock reading over SMB, exit 0.** **Job 31138 (2170 W 1st): 29 / 242 / 390 / 13**, merged into the
engineer's own export **without duplicating a thing** — the report reads *"306 wall(s) and 316 column(s)
were already modelled … not added again."* Three independent sources agree on both sets of figures: the
live run, the shipped report on the share, and the checked-in baseline test. **These are the correct
current numbers — any older figures in circulation (917 / 2,469 / 83, or 24 / 87 / 172 / 11) are stale
and appear nowhere in the system.**

*The rules are not constants:* **35 thresholds live as rows in a SQL database**, zero in C#, and a missing
rule **stops a production run by design — there is no fallback value** `[QUERIED]`. That is a genuinely
unusual claim and it is true. (One honest caveat if pressed: the enforcement currently covers the 32
numeric keys; the 3 layer-pattern keys fall back to defaults. All three rows are present today, and it is
a one-word fix.)

> ⚠ **DROP "an `.e2k` that ETABS itself will open." VI-17 could NOT confirm it.** `[DOC]`+`[RUN]`
> **Nobody has imported a generated model into ETABS.** ETABS is not installed on the development machine
> — no CSI folder exists under Program Files — so the ETABS half of the story cannot be shown from here at
> all. **Say instead: "it writes the `.e2k` in ETABS's own format, with the geometry already entered — and
> I'll be straight with you, we haven't yet sat an engineer down to import one and sign it off. That's the
> next thing on my list."** Then show the payoff that *does* work offline and needs no licence:
> `tools\Render-E2kModel.ps1` drew a **four-panel isometric / two elevations / composite plan PNG of the
> 31138 model in 9.2 seconds**, generated members in red, the engineer's own in grey `[RUN]`. That is the
> picture an architect audience actually wants.

> ⚠ **Two more boundaries to volunteer before they are found.** **(a) Two buildings, and both were used to
> tune it.** Six other engineer-authored models read cleanly but have no drawings. Say "two, and I'd want a
> third before I made a general claim." **(b) Do not invite an MVE drawing set.** `[READ]` KOR's layer
> defaults are `WALL`, `_COL`, `SLABEDG`; against a US National CAD Standard set the repo's own test
> documents the trap — `WALL` matches `S-CONC-WALL-NEW` by luck, **`_COL` misses `S-COLS` on the
> underscore and `SLABEDG` misses `S-SLAB-EDGE` on the hyphen** — and the result *"comes back with walls
> and no columns or floors, which looks like a building rather than like a failure."* Per-job layer
> overrides exist but **have never been run against a foreign drawing set.** The correct line is: *"it
> reads whatever layer names you tell it to — that's a database row now, not code — but I'd want to look
> at your standard first rather than guess on the spot."*

> ⚠ **The same layer gap is why "Revit → DXF → ETABS" is NOT a closed pipeline. Do not present it as one.**
> `[RUN]` KOR's Revit bridge really does export 110 views in 22 seconds under agent control, and the
> filename→storey contract is deliberate and correct. But the layers it actually emits are `A-WALL`,
> `S-COLS` and `A-FLOR` — so a run today would take **architectural partitions for structural walls, and
> return no columns and no floor plates.** Since 2026-08-15 those patterns are database rules rather than
> C# constants, **so the gap closes with a Revit export-layer table and three settings, not a code
> change.** That is an excellent "next sprint" answer. Presenting it as already working is the one move in
> this whole deck that could go badly with the one person qualified to catch it.

**The honest boundary, and say it unprompted.** **Higharc** raised **$95M on 2026-06-30** with **Simpson
Strong-Tie on the cap table** and auto-generates light-frame timber wall, floor and roof framing in real
time — and explicitly **self-limits**: *"more complex structural engineering continues to flow out as DXF
for engineers to complete."* That is proof the capital exists and the easiest typology is already taken.
Expect the boundary to move; it has not moved into concrete high-rise yet.

**One caveat you must keep in your own head.** The C4 research budget ran out before several names were
reached — Tekla's actual 2025–26 release notes, Graphisoft, Hypar, Konstru's current state, aSa, Autodesk
Takeoff AI, and a direct AEC-VC portfolio scan. So the correct phrasing in the room is **"I looked hard
and couldn't find one"**, never **"nobody has one."** The first is defensible; the second is one Google
search away from embarrassing.

---

## 6. Questions to ask MVE early — and why

Ask these in the first ten minutes, neutrally and casually, **before** building a segment on ground they
already own. KOR's own MVE dossier (2026-06-17) records **nothing** about their document or PIM stack —
do not guess it in the room.

| Ask | Why it changes what you do |
|---|---|
| *"What are you filing project email into today?"* | Decides which half of the Newforma segment you concede and which half you lead with. MVE is ~100 staff doing multifamily for Irvine Company, Toll Brothers, AvalonBay and Hines — squarely Newforma's historical core market. |
| *"Newforma — Project Center or Konekt?"* | Project Center has **no AI at all**; every AI feature is Konekt-only. The whole objection changes shape. |
| *"Do you run Deltek PIM?"* | If yes, drop "we built email filing" and lead with **transmittal + per-recipient download telemetry + SharePoint-native**. If no, filing and search are live capabilities, not table stakes. |
| *"What's your Deltek footprint — Vantagepoint, CRM or CRM Plus, GovWin IQ? Is Dela switched on?"* | Dela is **off by default** and enabled per security role. If it's on, they will benchmark you against it in real time. If they have CRM Plus + GovWin, they'll compare BD Brain to a $12k–42k/yr subscription that delivers rows, not dossiers. |
| *"Are you on Bluebeam Max?"* | If yes they already have Smart Overlay and Smart Review in preview. Pitch the rebar tool on the **steel-weight delta**, never on the change list. |
| *"Do you use OpenAsset?"* | If yes they will assume BD Brain is Shred.ai. It isn't — Shred works only on their own DAM content and **ingests no tender feeds**. Have that distinction ready. |
| *"Do your GCs run Procore or Trunk Tools?"* | That's how document AI reaches their projects, and it tells you which comparison is live in their head. |
| *"Who owns your Power BI semantic model today?"* | Surfaces whether they have already paid the curation cost Microsoft prescribes — and whether anyone still trusts the output. |
| If they claim Vojo is live: *"Which Konekt tier is that in, and do you have it enabled?"* | Fair, non-hostile, and the fastest way to tell a demo they saw from a feature they use. Newforma's Konekt release notes are unreachable publicly; their own answer is the best source available. |

---

## 7. Open items the owner must settle himself before the demo

Every unresolved verification the four reports flagged. **Items 1 and 2 are the ones that change what you
say on stage.**

| # | Item | How to settle it |
|---|---|---|
| **1** | **The Ask Dela aggregation contradiction — the single unresolved claim in C1.** Deltek's help page (7.0 and 7.2) says *"You cannot query Ask Dela for aggregate data, such as information about the top projects by revenue."* A Deltek article dated **2026-08-05** gives *"What are the top 10 projects by JTD revenue?"* as a live example and says *"These aren't roadmap promises."* Three possible explanations, none confirmed: the help tree is stale; the marketing outran the product; or it works for a narrow set of summary fields. | **KOR is a Vantagepoint customer — five minutes settles it.** Enable Ask Dela on a test role in KOR's tenant and ask, in order: (1) "What are the top 10 projects by JTD revenue?" (2) "Which PMs have the most WIP right now?" (3) "Show me AR over 90 days by client." (4) "Which projects are earning ahead of invoicing this quarter?" **Screenshot every answer.** Whatever it returns is the true baseline. **Do not stand in front of MVE asserting Ask Dela cannot do aggregates until you have run all four yourself.** |
| **2** | **Swapp's structural coverage.** Swapp claims **35M+ sq ft of production-grade construction documents** — *"dimensioned, tagged, sheeted, and QA'd to your firm's standards"* — and two independent fetches of swapp.ai found the site **deliberately avoids naming disciplines**. Secondary coverage mentions architectural and MEP. Structural is **neither claimed nor excluded**. It is the only company shipping AI-generated CD sets at production volume. | Request a demo, or ask directly: *"Do your outputs include structural sheets?"* This is the one question in the whole scan most worth resolving, because a yes changes the §5 white-space claim. |
| 3 | **Does MVE run Deltek PIM — and does PIM do client transmittals with per-recipient download tracking?** Not in the PIM 26.0 public help topic list; not on the product page; could not be verified either way. | Ask MVE (§6). If yes, confirm the transmittal question with a Deltek partner before the demo. |
| 4 | **Whether Newforma's Vojo / Smart Search / Smart Email Filing reached GA between 2026-05-07 and 2026-08-20.** Konekt release notes returned HTTP 404 to unauthenticated fetch on two URL forms; no dated June–August GA post found. | Ask MVE (§6), or a Newforma reseller. Until then, treat Smart Email Filing as **plausibly shipped** and Smart Search as **not GA**. |
| 5 | **Whether Konekt's Sharing Centre attributes a download to an individual recipient.** The docs state *"with whom"* and *"the number of times it was downloaded"* — the two are **not documented as joined**. KOR's strongest transmittal line leans on this. | If a Newforma contact is available, ask. Otherwise phrase the claim as "their docs record a count against a share", which is exactly what the docs say. |
| 6 | **Whether SharePoint exposes a per-link download report to an ordinary user** (as opposed to admin-only Purview `Search-UnifiedAuditLog`). The research pass ran out of budget before this was closed, and KOR's SharePoint argument leans on the gap. | Five-minute check on learn.microsoft.com for "file and page activity reports" before the demo. |
| 7 | **Egnyte's AEC-tier pricing** (Essentials / AEC Elite / AEC Ultimate). Only the general tiers ($10–$48/user/mo) are published. | Only matters if MVE says they are already on Egnyte AEC. Don't quote an AEC price. |
| 8 | **Bluebeam's per-recipient download audit trail; Procore's transmittal tracking granularity; Autodesk Docs' transmittal mechanics.** All unreachable in research (Studio FAQs silent; Autodesk pages 403/503). | If raised: *"I couldn't confirm what your Transmittals module tracks — what does it show you?"* Truthful, and it puts the burden on the person who uses it. |
| 9 | **Which LLM powers Dela, and whether customer data is used for training.** Not published on any reachable Deltek page. | If MVE raises data governance, KOR's own architecture is the stronger answer regardless of Deltek's — and **VI-18 CONFIRMS it `[QUERIED]`: the MCP service is `http://kor-app01:5500`, plain HTTP, RFC1918 (192.168.1.32), no TLS, no reverse proxy, no internet route — LAN-only by design.** `/health` returns 200, the service runs as `KOR\app-admin`, Basic auth is enforced (unauthenticated `/tools` → **401**), the Anthropic key lives **only on the server, never on a workstation**, and every question and tool call is written to an audit log with the caller's UPN. Model is `claude-sonnet-4-6`, current and not deprecated. ⚠ **This also answers "can we demo at MVE's office?" — NO, not without VPN.** ⚠ **And prepare the auth answer rather than improvising:** it is **one shared password** for the whole firm, committed to git in cleartext and byte-identical to the live server value, sent unencrypted over LAN HTTP, with **client-asserted identity via an unverified header**. The honest two-sentence version: *"Today it's a single service credential on a LAN-only endpoint with a full audit trail; per-user Windows Auth and TLS are the next step. What it is not, and never was, is our financial data leaving our building on someone else's key."* |
| 10 | **Deltek "Data at Your Service" (Snowflake) pricing, and whether ODBC carries a line-item charge.** Not published; Full Sail positions DaaS at 250+ employee firms. | Ask KOR's Deltek rep only if it becomes decision-relevant. If MVE is a DaaS candidate and KOR isn't, the correct line is that KOR's architecture reaches 30-minute-class freshness **without** the Snowflake tier — never that Snowflake doesn't exist. |
| 11 | **Unanet Champ pricing** (bundled or paid tier) and **Power Automate Premium / Agent 365 / M365 E7 pricing.** All unpublished. | Don't quote a number for any of them. |
| 12 | **`Kor.Transmittals.Redirector` is not in a git repo, and has not compiled since 2026-03-17** (**VI-1**, `[RUN]`, 2026-08-20). It is the one untracked thing external parties actually touch, and it blocks the honest answer to objection 1. | **BEFORE-DEMO. `git init` + commit the 2026-03-05 state as the baseline (~30 min), and reinstate a locally-constructed `GraphFacade` to clear the 5 build errors.** Until the build is fixed, no fix to anything else in that service can ship. **Also rotate, in the same pass: the hardcoded Azure AD client secret at `Program.cs:33` (it is the fallback the running service actually uses), the reCAPTCHA secret key beside it, and the `transmittals_app` SQL password — which is still the literal scaffold placeholder and is committed to git in `App.config`.** |

---

## 8. Do not say these

**These could not be substantiated. Each one, said in front of a well-read technical lead, discredits the
fifty claims in this document that are properly sourced. That is the whole cost — it is not that the claim
is wrong, it is that being caught inventing one name makes every real citation look invented too.**

> ⚠ **First, the seven claims about KOR's OWN systems that the module audits killed. These are more
> dangerous than anything below, because they are checkable in the room and they are ours to be wrong
> about.** Full detail and replacement wording in the Appendix.
>
> | Barred | Because |
> |---|---|
> | *"It's all in source control."* | The redirector is not in git and has not compiled since March **VI-1** |
> | *"A numbered transmittal register."* | It is a UTC timestamp, not a sequence **VI-2** |
> | *"RG is off, so `SUM(Revenue)` returns $0 firm-wide."* | RG is off, and `SUM(Revenue)` is **$69.06M** — false in both directions **VI-3a** |
> | *"`OdbcType.Date` binds to nothing."* | The reverse: `Date` returned 288 rows, `DateTime` threw **VI-3** |
> | *"Filed email is SharePoint-native / in our own M365 tenant."* | Zero Graph references in any email path; it is on the file server **VI-8** |
> | *"Blind to flattened or scanned sets by design."* / *"Field-slab only."* | It OCRs and flags them; and it prices whole buildings **VI-7, VI-15** |
> | *"An `.e2k` that ETABS itself will open."* / *"Revit → DXF → ETABS works."* | Nobody has imported one; the layer contract does not line up **VI-17** |

| Do not say | Why |
|---|---|
| **"Arup Neuron"** | **Could not be verified.** Searches on the exact term returned only unrelated Arup Group pages and **ARUP Laboratories, a medical-lab company**; arup.com returned 403 to every fetch. Arup's real, verifiable software programme is **Oasys** — a commercial software division since **1976**, selling GSA, AdSec, Compos, MassMotion, 50 years old in 2026, **with no AI-branded product on the page.** If you want an Arup example, use Oasys and use it accurately. |
| **"Gensler Product Development"** | **Could not be verified as a real named entity.** github.com/gensler exists with **zero public repositories**. What is public is Gensler Research Institute thought leadership on how AI affects workplace design — not a tool, not a product group. |
| **"Walter P Moore Applied Intelligence"** | **Could not be verified as a real public term.** walterpmoore.com has no mention of an internal software, automation or AI group; `/expertise/technology` and `/insights` both 404. Naming a competitor's internal group that may not exist is the single easiest way to lose a structural audience. |
| **The "Power BI report nobody trusts" statistic** | **There is no primary survey behind it.** No credible figure exists for AEC BI dissatisfaction, failed Power BI projects, or untrusted reports. The nearest thing is unquantified vendor marketing prose. **Argue the mechanism instead** — the Deltek quirks that fail silently (§3.3: padded account codes, project-currency labour dollars, column drift between releases), plus Microsoft's own documented nondeterminism. That is stronger than a statistic anyway, because it is checkable on the spot against KOR's own database — but only use the examples in §3.3, not the contested one. |
| "BSTPredict" / "BST Global Blackbird.ai" | Neither exists as a BST product (two independent search passes, VERIFIED absence). Blackbird.AI is an unrelated disinformation-intelligence company. |
| Any Newforma or Deltek **price** | Newforma publishes none; the same aggregator quotes both *"starts at $5,000 per license"* and *"$100–$500"* for the same product — they cannot both be right. Deltek publishes none. The defensible statement is *"per seat, quote-only, and it scales with headcount."* |
| Newforma **layoffs** | Glassdoor employee commentary only, uncorroborated, no press confirmation for 2024–2026. Raising it looks like mud-slinging and cannot be defended. |
| "Fabric has 20%+ **phantom CU consumption** on idle systems" | Uncorroborated, and sourced from TimeXtender — a vendor selling a competing platform. |
| "**Copilot bills jumped 25x** overnight" | That coverage is about **GitHub Copilot, not Copilot Studio.** Using it against Copilot Studio would be dishonest and is exactly the error a technical lead catches. |
| "Deltek can't see **Canadian** public work" | **False.** GovWin IQ covers federal, SLED **and Canada**. The accurate criticism is that it's a separate five-figure subscription delivering opportunity rows, public procurement only. |
| "**Deltek has no reporting**" / "**Newforma has no AI**" | Both false. The first will be corrected in the room in one sentence; the second ignores Newforma's own *"deployed and in your hands today."* |
| Gartner's *"80% of insights won't deliver outcomes"* | Never chased to a primary source. Same category as the Power BI statistic. |
| Bentley's 2026-08-13 **"Gold Stevie Award for AI Breakthrough"** | The headline exists but **could not be tied to any specific product**; the article body did not render. Do not cite it as evidence either way. |
| **"Nobody else does this"** — about anything except DXF→ETABS | And even there: say **"I looked hard and couldn't find one."** The C4 search budget ran out before roughly a dozen named candidates were reached. "Not checked" is not "found nothing", and the difference is exactly what a sceptic will probe. |

---

## Appendix — [VERIFY-INTERNAL] register: ALL 19 RESOLVED, 2026-08-20

Every claim in this battlecard that rested on **KOR's own** capability rather than a competitor's. The
eleven module audits have returned and closed all nineteen. **Six were corrected rather than confirmed —
those are the ones that could have cost the room, and each old wording is now barred.**

| ID | Verdict | What it now says | Tier |
|---|---|---|---|
| **VI-1** | ⚠ **CORRECTED** | `Kor.Transmittals.Redirector` is **not in git** and **has not compiled since 2026-03-17** (5 errors). **Drop "it's all in source control."** Counterweight that IS true: the deployed binary hashes **byte-identical** to the source on disk. **BEFORE-DEMO: `git init`, ~30 min.** | `[RUN]` |
| **VI-2** | ⚠ **CORRECTED** | Numbering is a **UTC timestamp**, `{project}-{yyyyMMdd-HHmmss}` (`GraphFacade.cs:352`) — three lines, no counter, no collision check. **Do not claim a numbered register.** | `[READ]` |
| **VI-3** | ✅ **CONFIRMED**, +1 correction, +1 warning | Quirks still encoded and current; `DeltekSchemaValidator` **CLEAN across all 34 columns against 676 live** — run it on screen. ⚠ `OdbcType.Date` is **not** the trap (it returned 288 rows; `OdbcType.DateTime` threw) — delete that example. ⚠ `PRSummaryMain`/`GLSummary` **stop at Feb 2026** with no staleness banner. | `[RUN]`/`[QUERIED]` |
| **VI-3a** | ⚠ **CORRECTED — was false in BOTH directions** | RG is **OFF** (`Revenue`=`Billed` on **47,246/47,366 = 99.75%**; `Unbilled` on 0.5%, a 7-year residue, never toggled). But **`SUM(Revenue)` = $69,061,768.57, not $0.** Replacement sentence in §3.3. ⚠ MCP `get_wip` still answers from that residue — mute it. | `[QUERIED]` |
| **VI-4** | ✅ **CONFIRMED**, with a boundary | Live on open: cold connect **960 ms**, loaders **252/82/180/77 ms**, total **1,559 ms**. Nothing cached but one YoY snapshot table. ⚠ **The pipeline is real-time; the ledger is not.** Lead with AR / utilisation / collections. | `[RUN]`/`[QUERIED]` |
| **VI-5** | ✅ **CONFIRMED end to end in production** | **829 transmittals · 4,284 per-recipient links · 741 external addresses · 2,682 clicks with zero null IP/UA/email · 88% with a recorded open.** ⚠ Lead with **clicks**, not the 8,947 opens (3.3:1, much of it scanner prefetch). | `[QUERIED]` |
| **VI-6** | ✅ **SETTLED: 23** | `GET /tools` returns exactly **23** registered tools (25 `.cs` = 23 + a wire ping + a static helper). ⚠ **19 of 23 answer correctly** — the deployed build is 34 days stale, breaking `get_wip`, `get_cash_position` and USD FX. One rebuild fixes all three. | `[QUERIED]`/`[RUN]` |
| **VI-7** | ✅ **CONFIRMED**, +1 correction | Steel-weight delta is real and shipped — change workbook + 2.3 MB on-drawing markup PDF on disk, logic covered in a 392-test green run. ⚠ **Not "blind to scanned sets"** — it OCRs image-only pages and flags them "verify". That path has **zero tests**. | `[RUN]`/`[QUERIED]` |
| **VI-8** | ⚠ **CORRECTED** | Filed **email is NOT SharePoint-native** — zero Graph/SharePoint references in any email path; it lands as `.msg` on `\\Kor-fs01\...\Newforma\email\`, and FileSync explicitly excludes that folder. **Transmittals are** SharePoint-native. The "if our software vanished" line survives — reword to "storage we own", not "our M365 tenant". | `[READ]` |
| **VI-9** | ✅ **CONFIRMED, re-measured** | `InPursuit` **176** · `LOST` **85** · `DNP` **8** · **Won 0**; `LostTo` on **3 of 85**; `ClosedReason`/`OpportunityID` on **0**. No WON value exists in the stage model. ⚠ Refinement: a won pursuit becomes `~WDEF~` (36,139 rows); KOR's own 177 wins are a frozen manual import, not the live feed. | `[QUERIED]` |
| **VI-10** | ✅ **CONFIRMED**, +3 corrections | **111 sources, 101 producing**; 139,472 awards; **9,641 orgs / 9,641 distinct names / 0 duplicate groups.** ⚠ Drop "six-tier" (unverified). ⚠ **Ingest-time dedup does not exist** — `WR26-021` appears 4× in the top 8. ⚠ **SAM.gov 401 since 2026-08-02**; AI research layer has produced nothing since 2026-06-27. | `[QUERIED]`/`[READ]` |
| **VI-11** | ✅ **CONFIRMED — absence is total** | No RFI, no submittal, anywhere. `Type` has exactly three values: Transmittal / Transfer / Upload. Pitch as transmittals-and-tracking, never as a full Info Exchange replacement. | `[READ]`/`[QUERIED]` |
| **VI-12** | ✅ **CONFIRMED** | No filing suggestion of any kind — no sender map, no subject parsing, no thread memory, no last-used default. Filing is user-click only. | `[READ]` |
| **VI-13** | ✅ **CONFIRMED — no semantic tier anywhere** | SQL full-text over subject/body/from/project; a grep for embedding, vector, semantic and cosine returns **0 hits**. The counter-punch: `seismic review` → **7,216 hits sub-second across 372,370 emails, 955 projects, back to 2014-10-28**, catalog current. | `[QUERIED]`/`[READ]` |
| **VI-14** | ✅ **CONFIRMED in both places** | No expiry column, **no `ExpirationDateTime` on the Graph link**, no reminders, no revision register. ⚠ Worse than "no expiry": an external link is `Scope="anonymous"`, so a forward grants a stranger the files forever — logged under the original recipient. Tenant-level default expiry **unverified**; run `Get-SPOTenant` before the demo. | `[QUERIED]`/`[READ]` |
| **VI-15** | ⚠ **CORRECTED — undersold** | Not field-slab only: it prices **slabs, mats, walls, columns and footings** whole-building from the issued PDF. **19,545 cy / 54 plates / exit 0 / $0 / zero AI calls**, byte-identical across two page ranges. 19 plates orange-flagged with printed reasons. ⚠ Accuracy is **error-cancelling** — never quote −7%. ⚠ **No button — it is CLI-only.** | `[RUN]` |
| **VI-16** | ✅ **ANSWERED — it splits** | **Staff: none** (WPF, Windows, LAN/VPN, no browser, no offline). **Recipients: yes** — `https://tracking.korstructural.com` is public, TLS (LE cert to 2026-10-09), `/health` 200 from anywhere, and the file-drop page is **mobile-responsive at 640px**. Concede the internal half, re-frame on the client-facing half. | `[QUERIED]` |
| **VI-17** | ✅ **CONFIRMED**, ⚠ one claim withdrawn | **31168 = 63 storeys / 1,119 walls / 2,462 columns / 82 plates** in **50.7 s**; **31138 = 29 / 242 / 390 / 13**, merged without duplication. 35 rules in SQL, none in C#, missing rule stops the run. ⚠ **DROP "an `.e2k` that ETABS itself will open" — nobody has imported one, and ETABS is not installed here.** Show the 9.2 s renderer PNG instead. ⚠ Two buildings only, both used to tune it. ⚠ Revit→DXF→ETABS is **not** closed (layer mismatch). | `[RUN]`/`[QUERIED]` |
| **VI-18** | ✅ **CONFIRMED — and it answers the venue question** | `/health` 200, service Running, Basic auth enforced (401 unauthenticated), key server-side only, model `claude-sonnet-4-6` current, full audit log with caller UPN. ⚠ **Plain HTTP on an RFC1918 address, no TLS, LAN-only — NOT reachable off the KOR LAN. VPN is mandatory at MVE.** ⚠ Have the auth answer prepared: one shared password, in git, unencrypted, client-asserted identity. | `[QUERIED]`/`[RUN]` |

**The six corrections, in one place — these are the sentences that are now barred:**
1. ~~"It's all in source control."~~ (VI-1)
2. ~~"A numbered transmittal register."~~ (VI-2)
3. ~~"Revenue Generation is off, so SUM(Revenue) returns $0 firm-wide."~~ (VI-3a) — and ~~"OdbcType.Date binds to nothing."~~ (VI-3)
4. ~~"Filed email is SharePoint-native / lives in our own M365 tenant."~~ (VI-8)
5. ~~"Blind to flattened or scanned sets by design."~~ (VI-7) · ~~"Field-slab only."~~ (VI-15)
6. ~~"An .e2k that ETABS itself will open."~~ (VI-17) — and ~~"Revit to DXF to ETABS works end to end."~~

---

**Source reports, all compiled 2026-08-20, with full URLs:**
`docs/audit-2026-08/competitive/C1-deltek-vantagepoint.md` ·
`C2-newforma.md` · `C3-microsoft.md` · `C4-field.md`

**Internal verification:** `docs/audit-2026-08/modules/01`–`11`, all compiled 2026-08-20, plus
`00-INVENTORY.md`, `02-CROSS-CUTTING-SCAN.md` and `RUBRIC.md`.
**Demo choreography:** `docs/audit-2026-08/06-DEMO-RUN-SHEET.md`.
