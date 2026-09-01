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
| Publish | `takeoff publish` |
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

**L1. PARTLY CLOSED. Twenty-eight of the thirty-five rules can be changed by an engineer; seven
decided workbook rows are still not learnable settings.**
All thirty-five rules live in `KorStandards` — none is a constant in code, and none is missing from
the database. Twenty-eight of them have a workbook row an engineer can answer. The other seven are
geometry-cleanup tolerances — join, bridge, wall-bridge and dash-join gaps, the extend limit, min
panel overlap, already-modelled tolerance — and asking a structural engineer to set a dash-join gap
would be noise, so no question is put in front of her. They are not hidden either: a *Rules in
force* sheet lists all thirty-five with value, units, confidence, who set it and why it holds, and
says which question changes each one.

The three added last are the layer-name patterns (migration 041), which decide what this tool
considers structure at all and were C# constants until then.
`dxf.floor-from-perimeter-wall` was added as a rule in migration 039 and moved to a single-source
ruling in migration 040 rather than staying behaviour welded into the classifier.

These counts are checked against the database and the generated workbook, not asserted: an earlier
edit to this entry silently failed to apply because the text it targeted had already been reworded,
and the stale numbers survived a round of review.

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

---

# PART 3 — THE ENGINEER'S FEEDBACK, 31 AUGUST, AND WHAT MEASUREMENT FOUND

She ran the model she was sent and gave nine points. Each is recorded with the measurement behind
it rather than the impression, because three of them had already been "answered" wrongly from a
plausible guess.

## Fixed

**A1. The model was north-south against her east-west grid.** `GridAlignment` solves rotation from
the grid-line SPACINGS, which are a fingerprint: 93.4, 141.2, 326, 326, 326, 287, 39, 175… appear
once, reversed. 19/19 X and 2/2 Y matched at 90°. It refuses rather than guesses where there is too
little grid to be sure.

**A2. C-LEVEL 3's outer edge, 12,862 → 22,663 sq ft.** The ring closes exactly, through two
segments on `JBP_C_B_STRUCT` — a layer a banked ruling excludes from structure. A banked rule with
no scope deleted a piece of a real building, and nothing in the report said so.

**A3. "This at L2" — a stair-stepped slab edge.** Flood fill rasterises at `MinPanelOverlap/2` = 6 in
while straightening ran at `RecoveredOutlineTolerance` = 3 in, HALF the cell, so it could only
preserve the raster's steps. Now `max(rule, pixelSize × 1.5)`. LEVEL 2 went 114 → 24 points, L1
58 → 26, areas moved under 0.03%. Blocking invariant `outline-is-a-raster-staircase` added.
⚠ The bounding box was right the whole time. Counting could not see it; she sent a picture.

**A4. Slab property names.** Ships `KOR-S7-30MPa`, `KOR-S9-30MPa`, `KOR-W12-65MPa`.
⛔ **"Slab strength is in no DXF" was true and useless.** Every `MPa` string in all 139 sheets is a
wall type, so the question went to the engineer twice. It is in HER REFERENCE MODEL, which this tool
opens on every run: `SHELLPROP "Rvt-Floor0" MATERIAL "30 MPa Floor"`, `"25 MPa Footings"`,
`"65 MPa Walls"`. The material was already carried onto every property written; only the NAME threw
it away. **Before declaring anything unobtainable, grep the reference model.**

**A5. "At L1 it is going past the basement walls."** Her `LEVEL 1` carried **73,776 sq ft** —
4050 × 2856 in, the entire site podium — in a model of a building whose own floors are 14,988.

Not a tagging fault and not the building cut failing. Building C's ground floor is drafted as a
HALF-SHEET, and the report already said what happened:

> `S2.10.1_1_LEVEL 1 PLAN - CONCRETE OUTLINE - BLDG C.dxf` **+**
> `S2.11.1_1_LEVEL 1 PLAN - CONCRETE OUTLINE - WEST.dxf` carry the same match line and were read as
> ONE plan. … `- BLDG C.dxf`: one floor plate was recovered by flood-filling — 73,776 sq ft.

Rejoined with the WEST half the fill recovers the whole site, and the group LEADER is the BLDG C
sheet — so the site is stamped building C's and the cut keeps it by its own correct rule.

