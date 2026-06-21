# BD Module Remediation Roadmap — 2026-06-21

Source of truth: `docs/BD-Module-GapAnalysis-2026-06-21.md` (Codex-produced, **verified by Claude against the live DB — every load-bearing number exact**). Goal: clean and solidify the BD module to production quality, **no regression**, every write gated.

## The gate (applies to EVERY write, no exceptions)
1. **Pre-flight / dry-run** — simulate the exact effect against the live DB (resolution map for ingests; `BdCanonicalDedup` plan for merges). The importer's own `--dry-run` is insufficient for resolution (it short-circuits the resolver) → use the deterministic resolution pre-flight (`output/preflight-org-resolution.ps1`).
2. **Review** — Ian sees the plan (match-vs-create, survivor-vs-loser) before commit.
3. **Commit** — apply.
4. **Verify + post-audit** — re-query; for merges, audit survivors (wrong SurvivorIds have bitten us twice — `feedback_honing_merge_audit`).

## Block 1 — Solidify the org graph (FOUNDATION; everything depends on it)
- **1a. Merge the dup clusters** — ✅ **DONE + VERIFIED (2026-06-21).** Tool plan = 98 pairs; reviewed all; excluded `legacy` (different firms); allowlisted 38 descriptor-variants (campaign file `dedup-non-similar-allowlist.d/north-cleanup-2026-06-21.csv`). **Committed 92/97; verified: active orgs 49,873→49,781 (−92), survivors present/losers gone, 0 new orphans.** **5 DEFERRED** (CitySpaces #4905, HSEA #71633, L7 #48014, Lime #70765, Wilden #25): rolled back safely on `UX_CrmEngagements_BdRelationship` collision (loser+survivor each hold a buyer+owner+region engagement) — **fix = reconcile the duplicate CRM engagement first, then re-merge.** No data lost; both rows intact.
- **1b. Reclassify the mislabels** — ✅ **DONE + VERIFIED (2026-06-21).** Of the original 45, several merged away in 1a; **40 reclassified** (role-derived; institutional owners — cities/counties/depts/universities/Microsoft/Stanford/Sun Life → **Buyer**, not Developer; clear devs/architects/GCs → their kind). **2 person-strings HELD** (#76990 CORRALES, #77005 HILL/HO/PANG) → moved to 1c name cleanup. After: only those 2 Vendor/Unknown remain in active MPI roles.
- **1c. Split / clean the name-integrity messes** — start with **#794** (7 health authorities concatenated); JV-strings per the keep-named-JVs / collapse-variants policy (`project_jvstring_canonicalization`). *Verified scope: ~291 triage candidates.*
- **1d. Repair the 16 MPI orphans** — 7 SE + 9 GC references point to absent org ids → null or re-resolve. *Verified: 7 + 9 = 16.*

## Block 2 — Pipeline integrity
- **2a. Add FKs on MPI `StructuralEngineerCanonicalOrgId` + `GeneralContractorCanonicalOrgId`** (only Proponent+Architect have them today — *verified*). Must follow 1d (can't add FK with orphans present).
- **2b. Close the resolver dup-creation cycle** — add a reviewed match-key/alias candidate check **before** create (the strict-vs-fuzzy gap that created Graham/IDL/NIC dups). Keep the `BdCanonicalDedup` FK-coverage guard.

## Block 3 — Source health + missing sources
- **3a. Triage the 21 enabled-but-dead sources** (run "succeeds" but 0 rows — incl. both provincial MPI feeds). Per-source: fix or disable, with a row-count canary going forward.
- **3b. Fix the stalled Vancouver permit poll** (`Content-Length 81MB exceeds 52MB limit` → page/stream).
- **3c. Add missing sources** (human-gated onboarding): Surrey/Calgary/Edmonton/Victoria permit APIs, OpenNWT/GNWT, Yukon Bids & Tenders, Infrastructure BC, Northern Health, First Nations.

## Block 4 — Extraction completeness
- **4a. Fix the lossy importer tags** so SE/GC/seat actually persist (the 3.2% SE-coverage root cause) — incl. `indigenous` writing SE into schedule-notes instead of the SE column; harden `pipeline-seats` against null-overwrite of classifications.
- **4b. Widen `OpportunityCandidate`** to carry architect/GC/SE/contacts (typed observations + provenance, not raw-JSON overload).

## Block 5 — Ingest the northern research onto the clean graph
Re-do the project + org ingest (the rolled-back batch) with the pre-flight resolution gate + MPI-match (backfill existing, create only true net-new). Uses exact canonical names → zero dups.

## Block 6 — Enrich barren entities
Queue the barren/newly-seeded orgs through the org-brief enrichment pass (post-ingest enrichment is standard — `feedback_postingest_enrichment`).

---
**Execution order:** 1 → 2 → 3 → 4 → 5 → 6. Blocks 3/4 (code) can be drafted as Codex prompts and verified in parallel with 1/2 (data). **Currently executing: Block 1a (dedup dry-run).**
