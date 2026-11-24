# CODEX budget stop: a board row's Budget must stop the next expensive step once it is spent

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by a Claude session (Framework coordinator framework-31, 23 Nov 2026) - the kind of session this stop is
meant to control. Ian asked that every Codex brief from a Claude session say so up front (#133998). Treat every statement
below as a claim; check each against the code, the database and the files named.

## Why
Ian's list of agentic-development issues (Framework `docs/THE-PRIZE-AGENTIC-DEV-ISSUES-2026-11-19.md`): issue 12 "I don't
feel cost ... I'll spend tokens and Revit hours as readily as minutes" and issue 5 "I optimise for the step in front of me,
not the goal". Ian chose this as the FIRST of the 15 to mitigate (23 Nov; Framework docs/HANDOVER.md, IAN'S PLAN "NOW: THE 15
ISSUES", item 1): "they should make you more predictable, efficient and controllabble".

Every row of the board (KorStandards `oversight.Commitment`) carries a free-text `Budget` (KOR.Drafter `db/163_EveryJobHasABudget.sql`,
nvarchar(400), required on new rows by CK_Commitment_Budget). Nothing reads it before spending. Evidence (verify it):
- Row 2248, Budget "At most one KOR-210 SAFE run and two Codex passes; ~4 h; ...", recorded 20 Nov 20:12, closed 23 Nov
  16:42. KOR-210 sets with its number in the tag (KOR.Drafter `work\_runner\sets\`): step5-2248-storeys, -b1l04-r2, -diag-a,
  -diag-b, -diag-c (5; the two -refuse-* tags were dry refusals, nothing ran). Nothing stopped the 2nd. Ian stopped it by hand.
- Row 2143, Budget "One SAFE run on KOR-210 for one building model plus one Codex review; sized before starting." Sets
  step5-b1-l02* (several; count them yourself and say which ran).
Codex passes are NOT counted: Ian 23 Nov, "don't contrain the budget for codex. If you need it to do more - let it do it".

## Prior work - EXTEND these, do not build beside them (an inventory by a helper, 23 Nov; check each)
- `Kor.Operations.Framework/Stall/StallGuard.cs` + `StallStore.cs:45-51` (`kor stall`, PreToolUse): already recognises KOR-210
  steps and logs refusals with `StallStore.LogGuardRefusal` to `oversight.GuardRefusal`. Counts per session, not per row.
- `Kor.Operations.Framework/Enforce/DrafterPythonGuard.cs` (`Approval = "IAN-APPROVED-PYTHON"`, `Approved(session, promptsDir)`
  at :99) and `SqlWriteCutGuard.cs`, wired in `Kor.Operations.Framework.Cli/Program.Enforce.cs:100-144`: the pattern for a C#
  guard and for a lift that only Ian's TYPED words in a prompt can give.
- KOR.Drafter `tools/virtual_drafter.py:315-474` (SAMPLE-FIRST STOP, Revit): count, refuse, Ian's typed token lifts for 12 h,
  each refusal/lift written to brain.Event. The behaviour to match. Read it; do not edit KOR.Drafter.
- Framework `tools/scorecard.py` (`BUDGET` regex :95, `budget_seconds()` :148): reads the hours in a Budget after a row
  closes - but only a duration at the START of the text, so row 2248's mid-text "~4 h" is missed by it.
- KOR.Drafter `docs/one-tool/run-on-210/send-set.ps1`: the ONE door that starts a KOR-210 run (`-Job -Tag -Holder [-Sample]`;
  writes `work\_runner\sets\<Tag>\set.json`, no row field). NOT in scope to edit - it is wired in a later step (see below).
Ian 19 Nov: new guards in C# / kor, not Python (record #134051).

## Build (in kor, C#)
1. `kor budget` verb:
   - `kor budget check --row N --step kor210-run [--ref <tag>]` -> exit 0 and records the spend when the row's budget allows
     it; exit nonzero with a one-paragraph refusal (row, Budget text, spent X of Y, how Ian lifts it) when it does not.
   - `kor budget show --row N` -> the row's Budget text, what was parsed from it, the spends recorded, what is left.
2. Reading a Budget: the KOR-210 run count from the free text - at least "one/a/at most one/N KOR-210 (SAFE|ETABS)? run(s)"
   and "One SAFE run on KOR-210" (row 2143's wording); hours from "~N h" anywhere in the text, plus every form scorecard's leading regex reads. A Budget that states
   NO KOR-210 count allows 0 runs and the refusal says so ("state the run count in the row's Budget"). Unparseable or
   ambiguous text refuses; it never guesses a number. Write down every wording you support.
3. Ledger: one brain.Event per check (allowed / refused / lifted) carrying row, step, ref, session. Spent = allowed events for
   that row. Hours run from the row's FIRST allowed spend (rows sit open for days before work starts), refused when over.
4. Lift: only Ian's typed `IAN-APPROVED-OVER-BUDGET <row>` in a user prompt of the session, found the way DrafterPythonGuard
   finds its approval; one row, 12 hours, logged as a lift. A token for another row, or quoted inside a tool result or file,
   does not lift.
5. Refusals logged through `LogGuardRefusal` (guard name `budget`), so `guard_log.py week` counts and judges them.
6. Raising a budget is Ian's: extend `SqlWriteCutGuard` so a SQL write that sets `Budget` on `oversight.Commitment` for an
   EXISTING row (UPDATE / MERGE) is refused without Ian's typed `IAN-APPROVED-BUDGET <row>`; an INSERT of a new row is allowed.

## Not in scope (say so in the response; do not do them)
- Wiring the door: send-set.ps1 calling `kor budget check` with a new `-Row` - KOR.Drafter's, a later step after this ships.
- Revit runs, Codex passes, token spend (SpendMeter stays a warning - Ian ruled out refusing on a percentage, #124879).

## How
Work in `C:\VIsual Studio Projects\Operations-budget-stop` on branch `budget-stop` at origin/develop 0883c0ff.
- CASES FIRST, committed alone (`budget stop A: cases (codex)`), each failing on the current code; include rows 2248 and
  2143 replayed as Budget text + a list of spends (the 2nd run refused), every wording above, the token rules, and the
  SqlWriteCut cases. Then the change (`budget stop B: <one line> (codex)`). Never amend A after starting B.
- Tests never leave a row in KorStandards: DB writes only inside a transaction that is rolled back; report any row left.
- Proofs ONE AT A TIME (lesson #138092: three suites at once pegged the PC): the targeted tests, then the full
  Kor.Operations.Framework tests ONCE at the end, alone, compared by name with a run at 0883c0ff (`kor testdiff`).
- Keep TEMP/TMP normal. Commit with `git -c core.hooksPath=<an empty folder> commit ...`. Must not: push, install kor, edit
  outside this worktree, change any other guard or settings or git hook, write the live database, touch another machine.
  The author holds back further cases.

## Write ONE response file
`docs/codex/CODEX-BUDGET-STOP-RESPONSE.md`, committed last: what was built (files, verbs), the Budget wordings supported and
the refusal text, the cases and their red/green counts, the targeted and full-suite numbers and the testdiff, repo rule 11
(what the stop COVERS, what it does NOT, and at least one same-class overspend it would still miss), then anything wrong in
this brief.
