# C4 — The Field

**Competitive intelligence for the MVE demo · compiled 2026-08-20 · KOR Structural**

Scope: everything in the field *except* Deltek, Newforma and Microsoft (covered by other
researchers). Sections A–E below, then the three judgements that matter: where KOR is
differentiated, where KOR is behind, and what commoditises inside twelve months.

---

## How this was researched, and what that means for trust

Prior art was checked first. `docs/`, `docs/audit-2026-08/` and `tools/` were grepped for existing
competitive-software intel (Togal, Trunk Tools, Monograph, BST, Unanet, OpenAsset, Kreo, Snaptrude,
Higharc). Nothing existed — the "competitor" documents in this repo are all AE-firm BD matrices
(`docs/bd-ca-competitor-matrix-2026-06-17.md`), not software scans. The existing MVE dossier
(`docs/bd-dossier-mve-mclarand-2026-06-17.md`) was read for the "what MVE already owns" question.
This report is net-new.

Every claim below carries a source URL, the date of the source or of the fetch, and a tag:

- **VERIFIED** — primary source: vendor site, product docs, release notes, funding announcement,
  fetched live on 2026-08-20 unless another date is given.
- **REPORTED** — secondary: press, analyst, aggregator.
- **INFERRED** — reasoning, labelled as such.
- **could not verify** — stated plainly, with what was searched.

Five parallel research streams ran live searches and fetches. The session's shared web-search
budget (200 queries) was exhausted part-way through, after which work continued via direct fetches
of vendor pages. Where a stream ran out of runway before reaching a named candidate, this report
says "not checked" rather than "found nothing" — those are different claims and the difference is
load-bearing.

---

## Executive verdict

**Nobody sells what KOR has built, but that is because nobody has assembled it — not because the
pieces are exotic. Three of KOR's four pillars now have a credible commercial analogue that did not
exist eighteen months ago. The fourth — drawings in, structural analysis model out — has none, and
the search for one came up empty across every phrasing tried.**

1. **The virtual CFO is no longer a category of one.** Unanet shipped **Champ for ERP** to GA on
   **2026-02-18** — natural-language query over AE firm financials, spanning cash, margin,
   utilisation and 90-day revenue forecast. Deltek's **Ask Dela** ships in Vantagepoint and Ajera
   Cloud. KOR is still ahead on depth (~25 typed analytical tools; no vendor discloses anything of
   that granularity), but "we built a chatbot for our ERP" stopped being a differentiator this year.
2. **BD Brain is the genuinely rare one, and it is rare in an awkward way.** No AEC vendor —
   OpenAsset, Unanet CRM — ingests public tender feeds or runs research agents at all; they are
   content-library proposal writers over the firm's own data. The people doing ingestion + ranked
   pursuit queues + capture lifecycle are **GovCon** platforms (GovDash, $30M Series B 2026-01-15;
   Sweetspot; Procurement Sciences; pWin.ai, which just absorbed Vultron). They are federal-data-shaped
   and none of them publishes an entity-resolution capability. KOR's combination appears to be
   unmatched commercially. It is also the module a well-funded GovCon platform could reach in a year
   by adding Canadian and municipal sources.
3. **Structural engineering is where the outside capital is not.** Venture money in AEC AI 2024-26
   went to architectural massing (Motif $46M, Arcol, Snaptrude, Qbiq $16M), MEP automation (Augmenta,
   Endra $50M a16z), GC document intelligence (Trunk Tools $70M total, Document Crunch, Nomic) and
   homebuilding (Higharc **$95M Series C, 2026-06-30**). Repeated targeted searches for a
   venture-funded structural-analysis-AI startup returned nothing.
4. **The incumbents you would expect to close this gap have not moved.** CSI ships zero AI in ETABS
   v23.3.1 (2026-07-02). Autodesk Robot Structural Analysis 2026 ships zero AI. Bentley STAAD and RAM
   ship zero AI. Their AI investment is aimed at massing, Revit chat and PDF markup.
5. **Where KOR is genuinely behind is the rebar change tool.** Trunk Tools' **TrunkReview** uses
   vision-language models to catch *unclouded* changes across a whole bulletin in five minutes, with
   overlay and narrative, on 500+ jobsites. Bluebeam's **Smart Overlay** does cross-discipline,
   cross-scale change detection and is in preview at $590/user/year. STACK ships version comparison.
   KOR's tool reads callout text and produces a bar-list steel-weight delta — deeper on the one axis
   that matters to a rebar detailer, narrower everywhere else, and about to be surrounded.

The single most useful number for the room comes from a competitor's own research: **only 1% of AEC
firms have achieved widespread adoption of AI-enabled processes**, and fewer than a quarter claim
mature or advanced AI readiness — BST Global, *AI + Data Insights 2026*, published 2026-05-04
(VERIFIED). When MVE asks "isn't everyone doing this?", that is the answer, and it comes from the
vendor with the loudest AI marketing in the sector.

---

## A) AE firm management / PSA platforms

The Deltek alternatives MVE might name. **Two vendors ship genuine conversational analytics over AE
financials today. The vendor with the loudest AI marketing is not one of them.**

### Unanet AE — the real competitor to KOR's virtual CFO

| Date | Event | Status |
|---|---|---|
| 2025-10-01 | Unanet announces investment in **Wyatt**, a "GPT-enabled copilot… purpose-built exclusively for Unanet" | announced |
| 2025-11-12 | **Champ AI™** launched — "the natural language copilot for GovCon and AEC firms" | launched |
| 2025-12-09 | Bundle release: ChampAI, Wyatt, OpportuneAI (pursuit identification), ProposalAI, AI AP/AR automation | GA |
| **2026-02-18** | **Champ™ for ERP, powered by Wyatt — GA for Unanet AE ERP customers** | **shipped-GA** |
| 2026-07-30 | Champ Agents expanded to GovCon ERP + AEC CRM; GovCon CRM "planned for later in Q3 2026" | GA / announced |

