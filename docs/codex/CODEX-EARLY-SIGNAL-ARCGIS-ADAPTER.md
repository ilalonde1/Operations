# CODEX — Early-signal ingestion: the ArcGIS Feature Service adapter

Adversarial review requested. Built 2026-09-03. Nothing here is enabled in production yet.

## Why this exists

Every source the platform ingests today is a **tender** feed — BC Bid, Bonfire, bids&tenders, APC,
CanadaBuys, MERX. By the time a structural tender is posted the engineer has been chosen. BC's one
forward-pipeline source, the BC Stats Major Projects Inventory, was discontinued (last issue Q3
2025, page removed 30 June 2026) and its job kept reporting `Success = true` against a frozen
Q1-2025 snapshot for two months.

Municipal **development-permit and rezoning applications** are earlier than MPI ever was. They name
the site, the purpose, the city file number and — in one of the three cities wired up here — the
applicant. Most BC municipalities publish them through ArcGIS Hub / ArcGIS Open Data, which is one
REST shape. So this is **one adapter for the platform**, and a new city is a config row.

## What was built

| Thing | Path |
| --- | --- |
| Provider | `Kor.Opportunities.Data/Ingestion/Providers/ArcGisFeatureOpportunityProvider.cs` |
| Enum value | `OpportunitySourceType.ArcGisFeatureService = 20` |
| DI registration | `Kor.Opportunities.Worker/Program.cs` (typed `HttpClient` + retry policy) |
| Tests | `Kor.Opportunities.Data.Tests/ArcGisFeatureOpportunityProviderTests.cs` (10, all passing) |
| Probe | `tools/ArcGisProbe` — runs the adapter against a live layer without the Worker |
| Seeds | `Kor.Opportunities.Data/Schema/298_…sql`, `299_…sql` (both applied, **sources disabled**) |

`tools/ArcGisProbe --source <Name>` reads the **real DB config** and prints the row→application
collapse, the relevance-gate verdict for every application, and the newest kept ones. That is how
each of the three cities below was proved before it was written into a migration, and it is how the
next one should be.

## Four things ArcGIS does differently, each of which fails silently

1. **Rows are `features[].attributes`**, not a flat array.
2. **An ArcGIS error is HTTP 200** with an `error` object in the body. It will not surface as a
   failed request.
3. **Dates are epoch milliseconds.** Read as anything else they become plausible wrong dates.
4. **One application is many rows.** These are spatial layers: a rezoning spanning nine parcels is
   nine features with identical attributes and different geometry. **Victoria's 258 rows are 146
   applications.** Ingested raw, one tower project files nine times. The adapter collapses on the
   application number and merges the addresses.

There is also the trap that a config row can point at the wrong kind of layer: most datasets named
"Development Permit **Area**" are static zoning overlays, not applications. They return perfectly
valid features and would ingest cleanly as nonsense. Abbotsford's
`Development_Layers_External_Feature/6` is exactly this and was rejected for it — it carries a file
number and a GIS edit timestamp, and no application date, type or description at all.

## What is wired, with the numbers the probe actually produced (2026-09-03)

| Source | Rows → applications | Date range | Gate keeps | Note |
| --- | --- | --- | --- | --- |
| `Victoria_DevelopmentApplications` | 258 → **146** | 2007-06-08 → 2026-08-27 | 104 of 146 | Per-application permalink on the city's Prospero tracker |
| `Coquitlam_DevelopmentApplications` | 484 → **483** | 2006-01-09 → **2026-09-01** | 384 of 483 | **Carries `APPLICANT`** — the developer by name |
| `MapleRidge_DevelopmentApplications` | 886 → **849** (11 rows have no file number) | 2009-07-30 → 2026-08-27 | 39 of 849 | Thin: no project prose, see the finding below |

Coquitlam's newest rows on 2026-09-01 include *"Applicant: Ledingham McAllister. Project Proposal:
To construct three, 6-7 storey wood frame buildings…"*. No tender feed we ingest carries that.

All three are seeded **`IsEnabled = 0`**: the running Worker binary does not have SourceType 20, and
enabling a source whose provider is missing produces failed runs. They are enabled after the deploy.

`tools/BdIntegrityCheck` before and after applying both migrations: **Errors 1, Warnings 30,
CheckFailures 0** — identical, byte for byte on the `[WARN]`/`[ERROR]` lines.

## FINDING — the relevance gate is tuned for tender prose and drops planning prose

This is the real result of the exercise and it is **not fixed**, deliberately: `StructuralRelevanceGate`
is shared by every source, so widening it changes what BC Bid and Bonfire ingest too. That needs its
own differential harness, not a quiet vocabulary edit.

Counted, not sampled — **42 of Victoria's 146** applications are dropped. Reading all 42:

