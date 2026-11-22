# CODEX step 3: review and narrow six guards (context-read, start-docs, plan_review, kor-enforce, board, kor-stall)

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by a Claude session (Framework coordinator fa93ad77, 22 Nov 2026), and Claude sessions are the ones these
guards check. Ian asked that every Codex brief from a Claude session say so up front (#133998). Treat everything below as
claims; check each against the code, the transcripts and the database, and say where it is wrong. Ian, 22 Nov, after
the last round found faults in all four guards the coordinator had judged alone: "don't skip codex again ... It found
issues with all 4 that you missed."

## The goal, and the method (same as `docs/codex/CODEX-STEP3-GUARDS-REVIEW.md`, whose response you should read first)

Plan step 3: narrow each noisy guard so it stops refusing good work, without leaving its goal unchecked (Ian: no guard
is cut). For each guard below: read its goal (header, the rule or board row it cites), recover and judge its refusals
yourself (RIGHT = the action really had the fault the guard names; WRONG = it did not), give a verdict SOUND or FAULT
(a false allow that leaves the goal unchecked, or a misfire class that keeps costing turns), and finish each FAULT you
can fix in a bounded way: cases first, committed alone, each shown failing on the current code; then the change, with the
guard's own CHECKS / ALLOWS / DOES NOT CATCH text updated. A FAULT not worth fixing now: one line why.

**Judge by the guard's contract, not by outcome.** The last round showed that "the forced search found nothing" or "the
retry was the same" does not make a refusal wrong when the guard's job is to force that step. The worker judgements below
use that outcome test in places; re-judge them.

## Prior work (searched by the author: `rg -il "ReadGuard|kor-context-read|StartDocs|start-docs|plan_review_guard|
claude_board_guard|kor-enforce|CheckStore|StallGuard|kor-stall" docs/codex/` over 359 top-level entries, recursive:
67 files, all broad audits; `rg -il` over Framework docs/ and lanes/: 23 files)

- `CODEX-FRAMEWORK-INVENTORY-2-RESPONSE.md`: an earlier verdict per guard (read it for each of the six).
- `CODEX-STEP3-GUARDS-REVIEW-RESPONSE.md`: the last round's method, evidence handling and containment.
- Framework `lanes/2026-11-19/PLAN-W1-W9-CHECKS.md` (start-docs is its check 1), Framework `docs/HANDOVER.md` plan block.

## The six guards (claims: open each one)

