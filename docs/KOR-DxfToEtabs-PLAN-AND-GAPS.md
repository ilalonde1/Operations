# DXF → ETABS generator: the product, and what is not yet true of it

Part 1 states what this tool is required to be. Part 2 is an inventory of what does not yet meet
that standard, each with the measurement behind it. Neither part ranks the work — sequencing is
not a technical fact.

Updated 2026-08-14.

---

# PART 1 — THE PRODUCT

## What it is for

A structural engineer points it at any job and gets back an ETABS model with the building's
geometry already entered, so the hours go into engineering instead of typing. **Any** job — not a
rescue built around particular buildings.

## What it is given

1. The folder of plan DXFs drafting already exports for that job.
2. Any ETABS file from that job — even an empty shell carrying only the storey list.

Nothing made specially for it. No per-job configuration.

## What it returns

- An ETABS `.e2k` carrying the geometry read from the drawings: walls at true thickness on their
  centrelines and connected, columns sized and oriented as drawn, floor plates, headers over
  openings, shaft and stair openings cut, pier labels.
- A report stating, location by location, everything it could not do.
- A workbook of the questions it could not decide, for the engineer to answer in a column.

## The three properties that make it a tool

**Agnostic.** Nothing in it knows about any particular building. No job numbers, no per-project
branches, no value tuned so that a specific model comes out right.

**Honest.** Anything read and not modelled, discarded, skipped, deduplicated or fallen back on
appears in the report. A correct decision made quietly is indistinguishable from a bug.

**Learning.** An engineer's answer becomes a rule that applies to every job afterwards, with no
code change and without her being asked twice. Rules and their evidence live in `KorStandards`;
the code is the machine that applies them. Every rule is measured against the portfolio of models
KOR engineers have actually built before it is trusted.

## What it must not do

No loads, diaphragms, stiffness modifiers, section properties, meshing or design. It must never
overwrite or duplicate geometry the engineer has already modelled.

## Where things are

| | |
|---|---|
| Engine | `Kor.Operations.EngineeringTools.Core/Dxf/` |
| Tests | `Kor.Operations.EngineeringTools.Core.Tests/` |
| CLI | `Kor.Operations.EngineeringTools.TakeoffCli/` |
| Publish | `tools/Publish-EtabsModel.ps1` |
| Shipped documents | `docs/KOR-DxfToEtabs-*.html` |
| Rules and evidence | `KorStandards` on `KOR-APP01\SQLEXPRESS`, schema `analysis` |
| Rule contract | `analysis.vw_RuleSetting` — key, value, units, confidence, authority |
| Migrations | `C:\VIsual Studio Projects\KOR.Drafter\db\` |

The rule loop: `takeoff dxf-to-etabs … --questions q.xlsx` → engineer answers the `YOUR ANSWER`
column → `takeoff dxf-import-rules q.xlsx --engineer <name>` → generate again. No code edit is part
of that cycle.

Two jobs have been run through it, 31168 and 31138, each folder holding the generated `.e2k`, the
report, the questions workbook and the drawings under `_DXF-plans-for-rebuild`. Roughly fourteen
hundred further ETABS models sit elsewhere under `\\Kor-fs01\Projects\Projects`. Some files in the
two job folders are this tool's own output round-tripped through ETABS; objects named `K` followed
by a letter and digits were written by this tool.

---

# PART 2 — WHAT IS NOT YET TRUE

## Learning

**L1. Ten of the thirty-one rules can be changed by an engineer; twenty-one cannot.**
The workbook binds eight questions to ten settings. The rest can only be moved by writing a
migration. Of those twenty-one, roughly half are engineering judgement — min and max column size,
the opening-span window that decides what gap is a doorway, max pier thickness, min wall thickness,
unusual wall thickness, min slab area, default slab thickness, pier fill ratio, min panel aspect —
and roughly half are CAD tolerances that arguably should not be engineer-facing.

**L2. Nothing re-measures the rules against the portfolio.**
The 2026-08-14 measurement of 1,126 models was a one-off run by hand. No part of the build re-runs
it, and nothing notices when a rule drifts from what engineers draw.

**L3. `dxf.opening-height` = 88″ has no sound basis.**
It was derived by reading the third value on a `POINT` line as a spandrel depth. Across 41,838 such
values the quantity is an elevation above the storey base, not a member depth. The proper source is
the `$ PIER/SPANDREL NAMES` block, which is empty in most models — so it may not be derivable from
the corpus at all, which is itself an answer someone needs to give. The 18″–60″ clamp is
unaffected; that is the engineer's own answer and stands on her authority.

## Correctness

**C1. Nobody has imported a generated model into ETABS.**
The largest untested surface. Every fault found so far came from an engineer opening one. The
construct most worth watching is the multi-storey panel span: portfolio measurement confirms
`n = 2` is real but very rare — 51 panels of 126,308, in 3 models of 1,037 — and `n ≥ 3` has never
been drawn by anyone. A reviewer will not recognise it on sight.

**C2. Panels of the form `1 0 0 1` are not handled.**
136 in the portfolio — a skewed panel whose corners sit at different storeys. The reader assumes
the first two integers are equal, so a reference model containing them will misparse.

**C3. Slab plates are the weak half.**
Slab edges frequently do not close: 110 unmatched endpoint bins on 31168 B-L28, 73 on C-L3, 567 on
Level 1 Mezz. Six 31168 storeys carry members with no plate. Building C's roof carries a plate with
no vertical structure on its own storey.

**C4. Round columns are recognised only from arc provenance.**
A drafter who draws a circle as a polyline gets a square column, and nothing says so. Whether that
occurs in the corpus is unmeasured.

**C5. Beams are not modelled at all.**
There is no `LINE … BEAM` path. Whether that is in scope has never been asked.

**C6. O1 is unanswered.**
On 31168's tower floors the elements the engineer marked openings between are drawn on the column
layer — 24 footprints at 16×40, 18×45, 30×30, 24×28, none more slender than 2.5:1. Columns or
pierced wall changes in-plane shear the height of the tower, and only she can say which.

## Shipping

**S1. The dossier and one-pager describe two buildings.**
They no longer travel to a job they do not name, so a third job now receives no document at all.
Either they are generated per job from that job's report, or the decision to ship without them is
made explicitly.

**S2. The publish count gate still carries two-job scaffolding.**
An allowlist of historical figures from 31168 and 31138, a positional check that assumes the left
column of the summary table is 31168 and the right is 31138, and a `$Project -eq '31168'` branch.
All of it exists to validate a two-job document and would go with S1.

## Build

**B1. The test suite requires `KorStandards` to be reachable.**
Deliberate — a green suite must not be able to certify built-in values while production runs on
database values — but it is a hard dependency on KOR-APP01 for any run on any machine, and it is
the opposite of how the share-backed tests behave, which skip quietly when `\\Kor-fs01` is
unreachable.
