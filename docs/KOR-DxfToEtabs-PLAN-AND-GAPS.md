# DXF → ETABS generator: the product, and what is not yet true of it

Part 1 states what this tool is required to be. Part 2 is an inventory of what does not yet meet
that standard, each with the measurement behind it. Neither part ranks the work — sequencing is
not a technical fact.

Updated 2026-08-15 (second pass, after an independent audit).

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
- A workbook in three sheets. Every judgement it had to make, each already decided with the
  measurement behind it: rows marked DECIDED are tied to a rule and change the model when answered,
  rows marked SCOPE record what the tool does or does not attempt at all and answering one is noted
  but changes nothing. Then every rule the model was built on, read-only. Then every location where
  a decision was applied, marked approximation or drawing limit.

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

**L1. PARTLY CLOSED 2026-08-15. Twenty-five of the thirty-two rules can be changed by an
engineer; seven decided workbook rows are still not learnable settings.**
The workbook binds eighteen rows to twenty-five settings. The remaining seven rules are
geometry-cleanup tolerances — join, bridge, wall-bridge and dash-join gaps, the extend limit, min
panel overlap, already-modelled tolerance — and asking a structural engineer to set them would be
noise. They are not hidden: a *Rules in force* sheet lists all thirty-two with value, units,
confidence, who set it and why it holds, and says which question changes each one.
`dxf.floor-from-perimeter-wall` was added as a rule in migration 039 and moved to a single-source
ruling in migration 040 rather than staying behaviour welded into the classifier.

What is still not true: seven DECIDED rows in the workbook (`C1`, `F2`, `M1`, `M2`, `O1`, `P1`,
`S2`) carry no setting key. They are visible and importable as prose rulings, but the generator
does not read them back, so an answer there does not change the next model.

**L2. Nothing re-measures the rules against the portfolio.**
The 2026-08-14 measurement of 1,126 models was a one-off run by hand. No part of the build re-runs
it, and nothing notices when a rule drifts from what engineers draw.

**L3. PARTLY CLOSED 2026-08-15. `dxf.opening-height` = 88″ has no sound basis, and now says so
where the engineer can see it.**
It is a DECIDED row (H1) carrying its derivation, so the weak basis is stated on the deliverable
rather than buried, and one cell replaces it. The measurement problem below is unchanged.
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

**C5. CLOSED 2026-08-15. Beams are out of scope, said out loud rather than by omission.**
Measured first: 31138's `JBP_S_BEAM` layer holds one entity per sheet, 25 across 25 drawings, so
these concrete-outline plans do not carry framing — the 27 beams in that engineer's own model were
drawn by her. Modelling them would mean inventing a depth as well as a position. It is now decision
M2 in the workbook, and `LayerLedger` names any unclaimed BEAM, JOIST, BRACE or TRUSS layer carrying
20 segments or more, so a job whose drawings *do* carry framing is told it was skipped instead of
reading a clean report.

**C6. DECIDED 2026-08-15, and it is the one decision most worth an engineer overturning.**
On 31168's tower floors the elements the engineer marked openings between are drawn on the column
layer — 24 footprints at 16×40, 18×45, 30×30, 24×28, none more slender than 2.5:1. Columns or
pierced wall changes in-plane shear the height of the tower. The layer governs, so they are columns;
that is the drafting convention and nothing in the drawings contradicts it. It ships as decision O1
with the footprint measurements beside it.

O1 is a SCOPE row, not a rule: it carries no setting key, so answering it is recorded as a ruling
and does not change geometry. An earlier version of this entry said disagreeing cost one cell. It
does not, and saying so was worse than leaving the gap open, because it told a reviewer to stop
looking at a modelling decision that still cannot learn. Making it learnable means a rule for
"which layer roles may face an opening", which does not exist yet.

## Shipping

**S1. PARTLY CLOSED 2026-08-15. Every job gets a document written from its own model, but the
document must not overstate learnability.**
The dossier and one-pager describe two buildings and travel only to the jobs they name — that part
stands, because a document about somebody else's tower reads authoritative and is wrong. What was
missing is now generated: `KOR-<job>-SUMMARY.pdf`, built from that job's own model and report every
time it publishes, stating what was produced and, verbatim from the run, everything it declined to
do. A third job no longer arrives as a bare `.e2k`. The summary now says only rule-backed rows can
be changed from one cell; rows without a rule key are visible scope decisions, not yet learnable
settings.

**S2. MOSTLY CLOSED 2026-08-15. The publish gate no longer names a job.**
The summary-table check reads which column belongs to which building from the table's own header
row, and the plateless-storey check reads whose storeys the dossier is listing from the sentence
that says so. Both were hardwired job numbers. What remains is the allowlist of historical figures
— numbers that appear in the prose and are true of something other than a current member count,
each carrying a written reason. That is document-specific by nature and goes with S1.

Found while doing it, and fixed: the gate matched `<number> <member>` only when adjacent, so
"315 of your columns" was invisible to it. The dossier said 315 in one sentence and 316 three lines
above, and the report says 316 — exactly the fault the gate exists to catch, sitting in the document
it was checking. Both patterns now also match "N of your/her/its/the <member>".

## Build

**B1. The test suite requires `KorStandards` to be reachable.**
Deliberate — a green suite must not be able to certify built-in values while production runs on
database values — but it is a hard dependency on KOR-APP01 for any run on any machine, and it is
the opposite of how the share-backed tests behave, which skip quietly when `\\Kor-fs01` is
unreachable.