- **13 are unambiguous building projects.** Among them: a *six-storey hotel with attached
  restaurant*; a *six storey, 42-unit purpose-built rental development* (dropped twice, once as the
  rezoning and once as the development permit); a *6 storey 51 unit multi-family development*; a
  *3-storey, 3-unit houseplex*; two more hotels; *move a heritage-designated principal dwelling*;
  and *relocate Heidelberg Material industrial operations*.
- ~5 more are marginal (a new deck, a dormer height, rooftop mechanical).
- The rest are correctly dropped: sign variances, fences, barbed wire, an awning, a cannabis retail
  use change, heritage tax-incentive paperwork, city-initiated zoning housekeeping.

**Two distinct causes:**

1. **Missing vocabulary.** The gate's building signals are tender words — "construction",
   "renovation", "building". Planning applications say *"six-storey"*, *"42-unit"*, *"multi-family
   development"*, *"townhouse"*, *"houseplex"*, *"hotel"*, *"mixed-use"*. Maple Ridge is the extreme
   case: its only project signal is a `WorkProposed` land-use word (*"Residential or Mixed-Use"*,
   *"Institutional"*), and **810 of its 849** applications are dropped as a result.
2. **Exclusion terms matching inside an address.** Three Victoria applications were rejected for
   `road` — *645/55 Tyee **Road***, *1435 Thurlow **Road***, *414 Craigflower **Road***. The word is
   in the street name, not the scope. One of the three is a genuine building job (height, interior
   setback and gross-floor-area variances).

Cause 2 is **the second instance of a known class in this codebase**. The first is recorded in
`Kor.Opportunities.Data/CLAUDE.md`: resolving orgs by substring made "Chard" match *Richard & Co.
Architecture* and "Seba" match *Sebastien Garon*. Per root rule 11, the class in one sentence:

> **A keyword test run against a concatenation of fields will match a term that occurs only inside a
> proper noun — a street name, a company name — and act on it as though it were about the subject.**

The check that would have caught both is a differential: run the gate over the existing corpus with
the exclusion list applied to the description **only** versus title+description, and diff the
verdicts. Any row whose verdict flips on a term that appears solely in its address or buyer name is
an instance. That harness does not exist yet; it is the prerequisite for touching the gate.

## What these tests cover, and what they do not

`ArcGisFeatureOpportunityProviderTests` covers: the features/attributes envelope; epoch-millisecond
conversion and the earliest-date rule; rejection of a number that is not a timestamp; paging driven
by `exceededTransferLimit` with offset advance; page size capped to the layer's own
`maxRecordCount`; an ArcGIS error body returned as HTTP 200; the status filter; rows missing an id
or title; the applicant prefix; and multi-field description composition.

It does **not** cover anything downstream of the provider — the relevance gate, `OpportunityKey`
composition, dedup against existing rows, or `CanonicalOrg` resolution. **A same-class fault it
would not catch:** a config row pointed at a zoning-overlay layer. That is only visible by looking
at what the source delivered, which is what `tools/ArcGisProbe` and `BdIntegrityCheck`'s
`source_everything_filtered_out` are for.

## Attack these

1. **Paging.** `exceededTransferLimit` is trusted as the only "there is more" signal, and the loop
   stops at `arcgis.maxPagesPerRun` (10). Is there a layer shape where that silently truncates
   without the partial-read warning firing? Note the run-timeout scar: many small pages is what got
   a GovCanada backfill cancelled, so the fix is not "more, smaller pages".
2. **The collapse.** Scalars take the first non-empty value across a fan-out of rows, the date takes
   the earliest, addresses take the union. Is first-wins wrong for any field where the parcel rows
   genuinely differ? Victoria's duplicates are identical apart from parcel identity — is that true
   of Coquitlam and Maple Ridge, and would we know if it stopped being true?
3. **`ExternalReference` stability.** `OpportunityKey` is composed from it. Do these file numbers
   ever get reused or renumbered by the city? A reused number would merge two unrelated projects —
   the same one-row-one-real-thing violation that produced the Continuum defect.
4. **The `{ref}` URL template.** It is substituted with `Uri.EscapeDataString`. Any injection or
   mis-encoding path?
5. **Freshness.** These sources are covered by `source_went_silent`,
   `source_never_delivered_anything` and `source_everything_filtered_out` on day one — but Maple
   Ridge keeps only 39 of 849. Will `source_everything_filtered_out` fire meaningfully, or does a
   heavily-but-not-totally-filtered source slip between the checks?

## Constraints

- No build, no test runs, no destructive operations.
- Do not "fix" the relevance gate here. The finding above says why, and what has to exist first.
- The three sources stay `IsEnabled = 0` until the Worker carrying SourceType 20 is deployed.
