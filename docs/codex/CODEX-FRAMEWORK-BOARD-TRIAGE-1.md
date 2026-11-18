# CODEX board triage 1: sort the Framework lead's 63 open rows (small, evidence only)

Written 16 Nov 2026. Ian 16 Nov 15:2x (#133855): the board grew about twice as fast as it was worked (545 rows opened,
244 closed in three weeks), so each open Framework row is sorted once. This is a sort for the lead to check, not a
close: nothing is changed on the board by you.

Write ONE file: `docs/codex/CODEX-FRAMEWORK-BOARD-TRIAGE-1-RESPONSE.md`. Edit nothing else, and commit nothing.

## Inputs: read these, and only these

In `docs/codex/CODEX-FRAMEWORK-BOARD-TRIAGE-1-work/` (exported 16 Nov; do not query the DB):

- `framework-open-rows.csv` (63 rows): Id, State, dates, What, DoneWhen, Budget, BlockedOn, Evidence.
- `commits-since-27oct.txt` (2,288 lines, `<repo> <sha> <date> <subject>`): every commit subject in Framework,
  Operations and KOR.Drafter since 27 Oct. SEARCH it (by "row <Id>", and by the row's own distinctive words); do not
  read it whole.

## The task

For EVERY one of the 63 rows, one line: Id | pile | evidence | the reason in under 25 words.

Piles:
- **DONE?** - a commit subject names this row (or its exact deliverable) as built. Evidence: the repo and sha. The lead
  checks it before closing; a commit that only mentions the row is not DONE.
- **DUPLICATE** - another open row in this file asks for the same outcome. Evidence: the other row's Id and the shared
  outcome in a few words.
- **STALE** - the row's own text shows it was overtaken: its What names a job, version, run or plan that a later row
  or commit replaced. Evidence: the row or commit that replaced it.
- **REAL** - still owed. Evidence: "no commit found for: <the search words you used>". Then say whether it needs Ian
  (it names a decision, a password, a purchase, or "waits for Ian") or not.

Then a summary table: count per pile, and the REAL rows that need Ian, listed by Id.

## Limits

- Facts from the two files only. A row you cannot place: pile UNSURE, with what is missing.
- "No commit found" is only as good as the words you searched: name them on every REAL and UNSURE line.
- Do not open code, the board, or any other file. Do not propose new rows.
- Budget: about 20 minutes. If you are going past that, stop and write what you have.
