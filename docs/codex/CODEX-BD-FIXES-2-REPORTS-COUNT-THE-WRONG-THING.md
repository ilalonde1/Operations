# Codex — Make the BD reports count the thing they claim to count

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits,
add the regressions as source, and ping when applied. Claude builds and runs the suite on the
dev box; Ian runs anything that touches `KOR-APP01`.

## Why this exists

Brief 2 of 4 from the BD audit. **`docs/codex/CODEX-BD-AND-BD-REPORTS-AUDIT-RESPONSE.md` is
your specification** — you wrote it, and each finding carries the evidence, the file and line
references, the repro, the live counts, and what must NOT be claimed. Read the finding before
each fix. Do not restate it; fix it.

These six share one shape: **a number is produced correctly from the wrong population, or a
population is labelled as something it is not.** The arithmetic is usually right. The meaning
is wrong, and these figures go to Ian and into client-facing material, so they get acted on.

⚠ **For a presentation defect the fix is to make the output state what it could not establish
— not to drop the figure.** A number that is approximate and labelled approximate is correct.
Deleting a total because it is incomplete is not a fix.

Diagnose each your way. If the fix differs from what the finding implies, say so and do the
right one.

## Fix 1 — finding 3 (High): a persisted verdict the renderer cannot classify

Synthesis lowercases accepted verdicts while the C# constants, rank, urgency and bucket
comparisons are uppercase ordinal, so a project counts as honed and lands in no section. 88
live rows are affected. A second defect at the same boundary silently rejects a requested
verdict whose token contains an underscore while accepting an unknown one, and the caller
still counts it as updated.

## Fix 2 — finding 9 (High): unknown cost valued at zero

Unknown-cost projects are coalesced to zero and folded into a displayed total and into heat
ranking, with no statement that the figure is the known-cost subtotal. Only 103 of 270 pursuit
projects are costed.

## Fix 3 — finding 10 (High): delivered work reported as active pipeline

The architect ranking unions inventory links and historical work without establishing a live
pursuit window, labels the result active projects and pipeline, and the renderer reads more
linked projects as more seats KOR can pursue.

## Fix 4 — finding 11 (High): "People" counts affiliations

The counts total affiliation rows rather than distinct live people, so a role change at the
same organisation counts twice and former roles contribute. 13 of the displayed top 20
architects are inflated.

## Fix 5 — finding 12 (High): region-brief "live" and "forward" use incompatible rules

The live prime-RFP count and list omit any dismissal or future-deadline test, so dismissed rows
present as live; the forward headline counts a narrower stage set than the section it heads.

## Fix 6 — finding 14 (Medium-high): capped and clipped output presented as complete

A hard TOP cap on strategic targets and a character clip on recommendation text both render as
finished content, so the reader cannot tell a limit was applied.

## What every fix must come with

- **A regression gate**, EXPECTED vs ACTUAL, that fails before the change and passes after.
  Where the defect needs a live database to reach, put the invariant in
  `tools/BdIntegrityCheck/Program.cs` in the shape of the existing entries.
- Each gate states what it covers and **what it does not**, per CLAUDE.md rule 11.
- ⚠ **Do not reproduce the defect class in your own verification queries.** Count distinct
  things, not rows, and say which you counted.
- If wrong data already exists, say how many rows and leave it alone. Repairs are Ian's.

## Out of scope — do not do these

- The other fourteen findings. They are in briefs 1, 3 and 4.
- Data changes, migrations, deploys, service actions.
- The enrichment join inventory. You resolved all 11 sites: each pins one provider, the
  multiplier is 1, and there is no provider fan-out. **Do not revisit it** — the brief that
  prompted the audit was wrong about this and the table in your response settles it.
- Entity identity and merging — audited separately. Finding 11 is a counting defect, not an
  identity one; do not turn it into a resolver change.
- Any report redesign larger than the finding. If one is genuinely needed, stop and say so.

Ping when applied, with the list of files touched.
