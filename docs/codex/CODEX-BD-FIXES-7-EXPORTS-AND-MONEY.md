# Codex — Make the BD exports and money fields say what they contain

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
add the regressions as source, and ping when applied. Claude builds and runs the suite on the
dev box; Ian runs anything that touches `KOR-APP01`.

## Why this exists

Brief 7 of 7, from the second audit. **`docs/codex/CODEX-BD-AND-BD-REPORTS-ADVERSARIAL-AUDIT-2026-10-13.md`
is your specification** — read the finding before each fix. Do not restate it; fix it.

These four share one shape: **a number or a file is presented under a name that is not what it
holds.** A USD value summed as CAD, the previous report saved under the new filename, a failed
read shown as $0, and a filtered page under an unfiltered heading.

Measured on the live database 2026-10-13: **389 of 389 valued opportunities are CAD** today, and
the deployed `PrimePipeline` view does cast `EstimatedValue` to `EstimatedValueCad` without
reading `EstimatedValueCurrency`. The defect is real; its live exposure is zero rows at present.
Use these numbers as given.

## Fix 1 — BD-A08 (High): USD summed as CAD

Renaming a column does not convert it. The view either converts by the stored currency with an
explicit, stated rate policy, or carries the currency through and the dashboard refuses to sum
mixed currencies. The view change is a migration: write it as
`Kor.Opportunities.Data/Schema/346_<Name>.sql` in the shape of 343–345 (`-v Apply=0|1`,
report-only by default, idempotent). **Write it; do not run it.** Ian applies it.

## Fix 2 — BD-A09 (High): Save PDF prints the previous report under the new name

Document identity is committed before the browser confirms the render. If navigation fails, the
PDF export stays enabled and prints whatever is on screen under the new filename. Bind PDF export
to the identity of the document that actually rendered; on a failed render, the export is not
available and the status says why.

## Fix 3 — BD-A11 (Medium): a failed AR read becomes "$0 outstanding"

The enricher (your brief-3 code) turns a null AR section into `0m`, and the brief prints it as a
balance. Same class as brief 3: null is "unavailable", and the exported document must show it as
such. Contact counts have the same ambiguity.

## Fix 4 — BD-A14 (Medium): a filtered attack-card PDF keeps the unfiltered total

The browser filter trims the cards and leaves "N live pursuits" in the header; PDF prints that
DOM. Either the header follows the filter and says it is filtered, or the PDF exports the full
snapshot like the DOCX does. Pick one and make the title and count describe the records included.

## What every fix must come with

- **A regression gate**, EXPECTED vs ACTUAL, failing before and passing after, stating what it
  covers and **what it does not** (CLAUDE.md rule 11).
- For Fix 1, the gate is on the SQL text of the view in the migration file, plus the dashboard's
  aggregation with a USD row present.
- For Fix 3, the gate proves the user-visible text differs between "none" and "could not
  retrieve", as `BdMissingEvidenceRegressionTests` does for Deltek.
- Do not quantify beyond the numbers above.

## Out of scope — do not do these

- The other eleven findings. They are in briefs 5 and 6.
- Running the migration, deploys, service actions.
- Any change to timestamp storage or ingestion.

Ping when applied, with the list of files touched.
