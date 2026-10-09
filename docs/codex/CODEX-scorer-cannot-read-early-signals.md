# CODEX — the scorer ranks 5,971 early signals on their title alone, in tender vocabulary

**One defect. Do not touch `StructuralRelevanceGate` — it is correct and was changed twice
this month. Do not change the hub, the segments, or anything shipped in
`CODEX-bd-hub-calls-early-signal-tenders`. Do not wire a source. Do not change ingestion
behaviour beyond what is named below.**

## The defect, measured 2026-10-09

Average `RelevanceScore` by early-signal source, live:

| Source | Rows | Avg score | Max |
|---|---:|---:|---:|
| PortMoody_DevelopmentApplications | 17 | 18.7 | 24 |
| Vancouver_DevelopmentApplications | 103 | 18.1 | 26 |
| Saanich_DevelopmentApplications | 2,081 | 11.9 | 26 |
| Esquimalt_DevelopmentApplications | 29 | 8.3 | 13 |
| **Calgary_LandUseRedesignation** | **864** | **3.0** | **9** |

**Calgary is the richest early-signal feed in the system — 82% of its rows name an
applicant, against 43% across the whole layer, and they are multi-residential
high-density rezonings — and it scores LAST, below Saanich single-family building
permits.** On any score-sorted view the whole of Alberta sits at the bottom of 8,836 rows.

This is new damage from a correct change. Commit `8c3d80a1` taught the **gate** Calgary's
land-use district codes (6 of 400 kept → 238). The rows now arrive. **Nothing taught the
scorer**, so they arrive and rank last, which for a radar is the same as not arriving.

## Why — two coupled causes, both verified

**1. The scorer structurally cannot see the description.**
`RuleBasedOpportunityScoringService.Explain` builds its text from exactly eight fields
(lines 39-49): `Name`, `BuyerName`, `ProjectAddress`, `ProjectCity`, `ProjectProvince`,
`ConstructionType`, `ProjectCategory`, `OutcomeReason`. **There is no `Description`** — and
there cannot be, because `opportunities.Opportunities` **has no Description column**. The
text that describes the work lives on `opportunities.OpportunityObservations.Description`,
which is what the GATE reads and the SCORER never sees.

And for the early-signal layer the other fields are empty. Measured:

| Source | Rows | ProjectAddress | ConstructionType | ProjectCategory | Observation Description |
|---|---:|---:|---:|---:|---:|
| Calgary | 864 | **0** | **0** | **0** | **864** |
| Saanich | 1,059 | **0** | **0** | **0** | 2,079 |
| Esquimalt | 29 | **0** | **0** | **0** | **29** |

So the scorer is ranking these rows on `Name` + `BuyerName` + city/province. **The title,
and nothing else.**

**2. The vocabulary is written for tenders.** `opportunities.ScoringProfile` has **0 rows**,
so the live scorer runs on the hardcoded defaults in `ScoringOptions.cs:79-110`. Those are
procurement words: *structural engineering, engineer of record, EOR, schedule B, schedule
C-B, letter of assurance, field review, seismic retrofit, mass timber, CLT, post-tensioned,
cast-in-place.* Every one belongs in an RFP **for structural services**. None appears in a
development application. A rezoning does not say "engineer of record" — that artefact does
not exist yet, which is the entire reason an early signal is early.

That is the same fault the gate had and had fixed on 2026-09-03/04 when planning vocabulary
was added for BC, and again on 2026-10-08 for Calgary. **The scorer was never part of either
pass.** Read `project_relevance_gate_planning_vocabulary` and commit `8c3d80a1` for what
that vocabulary work looked like.

## ⛔ The harness comes first, and it does not exist

`tools/RelevanceGateDiff` exists because a gate change silently alters what five platforms
ingest. **There is no equivalent for scoring** — `tools/` has nothing. A scoring change
reorders what every user sees and nothing in the system would say so.

Root `CLAUDE.md` rule 9 is explicit: *"Before the first fix: ground truth, a harness, and a
picture… One command measures EVERY deliverable."* **Build `tools/ScoringDiff` before
changing a single term**, modelled on `RelevanceGateDiff`: score the live corpus with the
current rules and the proposed rules, and report the movement — per source, per kind, and
the rows whose **tier** changes, which is what a user actually sees.

Report gains AND regressions. A term that lifts Calgary and also lifts 2,000 Saanich
single-family permits has achieved nothing; the measure is whether the ORDER improves, not
whether scores rise.

## The change

1. **`tools/ScoringDiff` first**, with a committed before-reading.
2. **Give the scorer the description.** It scores an `Opportunity`; the text is on the
   newest observation. Say explicitly how you got it there and what it costs — a per-row
   query inside a scoring loop over 8,836 rows is not acceptable (root rule 4: push the
   filter to the source).
