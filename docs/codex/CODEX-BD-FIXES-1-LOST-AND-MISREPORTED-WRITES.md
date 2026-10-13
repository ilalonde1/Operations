# Codex — Close the four BD paths where a write is lost or misreported

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
add the regressions as source, and ping when applied. Claude builds and runs the suite on the
dev box; Ian runs anything that touches `KOR-APP01`.

## Why this exists

Brief 1 of 4 from the BD audit. **`docs/codex/CODEX-BD-AND-BD-REPORTS-AUDIT-RESPONSE.md` is
your specification** — you wrote it, and each finding there carries the evidence, the file and
line references, the repro, the live exposure counts, and what must NOT be claimed. Read the
finding before each fix. Do not restate it; fix it.

These four share one shape: **a user acts, the app indicates success, and the durable state
does not match.** That is the worst class in a BD tool, because the user stops thinking about
it. Group them because the fixes live on the same write boundary, not because they are one bug.

Diagnose each your way. If the fix differs from what the finding implies, say so and do the
right one.

## Fix 1 — finding 1 (High): a dismissed opportunity is still claimable

The grab guard tests id, status and owner but not dismissal, so a row can be removed and then
claimed, with both users seeing success. 87 dismissed rows satisfy the guard today.

## Fix 2 — finding 2 (High): Dashboard Save closes successfully and discards the edit

Opening an opportunity from a Dashboard row gives the ordinary editable dialog, but the caller
ignores its result, so nothing is written. The comment calling it a read-only view is false —
fix the comment or the behaviour, whichever is the lie.

## Fix 3 — finding 4 (High): Briefs Maker exports the previous selection under the new id

Selecting a second project or person while the first is still loading leaves the captured data
on the old selection and the id on the new one, so Generate renders the wrong record into a
correctly-named file.

## Fix 4 — finding 5 (High): Admin reports a saved value it never sent to SQL

A second toggle while the first write is in flight returns early on a shared busy flag without
writing, reverting or reporting, and the first write then formats its success message from the
row's current value — so database, checkbox and success text can disagree three ways.

## What every fix must come with

- **A regression gate**, EXPECTED vs ACTUAL, that fails before the change and passes after.
  Where the defect needs a live database to reach, put the invariant in
  `tools/BdIntegrityCheck/Program.cs` in the shape of the existing entries instead of writing
  a test that needs SQL.
- Each gate states what it covers and **what it does not**, per CLAUDE.md rule 11. A broad
  name over a narrow check is worse than no check.
- Where a success message can outrun the write, the fix is that the message follows the
  confirmed result — not that the message is removed.
- If wrong state already exists in the data, say how many rows and leave them alone. Repairs
  are Ian's.

## Out of scope — do not do these

- The other sixteen findings. They are in briefs 2, 3 and 4.
- Data changes, migrations, deploys, service actions.
- Entity identity and merging — audited separately in
  `CODEX-BD-ENTITY-IDENTITY-AND-REFRESH-AUDIT.md`. Assume canonical resolution is as designed.
- Date parsing in `Kor.Opportunities.Data/Ingestion` — fixed and gated 2026-10-13.
- A general concurrency, locking or optimistic-versioning redesign. Three of these four are
  timing defects and will tempt you into one. If you believe a fix genuinely requires it,
  **stop and say so** instead of building it.

Ping when applied, with the list of files touched.
