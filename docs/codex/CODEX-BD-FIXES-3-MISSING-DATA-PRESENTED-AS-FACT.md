# Codex — Stop the BD surfaces presenting missing data as established fact

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
add the regressions as source, and ping when applied. Claude builds and runs the suite on the
dev box; Ian runs anything that touches `KOR-APP01`.

## Why this exists

Brief 3 of 4 from the BD audit. **`docs/codex/CODEX-BD-AND-BD-REPORTS-AUDIT-RESPONSE.md` is
your specification** — you wrote it, and each finding carries the evidence, the file and line
references, the repro, and what must NOT be claimed. Read the finding before each fix. Do not
restate it; fix it.

These six share one shape: **the system cannot tell "no" from "don't know" from "the read
failed", and renders all three as a confident assertion.** A reader cannot recover the
difference from the document, so an absence of evidence is presented as evidence of absence —
and in several cases it is exported to a PDF that outlives the session.

⚠ **The fix is a third state, not a different wrong answer.** "Unavailable", "unknown", "not
retrieved" are correct outputs. Do not make an unknown default to the opposite claim, and do
not silently drop the section.

Diagnose each your way. If the fix differs from what the finding implies, say so and do the
right one.

## Fix 1 — finding 6 (High): a failed read becomes an authoritative "none on record"

When the Deltek read throws, the catch clears the displays and the getters assert no prior work
and no contacts, while the outer load still reports success — and exporting the snapshot
carries the false absence into the PDF. The same placeholders also stand in for a missing link
and for a genuine empty result, which is the part that makes it unrecoverable.

Note the entry-point inconsistency in the same finding: one surface enriches and explains
unavailability, the other calls the raw store that returns null, so the same linked org exports
without its financial section and without the explanation.

## Fix 2 — finding 7 (High): unknown renders as "open seat" and as "we know them"

A card with no resolved structural engineer prints "open seat" with no seat-status field behind
the decision — 245 of 270 live cards. Separately, an organisation becomes warm on the existence
of a public research affiliation rather than on any KOR relationship.

## Fix 3 — finding 13 (High): "verified current" overclaims

The claim is satisfied by two narrow input streams being quiet, which is not evidence that the
underlying facts were re-checked.

## Fix 4 — finding 16 (Medium-high): generated prose asserts beyond its inputs

Template prose states that facts are verified and that a pursuit is urgent independently of
whether the underlying fields carry either.

## Fix 5 — finding 19 (Medium): partial research counted as complete

A research row that failed to parse, or parsed into nothing usable, still increments the
researched/identified counts.

## Fix 6 — finding 20 (Medium): an in-flight run labelled failed

Admin renders a run whose end timestamp is still null as failed, which is indistinguishable
from a real failure. This repo has already paid for that confusion: a 9-minute GovCanada run
was read at 7 minutes, declared failed and cancelled, having already inserted 295 rows.

## What every fix must come with

- **A regression gate**, EXPECTED vs ACTUAL, that fails before the change and passes after.
  Where the defect needs a live database, put the invariant in
  `tools/BdIntegrityCheck/Program.cs` in the shape of the existing entries.
- Each gate states what it covers and **what it does not**, per CLAUDE.md rule 11.
- For a failure path, the gate must prove the user-visible text differs between "none" and
  "could not retrieve". A test that only asserts no exception is thrown misses the whole point.
- Do not quantify a false-absence population you cannot measure: absence of a relationship
  cannot be proved from absence of one database edge, and your own response says so.

## Out of scope — do not do these

- The other fourteen findings. They are in briefs 1, 2 and 4.
- Data changes, migrations, deploys, service actions.
- Entity identity and merging — audited separately. Finding 7's warm-org defect is a counting
  and labelling defect, not an identity one.
- Adding a Deltek capability. The app has one; finding 6 is about an entry point not using it.
- Any retry, caching or resilience framework. If a fix genuinely needs one, stop and say so.

Ping when applied, with the list of files touched.
