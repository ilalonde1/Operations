# BD Overnight Enrichment — Morning Report (2026-06-24)

**Run by:** autonomous overnight session. **Targeting principle:** fan out from closest clients → cold clients → prospects; spend only on entities that matter; everything verified against the live DB. All writes went through the fixed `usp_ResolveOrCreateIntelPerson` resolver (no duplicate people).

---

## 1. What landed (verified counts)

| Metric | Before | After / added |
|---|---|---|
| Orgs with 2nd-pass hone (FirmNarrativeHoning) | 3,766 | **4,193** (+427) |
| Orgs with initial narrative added | — | +11 (real ring 1+2 firms) |
| New contacts (IntelPerson) | — | **+225** |
| New intel signals | — | **+281** |
| New recommended actions | — | **+658** |
| Apollo contacts revealed | — | 4 (4 credits — see §5) |
| People pruned (zero markers of life) | — | 0 (none qualified — data was clean) |

**Ring 1+2 (your closest + cold clients) real-firm gaps:**
- Missing initial narrative: **12 → 2**
- Missing 2nd-pass hone: **32 → 11**
- Missing contacts: **37 → 27**

The remaining ring 1+2 "gaps" are dominated by **shell/SPV entities** (numbered companies + project LPs) that have no website and no people — see §4. They are intentionally left empty.

---

## 2. Marquee live pursuits surfaced (time-sensitive — act on these)

1. **UBC Lower Mall Precinct** — $560M, Ryder/3XN, construction Sept 2026, **SE not named** → contact **Adam James** (Ryder Vancouver) now.
2. **Mikisew Commons** — 438-unit Edmonton Indigenous-led housing (Mikisew Cree + Paragon) → **Roy Tutschek**; APEGA registration required.
3. **Marcon — 2030 Barclay Hotel** (25-storey, West End) in rezoning, **SE open**; West Pender Hotel SE unconfirmed (likely Glotman via Henriquez).
4. **Nelson Investments — 937 View Street, Victoria** (23-storey) — SE not publicly named 18 months post-approval; lead **Mehrdad Ghods, P.Eng.** → urgent verify.
5. **Dayhu Group — 2245-2283 W Broadway** (25-storey rental) in rezoning, **SE open**; architect **Proscenium** → contact Proscenium.
6. **Perkins&Will — UVic follow-on student housing** — SE appointment Q3-Q4 2026 → Jana Foit / Alex Minard.
7. **Arcadis (new CEO Mar 2026 = sub-consultant reset)** — Nexus Mass Timber ($60M, Penticton) + Barclay Tower (48-st) → **Anita Leonoff** (Vancouver).
8. **Architecture49** — Glen Klym CEO (Dec 2025); anchor congratulations on existing Port Coquitlam relationship.
9. **Bowen Island Municipality** — new Director of Engineering Sarah Kosari (Apr 2026) building consultant roster → approach for Public Works yard.
10. **Pro-Can Construction** — 20-yr Surrey School District seismic-upgrade relationship → teaming target before fall tender.
11. **Revery Architecture** (ex-Bing Thom) — Venelin Kokalov warm-intro window (Dreamers exhibit closes Jul 18).

**Relationship/data intel:** Mondiale Development = **Pinnacle International's GC arm** (Michael De Cotiis on both). **Coromandel Properties is in receivership** — check outstanding KOR invoices (MNP Ltd).

---

## 3. CRM cleanup actions (need your nod — not auto-applied)

- Retire **"Bing Thom Architects"** → **Revery Architecture Inc.**
- Merge **IBI Group** standalone entries → **Arcadis** (Vancouver: Anita Leonoff)
- Consolidate 3× **3XN/Ryder JV** records (75557, 76285, 76397) → one canonical
- Consolidate 4× **Architecture49** records → one canonical
- Retire **Burnaby Hospital Phase 2** (mpiId 5992) — cancelled May 2026
- Recommend a **dedup-audit pass** (BdCanonicalDedup dry-run → audit → commit) before any auto-merge — per the standing wrong-SurvivorId rule.

---

## 4. Intentionally skipped (the "don't waste money" calls)

- **Shell/SPV ring 1+2 entities** (no web, no people, billing vehicles): `1100186 BC Ltd`, `1490697 BC Ltd`, `1450991 BC Ltd`, `2009850 Alberta Ltd`, `583230 B.C. Ltd`, `1353101 BC Ltd`, `1127241 BC Ltd`, `Aria Block B LP`, `Third Squamish LP`, `Third East Squamish LP`, `India & Beech LLC`, `Island Sky Place LLC`, `Kingdom Granville LP`, `Gibbins Road Holdings`, `Nexus…Ltd`.
- **The never-honed `orgs` tail** — sampled **72% junk** (garbled names, multi-entity concatenations, Deltek all-caps imports). Not fed. Flagged for cleanup instead.
- **Dedicated deep person-briefs** — the people-drain query isn't kind-filtered (would research vendor-side people); stopped it. Contacts came from org honing instead.

---

## 5. Apollo / Hunter finding

Apollo was pointed at thin high-value orgs (warmest first) and at the ring 1+2 contact gaps explicitly. Result: **4 verified contacts for 4 credits** across the whole night; near-zero coverage of small BC developers/GCs. **Conclusion: Apollo is not the contact engine for this market — the web-research honing is.** Spend stayed negligible by design.

---

## 6. What remains (honest)

- **Ring 3 prospects**: ~7,665 orgs, only ~427 honed tonight. The remaining un-honed pool is junk-heavy — recommend honing only *real* high-value prospects, not the tail. Consider tagging GC/Competitor as **No-Hone** (the agent flagged batches 031-036 as low-value Alberta GC lists).
- **Low-value people prune**: ~1,210 people attach only to low-value orgs / no high-value link — an audited prune awaiting your nod (reversible; regen on re-discovery).
- **Dedup-audit** for the §3 merges.

---

## 7. Recommended next moves

1. Action the §2 marquee pursuits (outreach — several have closing windows).
2. Approve the §3 CRM merges (I'll run them audited).
3. Decide on No-Hone tagging + junk-tail cleanup so future runs don't touch garbage.
