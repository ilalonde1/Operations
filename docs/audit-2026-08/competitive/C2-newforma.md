# C2 — Newforma and the Project-Information-Management Lane

**Prepared:** 2026-08-20 · **For:** MVE technical demo · **Subject:** Newforma (primary), adjacent PIM/CDE competitors (secondary)

**Evidence tags used throughout:**
- **VERIFIED** — primary source: newforma.com, Newforma help/release notes, company-issued press release, or the vendor's own docs.
- **REPORTED** — secondary: trade press, analyst, review site, employee-review site.
- **INFERRED** — my reasoning from the above. Always labelled.

Availability is stated as **GA / shipped**, **preview**, **announced (no date)**, **in development**, or **discontinued**. Where Newforma's own two sources disagree, both are quoted.

---

## 1. Executive verdict

**Newforma is not standing still, but the thing it has improved is not the thing KOR replaced.**

Three facts set the whole frame:

1. **Newforma's AI investment is going into Konekt, the cloud product — not into Project Center, the on-premises product that KOR used.** Vojo, Smart Search and Smart Email Filing are all Konekt features. Project Center's most recent release (2026.2) shipped Autodesk 2027 support, a .NET 10 Revit add-in, a MySQL upgrade, an Outlook add-in UI refresh and search-as-you-type project pickers. No AI. (VERIFIED — [PC 2026.2 release notes](https://projectcenter.help.newforma.com/whats-new/new-and-improved-features-in-newforma-project-center-2026-2/), accessed 2026-08-20)

2. **Newforma Info Exchange is not discontinued and not renamed — it is quietly stranded.** It is still fully documented as a Project Center component with download tracking and an audit trail. It appears nowhere on the current Project Center marketing page, nowhere in the 2026.2 release notes, and its overview help topic is stamped "last updated 3 years ago." Konekt does not have Info Exchange; it has a lighter Document Control "Share" + Sharing Centre with a **2-week link expiry** and a **download count** rather than per-recipient attribution. (VERIFIED — see §3, §5)

3. **The AI that would most directly compete with KOR's tool is mostly not GA yet, and Newforma's own two sources contradict each other on the one piece that might be.** Newforma's blog says Smart Email Filing is "deployed and in your hands today"; the company press release for the same announcement uses introduce/announce language with no availability date; and the Konekt packages page lists "AI Assist" as **Coming Soon**. Vojo and Smart Search are both described by Newforma as "in active development" / "coming soon." (VERIFIED — see §6)

**The honest bottom line for the demo:** MVE's technical lead can truthfully say Newforma has an AI email-filing story and a credible cloud roadmap backed by a 7-year AWS agreement. They cannot truthfully say, on the evidence available as of 2026-08-20, that Newforma ships GA semantic search over filed project email, or that Konekt's external file sharing matches a per-recipient download audit trail. **The gap KOR should defend is delivery mechanism and evidentiary quality, not "we have AI too."**

**And the real threat is not Newforma.** It is **Egnyte**, which shipped GA "Email Capture" on 2026-05-13 — Outlook filing into project folders, searchable, with an audit trail tracking *"every external access and download event"* — at published per-seat pricing. That is KOR's product, from a vendor, three months old. **The three things that still separate KOR from it are: Egnyte requires migrating off SharePoint, it costs per seat, and it has no numbered transmittal register.** Those three sentences are the defensible core of KOR's whole position, and they should survive contact with every competitor in this report. See §9.6.

---

## 2. Newforma product lineup as of August 2026

Newforma markets **three** products on newforma.com. A fourth name (Vojo) and a legacy name (Project Cloud) exist but are not in the product nav.

| Product | What it is | Positioning | Status |
|---|---|---|---|
| **Newforma Konekt** | Cloud-native AECO project information management; the BIM Track lineage | "Premier AECO cloud-based project information management." "Cloud-hosted construction project information management for improved communication, and increased efficiency." | **Active — the strategic product.** All AI investment lands here. |
| **Newforma Project Center** | On-premises, server-based PIM. The classic product; email filing + Info Exchange live here. | "Server-based, on-premises construction project information management for improved control, security, and accessibility." | **Active but maintenance-flavoured.** Still releasing (2026.2), no AI features. |
| **Newforma ConstructEx** | Cloud construction management aimed at general contractors | "Cloud-hosted construction management software." Submittal/RFI management, drawing sets, role-based workflows. | Active, out of scope for an architecture firm. |
| **Vojo** | AI assistant / agent framework layered on Konekt | "Newforma's conversational AI assistant" | **In active development** — announced May 2026, not on the products page. |
| **Newforma Project Cloud** | Older cloud offering; a separate legacy help site still exists | — | **INFERRED: legacy/superseded by Konekt.** Not marketed on the current products page; still reviewed on third-party sites. |

Sources: [newforma.com homepage/products](https://www.newforma.com/) (VERIFIED, accessed 2026-08-20); [Newforma Konekt page](https://www.newforma.com/newforma-konekt/) (VERIFIED); [Project Center page](https://www.newforma.com/newforma-project-center/) (VERIFIED); [legacy Project Cloud help](https://help.newforma.com/Newforma_Project_Cloud/Whats_New/What_s_New_in_Newforma_Project_Cloud.htm) (VERIFIED that it exists).

### Project Center editions (VERIFIED — [newforma.com/newforma-project-center](https://www.newforma.com/newforma-project-center/), accessed 2026-08-20)

- **Standard** — Manage Files, Manage Emails, **Search Everything**, Document Control, Action Items, Project Teams, Connectors (**Microsoft 365, SharePoint Online**, Bentley ProjectWise, ERP)
- **Contract Management** — adds RFIs & Submittals, change orders, Field Management
- **Enterprise** — adds Autodesk ACC/BIM 360/Revit/Navisworks data connectors, workflow connectors (Procore, ConstructEx, BIM 360), Bluebeam Studio connector

**Note for the demo:** Info Exchange, file transfer and transmittals are **not named on this page at all**. Email and search are. That is a marketing choice worth understanding before someone claims Info Exchange is a headline feature.

### Konekt packages (VERIFIED — [Konekt packages page](https://www.newforma.com/newforma-konekt/packages/), accessed 2026-08-20)

Four tiers: **Info Track**, **BIM Track**, **CA Track**, **Productivity Package (HUB) — "Coming Soon."**
Email-related line items include *"File Email as an Issue"* and *"File Email as an RFI/Submittal From Outlook Add-In."*
The **AI Features** section lists **"AI Assist — Coming Soon."** No published prices.

### What is being sunset

**Nothing has been publicly announced as sunset or end-of-life.** Newforma's stated posture is *"cloud-first, not cloud-forced"* and it explicitly states solutions are *"available both on-premises and cloud-hosted"* (VERIFIED — [PRNewswire 2026-05-07](https://www.prnewswire.com/news-releases/newforma-announces-ai-powered-innovations-and-open-ecosystem-strategy-at-newforma-world-2026-302765799.html)). Project Center *"remains a strategic part of that vision, supporting long-term and repeatable projects while enabling seamless transitions to cloud-connected environments"* (VERIFIED, same release).

**INFERRED (labelled):** the direction of travel is unambiguous even without an EOL notice — Project Center 2025.2 shipped *"updates to the project data migration from Newforma Project Center to Newforma Konekt"* (VERIFIED — [PC 2025.2 release notes](https://projectcenter.help.newforma.com/whats-new/new-and-improved-features-in-newforma-project-center-2025-2/)), Newforma publishes a customer migration case study titled *"Making the Move: How CESO Migrated From Newforma Project Center to Newforma Konekt"* (VERIFIED that the post exists — [Newforma blog index](https://www.newforma.com/resources/blog/), accessed 2026-08-20), and every 2026 AI feature is Konekt-only. A firm buying Project Center in 2026 is buying the product Newforma is building a migration path *off* of.

---

## 3. Newforma Info Exchange — status

**Verdict: still shipping, still documented, still does download tracking — but frozen and de-marketed. It has no counterpart of equal depth in Konekt.**

### What it is (VERIFIED — [Info Exchange Overview, Project Center Help](https://projectcenter.help.newforma.com/overviews/info_exchange_overview/), accessed 2026-08-20)

A **web-enabled server component of Project Center**, described in the docs as *"located outside of a firewall,"* through which internal staff exchange files with external parties via a website. It provides:

- email notifications and reminders
- **automated transfer expiration (deletion)**
- *"a history log (audit trail) for all posted file transfers"*
- the ability to *"see who has or has not downloaded specific file transfers, and, for transfers sent with Partial Download enabled, a list of which files were downloaded"*
- an option for *"anonymous access to anyone who visits your Newforma Info Exchange website"*

Download visibility is via the **Change Log tab** on a transfer (VERIFIED — [Info Exchange Activity Center help](https://projectcenter.help.newforma.com/activity-centers/info-exchange-activity-center/file_sharing_window/)).

Recipients who are **not** project team members *"will not have the ability to partially download files on Info Exchange"* (VERIFIED — [Transfer Files and Create an Outgoing Transmittal dialog](https://projectcenter.help.newforma.com/navigation/dialog-boxes/transfer_files_and_outgoing_transmittal/)).

### Is it still sold?

- **Not discontinued.** It remains in the live Project Center help set, with current-generation URLs (`projectcenter.help.newforma.com`), an Info Exchange Activity Center, a Quick Reference Guide, and how-to topics for creating transmittals through it. (VERIFIED, accessed 2026-08-20)
- **Not renamed.** `infoexchange.newforma.com` still resolves to a Newforma Info Exchange landing/redirect page. (VERIFIED)
- **Not cloud-only.** It is the on-premises path; Newforma states both deployment models remain available. (VERIFIED — PRNewswire 2026-05-07)
- **Could not verify** whether Info Exchange is licensed separately or bundled into a Project Center edition. Newforma publishes no pricing or SKU detail. Searched: newforma.com product and packages pages, Project Center editions page, Konekt packages page. The Project Center editions page does not name Info Exchange in any tier.

### The tell

Three independent signals that Info Exchange is receiving no investment:

1. Its overview help topic carries a **"last updated 3 years ago"** stamp. (VERIFIED, accessed 2026-08-20)
2. It is **absent from the current Project Center marketing page**, which does name Manage Files, Manage Emails, Search Everything, Document Control, Action Items, Project Teams and Connectors. (VERIFIED)
3. It is **absent from the 2026.2 and 2025.2 release notes.** (VERIFIED)

**INFERRED (labelled):** Info Exchange is in stable maintenance. Newforma's forward answer to "how do I get files to a consultant" is Konekt Document Control sharing plus the Sharing Centre (§5), which is a materially different — and in audit terms, weaker — mechanism.

**This matters for KOR's story.** KOR did not replace a product that Newforma has since rebuilt. KOR replaced a product Newforma has since left alone while moving its customers toward something with a shallower audit trail.

---

## 4. Email filing and email search

### Newforma Project Center (the product KOR left)

VERIFIED — [Project Email Management](https://www.newforma.com/newforma-project-center/organize-project-email/), [PC 2025.2 notes](https://projectcenter.help.newforma.com/whats-new/new-and-improved-features-in-newforma-project-center-2025-2/), [PC 2026.2 notes](https://projectcenter.help.newforma.com/whats-new/new-and-improved-features-in-newforma-project-center-2026-2/), all accessed 2026-08-20.

- **Filing:** Outlook add-in; drag-and-drop to a project folder; or auto-file by including the project's email address on send/forward/reply-all. *"The Newforma Outlook Add-in extracts key information from the email and auto-populates Project Center"* for workflow items.
- **Force filing:** 2025.2 added *"Force Email Filing on Send"* — a pop-up reminding users to pick a project — in the HTML add-in.
- **Add-in modernisation:** 2026.2 / Outlook Add-in 2.2.1 — *"Newforma updated the File Email, File on Send, and File Transfer pages to use new user interface components"* plus **dynamic search-as-you-type project search** with improved loading for large project lists.
- **Search:** marketed as **"Search Everything"** and *"Make project emails accessible and searchable for everyone on your team."* The marketing page does not state the search mechanism.
- **AI: none.** No AI feature appears anywhere in Project Center's marketing page or in the 2025.2 or 2026.2 release notes. (VERIFIED by absence)
- **Storage:** filed email lives in Project Center alongside RFIs and change orders. Team-wide retrieval: *"Everyone on the project who should have access to the email can easily retrieve it through Project Center."*

### Newforma Konekt (the current product)

VERIFIED — [Konekt Project Email](https://www.newforma.com/newforma-konekt/project-email/), accessed 2026-08-20.

- **Filing:** Outlook add-in; file to projects *"with just a few clicks, send and file simultaneously, or save entire email threads"*; file an email directly as an Issue, RFI or Submittal.
- **Assisted filing:** *"Smart project suggestions accelerate filing by intelligently recommending the correct project based on email content and sender."*
- **Search:** *"deep search capabilities"* across *"email content, attachments, and metadata,"* returning results *"within seconds."* The page does **not** claim semantic or AI search.
- **Storage location:** **could not verify.** The Konekt Project Email page does not state whether filed email rests in Newforma's cloud, a customer file share, or SharePoint. Searched: Konekt project email page, Konekt SharePoint connector page, Konekt file server connector help.

### Does Newforma have AI / semantic search over filed project email in 2026?

**Announced yes. Shipped — unclear, and Newforma's own sources disagree.** See §6 for the full evidence and the contradiction.

### Straight comparison to what KOR built

| | Newforma PC | Newforma Konekt | KOR |
|---|---|---|---|
| Outlook add-in filing | Yes (VERIFIED) | Yes (VERIFIED) | Yes |
| Suggested project on filing | Not stated | Yes — "smart project suggestions" (VERIFIED) | **Gap for KOR unless already built** |
| Force-file on send | Yes, 2025.2 (VERIFIED) | Not stated | Verify KOR parity |
| File email as RFI/Submittal/Issue | Yes via activity centers (VERIFIED) | Yes (VERIFIED) | **Gap — KOR files to folders, not to a contract-management record** |
| Full-text search of filed email | Yes, "Search Everything" (VERIFIED) | Yes, "deep search" (VERIFIED) | Yes — SQL Server full-text, paged, filterable by project/date/attachments |
| Semantic / AI search of email | **No** (VERIFIED by absence) | **Announced, in development** (VERIFIED) | **No — KOR's is keyword FTS. Be honest about this.** |

KOR-side evidence: `C:\VIsual Studio Projects\Operations\Kor.EmailSearch.Core\EmailSearchService.cs` calls stored procedure `dbo.SearchEmailsPaged` with a full-text condition and paging, filtering on project, date range and attachment presence. That is **keyword full-text search, not semantic retrieval.** Do not let the demo imply otherwise.

---

## 5. Transmittals, file transfer and download tracking

### Project Center + Info Exchange — the deep implementation

VERIFIED — [Transmittals Overview](https://projectcenter.help.newforma.com/Overviews/Transmittals_Overview/), [File Transfer Overview](https://projectcenter.help.newforma.com/overviews/file_transfer_overview/), [Create a Transmittal how-to](https://projectcenter.help.newforma.com/learning/how-tos/create_a_transmittal/), [Info Exchange Quick Reference Guide](https://projectcenter.help.newforma.com/learning/reference-guides/info_exchange_quick_reference_guide/), all accessed 2026-08-20.

- Transfers sent by email, via Info Exchange, or from a drag-and-drop location can all be **logged as a transmittal** in the Project Transmittals activity center.
- *"A compressed record copy of the transferred files is stored and linked to the transmittal record in the transmittal log."*
- **Download tracking:** who has and has not downloaded a given transfer; with Partial Download, which specific files were downloaded. Visible on the transfer's **Change Log** tab.
- Automated expiry/deletion, reminders, notifications, full audit history.
- Non-team-member recipients lose partial-download capability.

**This is a genuinely strong transmittal implementation and it predates KOR's tool.** It is the honest benchmark.

### Konekt — the current-generation replacement, and it is lighter

VERIFIED — [Konekt file server connector help](https://konekt.help.newforma.com/4408494681869-integrations-api/file-server-connector/newforma-konekts-file-server-connector/) and [Document Control Sharing and Issuance](https://konekt.help.newforma.com/document-control/document-control-sharing-and-issuance/), accessed 2026-08-20.

- Share files/folders from Konekt; *"Sharing creates a copy of the shared content at the time of share"* — a record copy.
- *"The recipient(s) will receive an email containing a link to download the record copy of the shared file."*
- **Link expiry: *"The download link will be available for 2 weeks."*** Hard-coded in the docs; no configurable retention stated.
- **Sharing Centre logs:** *"What files/folders were shared, With whom, When, and The number of times it was downloaded."*
- **Stated limitation:** *"History is only recorded for sharing; other actions to files and folders are not recorded in Newforma Konekt."*
- Document Control has a **Share** action to issue document revisions to stakeholders. Note the Document Control help topic is labelled **beta** in its "Add and Manage Files in Document Control (Beta)" article. (VERIFIED that the beta label appears in the help article title, accessed 2026-08-20)

**The precise difference that matters:** Konekt's Sharing Centre records *with whom* a file was shared and *how many times* it was downloaded. It does **not**, on the published documentation, attribute an individual download event to an individual recipient with a timestamp. Project Center's Info Exchange does do who-downloaded-what. **Konekt is a step backward from Info Exchange on audit granularity.** (VERIFIED on the two doc sets; the "step backward" characterisation is INFERRED from comparing them.)

### KOR's implementation — what the code actually does

Evidence from the repo, so the demo claim is defensible:

- `C:\VIsual Studio Projects\Operations\Kor.Operations.Data\SqlTransmittalsStore.cs` — download events are stored with **`RecipientEmail`, `ClientIp`, `UserAgent`** per event.
- Recipients are inserted with a **per-recipient `LinkId` (GUID) and `PersonalShareLink`** — i.e. each external party gets a distinct URL, which is what makes per-recipient attribution possible rather than a shared link with an aggregate counter.
- `C:\VIsual Studio Projects\Operations\Kor.Operations.App\Services\TransmittalService.cs` — composes the transmittal, sanitizes HTML, uses Microsoft Graph for delivery and an upload orchestrator for SharePoint, with a 10 MB attachment threshold above which the redirector link is used instead of an attachment.
- `Kor.Transmittals.Redirector` (net8.0-windows7.0, deps SQL + HTTP + SharePoint) is a separate self-hosted service — per `docs/audit-2026-08/00-INVENTORY.md`, **it is not in a git repo**, which is a real operational risk worth fixing before it is demoed as an asset.

**Honest scoring:** KOR ≈ Info Exchange on audit granularity and **ahead of Konekt** on it, with the addition of client IP and user agent per download, which neither Newforma product's docs claim. KOR **does** have formal transmittal numbering — `TransmittalService.SendAsync` calls `ReserveTransmittalNumberAsync(header.ProjectNumber)`, i.e. a per-project reserved sequence (VERIFIED by reading the code, 2026-08-20). KOR is **behind Project Center** on the rest of the surrounding apparatus: a grep of `TransmittalService.cs` and `SqlTransmittalsStore.cs` for expiry, reminder and revision-register logic returns nothing, so **automated reminders, configurable link expiry / auto-deletion, and a revision-issue register appear to be absent.** Info Exchange has had all three for years. Confirm before claiming parity.

### SharePoint

- **Project Center Standard** includes a **Microsoft 365 / SharePoint Online connector.** (VERIFIED — Project Center product page)
- **Konekt** lets you *"Add SharePoint as a Data Source,"* then *"View, access, share, and edit SharePoint files from Newforma Konekt,"* and add SharePoint files *"to RFIs, submittals, and change items while being able to search for files stored in SharePoint."* (VERIFIED — [Konekt SharePoint connector](https://www.newforma.com/app_market/sharepoint/sharepoint-newforma-konekt/), accessed 2026-08-20)
- **The distinction to hold onto:** in both Newforma products SharePoint is a **connected data source surfaced inside Newforma**. Newforma remains the system of record. In KOR's design SharePoint *is* the delivery surface and the store. That is a real architectural difference, and it is the difference that survives if you ever stop paying a vendor.
- A Konekt **File Server Connector** also mirrors on-premises file server content into Konekt using **Entra ID** authentication and mirrored folder permissions — Newforma's answer to firms that will not move their file server. (VERIFIED — Konekt file server connector help; the Feb-2025 dating of this capability is REPORTED via search summary and was not confirmed on a dated primary page.)

---

## 6. Newforma's AI story — precise status

**The single most important section for the demo.** Everything below is Konekt-only unless stated.

### Announced at Newforma World 2026 (2026-05-07)

VERIFIED — [PRNewswire, 2026-05-07](https://www.prnewswire.com/news-releases/newforma-announces-ai-powered-innovations-and-open-ecosystem-strategy-at-newforma-world-2026-302765799.html); [Newforma blog post, 2026-05-07](https://www.newforma.com/newforma-unveils-next-wave-of-ai-powered-innovation/); [engineering.com, 2026-05-08](https://www.engineering.com/newforma-outlines-ai-roadmap-and-ecosystem-updates/); [Architosh, 2026-05-13](https://architosh.com/2026/05/newforma-opens-up-with-ecosystem-and-ai/).

| Feature | What Newforma says it does | Status as of 2026-08-20 |
|---|---|---|
| **Vojo** | *"agent-driven framework designed to help users search project information, analyze content and complete tasks more efficiently"*; *"natural language search across project data, AI-driven analysis across BIM models and documents and AI-assisted submittal review workflows"*; elsewhere *"Newforma's conversational AI assistant"* that locates submittals, summarizes RFI threads, understands action items | **In active development / coming soon** (VERIFIED — Newforma's own AI blog post). Not GA on the evidence found. |
| **Smart Email Filing** | *"automatically associates emails with projects and recommends filing locations for RFIs, submittals, and action items"* — AI analyses correspondence and suggests or executes the filing destination | **DISPUTED — see below.** |
| **Smart Search** | *"goes beyond keyword matching to understand the intent behind a query, surfacing relevant documents, emails, submittals, and RFIs"*; a purpose-built AEC index | **In active development, "coming soon"** (VERIFIED — Newforma AI blog post). Not GA. |
| **"AI Assist"** (Konekt packages page) | Unspecified | **"Coming Soon"** (VERIFIED — Konekt packages page, accessed 2026-08-20) |
| **Agentic framework on AWS** | Underlying platform for *"interconnected, composable capabilities that can reason across data"*; specialized agents *"on the roadmap"* | Foundation deployed; specialized agents roadmap (VERIFIED — Newforma AI blog post) |
| **Amazon Bedrock document tooling** | *"automated tools for compliance and document review"* | Announced, no delivery timeline (VERIFIED — [AWS/Newforma PR, 2026-07-28](https://www.prnewswire.com/news-releases/aws-and-newforma-announce-strategic-7-year-collaboration-to-accelerate-customer-cloud-adoption-and-ai-innovation-302835695.html)) |

### The Smart Email Filing contradiction — quote both, and be fair

Newforma's own AI blog post states, under a heading *"This Is Not the Future. It's Already Underway,"* that Smart Email Filing is **"deployed and in your hands today,"** while Vojo and Smart Search are *"in active development"* / *"coming soon."* (VERIFIED — [How Newforma Is Building the AI-Powered Future of Construction Information Management](https://www.newforma.com/how-newforma-is-building-the-ai-powered-future-of-construction-information-management/), accessed 2026-08-20; the post carries no visible publication date.)

The company press release for the same announcement uses **introduce/announce framing with no availability date** for Smart Email Filing, alongside every other item (VERIFIED — [Newforma blog announcement 2026-05-07](https://www.newforma.com/newforma-unveils-next-wave-of-ai-powered-innovation/)). And the Konekt packages page still lists **"AI Assist — Coming Soon."**

**Recommended demo posture:** treat Smart Email Filing as **plausibly shipped in Konekt** and say so. Do not attack it. Attack nothing here — the honest line is *"their AI filing may well be live in Konekt; their AI search is not, and neither is on Project Center."*

### Could not verify

Whether Vojo, Smart Search or Smart Email Filing reached GA between 2026-05-07 and 2026-08-20. **Searched:** Konekt release notes (`konekt.help.newforma.com/360002798492-release-notes/` and `/release-notes/` — both returned HTTP 404 to an unauthenticated fetch), the Konekt help home page (no release-notes link exposed), the Newforma news index and blog index (no dated June–August 2026 AI-GA post visible). The most recent dated Newforma item found was the AWS press release of **2026-07-28**, which describes AI as an *"expanding AI roadmap"* with **no specific delivery timeline** — language that would be odd if Vojo had gone GA in the interim. **INFERRED (labelled):** Vojo was still not GA as of late July 2026.

**If MVE claims Vojo is live, ask them which Konekt tier it is in and whether they have it enabled.** That is a fair, non-hostile question and it is the fastest way to find out whether the claim is a demo they saw or a feature they use.

---

## 7. Company trajectory 2023–2026

| Date | Event | Tag |
|---|---|---|
| 2021 | Newforma acquires **BIM Track** | VERIFIED — [newforma.com/our-company](https://www.newforma.com/our-company/) |
| 2023 | **Newforma Konekt launched** from the BIM Track lineage | VERIFIED — same |
| **2023-04-03** | **Battery Ventures sells Newforma to Ethos Capital** (Boston PE). Terms undisclosed. Brock Philp stayed on as CEO at the time. | VERIFIED (seller's release) — [Businesswire 2023-04-03](https://www.businesswire.com/news/home/20230403005480/en/Battery-Ventures-Announces-Sale-of-Newforma-to-Ethos-Capital) |
| **2025-04-24** | **Peter Cannone appointed CEO**, replacing Brock Philp. A SaaS growth operator (DemandScience, ThriveHive/New Media, OnForce) — not an AEC lifer. Ethos founder Erik Brooks: *"Peter's appointment signals a bold new direction for Newforma"* and *"Peter's experience scaling cloud companies will help us meet the AECO industry's challenges head-on."* | VERIFIED — [Newforma press release](https://www.newforma.com/news-publications/newforma-names-saas-growth-strategist-peter-cannone-as-ceo/); [PRNewswire](https://www.prnewswire.com/news-releases/ceo-announcement-peter-cannone-joins-newforma-302440516.html) |
| 2025–2026 | Executive bench rebuilt: **Tim O'Neil** CFO (new), **Carl Veillette** CPO, **Ben Bazso** CTO, **Kevin Murray** COO, **Malcolm Tinkler** CRO | VERIFIED — [newforma.com/our-company](https://www.newforma.com/our-company/), accessed 2026-08-20 |
| **2026-05-07** | **Newforma World 2026** — Vojo, Smart Email Filing, modular user-based licensing, Egnyte partnership, Bluebeam Studio integration, Microsoft Teams integration, OFCDESK/Autodesk Build connector, FedRAMP Moderate initiative | VERIFIED — PRNewswire 2026-05-07 |
| **2026-07-28** | **AWS + Newforma 7-year strategic collaboration** — covers *"modernization of Newforma Project Center's cloud platform, continued evolution of the cloud-native Newforma Konekt platform using AWS services, modernization of supporting infrastructure"* and *"a parallel initiative to establish the compliance foundation for FedRAMP readiness."* AI via Amazon Bedrock. | VERIFIED — [PRNewswire 2026-07-28](https://www.prnewswire.com/news-releases/aws-and-newforma-announce-strategic-7-year-collaboration-to-accelerate-customer-cloud-adoption-and-ai-innovation-302835695.html) |

**Scale claims.** Newforma's own company page carries two inconsistent figures: *"With over 500,000 users in more than 1,500 firms worldwide"* alongside a stats block reading *"4.5 million Users, 20 million Projects, 24 million File Transfers, 6 million Submittals, 1 billion Emails."* (VERIFIED that both appear; **INFERRED** that the larger figures are cumulative-lifetime and the 500,000 is current installed base. Do not cite either as a hard number.)

**Layoffs.** Employee reviews on Glassdoor reference restructuring to trim costs and recurring layoffs. **REPORTED, weak source, uncorroborated** — [Glassdoor Newforma reviews](https://www.glassdoor.com/Reviews/Newforma-existing-Reviews-EI_IE267465.0,8_KH9,17.htm). No press-confirmed layoff event was found for 2024–2026. **Do not raise this in the demo.** It is unverifiable and raising it looks like mud-slinging.

**What the trajectory signals (INFERRED, labelled):** a PE-owned company on its second owner, with a growth-CEO hired specifically to scale a cloud business, a 7-year hyperscaler commitment, a FedRAMP push, and a licensing model rework. That is the profile of a firm optimising for ARR expansion and eventual exit. The practical consequence for a 40-person structural firm: **per-seat cost pressure and migration pressure toward the cloud SKU are more likely to increase than decrease.**

---

## 8. Pricing and licensing signals

- **Newforma publishes no prices.** Neither the Project Center page nor the Konekt packages page lists a price. (VERIFIED by absence, accessed 2026-08-20)
- **Licensing is per-user on both products**, and Newforma announced a **"simplified, modular user-based licensing model, enabling organizations to align purchases more closely with role-specific needs"** at Newforma World 2026. (VERIFIED — PRNewswire 2026-05-07). **INFERRED:** "modular, role-specific" is a tier-and-upsell structure; it lowers the entry price per seat while creating more SKUs to buy.
- **Project Center:** three editions (Standard / Contract Management / Enterprise), per-user. (VERIFIED — Newforma product page)
- **Konekt:** four packages (Info Track / BIM Track / CA Track / Productivity HUB — coming soon), custom pricing. (VERIFIED — Konekt packages page; four-edition count corroborated REPORTED by [G2](https://www.g2.com/products/newforma-konekt/pricing))
- **Third-party price estimates are unreliable and mutually inconsistent.** ITQlick quotes both *"starts at $5,000 per license"* and a *"$100–$500"* range for Project Center. (REPORTED — [ITQlick Project Center pricing](https://www.itqlick.com/newforma-project-center/pricing)). **These cannot both be a per-seat annual figure. Do not quote a Newforma price to MVE.** The defensible statement is *"per-seat, quote-only, and it scales with headcount."*
- A G2 reviewer flags **per-seat licensing as a cost burden for larger teams.** (REPORTED — G2)

---

## 9. Adjacent competitors — what MVE might raise instead

**The test applied to each:** does it do (a) email filing from Outlook into a project record, (b) search over that filed email, (c) transmittals to external parties with download tracking, and (d) does it work with SharePoint/a file share or is it a closed silo?

**Two headline findings, and neither of them is Newforma:**
- **Egnyte shipped "Email Capture" to GA on 2026-05-13** — Outlook filing into project folders, searchable, plus an audit trail tracking "every external access and download event." It is the closest product on the market to what KOR built, it is three months old, and Newforma announced a partnership with them six days before it shipped. **§9.6.**
- **Deltek PIM already does all four**, and KOR is already a Deltek customer. **§9.2.**

### 9.1 Scorecard

| Product | Email filing from Outlook | Search over filed email | Transmittals + download tracking | SharePoint / file share | Licensing |
|---|---|---|---|---|---|
| **Newforma Project Center** | **Yes** | **Yes** (full-text) | **Yes** — who downloaded what, via Info Exchange | Connector (SP is a data source) | Per-user, 3 editions, quote-only |
| **Newforma Konekt** | **Yes**, + AI-assisted | **Yes** ("deep search"); semantic in development | **Partial** — share log: with whom, when, **download count**; 2-week link expiry | Connector + file server connector (Entra ID) | Per-user, 4 packages, quote-only |
| **Deltek PIM** | **Yes** — "file project emails directly from Outlook... with a single click" | **Yes** — "search instantly across all projects for any email, document, or drawing" | **Yes, qualified** — transmittals + "complete audit trail... who accessed, edited, or approved each document and when"; per-recipient *download* granularity **could not be verified** | **Indexes in place** — network shares, OneDrive, Autodesk Docs, "without relocating or duplicating files" | Quote-only; no list price |
| **Deltek Vantagepoint** (without PIM) | **No** — Outlook add-in logs *activities/contacts*, does not archive the email | n/a | No | n/a | Per-user |
| **Egnyte (AEC)** | **Yes — GA 2026-05-13.** "Email Capture" Outlook add-in files threads into project folders; automated folder-mapping | **Yes** — by date/sender/subject/doc type, "AI-ready" | **Partial** — no tool named "Transmittals"; Secure Sharing (view-only, download restrictions, password, auto-expiry) + Audit Trail tracking **"every external access and download event"** | **Closed silo** — markets a wizard to **migrate off** SharePoint, not coexist with it | **Per-seat, published**: Team $10 / Business $22 / Ent. Lite $39 / Elite $48 per user/mo |
| **Bluebeam** | **No** — only Studio invite + activity-summary emails | n/a | **Partial / unverified** — Studio activity notifications; no confirmed per-recipient download log; no Transmittals tool | **Open** — "Integrate with SharePoint® and ProjectWise®"; DMS connectors incl. OneDrive, Box, Procore, Egnyte | **Per-seat, published**: $260 / $330 / $440 / $590 per user/yr |
| **Procore** | **Partial** — a project forwarding address accepts email; the structured **Correspondence** record is **manual-entry only** | **Partial** — Correspondence is searchable; Deep Search AI does **not** list Correspondence/email among covered types | **Yes (partial)** — dedicated Transmittals tool (To/CC, forward by email, PDF/CSV export, email log). Per-recipient download tracking **not documented** | **Could not verify** — no SharePoint connector found in reachable docs | **Per Annual Construction Volume — not per seat.** "Unlimited users," "we'll never charge you for adding more users" |
| **Autodesk Docs / ACC** | **Could not verify; likely no.** Build has a "Correspondence" nav item but the page 503'd repeatedly | **Could not verify** — Autodesk Assistant covers Build/Takeoff/Docs content, no email claim | **Partial** — "Transmittals" confirmed as a standalone module in the Docs help nav; tracking mechanics unreachable (503) | **Could not verify** — integration pages 403'd | **Could not verify** — pricing pages 403'd |
| **Revizto** | **No** — email *notifications* only | n/a | **No documented module** | **Open** — connects SharePoint, Procore, ACC, Box as a CDE | User / project / enterprise plans, quote-only |
| **OpenSpace** | **No** | n/a | **No** — shared folders + public links, no per-recipient tracking | Export-based; no live SharePoint indexing | **Annual Construction Volume %**, unlimited users |
| **SharePoint Premium / Syntex** | **No, not native** — needs Colligo or a custom Power Automate flow | n/a | **No** — "Manage Access" shows current permissions, not download history; real tracking needs Purview `Search-UnifiedAuditLog` (admin tool, not a project UI) | **It is** the file share | Azure pay-as-you-go meters on an M365 base; **no per-user SharePoint Premium SKU** |

### 9.2 Deltek PIM — the sharpest adjacent threat, and the one to be ready for

**This is the product whose marketing copy reads closest to KOR's own tool.** Deltek PIM does single-click Outlook filing, full-text search across email/documents/drawings, transmittals with an access audit trail, and — importantly — **indexes files where they already live** (network shares, OneDrive, Autodesk Docs) rather than relocating them, which is architecturally the same instinct as KOR's SharePoint-native design.

Two things blunt it:
- **It is a separate Deltek product from Vantagepoint.** Vantagepoint's Outlook add-in logs CRM activities and prefills contacts from an email; it does **not** archive the email. A firm running Vantagepoint alone does not have email filing. (VERIFIED — [Vantagepoint Outlook Add-in help](https://help.deltek.com/product/Vantagepoint/2.0/outlook_Microsoft_Outlook_Add-in.html))
- **Per-recipient download granularity is unverified.** Deltek's copy says "audit trail... who accessed, edited, or approved each document and when" for Teamwork external users — an access trail, not demonstrably a per-recipient transmittal download log. (Could not verify; checked the PIM product page and PIM Teamwork page.)

**Why this matters internally more than for the MVE demo:** KOR already pays Deltek and already pulls Deltek data over ODBC. If someone at KOR ever asks "could we have just bought Deltek PIM?", the honest answer is *"it is the closest thing on the market to what we built, and it would still have put the record in Deltek's index rather than our tenant, and it would still not have joined a transmittal to a BD pursuit record."* Have that answer ready. **REPORTED** third-party estimates put Vantagepoint at roughly $30–$200/user/month depending on modules, with implementation quoted separately — unverified against Deltek, which publishes no list price.

Sources: [Deltek PIM](https://www.deltek.com/products/delivery-assurance/project-information-management/), [Deltek PIM Teamwork](https://www.deltek.com/en/products/enterprise-information-management/project-information-management/teamwork), [Deltek Dela](https://www.deltek.com/products/platform/dela/) — all VERIFIED vendor primary, accessed 2026-08-20.

### 9.3 Deltek Dela (AI) — partial GA, module by module

Dela is **"the brand umbrella"** for Deltek AI and is explicitly **"available in select Deltek solutions"** — i.e. not uniformly GA. Concretely shipped: **Ask Dela + Smart Summaries + role-based access controls in Vantagepoint 2026.1**, with accuracy improvements in 2026.3; Ask Dela in Costpoint; a Dela "Smart Search" in GovWin IQ. **No Dela feature specific to email search or transmittal tracking was found.** (VERIFIED — [Vantagepoint 2026.1 release notes](https://help.deltek.com/product/Vantagepoint/2026.1/ReleaseNotes/DeltekVantagepoint202611ReleaseNotes.htm), [2026.3 release notes](https://help.deltek.com/product/Vantagepoint/2026.3/ReleaseNotes/DeltekVantagepoint20263ReleaseNotes.htm))

### 9.4 Revizto and OpenSpace — not in this lane, say so and move on

- **Revizto** is BIM/issue coordination. It has no email filing and no documented transmittal module; a Revizto blog post discusses transmittals as general industry practice, not as a named feature. It *consumes* documents from SharePoint/Procore/ACC/Box as a CDE. Its one 2026 AI move — **MCP Server + Model & Object Properties API, GA 2026-07-28** — opens live model data to external LLMs and agents ("bring your own AI"), which is genuinely interesting but touches neither email nor transmittals. (VERIFIED — [Revizto newsroom, 2026-07-28](https://revizto.com/resources/newsroom/company-news/revizto-expands-platform-enterprise-ai-integrations); [CDE integrations help](https://help.revizto.com/hc/en-us/articles/4415779193871-Managing-CDE-integrations))
- **OpenSpace** is reality capture. No email, no transmittals, no per-recipient download tracking; sharing is folders and public links. AI investment 2025–26 went entirely into field capture — **OpenSpace Field reached GA 2026-02-03** with AI Autolocation, Voice Notes, Image Enhance and two-way Procore/ACC sync. Priced on **Annual Construction Volume**, not per seat. (REPORTED — [PR Newswire, 2026-02-03](https://www.prnewswire.com/news-releases/openspace-announces-general-availability-of-openspace-field-bringing-visual-intelligence-directly-into-field-execution-302677091.html); [OpenSpace FAQ](https://www.openspace.ai/faq/) VERIFIED)

**If MVE raises either, the answer is one sentence:** *"Different lane — those are coordination and capture tools, they don't file email or run transmittals."*

### 9.5 Microsoft SharePoint Premium / Syntex — the "you could just use Microsoft" objection

This is the objection most likely to be aimed at KOR, because KOR's own answer is "we built on SharePoint." The evidence closes it cleanly:

- **No native Outlook→SharePoint email filing exists.** It requires a third-party add-in (Colligo) or a custom Power Automate flow. (REPORTED for the add-ins; **INFERRED** for the absence — this is absence of evidence in Microsoft Learn, not hard negative proof.)
- **No native transmittal download tracking.** SharePoint's "Manage Access" shows *current permissions*, not historical download events. Actual download auditing requires **Microsoft Purview Audit / `Search-UnifiedAuditLog`** — an admin/compliance surface, not a project-facing UI, and dependent on licensing and retention configuration. (VERIFIED — [Microsoft Support: see who a file is shared with](https://support.microsoft.com/en-us/sharepoint/sharepoint-sharing-and-permissions/see-who-a-file-is-shared-with-in-onedrive-or-sharepoint))
- **Pricing is Azure pay-as-you-go metering on top of an M365 base**, with no dedicated per-user SharePoint Premium SKU: OCR $0.001–$0.01/txn, translation $15/1M chars, content assembly $0.15/doc, **eSignature $2.00/request**, Archive $0.05/GB/mo, extra storage $0.20/GB/mo. (VERIFIED — [Microsoft Learn pay-as-you-go pricing](https://learn.microsoft.com/en-us/microsoft-365/documentprocessing/syntex-pay-as-you-go-services?view=o365-worldwide), page updated 2026-06-01). **Microsoft 365 Archive is in public preview** per the same page.
- **The brand has churned repeatedly:** Syntex → SharePoint Premium (2023) → in 2026 split into standalone SharePoint Advanced Management / Backup / Archive, with the AI content services relabelled **"Document Processing for Microsoft 365."** (REPORTED — one independent MVP blog; a Microsoft primary announcement of this rename **could not be located**.)
- **Legacy SharePoint In-Place Records Management and Information Management Policies are being retired as of April 2026** in favour of Microsoft Purview Data Lifecycle & Records Management. (VERIFIED — [Microsoft Learn](https://learn.microsoft.com/en-us/sharepoint/use-microsoft-purview-risk-and-compliance-solutions))

**The answer to "why not just use SharePoint natively":** *"We do use SharePoint — as the store. Microsoft doesn't ship Outlook email filing into a project record, and it doesn't ship per-recipient download tracking outside of Purview audit logs that no PM will ever open. That gap between 'we have SharePoint' and 'we have a project record' is precisely what we built."* **This is one of KOR's strongest and most verifiable lines.**

### 9.6 Egnyte — **the single closest competitor to KOR's tool, and it went GA three months ago**

**Read this section before the demo. It is the most consequential finding in this report, and it is not Newforma.**

On **2026-05-13** Egnyte announced GA of **Email Capture** — an Outlook add-in that files email threads into shared project folders, organised by year/month, searchable by date, sender, subject and document type, with automated folder-mapping on all Next Gen Platform tiers and manual filing on the AEC Elite / AEC Ultimate tiers. Egnyte's own framing is *"turn project email into project record."* (VERIFIED — [Egnyte press release, 2026-05-13](https://www.egnyte.com/press-releases/egnyte-targets-data-fragmentation-automated-email-and-content-governance); [Email Capture blog, 2026-06-30](https://www.egnyte.com/blog/post/email-capture-turn-project-email-into-project-record))

Alongside it: **Proposal Coordinator**, **AI Connectors** (MCP-based, into Outlook / Slack / DocuSign / Salesforce), automatic metadata tagging and image captioning for drawings and photos — all GA in the same announcement.

On external sharing, Egnyte's AEC page markets **Secure Sharing** — view-only access, download restrictions, password protection, auto-expiring links — plus an **Audit Trail** that tracks *"every external access and download event."* (VERIFIED — [egnyte.com/industries/aec](https://www.egnyte.com/industries/aec), accessed 2026-08-20)

**That is email filing + email search + externally-shared-file download auditing, GA, from a vendor with published per-seat pricing ($10 / $22 / $39 / $48 per user/month on the general tiers).** It is the closest thing on the market to what KOR built.

**And Newforma partnered with them on 2026-05-07** — six days before Egnyte's GA announcement. (VERIFIED — Newforma World 2026 press release)

**Three things blunt it, and they are the same three that carry the whole KOR argument:**

1. **Egnyte is a closed silo with respect to SharePoint.** Egnyte does not coexist with a live SharePoint tenant — it markets a **migration wizard to move you off SharePoint** into Egnyte storage, with documented limits (5 TB / 5 M objects, permissions not migrated). (VERIFIED — [SharePoint→Egnyte migration blog](https://www.egnyte.com/blog/post/a-faster-smarter-way-to-migrate-from-microsoft-sharepoint-to-egnyte); [Egnyte helpdesk SPO migration article](https://helpdesk.egnyte.com/hc/en-us/articles/27710200349965-SharePoint-Online-SPO-to-Egnyte-Cloud-Migration)). **Adopting Egnyte means leaving the tenant KOR already pays for.** This is the exact opposite of KOR's architecture.
2. **It is per-seat, and the price is public.** At ~40 staff, Elite at $48/user/month is roughly **$23,000/year** before AEC-tier pricing (AEC-specific price points **could not be verified** — no price table on the AEC page). That is a recurring, headcount-coupled cost against a fixed internal one.
3. **There is no transmittal register.** Egnyte has secure sharing with an audit trail; it does not have a numbered transmittal record tied to a project. KOR does (`ReserveTransmittalNumberAsync`).

**INFERRED (labelled):** the Newforma–Egnyte partnership press language is Newforma-side only — Newforma *"acts as an intelligent layer across existing storage environments, enabling direct connectivity with platforms such as Microsoft SharePoint, Autodesk Docs, and a myriad of third-party systems."* It describes Newforma reaching into Egnyte, **not** any new Egnyte capability, and mentions neither email filing nor transmittals. Treat it as a connector/ecosystem announcement, not a capability merger. Two vendors now ship overlapping email-filing products and have announced a partnership; **that overlap is unresolved and worth watching.**

### 9.7 Bluebeam — adjacent, not competitive, but the SharePoint line is useful

- **No email filing.** The only email functions are Studio Session invitations and optional daily activity-summary notifications. Nothing captures inbound project email. (VERIFIED — [Studio FAQs](https://support.bluebeam.com/studio/resources/studio-faqs.html), [notifications doc](https://support.bluebeam.com/studio/how-to/set-notifications-and-alerts.html))
- **No Transmittals tool**; Studio Projects/Sessions give "notifications about file and user activity," but **per-recipient download logging could not be verified.**
- **Openly SharePoint-friendly** — the pricing page states plans *"Integrate with SharePoint® and ProjectWise®"* and lists DMS connectors for SharePoint, OneDrive, Box, Dropbox, Procore and Egnyte. (VERIFIED — [bluebeam.com/pricing](https://www.bluebeam.com/pricing/))
- **Per-seat, published:** Basics $260 / Core $330 / Complete $440 / **Max $590** per user/yr, billed annually, introductory rates locked through 2027 renewal. (VERIFIED, same page)
- **AI: GA 2026-05-19** — **Bluebeam Max** launched globally: Revu + **Anthropic Claude via MCP**, AI-REVIEW / AI-MATCH (from the Firmus AI acquisition), Smart Overlay, Smart Review, Magic Markups, Stitching, Connected Studio Sessions with Revit. In beta with 2,000+ early adopters since Unbound 2025 (Oct 2025). (VERIFIED — [press.bluebeam.com, 2026-05-19](https://press.bluebeam.com/2026/05/bluebeam-max-launches-globally-bringing-ai-powered-productivity-to-aec-teams-everywhere/); REPORTED — [Nemetschek, Oct 2025](https://www.nemetschek.com/en/news-media/bluebeam-unveils-bluebeam-max-next-generation-ai-powered-innovations-unbound-2025))
- The **Bluebeam ↔ Newforma Konekt** integration announced 2026-05-07 carries **no technical detail** in the press release; the older Bluebeam ↔ Project Center submittal-review integration continues separately. (VERIFIED existence only)

**If MVE raises Bluebeam:** it is almost certainly already in the building, and it is complementary, not competitive. *"Bluebeam is markup and Studio — it doesn't file email and it doesn't run transmittals. It's a peer to our tool, not a replacement."* Bluebeam is also **evidence for KOR's architecture**: a major AEC vendor explicitly integrating *with* SharePoint rather than replacing it.

### 9.8 Procore — the pricing model is the interesting part

- **Email filing is partial and the mechanism matters.** A project-specific forwarding address accepts "daily logs, photos, documents, and emails," but the **Correspondence** tool — the structured project record — is documented as **manual-entry only**: *"enables you to send custom correspondences to collaborators."* No Outlook add-in ingestion into Correspondence is documented. **Raw email can land; the structured record is not auto-populated the way KOR's add-in does it.** (VERIFIED — [Procore Documents guide](https://support.procore.com/products/online/user-guide/company-level/documents), [Correspondence guide](https://support.procore.com/products/online/user-guide/project-level/correspondence))
- **Transmittals: a real, dedicated tool** — To/CC recipients, forward by email, PDF/CSV export, email correspondence view. **Per-recipient view/download tracking is not stated in the documentation reached — could not verify.** (VERIFIED — [Transmittals guide](https://support.procore.com/products/online/user-guide/project-level/transmittals))
- **Search:** Correspondence supports search/sort/filter. Procore's **Deep Search** AI agent cross-references specs, drawings, RFIs and submittals with citations — **Correspondence and forwarded email are not listed among its covered record types.** (VERIFIED — [procore.com/ai](https://www.procore.com/en-ca/ai))
- **Pricing is the standout: per Annual Construction Volume, not per seat.** *"Unlimited users"*, *"we'll never charge you for adding more users."* (VERIFIED — [procore.com/pricing](https://www.procore.com/pricing)) **This defuses KOR's "no per-seat licence" argument if Procore is the comparison** — and it is the one competitor where that line does not land. Know it before you use it.
- **AI:** Deep Search, Submittal Review, RFI, Daily Log (*"converts field photos, emails, video, voice into completed logs"* — the one place email meets AI), and Contract Review agents are presented as live on the product page, with **no GA/preview labels or dates.** **Could not verify formal GA** — procore.com/newsroom and investors.procore.com are JS-rendered and unreachable.
- **SharePoint connector: could not verify** — none found in reachable docs.

### 9.9 Autodesk Docs / Autodesk Construction Cloud — mostly unverifiable this pass, and mid-rebrand

**Notable:** `construction.autodesk.com/products/autodesk-docs/` **301-redirects to `autodesk.com/products/forma-data-management/overview`** (observed 2026-08-20) — Autodesk Docs/ACC appears to be folding into an **"Autodesk Forma Data Management"** brand. The destination page 403'd, so the scope of the rename **could not be confirmed.**

- **Transmittals module confirmed to exist** — "Transmittals" is a standalone nav item in the Autodesk Docs help site, distinct from Build's "Correspondence." **What it tracks could not be verified** (every detail page 503'd).
- **Email filing: no claim found anywhere reached.** Build's nav lists "Correspondence" but the page was unreachable. **INFERRED, low confidence:** given Autodesk markets document and AI features heavily and never mentions email capture — unlike Egnyte, which markets it explicitly — this is probably not a capability. **Absence of evidence, not proof.**
- **AI:** Autodesk Assistant, Construction IQ, AutoSpecs, Autotags, Sheets and Specifications tools, symbol detection, financial data extraction, bid forwarding, subcontractor recommendations — all listed as current on the AI workflows page with **no GA/preview labels or dates**; the Autodesk news feed showed no ACC AI releases in its most recent items (2026-07-22 to 2026-08-04). (VERIFIED that the features are listed — [construction.autodesk.com AI workflows](https://construction.autodesk.com/workflows/artificial-intelligence-construction/))
- **SharePoint integration and pricing: could not verify** — autodesk.com pages returned 403 on every attempt; marketplace and review-site fallbacks failed.

**Honest handling in the room:** if MVE raises Autodesk Docs, **do not claim it lacks features.** Say *"I couldn't confirm what their Transmittals module tracks — what does it show you?"* That is truthful, it is a good question, and it puts the burden on the person who actually uses it.

---

## 10. The demo section

### 10.0 What we do not know about MVE — check before the room

**Could not verify what MVE actually runs.** KOR's own MVE dossier (`docs/bd-dossier-mve-mclarand-2026-06-17.md`, 2026-06-17) covers the firm, its people and its structural-engineer defaults, but records **nothing about MVE's project-information-management or document stack.** MVE is ~100 staff, employee-owned, Irvine HQ with LA / San Diego / SF / Denver / Guadalajara offices.

**INFERRED (labelled):** at ~100 staff doing multifamily and mixed-use for The Irvine Company, Toll Brothers, AvalonBay and Hines, MVE is squarely in Newforma's historical core market and is more likely than not to have Newforma or Procore in the building. **Ask early and casually — "what are you filing project email into today?"** The answer changes which half of §10.1 you concede and which half of §10.3 you lead with. Do not guess it in the room.

### 10.1 What MVE could truthfully claim

Each of these is defensible on the evidence. Concede them cleanly and early — conceding accurate points is what buys credibility for the ones you contest.

1. **"Newforma files email from Outlook and has for twenty years."** True, in both products, with drag-drop, file-on-send, and a force-file prompt. (VERIFIED)
2. **"Newforma files an email directly as an RFI or submittal, not just into a folder."** True in both products. **This is a real capability gap for KOR** unless KOR's tool does it. (VERIFIED)
3. **"Newforma suggests the right project for you."** True in Konekt — *"smart project suggestions ... based on email content and sender."* (VERIFIED)
4. **"Newforma has AI email filing shipping now."** Defensible — Newforma's own blog says Smart Email Filing is *"deployed and in your hands today"* in Konekt. Concede it. (VERIFIED)
5. **"Newforma integrates with SharePoint."** True — a SharePoint Online connector in Project Center Standard, and SharePoint as a Konekt data source with content search. (VERIFIED)
6. **"Newforma tracks who downloaded a transmittal."** True — in **Project Center / Info Exchange**. (VERIFIED)
7. **"Newforma is investing heavily — 7-year AWS deal, FedRAMP, a new AI platform."** True as of 2026-07-28. (VERIFIED)
8. **"It's a system of record with RFIs, submittals, change orders, field management and a document register."** True. KOR's tool is not that, and should not pretend to be.

### 10.2 Where Newforma is genuinely ahead of what KOR built

Be blunt about these internally. Every one of them is a place where an unprepared demo gets embarrassed.

1. **Contract-management depth.** RFIs, submittals, change orders, document control registers, field management, revision tracking. KOR built email filing, search and transmittals. Newforma built a project delivery system. **Different scope, and Newforma's is larger.**
2. **Filing an email as a typed project record** (RFI / submittal / issue / action item) rather than into a folder path. Structurally more useful for downstream workflow. **Confirmed gap:** a grep for `RFI` and `submittal` across `Kor.Operations.App` and `EmailFiler` returns only unrelated BD-intel, proposal-import and analytics hits — **there is no RFI or submittal record type in KOR's suite.** (VERIFIED by reading the repo, 2026-08-20.) Concede this one immediately; it is the clearest thing Newforma has that KOR does not.
3. **AI-assisted filing destination.** Konekt's Smart Email Filing / smart project suggestions. KOR's filer, on the evidence in this repo, does not do AI destination prediction.
4. **Semantic search is on Newforma's roadmap with a funded platform behind it.** Smart Search is not GA — but it is being built on a 7-year AWS/Bedrock foundation by a company with an AI product org. KOR's search is SQL Server full-text. **When Smart Search ships, Newforma is ahead on search, and KOR should have a plan for that day rather than a denial.**
5. **The surrounding transmittal apparatus** — automated reminders, configurable expiry/auto-deletion, revision-issue registers, a compressed record copy linked to the log entry. Info Exchange has had these for years; KOR's code shows no reminder or expiry logic. (KOR *does* have per-project transmittal numbering — that one is not a gap.)
6. **Ecosystem breadth.** Revit, Navisworks, AutoCAD (through Autodesk 2027), ACC/BIM 360, Procore, Bluebeam Studio, ProjectWise, Teams, Egnyte, OFCDESK. KOR has Microsoft Graph, SharePoint and Deltek.
7. **Institutional durability.** Newforma has 1,500+ firms, a support org, documentation, training and a compliance roadmap (FedRAMP Moderate). KOR's suite has a bus-factor, and at least one component — the transmittals redirector — **is not currently in a git repository** per the inventory. Fix that before it is demoed.
8. **Mobile and browser access.** Konekt has a mobile companion app and browser access; Project Center supports filing from a phone or OWA. Verify KOR's story here before it is asked.
9. **Not Newforma, but the sharpest of all: Egnyte's Email Capture is GA and does the core of KOR's tool as a product** (§9.6). If MVE's technical lead is well-read, this is the name they raise — not Newforma. Have the three-part answer ready: SharePoint migration required, per-seat cost, no transmittal register.

### 10.3 Where KOR is genuinely ahead or structurally different

1. **Per-recipient download attribution with client IP and user agent.** KOR's redirector issues a distinct `LinkId` / personal share link per recipient and records `RecipientEmail`, `ClientIp` and `UserAgent` per download event. Konekt's Sharing Centre publishes *"the number of times it was downloaded"* — a count, against a share, not an attributed event. Project Center/Info Exchange does who-downloaded-what but no published IP/UA capture. **On evidentiary quality of a transmittal, KOR's implementation is at least as good as Newforma's best and better than Newforma's current-generation product.** For a structural engineer defending a delivery date in a dispute, that is not a cosmetic difference.
2. **No 2-week cliff.** Konekt's shared download links expire after two weeks, per Newforma's own docs. KOR controls its own retention because it controls the redirector and the SharePoint store.
3. **SharePoint as the system of record, not a connected data source.** In both Newforma products SharePoint is surfaced *inside* Newforma; Newforma holds the record. KOR's files live in the tenant KOR already pays for, under KOR's retention, DLP, eDiscovery and Purview controls, reachable by every other tool KOR owns. **If KOR's software vanished tomorrow the project files would still be exactly where they are.** That claim cannot be made about a PIM-hosted record.
4. **No per-seat licence, and no seat-count coupling.** Every person at KOR can be a full participant without a purchasing decision. Newforma's model is per-user on both products and was just re-cut into more granular role-based SKUs; Egnyte is $10–$48/user/month published; Bluebeam is $260–$590/user/year published. At ~40 staff across two countries this is the difference between a fixed internal cost and a line item that grows with hiring. **Caveat — know this before you use the line:** **Procore prices on Annual Construction Volume with unlimited users** (*"we'll never charge you for adding more users"*), and **OpenSpace prices on construction volume too.** If the comparison in the room is Procore, the per-seat argument does not land — pivot to data ownership and the Deltek/BD integration instead.
5. **Integration with KOR's own financial and BD data.** The suite talks to Deltek over ODBC, to SQL Server, to Microsoft Graph and to an internal MCP/AI layer in one application. A transmittal, a project's financial position and a BD pursuit record are in the same system. **No PIM vendor will ever build that, because it is specific to KOR's stack.** This is the strongest structural argument and it is the one MVE cannot counter with a product page.
6. **Change velocity on KOR's own terms.** A workflow change is a commit, not a feature request into a PE-owned vendor's roadmap that is currently pointed at cloud migration and FedRAMP.
7. **No migration exposure.** Newforma's customers on Project Center are being walked toward Konekt — migration tooling shipped in 2025.2, a customer migration case study is published, and every AI feature is Konekt-only. **A firm standardising on Project Center in 2026 is signing up for a migration project it has not scheduled yet.** KOR has already taken its migration hit and owns the destination.

### 10.4 The honest answer to "why did you build your own instead of just using Newforma?"

Give this as one answer, in this order. It is truthful and it does not require Newforma to be bad.

> **"Because we only needed three things out of it, and we were paying per seat for a platform.**
>
> We used Newforma. It works. For a firm running full contract administration — RFIs, submittals, change orders, field reports — Project Center is a serious product and I wouldn't tell anyone to rip it out.
>
> What we actually used was email filing, email search, and transmittals. Everything else we were paying for and not touching, and the parts we did use put our project record inside a vendor's system when we were already paying Microsoft for a tenant that could hold it.
>
> So we rebuilt those three things against SharePoint. Now the files are in our own tenant under our own retention policy, every person here can use it without a licence conversation, and the transmittal tracking is per-recipient — we know which individual opened which link, when, from where. Our redirector, our log.
>
> **And then we got the thing we didn't plan for**, which is that because we built it, it can talk to our project financials and our BD pipeline. A transmittal, the project's margin, and the pursuit it came from are in one application. That is not something we could have bought.
>
> **What we gave up is real** — we don't have their submittal workflow, we don't file an email as an RFI record, and their AI filing assistant is ahead of ours. If we needed full CA workflow tomorrow we'd have a decision to make. We didn't, so we built the 20% we used and kept the record in our own house."

**Why this works:** it concedes the strongest counterpoints before MVE raises them, it explains the decision as scope-fit rather than product-criticism, and it lands on the one differentiator (own-data integration) that has no vendor answer. It also does not claim AI parity, which is the claim most likely to be tested live.

### 10.5 Questions to expect, and the true answer

| MVE says | True answer |
|---|---|
| *"Newforma has AI email filing now."* | *"Yes — Smart Email Filing, in Konekt. We don't have AI destination prediction; ours is rule- and pattern-based. Their AI search isn't out yet."* |
| *"Newforma's search is semantic now."* | *"Not yet. Smart Search is announced and in development as of May, and I haven't found a GA announcement. Ours is SQL full-text — fast and exact, not semantic."* |
| *"Info Exchange already did download tracking."* | *"It did, and it did it well. Note it's on Project Center, and the Konekt equivalent tracks a download count against a share, not an attributed per-recipient event, with a two-week link expiry. Ours is per-recipient with IP and user agent."* |
| *"Newforma integrates with SharePoint."* | *"It does — as a connected data source. The record stays in Newforma. For us SharePoint is the record."* |
| *"Why maintain software you're not selling?"* | *"It's about a person-scale of maintenance for three tools we use every day, against per-seat licensing for a platform we used 20% of — plus integration with our financials that no vendor sells."* |
| *"Egnyte does exactly this now."* | **The hardest question in the room. Concede fast.** *"They do, and it shipped in May — it's the closest thing to ours on the market. Three differences: Egnyte wants you to migrate off SharePoint into their storage, it's per seat, and there's no numbered transmittal register. We stayed in our own tenant."* |
| *"Couldn't you just buy Deltek PIM? You already run Deltek."* | *"It's the closest fit and we looked at that lane. It still puts the record in Deltek's index instead of our tenant, and it still wouldn't join a transmittal to a BD pursuit."* |
| *"Procore has unlimited users — no per-seat problem."* | **True. Don't argue it.** *"Procore's volume-based, that's a fair point. For us it was never mainly cost — it was where the record lives and what it connects to."* |
| *"What happens when your developer leaves?"* | **Weakest point. Do not bluff.** *"It's a real risk and we manage it — it's in source control, it's tested, and it's a small surface. It's the trade we made for owning our record."* (Confirm the redirector is in git before answering this.) |

---

## 11. Sources

All URLs accessed **2026-08-20** unless a publication date is given.

**Newforma primary (VERIFIED)**
- https://www.newforma.com/ — products/homepage
- https://www.newforma.com/newforma-konekt/ — Konekt positioning
- https://www.newforma.com/newforma-konekt/packages/ — Info Track / BIM Track / CA Track / HUB (Coming Soon); "AI Assist — Coming Soon"
- https://www.newforma.com/newforma-konekt/project-email/ — Konekt project email, smart project suggestions, deep search
- https://www.newforma.com/newforma-project-center/ — editions, connectors, no Info Exchange, no AI
- https://www.newforma.com/newforma-project-center/organize-project-email/ — PC email filing and search
- https://www.newforma.com/newforma-project-center/share-files/ — PC file sharing
- https://www.newforma.com/app_market/sharepoint/sharepoint-newforma-konekt/ — SharePoint as Konekt data source
- https://www.newforma.com/app_market/sharepoint/sharepoint-newforma-project-center/ — SharePoint connector for PC
- https://www.newforma.com/our-company/ — leadership, Ethos ownership, scale claims, BIM Track 2021 / Konekt 2023
- https://www.newforma.com/how-newforma-is-building-the-ai-powered-future-of-construction-information-management/ — **"deployed and in your hands today"** (Smart Email Filing); Vojo & Smart Search "coming soon" (no visible date)
- https://www.newforma.com/newforma-unveils-next-wave-of-ai-powered-innovation/ — 2026-05-07 announcement, announce-only framing
- https://www.newforma.com/news-publications/newforma-names-saas-growth-strategist-peter-cannone-as-ceo/ — CEO effective 2025-04-24
- https://www.newforma.com/resources/blog/ — blog index incl. CESO PC→Konekt migration case study
- https://www.newforma.com/resources/news/ — news index

**Newforma documentation (VERIFIED)**
- https://projectcenter.help.newforma.com/whats-new/ — 2026.2 is the newest documented PC release
- https://projectcenter.help.newforma.com/whats-new/new-and-improved-features-in-newforma-project-center-2026-2/ — Autodesk 2027, .NET 10 Revit add-in, MySQL, Outlook add-in 2.2.1 UI refresh + dynamic project search; **no AI**
- https://projectcenter.help.newforma.com/whats-new/new-and-improved-features-in-newforma-project-center-2025-2/ — Force Email Filing on Send; PC→Konekt migration improvements
- https://projectcenter.help.newforma.com/overviews/info_exchange_overview/ — Info Exchange audit trail, download visibility, expiry, anonymous access; **"last updated 3 years ago"**
- https://projectcenter.help.newforma.com/activity-centers/info-exchange-activity-center/file_sharing_window/ — Change Log tab download tracking
- https://projectcenter.help.newforma.com/Overviews/Transmittals_Overview/ — transmittal register
- https://projectcenter.help.newforma.com/overviews/file_transfer_overview/ — file transfer
- https://projectcenter.help.newforma.com/learning/how-tos/create_a_transmittal/ — transfer via Info Exchange + outgoing transmittal
- https://projectcenter.help.newforma.com/learning/reference-guides/info_exchange_quick_reference_guide/
- https://projectcenter.help.newforma.com/navigation/dialog-boxes/transfer_files_and_outgoing_transmittal/ — non-team recipients cannot partially download
- https://konekt.help.newforma.com/4408494681869-integrations-api/file-server-connector/newforma-konekts-file-server-connector/ — record copy on share, **2-week link expiry**, Sharing Centre logs what/with whom/when/**download count**, "history is only recorded for sharing"
- https://konekt.help.newforma.com/document-control/document-control-sharing-and-issuance/ — Document Control Share
- https://konekt.help.newforma.com/document-control/add-and-manage-files-in-document-control-beta/ — Document Control **(Beta)** label
- https://help.newforma.com/Newforma_Project_Cloud/Whats_New/What_s_New_in_Newforma_Project_Cloud.htm — legacy Project Cloud help
- https://infoexchange.newforma.com/ — Info Exchange landing/redirect still live

**Press releases (VERIFIED — company-issued)**
- https://www.prnewswire.com/news-releases/newforma-announces-ai-powered-innovations-and-open-ecosystem-strategy-at-newforma-world-2026-302765799.html — **2026-05-07** — Vojo, Smart Email Filing, modular licensing, Egnyte, Bluebeam, Teams, OFCDESK, FedRAMP Moderate, AWS; Carl Veillette CPO quote
- https://www.prnewswire.com/news-releases/aws-and-newforma-announce-strategic-7-year-collaboration-to-accelerate-customer-cloud-adoption-and-ai-innovation-302835695.html — **2026-07-28** — 7-year AWS agreement, PC cloud modernization, Konekt evolution, FedRAMP readiness, Amazon Bedrock
- https://www.prnewswire.com/news-releases/ceo-announcement-peter-cannone-joins-newforma-302440516.html — CEO announcement
- https://www.businesswire.com/news/home/20230403005480/en/Battery-Ventures-Announces-Sale-of-Newforma-to-Ethos-Capital — **2023-04-03** — Battery → Ethos Capital
- https://ethoscapital.com/2026/07/30/aws-and-newforma-announce-strategic-7-year-collaboration-to-accelerate-customer-cloud-adoption-and-ai-innovation/ — **2026-07-30** — Ethos re-post, confirms current ownership

**Trade press / analyst (REPORTED)**
- https://www.engineering.com/newforma-outlines-ai-roadmap-and-ecosystem-updates/ — **2026-05-08**
- https://architosh.com/2026/05/newforma-opens-up-with-ecosystem-and-ai/ — **2026-05-13** — no critical analysis offered
- https://www.constructionowners.com/press-release/newforma-unveils-ai-powered-tools-and-open-ecosystem-strategy-at-newforma-world-2026

**Review/pricing sites (REPORTED — treat as unreliable)**
- https://www.itqlick.com/newforma-project-center/pricing — internally inconsistent price estimates
- https://www.g2.com/products/newforma-konekt/pricing — four editions, custom pricing, per-seat cost complaint
- https://www.selecthub.com/p/project-management-software/newforma-project-center/
- https://www.glassdoor.com/Reviews/Newforma-existing-Reviews-EI_IE267465.0,8_KH9,17.htm — layoff mentions, **uncorroborated, do not cite externally**

**Adjacent competitors** — full URLs are cited inline in §9. Key dated primary sources:
- **Egnyte** — [press release, 2026-05-13](https://www.egnyte.com/press-releases/egnyte-targets-data-fragmentation-automated-email-and-content-governance) (Email Capture GA); [Email Capture blog, 2026-06-30](https://www.egnyte.com/blog/post/email-capture-turn-project-email-into-project-record); [egnyte.com/industries/aec](https://www.egnyte.com/industries/aec); [egnyte.com/pricing](https://www.egnyte.com/pricing); [SharePoint→Egnyte migration blog](https://www.egnyte.com/blog/post/a-faster-smarter-way-to-migrate-from-microsoft-sharepoint-to-egnyte); [helpdesk SPO migration article](https://helpdesk.egnyte.com/hc/en-us/articles/27710200349965-SharePoint-Online-SPO-to-Egnyte-Cloud-Migration)
- **Bluebeam** — [press.bluebeam.com, 2026-05-19](https://press.bluebeam.com/2026/05/bluebeam-max-launches-globally-bringing-ai-powered-productivity-to-aec-teams-everywhere/) (Bluebeam Max GA); [bluebeam.com/pricing](https://www.bluebeam.com/pricing/); [Studio FAQs](https://support.bluebeam.com/studio/resources/studio-faqs.html); [Nemetschek, Oct 2025](https://www.nemetschek.com/en/news-media/bluebeam-unveils-bluebeam-max-next-generation-ai-powered-innovations-unbound-2025)
- **Procore** — [Transmittals guide](https://support.procore.com/products/online/user-guide/project-level/transmittals); [Correspondence guide](https://support.procore.com/products/online/user-guide/project-level/correspondence); [Documents guide](https://support.procore.com/products/online/user-guide/company-level/documents); [procore.com/ai](https://www.procore.com/en-ca/ai); [procore.com/pricing](https://www.procore.com/pricing)
- **Autodesk** — [AI workflows page](https://construction.autodesk.com/workflows/artificial-intelligence-construction/); `help.autodesk.com/view/DOCS/ENU/` (Transmittals nav item); Docs URL 301→`autodesk.com/products/forma-data-management/overview`
- **Deltek** — [PIM](https://www.deltek.com/products/delivery-assurance/project-information-management/), [PIM Teamwork](https://www.deltek.com/en/products/enterprise-information-management/project-information-management/teamwork), [Dela](https://www.deltek.com/products/platform/dela/), [Vantagepoint Outlook add-in](https://help.deltek.com/product/Vantagepoint/2.0/outlook_Microsoft_Outlook_Add-in.html), [VP 2026.1](https://help.deltek.com/product/Vantagepoint/2026.1/ReleaseNotes/DeltekVantagepoint202611ReleaseNotes.htm) / [2026.3 release notes](https://help.deltek.com/product/Vantagepoint/2026.3/ReleaseNotes/DeltekVantagepoint20263ReleaseNotes.htm)
- **Revizto** — [newsroom, 2026-07-28](https://revizto.com/resources/newsroom/company-news/revizto-expands-platform-enterprise-ai-integrations); [CDE integrations help](https://help.revizto.com/hc/en-us/articles/4415779193871-Managing-CDE-integrations)
- **OpenSpace** — [FAQ](https://www.openspace.ai/faq/); [PR Newswire OpenSpace Field GA, 2026-02-03](https://www.prnewswire.com/news-releases/openspace-announces-general-availability-of-openspace-field-bringing-visual-intelligence-directly-into-field-execution-302677091.html)
- **Microsoft** — [document processing pay-as-you-go pricing](https://learn.microsoft.com/en-us/microsoft-365/documentprocessing/syntex-pay-as-you-go-services?view=o365-worldwide) (updated 2026-06-01); [Purview records management](https://learn.microsoft.com/en-us/purview/records-management); [SharePoint→Purview deprecation, April 2026](https://learn.microsoft.com/en-us/sharepoint/use-microsoft-purview-risk-and-compliance-solutions); [see who a file is shared with](https://support.microsoft.com/en-us/sharepoint/sharepoint-sharing-and-permissions/see-who-a-file-is-shared-with-in-onedrive-or-sharepoint); [vladtalkstech.com rename analysis](https://vladtalkstech.com/microsoft-365/sharepoint/sharepoint-premium-is-dead-heres-what-microsoft-just-renamed-again/) (REPORTED, uncorroborated)

**KOR-side evidence (repo, VERIFIED by reading the code)**
- `C:\VIsual Studio Projects\Operations\Kor.EmailSearch.Core\EmailSearchService.cs` — SQL Server full-text via `dbo.SearchEmailsPaged`; keyword, not semantic
- `C:\VIsual Studio Projects\Operations\Kor.Operations.Data\SqlTransmittalsStore.cs` — per-recipient `LinkId`/`PersonalShareLink`; download events carry `RecipientEmail`, `ClientIp`, `UserAgent`
- `C:\VIsual Studio Projects\Operations\Kor.Operations.App\Services\TransmittalService.cs` — Graph delivery, SharePoint upload orchestrator, redirector base URL, 10 MB attachment threshold
- `C:\VIsual Studio Projects\Operations\docs\audit-2026-08\00-INVENTORY.md` — `Kor.Transmittals.Redirector` is **NOT A GIT REPO**; `Kor.Operations.App` carries SQL/ODBC/Graph/HTTP/SharePoint/AI dependencies

### Could not verify — stated plainly

1. **Whether Vojo / Smart Search / Smart Email Filing reached GA between 2026-05-07 and 2026-08-20.** Searched: Konekt release notes (two URL forms, both HTTP 404 unauthenticated), Konekt help home, Newforma news index, Newforma blog index. No dated June–August 2026 GA announcement found.
2. **Whether Info Exchange is a separate SKU or bundled into a Project Center edition.** No pricing or SKU detail is published; the editions page does not name it.
3. **Where Konekt stores filed email** (Newforma cloud vs customer file share vs SharePoint). Not stated on the Konekt project email page or the connector docs.
4. **Whether Konekt's Sharing Centre attributes a download to an individual recipient.** The docs state "with whom" and "number of times downloaded" — the two are not documented as joined.
5. **Any 2024–2026 layoff event at Newforma.** Only Glassdoor employee commentary; no press confirmation.
6. **What MVE actually runs for project information management.** KOR's own MVE dossier records nothing about their document/PIM stack. Ask in the room.
7. **Egnyte's AEC-specific tier pricing** (Essentials / AEC Elite / AEC Ultimate). No price table on the AEC page; only the general tiers are published.
8. **Bluebeam's per-recipient download/view audit trail.** Studio FAQs and the notifications doc do not specify it.
9. **Procore's Transmittals download/view tracking granularity, and any Procore–SharePoint connector.** Doc pages 404'd; newsroom and investor pages are JS-rendered and unreachable.
10. **Autodesk Docs/ACC transmittal tracking mechanics, SharePoint integration, and pricing.** `help.autodesk.com` detail pages returned 503 repeatedly and `autodesk.com` root pages returned 403 repeatedly; G2/Capterra fallbacks failed. Also unconfirmed: the scope of the apparent **Autodesk Docs → "Autodesk Forma Data Management"** rebrand (the 301 redirect was observed; the destination page 403'd).
11. **Formal GA designation for Procore's AI agents and Autodesk's AI features.** Both vendors list the features as live with no GA/preview labels or dates.
12. **The technical content of both May 2026 Newforma partnerships** (Egnyte, and Bluebeam↔Konekt). Neither press release nor either vendor's blog gave feature-level detail.
13. **Whether Deltek PIM's transmittal module logs per-recipient download timestamps** (vs. a general access audit trail). Checked the PIM product page and PIM Teamwork page; only general "audit trail" language surfaced.
14. **A native SharePoint per-link download-activity report visible to an ordinary user** (as opposed to admin-only Purview audit). The research pass ran out of WebSearch budget before a Microsoft Learn "file and page activity reports" page could be pulled. Worth a five-minute check on `learn.microsoft.com` before the demo, since KOR's SharePoint argument leans on this gap.

---

## Method note

Prior art was checked before any research began, per the repo working rules: `grep -ril newforma` across the repo (hits are all product code and one FileSync migration script — no prior Newforma competitive research existed), `docs/` for competitive files (found `bd-ca-competitor-matrix-2026-06-17.md`, `bd-ca-competitor-playbooks-2026-06-18.md`, `bd-dossier-mve-mclarand-2026-06-17.md`), and `docs/audit-2026-08/` (contained only `00-INVENTORY.md`). The MVE dossier was read; it has no PIM/document-stack content.

All Newforma research was live WebSearch/WebFetch on 2026-08-20 — nothing about post-early-2026 product state was asserted from model knowledge. The session's WebSearch budget (200 calls) was exhausted during the adjacent-competitor pass; remaining verification was done by direct WebFetch, and every point that could not be reached is listed above rather than filled in by inference.

KOR-side claims were verified by reading the code in this repo, not by recollection: `EmailSearchService.cs` (full-text, not semantic), `SqlTransmittalsStore.cs` (per-recipient LinkId, RecipientEmail/ClientIp/UserAgent), `TransmittalService.cs` (`ReserveTransmittalNumberAsync`, no expiry/reminder logic), and a repo-wide grep confirming no RFI/submittal record type exists.
