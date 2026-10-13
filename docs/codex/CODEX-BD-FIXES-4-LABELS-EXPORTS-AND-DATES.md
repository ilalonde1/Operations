# Codex — Fix the BD labels, exports and dates that change a number's meaning

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
add the regressions as source, and ping when applied. Claude builds and runs the suite on the
dev box; Ian runs anything that touches `KOR-APP01`.

## Why this exists

Brief 4 of 4 from the BD audit. **`docs/codex/CODEX-BD-AND-BD-REPORTS-AUDIT-RESPONSE.md` is
your specification** — you wrote it, and each finding carries the evidence, the file and line
references, the repro, the live counts, and what must NOT be claimed. Read the finding before
each fix. Do not restate it; fix it.

These four share one shape: **the value is right and what the reader is told about it is
wrong** — a label that changes its meaning, an export that is not what it says, a date that
renders two ways, a party on the wrong side of a table. Cheap to fix, and each one is the kind
of thing a client notices before we do.

Diagnose each your way. If the fix differs from what the finding implies, say so and do the
right one.

## Fix 1 — finding 8 (High): a contracted fee labelled as billed, and a frozen source undisclosed

One export path labels a contracted fee sum "Lifetime billed" while another labels the same
amount "Lifetime fee", so the choice of renderer changes the meaning of the number without
saying so. Separately, a column sourced from the frozen `PRSummaryMain` is labelled "Billed"
with no as-of or freeze disclosure, and a successful stale query does not mark the section
degraded.

⚠ The freeze does not make every money field stale — AR and fee fields have different sources,
and your response is explicit about that. Disclose the frozen one; do not blanket-label.

## Fix 2 — finding 15 (Medium-high): the visual DOCX export saves one viewport

The export writes a single viewport rather than the full cards report, while presenting itself
as the report.

## Fix 3 — finding 17 (Medium): one deadline renders as two calendar dates

Briefs format the stored offset's date while the Dashboard converts to workstation local time,
so the same deadline shows two calendar dates; rounded countdowns compound it. 61 stored
deadlines differ this way, 17 of them in the future.

⚠ **Stored deadlines legitimately carry a non-UTC offset** as of the 2026-10-13 ingestion fix,
and `RfpReleaseDate` is deliberately derived from the published LOCAL date. This is a consumer
inconsistency: pick one policy and apply it to every consumer. **Do not "fix" the stored
offsets, and do not touch `Kor.Opportunities.Data/Ingestion` — that defect is closed and gated,
and a test will fail if you reopen it.**

## Fix 4 — finding 18 (Medium): KOR appears as KOR's own competitor

A project brief can list KOR among the competitors on its own pursuit.

## What every fix must come with

- **A regression gate**, EXPECTED vs ACTUAL, that fails before the change and passes after.
  Where the defect needs a live database, put the invariant in
  `tools/BdIntegrityCheck/Program.cs` in the shape of the existing entries.
- Each gate states what it covers and **what it does not**, per CLAUDE.md rule 11.
- For fix 3, the gate must pin the policy for a deadline on both sides of a DST boundary, in
  both consumers. One consumer tested in isolation is how this defect survived.
- For fix 1, the gate must cover **both** renderers. A label fixed in one path and not the
  other is the defect, not a fix.

## Out of scope — do not do these

- The other sixteen findings. They are in briefs 1, 2 and 3.
- Data changes, migrations, deploys, service actions.
- `Kor.Opportunities.Data/Ingestion` and anything about how a timestamp is PARSED or STORED.
  Fixed and gated 2026-10-13; `PublishedClockTests` fails if `DateTimeStyles.AssumeUniversal`
  returns to that layer.
- The upstream `PRSummaryMain` freeze. Not in these modules — disclose it, do not chase it.
- Entity identity and merging — audited separately.
- Any renderer or export rewrite larger than the finding. If one is genuinely needed, stop and
  say so.

Ping when applied, with the list of files touched.
