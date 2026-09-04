# Island pipeline build — RESUME

**Read this first if context was lost.** State as of 2026-09-04, mid-build.
Everything below is live and verified unless marked TODO.

## ⛔ SCOPE RULE — Rory's dossier is VANCOUVER ISLAND ONLY

Confirmed by Ian, 2026-09-04. v3 named **Coquitlam and Maple Ridge** in its
methodology box and used a **Coquitlam** project (Ledingham McAllister) as the
worked example of "our feed publishes applicant names". Both are Lower Mainland.
In a document for the Principal, Vancouver Island that reads as though we are
presenting them as his market — in the one section meant to build confidence in
the data.

**In the dossier — the five Island cities (2,774 applications):**

| Saanich | Nanaimo | Langford | Victoria | Colwood |
| --- | --- | --- | --- | --- |
| 1,398 | 877 | 349 | 121 | 29 |

Plus **$326M of construction value across 293 priced Langford applications.**

**In the platform, OUT of the dossier:** Coquitlam (391), Maple Ridge (120).
⚠ Any headline count must exclude them — "3,285 applications" silently included
511 Lower Mainland rows.

**Off-Island HEAD OFFICE is fine; off-Island MARKET is not.** Northland,
Townline, Intracorp, Reliance, Ledcor and Starlight are Vancouver- or
Toronto-headquartered and belong in the dossier, because they are filing
applications *in Victoria*.

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

✅ **DONE — reverted 2026-09-04.** The batch size below was raised to clear the
backlog, then put back to the default and the service restarted;
`appsettings.Production.json` is Logging-only again. Historical note follows.

`LiveOppDetailEnrichmentBatchSize: 60` was written into
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

## ⭐ THE HEADLINE FINDING (migration 302, verified live 2026-09-04)

Decomposing the permit harvest into the Brain resolved 42 applicant firms — **18
created, 24 matched orgs KOR already had**. Ten of them carry a **Deltek client
id**, and the merge revealed that the most active applicants in Victoria right
now are **already KOR clients**:

| Firm | Kind | Deltek | KOR projects | Last KOR project | Live Victoria apps |
| --- | --- | --- | --- | --- | --- |
| Northland Properties Ltd. | KorClient | CL00299 | **369** | **2026-08-26** | 2 |
| Reliance Properties Ltd. | KorClient | CL00356 | **82** | 2026-01-19 | 1 |
| Intracorp Projects Ltd. | KorClient | CL00205 | **46** | 2026-06-25 | 1 |
| Townline Group of Companies | KorClient | 7A3C11F3… | **36** | 2026-02-05 | 2 |
| Primex Investments Ltd. | KorClient | CL00337 | 9 | 2025-07-25 | 2 |
| Sakura Developments | KorClient | 42B07C0E… | 3 | 2024-11-24 | 3 |
| GWL Realty Advisors | Developer | CL00167 | 2 | 2023-10-11 | 2 |
| M'Akola Development Services | KorClient | ce787f97… | 1 | 2025-10-20 | 3 |
| Ledcor Construction | GC | CL00238 | 0 | — | 1 |
| D'Ambrosio Architecture + Urbanism | Architect | f147c32b… | 0 | — | 4 |

**Coldest high-value targets** (no Deltek id, most live applications):
Islandview Group (4) · Aryze Developments (4) · Cascadia Architects (3) ·
CHA – Colin Harper Architect (3) · Wiser Projects (2) · MJM Architect (2) ·
Mike Geric Construction (2) · Bayview Place (1, Roundhouse master plan).

Brain state written: **47 IntelPerson, 47 affiliations, 42 OrgFacts (MarketFocus),
42 CanonicalOrgEnrichment rows**, provider tag `PermitApplicants`, fact CreatedBy
`claude-island-permits-2026-09-04`.

⚠ `OrgFact.FactType` is constrained to RiskNote / MarketFocus / DuplicateOf /
DeltekLink / CompetitorNote / DeliveryModel / WarmChannel / SelfPerformsStructural.
"LivePipeline" was rejected; MarketFocus is used.