3. **Teach it planning vocabulary**, using the gate's own list as the starting point rather
   than inventing one — `StructuralRelevanceGate.BuildingSignals` already holds the terms
   this practice decided on twice, including Calgary's district codes.
4. **Re-score the existing rows.** Scoring happens at ingest only
   (`IngestionService.cs:331,389`); there is no bulk re-score path anywhere. Without one,
   nothing changes for the 8,836 rows already stored and the fix is invisible. State how
   you re-scored and how many rows moved.

## ⚠ The trap to avoid, which this repo has paid for twice

**Do not make the single-family flood score higher.** Saanich's 2,081 rows are mostly
`RESIDENTIAL BUILDING PERMIT — <address>`, and they already out-score Calgary because
"residential" is a word the scorer likes. Adding planning terms naively will lift them too.

The Island solved the equivalent problem at the READ layer, not in the scorer:
`opportunities.vIslandApplications` (migration 316) exposes an `IsBigTicket` bit over one
shared predicate — written once precisely because the same filter was written twice in a
session and the two versions disagreed by 26 rows, one of which shipped in a dossier. **If
your answer needs a "this is small" judgement, say so and leave it to that layer; do not
build a second one here.**

## The rule that must hold afterwards

**A development application that names a multi-residential or mixed-use form must not rank
below a single-family building permit.** Prove it from `ScoringDiff` output on the live
corpus, not by reasoning, and state the before/after position of Calgary against Saanich.

And the invariant: **no row's tier may change without the harness showing it.**

## ⚠ Required in the answer, per rule 11

State in `ScoringDiff`'s own header and in your reply: the properties it compares, the
properties it does not, and **at least one same-class fault it would not catch**. A
starting example to improve on, not copy: it compares scores on the corpus as it stands, so
it cannot see a feed whose descriptions are absent — Sooke publishes applications with an
empty description and would score identically before and after any vocabulary change.

## Shape of the expected answer

- `ScoringDiff` output: movement per source and per kind, tier changes counted both ways.
- The before/after table for the five sources above.
- How the description reaches the scorer, and the cost of doing so.
- The terms added, and which came from the gate rather than invention.
- The re-score: how, and how many rows moved tier.
- Test names. `dotnet test --filter "Speed!=Slow"` on `Kor.Opportunities.Data.Tests` is the
  fast gate (231 tests, ~5s); the full suite is owed only on geometry work, which this is
  not.

## Reproduce

```sql
-- the headline
SELECT s.Name, COUNT_BIG(DISTINCT o.Id) AS Rows_,
       AVG(CAST(o.RelevanceScore AS float)) AS AvgScore, MAX(o.RelevanceScore) AS MaxScore
FROM opportunities.Opportunities o
JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
WHERE o.DismissedAtUtc IS NULL AND s.SourceType IN (2, 20, 21, 22)
GROUP BY s.Name ORDER BY AVG(CAST(o.RelevanceScore AS float));

-- and the reason: the fields the scorer reads are empty, the one it cannot read is full
SELECT s.Name, COUNT_BIG(DISTINCT o.Id) AS Rows_,
       SUM(CASE WHEN ISNULL(o.ProjectAddress, '') <> '' THEN 1 ELSE 0 END)  AS HasAddr,
       SUM(CASE WHEN ISNULL(o.ConstructionType, '') <> '' THEN 1 ELSE 0 END) AS HasType,
       SUM(CASE WHEN ISNULL(ob.Description, '') <> '' THEN 1 ELSE 0 END)     AS ObsHasDescription
FROM opportunities.Opportunities o
JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId = o.Id
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
WHERE o.DismissedAtUtc IS NULL AND s.Name IN
      ('Calgary_LandUseRedesignation','Saanich_DevelopmentApplications',
       'Esquimalt_DevelopmentApplications')
GROUP BY s.Name;
```

## Out of scope, explicitly

- **`StructuralRelevanceGate`.** Correct, and changed twice this month. The scorer must
  borrow its vocabulary, not alter it.
- **The big-ticket / single-family filter.** Real, and the next brief; `vIslandApplications`
  is the precedent and it belongs at the read layer.
- **`ScoringProfile` as a UI feature.** The table is empty and `ScoringProfileWindow` exists;
  whether operators should edit terms is a separate question. Change the defaults.
- **Tender scoring.** The 2,865 Bids rows are ranked acceptably by the current vocabulary,
  which was written for them. Do not regress them — that is what the harness is for.
- **MPI / Future Projects**, the hub, and anything in the FIND/PURSUE/KNOW/TOOLS rail.
