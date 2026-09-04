# Island pipeline build — RESUME

**Read this first if context was lost.** State as of 2026-09-04, mid-build.
Everything below is live and verified unless marked TODO.

## The goal

Rory Beirne (Principal, Vancouver Island) asked for a who's-who of Victoria and
Nanaimo — architects, developers, GCs. The first dossier
(`docs/KOR-Island-WhosWho-Victoria-Nanaimo-2026-09-03-web.pdf`) was research-led.
Ian's instruction on 2026-09-04: collect **all** pertinent data, dedupe it,
decompose it to the Brain, enrich with Apollo/Hunter, then **re-hone the dossier**
with the market research updated, **plus an addendum of the empirical data /
opportunities**, **plus a section on how the data was produced**. Recurring
delivery is deferred.

Rory is INTERNAL, so a methodology section is fine here — the "never hand the
client our method" rule does not apply.

## What is live in production right now

Worker deployed 4× on 2026-09-04 from KOR-1001 → KOR-APP01. Claude runs these
deploys (see the memory `feedback_user_runs_deploys`).

| Source | Type | Live rows | Newest |
| --- | --- | --- | --- |
| `Victoria_DevelopmentApplications` | ArcGIS (20) | 116 opportunities | 2026-08-27 |
| `Coquitlam_DevelopmentApplications` | ArcGIS (20) | 389 | 2026-09-01 |
| `MapleRidge_DevelopmentApplications` | ArcGIS (20) | 120 | 2026-08-27 |
| `Nanaimo_WhatsBuilding` | GenericJson (2) | 877 inserted of 1,287 | no date field |

**All 116 Victoria applications are detail-enriched** — the Prospero extractor
pulled the applicant's own agent (name, email, phone) for each.

⚠ **REVERT PENDING:** `LiveOppDetailEnrichmentBatchSize: 60` was written into
`\\KOR-APP01\C$\Program Files\KorOperations\Opportunities\appsettings.Production.json`
to clear the 116-row backlog fast (default is 8/hour). A `.bak-2026-09-04` sits
beside it. **Put it back to the default once enrichment work is done.**

## Commits (all on `develop`, unpushed)

- `11d1d09f` ArcGIS adapter + probe + Victoria/Coquitlam/MapleRidge
- `58e3511a` relevance-gate planning vocabulary + `tools/RelevanceGateDiff`
- `c477189c` dedup hash now includes Description
- `f05a7206` Victoria Prospero detail extractor + migration 300
- (uncommitted at time of writing: GenericJson `json.urlTemplate` +
  `json.buyerOverride`, migration 301 Nanaimo)

## Where the data is

- **Enriched firm profiles:** `docs/island-pipeline/victoria-applicant-firms-2026-09-04.json`
  — 31 of 42 applicant domains matched in Apollo, plus the 11 unmatched with notes.
  THIS IS THE PRIMARY INPUT FOR THE DOSSIER. It is already written to disk;
  do not re-spend Apollo credits on it.
- **Query helper:** `docs/island-pipeline/query-opportunities-db.py` — run as
  `python query-opportunities-db.py <file.sql>`; reads the connection string from
  `KOR_OPPORTUNITIES_OPPORTUNITIESDB`. Always put `SET QUOTED_IDENTIFIER ON;` at
  the top of any script that UPDATEs `opportunities.Opportunities`.

### The one query that reproduces the agent harvest

```sql
SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON;
SELECT o.BuyerContactName AS Agent, ISNULL(o.BuyerContactEmail,'') AS Email,
       COUNT(DISTINCT o.Id) AS Applications
FROM opportunities.Opportunities o
JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
WHERE s.Name = 'Victoria_DevelopmentApplications' AND o.BuyerContactName IS NOT NULL
GROUP BY o.BuyerContactName, o.BuyerContactEmail
ORDER BY COUNT(DISTINCT o.Id) DESC;
```

## Headline facts the dossier rests on

- **116 live Victoria development applications**, newest 2026-08-27, each with the
  applicant's agent by name and email. Largest: **22-storey designated-affordable
  residential at 251 Esquimalt Rd**; six-storey at 350 Douglas St; four-storey
  mixed-use at 1252 Gladstone Ave; **three mixed-use towers at 829-899 Fort St /
  846-856 Broughton St** (agent Kaeley Wiseman, Wiser Projects).