Refusals are in KorStandards `oversight.GuardRefusal` by `Guard` name. `RefusedAtUtc` carries an explicit -07:00
offset. `Matched` is cut at about 400 characters; full actions come from the transcripts under
`C:\Users\ilalonde\.claude\projects\` and `C:\Users\kor-claude\.claude\projects\` (main and `subagents\agent-*.jsonl`).
Counts are all rows as read 22 Nov 11:4x local; the worker who judged some of them on 21 Nov judged by transcript.

| Guard (Guard column) | Code | Rows | Worker judged right / wrong / not |
|---|---|---|---|
| kor-context-read | Operations `Kor.Operations.Framework/Context/ReadGuard.cs` (+ `Cli/Program.Context.cs`), tests `ReadGuardTests.cs` | 240 | 10 / 12 / 218 |
| kor start-docs | Operations `Kor.Operations.Framework/Start/StartDocs.cs`, `Cli/Program.Start.cs`, tests `StartDocsTests.cs` | 16 | 0 / 8 / 8 |
| plan_review | Framework `tools/plan_review_guard.py`, tests `plan_review_guard_test.py` (+ `plan_review_guard_fixtures/`) | 29 | 2 / 8 / 19 |
| kor-enforce | Operations `Kor.Operations.Framework/Enforce/` (checks are rows in KorStandards `brain.Check`, cached locally) | 117 | 4 / 4 / 109 |
| board | Framework `tools/claude_board_guard.py`, tests `claude_board_guard_test.py` | 26 | 1 / 6 / 19 |
| kor-stall | Operations `Kor.Operations.Framework/Stall/`, tests `StallTests.cs` | 7 | 1 / 1 / 5 |

The worker's classes, to check:
- **context-read:** wrong = the same content read anyway in a cut form (a big Read refused, then read as 300 + 300 lines;
  a picture refused, then re-encoded under 50,000 bytes or cropped and read in the main session); right = the read moved
  to a helper, or a smaller crop / the PDF text read instead. 8956/8957: the session was not allowed subagents.
- **start-docs:** 6 of 8 wrong were mid-session read-only commands (date, git log, ls, a board query) refused because
  the handover's sections had not been re-read since a compaction; the guard's own header says that re-read is required.
  2 (8607/8608) were the start routine's own first read, done with Bash `sed` instead of the Read tool.
- **plan_review:** 7 of 8 wrong were helper briefs or board writes that, once reviewed, came back GO; 8442 was an
  `accept` carrying Ian's typed reason, refused because the hook judged the whole command; 8980 a CHANGE only because the
  reviewer's own search was unfinished.
- **kor-enforce:** 8588 a check (#978) retired in the DB but still enforced from `kor-checks-cache.json`; 8589 check
  #3982 read the literal `$f` of a loop; 8902 a worktree only mentioned in `--evidence` text; 8587 approved worktree adds.
- **board:** wrong = a conditional future ("if 302N doesn't answer, the lane marks row 2084 blocked") read as a claim
  that contradicts the board; 8927 a reply that stated the board's state explicitly.
- **kor-stall:** 9007 a rerun after the script had been edited to fix the error (the guard's header says an edited
  script is a fix, not a repeat).

## Population and the held-back check

Judge every refusal with `RefusedAtUtc < '2026-11-21T00:00:00-07:00'` for these six guards (for kor-context-read and
kor-enforce, if recovering and judging every row is not bounded, judge all rows since `2026-11-12T00:00:00-07:00` plus a
stated random sample of the earlier ones, and say so). **Do not open, query or judge any row at or after 21 Nov 00:00
-07:00 for these guards: those are the author's held-back check.** Replay your judged population before and after each
change; report X of Y recovered and every verdict that changes.

## What you may and must not touch

- Two worktrees, branch `step3-narrow6` in each: `C:\VIsual Studio Projects\Operations-step3-narrow6` (at develop
  a2784d88; the C# guards and this brief) and `C:\VIsual Studio Projects\Framework-step3-narrow6` (at Framework main
  7a3b0a8; plan_review and board). Edit only these six guards and their tests.
- READ-ONLY database access, SELECTs only, through `python -I tools/SqlQuery/oq.py -q "SELECT ..."` (Operations), on
  `KorStandards.oversight.GuardRefusal` and, for kor-enforce, `KorStandards.brain.Check` (read sys.columns first).
  A change kor-enforce needs in a `brain.Check` row (retire, replace, re-pattern) is NOT written: put the exact SQL in
  your response for the author to apply. The cache fault is code; fix it in code.
- Replays and tests must not log refusals or write the database: check how each guard logs (Python guards use Framework
  `tools/guard_log.py`, which writes nothing for a payload without `hook_event_name`; the C# guards log through
  `StallStore.LogGuardRefusal` - find what stops a test or replay from logging, and say how you made sure). Replay Stop or
  transcript-reading guards on copies, never on a live session's transcript.
- C# proof: `dotnet build` / `dotnet test` of `Kor.Operations.Framework.Tests` only (never piped without
  `set -o pipefail`), the guard's own tests, then the full Framework.Tests, compared with a base run by
  `kor testdiff <base.trx> <new.trx>` (the CLI project in this worktree). TEMP and TMP stay at their normal values,
  NOT inside a worktree: on 21 Nov a TEMP inside a worktree let `CaptureTests` write real rows.
- Commit with `git -c core.hooksPath=<an empty folder> commit ...` (the shared post-commit hook writes the database).
- Must not: push, install (the author installs kor), edit outside the two worktrees, change any other guard, settings
  file or git hook, write the database, run anything on another machine, open C:\Users\ilalonde\Documents\KOR-holdout.

## Write ONE response file

`docs/codex/CODEX-STEP3-NARROW-SIX-RESPONSE.md` in the Operations worktree, committed last. Per guard: verdict SOUND or
FAULT with its evidence; X right / Y wrong / Z not settled of N, with ids (and the sample rule if you sampled); what
changed (commits, in which repo) or why not; the before/after replay; for kor-enforce, any `brain.Check` SQL to apply;
repo rule 11 for each change (covers, does not cover, one same-class miss). Then: the C# test numbers and testdiff
verdict, anything wrong or missing in this brief, and every claim above you found false.
