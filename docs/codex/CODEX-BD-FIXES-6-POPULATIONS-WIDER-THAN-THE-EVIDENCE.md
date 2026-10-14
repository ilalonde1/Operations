# Codex — Stop the BD reports claiming a population or a fact wider than the evidence

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
add the regressions as source, and ping when applied. Claude builds and runs the suite on the
dev box; Ian runs anything that touches `KOR-APP01`.

## Why this exists

Brief 6 of 7, from the second audit. **`docs/codex/CODEX-BD-AND-BD-REPORTS-ADVERSARIAL-AUDIT-2026-10-13.md`
is your specification** — read the finding before each fix. Do not restate it; fix it.

These six share one shape: **the label says more than the evidence behind it.** "Live" includes
dismissed rows, "forward" includes completed ones, a project seat is asserted from an
architect-wide pattern, "confirmed structural" from any engineer, "urgent" from a substring,
and "KOR-project match" from a project KOR never touched.

Measured on the live database 2026-10-13 before this brief was written (one query each):

| Finding | Live count |
|---|---|
| BD-A05 dismissed, non-retired projects whose latest verdict is PURSUE/PURSUE_URGENT | **0 of the active set** — the path is wrong, the data is clean today |
| BD-A06 `Status = New` prime RFPs that are dismissed and would still count as "Live RFP" | **8 of 69**; expired-but-New: 0 (the retirement job expires them) |

Use these numbers as given. Do not re-measure, do not invent others.

## Fix 1 — BD-A05 (High): dismissed projects in the live-pursuit reports

Call sheet, pursuit dossiers and attack cards select non-retired PURSUE rows without excluding
dismissal. Brief 2 built `BdReportPopulationSql` so one definition of a population is shared;
dismissal belongs in it, and these three readers use it.

## Fix 2 — BD-A06 (High): sector briefs count expired and dismissed as live, completed as forward

Region briefs got the shared predicates in brief 2; the sector paths did not. Same predicates,
same file, both report families. A report that must show a wider population says so in the
document.

## Fix 3 — BD-A07 (High): an architect-wide pattern presented as this project's seat

The pursuit brief does not fetch the project's resolved structural-engineer organisation, then
labels the architect's partner map "Likely prime & structural seat". Fetch the project-level
appointment, show it first, and keep the architect pattern visibly separate from it. A named
project SE is not a seat to displace by inference.

## Fix 4 — BD-A12 (Medium): any "engineer" becomes "confirmed structural engineer"

The role predicate accepts `engineer` anywhere. A confirmed discipline needs discipline evidence;
anything else is listed under what it actually is, or not under "confirmed".

## Fix 5 — BD-A13 (Medium): "no urgent action" is urgent

`IsUrgent` is true for `PURSUE` whenever `korAngle` contains the substring. `PURSUE_URGENT` is the
structured signal; the substring rule either goes, or becomes something that cannot be satisfied
by a negation. The gate includes the negated sentence from the finding.

## Fix 6 — BD-A15 (Medium): award matches without KOR involvement or the stated geography

"Light KOR-project match" must establish KOR involvement, and a geographic hint must enforce
the geography it names. `US` and `Canada` bypass; narrower regions have no branch. Fix the
query, and make the column label say what the query now guarantees.

## What every fix must come with

- **A regression gate**, EXPECTED vs ACTUAL, failing before and passing after, stating what it
  covers and **what it does not** (CLAUDE.md rule 11). Where a population is the defect, the
  gate feeds dismissed / expired / completed / wrong-discipline rows through the real query text
  or the real classifier, the way `BdReportMeaningRegressionTests` does.
- Where the defect needs live data, add the invariant to `tools/BdIntegrityCheck/Program.cs` in
  the shape of the existing entries (`bd_region_live_rfp_population` is the model).
- Do not quantify beyond the table above.

## Out of scope — do not do these

- The other nine findings. They are in briefs 5 and 7.
- Data changes, migrations, deploys, service actions.
- Entity identity and merging.

Ping when applied, with the list of files touched.
