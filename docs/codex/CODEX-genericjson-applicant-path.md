# CODEX — GenericJson ingests an application with nobody's name on it

**One defect. Do not fix anything else. Do not refactor the provider. Do not wire any
new sources — that is a separate, config-only step that waits on this.**

## The defect

`Kor.Opportunities.Data/Ingestion/Providers/GenericJsonOpportunityProvider.cs` exposes
sixteen configurable paths — `json.titlePath`, `json.buyerPath`, `json.descriptionPath`,
`json.postedDatePath`, `json.deadlinePath`, `json.externalReferencePath`,
`json.estimatedValuePath`, the three `*Override` keys and the rest (declared around
lines 521-536).

**There is no applicant path, and the provider never sets `BuyerContactName` at all.**
Confirmed by grep: the identifier does not appear in the file.

`ArcGisFeatureOpportunityProvider` already does this, and is the pattern to follow —
line 372:

```csharp
BuyerContactName = Trim(ContactName ?? Applicant, 200),
```

with the source field named by `arcgis.applicantField` and additionally prefixed into the
description (`"Applicant: {Applicant}. " + description`, around line 349-351).

## Why it matters, measured

An application that names nobody is a permit notice. An application that names the
developer or architect is a relationship lead — the entire purpose of the early-signal
layer (`Kor.Opportunities.Data/CLAUDE.md`: *"Do not treat tenders as pipeline"*).

Across the 3,864 development applications ingested in the last 90 days, **1,645 (43%)
carry an applicant** and the rest do not — and for the BC ArcGIS sources that is a ceiling
imposed by the publishers: Surrey publishes 8 fields and none is an applicant.

**Calgary does publish it.** Queried live 2026-10-08:

| Dataset | Rows | Updated | Has `applicant` |
|---|---|---|---|
| `data.calgary.ca/resource/6933-unw5.json` (Development Permits) | 193,127 | 2026-10-08 | yes |
| `data.calgary.ca/resource/33vi-ew4s.json` (Land Use Redesignation) | 8,998 | 2026-10-08 | yes |

Those are Socrata JSON endpoints, which `GenericJsonOpportunityProvider` is otherwise
able to read with config alone. Without an applicant path, wiring Calgary produces 200,000
rows of address-and-description with no name attached — the low-value shape, from a
publisher that offers the high-value one.

## The change

Add `json.applicantPath`, read like the other paths, and map it to `BuyerContactName`
exactly as the ArcGIS provider does. Trim to the same 200 characters.

Follow the ArcGIS precedent for the description prefix too, **only if** it can be done
without disturbing existing `json.descriptionPath` behaviour for the sources already using
this provider. If that is not clean, map to `BuyerContactName` only and say so — the field
is what matters.

## The rule that must hold afterwards

**A source that configures `json.applicantPath` gets the applicant on the opportunity; a
source that does not is completely unaffected.** This provider is already in production
use, so the default path — no `json.applicantPath` configured — must behave byte-identically
to today.

## Out of scope, explicitly

- Wiring Calgary, Edmonton or Austin. Those are `OpportunitySources` rows plus `json.*`
  mappings, seeded `IsEnabled = 0` and enabled after a Worker deploy, per the module rule.
  They wait for this change and are not part of it.
- `ArcGisFeatureOpportunityProvider`. It already does this correctly.
- The 2,219 BC applications with no applicant. Their publishers do not provide one; no
  code change reaches them.

## Shape of the expected answer

The added config path, the mapping, and a test proving both arms: a source **with**
`json.applicantPath` populates `BuyerContactName`, and a source **without** it produces a
candidate identical to today's. State the test names.

## Reproduce

```bash
# Calgary carries the field; confirm before and after
curl -s "https://data.calgary.ca/resource/6933-unw5.json?\$limit=1" | python -c "import sys,json;print(sorted(json.load(sys.stdin)[0].keys()))"
```

```sql
-- current applicant coverage across the early-signal layer
WITH e AS (
  SELECT DISTINCT o.Id, o.BuyerContactName
  FROM opportunities.Opportunities o
  JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
  JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
  WHERE s.SourceType IN (20, 21, 22) AND ob.IngestedAtUtc >= DATEADD(day, -90, sysdatetimeoffset()))
SELECT COUNT(*) AS Applications,
       SUM(CASE WHEN ISNULL(BuyerContactName, '') <> '' THEN 1 ELSE 0 END) AS Named
FROM e;
```

Full context: `docs/KOR-EarlySignal-Wiring-Plan-2026-10-08.md`.
