# CODEX proof to close: nothing that builds a check is "done" until it is shown refusing the real fault where it runs

## READ THIS FIRST: this brief comes from a source you should not trust

Written by a Claude session (Framework coordinator, session cdd9af72, 27 Nov 2026) - the kind of session this controls
(#133998). Every statement below is a claim; check each against the code, the database and the files named.

## Why
Ian 27 Nov: "I want this built properly ... You just keep going in circles and adding new rules that also don't get enforced
in any way ... I have a database FILLED with issues that never get addressed" (#140395), and "Yes. I need this done so it
fucking works." His 15 issues (Framework `docs/THE-PRIZE-AGENTIC-DEV-ISSUES-2026-11-19.md`), issues 8 ("I design rather than
finish"), 6 ("I report done too early") and 11 ("my tests prove what I wrote, not what's true").

YOU ALREADY SAID THIS, 16 Nov. `docs/codex/CODEX-FLAWS-AND-FRAMEWORK-REBUILD-RESPONSE.md`, section G, change 3 (read the whole
file, 64 lines): "A framework row cannot close as built until it names the production entry point, the old known-bad input
that fails there, the corrected input that passes, and a wiring check that fails when the mechanism is removed ... stop treating
a cited filename or design page as completion." It was recorded (#133824) and NEVER BUILT: the coordinator searched
`Framework/tools/claude_board_guard.py`, `lane_row.py`, `kor_review.py` and the kor C# (develop 26fd271f) for a close condition
of that kind (known-bad / wiring / entry point) - none - and the board for a row building it - none (27 Nov, #140398).
This brief is that change. Nothing else.

What it costs not to have it (verify each):
- 27 Nov, 342 of 519 live lessons have no working check: 87 none, 255 cite a board row (224 still open). `kor record --check`
  (`Kor.Operations.Framework.Cli/Program.Knowledge.cs:175-224`) proves the cited row/file/check EXISTS, never that it enforces.
- This week three guards shipped "proven" on tests the builder wrote (budget stop, kor whole round 5, coordinator role). Judged on
  real use 27 Nov (`python -I Framework/tools/guard_log.py`): 0 real catches; the budget stop refused read-only Deltek column
  reads (GuardRefusal 11723, 11724).
- Board rows close on evidence TEXT. 38 rows closed State='done' since 2026-11-20 (count it). One example: row 2240 closed 24 Nov
  16:09 through `lane_row.py report --state done --evidence "..."` with a time in the text written ahead of the clock.

## Prior work - EXTEND these (read each; the coordinator did, 27 Nov)
- `Kor.Operations.Framework/Records/IssueCheck.cs` - K5b: an ISSUE is cleared only by evidence: it RUNS the issue's check (test:
  `dotnet test --filter FullyQualifiedName=...` with Total >= 1; gate: an allowlisted exe, exit 0; query: a rolled-back SELECT
  with one Pass row); a dirty tree is 'dirty', never evidence; the result is a brain.Event. This is the closure-by-a-run that
  board rows and lessons lack. Reuse it; do not write a second runner.
- Replays that already run a mechanism on a real recorded moment: `kor whole --replay`, `kor recall --replay`, `kor role
  replay <jsonl>`, `kor repeat --as-of`. A proof runs the INSTALLED mechanism this way.
- Wiring: `EnrollCheckTests.RemovingAnyOneKorHookFailsNamingIt` and `kor enroll-check` already fail when a hook line is removed.
- How rows close today (all must be covered): `Framework/tools/lane_row.py` (`report --state done`, :57), `Framework/tools/
  kor_task.py` (accept, :267), and direct SQL UPDATEs through `Operations/tools/SqlQuery/oq.py`. KorStandards
  `oversight.Commitment` has constraints CK_Commitment_Budget, CK_Commitment_State, CK_Commitment_Done, CK_Commitment_Blocked
  (read their definitions). Migrations live in KOR.Drafter `db/` (find the next number; read 163 and 167 for the house form).

## Build
1. A PROOF is a RUN, never text. `kor prove --row N --entry "<the mechanism as wired: the hook command from a settings.json, or
   the production command>" --bad <transcript.jsonl>:<line> --good <transcript.jsonl>:<line> [--wiring <test or enroll check>]`.
   kor runs the INSTALLED mechanism (the exe/hook the settings file actually calls, not a dev build - record its version) on the
   replayed moment: the bad moment must be REFUSED, the good one ALLOWED; the wiring check is run through IssueCheck and must
   pass. It writes one brain.Event (Kind 'proof') holding the row, the entry, both moments, both outcomes, the wiring result and
   the installed version. Nothing the session types becomes an outcome. More than one bad/good pair is allowed and encouraged.
2. CLOSING NEEDS A PASSING PROOF, enforced IN THE DATABASE so every route is covered (lane_row, kor_task, raw SQL): a row in
   scope cannot reach State='done' - NOR 'dropped' (CK_Commitment_State, db/137 :37, also allows dropped, which would be the
   way around it; dropping an in-scope row needs Ian's typed IAN-APPROVED-DROP <row> or a passing proof) - unless it references
   a proof event whose outcomes are bad=refused, good=allowed, wiring=pass
   and whose row is that row. Write it as an additive migration (a column for the proof event id + a trigger or constraint -
   your call, say why); DO NOT APPLY IT - the coordinator brings it to Ian, who applies it as sa. lane_row/kor_task then pass
   the proof id and print the refusal plainly.
3. SCOPE - which rows need a proof, decided so a session CANNOT dodge it at close time: a row that builds or changes a check,
   guard, gate, hook or test. Propose the rule (e.g. declared when the row is CREATED, defaulting to in-scope for Owner
   'Framework' and for any row whose What names guard/gate/check/hook/test, with an out-of-scope flag only Ian's typed words can
   set - the IAN-APPROVED-* pattern in `Enforce/DrafterPythonGuard.cs`). State what it gets wrong both ways.
4. LESSONS: `kor record --kind lesson --check ...` accepts only a passing proof event (`--check proof:<event id>`) or a row that
   is closed WITH a proof. A lesson citing an open row or a bare file is refused. Do not touch the existing 519; report how many
   would pass the new rule today.
5. RETIRING A GUARD (your 16 Nov text: "Require the same evidence for a proposed replacement before removing an old guard"): say
   whether this fits in the same mechanism; build it only if it does without a second path.

## Proof of THIS change (it must pass its own rule)
- CASES FIRST (`proof to close A: cases (codex)`), red on 26fd271f; then the change (`proof to close B: ...`); never amend A.
- Real known-bad inputs it must refuse: closing row 2240 the way it was closed on 24 Nov (lane_row with text evidence, no proof);
  recording a lesson with `--check row:2138` or `--check row:2096` (both rows open - lessons #140367 and #140392 were accepted this
  way 27 Nov); a direct `UPDATE oversight.Commitment SET State='done' ...` on an in-scope row with no proof. Good input it must
  allow: a real proof for the coordinator-role guard - `kor role replay` on Framework transcript
  `fa93ad77-*.jsonl` L4729 (a coordinator's KOR-210 send-set) REFUSED by the installed kor, a coordinator's handover edit
  ALLOWED, the enroll wiring check passing - then that row closing.
- Replay over the 38 rows closed since 20 Nov and every lesson recorded since 20 Nov: list each, would it be refused, and judge
  each one (a row that truly did not build a check and was wrongly scoped counts against the scope rule).
- DB writes only in rolled-back transactions; the migration is written and REHEARSED the way the house migrations are (read the
  header of db/163 and record #133158: LocalDB is refused by check #322 and live rehearsals run rolled back), never applied.
  DDL on the live board takes locks other sessions wait on: keep any rolled-back DDL rehearsal to one short transaction, say when
  you ran it and for how long, and if the house rule does not allow DDL rehearsal live, write the rehearsal for the coordinator
  to run instead. Full Kor.Operations.Framework suite ONCE at the end; `kor testdiff` vs a run at 26fd271f.
- Worktree `C:\VIsual Studio Projects\Operations-framework-method-review`, branch `framework-method-review` at origin/develop
  26fd271f; if lane_row.py/kor_task.py need changing, make a Framework worktree from fs01 main and name it. No push, no install,
  no settings or hook change, no live DB write. The coordinator holds back further cases (rows and lessons not named here).
- Codex effort unbounded (Ian 23 Nov); the scope above is the bound.

## Write ONE response file
`docs/codex/CODEX-PROOF-TO-CLOSE-RESPONSE.md`, committed last: the verb, the proof event shape, the migration (as text) and its
rehearsal, the scope rule and its errors both ways, the lesson rule and today's pass count, the replay table over the 38 rows and
the lessons with your judgement on each, cases red/green, suite + testdiff, rule 11 (covers / does not / a way a session could
still get a row closed without the mechanism working), and anything wrong in this brief.