⭐ **The general rule this establishes: "compose the site once, cut after" holds for MEMBERS and not
for JOINS.** Every member carries the sheet it was drawn on, so a cut can put it back. A plate
recovered by flood fill across a seam is one ring over both halves with nothing left in it to say
where one half ended — a join is not reversible by a later cut. So a half-sheet naming another
building is no longer joined into a model being cut to this one. Narrow on purpose: a sheet naming
NOBODY still joins, because the parkade is drafted once for the site and dropping untagged geometry
cost the YMCA 66 walls and 108 columns the first time it was tried.

Measured, not assumed: read alone, C's half recovers 11,026 sq ft, which is exactly what `--bldg C`
has always produced — and `--bldg` filters those sheets out, which is why this never showed there.
⚠ `--bldg` does NOT exercise the building cut at all: it sits behind `if (request.TowerOnly is …)`.
Measure a per-building deliverable with `--tower`, which is what she was sent.

## Open, with the measurement already done

**A6. "Mezzanine levels still not good, the slab edge is wrong."** `LEVEL 1 MEZZ` ships THREE plates
— 2,754, 2,330 and 1,098 sq ft — where the drawing's largest closable chain is 1,857. `chains.py`
on the sheet: 274 chains, the largest OPEN with an 82 in gap. The report names both mechanisms
itself: one plate is *"a slab outline crossed itself and was read as 1 separate plate rather than
one ring through its own edge"*, another is *"recovered by flood-filling"*.
**Render it and the sheet has no continuous outline at all** — several separate small regions.

⚠ "Levels" is plural and the second is absent: `LEVEL P1 MEZZ PLAN` (54 walls, 51 columns, 1 plate)
is *"not placed"* because **her own reference model has no `LEVEL P1 MEZZ` storey** — it carries
only `LEVEL 1 MEZZ`. That half is a question for her, with the evidence attached.

⭐ **The strongest lead, not yet acted on: it is the SAME join mechanism as A5.** Building C's own
mezzanine sheet closes ONE substantial chain — 1,857 sq ft, and nothing else over 500 — yet
`LEVEL 1 MEZZ` ships 2,754 + 2,330 + 1,098. The report has both the whole-site
`LEVEL 1 PLAN MEZZ - CONCRETE OUTLINE.dxf` and `S2.12.1_1_… - BLDG C.dxf` quoting the SAME 2,754
sq ft, which is what two sheets read as one plan look like. The A5 fix does not reach it because the
whole-site sheet names NOBODY, and untagged sheets are deliberately still joined — that exemption
exists to protect the parkade, which is drafted once for the site. Whether an untagged sheet should
also be held back on a storey the building draws for itself is the open question.

⛔ **FALSIFIED, do not retry: closing an open chain orthogonally.** The reasoning was that every
slab edge on the mezzanine sheet is drawn square, so an invented diagonal closure must be wrong.
`LoopGeometry.CloseAsDrawn` turned the closing gap through a corner instead of cutting across it.
It did NOT move the mezzanine at all, and it BROKE level 1: the correct single 11,026 sq ft plate
became two of 7,048 + 4,603, because **building C's north edge really is diagonal** — visible on
`S2.10.1_1_LEVEL 1 PLAN - CONCRETE OUTLINE - BLDG C.dxf`. The premise "this drawing is square" is
false, and squaring a real diagonal is worse than the fault it was aimed at.

**A7. "Different slab thicknesses, this complexity is not reflected."** L3 prints 60 call-outs
across 11 thicknesses, L2 prints 22 across 8; one plate at one thickness is modelled. The attempt is
stashed and needs the full nesting tree from every closed ring rather than a branch on the opening
test — zones nest, and a probe found ring 11,026 inside 72,424 carrying `[14,30,36,56,76]` where the
ring around it carried `[12,14,30,36,37,56]`.

**A8. "The wall and column thing has to be fixed, otherwise I can't really use the model."**
One object, one label, an assign per storey — measured in HER model, not inferred: 87 column
objects, every one LINE span 1, 57 assigned to about five storeys each. 19 of the 87 carry more than
one section and four pair a rectangular with a circular one, so a column round on one storey and
rectangular on the next IS one object with one label.

`MemberPlanStoreyMultisetPreserved` gates it, and the merge is committed behind
`const bool MergeStacksIntoOneLabel` in `DxfToEtabsService`. Placement is proven exact — 1,769
column objects to 268 with all 29 storeys identical, confirmed by `members_by_storey.py`, which
shares no code with the gate. **The "adds members, LEVEL 2 columns 36→60" it was stashed for does
not reproduce and was never the merge.** Three coverage checks still disagree on column size and
shape; that is unexplained, and the merge stays off until it is not.
