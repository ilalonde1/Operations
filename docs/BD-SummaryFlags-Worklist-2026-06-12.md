# BD Summary-Flags Worklist — 2026-06-12

Harvested from every QueueDrain SUMMARY-*.txt of the 06-10..06-12 waves.
Mechanical-confidence items already actioned (13 dup merges via dedup
--pairs; m132 competitor kind hygiene 125 rows; m133: 5 deceased persons,
4 misclassified orgs, 7 defunct orgs). Remaining items below are
VERIFY-CLASS — each needs a record lookup before acting. Process gap that
caused this backlog: summaries were never systematically read; fix is the
flags-harvester step below.

## STATUS 2026-06-12 (post verify-flags campaign)

The 33-item verify-flags QueueDrain ran 2026-06-12 (evidence per item in
C:\ProgramData\KorOperations\QueueDrain\verify-flags\outputs\verify-N.json)
and was ingest-reviewed against the live DB. Applied as migration
134_VerifyFlagsSweep.sql + 13 dedup pairs
(output/verify-flags-pairs-2026-06-12.csv). Notable divergences from the
raw verdicts, found during DB re-verification:
- James Cheng survivor FLIPPED: 69676 survives (mpi=11) not 54297 (mpi=3).
- E. Holland was a MERGE into existing 50504, not a rename.
- 38998/39053/39073 were FUSED rows -> surgical repoints, not merges
  (38998 stays Standards Council of Canada; 39053 retired as typo shell;
  39073 stays ABC Life Literacy Canada, Kind=Vendor).
- Worklist person ids 80/88/124 were a different id space; real records
  found by name (McPhee/Lee = IntelProjectKeyPerson; Sammi Ha already
  retired, no action).
- McIntosh Perry consolidated INTO 74130 'Egis Canada' (renamed from the
  CCI Group junk name; Kind=Unknown allied-discipline, m132-consistent).
- Bonus dups merged: Eng-Spire 71169->68740; person 9099 -> 244 (Wolsey).

NOT actioned, with reasons (carry-forward):
- verify-14 Lemco 10869 / Lemay 10868: different award contracts, no shared
  contacts, no web trace of 'Lemco Architecture' — same-entity unproven.
- verify-17 ATB/CTA 2751: CTA Architecture + Design bankruptcy real, but
  'ATB' in the flag was the CREDITOR (ATB Financial); no evidence 2751
  'ATB Architecture' (single 2015 Edmonton award) is that firm.
- Isle Energy Consulting not minted (Part 9 residential — out of BD scope).
- org-name-repair payload refusals still in outputs/: 74051 (BCIT,
  contradictory), 74153 (Ledcor, truncated echo). 74130 resolved by m134.
- Faction org sprawl noted (69429/70557/74016/70546 variants) — planner.
- AtkinsRealis + Egis vendor-row sprawl left for the awards-tier campaign.

## Rebrands (rename vs merge-into-successor — decide per record)
- 15558 SNC-Lavalin -> AtkinsRealis (Sept 2023)
- 53516 Ivanhoe Cambridge -> La Caisse (merged CDPQ, June 2025)
- 53335 Hokanson Capital -> HCI Ventures Inc. (hci.ca)
- 53491 Points West Living -> Connecting Care
- 70731 Twin Peaks Structural -> Ikon Engineers (Whistler)
- 11533/11534 McIntosh Perry -> Egis Canada (2023)
- 47266 Method Engineering -> Metachro Engineering + Isle Energy (2024, split!)
- 12107 METAFOR = rebrand of another record (find + merge pair)
- 9217 IBI Group Architects -> acquired by Arcadis 2022 (archive vs merge to Arcadis record)
- Stuart Olson Dominion -> Bird Construction (id not captured; in honing-gcs b001)
- 46917 E. Holland Contracting -> Holland Power Services (Alectra 2021)
- 11418 Maskell Plenzik -> MP&P: Powered by MCW (acquired June 2022; already kind-reclassified m132)

## Possible/probable duplicates (verify then merge)
- 4851 CIMA Canada Inc. vs 4856 CIMA+ — verify same entity
- 69676 James KM Cheng Architects vs 54297 — verify
- 30665 Gustavson Wiley vs 19491 Gustavson Wylie — verify spelling twins
- 11041 LOLA Architectures vs 66594 — probable
- 10869 Lemco Architecture vs 10868 Lemay — probable typo
- 8314 Goss Architectural vs 8315 — verify
- 74133 Kassian Dyck & Associates Engineering vs 68749 (mutual dup flags; pick survivor by links)
- 2751 ATB/CTA Architecture — possibly defunct (bankruptcy Mar 2025) — verify before retiring
- 7031 MPI Vernon Jubilee Psych Unit = duplicate of MPI 4320/4489 (project-side; m-style merge)
- 54085 District Central Saanich — mis-classified duplicate (find true target)
- 14565/14566 Roads West garbled pair — 14566 merged; verify 14565 name quality

## People corrections (update title/employer; verify each)
- 1669 Karen Marler — Principal Emeritus at hcma Jan 2026; no longer selection authority (engagement plan already notes)
- 288 Atila Zekioglu -> Degenkolb Senior Principal (Feb 2024)
- 290 Michael Liou -> Degenkolb (2024)
- 248 Andrew Lischuk — possible return to Stantec (verify)
- 68753-related: Danny Wolsey departed Wolsey Structural -> DW Structural Engineering Ltd. (person + new org)
- Tim McLennan departed HDR -> founder/CEO Faction Projects Inc. (Kelowna)
- 481 Brad Klassen — CGO since Nov 2024 (not CFO); Jeff Kennedy is CFO (create?)
- 484 Gudrun Seredynski — VP Real Estate Accounting (not CFO)
- 477 Laurie Anderson — ONE Properties ceased Aug 2025; employer unconfirmed
- 476 Darren Durstling -> President, Integro Investments
- 88 Dr. Victoria Lee — departed Fraser Health Feb 2025
- 80 Doug McPhee — title wrong (not Secretary-Treasurer)
- 124 Sammi Ha — departed Hopewell -> GroundBreak Ventures Toronto
- Rhiannon Mabberley — departed (lawsuit 2025); org from honing-developers b001

## Buyer mapping errors (wrong entity links — verify + remap)
- 38998, 39053, 39073: CRM entries map to wrong entities (IHA, SD23, SD73)

## Other
- honing-buyers-deep b001: 6 duplicate buyer pairs flagged "in each record" — extract ids from briefs
- honing-developers-deep b001: bdValue undercounts flagged (e.g. upgrade 1 -> 13) — bdValue is computed at batch-time, no action needed; confirm
- GC wrong-sector/wrong-market lists in honing-gcs b001 (civil/highway/demolition/residential) — kind hygiene pass like m132, evidence in briefs
- 60194 Minerva for Engineering Studies — DATA ERROR, possibly Jordanian firm (verify, likely retire)
- 11225 Magna Vi — DATA ERROR, misread of Magna IV (11222 exists; merge/retire)

## PROCESS FIX (build item)
Summaries must be harvested automatically: extend the morning ingest ritual
(or BdMorningReportJob) with a flags section — scan new SUMMARY-*.txt for
the flag-pattern lines and surface them in the 6am email so nothing files
itself silently again.
