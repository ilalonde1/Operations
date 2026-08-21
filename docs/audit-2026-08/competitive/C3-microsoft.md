# C3 — "Why not just use Microsoft?"

**Competitive intelligence for the MVE technical demo.**
Compiled 2026-08-20. All product-state claims verified live against primary sources on that date, because
the model's training cutoff (May 2026) is three months stale and this market moves monthly.

**Evidence tags used throughout:**
**VERIFIED** = learn.microsoft.com, an official Microsoft pricing page, the Azure Retail Prices API, or
vendor primary documentation. **REPORTED** = secondary source, practitioner blog, press.
**INFERRED** = labelled reasoning, usually arithmetic over two VERIFIED inputs.
Where something could not be established, it says **could not verify** and names what was searched.

---

## 1. Executive verdict

**The Microsoft argument is genuinely strong, and it has become much stronger in the last twelve months.
It is not a strawman and should not be treated as one in the room.** Microsoft now ships a governed
natural-language-over-data agent (Fabric data agent), it exposes that agent over **MCP** — the same protocol
KOR built on — and it ships a conversational assistant across Outlook, SharePoint and Teams. A firm starting
from zero today would be foolish not to price this path first.

**But the argument breaks in five specific, checkable places**, and each break is documented by Microsoft or
by Deltek, not asserted by us:

1. **The data cannot get there in real time.** KOR's Deltek Vantagepoint is a Deltek-hosted tenant reached
   through a DataDirect Hybrid Data Pipeline ODBC endpoint over HTTPS/443 — there is no SQL Server for
   Fabric to mirror and no TDS endpoint to DirectQuery. Microsoft's own Power Query ODBC connector page lists
   its supported capabilities as **"Import"** only — DirectQuery is not among them. Import mode caps at
   **8 scheduled refreshes/day** on Pro and **48/day** on capacity. And even the theoretical DirectQuery path
   dies twice over: Deltek gates direct database access behind **Flex Cloud or a Vantagepoint Intelligence
   licence**, and points reporting at a **read-only replica** — while Microsoft's own DirectQuery guidance says
   *"best optimization results are often achieved by applying optimizations to the source database."* You
   cannot index a replica you do not own. The "real-time views" in KOR's product are not reproducible on this
   path; they become half-hourly snapshots at best.
2. **Microsoft's conversational layer cannot see the email KOR files.** Verbatim, from a page dated
   **today, 2026-08-20**: *"Microsoft Copilot is only supported on primary mailboxes that are hosted on
   Exchange Online. It isn't available on a user's archive mailbox, group mailboxes, or shared and delegate
   mailboxes that they have access to."* Filed project correspondence is precisely archive/shared/group mail.
3. **The vendor's own AI over the same data is weaker than KOR's.** Deltek's Ask Dela — Deltek's AI over
   Deltek's own database — documents: *"You cannot query Ask Dela for aggregate data, such as information
   about the top projects by revenue,"* WBS1 only, single record per conversation, two questions of memory.
   KOR's MCP catalogue answers exactly those aggregate questions across 24 typed tools.
4. **Microsoft says out loud that Copilot over a complex model may not be fit to ship.** Verbatim:
   *"In general, you should test your model with Copilot to determine whether you get consistently correct
   and reliable results. If not, you might want to consider advising users not to use Copilot to consume your
   semantic model."* And: *"the underlying model … is nondeterministic and isn't guaranteed to produce a
   correct answer, or the same answer with the same prompt, model, and data."* KOR's Deltek model is
   textbook "complex" by Microsoft's own criteria — multi-currency, three ledgers, disconnected tables.