All VERIFIED from Unanet's own newsroom: [Wyatt](https://unanet.com/news/unanet-invests-in-wyatt-to-deliver-a-new-class-of-erp-and-crm-intelligence-to-customers) (2025-10-01) ·
[Champ AI launch](https://unanet.com/news/unanet-introduces-champ-ai-the-natural-language-copilot-for-govcon-and-aec-firms) (2025-11-12) ·
[Champ for ERP GA](https://unanet.com/news/unanets-latest-ai-copilot-helps-architecture-and-engineering-firms-turn-complex-erp-queries-into-instant-accurate-answers-and-automated-workflows) (2026-02-18) ·
[Champ Agents expansion](https://unanet.com/news/unanet-expands-champ-agents-powered-by-wyatt-across-its-erp-and-crm-portfolio) (2026-07-30).

**What it actually does.** Vendor-published example prompts on [unanet.com/champai](https://unanet.com/champai)
(VERIFIED, fetched 2026-08-20): *"How is my project performing right now?"*, *"How does this month's
performance compare to last?"*, *"What's our revenue forecast for the next 90 days?"* — cash, margin,
utilisation and firm-wide risk. Three surfaces: **Chat** (Q&A), **Chores** (scheduled AR/WIP/compliance
monitoring), **Round Ups** (agentic analysis with recommendations). Role-based governance, audit
logging. Unanet positions it explicitly against generic chatbots — CIO Steve Karp, 2025-11-12: *"The
industry doesn't need another chatbot… Champ AI goes beyond basic Q&A to deliver context, confidence,
and action in a single conversation."* Customer quote, Mark Bertsch, CFO of AE Works (2026-02-18
release): *"It's an intelligent agent that gathers the data, applies my criteria, drills deeper when
something looks off, and brings back recommendations."*

**The caveat worth carrying into the room.** A still-live
[Wyatt Beta Terms page](https://unanet.com/wyatt-beta-terms-and-conditions) (VERIFIED, checked
2026-08-20) describes the underlying engine as having run a 90-day approval-gated beta and being "in
early access stage." Read Unanet's GA as vendor-declared productisation of a maturing agent, not an
audited GA. **Add-on cost: could not verify** — no Unanet page discloses whether Champ is bundled or
a paid tier; searched "Champ AI Unanet pricing add-on cost", no primary source found.

INFERRED, worth knowing: Wyatt co-founder Matt Pantana is quoted in both the Oct-2025 and Feb-2026
releases. This is likely the same Matt Pantana who led Clearview Software at its 2020 acquisition by
Unanet — i.e. Unanet's AI leadership carries AE-ERP domain continuity, not generic SaaS pedigree.

### BST10 / BST11 (BST Global) — loudest on AI, thinnest on shipped product

- Current branding is **BST11 ERP**; the BST10 → BST11 rename predates this window. VERIFIED,
  [bstglobal.com/erp](https://bstglobal.com/erp/); [GlobeNewswire](https://www.globenewswire.com/news-release/2024/06/25/2903968), 2024-06-25.
- **The one shipped AI product is BST Insights** — announced 2024-06-25, tracks "35+ digital signals",
  claims prediction of project outcomes with ">95% accuracy" (self-reported, unaudited), runs
  alongside BST11, Deltek, Oracle and others. It is dashboards, anomaly detection and prediction.
  **It is not conversational.** VERIFIED, [bstglobal.com/insights](https://bstglobal.com/insights/),
  checked 2026-08-20.
- **"BSTPredict" and "Blackbird.ai" do not exist as BST Global products.** Two independent search
  passes found no match on bstglobal.com or in press coverage. Blackbird.AI is an unrelated
  narrative-intelligence/disinformation company. VERIFIED (absence) — searched "BST Global BSTPredict",
  "BST Global Blackbird.ai", "BST Global AI assistant OR virtual assistant OR copilot ERP".
- **No chatbot, assistant or natural-language query interface exists anywhere in BST's line.**
  VERIFIED (absence), checked 2026-08-20.
- A 2024-announced integration with "Microsoft Copilot for Power BI" was forward-looking at the time;
  **could not verify** whether it shipped — searched BST's site and press for "Copilot" post-2024,
  no confirming update.
- Their 2025-26 AI activity is **events and surveys, not product**: second annual AI Summit,
  2025-05-06/08, Palm Beach, panel including Arcadis, Arup, AtkinsRéalis, Gensler, GHD, Jacobs, Mott
  MacDonald, NVIDIA, Parsons, Stantec, WSP (VERIFIED, [bstglobal.com/ai-summit-2025](https://bstglobal.com/ai-summit-2025/));
  2026 AI Data Impact Survey launched 2025-11-05 (BusinessWire); third AI Summit registration opened
  2026-04-21 ([BusinessWire](https://www.businesswire.com/news/home/20260421204454/en/), 2026-11-10/12 event);
  **AI + Data Insights 2026 report published 2026-05-04** (VERIFIED,
  [bstglobal.com/news](https://bstglobal.com/news/bst-global-releases-2026-report-examining-ais-impact-on-the-aec-industry/)).
- Frost & Sullivan named BST Global "2026 Global AEC Project Management Solutions Company of the Year"
  for AI-powered project management — vendor-award PR, REPORTED, not independently verified capability.

> **The BST 2026 report is the most useful artefact in this entire scan, and it argues against its own
> author.** Fewer than a quarter of AEC respondents claim AI readiness at a mature or advanced level;
> **only 1% of firms have achieved widespread adoption of AI-enabled processes.** VERIFIED, 2026-05-04.

### Ajera (Deltek) — conversational, but narrow

**Ask Dela** is live in **Ajera Cloud only** (explicitly excludes on-premises). VERIFIED,
[deltek.com/en/blog/ask-dela-in-ajera](https://www.deltek.com/en/blog/ask-dela-in-ajera); release
announcement [What's New in Ajera](https://www.deltek.com/en/blog/whats-new-in-ajera-103) dated
2025-11-04, companion feature-detail post dated 2025-12-08 (Deltek's own two posts disagree by a
month; noted, not resolved). It answers *"Analyze the financials… and summarize any areas of concern"*,
AR balances, contract balances, project financial performance — multi-turn, role-based, chat clears on
logout. Real retrieval and summarisation, **scoped to client/contact/project lookups rather than
firm-wide margin/utilisation/cash analytics, with no evidence of a structured multi-tool analytical
architecture.** No add-on fee identified (VERIFIED absence).

*Cross-reference for the Deltek researcher:* Ask Dela is a Deltek-wide brand and **also ships in
Vantagepoint** — [deltek.com/en/blog/vantagepoint-ask-dela](https://www.deltek.com/en/blog/vantagepoint-ask-dela)
and the [Vantagepoint 7.0 docs](https://help.deltek.com/product/Vantagepoint/7.0/DVP_DeltekDela_Overview.html),
which note it "replaces the previous 'Hey Deltek!' functionality." That is the platform KOR's MCP
layer sits on top of. It is real, shipped, and carries no separate fee. Treated as a commoditisation
risk in the closing section.

### Monograph — shipped AI, undated, and now merging

- **Ask Monograph** ("AI-powered search to answer questions using your firm's data") is listed as a
  standard Dashboard feature, not flagged beta. VERIFIED,
  [support.monograph.com](https://support.monograph.com/en/collections/19671455-dashboard), checked 2026-08-20.
- AI budget recommendations (from similar past projects), AI staffing recommendations (from capacity),
  AI workflows that auto-populate projects from proposals/files/emails. VERIFIED,
  [monograph.com/benefit/automate-your-work](https://monograph.com/benefit/automate-your-work).
- The only feature with a confirmed ship date is **Smart Time Suggestions, 2026-06-16** — VERIFIED,
  [monograph.canny.io/changelog](https://monograph.canny.io/changelog). No launch dates found for Ask
  Monograph or the recommendation features; treat as "live now, undated."
- **MoneyGantt™** is marketed under an "AI Project Forecasting" headline, but the mechanism described
  is threshold/rules-based (progress bars green→red, CPI/SPI/utilisation/realisation). INFERRED:
  conventional BI rebranded as AI — no model, training data or algorithm described.
- **2026-08-10 — Total Synergy and Monograph announced a merger**, backed by PSG and M33 Growth:
  *"Total Synergy and Monograph Join Forces to Build a Global, AI-Enabled Standard for A&E Practice
  Management."* REPORTED (direct Business Wire fetch returned 403; text confirmed identical across
  three independent pickups) — [Yahoo Finance mirror](https://finance.yahoo.com/technology/ai/articles/total-synergy-monograph-join-forces-162100782.html).
  The AI language in the release is entirely forward-looking. Total Synergy CEO Kane Hochster:
  *"A&E firms aren't just delivering projects – they're running complex businesses, and their software
  hasn't kept pace."* **Ten days old at the time of the demo — worth knowing before MVE mentions it.**

### Total Synergy — commitment-stage

The December 2025 release notes add a Power BI "Insights" section with a Financials dashboard
containing **Forecast dashboards** — and contain **no AI, ML or NLP language at all**. VERIFIED
(direct fetch), [Synergy Release — December 2025](https://help.totalsynergy.com/en/articles/12895620-synergy-release-december-2025).
The public roadmap's "Global Search… plain language" is search UX, not NLP. VERIFIED,
[help.totalsynergy.com](https://help.totalsynergy.com/en/articles/12086589-what-s-next-for-total-synergy).
2025-07-24: acquired **Factor A/E**, with a PSG + M33 Growth round citing "strategic investment in AI"
and no specifics. VERIFIED, [totalsynergy.com](https://totalsynergy.com/resources/blog/total-synergy-acquires-factor-ae-secures-significant-growth-investment/).

### Mission Control (Aprika, Salesforce-native)

Naming collision resolved: `missioncontrol.io` is an unrelated gaming-analytics product and
`usemissioncontrol.com` an unrelated wearables company. The AE PSA is **Mission Control by Aprika
Business Solutions** (Melbourne, founded 2010), native to Salesforce. The base PSA has **no AI**; AI
ships as a separately purchased companion app, **"Agentforce for Mission Control"** — listed for sale
(not beta), 14-day trial, requires the base app. Scope is **project health and risk assessment, plus
lessons-learned retrieval — not financial Q&A.** VERIFIED (absence of financial NL query).
**Pricing is explicit, which is unusual in this set: base US$39/user/month (5-seat minimum) plus
US$10/user/month for the AI agent.** VERIFIED, Salesforce AppExchange listings `a0N30000008ZFI5EAO`
and `4ca7c25a-dd0f-4d94-9e23-1f0c44d48c41`, checked 2026-08-20. Launch date **could not be verified** —
neither listing shows a publish date.

### Rapport3 (Cubic Interactive) — no AI activity of any kind

**2025-10-21: Cubic Interactive was acquired by Milient Software** (Norway, PE-backed by Monterro),
with a combined revenue target of roughly NOK 200M for 2026 and **no AI roadmap commitment**. The
announcement describes Rapport3 as "project planning, expense tracking, resource allocation, business
intelligence, and… integration with third-party systems", with **no AI, ML, NL or chatbot capability
mentioned anywhere.** VERIFIED (direct fetch),
[milientsoftware.com](https://www.milientsoftware.com/en-gb/blog/press-release-milient-cubic-interactive).
Could not verify any separate Rapport3 AI announcement — searches were dominated by an unrelated
company also called Cubic (cubic.dev, a code-review tool). Rapport3 shows the least AI activity of
any vendor in this section — not even marketing-only claims.

### Clearview InFocus — no longer exists

Acquired by Unanet (deal closed 2019-10-24), rebranded "Unanet A/E, powered by Clearview", now simply
**Unanet AE**. VERIFIED, [PR Newswire](https://www.prnewswire.com/news-releases/clearview-software-is-now-unanet-ae-forging-a-path-for-more-investment-in-architecture-and-engineering-erp-software-and-service-301052677.html),
2020-05-05 (Unanet's own mirror is dated 2020-05-04 — a one-day mirror discrepancy, not a conflict).
`clearviewinfocus.com` no longer resolves; `clearviewsoftware.net` has an expired TLS certificate
(both checked 2026-08-20). Anyone comparing against "Clearview" today is comparing against Unanet.

### A) verdict

Two vendors ship genuine conversational analytics over AE financial data: **Unanet** (Champ for ERP,
GA 2026-02-18, firm-wide scope) and **Deltek** (Ask Dela, Ajera Cloud and Vantagepoint, narrower
scope). Everyone else is dashboards, rules-based forecasting, or press releases. **No vendor in this
set discloses anything resembling a ~25-tool typed analytical agent** — where agentic depth is
claimed, vendors describe outcomes, not architecture. KOR's virtual CFO is no longer unique in kind.
It is still ahead in depth, and that is the distinction to draw in the room.

---

## B) Marketing / BD and proposal tooling for AEC

The question that matters: **does anyone ingest public tender feeds and auto-research pursuits?**
Answer: the AEC-native vendors do not, and the vendors who do are GovCon platforms shaped around
federal data.

### The AEC-native tools are content-library proposal writers

**OpenAsset** — digital asset management for AEC, founded 2003, backed by Marlin Equity since 2023,
passed 1,000 customers in 2024. VERIFIED, [openasset.com/about](https://openasset.com/about/). Its AI
product is **Shred.ai / "Shred 3.0"**: Proposal Studio (drafts from past proposals plus RFP criteria),
AI Chat over the firm's own OpenAsset data, Go/No-Go analysis against firm-set criteria, InDesign
drag-and-drop. VERIFIED, [openasset.com/shred](https://openasset.com/shred/), fetched 2026-08-20.
**It does not ingest public tender feeds** — it operates entirely on the firm's own DAM content
(VERIFIED, explicit absence). Status GA (versioned "Shred 3.0", no beta language). A search synthesis
put the original Shred.ai launch at June 2025 — REPORTED, not confirmed against a primary release.
No entity resolution claimed anywhere. No acquisitions found in OpenAsset's own history (VERIFIED negative).

**Unanet CRM (formerly Cosential)** — AI-Enhanced/Smart Technology suite: **Contact Detection**
(ML extraction of contacts from Outlook), **Contact Enrichment** (from 200+ public data sources),
**Company Researcher** (builds a company record from a URL), **Caller IQ**. VERIFIED,
[unanet.com/crm-aec/ai-enhanced-and-smart-technology](https://unanet.com/crm-aec/ai-enhanced-and-smart-technology).
**ProposalAI for AEC** draws on the firm's own CRM data (project history, resumes, qualifications) to
auto-populate proposals and quals, exported to Word/InDesign/PDF. Marketed as "the only AI-first
proposal workflow powered by rich CRM data." VERIFIED,
[unanet.com/crm-aec/proposals](https://unanet.com/crm-aec/proposals). A customer quote on that page
gives the game away — the point is to *"provide better data… rather than conducting fresh research."*
**No public tender ingestion. No cross-source entity resolution.** VERIFIED (absence on both).

**ConstructionOnline (UDA Technologies)** — contractor-side project and bid management (950,000+ users
claimed; REPORTED via G2/SoftwareAdvice). Added a Bid Tracking module in January 2026. **No AI
initiative specific to this product was located** — could not verify; searched "ConstructionOnline UDA
Technologies AI 2026". INFERRED: functionally adjacent to Bidtracer, not to OpenAsset/Unanet.

**Bidtracer** — cloud CRM/bid-management/estimating for small-to-mid firms and **subcontractors
tracking invitations to bid they receive**, not an AEC design-firm BD platform. REPORTED
(SelectHub/Capterra). A direct fetch of [bidtracer.com](https://www.bidtracer.com/) surfaced no
statement on tender-feed ingestion, AI research or entity resolution — **could not verify** any of the
three; the page is thin marketing copy.

**Responsive (formerly RFPIO)** — general-purpose enterprise RFP response, not AEC-specific; AI is
NLP autofill and content-library reuse. REPORTED (vendor-comparison listicles; not independently
fetched). No tender ingestion, no entity resolution.

### Three names in the brief turned out to be something else

- **Unanimous AI** — there is no AEC proposal company by this name. The only operator is the
  **swarm-intelligence** company (founded 2014) selling Artificial Swarm Intelligence and Hyperchat AI
  for group forecasting. VERIFIED (negative on the collision hypothesis), [unanimous.ai](https://unanimous.ai/),
  [Wikipedia](https://en.wikipedia.org/wiki/Unanimous_A.I.).
- **Ontra** — contract automation and legal ops for **private markets** (PE/VC fund documents;
  Blackstone, Bain Capital, Warburg Pincus named). VERIFIED, [ontra.ai](https://www.ontra.ai/). Out of scope.
- **Kojo** — **materials procurement** for trade contractors (MEP, concrete, drywall), with AI invoice
  scanning and supplier price comparison launched April 2025. VERIFIED, [usekojo.com](https://www.usekojo.com/);
  [BusinessWire](https://www.businesswire.com/news/home/20250418536028/en/Kojo-Launches-AI-Tool-Suite-to-Help-Contractors-Combat-Rising-Material-Costs), 2025-04-18.
  $85.6M raised across 4 rounds, 163 employees as of June 2026 (REPORTED, Tracxn/Crunchbase). Out of scope.

### The GovCon capture platforms — the ones that actually do what BD Brain does

**GovDash** is the closest analogue found anywhere. **$30M Series B announced 2026-01-15**, led by
**Mucker Capital** and **British Columbia Investment Management Corporation (BCI)**, with Northzone
and Y Combinator participating. VERIFIED,
[govdash.com press](https://www.govdash.com/blog/press-govdash-raises-30m-series-b-to-help-companies-win-and-manage-government-contracts-with-ai);
corroborated by [SiliconANGLE](https://siliconangle.com/2026/01/15/govdash-secures-30m-expand-ai-driven-government-contracting-software/)
(2026-01-15) and [bci.ca](https://www.bci.ca/govdash-raises-30m-series-b/). Since Series A: 16x revenue
growth, 18x customer growth to ~200 companies, headcount 3 → 45+; customers won $5B+ in contracts in 2025.

Its **Discover** module continuously scans **SAM.gov, USASpending.gov, agency forecast portals, GovWin
IQ, HigherGov and contracting-office websites**, filtered against firm NAICS/set-aside/past-performance
profile, cross-referencing incumbent contract histories, congressional appropriations, budget
justifications and recompete windows up to **14 months ahead**, producing a **ranked pursuit queue**.
VERIFIED, [govdash.com blog](https://www.govdash.com/blog/ai-agents-government-contract-opportunity-discovery),
published 2026-08-12 — eight days before this report. Full pipeline: Discover → Capture → Pricer →
Proposal → Contract, plus a "Dash" agent. GA with named case-study customers. **No entity resolution
claimed** (VERIFIED absence). GovCon-only; the output is a ranked queue, not a narrative dossier.

**Sweetspot** (YC S23) — **$2.2M seed, 2024-08-07**, led by **1984 Ventures** with Liquid 2, Orange
Collective, Pioneer Fund, Soma Capital. VERIFIED,
[Semafor](https://www.semafor.com/article/08/07/2024/ai-startup-sweetspot-raises-22-million),
[YC profile](https://www.ycombinator.com/companies/sweetspot). Ingests SAM.gov, FPDS, USASpending,
DIBBS plus **1,000+ state/local/education portals**; does incumbent research, competitive intelligence
and bid/no-bid recommendations from real USASpending data; configurable capture-stage pipeline with
velocity analytics. VERIFIED, [sweetspot.so/govcon-workflows](https://www.sweetspot.so/govcon-workflows/).
~17 employees as of January 2026 (REPORTED). No entity resolution found.

**Procurement Sciences / Awarded AI** — **$30M Series B** led by **Catalyst Investors** with Battery
Ventures; "AI-native operating system" spanning opportunity ID, proposal drafting, compliance review
and contract execution; 300+ organisations, aerospace/defence-heavy. REPORTED,
[PR Newswire](https://www.prnewswire.com/news-releases/procurement-sciences-closes-30-million-series-b-to-accelerate-ai-platform-helping-businesses-find-win-and-deliver-government-contracts-302604955.html).
A direct fetch of the product page did not detail the ingestion mechanism, mention entity resolution,
or describe autonomous dossier generation — **could not verify** those specifics.
Separately, **Procurement Sciences acquired Rogue AI** (announced ~February 2026) — REPORTED,
[PR Newswire](https://www.prnewswire.com/news-releases/procurement-sciences-acquires-rogue-ai-to-accelerate-end-to-end-ai-growth-platform-for-government-contracting-302692361.html).
Note the name trap: **"Rogo"** (rogo.com) is an **investment-banking finance AI** at roughly $2B
valuation, unrelated to GovCon (VERIFIED, [rogo.com/product](https://rogo.com/product)). The GovCon
company is **Rogue AI**.

**pWin.ai** — $10M seed led by MicroStrategy co-founder Sanju Bansal (REPORTED,
[ExecutiveBiz](https://www.executivebiz.com/articles/pwinai-10m-investment-ai-driven-proposal-platform)).
Shipley-methodology proposal engine, Knowledge Repository, "Readiness Report" flagging capability gaps
— scoped to the firm's own content. **No native tender ingestion**; it gets market intelligence through
a **partnership with TechnoMile announced 2026-02-10** (VERIFIED,
[GovConWire](https://www.govconwire.com/articles/technomile-pwin-ai-partner-capture-proposal)).
**Consolidation confirmed: pWin.ai has acquired Vultron.** Vultron's own site now carries the banner
*"Vultron has joined forces with pWin.ai"* (VERIFIED, [vultron.ai](https://vultron.ai/), fetched
2026-08-20), resolving an earlier ambiguity. Vultron had raised **$17M Series A led by Greycroft on
2025-07-16, $22M total** (VERIFIED, [BuiltIn SF](https://www.builtinsf.com/articles/vultron-raises-22m-20250716))
and claims 400+ federal contractors.

**Tendium** (Nordic B2G) — Find (continuous tender monitoring with AI summaries), BidFlow, Intelligence,
plus a "Louie" assistant; claims ~1.0M active and historical European public-sector tenders. VERIFIED,
[tendium.com](https://tendium.com/). **Funding could not be verified** — search results conflated it
with a distinct company, Tendavo.

**Arphie** ($2.9M seed, 2024-11-14, led by General Catalyst — VERIFIED,
[Arphie blog](https://www.arphie.ai/blog/arphies-2-9m-seed-round-led-by-general-catalyst),
[Axios Pro](https://www.axios.com/pro/fintech-deals/2024/11/14/ai-driven-rfp-startup-arphie-seed)),
**DeepRFP** (GovCon proposal agents, GA, no funding disclosed, explicitly manual RFP load — VERIFIED,
[deeprfp.com](https://deeprfp.com/ai-rfp-software/government-proposal-software/)) and **AutoRFP.ai**
(Brisbane, ~21 staff; funding claims conflict between "none raised" and "SignalFire/Building Ventures" —
**could not verify**, contradiction flagged) are all content-library drafting, no ingestion, no entity
resolution. **Hyperspace** and **Wisdom AI** could not be confirmed as players in this space at all —
the only companies found under those names are a search-database infrastructure company and an
enterprise data-analyst AI respectively.

### The capability nobody advertises

**Across every vendor and startup fetched, not one claims cross-source entity resolution — organisation
and people deduplication — as a product capability.** The closest analogues are Unanet's Contact
Enrichment (appends data to an existing record) and Company Researcher (creates one record from a URL).
Neither merges duplicates across sources. INFERRED from consistent absence across OpenAsset, Unanet,
GovDash, Sweetspot, pWin.ai, Procurement Sciences, Arphie and DeepRFP: none uses the words
"deduplicate", "entity resolution", "record matching" or "canonical ID" anywhere fetched.

### B) verdict

**No product combines all five of ingestion + entity resolution + agentic research + narrative dossier
+ pursuit lifecycle.** The market splits three ways: bid-finding feeds (ConstructConnect, Dodge,
BidPrime, GovSpend); proposal writers over the firm's own content (OpenAsset Shred, Unanet ProposalAI,
Arphie, DeepRFP, Responsive); and GovCon capture platforms that genuinely do ingestion + ranked queue +
lifecycle (GovDash, Sweetspot, Procurement Sciences, pWin.ai). **The AEC-native vendors have not moved
into ingestion or research at all.** The GovCon platforms are architected around federal data models —
NAICS, set-asides, incumbents, recompetes — which do not map onto AEC's client-relationship,
building-typology, project-experience model, and none publishes entity resolution. KOR's BD Brain, built
around Bonfire, BidsAndTenders, MERX, CivicInfo and BC Bid, is ahead of anything shipping commercially
for the AEC use case.

---

## C) AI-native AEC entrants, 2024–2026

Where the venture money actually went. **Almost none of it went to structural.**

| Company | Latest round | Date | Lead / notable investors | Total | Touches structural? |
|---|---|---|---|---|---|
| **Higharc** | **$95M Series C** | **2026-06-30** | Insight Partners; Wellington, Fifth Wall, Spark, Lux, SE Ventures, **Simpson Strong-Tie**, MetaProp | **>$170M** | **Yes — light-frame timber framing** |
| Trunk Tools | $40M Series B | 2025-07-24 | Insight Partners; Redpoint, Innovation Endeavors, Liberty Mutual SV | $70M | No |
| Endra (MEP) | $50M Series A | ~2026, expanded 2026-08-03 | Andreessen Horowitz | — | No |
| Motif | $46M seed + A | 2025-01-30 | CapitalG (Alphabet), Redpoint, Baukunst | $46M | No |
| Augmenta | $10M | 2025-03-12 | Prelude Ventures, Montage | $25.6M | No (electrical only) |
| Qbiq | $16M Series A | 2025-01-15 | Insight Partners; JLL Spark, 10D | $26M | No |
| Document Crunch | $21.5M Series B | 2024-10-09 | Titanium Ventures; **Nemetschek Group**, Fifth Wall | ~$30M+ | No |
| Swapp | $11.5M Series A | 2023-05-17 | Eurazeo, Entrée Capital | $18.5–21.3M | **Unresolved** |
| Snaptrude | $14M Series A | 2023-11-09 | Foundamental, Accel | $21.5–35.8M (trackers disagree) | No |
| Arcol | $3.6M seed (latest disclosed) | 2022-03-17 | Cowboy, Craft; Amar Hanspal, Dylan Field, Tooey Courtemanche | ~$20M claimed by company; trackers say $5.1–17.1M | No |
| Bild AI | $3.1M seed | ~2025-07 | Khosla Ventures (YC W25) | ~$3.5M | No (Division 8 only) |
| Nomic | strategic investment, undisclosed | 2026-08-03 | **Arcadis** | — | Cross-discipline |
| Workorb | $2.6M CAD seed | ~mid-2025 | — | — | Unknown |

### The ones that matter to KOR

**Higharc — the most structurally relevant, best funded company in this scan.** $95M Series C
announced **2026-06-30**, led by Insight Partners, total >$170M since 2019, and — note this for a
structural audience — **Simpson Strong-Tie is on the cap table**. VERIFIED,
[PRNewswire](https://www.prnewswire.com/news-releases/higharc-raises-95m-series-c-to-scale-ai-for-homebuilding-302814598.html).
Per an [AEC Magazine feature dated 2026-07-21](https://aecmag.com/bim/higharc-pushes-buildings-as-data-thinking-into-new-areas/),
the platform **auto-generates timber wall, floor and roof framing in real time** as designers place
walls and openings in plan, dimensioning and code-checking as it goes, representing buildings as
structured data rather than drawings (their example: a Signature Homes project at 6GB in Revit versus
11MB in Higharc). Critically it **self-limits**: "more complex structural engineering continues to
flow out as DXF for engineers to complete." **It automates light-frame residential framing and
explicitly hands off everything harder to human structural engineers.** That is a boundary, and KOR
works on the far side of it — but it is the clearest proof that automated structural generation is
being funded at scale, just in the easiest typology first.

**Nomic — the sharpest document-intelligence edge, and it is already inside two ENR-scale firms.**
AEC AI agents for **drawing review against standards, code compliance checks, submittal review, RFI
research and BIM coordination**, integrating with Autodesk Forma and Bentley ProjectWise. **Arcadis
announced a strategic investment and commercial partnership on 2026-08-03** (amount undisclosed) after
a six-month trial with ~150 Arcadis engineers across 12 countries, of whom 86% said it changed their
workflow. REPORTED, [AEC Magazine](https://aecmag.com/data/arcadis-invests-in-aec-ai-platform-nomic/),
2026-08-03. **Aurecon is a named case study on Nomic's own site, claiming +30% productivity and +20%
engineering capacity** — VERIFIED, [nomic.ai](https://nomic.ai/), fetched 2026-08-20. Self-serve tier
starts at $20/month. (Disambiguation: this is the AEC company, not the Atlas/embeddings company —
verified by direct fetch.) Structural discipline-specificity is not claimed; it is cross-discipline.
Total funding and founding date **could not verify**.

**Swapp — the unresolved question.** Claims "in production today" with **35M+ sq ft of production-grade
construction documents** delivered — full CD sets, "dimensioned, tagged, sheeted, and QA'd to your
firm's standards." 52 employees as of 2026-05-31 (REPORTED, Tracxn). **Two independent fetches of
swapp.ai on 2026-08-20 found the site deliberately avoids naming disciplines.** Secondary coverage
mentions architectural and MEP layouts; structural sheets are neither claimed nor excluded.
**Could not verify** either way. Given that Swapp is the only company shipping AI-generated CD sets at
production volume, this is the single question in this report most worth resolving with a direct demo
request. Roadmap banner says preconstruction, construction-phase and O&M are "coming 2026."

**Trunk Tools — direct overlap with KOR's rebar change tool.** Covered in detail in section D.

**Document Crunch** — GA, three products: **CrunchAI** (cited, sourced answers over contracts, specs
and addenda), **Project Assist** (agentic submittal/notice/RFI drafting) and a risk-tracking platform.
500+ companies; named customers Balfour Beatty, DPR, Swinerton, Webcor. VERIFIED,
[documentcrunch.com](https://www.documentcrunch.com/), fetched 2026-08-20. **Contracts and specs, not
drawings** (VERIFIED absence). The notable structural fact is on the cap table: **Nemetschek Group —
owner of Bluebeam, Graphisoft, Allplan, SCIA — took a strategic position in the $21.5M Series B**
(2024-10-09, led by Titanium Ventures; VERIFIED,
[ENR](https://www.enr.com/articles/59492-contract-analysis-start-up-document-crunch-raises-215m-in-series-b-round)).
An incumbent buying into document AI rather than building it.

**Bild AI — a cautionary tale about scope.** Pitched as "read and understand blueprints using AI"; the
shipped product as of 2026 is **AI estimating and detailing for Division 8 doors, frames and hardware**,
exporting to Comsense, Avaware and ProTech. $3.1M seed ~2025-07 led by Khosla (YC W25). REPORTED —
direct fetches of bild.ai returned 403; corroborated by
[americanbazaaronline](https://americanbazaaronline.com/2025/07/04/construction-startup-bild-ai-raises-3-1-million-to-use-ai-for-affordable-housing-464682/)
and the YC listing. **A funded team with a general blueprint-reading pitch narrowed to one CSI division
to make it work.** That is the shape of the problem KOR is solving in structural.

### Architectural and MEP entrants — for recognition, not threat

**Motif** ($46M, 2025-01-30, CapitalG + Redpoint; founded by **Amar Hanspal**, former Autodesk co-CEO,
and **Brian Mathews**, former Autodesk VP Platform Engineering — VERIFIED,
[TechCrunch](https://techcrunch.com/2025/01/30/ex-autodesk-execs-snag-46m-to-build-the-next-gen-of-architecture-design/))
and **Arcol** are separate competing companies, not a rebrand — confirmed. Both are browser-native
architectural design tools. Amar Hanspal is a personal investor in Arcol while co-founding Motif.
Arcol won Best of Show (BIM) at AIA National, June 2025; named customers include SERA Architects,
Warren and Mahoney, HCM, GWWO. **Snaptrude** (sketch-to-BIM; customers Clark Nexsen, VMDO, MHAWorks),
**Qbiq** (AI space planning; the strongest logo list in this scan — AECOM, JLL, CBRE, Cushman &
Wakefield, Colliers, Skanska, Brookfield; claims 700M sq ft across 62 countries), **Augmenta**
(electrical design agent GA, ACP 2.0 released 2026-06-17; **mechanical and plumbing still "coming
soon"**), **Endra** (MEP automation, $50M Series A led by a16z, US/UK expansion 2026-08-03). **None of
these generate, analyse or compare structural drawings or models** (VERIFIED absence in each case).

**EvolveLab was acquired.** **Chaos** — the V-Ray/Corona/Enscape company — **acquired EvolveLAB on
2025-02-19**, terms undisclosed; Veras AI is being folded into Chaos's rendering engines. REPORTED
(CG Channel, AEC Magazine, Engineering.com), confirmed by the "part of the Chaos ecosystem" banner on
evolvelab.io (VERIFIED, primary). Veras is still sold at $29–59/month with 2026 Revit support.
EvolveLab no longer exists as an independent competitor.

**"Fina" could not be found.** Searched "Fina.ai AEC", "Fina architecture AI", and fetched fina.ai
directly — it 302-redirects to a domain-marketplace listing at atom.com, i.e. the domain appears parked
for sale. The only operating "Fina AI" is an unrelated bookkeeping tool. Nearby real companies that may
be the intended reference: **Finch** (Swedish AEC collaboration, €2.5M, 2022) and **Build** (AI
infrastructure due-diligence, $8.5M seed, AEC Magazine 2026-07). **Treat "Fina" as likely a
misremembered name.**

### Two incumbent signals worth more than any single startup

- **Revizto opened its project data to external AI platforms — ChatGPT and Claude — via a developer
  portal on 2026-07-28**, and its homepage now advertises an **MCP server**: *"Access project data via
  your trusted AI."* VERIFIED, [revizto.com](https://revizto.com/en/), fetched 2026-08-20.
- **Bluebeam Revu can now be connected to Claude, GitHub Copilot and AnythingLLM**, using prompts to
  find information, create and update markups, create custom columns and turn metadata into insights.
  VERIFIED, [bluebeam.com/bluebeam-max](https://www.bluebeam.com/bluebeam-max/), fetched 2026-08-20.
- **PlanRadar** shipped AI agents on **2026-08-20** that "complete the action itself, without an
  approval step." REPORTED.

**The incumbents are exposing MCP endpoints rather than ceding the AI layer.** That is directly
relevant to how KOR should position its own MCP work: the protocol is becoming table stakes; the
tools behind it are the asset.

---

## D) Structural-engineering-specific AI and automation

**The deepest section, and the one that decides whether KOR keeps building.**

### D.1 — Drawing → analysis model: the incumbents have not moved

**CSI (ETABS / SAFE / SAP2000) ships no AI, in any 2025 or 2026 release.** Checked directly: ETABS
v22.4.0 enhancements (2024-12-23), the csiamerica.com news feed through CSiPlant v10.1.0 (2026-08-17),
and **ETABS v23.3.0 / v23.3.1 (2026-07-02, latest)**. Every addition is code compliance (ACI 318-25,
CSA A23.3-2024, KDS 41 12 00, ASME B31.8-2025) or analysis mechanics (FEM buckling plugin,
large-displacement links). **Zero mentions of AI or machine learning anywhere in CSI's own release
notes.** VERIFIED, [csiamerica.com/news](https://www.csiamerica.com/news) and
[ETABS enhancements](https://www.csiamerica.com/products/etabs/enhancements/22-22.4.0), fetched 2026-08-20.

**ETABS's DXF import is not a competitor — it is manual tracing.** File → Import → DXF/DWG of
Architectural Plan requires the file to be pre-cleaned to exactly two layers, all blocks exploded,
curves manually pre-segmented into straight chords. **ETABS does not detect walls versus columns
versus openings; it imports lines for the user to trace and assign by hand.** Decades-old
functionality, untouched by any 2025-26 development. VERIFIED,
[docs.csiamerica.com — DXF import](https://docs.csiamerica.com/help-files/etabs/Menus/File/Import/DXF_Import_3D_Model.htm).

**CSiXRevit 2026** (released 2025-09-17) adds Revit 2026 ↔ ETABS/SAP2000/SAFE exchange including rebar
export from ETABS back to Revit. Release existence VERIFIED (csiamerica.com news); exact scope
REPORTED/INFERRED — the product wiki pages returned empty content. **This is analysis-model-to-BIM
interchange between two models that already exist. It is not generation from architectural geometry.**

CSI publishes a VBA/.NET/C#/C++/Python/Matlab API ([csiamerica.com/developer](https://www.csiamerica.com/developer),
VERIFIED) with **no documented AI partner ecosystem and no third-party drawing-to-model tool listing**.
One third-party site observes that ETABS "has no native AI today, but its open API makes it a target
for third-party AI tools" — REPORTED, [aiproplaybook.com](https://aiproplaybook.com/tools/csi-etabs).

**Autodesk Robot Structural Analysis Professional 2026 ships zero AI features, zero drawing-import
features and zero model-generation features.** The official What's New page is entirely regional code
updates: Eurocode 3 new generation, IS:800:2007, NBC 2020 seismic (Canada), IS 1893, AS 4100:2020 Amd 1,
NS-EN 1998-1, EN 1990:2023 load combinations. VERIFIED,
[help.autodesk.com/view/RSAPRO/2026/ENU](https://help.autodesk.com/view/RSAPRO/2026/ENU/).

**Revit 2026** (GA 2025-04-02) has no native AI; Generative Design got a UX refresh with new sample
studies including "structural grillage optimization" — which optimises a parametric model that already
exists. VERIFIED, [Revit 2026 What's New](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-WhatsNew/).
**"Autodesk Assistant in Revit" is Tech Preview only**, blog dated 2026-04-22 — a natural-language
copilot for querying and editing an existing Revit model. REPORTED (announcement blog returned 403;
title, date and Tech Preview status corroborated across multiple secondary sources). **Autodesk Forma**
was in closed beta in late 2025 with GA "expected" in 2026 (REPORTED, third-party reviews, not
confirmed on Autodesk's own site); it is cloud massing and site analysis — sun, wind, noise, embodied
carbon, concrete-versus-mass-timber comparison at massing level — and **produces no structural analysis
model.**

**Bentley**: STAAD.Pro product page shows no AI features and only "basic DXF" compatibility, with ISM
giving two-way exchange with Bentley's own physical-model format. RAM Structural System is still
actively sold with Revit/Tekla/OpenBuildings data sharing and **zero AI or drawing-import features**.
VERIFIED (absence), [STAAD.Pro](https://www.bentley.com/software/staad-pro/),
[RAM](https://www.bentley.com/software/ram-structural-system/). A Bentley newsroom headline dated
2026-08-13 — "Gold Stevie Award for AI Breakthrough in Construction and Civil Engineering Technology" —
**exists but could not be tied to a specific product**; the article body did not render and follow-up
queries returned noise. **Do not cite it as evidence of a shipped structural-drawing AI feature.**

**Trimble / Tekla**: **Tekla Structures 2026 exists** ("cloud-powered productivity and intelligent data
exchange at every project stage") with **no AI or 2D-to-model features named on the product page**
(VERIFIED absence, [tekla.com](https://www.tekla.com/products/tekla-structures), fetched 2026-08-20).
The release's existence is independently corroborated by RISA's homepage noting RISA-Tekla Link
compatibility with Tekla Structures 2026. **A full read of Tekla's 2025-26 release notes could not be
completed** — tekla.com/news returned 404 and the support release-notes URL 404'd. **This is a genuine
gap, flagged rather than filled.**

**Nemetschek group**: SCIA's news page for 2025-26 covers a student contest, second-generation
Eurocodes, and an ISO 27001 certification — **no AI, no DXF-import features** (VERIFIED,
[scia.net/en/news](https://www.scia.net/en/news)). The group newsroom's only AI-branded product news is
**Bluebeam Max** — construction PDF markup, not structural analysis — plus academic partnerships with
TUM Munich and Novatr. **No acquisition of an AI drawing-recognition startup found.** VERIFIED,
[nemetschek.com newsroom](https://www.nemetschek.com/en/newsroom). Graphisoft AI Visualizer and
Allplan/FRILO AI status **could not be verified** this pass.

### D.2 — The mid-tier and the plumbing: all require structured input

| Tool | What its "AI" is | Requires as input | AI? |
|---|---|---|---|
| **IDEA StatiCa** (+ Checkbot) | none — rules-based automation, "cut connection design time by 80%" | an already-built analysis model (imports from 40+ tools: SAP2000, ETABS, Midas…) | **zero mentions of AI on the homepage** |
| **Speckle** ("Speckle Intelligence") | query/reporting layer over connected models | models already in Revit/Rhino/Civil 3D | AI query layer only |
| **Karamba3D** 3.1.4 | none | parametric geometry built in Grasshopper | no |
| **RISA** | none | — | no |
| **Dlubal RFEM/RSTAB** | "Mia AI Ambassador" is **conference branding**, not a product feature | — | no |
| **SkyCiv** | nothing on the homepage; news page stale to June 2021 | — | could not verify |

All VERIFIED by direct fetch on 2026-08-20 except where noted. **Not one of them reads a raw
architectural DXF or PDF and infers wall-versus-column, openings, headers or storey stacking.**

**Hypar and Konstru could not be verified** — hypar.io returned a near-empty page; Konstru searches
returned irrelevant results. **cove.tool has rebranded to "cove" (cove.inc)** and its product is
**Vitras.ai** — AI code compliance, energy and cost analytics for architecture, **no structural
analysis or drawing-to-model** (VERIFIED, [cove.inc](https://cove.inc/), fetched 2026-08-20).
**Skema (skema.ai)** is real and actively selling: schematic design → **construction-document-ready
native Revit model**, integrating Rhino, Grasshopper, Miro, SketchUp — **no structural analysis
capability claimed anywhere** (VERIFIED).

### D.3 — Quantity takeoff AI: real, crowded, and aimed at contractors

| Product | Structural quantities? | Status | Evidence |
|---|---|---|---|
| **Togal.AI** | **No.** Assemblies cover drywall, ceilings, wood framing, metal framing; **"expansion into concrete in 2026"** is roadmap. Trade list across 10+ trades **does not include structural.** | GA | VERIFIED, [2025 Features Roundup](https://www.togal.ai/blog/2025-features-roundup) + togal.ai, fetched 2026-08-20 |
| **Kreo** | **Partially — the closest.** Names Concrete, Framing, Steel and Masonry among eight trades. Auto Measure (text-prompt), One-Click Area, Auto Count. **Pro tier US$175/month includes the AI features.** No rebar-specific claim on the page. | GA | VERIFIED, [kreo.net](https://www.kreo.net/solutions/ai-construction-takeoff-sofware) |
| **STACK** | Concrete & Masonry listed as a trade category; AI auto-counting, measurement suggestions, **version comparison "to catch every change"** | GA | VERIFIED, [stackct.com](https://www.stackct.com/) |
| **Bild AI** | No — Division 8 doors/frames/hardware only | GA | REPORTED (site 403) |
| **Handoff.ai** | No — **residential only** (home builders, remodelers, handymen); explicitly not positioned for commercial or structural | GA | VERIFIED, [handoff.ai](https://www.handoff.ai/) |
| **ConstructConnect / On-Screen Takeoff** | could not verify — takeoff now redirects to hub.isqft.com and the landing page names no AI | — | VERIFIED redirect; AI status not established |
| **Bluebeam** | see below — Max is markup + preview AI, not quantification | preview | VERIFIED |

**Nobody in this list sells structural quantity takeoff as a first-class product.** Kreo comes closest
by naming concrete, framing and steel as trades. Togal — the best-known name — explicitly has concrete
on its 2026 roadmap, not in the product. Every one of these targets GCs and subcontractors, not the
structural engineer of record.

Beam AI, Countfire, Nomitech CostOS, Buildxact and Autodesk Takeoff AI **were not reached** this pass
(search budget exhausted before them). Not checked ≠ found nothing.

### D.4 — Rebar detailing automation

- **Tekla Structures 2026** — rebar automation is long-standing; **no AI features named on the product
  page** (VERIFIED absence). Full release notes could not be read (404s).
- **aSa (Applied Systems Associates)** — asarebar.com refused the connection (ECONNREFUSED) at fetch
  time on 2026-08-20. **Could not verify.**
- **Rebartek** — **rebartek.com 301-redirects to rushskatepark.org**, an unrelated domain, as of
  2026-08-20. Either the company has changed domain or is gone. **Could not verify status** — flagged
  because a dead rebar-automation startup is itself signal.
- **IDEA StatiCa Detail** — concrete D-region design, rules-based, no AI (VERIFIED).
- **No AI rebar-detailing startup surfaced** in any search across this scan. Targeted queries — "AI
  rebar detailing startup", "rebar automation AI funding 2026", "AI rebar detection construction
  startup" — returned nothing across WebSearch, Bing and Brave. CADS RebarCAD, SmartRebar, Soule and
  Toggle Industries **were not reached** and remain unverified.

### D.5 — Drawing comparison and change detection: **this is where KOR is exposed**

**Trunk Tools — TrunkReview.** A **vision-language-model** drawing-revision agent that "scans new
revisions in just a few minutes, identifies both **clouded and unclouded** changes, and produces a
visual overlay and bullet point list." Vendor claims: a 20-sheet bulletin in ~5 minutes; 500+
jobsites; >$50B construction volume; 87% verified field accuracy. **VERIFIED**,
[trunktools.com](https://www.trunktools.com/) and /product, fetched 2026-08-20. **$70M raised, $40M
Series B led by Insight Partners, 2025-07-24.** No structural or rebar-specific claim — it is
discipline-agnostic. **Catching unclouded changes is precisely the value proposition of KOR's rebar
delta tool, generalised to every discipline and funded at $70M.**

**Bluebeam Max — Smart Overlay.** "Detect design changes **across disciplines and drawing scales**."
Bundled with Smart Review (scans documents for missing sheets, missing door or plumbing fixture tags,
gridline inconsistencies), Magic Markups, Stitching, and Connected Studio Sessions with Revit.
**Both Smart Review and Smart Overlay are currently in preview**, with Smart Review "optimized for
US-based vertical commercial construction." **US$590 per user per year, introductory pricing locked
through 2027 renewals.** VERIFIED, [bluebeam.com/bluebeam-max](https://www.bluebeam.com/bluebeam-max/),
fetched 2026-08-20. **Bluebeam is the tool every architect and contractor already has.** When Smart
Overlay leaves preview, cross-discipline change detection becomes a checkbox in software MVE already
owns.

**STACK** ships version comparison "to catch every change" (VERIFIED). **Revizto** ships an API and MCP
server for AI access to project data but names no change-detection AI (VERIFIED). **Procore AI** ships
five named agents — Deep Search, Submittal Review, RFI, Daily Log, Contract Review — with 150+ built-in
actions, and **no takeoff, no drawing comparison and no conversational financial analytics** among them
(VERIFIED, [procore.com/en-ca/ai](https://www.procore.com/en-ca/ai), fetched 2026-08-20). **Document
Crunch** is contracts and specs, not drawings (VERIFIED).

### D) verdict

**DXF/PDF → structural analysis model is not a solved commercial problem in August 2026. It is
genuinely rare, bordering on absent.**

- Every incumbent analysis vendor checked against its own current release notes — CSI, Autodesk Robot,
  Bentley STAAD/RAM — ships **zero** AI and **zero** automated drawing-to-model capability.
- The real AI investment at the big vendors targets architecture and massing (Forma), Revit-model chat
  (Assistant, Tech Preview), or construction document markup (Bluebeam Max) — **none produces an
  analysis model.**
- Every mid-tier analysis and interop tool — IDEA StatiCa, Speckle, Karamba3D, RISA, Dlubal — either
  has no AI or its AI is a query layer over models that already exist. **All require structured input.**
- The AI-native startups that do read raw drawings — Togal, Snaptrude, Skema, Swapp, Bild — **stop at
  geometry, BIM or takeoff.** None claims loads, diaphragms, releases or anything an analysis engine
  could run.
- **A dedicated search for a startup occupying exactly this niche found nothing**, across many
  phrasings and three engines.

**Honest caveat:** the search budget ran out before several named candidates were reached — Tekla's
actual 2025-26 release notes, Graphisoft AI Visualizer, Hypar, Konstru, aSa, Avvir/PointCab/Imerso,
Beam AI, Countfire, Autodesk Takeoff AI, and a direct AEC-VC portfolio scan. Nothing found contradicts
the white-space conclusion, but it is not airtight. Anyone re-running this should spend a fresh budget
on exactly those names.

---

## E) Firms building their own

**"Everyone is building their own" is not what the public record shows.** Two firms have real,
sustained, verifiable programmes. Most of the names people assume are doing this have published
nothing dated in 2024-26, and several of their GitHub organisations are empty.

### Thornton Tomasetti — CORE studio / CORE.AI: the one real structural precedent

The most important firm in this section, because it is the closest analogue to KOR's engineering tools
and the argument MVE is most likely to reach for.

VERIFIED by direct fetch of [thorntontomasetti.com/core-studio](https://www.thorntontomasetti.com/core-studio)
(2026-08-20): CORE studio, founded **2011**, **21 employees**, "10 web applications and over 150
plugins". The pipeline is documented: **CORE lab** (founded 2016, biannual internal pitch forum — "442
ideas, 65 funded projects") feeds **CORE studio**, which builds winners into tools, which feeds
**TWiiN**, an accelerator that spins proven tools out into independent companies with equity for the
team. Pipeline VERIFIED via BuiltWorlds, "From Idea to Action: A Case Study in Incubation",
2024-05-08.

**CORE.AI is real**, quoted verbatim from the primary page: *"Now, with a new CORE lab R&D project and
freshly allocated resources, we've consolidated our efforts into a new initiative: CORE.AI."* VERIFIED —
but the page carries no publish date; treat as current-as-of-fetch, not a dated announcement.

Named shipped tools (all VERIFIED on that page): **Spotlight** (structural quantity takeoff and data
visualisation), **CSI Toolboxes** (SAP2000/ETABS automation), **Tekla Toolbox**, **TT Toolbox**
(Grasshopper), **Revit Toolbox**, **Design Explorer**, **Framing Repair**, **Skipper** (automatic
structural-bay design), **Konstru**, **T2D2**, **Asterisk**.

**Asterisk is the closest thing anyone has publicly built to KOR's DXF→ETABS tool — and it is a
different problem.** VERIFIED, [thorntontomasetti.com/capability/asterisk](https://www.thorntontomasetti.com/capability/asterisk),
fetched 2026-08-20: it "generates structural solutions – in seconds – from a simple building **massing
model**", using "computational geometry and machine learning built on our 70 years of structural
engineering experience" and "custom ML and generative AI models" for steel, concrete and mass timber,
returning embodied carbon, cost per square foot, floor placement and weight. First generation 2017,
current generation is the second. **Asterisk starts from a massing model and produces a scheme.
KOR starts from the architect's issued DXF plans and produces a runnable ETABS model of a specific
building.** That distinction is the entire pitch, and it should be made explicitly in the room —
because a well-briefed MVE person may say "Thornton Tomasetti already does this."

**Commercialisation**: **Konstru** is a live, sold SaaS product (konstru.com, pricing plus 14-day
trial). **T2D2** was spun out via TWiiN into an independent company with its own CEO (Jonathan
Ehrlich) and a TT-linked advisory board — VERIFIED, [t2d2.ai/about](https://t2d2.ai/about). **TTX
could not be verified** — absent from every primary source checked.

CTO **Robert Otani**, "Beyond Legacy Tools", *Modern Steel Construction*, **March 2026** — argues AI
is "reshaping structural engineering by reducing manual effort, improving coordination, and enabling
faster, data-driven decisions", moving away from legacy 2D toward integrated model-based workflows.
VERIFIED, [thorntontomasetti.com/news/ai-structural-engineering...](https://www.thorntontomasetti.com/news/ai-structural-engineering-moving-beyond-legacy-tools-model-based-workflows).
No product names or ship dates in the article.

Two useful negatives: **github.com/Thornton-Tomasetti does not exist (404)** and
**github.com/CORE-Studio exists with zero public repositories** — both VERIFIED. A reported claim
that CORE has agentic-workflow experiments but "nothing in production yet" **could not be verified**;
the named BuiltWorlds video could not be located, and the CORE studio page contains no mention of
agentic AI at all.

### Buro Happold — BHoM: the largest verifiable body of firm-built code in AEC

VERIFIED and unusually hard evidence: [github.com/BHoM](https://github.com/BHoM) is a real,
cross-firm open-source project — **112 repositories**, core `BHoM` repo at 246 stars (C#),
`documentation` 88 stars, LGPL-3.0, named maintainers visible. **Repositories were updated
2026-08-18 to 2026-08-20 — the days this research ran.** The org describes itself as
"transdisciplinary, software-agnostic and office/region/country-invariant", i.e. built for
industry-wide use. A second org, `BuroHappoldEngineering`, holds four more repos including
`OneClickLCA_Toolkit` (updated 2026-08-13). **BHoM is a BIM/engineering interoperability object
model — not AI, ML or LLM.**

### Arup — Oasys is real; "Neuron" could not be verified

**Oasys** is Arup's software division since **1976** ("Ove Arup Systems"), commercially selling
geotechnical (Gofer, XDisp, PDisp, Alp, Pile, Frew, Slope, Greta), structural (**GSA**, AdSec, Compos,
ADC) and pedestrian-simulation (MassMotion) software; **celebrating 50 years in 2026**, site refreshed
2026-04-28, "trusted by over 6,000 users in more than 40 countries", **no AI-branded product on the
page**. VERIFIED, [oasys-software.com](https://www.oasys-software.com/). This is the clearest
internal-software-commercialised-externally example in all of AEC — and it predates the AI wave by
five decades.

**"Arup Neuron" could not be verified.** Searches on the exact term returned only unrelated Arup Group
and ARUP Laboratories (a medical-lab company) results; arup.com and its digital-services pages
returned 403 to every fetch attempt. **Do not repeat "Arup Neuron" as fact without a fresh check.**

### The firms that have published nothing

| Firm | What is actually public | Tag |
|---|---|---|
| **Gensler** | Gensler Research Institute *Global Workplace Survey 2026* discusses how AI affects workplace design — thought leadership, not a tool. **"Gensler Product Development" could not be verified as a real named entity.** github.com/gensler exists with **zero public repositories**. | VERIFIED |
| **HOK** | **Nothing AI-related on hok.com/news across 2024-26.** Only tech-adjacent mention found anywhere is a design-technology director quote from 2020. | VERIFIED (absence) |
| **Perkins&Will** | "Machine Visions" (Innovation Incubator research on AI-generated imagery — research status) and an unnamed AI platform for building adaptation/lifecycle carbon. New Research Director Gilad Rosenzweig, ~April 2026. No named product, no external sale. | VERIFIED |
| **SOM** | No AI software tool. Research page lists AMIE 1.0 (robotic fabrication with Oak Ridge National Lab), Glass Vault, Timbrel Vault, Bio-Block Spiral — fabrication and materials, not AI. github.com/som is an unrelated individual. | VERIFIED |
| **Walter P Moore** | **Nothing.** Homepage has no mention of an internal software, automation or AI group; /expertise/technology and /insights both 404. "Applied Intelligence" **could not be verified** as a real public term. | VERIFIED (absence) |
| **KPFF** | **Nothing.** Zero AI or technology mentions on kpff.com. Consistent with a quiet, partner-owned firm. | VERIFIED (absence) |
| **AECOM** | No named tool; insights.aecom.com returned empty. **github.com/AECOM exists with zero public repositories.** Notable for a top-three global AEC firm. | VERIFIED |
| **Stantec** | No named tool. github.com/stantecinc's only public repo is `coding-assignment` — a job-candidate take-home test, last updated 2024-07-17. | VERIFIED |
| **WSP** | **github.com/wsp-sag is real and actively maintained** — `tcadr`, `wsp-cheval`, `wsp-balsa` (updated 2026-08-11), `Lasso` (2026-06-17), `activitysim` (2026-06-24). Genuine open-source, but **transportation modelling and data science, not AI/LLM**, and not a firm-wide initiative. | VERIFIED |
| **Zaha Hadid Architects** | zha.com/research names **"Spatial Intelligence"** — *"using data analytics and artificial intelligence to enhance how people experience spaces"*. No tool names, no dates, no external sale. The "CODE" brand does not appear on current content. | VERIFIED |
| **Foster + Partners (ARD)**, **BIG**, **SHoP**, **Bryden Wood** | **Could not verify** any current public artefact. Foster's research and ARD pages 404; big.dk is a pure portfolio; shoparc.com/news has no technology content; brydenwood.com describes only P-DfMA and Reference Design as internal methods. | could not verify |

### The one firm that has publicly described an internal LLM stack

**Henning Larsen** (600-person architecture firm within Ramboll) — director of innovation and
sustainability Jakob Strømann-Andersen described an AI stack built from **Claude, Midjourney and
Google NotebookLM**, plus an enterprise **Krea** agreement, and said the firm "writes a good deal of
its own tooling." Governance: the firm "prefers to set dogmas rather than rules", AI use is "declared
openly to colleagues and clients", tool choice is left to individual staff. REPORTED — AEC Magazine's
coverage of the **Symetri BIM Summit 2026** (Stockholm, ~2026-07-21/22). **This is curated
stack-assembly plus scripting, design-scoped — not a built platform, and nothing to do with BD or
finance.**

**Arcadis bought rather than built**: strategic investment in and firm-wide deployment of Nomic across
12 countries, 2026-08-03 (REPORTED, AEC Magazine). That is the dominant pattern at scale — invest,
deploy, don't build.

### The standout negative finding

**Across every firm checked, not one has publicly shown an internal AI tool for business development,
proposals, win-rate analytics, or financial/project-profitability data.** Every real example found
anywhere in large-firm AEC — Skipper, Asterisk, T2D2, BHoM, Konstru, Spotlight, Machine Visions,
Spatial Intelligence, Henning Larsen's stack — is a **design, structural, sustainability or
damage-detection** tool. Searched directly and repeatedly for "internal AI" plus business
development / CRM / win rate, and "internal chatbot/GPT" plus project or financial data. Nothing.

### Adoption data — one usable number, and it comes from a vendor

**No named industry survey produced a citable "% of AE firms using AI" or "% building custom tools"
figure.** Checked directly: **Deltek Clarity A&E** (47th annual, 2026 cycle — exists, lead-gen gated,
no accessible figures); **ACEC** "Leading Through AI Risk: The Enterprise Framework for Engineering
Firm Leaders", **2026-08-17** (VERIFIED — qualitative only: literature review plus interviews with
21 leaders, no percentages); **AIA** resources (no Firm Survey AI data; current content is ABI and an
M&A survey dated 2026-08-19); **Zweig Group** (AI listed as a consulting service line and a podcast;
no survey; reports store 404); **Dodge SmartMarket "AI for Contractors"**, ~2025-12-05 (exists,
contractor-scoped, content inaccessible); **JBKnowledge** (could not verify a 2025/26 edition).

**The one hard number available is BST Global's**, from *AI + Data Insights 2026*, published
2026-05-04: fewer than a quarter of AEC respondents claim mature or advanced AI readiness, and
**only 1% of firms have achieved widespread adoption of AI-enabled processes.** VERIFIED.

---

## Where KOR is genuinely differentiated

Four claims that survive this scan, in descending order of how safe they are to make in the room.

**1. DXF → ETABS with structural intent inference. Nothing commercial does this.**
Not CSI (ETABS's DXF import is manual line-tracing requiring a two-layer pre-cleaned file, and their
2026 releases contain zero AI). Not Autodesk (Robot 2026 is code updates; Forma stops at massing).
Not Bentley, Trimble or Nemetschek. Not IDEA StatiCa, Speckle, Karamba3D, RISA or Dlubal — all of
which require an already-structured input. Not the AI-native startups — Togal, Snaptrude, Skema and
Swapp stop at geometry, BIM or takeoff. Thornton Tomasetti's Asterisk, the nearest published thing,
starts from a **massing model**, not the architect's issued plans. A targeted search for a startup in
this niche found nothing across three engines. **The hard part KOR solved — classifying a ribbon of
lines into wall panels with centrelines and true thickness, mapping a sheet titled "LEVEL 29 PLAN
(L29-35)" onto seven storeys, treating rings inside rings as openings, and merging into an .e2k that
ETABS itself exported — is the part nobody else is attempting.**

**2. BD Brain's full combination.** No product anywhere combines public tender ingestion + cross-source
entity resolution + agentic research + narrative dossier generation + pursuit lifecycle. The AEC
vendors (OpenAsset, Unanet CRM) do none of the first four. The GovCon platforms that do ingestion and
lifecycle (GovDash, Sweetspot, Procurement Sciences, pWin.ai) are shaped around federal data models —
NAICS, set-asides, incumbents, recompetes — and **not one of them publishes entity resolution as a
capability.** KOR's sources (Bonfire, BidsAndTenders, MERX, CivicInfo, BC Bid) are not in anyone's
product.

**3. Nobody in AEC has publicly built BD or financial AI over their own data.** Not Gensler, HOK,
Perkins&Will, SOM, Arup, Thornton Tomasetti, Walter P Moore, KPFF, AECOM, Stantec or WSP. Every public
firm-built tool in the industry is design, structural, sustainability or damage detection. **A
40-person firm demoing operational and BD AI is not late to a crowded field — it is showing something
the largest firms in the industry have not shown at all.**

**4. Integration across four domains under one roof.** Every competitor in this report is a point
solution. Trunk Tools does not touch finance; Unanet does not touch drawings; Togal does not touch BD;
Higharc does not touch commercial structural. KOR has email/transmittals, ERP analytics, BD, and
engineering tools sharing one data estate and one AI layer. **That is not a product anyone sells,
because no vendor has all four data sets.** It is also the hardest thing to demo and the easiest to
undersell — lead with it.

---

## Where KOR is behind

Stated bluntly, because being blindsided in the room is worse than being uncomfortable now.

**1. Rebar change detection — Trunk Tools is ahead on the general problem and funded at $70M.**
TrunkReview uses vision-language models to catch **clouded and unclouded** changes in a 20-sheet
bulletin in ~5 minutes with a visual overlay and written narrative, across every discipline, on 500+
jobsites. KOR's tool reads callout **text** — deeper on the one axis a rebar detailer cares about (a
per-callout delta plus a bar-list steel-weight delta) but **blind to flattened or scanned sets by
design**, and narrower everywhere else. TrunkReview is not blind to scans; a VLM reads pixels.
**If MVE's contractors already run Trunk Tools, KOR's rebar tool will look like a subset.** The
defensible framing is the steel-weight delta and the refuse-on-unreadable guard, not the change list.

**2. Conversational ERP analytics is no longer novel.** Unanet shipped Champ for ERP to GA on
2026-02-18. Deltek's Ask Dela ships in Vantagepoint — the very system KOR's MCP layer sits on — at no
extra charge. KOR is ahead on depth (~25 typed tools; nobody discloses anything comparable) and on
the fact that it answers questions Deltek's own reporting cannot. **But "we built an AI that answers
questions about our financials" is a 2025 claim in an August 2026 room.** Lead with a question
Ask Dela cannot answer.

**3. Structural quantity takeoff has a well-funded near-neighbour.** Kreo already names Concrete,
Framing, Steel and Masonry as trades with AI auto-measure at **US$175/month**. Togal has concrete
assemblies on its 2026 roadmap and 98%-accuracy marketing with DPR, Clark and Consigli as customers.
KOR's slab tool is more honest (field-slab-only, exclusions stated, three-way cross-check) and more
limited (no built-up volume below the slab). **A commodity tool at $175/month that is 80% right will
beat an internal tool that is 95% right and free, in the eyes of anyone who has not read the
methodology.**

**4. Document and drawing intelligence is being bought by firms the size of the ones MVE respects.**
Nomic is inside Arcadis (150 engineers, 12 countries, strategic investment 2026-08-03) and Aurecon
(+30% productivity claimed) doing drawing review, code compliance and submittal analysis, with a
self-serve tier at **$20/month**. KOR has nothing in this lane. If MVE asks about automated drawing
review against standards, the honest answer is "not yet, and there is a good product for it."

**5. Interoperability plumbing is a solved problem KOR should not rebuild.** Speckle, Konstru and
CSiXRevit exist and are maintained. Any KOR effort in model-exchange middleware is duplicated work.

---

## What is likely to commoditise within 12 months

Ranked by confidence. Each of these argues against finishing a KOR module.

**1. Cross-discipline drawing change detection — high confidence, ~6-12 months.**
Bluebeam **Smart Overlay** ("detect design changes across disciplines and drawing scales") is in
**preview today at US$590/user/year**, inside the tool every architect and contractor already owns.
STACK ships version comparison. Trunk Tools ships TrunkReview. **When Smart Overlay leaves preview,
generic change detection becomes a checkbox in software MVE already licenses.** KOR's rebar delta
survives only on the structural-specific output — the bar-list steel-weight delta — not on the
"what changed" list. **Do not invest further in the generic comparison path.**

**2. Conversational analytics over ERP data — already happening.**
Unanet GA'd it in February. Deltek ships it in Vantagepoint free. Within twelve months this is a
line item on every PSA vendor's feature matrix. **What does not commoditise is the tool layer beneath
it** — the typed, domain-specific analytical functions that encode KOR's own metric definitions.
Vendors ship a chat box over their own schema; they cannot ship KOR's methodology.

**3. MCP endpoints over project data — already table stakes.**
**Revizto ships an MCP server** ("Access project data via your trusted AI", 2026-08-20 fetch) and
opened a developer portal to ChatGPT and Claude on 2026-07-28. **Bluebeam Revu connects to Claude,
GitHub Copilot and AnythingLLM.** Procore ships five named agents with 150+ actions. **The protocol
is not the moat.** KOR should stop describing the MCP server as the achievement and start describing
the 25 tools behind it.

**4. Architectural takeoff and general document Q&A — commoditised now.**
Togal, Kreo, STACK, Bild, Handoff, Trunk Tools, Document Crunch, Procore, Nomic. Anything KOR builds
in generic document Q&A over project files is competing with $20-per-month products backed by $70M.

**5. Light-frame residential structural generation — funded and moving.**
Higharc auto-generates timber wall, floor and roof framing in real time, with **$95M raised on
2026-06-30 and Simpson Strong-Tie on the cap table**. It explicitly hands off "more complex structural
engineering" as DXF. **Not a threat to KOR's concrete high-rise work — but it is proof that the
capital exists and the easiest typology is already taken.** Expect the boundary to move.

**What is NOT commoditising within 12 months:** DXF/PDF → analysis model with structural intent
inference. The incumbents are not building it, no startup was found building it, and the interop
layer explicitly requires structured input. **This is where KOR's engineering-tool investment should
concentrate.**

---

## What MVE plausibly already owns

INFERRED unless noted — MVE publishes no software list. Its
[careers page](https://mve-architects.com/careers/) names **no software at all** and routes postings
through ADP Workforce Now (VERIFIED absence, fetched 2026-08-20). Treat this section as the list of
things to *ask about*, not assert.

- **Bluebeam Revu** — near-universal in US architecture. **If they have upgraded to Max (US$590/user/yr),
  they already have Smart Review and Smart Overlay in preview.** Ask this early; it changes how the
  rebar tool should be pitched.
- **Autodesk Revit + Autodesk Construction Cloud / Docs** — a ~100-person multifamily architect is a
  Revit shop. Autodesk Assistant in Revit is Tech Preview (2026-04-22), so they will have heard of it.
- **A PSA/ERP** — most likely Deltek Vantagepoint or Ajera, possibly BQE. **If Ajera Cloud or
  Vantagepoint, they have Ask Dela already** and will benchmark KOR's virtual CFO against it.
- **OpenAsset** — the default DAM for AEC marketing teams; if they have it, they have Shred.ai's
  proposal drafting and will assume KOR's BD Brain is the same category. **It is not — Shred works
  only on the firm's own content and ingests no tender feeds.** Have that distinction ready.
- **Newforma or a SharePoint/Egnyte estate** — covered by another researcher.
- **Enscape / V-Ray (Chaos)** — and therefore, since the 2025-02-19 acquisition, exposure to **Veras**
  AI rendering.
- **Procore** — more likely on their contractors' side than theirs, but their GC partners will have it,
  which is how Trunk Tools and Document Crunch reach the project.

---

## Sources, with dates

All URLs below were fetched or search-verified on **2026-08-20** unless the source itself carries a
date, which is given.

**A) PSA platforms** — unanet.com/news: Wyatt investment (2025-10-01), Champ AI launch (2025-11-12),
product innovations bundle (2025-12-09), Champ for ERP GA (2026-02-18), Champ Agents expansion
(2026-07-30); unanet.com/champai; unanet.com/wyatt-beta-terms-and-conditions ·
bstglobal.com/erp, /insights, /ai-summit-2025, /news (AI + Data Insights 2026, 2026-05-04);
globenewswire.com 2024-06-25 (BST Insights); businesswire.com 2025-11-05 (survey launch), 2026-04-21
(AI Summit 2026 registration) · deltek.com/en/blog/ask-dela-in-ajera; /whats-new-in-ajera-103
(2025-11-04); /vantagepoint-ask-dela; help.deltek.com Vantagepoint 7.0 Dela overview ·
support.monograph.com dashboard collection; monograph.com/benefit/automate-your-work;
monograph.canny.io/changelog (Smart Time Suggestions, 2026-06-16); monograph.com/blog/ai-project-forecasting-budget-overruns ·
finance.yahoo.com — Total Synergy + Monograph merger (2026-08-10) · help.totalsynergy.com December
2025 release; totalsynergy.com Factor A/E acquisition (2025-07-24) · appexchange.salesforce.com
listings a0N30000008ZFI5EAO and 4ca7c25a-dd0f-4d94-9e23-1f0c44d48c41 ·
milientsoftware.com — Cubic Interactive acquisition (2025-10-21) · prnewswire.com — Clearview is now
Unanet A/E (2020-05-05).

**B) BD and proposal tooling** — openasset.com/about, /shred · unanet.com/crm-aec/ai-enhanced-and-smart-technology,
/crm-aec/proposals · bidtracer.com · govdash.com press release and
/blog/ai-agents-government-contract-opportunity-discovery (2026-08-12); siliconangle.com (2026-01-15);
bci.ca · semafor.com (2024-08-07) and ycombinator.com/companies/sweetspot; sweetspot.so/govcon-workflows ·
prnewswire.com — Procurement Sciences Series B; Procurement Sciences acquires Rogue AI (~2026-02) ·
pwin.ai; govconwire.com — TechnoMile/pWin.ai partnership (2026-02-10); **vultron.ai — "Vultron has
joined forces with pWin.ai" banner (VERIFIED 2026-08-20)**; builtinsf.com (2025-07-16) ·
tendium.com · arphie.ai blog and axios.com (2024-11-14) · deeprfp.com · unanimous.ai and Wikipedia ·
ontra.ai · usekojo.com and businesswire.com (2025-04-18) · rogo.com/product.

**C) AI-native entrants** — prnewswire.com — Higharc $95M Series C (2026-06-30); aecmag.com Higharc
feature (2026-07-21) · trunktools.com and /product; trunktools.com Series B release (2025-07-24) ·
documentcrunch.com; documentcrunch.com/news/series-b and enr.com (2024-10-09) · aecmag.com — Arcadis
invests in Nomic (2026-08-03); nomic.ai · swapp.ai; archinect.com (2023) · techcrunch.com — Motif
(2025-01-30); businesswire.com (2025-01-30) · arcol.io and /press · snaptrude.com; techcrunch.com and
aecmag.com (2023-11-09) · qbiq.ai; siliconangle.com and timesofisrael.com (2025-01-15) · augmenta.ai;
globenewswire.com and betakit.com (2025-03-12) · aecmag.com — Endra (2026-08-03) · evolvelab.io
(Chaos acquisition banner, deal 2025-02-19) · ycombinator.com/companies/bild-ai; americanbazaaronline.com
and proptechbuzz.com (2025-07) · revizto.com; bluebeam.com/bluebeam-max.

**D) Structural-specific** — csiamerica.com/news; csiamerica.com/products/etabs/enhancements/22-22.4.0;
docs.csiamerica.com ETABS DXF import; csiamerica.com/developer · help.autodesk.com/view/RSAPRO/2026/ENU;
help.autodesk.com/cloudhelp/2026/ENU/Revit-WhatsNew · bentley.com/software/staad-pro, /ram-structural-system;
bentley.com newsroom (Stevie Award headline, 2026-08-13, unattributed) · tekla.com/products/tekla-structures ·
scia.net/en/news; nemetschek.com/en/newsroom · ideastatica.com and /checkbot · speckle.systems ·
karamba3d.com · risa.com · dlubal.com/en · skyciv.com · skema.ai · cove.inc · togal.ai and
/blog/2025-features-roundup · kreo.net/solutions/ai-construction-takeoff-sofware · stackct.com ·
handoff.ai · hub.isqft.com/takeoff · bluebeam.com/bluebeam-max · trunktools.com/product ·
revizto.com/en · procore.com/en-ca/ai · rebartek.com (301 to unrelated domain, 2026-08-20) ·
asarebar.com (connection refused, 2026-08-20).

**E) Firms building in-house** — thorntontomasetti.com/core-studio, /capability/asterisk,
/news/ai-structural-engineering-moving-beyond-legacy-tools-model-based-workflows (Modern Steel
Construction, 2026-03); builtworlds.com incubation case study (2024-05-08); t2d2.ai/about;
konstru.com; github.com/CORE-Studio (empty), github.com/Thornton-Tomasetti (404) ·
github.com/BHoM (112 repos, updated 2026-08-18/20), github.com/BuroHappoldEngineering ·
oasys-software.com (50 years in 2026, refreshed 2026-04-28) · gensler.com research-insight;
github.com/gensler (empty) · hok.com/ideas and /news · perkinswill.com/research and /insights ·
som.com/ideas/research-innovation · walterpmoore.com · kpff.com · github.com/AECOM (empty),
github.com/stantecinc, github.com/wsp-sag · zha.com/research · aecmag.com — Symetri BIM Summit 2026,
Henning Larsen (~2026-07-21/22) · acec.org "Leading Through AI Risk" (2026-08-17) · aia.org/resources
(M&A survey 2026-08-19) · info.deltek.com/Clarity (47th annual, gated) · zweiggroup.com ·
construction.com/toolkit/reports — Dodge SmartMarket "AI for Contractors" (~2025-12-05).

**KOR internal, for accuracy of the differentiation claims** —
`docs/DxfToEtabs.md`; `docs/Structural-Takeoff-Tools-Summary-2026-06-29.md` (286 tests green,
31065 IFT→IFC: 69 sheets compared, 21 changed; overlay output 0.02% vs reference);
`docs/architecture/Kor.Operations.Mcp.md`; `docs/audit-2026-08/00-INVENTORY.md`;
`docs/bd-dossier-mve-mclarand-2026-06-17.md`.

**Named but not reached** (search budget exhausted; "not checked", not "found nothing"): Tekla's
2025-26 release notes, Graphisoft AI Visualizer, Allplan/FRILO, Hypar, Konstru's current state, aSa,
Avvir, PointCab, Imerso, Kaarta, Beam AI, Countfire, Nomitech CostOS, Buildxact, Autodesk Takeoff AI,
CADS RebarCAD, SmartRebar, Soule, Toggle Industries, Foster + Partners ARD, BIG, SHoP, and a direct
AEC-VC portfolio scan.
