# What happened — and how it affects Michael Li's partnership entitlements

**A note for the partners** · Prepared by Ian Lalonde (Ops & IT) · 2026-07-14 · **For: the partners → legal** · *Not legal advice*

---

> **Read this first.** This is my own read, put together from the records I could get to — the Partnership Agreement, our BC registry filings, the partner balance sheet, and Deltek. **It may contain mistakes, and it is not legal advice.** It's meant to give the partners the shape of the situation and a starting point for legal — the numbers and the enforceability of everything below need Finance and counsel to confirm.

---

## What's at stake — and what we can recoup

What we would otherwise pay him, and what the firm can set off against it for the cost his conduct created. Figures are ballpark and as-dated — Finance to firm up.

| Item | CAD, ballpark |
|---|---|
| **What we'd otherwise pay him — §9.6** | |
| Paid-in capital (Buy-In account) | $35,600 |
| Unpaid allocations (net) | $36,733 |
| Buyout (indicative — Finance to confirm) | ~$90,000 |
| Retained equity, KOR USA Corp (US$15,498) | ~$21,000 |
| **Gross payout** | **~$180,000** |
| **What we can set off against it — §6.16** | |
| Rebuild the toolset he obfuscated & took (multi-month developer) | ~$75k–$150k+ |
| Investigation & re-securing the environment | ~$10k–$30k |
| **Set-off (estimate)** | **~$85k–$180k** |
| **Realistic net actually payable to him** | **~$0–$95k** |

Gross entitlement ≈ **$180,000** (table above). Under §6.16 the firm may apply its documented costs from the conduct in Section 1 against that total. On the cost estimates shown ($85k–$180k), the arithmetic net payable is roughly **$0–$95k**. The buyout (~$90k) and unpaid allocations fall entirely within the set-off; return of his own paid-in capital (~$35.6k) is the item most likely to be treated differently — a question for counsel.

**Mechanics, not advice.** Set-off under §6.16 is exercised by the Finance Department "at any time and without prior notice" — no court process. Recovering *beyond* the set-off (a damages claim exceeding his entitlement) would require separate legal action. Which course to take is for the partners and counsel; this note sets out only the figures and the contract terms.

---

## 1. What happened

He gave formal notice on **2026-06-19** (effective **2026-07-31**), stating he was "fully committed to helping with the handover… to make sure nothing is left undone." **During that notice period**, the following was recorded on his company workstation (documented with evidence in the separate *KOR-302N Security Incident Dossier*):

- **He took the company's source code.** His working-source folders were emptied 2026-07-08, with Explorer open to the same projects on a personal USB — a move, not a delete. KOR's custom Revit tooling source left our systems.
- **He obfuscated the company's own tools.** 169 of ~195 deployed tool DLLs were deliberately scrambled so they can't be read or rebuilt. Applied to KOR's own software, the only effect is to stop KOR maintaining it.
- **He exported his KOR mailbox** to a 357 MB file using a purpose-built tool (2026-06-29).
- **He ran unauthorized outside access.** A personal VPN tied to an unidentified account bridged his KOR machine to an off-domain PC for ~6 weeks; an unauthorized never-expiring admin account (confirmed not IT-created) was present on the machine; the company's remote-management agent was removed from it; his browser history was wiped the night of 2026-07-09.

All of the above are dated after his 2026-06-19 notice. The confidentiality and non-solicitation obligations in the agreement (§9.11, below) apply to partners and Former Partners; whether this conduct breaches them, or supports a damages claim, is for counsel to assess.

---

## 2. How it affects his partnership entitlements

He has resigned and left. His interest is held through **L&Z Concept Consultants Ltd.**, so his figures show under that name. Under our current agreement (*Kor Structural — Partnership Agreement, 2023-12-01*):

### Going forward, he gets nothing — §9.1 / §9.8
A partner who resigns **"will not be entitled to any further draws, distributions or allocations or the payment of any other future amounts"** after his final month (§9.1). He **surrenders his entire interest** in the firm's funds, assets and any profit earned after he ceases (§9.8, Effect of Ceasing). No ongoing income, no share of future firm value.

### What he is owed is a short, closed list — §9.6
§9.6 is exhaustive — it ends **"no other or additional payment to a Former Partner for any reason."** Three things only, and every one is exposed to set-off (below):

| What he's owed | Amount (as-dated — refresh needed) |
|---|---|
| (a) Paid-in capital — his Buy-In / Contribution account | $35,600 (Jan 1 2025) |
| (b) Unpaid allocations (net) | $36,732.71 (May 31 2025) |
| (c) Buyout — Schedule D (see below) | qualifies |
| Separate: retained equity in KOR Structural USA Corp. | US$15,497.71 (Dec 31 2024) |