5. **The Copilot Studio route to a live database cannot express a financial query.** The Power Platform SQL
   Server connector's aggregation surface is five OData functions — `average, max, min, sum, countdistinct` —
   with **no GROUP BY and no multi-table join**, and it documents its own **non-determinism** ("duplicating
   records… when pagination is enabled"). The deterministic-compute feature that would fix this is preview
   and reaches **CSV/XLSX and SharePoint libraries only — not SQL**. WIP, lifetime profitability and
   utilisation-by-office are all multi-table joins with GROUP BY. **The only supported shape is a stored
   procedure computing the number and the agent narrating it — which is exactly the software KOR already
   wrote.**

**Honest costing.** The narrow Microsoft path — a Fabric F2 with Power BI Pro for 40 staff — is **~$8,600/yr
in licences**, which is cheap. That number is real and should be conceded. What it does not include is the
**~9–17 person-months** of engineering and modelling to make it produce answers a partner would sign, the
named human who owns the semantic model forever after, and the fact that at the end of it three of KOR's
four demo capabilities still do not exist.

**One concession that must be made first, and made generously.** Microsoft has adopted MCP across the stack —
Copilot Studio (GA since 2025-05-29), Foundry Agent Service (GA), Azure API Management (GA), VS Code, Visual
Studio, M365 Copilot declarative agents (GA), and Fabric data agents (preview). Microsoft and GitHub sit on
the MCP steering committee. **And Claude is a first-class GA model on Microsoft Foundry**, with Azure-native
endpoints and Entra auth. So "we're a Microsoft shop" is no longer an argument against either MCP or Claude.

**But read what that concession actually implies.** Because every one of those surfaces speaks the same
protocol, **the MCP server KOR already runs is consumable from all of them without modification.** Putting
KOR's virtual CFO inside MVE's Teams is a URL and a token. Microsoft's MCP adoption does not commoditise
KOR's work — it removes the integration objection to it.

**The one-line answer to give MVE's technical lead:** *"You could build the dashboard with Power BI in a
quarter. You could not build the conversation, because the conversation is not a BI feature — it's twenty-four
typed functions over eight Deltek quirks that took us a year to find, and Microsoft's own docs say their
agent can't do aggregates, can't read shared mailboxes, and shouldn't be trusted on a complex model. And
because Microsoft standardised on MCP, you can call ours from Teams tomorrow."*

---

## 2. The steelman — MVE's argument, at its strongest

Stated as well as it can honestly be stated. Every claim in this section is true.

> *"You're a forty-person engineering firm. You are already an M365 tenant. You already pay Microsoft.
> Microsoft now sells, off the shelf:*
>
> - ***Microsoft Fabric** — one platform for ingestion, storage, modelling and reporting. It has real
>   capacity SKUs starting at F2, and Microsoft publishes the price. And it is not a science project:
>   Nadella told investors on 2026-07-29 that Fabric has **"over 40,000 paid Fabric customers, up more than
>   60% year-over-year"**, on a **">$2B annual revenue run rate"** as of January 2026. Microsoft has been a
>   Gartner Analytics & BI Leader for nineteen consecutive years.*
> - ***Fabric data agents** — a governed, read-only, natural-language Q&A agent over your data. Microsoft's
>   own documentation calls it **"a generally available feature."** It does NL2SQL, NL2DAX and NL2KQL, it
>   enforces the calling user's permissions, it respects Purview DLP, and it can carry organisation-specific
>   instructions and example queries. That is, structurally, a "virtual CFO."*
> - ***MCP** — and this is the part that should worry you. A published Fabric data agent exposes an
>   **MCP server endpoint** at `https://api.fabric.microsoft.com/v1/mcp/workspaces/{ws}/dataagents/{id}/agent`.
>   Microsoft adopted your protocol. Your differentiator is now a Microsoft feature.*
> - ***Microsoft 365 Copilot** at $30/user/month — chat across Word, Excel, Outlook, Teams, SharePoint,
>   grounded in tenant content with the user's own permissions, plus prebuilt Researcher and Analyst agents.*
> - ***Copilot Studio** — build a custom agent, publish it to Teams. And if our staff already hold M365
>   Copilot licences, the documentation says employee-facing agent usage — classic answers, generative
>   answers, agent actions, tenant graph grounding — is **"No charge"** against the Copilot Studio meter.*
> - ***Claude models are available on Microsoft Foundry**, billed through the Microsoft Marketplace at
>   standard Anthropic API rates. So even "we use Claude" is not a reason to leave the Microsoft estate.*
>
> *Against that: you built ~364,500 lines of C# across 92 projects in five repositories, maintained by
> essentially one person. When that person is unavailable, who fixes the Deltek FX bug at month-end? We would
> be buying a dependency on an individual, not on a vendor. And Deltek is shipping Dela into Vantagepoint
> anyway — the AI project advisor, Ask Dela, nightly insight generation. Why would we pay for a bespoke
> version of something two vendors are giving us in the base product?"*

**That is the argument. It is a good argument. Sections 3–8 test it against the documentation; sections 9–11
price it.**

---

## 3. Microsoft Fabric — what it is, what it costs, what it takes

### 3.1 Real pricing — from the Azure Retail Prices API, not a marketing page

The public Fabric pricing page (`azure.microsoft.com/en-us/pricing/details/microsoft-fabric/`) renders all
figures client-side and returned `$-` placeholders when fetched on 2026-08-20 — so the numbers below come
from the **Azure Retail Prices API**, queried directly on 2026-08-20, `currencyCode=USD`,
`armRegionName=westus2`. This is Microsoft's own billing feed and is the strongest available source.

| Meter | Rate | Effective from | Tag |
|---|---|---|---|
| Fabric Capacity — capacity usage | **$0.18 per CU-hour** | 2023-11-01 | VERIFIED |
| Fabric Capacity Reservation — 1 year | **$938.00 per CU** (per year) | 2023-11-01 | VERIFIED |
| Fabric Capacity Reservation — 3 years | **$2,814.00 per CU** | 2026-04-01 | VERIFIED |
| Capacity Overage | **$0.54 per CU-hour** (3× the base rate) | 2025-10-01 | VERIFIED |
| Copilot and AI — capacity usage | **$0.18 per CU-hour** | 2024-06-01 | VERIFIED |
| Copilot and AI — on-demand usage | **$0.18 per CU-hour** | 2026-08-01 | VERIFIED |
| OneLake Storage Hot | **$0.023 per GB/month** | 2023-11-01 | VERIFIED |
| OneLake Storage Cold | **$0.004 per GB/month** | 2026-07-01 | VERIFIED |

**Derived SKU costs (INFERRED — arithmetic over the VERIFIED rates above; 730 h/month, 8,760 h/year):**

| SKU | CUs | PAYG /month | PAYG /year | 1-yr reserved /year |
|---|---|---|---|---|
| F2 | 2 | $262.80 | $3,153.60 | **$1,876** |
| F4 | 4 | $525.60 | $6,307.20 | $3,752 |
| F8 | 8 | $1,051.20 | $12,614.40 | $7,504 |
| F16 | 16 | $2,102.40 | $25,228.80 | $15,008 |
| F32 | 32 | $4,204.80 | $50,457.60 | $30,016 |
| **F64** | 64 | $8,409.60 | $100,915.20 | **$60,032** |

Reservation discount vs pay-as-you-go: **40.5%** (INFERRED: `1 − 938 / (0.18 × 8760)`).
Pricing is regional; Azure bills per second with a one-minute minimum (VERIFIED,
[Fabric licenses](https://learn.microsoft.com/en-us/fabric/enterprise/licenses), ms.date 2026-06-15,
updated 2026-08-05).

### 3.2 The F64 cliff — the licensing fact that decides the whole cost model

VERIFIED, same page:

> *"On F SKUs smaller than F64, each user viewing Power BI content must have Pro, PPU, or an individual trial.
> On F64 or larger, users with only a Free license and a viewer role can view Power BI content."*

So there are exactly two shapes for a 40-person firm, and they are an order of magnitude apart:

- **F2 + 40 × Power BI Pro** = $1,876 + $6,720 = **$8,596/yr**
- **F64 + free viewers** = **$60,032/yr** (+ a handful of Pro seats for authors)

For 40 people, F2 + Pro wins decisively. Note that this is a real and favourable fact for the Microsoft case
and should be conceded plainly. The catch is in §3.4.

Also VERIFIED on that page: *"Microsoft is consolidating purchase options and retiring the Power BI Premium
per-capacity SKUs"* — P SKUs are a dead end; F SKUs are the only forward path.

### 3.3 Fabric data agents — the "virtual CFO" equivalent

**Status: contradictory in Microsoft's own docs, and the report should say so.**
[concept-data-agent](https://learn.microsoft.com/en-us/fabric/data-science/concept-data-agent)
(ms.date 2026-05-11) states plainly: *"Data agent in Microsoft Fabric is a **generally available** feature."*
But the [release-status matrix](https://learn.microsoft.com/en-us/fabric/fundamentals/copilot-ai-feature-state)
(ms.date 2026-06-19) lists the Data Science row — which bundles data agent, AI functions and Foundry Tools —
as **Preview**. Both VERIFIED; treat "GA" as the safer reading for the agent itself, "preview" for the
surrounding toolchain.

**How it works (VERIFIED, concept-data-agent):** Azure OpenAI Assistant APIs orchestrate; the agent picks a
source, then invokes **NL2SQL** (Lakehouse/Warehouse), **NL2DAX** (Power BI semantic models), **NL2KQL**
(KQL databases), or Microsoft Graph. Read-only is strictly enforced. It runs under the calling user's
credentials and honours Purview DLP and access-restriction policies.

**The hard limits that matter for a Deltek use case (all VERIFIED):**

| Limit | Value | Why it matters to KOR |
|---|---|---|
| Data sources per agent | **Maximum five**, any combination | KOR's 24 tools span WIP, AR, cash, backlog, utilisation, PM/DM performance, YoY, earned-vs-invoiced, billed P&L, collections, at-risk. Five sources is a real ceiling. |
| Supported source types | Warehouse, Lakehouse, Power BI semantic model, KQL DB, mirrored DB, ontology, Microsoft Graph | **An external SQL Server or Deltek's ODBC endpoint is not on this list.** Deltek data must be copied into Fabric first. |
| SQL sources | Lakehouse, Data Warehouse, Fabric SQL Database, Mirrored Databases only | Same conclusion, stated a second way ([data-agent-sql-sources](https://learn.microsoft.com/en-us/fabric/data-science/data-agent-sql-sources), ms.date 2026-06-03). |
| Example queries on semantic models | **Not supported** — *"Adding sample query/question pairs isn't currently supported for Power BI semantic model data sources."* | The NL2DAX path — the natural home for Deltek financials — is the one path where you cannot give the agent worked examples. |
| Capacity floor | Paid **F2+** or P1+ | Trial and free SKUs excluded. |
| Cross-geo | Cross-geo processing/storing for AI **must be enabled** | A Canadian firm must consent to data leaving its geography. |

**Advanced NL2SQL (multi-step reasoning) is preview**, and Microsoft's description of why it exists is an
admission about the GA tool (VERIFIED, data-agent-sql-sources): *"NL2SQL doesn't always follow your example
queries closely, and sometimes adds logic or constraints that weren't in the examples… When a question is
ambiguous, NL2SQL tends to commit to an assumption and generate a query anyway."*

### 3.4 Copilot capacity consumption — and the throttling trap

VERIFIED, [copilot-fabric-consumption](https://learn.microsoft.com/en-us/fabric/fundamentals/copilot-fabric-consumption)
(ms.date 2026-05-22):

| Operation | Consumption rate |
|---|---|
| Copilot input prompt | **100 CU-seconds per 1,000 tokens** |
| Copilot cached input prompt | **10 CU-seconds per 1,000 tokens** |
| Copilot output completion | **400 CU-seconds per 1,000 tokens** |

**INFERRED — effective token price.** At the VERIFIED $0.18/CU-hour PAYG rate, 1 CU-second = $0.00005.

- Input: **$5.00 per 1M tokens** · Cached input: **$0.50 per 1M** · Output: **$20.00 per 1M tokens**
- On 1-year-reserved capacity ($938/CU-yr → $0.1071/CU-hour): **$2.97 / $11.90 per 1M in/out**

For comparison, current Anthropic first-party API rates (VERIFIED, Claude API reference, cached 2026-06-24):
Sonnet 4.6 $3/$15, Opus 5 $5/$25, Haiku 4.5 $1/$5 per 1M tokens.
**So Fabric Copilot charges roughly Opus-tier token prices for a model you do not choose, cannot pin, and
cannot swap.** That is a fair and quotable comparison — and it is arithmetic, not opinion.

**The throttling trap (VERIFIED, same page):** Copilot operations are background jobs smoothed over 24 hours.
Microsoft's own worked example: a 2,000-in/500-out request = 400 CU-seconds = 6.67 CU-minutes. An F64 has
1,536 CU-hours/day, so *"customers can run over 13,824 Copilot requests per day before they exhaust the
capacity. **Once the capacity is exhausted, all operations will shut down.**"*

**INFERRED, scaled to F2:** an F2 has 2 × 24 = **48 CU-hours/day**, i.e. ~**432 Copilot requests/day** — and
that budget is shared with every semantic-model refresh, every report render and every dataflow on the same
capacity. Microsoft's own warning, VERIFIED from
[copilot-enable-fabric](https://learn.microsoft.com/en-us/fabric/fundamentals/copilot-enable-fabric)
(ms.date 2026-05-22): *"Enabling Copilot for your entire tenant without proper planning can lead to higher
capacity utilization and other potential risks."* And overage bills at **3× the base rate** ($0.54/CU-hour).

**This is the real cost shape of the cheap option: $8,596/yr buys you a capacity that stops your entire BI
platform when the AI feature gets popular.** The honest mitigation is F4 or F8 ($3,752 / $7,504 reserved),
which is still cheap — but the sizing exercise is a permanent operational job, not a one-time purchase.

### 3.5 What it takes to stand up

**Governance surface, all VERIFIED from copilot-enable-fabric:** tenant setting *"Users can use Copilot and
other features powered by Azure OpenAI"*; tenant setting *"Data sent to Azure OpenAI can be processed outside
your capacity's geographic region, compliance boundary, or national cloud instance"*; delegated capacity
settings if delegation is on; security groups to scope rollout; workspace assignment to a Copilot-enabled
capacity. Plus: *"You can't enable only specific Copilot experiences… You can only control whether Copilot is
enabled at the workload level."*

Also VERIFIED: after buying or scaling capacity, *"it can take up to 24 hours for Copilot to recognize the
change."* Sovereign clouds unsupported.

### 3.6 Operational reality — five things a 40-person firm should price in

1. **Capacity sizing is a permanent job, and Microsoft's own tooling for it is still preview.** The
   **Fabric SKU estimator has been in preview since 2025-05-05** — roughly fifteen months (VERIFIED,
   [fabric-sku-estimator](https://learn.microsoft.com/en-us/fabric/enterprise/fabric-sku-estimator)).
2. **The trial systematically flatters the decision.** The 60-day Fabric trial runs at **F64-equivalent**
   (VERIFIED, Fabric licenses). Microsoft simultaneously advises sizing by measuring on a trial. **INFERRED:**
   measuring on an F64 and then buying an F8 is a predictable way to under-buy.
3. **The overage safety net is not recommended at the tier a small firm would buy.** Capacity overage is in
   public preview and *"it's recommended only for F16 capacities and higher"*, and *"Charges already incurred
   won't be refunded"* (VERIFIED, [enable-capacity-overage](https://learn.microsoft.com/en-us/fabric/enterprise/enable-capacity-overage), ms.date 2026-03-11).
   So at F2–F8 — exactly where a 40-person firm lands — the 3× overage buffer is not advised, and the
   alternative is throttling.
4. **Defect disclosure moved off Microsoft Learn.** Fabric known issues were removed from Learn around
   2026-02-17; the page now says only that they are *"no longer published on Microsoft Learn"* and routes
   users through a manual filter on the support site. The release plan 301-redirects off Learn to a
   JS-rendered roadmap site. The scale is recoverable from the retirement commit itself: the redirection
   JSON carries **90 entries with issue IDs spanning #447 to #1011**, so Microsoft has issued **at least
   1,011 numbered Fabric known issues**. (VERIFIED.) **INFERRED:** this is a governance regression — defect
   history is no longer on an indexed, versioned, Git-backed surface.
5. **SQL analytics endpoint sync lag is documented and awkward.** Normally under a minute, but *"can vary
   from a few seconds to minutes"*; *"you might create a new table in lakehouse, but it's not yet listed in
   the SQL analytics endpoint"*; sync *"halts after 15 minutes of inactivity"*; metadata discovery is one
   instance per workspace, and Microsoft's suggested remedy is *"consider migrating each lakehouse to a
   separate workspace."* (VERIFIED, [SQL analytics endpoint performance](https://learn.microsoft.com/en-us/fabric/data-engineering/sql-analytics-endpoint-performance), updated 2026-08-04.)

**Also pre-announced:** *"Networking billing is coming soon. We will provide at least 90 days of notice
before we start billing"* (VERIFIED, Azure Fabric pricing page) — a known future cost increase.

**Practitioner temperature, honestly caveated (REPORTED):** the most-cited critical piece is Brent Ozar,
*"Fabric Is Just Plain Unreliable, and Microsoft's Hiding It"* (2025-05-19). The article 403s to automated
fetch; the quotable line comes from the Hacker News thread in which Ozar participated (2025-05-21, 117
points): *"Fabric will be great in 5 years, but right now it tends to be unreliable, unergonomic, and
surprisingly expensive."* **Use with the caveat that it is fifteen months old and Fabric has shipped a lot
since.** The independent read worth more weight is Concord's analysis of Gartner's 2026 ABI Magic Quadrant,
which notes Gartner flagged that *"duplicate workspaces and fragmented semantic models are a growing
liability as organizations scale"*, and that Power BI's *"full value is increasingly tied to Microsoft Fabric
capacity"* so *"the BI decision and the platform decision aren't really separable anymore"* — clients now ask
for *"Fabric capacity planning as part of what used to be a straightforward Power BI rollout."*

**Bias flags — apply before quoting anyone on Fabric.** Sandeep Pawar (fabric.guru) is now **Principal PM,
Microsoft Fabric CAT** — no longer independent. Advancing Analytics has pivoted to Databricks (7 of 7 recent
posts). TimeXtender's *"7 Hidden Costs of Microsoft Fabric"* sells a competing platform, and its claim of
*"'phantom CU consumption' of 20%+ overnight on idle systems"* is **uncorroborated — do not cite it.**
EPC Group, MSRcosmos and similar are Microsoft partners whose incentive is "Fabric is hard, hire us":
directionally useful on difficulty, unreliable on magnitude.

---

## 4. Power BI + Copilot — the documented limitations

This section is the core of the technical rebuttal, and every sentence in it is Microsoft's.

### 4.1 What is actually GA

VERIFIED, [copilot-introduction](https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-introduction)
(ms.date 2026-03-23, updated 2026-07-23):

> *"Some Copilot experiences are generally available, and others are in preview. The report agent Copilot pane
> available on the right side of reports is generally available. The Power BI agent available as a standalone,
> full-screen experience accessible from the Power BI left navigation is **in preview**. The Power BI app agent
> … is **in preview**. Copilot experiences for Data Factory, Data Engineering, Data Science, Data Warehouse,
> and Real-Time Intelligence are **in preview**."*

| Surface | Status |
|---|---|
| Copilot pane inside an open report | **GA** |
| Standalone / full-screen Copilot agent | **Preview** |
| Copilot in Power BI apps (app-scoped) | **Preview** |
| Power BI mobile Copilot | **Preview** |
| "Prep data for AI" (AI schemas, verified answers, AI instructions) | **Preview** — the page title is literally *"(Preview)"* |
| Copilot in web modeling | **Preview** |

**INFERRED, and load-bearing:** the only GA surface is report-scoped. The cross-model, open-ended
conversational assistant that the "just use Copilot" argument describes is still preview in August 2026.

### 4.2 Can it answer open-ended financial questions? Microsoft's own answer

**It cannot answer "why", and Microsoft names a "why" question as the example.**
VERIFIED, [copilot-ask-data-question](https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-ask-data-question)
(ms.date 2026-05-28), section *"Unsupported question types"*:

> *"Copilot can't currently answer questions that require generating new insights, such as anomaly detection,
> forecasting, or finding key influencers… **"Why do our sales go down every July?"** This question involves
> generating deeper insights from the provided data. **"How many books do you think we will sell next year?"**
> This question asks for forecasting, which isn't currently supported."*

**Be precise and fair: it CAN compute values not in the model.** Same page:

> *"Copilot can also generate DAX queries to answer questions that require ad hoc calculations, such as
> creating new measures that aren't contained in the model… What was the year-over-year growth for sales? …
> Calculate the ratio of cosmetic product orders to all products."*

So: period-over-period comparison — yes. Causal explanation, anomaly detection, forecasting — no.
That distinction is the crux, and overstating it would be dishonest.

### 4.3 Nondeterminism and accuracy — Microsoft's own words

VERIFIED, [copilot-semantic-models](https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-semantic-models)
(ms.date 2026-04-20, updated 2026-07-21):

> *"If you don't prepare these elements, Copilot mainly produces low-quality and inaccurate outputs that
> might be incorrect or even misleading."*

> *"Irrespective of prompt or model quality, you can still obtain inaccurate or low-quality outputs from
> Copilot… That's because the underlying model — with its current configuration — **is nondeterministic and
> isn't guaranteed to produce a correct answer, or the same answer with the same prompt, model, and data.**"*

> *"Inaccurate responses to data questions can lead to incorrect decisions and actions by business users,
> which produces bad results."*

**And the sentence to read aloud in the room:**

> *"In general, you should test your model with Copilot to determine whether you get consistently correct
> and reliable results. **If not, you might want to consider advising users not to use Copilot to consume
> your semantic model.**"*

On complexity — and note how precisely this describes KOR's Deltek model:

> *"**Model complexity:** The more complex your model is, including having more fields, dependencies, and
> business logic, the more likely you are to experience difficulties when using Copilot. For instance,
> complex patterns like **currency conversion** or **disconnected tables** … might cause unexpected or
> incorrect results."*

KOR's model has currency conversion (CAD/USD at 1.36, bucketed by `PR.Org`), three GL companies, four
sub-ledgers, and a fee/billed/revenue split that is firm-configuration-dependent. By Microsoft's own
criteria it is the hard case.

On fabrication, VERIFIED, [copilot-faq-fabric](https://learn.microsoft.com/en-us/fabric/fundamentals/copilot-faq-fabric):
*"Because AI is generating the summary, **it can try to fill the holes and fabricate data.**"*

### 4.4 Documented caps

| Limit | Value | Source |
|---|---|---|
| Value indexing | 5,000,000 instance values **or 1,000 model entities** (tables/columns) | copilot-prepare-data-ai-faq |
| Text values indexed | Text of **100+ characters is not indexed** | copilot-prepare-data-ai-faq |
| Prompt length | **10,000 characters** per prompt | copilot-introduction |
| AI instructions | **10,000 characters** total | copilot-prepare-data-ai-instructions |
| Description text used | Only the **first 200 characters** of a measure/table/column description | copilot-evaluate-data |
| Verified answers | **250 per model**, ~5–7 trigger phrases each, **500-char** triggers, **max 3 filters**, 10 filter permutations | copilot-prepare-data-ai-verified-answers |
| Capacity change recognition | up to **24 hours** | copilot-introduction |
| Language | **English only** | copilot-ask-data-question |
| Report filters/slicers | **Not applied** to Copilot answers | copilot-ask-data-question |
| Response cache | Identical prompt on unchanged model reuses a cached answer for **24 hours** | copilot-introduction |

There is **no published hard cap on tables/measures Copilot considers**; instead Microsoft documents silent
**schema reduction** — VERIFIED: *"In scenarios where your semantic model schema is too large for Copilot to
render a response, Copilot reduces the model schema… You can tell when this reduction happens by downloading
the Copilot Diagnostics and checking for **"AgentSchemaReduced"** in the warnings."* In other words, on a
large model Copilot quietly stops looking at parts of your data and you find out from a diagnostics file.

**Could not verify:** a documented row cap on data returned by a Copilot answer, or a DAX-query row/time
limit specific to Copilot. Searched copilot-ask-data-question, copilot-reports-overview,
copilot-semantic-models, q-and-a-limitations, copilot-prepare-data-ai-faq. Any such claim should be treated
as unsourced.

### 4.5 What Copilot specifically does to your security boundary

- General case (VERIFIED, copilot-faq-fabric): *"The data that Copilot can access depends on your role-level
  security and user-based permission on Fabric."* RLS holds.
- **Exception, and it is a serious one** (VERIFIED, copilot-prepare-data-ai-verified-answers):
  *"Row-level security (RLS) and object-level security (OLS) **aren't fully supported as security features**
  for verified answers… there are scenarios where data might still be exposed (for example, through the file
  format in Git). **During preview, don't rely on this functionality as a security feature.**"*
- And in Desktop (VERIFIED, copilot-semantic-models): *"**Caution** — … Copilot might use report metadata as
  grounding data. In certain circumstances, report metadata can contain data points, such as column values,
  which might include sensitive information."*

For a firm whose model would contain salaries, utilisation by named person, and per-PM profitability, an
"in most cases" security boundary is not a security boundary.

### 4.6 The work Microsoft says you must do first

This is the effort quantification, and it is Microsoft's prescription, not ours.
VERIFIED, copilot-introduction, section *"Data preparation"*:

> *"**You need to prepare data to work with Copilot.** Model owners need to invest in prepping their data for
> AI… **Without this prep, Copilot can struggle to interpret data correctly — leading to generic, inaccurate,
> or even misleading outputs.**"*

The prescribed order (VERIFIED, copilot-prepare-data-ai-faq):

1. **Define the AI data schema** — hand-pick every table, field and measure Copilot may see; remove anything
   ambiguous.
2. **Create verified answers** — pick a visual, write **5–7 trigger phrases each**, attach filters. Up to 250
   per model.
3. **Write AI instructions** — up to 10,000 characters of business glossary. Microsoft's worked example runs
   to a multi-section document defining terms, disambiguating IDs across three tables, and setting linkage
   rules.
4. **Add descriptions to tables and columns** — only the first 200 characters are used.

Plus star schema, human-readable English names, hidden unused fields, no duplicate names across tables, a
marked date table with a Year→Quarter→Month→Day hierarchy, predefined YTD/MoM measures, and a linguistic
model — which Microsoft itself flags as extra: *"Setting up a linguistic model for your semantic model
**costs additional time and effort on top of your semantic model development tasks**."*

**And it is iterative, with a documented ceiling:** *"You often need to iterate to get the most benefit from
AI instructions."* And, critically for a firm where BD, finance and PMs mean different things by the same
word: *"You can't currently create a glossary based on different groups, or define a term in two different
ways… **You can't currently give *usage* different definitions in the same model.**"*

**Could not verify:** any Microsoft-published estimate in hours or days for this preparation. No such figure
exists in the pages read.

### 4.7 Licensing prerequisite — there is no Copilot without capacity

VERIFIED, copilot-introduction:

> *"Your organization needs a **paid Fabric capacity (F2 or higher)** or **Power BI Premium capacity (P1 or
> higher)**. **A Power BI Pro or Premium Per User (PPU) license alone isn't sufficient — Copilot requires
> organizational capacity.**"*

Current per-user prices, VERIFIED from
[Microsoft's Power BI pricing page](https://www.microsoft.com/en-us/power-platform/products/power-bi/pricing),
fetched 2026-08-20: **Power BI Pro $14.00/user/month paid yearly**; **Power BI Premium Per User
$24.00/user/month paid yearly**. (The rise from $10/$20 was announced effective 2025-04-01 — **REPORTED**;
Microsoft's own announcement blog returned 403/404 on three URL variants, so only the current figures are
VERIFIED.) The page carries the disclaimer *"Prices shown are for marketing purposes only."*

**KOR's starting position matters here:** KOR's tenant holds `POWER_BI_STANDARD` — the free SKU — and no Pro
or PPU seats at all (VERIFIED from KOR's own M365 licence report, `docs/KOR-M365-Licence-Report-2026-08-05-web.pdf`,
generated live from Microsoft Graph 2026-08-05). Every Power BI seat in the MS path is net-new spend.

### 4.8 Refresh latency — why "real-time" does not survive the move

VERIFIED, [refresh-data](https://learn.microsoft.com/en-us/power-bi/connect-data/refresh-data)
(ms.date 2025-09-18, updated 2026-08-11):

> *"Power BI limits semantic models on shared capacity to **eight scheduled daily semantic model refreshes**."*
> *"If the semantic model resides on a Premium capacity, PPU, or Fabric capacity, you can schedule **up to 48
> refreshes per day**."*
> *"Data refreshes must complete in less than **two hours** on shared capacity… On Premium, the maximum refresh
> duration is **five hours**."*

And the connector fact that decides it — VERIFIED,
[Power Query ODBC connector](https://learn.microsoft.com/en-us/power-query/connectors/odbc)
(ms.date 2025-08-27, updated 2026-07-31). Under **"Capabilities Supported"** the page lists exactly:

> *"- Import
> - Advanced options … Connection string … SQL statement … Supported row reduction clauses"*

**DirectQuery is not listed.** KOR's Deltek is reachable only through a DataDirect Hybrid Data Pipeline ODBC
DSN (`vp-ca-hdp01.prd.mydeltek.com:443`, HTTPS) — verified first-hand from KOR's own connection configuration.
There is no on-prem SQL Server to mirror into Fabric and no TDS endpoint to DirectQuery.

**INFERRED, and this is the single most important technical conclusion in the report:** on the Microsoft path,
KOR's Deltek data can only arrive by scheduled Import through an on-premises data gateway, at best every
30 minutes. "Real-time views over Deltek Vantagepoint data" — the phrase in KOR's product description — is
not achievable. It becomes "views over a snapshot up to 30 minutes old, if the gateway is up."

**Three further gateway/refresh facts that bite a small firm (all VERIFIED, refresh-data):**
- The shared-capacity cap **also applies to REST-API-triggered refreshes**; only a manual UI "Refresh now" is exempt.
- **Refresh auto-pauses after two months** with no user visiting the report or dashboard.
- *"A semantic model can only use a single gateway connection,"* **only gateway admins can add data sources**,
  and Microsoft advises **separate gateways for Import vs DirectQuery** models.

### 4.9 Deltek's side of the same problem — access is tier-gated, and the source is a replica you cannot index

Three findings, and together they close the DirectQuery question:

1. **Direct database access is a paid tier, not a default.** Deltek's Cloud Admin Guide, *Set Up Direct
   Database Access*: *"If your firm subscribes to the **Flex Cloud** offering, or if you have a **license to
   use Vantagepoint Intelligence**, you can establish a secure, read-only connection to your Vantagepoint
   transaction database, using ODBC."* And on cloud levels: *"Enterprise Development environments support
   ODBC read/write/execute rights, whereas Flex Cloud provides read-only access."*
   **INFERRED:** a Basic Cloud customer gets no direct database access at all.
   (VERIFIED* — Deltek's own wording; `help.deltek.com` returns a JS shell to a fetcher, so retrieved via
   search extract 2026-08-20. Flagged as VERIFIED* rather than VERIFIED for that reason.)
2. **Reporting runs off a read-only replica, not the live database.** *"Deltek Cloud environments leverage a
   read-only replica as the data source for reporting to maintain optimal performance."* Replica lag is not
   published — **could not verify**. (VERIFIED*, Deltek Cloud Admin Guide.)
3. **Microsoft's DirectQuery guidance says good performance usually requires optimising the source database —
   which on a read-only replica you cannot do.** VERIFIED,
   [DirectQuery model guidance](https://learn.microsoft.com/en-us/power-bi/guidance/directquery-model-guidance)
   (ms.date 2024-12-30, updated 2026-08-05): *"best optimization results are often achieved by applying
   optimizations to the source database"* — indexes, materialised computed columns, indexed views, a
   materialised date table. None of that is available on a Deltek-hosted replica.

Microsoft also frames DirectQuery as a team sport, not a one-person job: *"We often see that a successful
DirectQuery model deployment is the result of a team of IT professionals working closely together"* — model
developers **plus source DBAs**, sometimes data architects and ETL developers. And it carries a hard
**1,000,000-row limit** on rows returned, with TopN filters returning *"all categories from the underlying
source"* first and `Median` not folding at all. (All VERIFIED.)

**Even if you were on Flex Cloud, DirectQuery over a generic-ODBC-through-a-gateway connection to an
un-indexable replica is not a design Microsoft's own guidance would endorse.** Import is the only sane path,
and Import means snapshots.

### 4.10 Row-level security — the constraint that decides who can build reports

All VERIFIED,
[Row-level security in Fabric/Power BI](https://learn.microsoft.com/en-us/fabric/security/service-admin-row-level-security)
(ms.date 2026-05-13, updated 2026-07-08):

- **The killer for a 40-person firm:** *"RLS only restricts data access for users with **Viewer**
  permissions. It doesn't apply to workspace **Admin**, **Member**, or **Contributor** roles."* And:
  *"If you want RLS to apply to people in a workspace, you can only assign them the Viewer role."*
  At KOR's size the people who would build reports are the people who should not see every salary.
  **You cannot be both an author and RLS-restricted.**
- Dynamic RLS needs a **maintained user-mapping table** in the model, filtered by `USERPRINCIPALNAME()` —
  for a project firm that is an employee→project→row bridge kept in sync with Deltek's `EMMain`/`PR` as
  staff move between projects (INFERRED).
- **RLS is row-only:** *"if a user has access to a particular row of data, they can see all the columns of
  data for that row."* You cannot show a PM a project total while hiding the line items.
- **Import mode discards source security:** *"If you're importing data into your Power BI dataset, the
  security roles in your data source aren't used."* Deltek's own project security does not carry over — it
  must be rebuilt from scratch.
- Roles are **additive** (multi-role users see the union). **Microsoft 365 groups aren't supported** for RLS
  membership. Service principals can't be added. `USERELATIONSHIP()` *"might cause unexpected errors"* with RLS.
- The failure mode looks like a bug, not a permission: a user with no matching role *"typically see[s] no
  data"*, and a referenced RLS-filtered field shows *"the same message… as for a deleted or non-existing
  field. To these users, it looks like the report is broken."*

### 4.11 Source control for Power BI content is still preview

VERIFIED, [Git integration overview](https://learn.microsoft.com/en-us/fabric/cicd/git-integration/intro-to-git-integration)
(ms.date 2026-07-21, updated 2026-07-31): of 44 supported item types, **19 are marked *(preview)*** — and
**all five Power BI item types are among them**: Report, Semantic model, Paginated report, Metrics Set,
Org app. Preview terms are explicit: *"Aren't meant for production use… Are not subject to SLAs"*
(VERIFIED, [Fabric preview terms](https://learn.microsoft.com/en-us/fabric/get-started/preview), updated 2026-01-13).
Unsupported items fail **silently**: *"They appear in the source control panel but you can't commit or
update them."*

**INFERRED, and worth stating in the room:** KOR's entire suite is in Git with a test gate. On the Microsoft
path, the artefact that carries all the Deltek business logic — the semantic model — has source control that
is still preview, unSLA'd, in August 2026.

---

## 5. Microsoft 365 Copilot

### 5.1 Price and qualification

VERIFIED, fetched 2026-08-20:

| SKU | Price | Source |
|---|---|---|
| **Microsoft 365 Copilot** (enterprise) | **$30.00/user/month paid yearly**, or $31.50/month on annual commitment | [microsoft.com/…/copilot-for-microsoft-365](https://www.microsoft.com/en-us/microsoft-365/enterprise/copilot-for-microsoft-365) |
| **Microsoft 365 Copilot Business** | **$18.00/user/month paid yearly** (promotional, reduced from $21.00, promo through **2026-09-30**); $25.20/month monthly | [microsoft.com/…/copilot/business](https://www.microsoft.com/en-us/microsoft-365/copilot/business) |

**Qualifying base plans**, VERIFIED verbatim from
[microsoft-365-copilot-licensing](https://learn.microsoft.com/en-us/microsoft-365/copilot/microsoft-365-copilot-licensing)
(ms.date 2026-05-19, updated 2026-08-18):

- **Microsoft Copilot Business** qualifies on: Microsoft 365 Business Basic, Business Standard, Business Premium, Apps for Business.
- **Microsoft Copilot** (the $30 SKU) qualifies on, among others: Microsoft 365 E3/E5/E7/F1/F3, Business Basic/Standard/Premium, **Office 365 E1, Office 365 E3, Office 365 E5**, Office 365 F3, Exchange Plans 1/2, SharePoint Plans 1/2.

**Relevance to KOR:** KOR's tenant runs **Office 365 E3 (19 seats), Office 365 E1 (28), and Microsoft 365
Business Standard** (VERIFIED, KOR M365 Licence Report 2026-08-05). All three qualify — the E1/E3 users at
$30, the Business Standard users potentially at the $18 Business rate. **This is a genuine point for the
Microsoft case: there is no base-licence upgrade needed.**

### 5.2 The mailbox limitation — the decisive fact for KOR's email product

VERIFIED verbatim,
[microsoft-365-copilot-requirements](https://learn.microsoft.com/en-us/microsoft-365/copilot/microsoft-365-copilot-requirements),
**ms.date 2026-08-20 / updated 2026-08-20 — i.e. today**:

> *"**Important** — Microsoft Copilot is only supported on primary mailboxes that are hosted on Exchange
> Online. **It isn't available on a user's archive mailbox, group mailboxes, or shared and delegate mailboxes
> that they have access to.**"*

And in Prerequisites: *"Microsoft Copilot is only supported on primary mailboxes that are hosted on Exchange
Online."*

**Why this is decisive.** KOR's product files project email so that *any* team member can find *any* project's
correspondence. That is, by construction:

- **shared/group mail** — project or discipline mailboxes → **excluded**;
- **archive mail** — correspondence older than the primary-mailbox retention window → **excluded**;
- **other people's mail** — a PM looking up what the client told the previous PM → **excluded**, because
  Copilot only ever sees what the calling user can already open.

M365 Copilot answers "what did *I* say about this?" It structurally cannot answer "what did *the firm* say
about this?" — which is the question a transmittal dispute or a claim actually turns on.

**Could not verify:** a published cap on how far back Copilot searches within a primary mailbox, or a
per-tenant Graph-connector index item quota with a current price. Searched the M365 Copilot requirements,
licensing, and architecture pages; the WebSearch quota for the session was exhausted before secondary
sources could be checked. State these as open.

### 5.3 Reaching Deltek from M365 Copilot

**INFERRED from VERIFIED constraints.** There are three documented routes and none of them is a live query
against Deltek:

1. **Graph connectors** — index external content into the Microsoft Graph semantic index. This is a
   *document/record index*, refreshed on a schedule, not a query path. Financial aggregates are not what an
   index returns.
2. **Copilot Studio agent with a Power Platform SQL connector** — see §6. This can query a SQL Server, but
   Deltek's hosted endpoint is DataDirect ODBC over 443, not SQL Server.
3. **Fabric data agent surfaced into M365 Copilot** — VERIFIED as supported (concept-data-agent:
   *"Fabric data agents can also integrate with Microsoft 365 Copilot"*). But this inherits §3.3's constraint:
   the Deltek data must already be inside Fabric.

Every route ends at the same place: **someone has to build and maintain a Deltek→Fabric pipeline first.**

---

## 6. Copilot Studio

### 6.1 Pricing model, as of 2026-08-20

VERIFIED from the [Copilot Studio product page](https://www.microsoft.com/en-us/microsoft-copilot/microsoft-copilot-studio)
and [billing-licensing](https://learn.microsoft.com/en-us/microsoft-copilot-studio/billing-licensing)
(ms.date 2026-08-03):

- **Prepaid packs: $200.00 per pack per month for 25,000 Copilot Credits**, tenant-wide, ~20% bulk discount available.
- **Pay-as-you-go** via an Azure subscription, billed monthly on actual consumption, no commitment.
- **Copilot Credits prepurchase plan** — one-year prepaid pool of Copilot Credit Commit Units (CCCUs).
- The currency changed from *messages* to *Copilot Credits* on **2025-09-01**; quantities and PAYG rate unchanged.
- **INFERRED:** $200 / 25,000 = **$0.008 per Copilot Credit**.

### 6.2 Consumption rates — the table that decides the economics

VERIFIED verbatim,
[requirements-messages-management](https://learn.microsoft.com/en-us/microsoft-copilot-studio/requirements-messages-management)
(ms.date 2026-08-03):

| Agent feature | Billing rate | Used by M365 Copilot licensed user |
|---|---|---|
| Classic answer | 1 Copilot Credit | **No charge** |
| Generative answer | 2 Copilot Credits | **No charge** |
| Agent action | 5 Copilot Credits | **No charge** |
| Tenant graph grounding for messages | 10 Copilot Credits | **No charge** |
| Agent flow actions (per 100 actions) | 13 Copilot Credits | **No charge** |
| Text/generative AI tools — basic (per 10 responses; 0.1 credit / 1K tokens) | 1 Copilot Credit | No charge |
| Text/generative AI tools — standard (per 10 responses; 1.5 credits / 1K tokens) | 15 Copilot Credits | No charge |
| **Text/generative AI tools — premium** (per 10 responses; **10 credits / 1K tokens**) | 100 Copilot Credits | No charge |
| Content processing tools (per page) | 8 Copilot Credits | No charge |

**Reasoning models are billed twice** (VERIFIED): *"Total cost = feature rate for the operation + text and
generative AI tools (premium) for the reasoning model's token usage,"* at *"10 Copilot credits"* per 1,000 tokens.

**INFERRED — the headline number.** At $0.008/credit, the premium (reasoning-model) tier costs
**$0.08 per 1,000 tokens = $80 per 1M tokens**. That is ~16× Anthropic's Opus 5 input rate and ~3.2× its
output rate. Standard tier works out to **$12/1M tokens**; basic to **$0.80/1M**.

**The honest counterweight, and it is a strong one.** If every user holds an M365 Copilot licence, the
right-hand column is **"No charge"** for employee-facing agents running under the authenticated user's
identity — subject to *"fair usage limits"* which Microsoft reserves the right to change. For a 40-person
firm that buys 40 × $30/month = **$14,400/yr**, the marginal cost of a Copilot Studio "virtual CFO" for staff
is close to zero. **Concede this. It is the single best economic argument on the Microsoft side.**

### 6.3 Enforcement and governance overhead

VERIFIED, same page:

- *"Copilot Studio enforces purchased capacity monthly, and unused Copilot Credits **don't carry over**."*
- *"Enforcement is triggered when a tenant reaches **125% of their prepaid capacity**."* At that point
  *"**Custom agents are disabled.**"* End users then see *"There is a billing issue."* or *"This agent is
  currently unavailable. It has reached its usage limit."*
- Agent-flow enforcement blocks new flow runs when prepaid capacity is exhausted.

Add to that: environment strategy, DLP policies, billing policies linking environments to Azure subscriptions,
credit allocation per environment, the Copilot Studio authors security group, and per-agent monthly
consumption caps in the Power Platform admin center. **INFERRED:** this is a real administrative function,
not a checkbox — realistically a recurring few hours a month plus a named owner.

**Premium connectors.** The SQL Server connector is a premium Power Platform connector requiring a premium
licence. KOR already holds 13 × Power Automate Premium (VERIFIED, KOR M365 Licence Report 2026-08-05) — but
extending to 40 users would need more seats. **Could not verify** the current Power Automate Premium list
price per user/month; the session's WebSearch quota was exhausted. Flag as an unpriced line.

### 6.4 Can it be a "virtual CFO" over a SQL/Deltek source? Four vendor-documented constraints say no

This was the sharpest open question in the brief, and the answer turns out to be Microsoft's own
documentation rather than community grumbling. Four constraints stack:

**1. The SQL Server connector cannot express the query shape.** VERIFIED,
[SQL Server connector reference](https://learn.microsoft.com/en-us/connectors/sql/) (updated 2026-07-11).
Aggregation is limited to OData `$apply` with exactly five functions — **`average, max, min, sum,
countdistinct`** — and there is **no GROUP BY and no multi-table join**. A WIP figure, a lifetime-profitability
number, or utilisation by office is a multi-table join with GROUP BY. **That query shape is not expressible
through the connector.**

**2. The same connector documents its own non-determinism.** VERIFIED, verbatim:

> *"Usage of the `Order By` parameter is recommended in order to get deterministic results in action output…
> **Non-deterministic results might cause issues, such as duplicating records in the action output when
> pagination is enabled.**"*

Hard limits alongside it: **110-second timeout** on queries and stored procedures; **2 MB request / 8 MB
response** on-premises; throttling at **100 CRUD calls per 10 seconds and 500 native calls per 10 seconds
per connection**; an OData filter node limit of 100.

**3. The deterministic-maths escape hatch is preview and cannot reach SQL.** VERIFIED,
[code interpreter for structured data](https://learn.microsoft.com/en-us/microsoft-copilot-studio/knowledge-code-interpreter-structured-data)
(2026-08-04). It covers **user-uploaded CSV/XLSX and SharePoint document libraries only — 16 MB per file, max
10 files — not SQL Server and not Dataverse.** Its own stated value proposition is an admission about the
base product:

> *"By using **deterministic, reproducible computation**, you can unlock **trustworthy** analysis inside agents
> **instead of relying on large language model's inherent math and inference capabilities** to answer
> analytical questions."*

**4. Throughput is a pre-launch workstream, and the ask can be refused.** VERIFIED,
[plan agent throughput and rate limits](https://learn.microsoft.com/en-us/microsoft-copilot-studio/guidance/plan-agent-throughput-rate-limits)
(2026-06-11): estimate peak requests per minute/hour, load test, and file a support ticket *before* launch —
*"Open the ticket early… Don't wait for the first production failure"* — and *"**A throughput increase isn't
guaranteed.**"*

**INFERRED, and this is the conclusion:** any architecture that asks the model to *produce* the financial
number is unsupported by the vendor. The only defensible shape is a **stored procedure or agent flow that
computes the figure, with the agent narrating it** — at which point Copilot Studio is a chat veneer over
software someone still has to write, test and own. That software is what KOR already built. Copilot Studio
does not remove the domain layer; it relocates it and adds $200/month packs and a 125%-shutdown rule.

Practitioner corroboration, **REPORTED and thin — offer as colour, not evidence**: *"CoPilot Studio sucks at
determinism. You really need to create higher level tools in Power Automate and call them from Studio"*
(single detailed HN practitioner account, 2025-12-03). Matthew Devaney (MVP, 2026-07-05) on the
SharePoint-list equivalent: agents can *"search and filter items, but not to perform counts or sums; for the
latter, an agent flow is required."*

### 6.5 Two honest notes on the effort and cost evidence

**The build-effort question is unanswerable from the public record, and that absence is itself a finding.**
No credible practitioner or consultancy person-day, person-month or TCO figure for a production Copilot
Studio agent over structured data could be found. Searched: HN via the Algolia API, Google/Bing News RSS,
MVP blogs, Microsoft Learn guidance. Reddit and StackExchange were unreachable (403/429) for this session.
**A 40-person firm should price this as unbounded, not as a weekend.**

**Do not conflate two different billing stories.** The 2026 "Copilot billing shock" trade press
(Visual Studio Magazine, 2026-06-03 and 2026-08-06; *"bills jumped 25x overnight"*) is about **GitHub
Copilot, not Copilot Studio**. **Copilot Studio-specific credit-burn incidents: could not verify.** Using
those headlines against Copilot Studio would be dishonest and is the kind of error a technical lead will
catch.

**On agent sprawl** — REPORTED: SD Times (2026-07-20), citing OutSystems research, reports 94% of respondents
raising agent-sprawl concerns and frames the structural problem as *"the marginal cost of creating one more
agent is low, while the operational cost of governing it is high."* The stronger signal is a product one:
Microsoft GA'd **Agent 365** to govern agent estates and bundled it into a new **Microsoft 365 E7** suite tier
(REPORTED, Redmond 2026-04-30 — headline and date from RSS; article body returned 403). **INFERRED:** a vendor
shipping a governance product and a suite tier around agent sprawl is evidence the problem is real, and it is
a cost line a 40-person firm has not yet been quoted.

---

## 7. SharePoint Premium (formerly Syntex)

**Licensing model changed and this matters.** VERIFIED,
[syntex-licensing](https://learn.microsoft.com/en-us/microsoft-365/documentprocessing/syntex-licensing)
(ms.date 2025-08-11, updated 2026-04-23): *"Per-user licenses for these services are **no longer available for
purchase**… Once expired, you must switch to pay-as-you-go to continue using services."* Billing runs through
an Azure subscription and **does not** count toward a Microsoft Azure Consumption Commitment.

**Rates (VERIFIED, [syntex-pay-as-you-go-services](https://learn.microsoft.com/en-us/microsoft-365/documentprocessing/syntex-pay-as-you-go-services), ms.date 2025-08-01, updated 2026-06-01, USD):**

| Service | Rate |
|---|---|
| Autofill columns | $0.005 / transaction |
| **eSignature** | **$2.00 / request** (up to 10 recipients) |
| **Content assembly** | **$0.15 / document generated** |
| Taxonomy tagging | $0.05 / document |
| Prebuilt document processing | $0.01 / page |
| Structured & freeform document processing | $0.05 / page |
| Unstructured document processing | $0.005 / page |
| OCR | $0.001 / transaction |
| Document translation | $15.00 / 1M characters |
| Microsoft 365 Archive | $0.05 / GB / month |
| Microsoft 365 Backup | $0.15 / GB / month |
| SharePoint storage over quota | $0.20 / GB / month |

Also VERIFIED: *"In October 2025, Microsoft announced a progressive end of AI Builder credits."* Through June
2026 a limited free monthly allocation applied.

**Relevance to KOR's transmittals product — INFERRED:** these are genuinely useful primitives for *ingesting*
documents (classify a drawing set, extract metadata, auto-tag a library). They are **not** an outbound
transmittal system. Nothing on this list issues a transmittal, tracks who downloaded which revision of which
sheet, or produces the record a claim needs.

**On download tracking specifically — could not verify a clean answer.** SharePoint/Purview audit logs record
`FileDownloaded` events, but audit retention differs by licence tier and the retention/licensing specifics
could not be checked before the session's WebSearch quota was exhausted. What can be said with confidence:
an audit log is a **compliance artefact queried by an administrator**, not a **per-recipient delivery receipt
surfaced to the project manager who sent the transmittal**. Those are different products. KOR's
self-hosted download-tracking redirector produces the second; Purview produces the first.

---

## 8. Azure AI Foundry, and MCP in the Microsoft stack

### 8.1 Microsoft has adopted MCP — concede this immediately and precisely

**VERIFIED**, [data-agent-mcp-server](https://learn.microsoft.com/en-us/fabric/data-science/data-agent-mcp-server)
(ms.date 2026-06-30). A published Fabric data agent exposes an MCP endpoint at:

```
https://api.fabric.microsoft.com/v1/mcp/workspaces/{WorkspaceId}/dataagents/{DataAgentId}/agent
```

**This feature is in preview** (stated explicitly on the page). Three details matter, all VERIFIED:

1. **It exposes exactly one tool.** *"A published Fabric data agent exposes a **single MCP tool**. That tool
   represents the data agent itself, so an MCP client sends a question to the tool and gets back an answer."*
   Compare: KOR's MCP server exposes **24 typed analytical tools** (verified by file count in
   `Kor.Operations.Mcp/Tools/`), each with typed inputs, filters, and dense record outputs. One opaque
   "ask the agent" tool and twenty-four typed functions are different architectures. The typed catalogue is
   what lets an LLM compose a real answer — join utilisation to backlog to AR — rather than round-trip a
   natural-language question and hope.
2. **No dynamic client registration** — *"Your client can't register itself and obtain credentials
   automatically through the protocol."* You bring your own Fabric bearer token.
3. **A data-egress warning in bold:** *"When you consume a Fabric data agent as an MCP server, responses
   returned by the data agent might be sent outside of Fabric's compliance boundary or geographic region,
   and processed or stored according to the terms and data handling policies of the MCP client that you use."*

**The honest read:** Microsoft adopting MCP does not commoditise KOR's work — it *validates the architecture
and then implements one narrow slice of it*. The differentiator was never "we speak MCP." It is the twenty-four
tools and the Deltek semantics inside them.

### 8.2 MCP is now GA across most of the Microsoft stack — concede the whole board

This is broader than the Fabric endpoint above, and it should be conceded completely rather than minimised.

| Surface | MCP status | Date | Tag |
|---|---|---|---|
| **Copilot Studio** — MCP as tools/knowledge | **GA** (streamable HTTP; SSE transport deprecated). Requires generative orchestration enabled | 2025-05-29 | VERIFIED |
| **Microsoft Foundry Agent Service** — MCP tool + Toolbox | **GA** (service GA 2026-03-16; MCP doc ms.date 2026-08-05) | 2026 | VERIFIED |
| **Azure API Management** — expose a REST API as an MCP server, or front an existing one | **GA**, no preview label. Tools only — **no MCP resources or prompts**; not supported in workspaces | doc ms.date 2025-11-13, updated 2026-07-01 | VERIFIED |
| **Azure API Center** — private enterprise MCP registry | Documented | 2026 | VERIFIED |
| **Visual Studio** — MCP client | **GA** | 2025-08-19 | VERIFIED |
| **VS Code / GitHub Copilot** — MCP client | Shipped; tools, resources, prompts and MCP Apps | doc 2026-08-19 | VERIFIED |
| **M365 Copilot declarative agents** — MCP | **GA**; needs a remote HTTPS endpoint + SSO/OAuth 2.0 | 2025-12-15 | VERIFIED |
| **Windows** — On-device Agent Registry (`odr.exe`), MCP containment sandbox, Intune control | **Prerelease**, explicit "might change substantially" banner | doc updated 2026-06-04 | VERIFIED |
| **Fabric data agent** — as MCP server | **Preview**; single tool | ms.date 2026-06-30 | VERIFIED |

**Governance:** *"Microsoft and GitHub have joined the MCP Steering Committee to help advance secure,
at-scale adoption of the open protocol"* — Microsoft Build blog, 2025-05-19 (VERIFIED). MCP itself is now
*"Model Context Protocol a Series of LF Projects, LLC"* under the Linux Foundation, Apache 2.0 (VERIFIED,
modelcontextprotocol.io governance), donated December 2025 to the **Agentic AI Foundation** co-founded by
Anthropic, Block and OpenAI (REPORTED — primary Linux Foundation press release could not be reached).
Note the nuance: MCP governance is **individual, not corporate** — *"there are no seats reserved for specific
companies"* (VERIFIED). Whether Microsoft holds a named AAIF membership today: **could not verify**.

**The honest conclusion, and it cuts both ways.** MCP being everywhere is *not* a threat to KOR — it is the
opposite. Because Foundry, Copilot Studio, APIM, VS Code and M365 Copilot all speak the same protocol,
**the MCP server KOR already runs is consumable from every one of those surfaces without modification.**
The integration work does not become Azure-specific. If MVE wants KOR's virtual CFO inside Teams, the wiring
is a URL and a token, not a rewrite. **Say this in the room — it converts the objection into a feature.**

### 8.3 Claude is a first-class GA model on Microsoft Foundry

This is the single most important correction to any "staying Microsoft means giving up Claude" framing —
in either direction.

- **VERIFIED**: Anthropic's Claude models launched in Microsoft Foundry **2025-11-18** and reached **GA in
  June 2026** ([Foundry blog, 2026-08-17](https://devblogs.microsoft.com/foundry/five-new-claude-capabilities-now-available-in-foundry/)).
- Two hosting modes: **Hosted on Azure** (end-to-end on Azure; GA models include `claude-opus-5`,
  `claude-opus-4-8`, `claude-sonnet-5`, `claude-haiku-4-5`) and **Hosted on Anthropic infrastructure**
  (adds older Opus/Sonnet lines; `claude-fable-5` preview).
- The endpoint is the **Anthropic Messages API** at
  `https://<resource>.services.ai.azure.com/anthropic/v1/messages`, callable from the standard `anthropic`
  SDK. Prompt caching, context editing, tool search, the **MCP connector** and structured outputs are all
  supported. (VERIFIED, [Claude models in Foundry](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models), ms.date 2026-08-17.)
- Microsoft's own claim: *"Azure is now the only cloud providing access to both Claude and GPT frontier
  models to customers on one platform."* (VERIFIED.)
- **Billing is different and worth knowing:** Claude bills through **Azure Marketplace** on a single
  **Claude Consumption Unit (CCU)** meter — MACC-eligible, but **Azure Cost Management shows one CCU line,
  not per-model cost**; per-model detail lives only in the Foundry portal (VERIFIED,
  [CCU billing](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-billing), ms.date 2026-08-14).
  There are **no Claude/Anthropic meters in the Azure retail price list at all** — VERIFIED negative,
  queried 2026-08-20.
- **Two capability caveats (VERIFIED):** Foundry provides **no built-in content filtering for Claude models**
  at deployment time — you configure AI Content Safety yourself. And some Anthropic features (Files API,
  web fetch) require the Anthropic-hosted variant.

**Both consequences, stated plainly:**
- MVE cannot argue "staying Microsoft means not using Claude." It does not.
- KOR cannot argue "Microsoft can't run our model." It can. **KOR's differentiator is not the model, and it
  was never the model.** It is the twenty-four tools and the Deltek semantics inside them.

### 8.4 Microsoft Foundry — what is GA, what is not, and what it costs

**Naming, first:** "Azure AI Foundry" is now **Microsoft Foundry**; docs moved to `learn.microsoft.com/azure/foundry/*`
and the previous hub/Azure-OpenAI experience is "Foundry (classic)". Retail meters bill under
`Foundry Models` and `Foundry Tools`. (VERIFIED, 2026-08-20.)

**GA / preview matrix** (VERIFIED, [Foundry general availability overview](https://learn.microsoft.com/en-us/azure/foundry/concepts/general-availability), ms.date 2026-07-21):

| Capability | Status |
|---|---|
| Portal, model catalog, playgrounds, **Agents (core)**, Toolboxes, publish to M365 Copilot & Teams, Models, Fine-tuning, Red teaming, Quota, Admin, **Evaluations** | **GA** |
| Tracing | GA for prompt + hosted agents; preview for workflow/external |
| Knowledge (Foundry IQ) | API GA, portal preview |
| Guardrails — Models | GA. **Guardrails — Agents: preview** |
| **Memory, Routines, Monitoring, Operate (Compliance)** | **Preview** |
| **Workflows** | Preview — **being retired 2026-12-01**; Microsoft says use Microsoft Agent Framework instead |

**Foundry Agent Service went GA 2026-03-16**, built on the OpenAI Responses API, with prompt agents
(config-only) and hosted agents (your container — supported frameworks explicitly include LangGraph, the
OpenAI Agents SDK and the **Anthropic Agent SDK**). Custom API tool calling via OpenAPI specs and Azure
Functions is supported. Hard limit: **100-second non-streaming timeout** unless background mode is used.
(All VERIFIED.)

**Agent hosting rates** (VERIFIED, Azure Retail Prices API, `Foundry Tools`, East US, USD, 2026-08-20):
hosted vCPU **$0.0994/vCPU-hour**; hosted memory **$0.0118/GiB-hour**; skills execution container
**$0.033/hour**; Assistants file-search vector storage **$0.10/GB/day**.

**Model token rates** (VERIFIED, same API, Global Standard PAYG, East US, USD, per 1M tokens, 2026-08-20) —
a representative slice: GPT-5 $1.25 in / $10.00 out; GPT-5 mini $0.25 / $2.00; GPT-5.4 $2.50 / $15.00;
GPT-5.4 mini $0.75 / $4.50; GPT-4.1 $2.00 / $8.00; o4-mini $1.10 / $4.40;
`text-embedding-3-small` $0.02. Batch is ~50% off; Data Zone is +10% over Global.

**The cost that actually dominates is retrieval, not the model.** VERIFIED unit rates, East US, 2026-08-20:
Azure AI Search Basic $0.101/unit-hour, **S1 $0.336/unit-hour**, S2 $1.344; semantic ranker **$16.12/day**.
AI Search bills per **search unit = replicas × partitions**, and Microsoft's own baseline architecture
requires **at least three replicas** for zone redundancy.

**INFERRED, 40-person firm, moderate use** (40 users × 20 turns/day × 22 days = 17,600 calls; 8K in / 1K out):

| Line | ≈ /month |
|---|---|
| Tokens, GPT-5.4 | $616 |
| Tokens, GPT-5 mini | $70 |
| App Service Linux P0v3 | $57 |
| Azure SQL Standard S1 | $29 |
| **Azure AI Search S1 × 3 replicas** | **$736** |
| **AI Search semantic ranker** | **$490** |
| **Standing infrastructure before a single token** | **~$1,312** |

That is **~$15,700/yr of Azure infrastructure** before any model spend — and it exists in any RAG design;
Azure simply meters it.

**What the production pattern actually requires.** Microsoft's own
[Baseline Microsoft Foundry Chat reference architecture](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/architecture/baseline-microsoft-foundry-chat)
(ms.date 2026-06-17, updated 2026-08-18) lists, to stand up "chat over your enterprise data" securely:
App Service across three zones, **Application Gateway + WAF + DDoS Protection**, Key Vault, Storage,
**Azure AI Search (≥3 replicas)**, **Cosmos DB** for agent state, **Azure Firewall** for egress,
**Private Link private endpoints on every PaaS service**, private DNS zones, **Azure Bastion + jump box +
build-agent subnets**, App Insights/Monitor and Entra ID. (VERIFIED.)

And Microsoft's own caveat, verbatim: *"Foundry doesn't support advanced load balancing or failover
mechanisms, like round-robin routing or circuit breaking, for model deployments. If you require granular
redundancy and failover control within a region, host your model access logic outside the managed service…
But it also increases operational complexity and shifts responsibility for the reliability of that component
to your team."* (VERIFIED.)

### 8.5 Honest read on the build-your-own path

**Genuinely different, and not nothing (INFERRED from the VERIFIED facts above):**
1. Claude and GPT frontier models behind **one Entra identity, one invoice, one MACC drawdown**.
2. **Network isolation you would otherwise build** — BYO VNet, no public egress, private endpoints extending
   to MCP servers.
3. **Agent identity** — each agent gets a dedicated Entra identity with OAuth on-behalf-of passthrough.
4. **Toolbox** — a governed MCP endpoint that is portable, consumable by LangGraph or your own code.

**The same engineering with Azure billing attached (INFERRED):**
1. Agent Service *is* the OpenAI Responses API with a control plane. Hosted agents are your container, your
   code, your framework — Foundry runs it at $0.0994/vCPU-hour. You still write the orchestration, the tools,
   the retrieval and the evals.
2. **Preview risk is load-bearing.** Memory, Monitoring, Agent Guardrails, Routines and Operate/Compliance
   are all preview, and **Workflows — shipped as the orchestration story — is being retired 2026-12-01.**
   Building on the "managed" parts still means building on moving ground.
3. It does not remove **one hour** of the Deltek semantic work in §10.2.

**Bottom line:** for a 40-person firm, Azure's build path is **a compliance and identity wrapper around the
same agent you would write anyway**, at ~$1,300/month of standing infrastructure before tokens. The
defensible reasons to choose it are Entra, VNet and MACC — not developer velocity.

---

## 8A. The industry evidence — what it shows, and what it does not

The brief asked for *real evidence of AEC firms struggling with this, not just assertion*. Here is what the
evidence actually supports, and — more importantly — what it does not.

### 8A.1 What the evidence supports

The **2026 Deltek Clarity A&E Industry Study** (47th annual, ~900 A&E firms across the US and Canada;
Deltek's own landing page VERIFIED at https://info.deltek.com/Clarity-AE, fetched 2026-08-20). The figures
below are **REPORTED** — taken from Full Sail Partners' study recaps, because the underlying study PDF is
form-gated and could not be opened:

| Finding | Figure |
|---|---|
| Firms relying *"completely or moderately on manual processes"* for administrative/management functions | **80%** |
| Firms with significant manual reliance in **accounting/finance** | **75%** |
| High visibility into cost variance / project KPIs | 56% / 55% |
| High visibility into **schedule variance** / client satisfaction | **31% / 24%** |
| *"Establishing PM accountability metrics"* — debuted as the **#1 planned initiative** | 33% of firms |
| Top technology challenge: *"lack of time to invest in learning"* | **51%**, up from 43% |
| Second: cost | 48% |
| Self-classify as digitally *"mature or advanced"* | 33% |

Financial context that would squeeze any discretionary BI budget (REPORTED, same source): operating profit on
net revenue **fell to 16.7%, down 4.7 points**; backlog **9.0 → 6.3 months**; utilisation **58.9%**; overhead
rate at a ten-year high of **161.3%**.

**What this legitimately supports:** A&E firms are **manual-process-heavy and metric-poor**, they know it
(PM accountability metrics is the #1 initiative), and the binding constraint on fixing it is **time to learn
(51%)** — which is precisely the constraint a Power BI/Fabric build imposes.

### 8A.2 What the evidence does NOT support — say this plainly rather than dressing it up

- **There is no credible primary survey of AEC BI dissatisfaction.** No figure for "firms unhappy with their
  BI", "failed Power BI projects", or "reports nobody trusts" could be found. **The "Power BI report nobody
  trusts" framing is not evidenced** — the nearest thing is vendor prose (Full Sail: *"Most firms already
  have reporting in place. Dashboards exist. Data flows. Reports get built"* but teams still hit *"the usual
  'export to Excel and lose control' cycle"* — REPORTED, and unquantified marketing).
- **Not verified:** Zweig Group, ACEC, PSMJ or AIA technology surveys; the perennial Gartner "80% of insights
  won't deliver outcomes" statistic, which was not chased to a primary source. **Do not present these as
  support.**

**The honest posture for the room:** argue the mechanism, not a survey. The mechanism is §10.2 — eight
Deltek quirks, seven of which fail silently — plus Microsoft's own documented nondeterminism. That is
stronger than a statistic anyway, because it is checkable on the spot against KOR's own database.

### 8A.3 The market's revealed preference

Three observations, each modest on its own, consistent together:

1. **Deltek does not publish a usable data dictionary.** The Deltek "Custom Reports and Microsoft SQL" guide
   at dsm.deltek.com is an **image-only PDF** — DCTDecode streams, no text layer, not machine-readable
   (VERIFIED, fetched 2026-08-20). No public ERD or table count for Vantagepoint was reachable.
2. **The established Deltek integration partner's productised BI answer is not Power BI.** Full Sail Partners'
   Blackbox Connector catalogue lists Constant Contact, Mailchimp, OpenAsset, ADP Workforce Now, SAP Concur,
   Client Feedback Tool, and **Informer** for BI — **there is no Power BI connector in it** (VERIFIED,
   blackboxconnector.com/deltek-integrations, fetched 2026-08-20). Full Sail sells "Power BI Enablement
   Services" separately — i.e. as consulting, not as a product.
3. **Stambaugh Ness — a Deltek Premier Partner and a Microsoft partner — sells "Connecting Deltek Cloud to
   Microsoft Power BI" as a lead-gated guide**, with a separate one for on-premises (VERIFIED, both confirmed
   gated, 2026-08-20). And a Microsoft Fabric community thread on connecting Power BI to the Vantagepoint API
   concludes the workable routes are an **Azure Function/Logic App intermediary** for the OAuth handshake, or
   a **custom Power BI connector** implementing OAuth and token refresh (REPORTED — 403 on direct fetch,
   content via search extract). *That neither option is "pick the connector from the list" is the finding.*

**INFERRED, and labelled as inference:** paid enablement services, a competing BI product, and no certified
connector are what a hard integration looks like from the outside. This is circumstantial evidence, not a
measurement, and should be offered as such.

---

## 9. What it would actually take

To reach parity with what KOR demos, using Microsoft tooling, for a 40-person firm.

### 9.1 Named products and annual licence cost

| Line | Product | Cost/yr (USD, list) | Tag |
|---|---|---|---|
| Capacity | Fabric **F2**, 1-year reserved (2 CU × $938) | **$1,876** | VERIFIED rate, INFERRED total |
| Capacity (realistic) | Fabric **F4–F8** once Copilot use is real | $3,752 – $7,504 | INFERRED |
| Viewers | Power BI **Pro** × 40 @ $14/mo | **$6,720** | VERIFIED |
| Conversational layer | **M365 Copilot** × 40 @ $30/mo | **$14,400** | VERIFIED |
| — cheaper variant | M365 Copilot **Business** × 40 @ $18/mo (promo to 2026-09-30) | $8,640 | VERIFIED |
| Custom agent | **Copilot Studio** 1 credit pack @ $200/mo — *largely zero-rated if all users hold M365 Copilot* | $0 – $2,400 | VERIFIED |
| Document services | **SharePoint Premium** PAYG (transmittal-adjacent only) | usage-based, ~$0.15/doc | VERIFIED |
| Premium connector seats | Power Automate Premium beyond the 13 held | **could not verify** | — |
| Copilot token consumption | Metered against capacity CUs, not a separate invoice | included above | VERIFIED |
| **Total, minimum viable** | F2 + 40 Pro | **~$8,600** | INFERRED |
| **Total, full parity attempt** | F8 + 40 Pro + 40 M365 Copilot + Copilot Studio | **~$31,000** | INFERRED |

### 9.2 Engineering effort

**INFERRED throughout — labelled reasoning, scaled from the documented work Microsoft prescribes and from
KOR's own first-hand record of what the Deltek model actually contains. No Microsoft-published effort
estimate exists (verified absence, §4.6).**

| Workstream | What it involves | Person-months |
|---|---|---|
| Deltek → Fabric ingestion | On-prem data gateway; ODBC Import-mode dataflows against a DataDirect HDP endpoint; incremental refresh; failure handling; the fact that no native Deltek connector exists | **2 – 4** |
| Semantic model over Deltek | Star schema; the eight landmines in §10.2; FX bucketing by `PR.Org`; three GL companies; four sub-ledgers; measure library | **3 – 6** |
| Prep-data-for-AI curation | AI data schema; up to 250 verified answers × 5–7 trigger phrases; 10,000 chars of AI instructions; 200-char descriptions on every field; linguistic model | **2 – 4** |
| RLS / OLS design and test | Who sees which project, which PM, which salary — plus re-testing after the verified-answer RLS caveat | **0.5 – 1** |
| Copilot Studio agent + governance | Environments, DLP, billing policy, credit caps, publishing to Teams | **1 – 2** |
| **Subtotal to reach the financial-reporting slice only** | | **8.5 – 17** |
| Email filing / transmittals equivalent | **Not achievable off the shelf** — see §10.1 | n/a |
| BD ingestion pipeline equivalent | **Not achievable off the shelf** — see §10.3 | n/a |

**Maintenance owner.** Microsoft's model requires a named human who owns the semantic model permanently:
re-marking it *"Approved for Copilot"* after changes, re-curating verified answers whenever a measure or
dimension changes (a swapped field silently breaks the match), re-testing after every Deltek release,
watching the Fabric Capacity Metrics app for Copilot consumption, and re-sizing the capacity before the
125%/throttle cliff. **INFERRED: 0.2–0.4 FTE, permanently.** At a Canadian/US analyst loaded cost this is
plausibly **$25,000–$60,000/yr** of internal time — **could not verify** a 2026 salary benchmark, so treat
the dollar figure as an order-of-magnitude estimate, not a sourced number.

---

## 10. What still wouldn't work

Five things. Each is blocked by a documented constraint, not by a preference.

### 10.1 Filed project email and transmittals

**Blocked by:** *"It isn't available on a user's archive mailbox, group mailboxes, or shared and delegate
mailboxes that they have access to."* (VERIFIED, 2026-08-20.)

Copilot answers from the calling user's primary mailbox. A firm-wide project correspondence record — the
thing that answers "what did we tell the architect about the transfer slab in March" two PMs later — is
outside its reach by design. And nothing in SharePoint Premium's catalogue (§7) is an outbound transmittal
with per-recipient download tracking. **This capability has no Microsoft equivalent to buy.**

### 10.2 Deltek semantics — the part that cannot be bought, only learned

This is the strongest section of the whole argument because it is specific, checkable, and was expensive to
acquire. Every item below is **VERIFIED first-hand** against KOR's live Deltek catalogue and recorded in
KOR's own engineering notes. A Power BI consultant starting Monday knows none of it.

**First, the scale of the mapping problem, measured (VERIFIED, grep over KOR's `*.cs`, 2026-08-20):**

- KOR's production integration touches **29 distinct Deltek tables** just to answer ordinary financial
  questions: `PR`, `PRSummaryMain`, `PRContactAssoc`, `EM`, `EMMain`, `EMCompany`, `Clendor`, `CL`, `CA`,
  `AR`, `Activity`, `Contacts`, `LedgerAR`, `LedgerAP`, `LedgerEX`, `LedgerMisc`, `GLSummary`, `GLTable`,
  `GLGroup`, `GLGroupDetail`, `GLGroupHeading`, `GLParentGroup`, `GLParentDetail`, `GLParentHeading`,
  `CFGAcctngCalendarData`, `CFGBanks`, `tkDetail`, `apDetail`, `ProjectCustomTabFields`.
- `Financials/FinancialsService.cs` alone contains **28 JOIN clauses**; `ProjectAnalyticsService.cs` 12;
  `WipFinancialsService.cs` and `GlProfitLossService.cs` 10 each. That is the shape of the semantic model
  someone would have to rebuild in Power Query or in views.
- **Four-part naming is mandatory and `USE` is unavailable** on the hosted ODBC connection — every accessor
  interpolates the catalog: `FROM [{_catalog}].dbo.PR pr LEFT JOIN [{_catalog}].dbo.Clendor cl ON cl.ClientID = pr.ClientID`.
- **KOR's code capability-probes for columns that move between Deltek versions**, querying
  `INFORMATION_SCHEMA` before building SQL (`DeltekKorPursuitDeltekAccessor.cs`,
  `DeltekKorStaffDirectoryAccessor.cs`). A Power BI semantic model has no equivalent defence — and Microsoft
  states the consequence explicitly: *"Data refresh in the Power BI service will fail when the source column
  or table is renamed or removed… because the Power BI service doesn't also include a schema refresh"*
  (VERIFIED, [refresh-data](https://learn.microsoft.com/en-us/power-bi/connect-data/refresh-data),
  updated 2026-08-11). **A Deltek upgrade that renames a column breaks the refresh; KOR's code survives it.**

**And now the eight quirks — seven of which fail silently:**

| # | The quirk | What a naive semantic model does |
|---|---|---|
| 1 | KOR has **Revenue Generation disabled**. `PRSummaryMain.Revenue` is **$0 on every active project**; `BilledFee` is the meaningful column. Legacy projects are the reverse. | `SUM(Revenue)` returns **$0 firm-wide** — a dashboard that is confidently, completely wrong. Correct form: `SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END)`. |
| 2 | Account codes are stored **padded**: `'4001.00'`, not `'4001'`. | `Account IN ('4001','4003')` matches **zero rows** — silently. This bit KOR in production: Net Multiplier showed 0.00 and DSO showed "Data unavailable." Correct form: `LEFT(LTRIM(RTRIM(Account)),4) IN (…)`. |
| 3 | `tkDetail.RegAmt/OvtAmt/BillExt` and `PRSummaryMain.BilledFee` are denominated in the **project's** currency (`pr.Org`), not the employee's. | Cross-org rollups are wrong by the FX rate (1.36). Same employee reads $53.90/hr on a CAD job and $39.51/hr on a USA job. |
| 4 | Three GL companies — **CAD / USA / BCC** — where BCC is a holding entity with no project activity. | Firm totals include a shell company and mix currencies. |
| 5 | Client names live in **`Clendor`**, not a `CL` table. `HireDate` is on **`EMCompany`**, not `EMMain`. REST API field names do **not** match ODBC table names. | Wrong joins, or no join at all. |
| 6 | *"Lots of shit in the GL"* — `GLSummary`/`GLDetail` are unusable for breakdowns; the curated sub-ledgers (`LedgerAR` with `TransType='IN'`, accounts 4001/4003/4210/4220/4240, excluding intercompany 4260) are the only clean path. | A GL-based P&L that no partner will sign. |
| 7 | `OdbcType.Date` **binds to nothing** against the Deltek ODBC driver — the query succeeds and returns zero rows. `OdbcType.DateTime` works. | Silent empty results with no error. |
| 8 | `BilledFee` is only as current as the **monthly billing close** — which was **three months stale** when measured. `PR.ActCompletionDate` is mostly NULL; `MIN/MAX(tkDetail.TransDate)` is the real work-period proxy. `PR.LostTo` is populated on **3 of 79** lost pursuits. | A "current" dashboard reporting a quarter-old picture, with no staleness banner. |

**Seven of these eight fail silently.** They return zero, or a plausible wrong number — never an error.
That is precisely the mechanism behind the "Power BI report nobody trusts": the report renders, the number
looks fine, and it is wrong. Microsoft's Copilot documentation independently warns about exactly this class
of model (*"complex patterns like currency conversion or disconnected tables… might cause unexpected or
incorrect results"*) — and Copilot's answers are **nondeterministic**, so the same question can produce a
different wrong number twice.

**The argument to make:** *this list is the product.* Anyone can buy F2. Nobody can buy the year it took to
find these eight.

### 10.3 The BD ingestion pipeline

KOR's BD Brain ingests **~100+ registered public sources** (BC Bid, CanadaBuys, SAM.gov, 32 Bonfire tenants,
bids&tenders, CivicInfo RSS, municipal permit feeds, four Major-Projects-Inventory providers), through a
structural-relevance gate, SHA-256 content-hash dedup, and a **six-tier canonical organisation resolver**
(alias → merged-survivor redirect → strict normalised name → domain → fuzzy → create) with a physical unique
filtered index guaranteeing one live org per normalised name, plus person identity anchored on
email → LinkedIn → name+org (VERIFIED, KOR BD Platform Technical Overview, 2026-07-11).

**There is no Microsoft product for this.** Fabric Data Factory can fetch and land data — that is the easy
20%. Entity resolution, relevance gating, merge ledgers with survivor redirects, and identity resurrection
rules are domain logic that has to be written wherever it lives. Copilot Studio does not do it; Fabric does
not do it; M365 Copilot does not do it.

### 10.4 A financial number the model computes itself

**Blocked by:** two independent vendor constraints, from opposite directions.

- On the **Power BI path**, Copilot's answer is nondeterministic by Microsoft's own statement, and Microsoft
  explicitly advises testing whether your model produces *"consistently correct and reliable results"* and, if
  not, *"advising users not to use Copilot to consume your semantic model."*
- On the **Copilot Studio path**, the SQL connector cannot express a GROUP BY or a multi-table join, and the
  deterministic-compute feature that would fix it reaches CSV/XLSX and SharePoint libraries only — not SQL
  (§6.4).

**INFERRED:** the only supported way to get a trustworthy financial figure out of Microsoft's agent surfaces
is to compute it in code first and let the agent narrate it. That is exactly the design of KOR's 24 typed
tools — each returns a dense typed record, so the same inputs return the same numbers, and the LLM composes
rather than calculates. **The architecture Microsoft's documentation forces you toward is the one KOR already
shipped.** That is the single strongest technical point available in the room, because it is Microsoft
agreeing with the design.

### 10.5 Desktop and Outlook integration

KOR ships a WPF desktop application (108,254 LOC in `Kor.Operations.App` alone), an Outlook add-in, a Revit
add-in, `kor://` deep links from PDFs and email into the running app, and a 6 a.m. per-owner digest email.
Microsoft's surfaces are the browser, the Office task pane, and Teams. **INFERRED:** a Copilot answer cannot
open a project in a desktop application, populate a fee proposal, drive a Revit palette, or file an email
against a WBS — because that is what an application does, and Copilot is not an application.

---

## 11. Total cost of ownership — 40-person firm, 3 years

All figures USD, list price, US region. Licence figures VERIFIED per §3.1/§4.7/§5.1/§6.1;
totals and effort figures **INFERRED**.

### 11.1 Microsoft path

| | Year 1 | Year 2 | Year 3 | 3-yr |
|---|---|---|---|---|
| Fabric F8 reserved (realistic once Copilot is used) | $7,504 | $7,504 | $7,504 | $22,512 |
| Power BI Pro × 40 @ $14/mo | $6,720 | $6,720 | $6,720 | $20,160 |
| M365 Copilot × 40 @ $30/mo | $14,400 | $14,400 | $14,400 | $43,200 |
| Copilot Studio (largely zero-rated with M365 Copilot) | $0 | $0 | $0 | $0 |
| OneLake storage + SharePoint Premium PAYG | ~$500 | ~$500 | ~$500 | ~$1,500 |
| **Licences subtotal** | **$29,124** | **$29,124** | **$29,124** | **$87,372** |
| Build: 8.5–17 person-months of modelling/curation | — | — | — | — |
| Maintenance owner @ 0.2–0.4 FTE | — | — | — | — |
| **Delivers** | Financial dashboards + a curated, English-only, nondeterministic Q&A envelope over a ≤30-min-stale snapshot. **No email filing. No transmittal tracking. No BD pipeline. No desktop app.** | | | |

**Cheapest defensible variant** (F2 + 40 Pro, no M365 Copilot, no conversational layer): **$8,596/yr /
$25,788 over 3 years** — but this buys reports, not a conversation, since Copilot needs the capacity anyway
and the answers still need the curation.

**Third variant — the Azure build-your-own path** (Microsoft Foundry, per §8.4). This is the only Microsoft
route that could reach KOR's *architecture*, because it lets you write your own typed tools:

| Line | ≈ /yr |
|---|---|
| Standing infrastructure (App Service P0v3, Azure SQL S1, **AI Search S1 × 3 replicas, semantic ranker**) | **~$15,700** |
| Model tokens (GPT-5.4 at moderate use) | ~$7,400 |
| **Subtotal** | **~$23,100** |
| Plus: the 24 typed tools, the Deltek semantic layer, evals and orchestration | *still to be written* |

**INFERRED:** the Azure path costs roughly three times KOR's current API spend and does not remove any of the
engineering. Its genuine value is Entra identity, VNet isolation and MACC drawdown — governance, not capability.

**One further cost line nobody has been quoted.** Microsoft GA'd **Agent 365** for governing agent estates and
bundled it into a new **Microsoft 365 E7** tier (REPORTED, 2026-04-30). Pricing **could not be verified**. If
a firm ends up running several agents across Fabric, Copilot Studio and Foundry, this becomes a real line —
and it is a cost KOR does not have, because KOR runs one MCP server with one audit log.

### 11.2 KOR's actual position

| | Annual | Basis |
|---|---|---|
| Power BI / Fabric | **$0** | Tenant holds `POWER_BI_STANDARD` (free) only — VERIFIED, KOR M365 Licence Report 2026-08-05 |
| M365 Copilot | **$0** | Not purchased |
| Server hosting | **~$0 marginal** | KOR-APP01 (64 GB / 8 vCPU) already exists and runs FileSync, the Opportunities Worker and the MCP server |
| SQL Server | **~$0 today**, with a flagged risk | SQLEXPRESS in use; **SQL 2022 Developer edition is running in production, which is a licensing gap KOR has already identified.** Honest disclosure: this is unpriced exposure, not a saving. |
| LLM API (BD Brain enrichment) | **~$18,250/yr** at the observed $50/day; **~$7,300–9,125/yr** at the audited optimised $20–25/day | VERIFIED, KOR BD Cron Cost Audit 2026-06-10 |
| LLM API (virtual CFO /ask) | small, usage-driven | Not separately measured — **could not verify** |
| **Cash out the door** | **~$7,300 – $18,250** | |
| **Delivers** | Live Deltek queries (not snapshots), 24 typed analytical tools, email filing + search, transmittals with download tracking, the BD pipeline, WPF + Outlook + Revit integration | |

### 11.3 The comparison stated fairly

| | Microsoft path | KOR as-built |
|---|---|---|
| **Cash/yr (full parity attempt)** | ~$29,100 | ~$7,300 – $18,250 |
| **Cash/yr (minimum)** | ~$8,600 | ~$7,300 |
| **Build cost** | 8.5–17 person-months, **ahead of you** | ~364,500 LOC across 92 projects, **behind you** (sunk) |
| **Data freshness** | ≤30 min (Import-mode only) | Live ODBC query |
| **Analytical surface** | 1 opaque MCP tool / a curated verified-answer envelope | 24 typed tools |
| **Email + transmittals** | Not available | Shipped |
| **BD pipeline** | Not available | Shipped, ~100+ sources |
| **Determinism** | *"isn't guaranteed to produce… the same answer with the same prompt, model, and data"* | Typed tools return the same record for the same inputs |
| **Vendor risk** | Low — Microsoft maintains it | Real: Copilot/Fabric roadmap can absorb features |
| **Key-person risk** | Low | **High, and should be conceded without spin** |
| **Model choice** | Fixed, unnamed | Chosen per job (Opus/Sonnet/Haiku), swappable |

**The two risks that cut against KOR must be stated, not hidden:**
1. **Key-person risk is the strongest thing MVE can say,** and no cost table answers it. The answer is
   documentation, tests, and a second pair of hands — not a rebuttal.
2. **Deltek is building this into the base product.** Vantagepoint 2026.1/2026.2 shipped an AI project
   advisor running nightly per-project analyses that surface the three most critical issues, focused on
   misalignment between **Earned, Invoiced and Spent** — which overlaps KOR's COO Card and earned-vs-invoiced
   tool (REPORTED, Deltek release notes and articles; 2026.2 released 2026-03-13, VERIFIED from
   help.deltek.com). Some of what KOR built will become table stakes.

**But the counter is precise and documented.** Deltek's own AI, over Deltek's own database, states:

> *"**No Support for Aggregate Data Queries:** You cannot query Ask Dela for aggregate data, such as
> information about the top projects by revenue."*
> *"Ask Dela can provide information only for **WBS1**… "*
> *"Ask Dela currently supports conversations only about a **single record**."*
> *"it remembers the context of only the **last two questions**."*
> *"US English is currently the only supported language."*
> *"Double-check responses, especially for crucial decisions or data, as occasional inaccuracies may occur."*

(VERIFIED, [help.deltek.com — Use Ask Dela](https://help.deltek.com/Product/Vantagepoint/7.0/Ask_Dela_UseAskDela.html).)

"Top projects by revenue," across all three GL companies, FX-corrected, is question one from any principal.
Neither Deltek's AI nor Microsoft's shipped in a form that answers it against KOR's data. KOR's does.

---

## 12. Sources

All fetched or queried **2026-08-20** unless a different date is given. `ms.date` = Microsoft's content date;
`updated_at` = last publish.

**Microsoft — pricing (VERIFIED)**
1. Azure Retail Prices API — `https://prices.azure.com/api/retail/prices?currencyCode=USD&$filter=serviceName eq 'Microsoft Fabric' and armRegionName eq 'westus2'` — queried 2026-08-20. Capacity $0.18/CU-h; 1-yr reservation $938/CU; 3-yr $2,814/CU; overage $0.54/CU-h; Copilot & AI $0.18/CU-h; OneLake Hot $0.023/GB/mo.
2. Power BI pricing — https://www.microsoft.com/en-us/power-platform/products/power-bi/pricing — Pro $14.00/user/mo paid yearly; PPU $24.00/user/mo paid yearly.
3. M365 Copilot enterprise — https://www.microsoft.com/en-us/microsoft-365/enterprise/copilot-for-microsoft-365 — $30.00/user/mo paid yearly; $31.50/mo annual commitment.
4. M365 Copilot Business — https://www.microsoft.com/en-us/microsoft-365/copilot/business — $18.00/user/mo paid yearly (promo, was $21.00, to 2026-09-30); $25.20/mo monthly.
5. Copilot Studio — https://www.microsoft.com/en-us/microsoft-copilot/microsoft-copilot-studio — $200.00/pack/month for 25,000 Copilot Credits; PAYG available.
6. Azure Fabric pricing page — https://azure.microsoft.com/en-us/pricing/details/microsoft-fabric/ — **renders `$-` placeholders**; not usable as a source. Noted for completeness.

**Microsoft — Fabric (VERIFIED)**
7. Understand Fabric licenses and capacity — https://learn.microsoft.com/en-us/fabric/enterprise/licenses — ms.date 2026-06-15, updated 2026-08-05. F64 free-viewer threshold; P-SKU retirement.
8. Enable and configure Copilot in Fabric — https://learn.microsoft.com/en-us/fabric/fundamentals/copilot-enable-fabric — ms.date 2026-05-22, updated 2026-07-24. F2+ prerequisite; cross-geo tenant setting; whole-tenant warning.
9. Consumption rates and billing for Copilot in Fabric — https://learn.microsoft.com/en-us/fabric/fundamentals/copilot-fabric-consumption — ms.date 2026-05-22. 100/10/400 CU-seconds per 1K tokens; F64 = 13,824 requests/day; *"all operations will shut down."*
10. Release status of AI and Copilot in Fabric — https://learn.microsoft.com/en-us/fabric/fundamentals/copilot-ai-feature-state — ms.date 2026-06-19. GA/preview matrix.
11. Fabric data agent concepts — https://learn.microsoft.com/en-us/fabric/data-science/concept-data-agent — ms.date 2026-05-11. *"a generally available feature"*; five-source cap; NL2SQL/NL2DAX/NL2KQL; no example queries on semantic models.
12. Data agent prerequisites — https://learn.microsoft.com/en-us/fabric/data-science/includes/data-agent-prerequisites.md — ms.date 2026-04-20. F2+/P1+; cross-geo required; supported source list.
13. SQL sources in Fabric data agent — https://learn.microsoft.com/en-us/fabric/data-science/data-agent-sql-sources — ms.date 2026-06-03. Lakehouse/Warehouse/Fabric SQL DB/Mirrored only; Advanced NL2SQL preview.
14. Data agent as MCP server (preview) — https://learn.microsoft.com/en-us/fabric/data-science/data-agent-mcp-server — ms.date 2026-06-30. Single MCP tool; no dynamic client registration; compliance-boundary warning.

**Microsoft — Power BI Copilot (VERIFIED)**
15. Copilot for Power BI overview — https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-introduction — ms.date 2026-03-23, updated 2026-07-23. GA/preview split; F2+/P1+ requirement; 10,000-char prompt; 24-h cache; 24-h capacity lag; data-preparation section.
16. Ask Copilot questions about your data — https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-ask-data-question — ms.date 2026-05-28, updated 2026-07-29. Unsupported question types (*"Why do our sales go down every July?"*); ad-hoc DAX; English only; filters/slicers not applied; may answer from LLM general knowledge.
17. Copilot with semantic models — https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-semantic-models — ms.date 2026-04-20, updated 2026-07-21. Nondeterminism; *"consider advising users not to use Copilot"*; model-complexity/currency-conversion warning; Desktop metadata caution.
18. Prepare your data for AI (Preview) — https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-prepare-data-ai — ms.date 2026-05-26. Preview status; Q&A prerequisite; nondeterminism note.
19. AI instructions — https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-prepare-data-ai-instructions — 10,000-character limit.
20. Verified answers — https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-prepare-data-ai-verified-answers — 250/model; 500-char triggers; max 3 filters; RLS/OLS *"not fully supported as security features"*.
21. Prep data for AI FAQ — https://learn.microsoft.com/en-us/power-bi/create-reports/copilot-prepare-data-ai-faq — ms.date 2026-07-06. `AgentSchemaReduced`; 5M values / 1,000 entities; no per-group glossary.
22. Limitations of Power BI Q&A — https://learn.microsoft.com/en-us/power-bi/natural-language/q-and-a-limitations — ms.date 2026-05-22. *"Q&A experiences are going away in December 2026."*
23. Data refresh in Power BI — https://learn.microsoft.com/en-us/power-bi/connect-data/refresh-data — ms.date 2025-09-18, updated 2026-08-11. 8/day shared, 48/day capacity; 2 h / 5 h duration caps.
24. Power Query ODBC connector — https://learn.microsoft.com/en-us/power-query/connectors/odbc — ms.date 2025-08-27, updated 2026-07-31. **Capabilities Supported: Import** (DirectQuery not listed).
25. Copilot in Fabric FAQ — https://learn.microsoft.com/en-us/fabric/fundamentals/copilot-faq-fabric — RLS honoured; *"it can try to fill the holes and fabricate data."*

**Microsoft — M365 Copilot, Copilot Studio, SharePoint Premium (VERIFIED)**
26. App and network requirements for Microsoft Copilot — https://learn.microsoft.com/en-us/microsoft-365/copilot/microsoft-365-copilot-requirements — **ms.date and updated_at 2026-08-20**. Primary-mailbox-only limitation.
27. License options for Microsoft Copilot — https://learn.microsoft.com/en-us/microsoft-365/copilot/microsoft-365-copilot-licensing — ms.date 2026-05-19, updated 2026-08-18. Qualifying plan list incl. Office 365 E1/E3/E5.
28. Copilot Studio — standard harness licensing — https://learn.microsoft.com/en-us/microsoft-copilot-studio/billing-licensing — ms.date/updated 2026-08-03. Credits model; messages→credits 2025-09-01; capacity enforcement.
29. Copilot Studio — billing rates and management — https://learn.microsoft.com/en-us/microsoft-copilot-studio/requirements-messages-management — ms.date/updated 2026-08-03. Full credit-rate table; reasoning-model double billing; 125% enforcement.
30. Licensing for document processing (SharePoint Premium) — https://learn.microsoft.com/en-us/microsoft-365/documentprocessing/syntex-licensing — ms.date 2025-08-11, updated 2026-04-23. Per-user licences withdrawn; PAYG only.
31. Pay-as-you-go pricing for document processing — https://learn.microsoft.com/en-us/microsoft-365/documentprocessing/syntex-pay-as-you-go-services — ms.date 2025-08-01, updated 2026-06-01. Full rate table.

**Deltek (VERIFIED / REPORTED)**
32. Use Ask Dela — https://help.deltek.com/Product/Vantagepoint/7.0/Ask_Dela_UseAskDela.html — VERIFIED. Full limitation list incl. no aggregate queries, WBS1 only, single record, two-question memory.
33. Vantagepoint 2026.2 release notes — https://help.deltek.com/product/Vantagepoint/2026.2/ReleaseNotes/DeltekVantagepoint20262ReleaseNotes.htm — VERIFIED, released 2026-03-13. Ask Dela, Dela Insights, Company Policy Insights, Smart Summaries, Contract Management Agent.
34. Vantagepoint 2026.3 release notes index — https://help.deltek.com/product/Vantagepoint/2026.3/ReleaseNotes/ — latest build page dated 2026-08-19; **individual build notes not read** — could not verify 2026.3 Dela specifics.
35. *How the Dela Agent Workforce is Redefining Project Delivery* — https://www.deltek.com/resources/articles/how-the-dela-agent-workforce-is-redefining-project-delivery/ — **REPORTED**, published 2025-11-19. Largely forward-looking (*"will be built"*, *"what's coming"*); no pricing or GA statements.

**Microsoft — Fabric operations, Git, adoption (VERIFIED)**
36. Git integration overview — https://learn.microsoft.com/en-us/fabric/cicd/git-integration/intro-to-git-integration — ms.date 2026-07-21, updated 2026-07-31. 19 of 44 item types preview; all five Power BI types among them; silent failure on unsupported items.
37. Fabric preview terms — https://learn.microsoft.com/en-us/fabric/get-started/preview — updated 2026-01-13. *"Aren't meant for production use… Are not subject to SLAs."*
38. Enable capacity overage — https://learn.microsoft.com/en-us/fabric/enterprise/enable-capacity-overage — ms.date 2026-03-11. Preview; *"recommended only for F16 capacities and higher"*; no refunds.
39. Fabric SKU estimator — https://learn.microsoft.com/en-us/fabric/enterprise/fabric-sku-estimator — in preview since 2025-05-05.
40. SQL analytics endpoint performance — https://learn.microsoft.com/en-us/fabric/data-engineering/sql-analytics-endpoint-performance — updated 2026-08-04. Sync lag; 15-minute inactivity halt; one metadata-discovery instance per workspace.
41. Fabric known issues — https://learn.microsoft.com/en-us/fabric/known-issues/ — *"no longer published on Microsoft Learn"* (~2026-02-17). Retirement redirection JSON (https://raw.githubusercontent.com/MicrosoftDocs/fabric-docs/main/docs/known-issues/.openpublishing.redirection.known-issues.json) carries 90 entries, IDs #447–#1011.
42. Microsoft FY26 earnings — Q2 https://www.microsoft.com/en-us/investor/events/fy-2026/earnings-fy-2026-q2 (2026-01-28, *"over two billion dollars… over 31,000 customers"*); Q3 https://…/earnings-fy-2026-q3 (2026-04-29, 35,000); Q4 https://…/earnings-fy-2026-q4 (2026-07-29, *"over 40,000 paid Fabric customers, up more than 60% year-over-year"*). Fabric appears in prepared remarks only — **not** in the FY26 Q4 press release (VERIFIED by absence).

**Microsoft — Power BI DirectQuery and RLS (VERIFIED)**
43. DirectQuery model guidance — https://learn.microsoft.com/en-us/power-bi/guidance/directquery-model-guidance — ms.date 2024-12-30, updated 2026-08-05. Team-of-professionals framing; 1M-row limit; *"best optimization results are often achieved by applying optimizations to the source database"*; P-SKU retirement note.
44. Row-level security — https://learn.microsoft.com/en-us/fabric/security/service-admin-row-level-security — ms.date 2026-05-13, updated 2026-07-08. Viewer-role-only constraint; row-only scope; Import discards source security; M365 groups unsupported; "looks like the report is broken."

**Microsoft — Foundry and MCP (VERIFIED unless noted)**
45. Foundry Agent Service overview — https://learn.microsoft.com/en-us/azure/foundry/agents/overview — ms.date 2026-08-13, updated 2026-08-19. Rename to Microsoft Foundry; prompt vs hosted agents; supported frameworks; 100-second timeout.
46. Foundry general availability overview — https://learn.microsoft.com/en-us/azure/foundry/concepts/general-availability — ms.date 2026-07-21. Full GA/preview matrix; Workflows retiring 2026-12-01.
47. Foundry Agent Service GA announcement — https://devblogs.microsoft.com/foundry/foundry-agent-service-ga/ — 2026-03-16.
48. Connect agents to MCP server endpoints — https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/model-context-protocol — ms.date 2026-08-05, updated 2026-08-19.
49. Toolbox overview — https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/toolbox-overview — ms.date 2026-07-28. Single managed MCP-compatible endpoint.
50. Claude models in Foundry — https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models — ms.date 2026-08-17. Azure-hosted GA model list; Anthropic Messages API endpoint; no built-in content filtering.
51. Claude CCU billing — https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/claude-models-billing — ms.date 2026-08-14. Marketplace CCU meter; MACC-eligible; single cost line.
52. Anthropic Claude in Microsoft Foundry launch — https://azure.microsoft.com/en-us/blog/introducing-anthropics-claude-models-in-microsoft-foundry-bringing-frontier-intelligence-to-azure/ — 2025-11-18. *"Azure is now the only cloud providing access to both Claude and GPT frontier models."*
53. Five new Claude capabilities in Foundry — https://devblogs.microsoft.com/foundry/five-new-claude-capabilities-now-available-in-foundry/ — 2026-08-17. GA June 2026.
54. MCP GA in Copilot Studio — https://www.microsoft.com/en-us/microsoft-copilot/blog/copilot-studio/model-context-protocol-mcp-is-now-generally-available-in-microsoft-copilot-studio/ — 2025-05-29.
55. MCP servers in Azure API Management — https://learn.microsoft.com/en-us/azure/api-management/mcp-server-overview and .../export-rest-mcp-server — ms.date 2025-11-13, updated 2026-07-01. GA; tools only.
56. MCP on Windows — https://learn.microsoft.com/en-us/windows/ai/mcp/ — updated 2026-06-04. Prerelease.
57. MCP GA in Visual Studio — https://devblogs.microsoft.com/visualstudio/mcp-is-now-generally-available-in-visual-studio/ — 2025-08-19.
58. Declarative agents for M365 Copilot with MCP — https://devblogs.microsoft.com/microsoft365dev/build-declarative-agents-for-microsoft-365-copilot-with-mcp/ — 2025-12-15.
59. Microsoft Build 2025 — https://blogs.microsoft.com/blog/2025/05/19/microsoft-build-2025-the-age-of-ai-agents-and-building-the-open-agentic-web/ — 2025-05-19. *"Microsoft and GitHub have joined the MCP Steering Committee."*
60. MCP governance — https://modelcontextprotocol.io/community/governance — Linux Foundation series; individual (not corporate) maintainership.
61. Baseline Microsoft Foundry Chat reference architecture — https://learn.microsoft.com/en-us/azure/architecture/ai-ml/architecture/baseline-microsoft-foundry-chat — ms.date 2026-06-17, updated 2026-08-18. Component list; load-balancing/failover caveat.
62. Azure Retail Prices API — https://prices.azure.com/api/retail/prices — queried 2026-08-20, East US, USD. `Foundry Models` token rates; `Foundry Tools` agent hosting rates; App Service, Container Apps, Azure SQL, Azure AI Search unit rates. **VERIFIED negative:** no Claude/Anthropic meters exist in the retail catalogue.
63. azure-search-openai-demo — https://github.com/Azure-Samples/azure-search-openai-demo — README declines to estimate cost.

**Deltek data access (VERIFIED\* — Deltek's own wording, retrieved via search extract because help.deltek.com returns a JS shell)**
64. Set Up Direct Database Access — https://help.deltek.com/product/vantagepoint/cloudadminguide/Set_Up_Direct_Database_Access.html — Flex Cloud or Vantagepoint Intelligence licence required for read-only ODBC.
65. Cloud levels — https://help.deltek.com/product/vantagepoint/cloudadminguide/dvp_Cloud_Levels.html — *"Enterprise Development environments support ODBC read/write/execute rights, whereas Flex Cloud provides read-only access."*
66. Deploy Custom Reports — https://help.deltek.com/product/Vantagepoint/CloudAdminGuide/Deploy%20Custom%20Reports.html — *"Deltek Cloud environments leverage a read-only replica as the data source for reporting."*
67. Vantagepoint REST API — https://vantagepointapi.deltek.com/ (titled *"Deltek Vantagepoint 2026.3 API"*, fetched 2026-08-20) and https://help.deltek.com/Product/Vantagepoint/7.0/DPS_REST_API.html — VERIFIED existence; auth/rate-limit detail not published at those URLs.

**AEC market and Deltek ecosystem (REPORTED unless noted)**
68. Deltek Clarity A&E Industry Study 2026 landing page — https://info.deltek.com/Clarity-AE — VERIFIED (47th annual, ~900 US/Canada A&E firms). Underlying study PDF form-gated.
69. Full Sail Partners, *Deltek Clarity Technology Trends*, 2026-05-28 — https://www.fullsailpartners.com/fspblog/deltek-clarity-technology-trends — 80% manual processes; 75% in accounting/finance; 51% lack of time; 48% cost.
70. Full Sail Partners, *Trends in Project Management*, 2026-07-23 — https://www.fullsailpartners.com/fspblog/trends-in-project-management — 56%/55% cost-variance and KPI visibility; 31%/24% schedule variance and client satisfaction; PM accountability metrics #1 at 33%.
71. Full Sail Partners, *From Growth Mode to Sustain Mode*, 2026-06-26 — https://www.fullsailpartners.com/fspblog/from-growth-mode-to-sustain-mode-what-the-2026-clarity-study-says-about-financial-management — operating profit 16.7%; backlog 6.3 months; utilisation 58.9%; overhead 161.3%.
72. Blackbox Connector Deltek integrations catalogue — https://www.blackboxconnector.com/deltek-integrations — VERIFIED. **No Power BI connector listed**; Informer is the BI option.
73. Full Sail Partners BI solutions — https://www.fullsailpartners.com/business-intelligence-solutions — VERIFIED page; "Power BI Enablement Services" sold as consulting; no pricing.
74. Stambaugh Ness, *Connecting Deltek Cloud to Microsoft Power BI* — https://www.stambaughness.com/publication/connecting-deltek-cloud-microsoft-power-bi/ (and the on-premises variant) — VERIFIED as lead-gated, no public technical content.
75. Microsoft Fabric community, *Connecting Power BI to Deltek Vantagepoint API* — https://community.fabric.microsoft.com/t5/Developer/Connecting-Power-BI-to-Deltek-Vantagepoint-API/m-p/4917079 — REPORTED (403 on direct fetch; content via search extract). Azure Function/Logic App intermediary or a custom connector are the workable routes.
76. Brent Ozar, *Fabric Is Just Plain Unreliable, and Microsoft's Hiding It*, 2025-05-19 — https://www.brentozar.com/archive/2025/05/fabric-is-just-plain-unreliable-and-microsofts-hiding-it/ — REPORTED; article 403s to automated fetch. Quote via Hacker News thread https://news.ycombinator.com/item?id=44029566 (2025-05-21).
77. Concord, analysis of Gartner's 2026 Analytics & BI Magic Quadrant — https://www.concordusa.com/blog/what-gartners-2026-magic-quadrant-for-abi-tells-us-about-the-tools-we-work-in-every-day — REPORTED. Fragmented semantic models; BI and platform decisions no longer separable.
78. BI analyst / Power BI developer salaries — https://www.talent.com/salary?job=business+intelligence+analyst (US avg $92,200) and https://ca.talent.com/salary?job=power+bi+developer (CAD avg $117,000) — REPORTED, aggregator/self-reported, **weak evidence, indicative only**.

**Anthropic (VERIFIED)**
79. Claude API model/pricing reference (skill data cached 2026-06-24) — Opus 5 $5/$25, Sonnet 4.6 $3/$15, Haiku 4.5 $1/$5 per 1M tokens; Claude on Microsoft Foundry billed at standard API rates via Microsoft Marketplace.

**Practitioner commentary (REPORTED)**
37. Kurt Buhler (Data Goblins), *Myths, Magic, and Copilot for Power BI*, 2024-09-05 — https://data-goblins.com/power-bi/copilot-in-power-bi — *"significant investment needed to make a solution work well with Copilot"*; *"a solution looking for a problem."* **Caveat: predates the Prep-data-for-AI toolchain by ~1 year.**
38. Nikola Ilic (Data Mozart), 2026-04-01 — https://data-mozart.com/what-now-for-power-bi-the-question-i-cant-escape/ — *"Copilot in Power BI … has been promising but kind of underwhelming for many users so far"*; *"The semantic model is the moat."*
39. Marco Russo (SQLBI), *Generative AI guidelines at SQLBI (2026 update)*, 2026-07-08 — https://www.sqlbi.com/blog/marco/2026/07/08/generative-ai-guidelines-at-sqlbi-2026-update/ — human review before publishing AI-generated DAX.

**KOR internal (VERIFIED — first-hand, live systems)**
40. `docs/audit-2026-08/00-INVENTORY.md` — machine-generated 2026-08-20. 92 projects, ~364,500 LOC.
41. `Kor.Operations.Mcp/Tools/` — 24 tool classes + `ToolErrorEnvelope.cs`, counted 2026-08-20.
42. `docs/KOR-M365-Licence-Report-2026-08-05-web.pdf` — pulled live from Microsoft Graph 2026-08-05. Office 365 E3 ×19, E1 ×28, Business Standard, Entra P1 ×28, Power Automate Premium ×13, `POWER_BI_STANDARD` (free) only.
43. `docs/KOR-BD-Platform-Technical-Overview-2026-07-11-web.pdf` — ~100+ sources, six-tier canonical resolver, write doctrine.
44. `docs/BD-Cron-Cost-Audit-2026-06-10.md` — $50/day observed, ~$46/day attributed, $20–25/day optimised.
45. KOR Deltek engineering record (schema, ODBC quirks, Revenue Generation, tkDetail currency, account codes, company geo split) — the eight items in §10.2, each verified against KOR's live Deltek catalogue between 2026-05-03 and 2026-07-04.

**Microsoft — Power Platform connectors and agent operations (VERIFIED)**
80. SQL Server connector reference — https://learn.microsoft.com/en-us/connectors/sql/ — updated 2026-07-11. Five OData `$apply` aggregation functions; no GROUP BY / joins; non-determinism and pagination duplication; 110-second timeout; 2 MB/8 MB on-prem payload caps; 100 CRUD / 500 native calls per 10 s throttle; 100-node OData filter limit.
81. Code interpreter for structured data — https://learn.microsoft.com/en-us/microsoft-copilot-studio/knowledge-code-interpreter-structured-data — 2026-08-04. **Preview**; CSV/XLSX and SharePoint document libraries only (16 MB/file, max 10 files); not SQL, not Dataverse.
82. Plan agent throughput and rate limits — https://learn.microsoft.com/en-us/microsoft-copilot-studio/guidance/plan-agent-throughput-rate-limits — 2026-06-11. Pre-launch load test + support ticket; *"A throughput increase isn't guaranteed."*

**Copilot Studio practitioner and market colour (REPORTED — thin, use as colour only)**
83. HN practitioner account on Copilot Studio determinism — https://news.ycombinator.com/item?id=46136275 — 2025-12-03.
84. Matthew Devaney (MVP), *Copilot Studio: build agents with SharePoint list knowledge* — https://www.matthewdevaney.com/copilot-studio-build-agents-with-sharepoint-list-knowledge/ — 2026-07-05.
85. SD Times, *AI agent governance* — https://sdtimes.com/ai-agent-governance/ — 2026-07-20. OutSystems research, 94% raise sprawl concerns.
86. Redmond, *Microsoft Agent 365 goes live as company unveils E7 suite* — https://redmondmag.com/articles/2026/05/01/microsoft-agent-365-goes-live-as-company-unveils-e7-suite.aspx — 2026-04-30. Headline/date from RSS; article body returned 403.
87. **Explicitly excluded as a source:** Visual Studio Magazine "Copilot billing shock" coverage (2026-06-03, 2026-08-06) — this concerns **GitHub Copilot, not Copilot Studio**, and must not be cited against Copilot Studio.

### Explicitly not verified

- **Power Automate Premium** current per-user list price.
- **Microsoft Graph connector** index quotas (the 50M/5M figures appear only in Tech Community posts) and pricing.
- Power BI Pro/PPU price-rise **effective date** from Microsoft's own announcement (blog returned 403/404 on three URL variants); the current $14/$24 figures themselves are VERIFIED.
- Fabric F-SKU prices from Microsoft's **marketing** pricing page (renders `$-`) — derived from the Retail Prices API instead, which is a stronger source.
- **SharePoint/Purview audit-log retention and licensing** for `FileDownloaded` events.
- A documented **row cap on Copilot answer results**; and the widely-repeated "2,000-row SQL connector cap" — the 2,048-row cap that *is* verifiable applies to **SharePoint lists, not SQL**.
- Any **Microsoft-published person-hours estimate** for semantic-model AI preparation.
- Any credible **person-day / person-month / TCO figure** for building a production Copilot Studio agent over structured data — searched HN (Algolia API), Google/Bing News RSS, MVP blogs and Microsoft Learn; **Reddit and StackExchange were unreachable (403/429) for the whole session.**
- Any documented, quantified case of a Copilot Studio agent returning a **wrong financial total** over SQL. The case against it in §6.4 is architectural, from vendor documentation — not anecdotal.
- **Deltek Dela pricing, GA scope**, and whether it is included in the base Vantagepoint subscription.
- **Deltek Vantagepoint 2026.3 Dela specifics** — the release-notes index was read; individual build notes were not.
- Deltek **Vantagepoint Intelligence / Dashboards pricing**; whether REST API use carries a licence gate or cost; Deltek Cloud **read-only replica lag**.
- Whether a Deltek-hosted customer is permitted to run **DirectQuery** — no vendor statement either endorsing or prohibiting it was found.
- A quantified **2026 salary benchmark** for the BI-maintenance-owner estimate. The talent.com figures cited are aggregator-grade self-reported data; **Robert Half's 2026 guide keeps BI roles behind an interactive calculator**. Treat the $25k–$60k/yr maintenance figure as order-of-magnitude only.
- **Power BI / Fabric consulting day rates** or fixed-price project benchmarks — no published rate card was reachable.
- Any **primary AEC survey of BI dissatisfaction** — see §8A.2. The "report nobody trusts" framing is not evidenced and should not be asserted.
- Live **active** Fabric known-issue count (the page is client-rendered); the ≥1,011 figure comes from the retirement redirection JSON.
- Reddit-sourced practitioner sentiment on Fabric, Power BI Copilot or Copilot Studio — **all Reddit domains were blocked for this session.** No claim in this report rests on Reddit.
- Whether **Microsoft holds a named Agentic AI Foundation membership** today; and the primary Linux Foundation press release announcing the AAIF (404 / parked domain).
- **Bing grounding pricing** in Foundry — no such meter exists in the East US retail catalogue.
- Current **per-model Claude rates inside Foundry** from an Azure-published page — Foundry defers to Anthropic's published pricing; the last Azure-published figures date to the 2025-11-18 launch post.