- **Most active applicants by application count:** Islandview Group (Niall
  Paltiel, 4) · Aryze Developments (Kieran Lynch + Dan Kwak, 4) · D'Ambrosio /
  DAU Studio (Erica Sangster + Michael Barros, 4) · Cascadia Architects (Gregory
  Damant, 3) · Colin Harper Architect (3) · M'akola Development Services (Holly
  Pridie + Bronwyn McLean, 3) · Sakura Developments (Dan Robbins, 3).
- **KOR-lane fits flagged in the JSON:** MJM Architect (seismic upgrades +
  heritage adaptive reuse), Studio 531 (mass timber, institutional, +75% headcount),
  Cascadia (Passive House incl. large affordable), Reliance Properties (heritage
  restoration), Townline (explicitly targeting Vancouver Island), Bayview Place
  (Roundhouse master plan), Wiser Projects (gatekeeper to Indigenous / non-market
  housing).
- **One competitor found in the applicant set:** Evoke Buildings Engineering
  (building science / envelope, employee-owned, expanding in BC) — adjacent to
  KOR, not structural.
- **Starlight:** David Woo (`dwoo@starlightinvest.com`) is **Starlight
  Investments** — Canada's largest private landlord, the Harris Green developer.
  NOT our client Starlight Development. The shipped dossier already carries this
  correction; keep it.
- Nanaimo's richest descriptions name the occupant: "ABC Recycling Ltd., Steel
  Recycling Facility, consists of 3 buildings"; "(Convertus Canada) Addition of
  593m2 Group F2 processing plant … 4 pre-cast concrete composting tunnels".

## What remains — TODO

1. **Decompose to the Brain.** No existing tool promotes `Opportunity.BuyerContact*`
   into `CanonicalOrg` / `IntelPerson`. Schema facts already established:
   - `IntelPerson.NaturalKey` = **SHA1 hex, uppercase**, of the lowered email.
   - `IntelPerson.SourceEnrichmentId` has an **FK to `CanonicalOrgEnrichment`** —
     a parent row must exist first. `SourceProviderName` is a free tag; use
     something like `PermitApplicants`.
   - `CanonicalOrg.NormalizedName` is COMPUTED; `FuzzyNormalizedName` is NOT —
     set it explicitly or the row groups with unrelated orgs.
   - `OrgFact` needs `NaturalKey` (SHA1), `FactType`, `Body`, `Confidence`,
     `CreatedBy`. Precedent tag from the earlier Island pass:
     `claude-island-verify-2026-09-03`.
   - Existing `CanonicalOrg.Kind` values: Buyer, Vendor, Competitor, Investor,
     KorStructural, KorClient, Unknown, Designer.
   - ⛔ Resolve orgs by EXACT `DisplayName`, never `LIKE`.
2. **Hunter** on the firms whose people we do not have (`tools/BdContactEnrich`
   already does Hunter domain-search for orgs that exist in the Brain — so run it
   AFTER step 1).
3. **Re-hone + re-decompose**, then rebuild the dossier:
   updated market research + empirical addendum + methodology section.
   Build per `tools/BdDocTemplate/CLAUDE.md`: copy the `<style>` block verbatim,
   render with `tools/Format-BdWebPdf.ps1`, **verify the PDF not the HTML**
   (`pdftotext -layout`, and `pdftoppm` and LOOK at it).
   ⚠ Wide tables silently lose right-hand columns — use `<ul class="plain">` for
   anything with prose per row.
4. **Revert the enrichment batch size** (see above).
5. Later, not now: the recurring weekly delivery, which should be a second
   profile on the existing `WeeklyAttackSheetJob` rather than a new job.

## Open findings not yet fixed

- Relevance gate still matches exclusion terms inside street names ("Tyee **Road**").
  311 `road` rejects platform-wide, untriaged.
- `opportunity_merged_across_sibling_sources` = 1 (BIDSTEND-26-067 carries both
  Maple Ridge's and Coquitlam's tender). Key prefix is 8 chars; 48 sources collide.
- `source_insert_rate_collapsed` = 1 (`Bonfire_Saanich`: 353 runs, 704 filtered in
  30 days, 1 insert ever).