Capital is repaid over 12 months, no interest (§9.7). **These figures are over a year old — Finance must refresh to his cease date.**

### The buyout — he qualifies (Years of Service ≥ 10) — Schedule D
The buyout needs **10 Years of Service** (years as a partner + 50% of pre-partner firm years). Our records — hired **2014-10-01**, a partner **in 2015** (2015 profit-share allocated; partner draws from Aug 2015; L&Z in the BC registry by Jan 2016) — put him at **~11 years, over the line** (his own notice says "over 11 years"). So it is **not** auto-zeroed; he qualifies.

Amount = **his 5-year-average allocation × a factor** (0.50 at 10 yrs rising to 1.50 at 30; ~**0.56** at his ~11 yrs), then **capped at 10% of the firm's 3-year-average profit**. His verified allocations (the "incl. dividends" basis the firm uses), from the internal allocation ledger:

| Year | 2020 | 2021 | 2022 | 2023 | 2024 | 5-yr avg |
|---|---|---|---|---|---|---|
| Allocation | $189,949 | $152,071 | $187,537 | $193,116 | $88,114 | **$162,157** |

**Indicative buyout ≈ $90,000** ($162,157 × ~0.56), and at that level the 10%-of-3-yr-profit cap almost certainly does *not* bind. It "resolves all future financial claims on the Firm," and its terms can't be changed without every partner's written consent.

*My calculation — Finance to confirm.* With a **31 July 2026** cease date the buyout window is the five full fiscal years **2021–2025**; I've shown **2020–2024** as a proxy because 2025's allocation wasn't finalized in the records I had — Finance should drop 2020 and add 2025, then apply the exact factor and the 10%-of-profit cap. (The firm's **3-year** average, $156k, is the separate **buy-in / capital** figure — not the buyout.)

### But all of it is exposed to set-off — §6.16
§6.16 (verbatim): "…the Partnership and any related entities will have the right to set off any amount owed by a Partner or a Former Partner, against any amount payable to any such Partner or Former Partner under this Agreement. This right may be exercised by the Finance Department … at any time and without prior notice."

**Observation:** the agreement contains **no separate "for-cause forfeiture" provision**; set-off is the mechanism it provides for recovering what a Former Partner owes. Where that amount equals or exceeds his §9.6 entitlement, the net payable is nil.

### Obligations that survive his departure — §9.11
As a Former Partner he remains bound by the **non-solicitation** of KOR clients and staff (§9.11) and by **confidentiality**. The conduct in Section 1 involved company source code and mailbox data; application of these obligations to it is for counsel.

---

## 3. The set-off basis — what his conduct cost us

| Cost | Basis |
|---|---|
| **Rebuilding the Revit toolset** he obfuscated and removed the source for | A multi-month engineering project needing a dedicated Revit/C# developer (per the Rebuild SOW). A direct, avoidable cost created by the obfuscation + source removal. |
| **Forensic investigation & re-securing** | IT time to investigate, preserve evidence, and close the account / VPN / backdoor exposures across the fleet. |
| **Disruption & risk** | Lost maintainability of tools the drafting team uses daily; the standing exposure from an outside VPN into our network for ~6 weeks. |

These are order-of-magnitude figures for Finance/legal to quantify, not a computed total. On the SOW scope, the rebuild alone is a multi-month developer engagement — on that basis it could approach or exceed his net entitlement.

---

## 4. For legal / next steps

- **Don't pay or sign anything yet.** A voluntary payment or a clean release can waive the set-off.
- **Finance:** refresh his entitlement to the cease date and compute the buyout (5-yr allocation average × ~0.55, then the 10%-of-profit cap).
- **Finance/IT:** tally the set-off — rebuild + investigation + disruption — as a documented number.
- **Legal:** confirm the entitlement figures, the enforceability of set-off (especially against return of capital), and whether the conduct supports any position beyond set-off.
- **Cease date: 31 July 2026** — per his resignation notice (emailed to the partners 2026-06-19, effective 2026-07-31). Under §9.1 that makes **July 2026 his final month**; strike the entitlement as of then. His Deltek record still shows him active with time to 2026-07-12 and no termination flag — deactivate it. (Domain, M365 and building access are already closed.)

*Sources: the executed Partnership Agreement (2023-12-01) incl. Schedule D; BC Registry partnership filings; the Partner Balance Sheet (2025-05-31); Deltek Vantagepoint (employee P0010); the KOR-302N Security Incident Dossier; and the Revit Toolset Rebuild SOW. Financial figures are as-dated and must be refreshed to the cease date.*
