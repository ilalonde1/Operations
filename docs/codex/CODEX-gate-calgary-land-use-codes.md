# CODEX — the relevance gate cannot read Calgary, and drops 91% of its rezonings

**One defect. Do not fix anything else. Do not wire any source. Do not touch Edmonton —
it has the opposite problem and a separate fix.**

## The defect, measured 2026-10-08

Calgary's Land Use Redesignation feed (`data.calgary.ca/resource/33vi-ew4s.json`, 8,998
rows, updated daily) was scored against the **real** `StructuralRelevanceGate` vocabulary,
extracted from the C# source rather than remembered:

```
400 most recent redesignations
  KEPT     36   ( 9%)
  DROPPED 364   (91%)  "no building/structural/design signal"
```

Everything dropped looks like this:

```
Land Use Amendment to accommodate M-C1      dropped
Land Use Amendment to accommodate M-H1      dropped
Land Use Amendment to accommodate R-CG      dropped
Land Use Amendment to accommodate MU-1      dropped
```

`M-H1` is multi-residential high density. `MU-1` is mixed use. `R-CG` is grade-oriented
infill housing. Every one is a real rezoning to a buildable form, filed by a named planning
firm (O2 Planning and Design, Sparks Planning Group), and the applicant is published.

**The gate is not wrong, it is unilingual.** Its planning vocabulary was added 2026-09-03/04
for BC, which writes applications in prose — *storey*, *multi-family*, *floor area ratio*,
*townhouse*, *setback*. Calgary writes district codes.

Left as is, Calgary would be wired, run green forever, insert almost nothing, and surface
months later as `source_everything_filtered_out` — which the module guidance correctly calls
*usually correct*, so it would read as "Calgary doesn't publish much."

## What to add, and what NOT to add

Calgary's recent 600 coded redesignations, by family:

| Family | Recent count | Take? |
|---|---:|---|
| Multi-residential `M-*`, `H-GO` | 165 | **yes** |
| Mixed use `MU-*` | 104 | **yes** |
| Industrial `I-*` | 53 | **yes** |
| Commercial `C-*`, `CC-*` | 58 | **yes** |
| Direct control `DC` | 40 | **yes** — site-specific, usually the large ones |
| Special purpose `S-*` | 34 | **yes** |
| **Low-density residential `R-C1`, `R-C1s`, `R-C2`, `R-1s`** | **92** | **⛔ NO** |

⛔ **Do not add the low-density residential codes.** They are Calgary's single-family flood
and importing them recreates, in Alberta, the exact problem Edmonton has (344 of 400 kept,
222 of them single detached houses). `R-CG` is the one R-code worth arguing about — it is
grade-oriented infill, i.e. rowhouse form — and the harness should decide it, not a guess.

## ⚠ The ceiling on this fix, which must be stated in the result

The gate reads **title + description**. Calgary's district code is a separate field
(`proposedlud`), and **it appears in the description text on only 388 of 600 recent coded
rows (65%)**. So vocabulary alone cannot reach the other 35%.

Those need the ingest to compose the code into the description — the way
`ArcGisFeatureOpportunityProvider` prefixes `"Applicant: {X}. "`. **That is out of scope
here** and will be handled separately. Do not attempt it. Do report the measured ceiling.

## ⛔ The harness is mandatory

`tools/RelevanceGateDiff` exists for exactly this, and says so in its own header: *"Adding a
word to it changes what BC Bid, Bonfire, bids&tenders, APC and CanadaBuys ingest, and nothing
in the system would say so."* Run it. Report both arms: gains, and **regressions must be
zero**.

The gate's own comments record what this harness has already killed: `"retail"` (16 commodity
hits — lottery terminals, shelving, packaging), `"restaurant"` (golf-clubhouse concessions),
and `"triplex"` (a triplex mower is a golf-course machine). **Short codes are precisely that
risk.** `DC`, `I-B`, `I-C`, `R-G`, `S-CI` are two to four characters and will collide with
ordinary prose unless matched on word boundaries. The gate already builds word-boundary
regexes for its hard-irrelevant terms; use the same discipline, and drop any term the harness
shows regressing.

## The rule that must hold afterwards

**A Calgary land-use redesignation to a non-residential or multi-residential district is kept;
nothing that was kept before is lost; and no low-density single-family rezoning is newly
admitted.** Prove all three from the harness output, not by reasoning.

## Shape of the expected answer

The term list actually shipped, with any candidate the harness rejected and why, plus:

- Calgary keep-rate before and after on the same 400-row sample (currently 36).
- `RelevanceGateDiff` gains and regressions across the existing corpus.
- The measured ceiling — what fraction of Calgary rows the change cannot reach because the
  code is absent from the description.

## Reproduce

```bash
# the sample this brief measured
curl -s "https://data.calgary.ca/resource/33vi-ew4s.json?\$limit=400&\$order=applieddate%20DESC"
```

Vocabulary lives in `Kor.Opportunities.Core/Ingestion/StructuralRelevanceGate.cs`
(`BuildingSignals`, around the planning block added 2026-09-03). Rejects land in
`opportunities.RelevanceGateRejects` with the full record.

## Out of scope

- Wiring Calgary. Config rows seeded `IsEnabled = 0`, enabled after a Worker deploy.
- Edmonton. The gate already keeps 86% of its permits; its problem is a single-family flood,
  and the fix is the `IsBigTicket` classification pattern already used by
  `opportunities.vIslandApplications` — at the read layer, not the gate.
- Composing `proposedlud` into the description for the 35%.