Re-run the standing query any time:
`docs/island-pipeline/` + the warm/cold SQL in this file's history, or:

```sql
SELECT co.Kind, co.DisplayName, co.ClendorClientId, co.KorProjectsCount,
       co.LastKorProjectAtUtc, f.Body
FROM opportunities.OrgFact f
JOIN opportunities.CanonicalOrg co ON co.Id = f.CanonicalOrgId
WHERE f.CreatedBy = 'claude-island-permits-2026-09-04'
ORDER BY co.KorProjectsCount DESC;
```

## Gap-closing pass — 2026-09-04 (commits 5e2c339a … 0ac7606d, all PUSHED)

| Gap | State |
| --- | --- |
| Vancouver permits dark since 7 Jun | **FIXED.** Adapter pages `/records` instead of downloading the 82 MB export. 50,811 → **51,896** permits, latest issued 2026-09-01, `LastErrorMessage` NULL. |
| Coquitlam applicants buried in prose | **FIXED.** Applicant takes the contact slot; contact joined the dedup hash (a duplicate hash returns before the refresh block, so 387 of 389 had been silently skipped). **2 → 391** contacts. |
| Exclusions matching street names | **FIXED.** Exclusions score against address-masked text; keep-signals still see everything. `road` rejects gone from Victoria, 179 rows re-reasoned, **0 wrongly kept**. |
| 37 of 42 firms with one contact | **FIXED.** `BdContactEnrich --roster-ingest` (new mode). 42 Hunter credits → 695 people. **37 → 11** single-contact firms; **47 → 937** people on those firms. |
| 7 orgs with no website anchor | **FIXED.** 0 remaining. |
| `org_merge_dead_survivor` ERROR | **FIXED** — was a false positive; both orgs deleted, zero references. Check now requires the loser to still exist. |
| Key prefix collisions | **FIXED going forward** without re-keying: collisions disambiguate at ingest with 6 hex of the source id, only when the key is held by a different source at a different url. The one bad row self-heals on the next bids&tenders run. |
| ~300 unpushed commits | **PUSHED.** 309 commits, clean fast-forward, `origin/develop` now level. |
| Integrity | **Errors 2 → 1**, warnings 32 → 31. |

Side effect worth knowing: Victoria went **116 → 119 opportunities**. The three
new rows are exactly what the address guard and the setback vocabulary rescued —
717 Pandora Ave, 1435 Thurlow Road, 1311 Johnson St.

### Still open, and honestly scoped

- **Saanich, Langford and Colwood are still unwired**, and they are the three
  biggest permit markets in Greater Victoria (Q1 2026 starts 195 / 171 / 143
  against the City of Victoria's 117).
  **Saanich and View Royal both run Tempest/Prospero**, and their detail pages
  parse correctly with the EXISTING extractor — verified against Saanich
  ALR00041: identical `ctl00_FeaturedContent_*` ids, identical JS-obfuscated
  applicant email. `UrlHostLike` is now path-matched so they are already covered
  for enrichment.
  **What is missing is only a LISTING source.** `Search.aspx` is ASP.NET
  WebForms: 20 folder numbers per page, paging by postback, so it needs a
  Playwright-paged provider (`PlaywrightBrowserPool` + `BcBidScraper` are the
  models). That is the single highest-value remaining build.
  Langford and Colwood run neither Tempest nor ArcGIS — platform still unknown.
- **Two permit models still coexist** — `opportunities.BuildingPermit` (Vancouver
  only, but with `EstimatedValue`, `NumberOfDwellingUnits` and three resolved org
  roles) and the Opportunities route used by the four municipal feeds. Mine has
  no value or unit count. Deciding which wins is a design call, not a bug fix.
- No dollar value or unit count on any of the four municipal feeds.
- 458 duplicate person affiliations; 470 ambiguous name clusters; 4,316
  narratives with no website anchor. All pre-existing, all WARN.

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
