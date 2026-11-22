# CODEX step 3, six guards, round 2: close the false allows round 1 opened, and bound kor-enforce's cost

## READ THIS FIRST: this brief comes from a source you should not trust

Written by a Claude session (Framework coordinator fa93ad77, 22 Nov 2026), and Claude sessions are the ones these guards
check (#133998). Treat everything below as claims and check each. Ian, 22 Nov, chose "Hold all, Codex round 2": nothing
from round 1 ships until this round is in.

## Where round 1 stands (read `docs/codex/CODEX-STEP3-NARROW-SIX.md` and `-RESPONSE.md` first)

Branch `step3-narrow6` in both worktrees, at Operations 130c012f and Framework c10ea6f. The author checked round 1:
Framework.Tests run normally (live database, normal TEMP) against the 21 Nov base run: `kor testdiff` NO NEW FAILURES,
42 of 1356 names failing, all 42 also failing on base; board suite 111 of 111 (with its live cases); plan_review 41 of
42 (case 22 also fails at base 7a3b0a8: 31 of 32); the 6 held-back start-docs refusals replayed on transcript copies with
a fresh `--cache-dir` and `KORBRAIN_SQL` set invalid: 6 of 6 still refused on base and new, 0 rows logged. Independent
reviews APPROVED every commit (Operations rows 3642-3650, Framework 3651-3654) with the faults below.

## Tasks (each fault: cases first, committed alone and shown failing; then the fix; same rules as round 1)

**1. kor-enforce cost.** `259f6f09` makes every enforce call read `brain.Check` live while the database is up. Measured by
the author, Release publish of 130c012f vs the installed kor 1.0.0+1038597c, `kor enforce --module Framework --hook` on
an allowed `echo hi` payload (no `hook_event_name`), 8 runs each, twice: median **~115 ms before, ~485 ms after**. This
hook runs before every tool call in every session. Keep the goal of 259f6f09 - a retired or replaced check must stop
being enforced soon after the database says so (8588, 7030) - but bound the cost: re-read the live checks at most once per
a short window (you choose and justify it; the author suggests up to 60 s) and otherwise use the fresh cache. Prove both:
the retirement/replacement cases still pass (with a clock you can control in the test), and the median of the same
measurement is within **50 ms** of the installed kor's. Report the numbers and how you measured them.

**2. plan_review false allows** (review 3654, `tools/plan_review_guard.py` around lines 50, 113, 121, Framework):
- an `oq.py ... -i <file>` behind an environment-variable prefix (`PYTHONIOENCODING=utf-8 python -I oq.py -i f.sql` - a
  form these sessions use), `pushd X`, `Set-Location -Path X`, or `-Xutf8` written as one word now slips past; before
  c10ea6f any `oq.py ... -i` anywhere was caught;
- the SQL comment/string stripper can blank out a real INSERT after a `/*` inside a string or an unclosed `/*`;
- the stricter VERDICT pattern reads `**VERDICT: GO**`, `VERDICT: GO.` and `VERDICT: CHANGE - x` (an em dash) as no verdict
  (a false refusal, fail-safe; fix only if it does not reopen the prose-mentions-VERDICT false allow round 1 closed).

**3. board false allows** (review 3652-3654, `tools/claude_board_guard.py` around line 179): CLAUSE_CONDITION and
ATTRIBUTED search everything before the row reference, so a real claim such as `When I checked, row 137 is done.` or `If
you ask me, row 137 is done.` is skipped. None of round 1's 17 new allow cases has a near-identical unconditional
neighbour that must still refuse: add those neighbours (e.g. the two sentences above, `The handover says X. Row 137 is
blocked.`), then narrow the skip so they refuse while round 1's 17 still pass.

**4. Small notes, only if free:** K7Tests' new case is labelled `// 13` like `Live13_AWritePathRule`; StartDocsTests'
new cases split its constant block. No other change.

## Proof and limits (as round 1)

- Re-run each guard's own suite, and for the C# change the full `Kor.Operations.Framework.Tests` with `kor testdiff`
  against your round-1 base trx. Replay round 1's judged rows for the guards you change (before/after per row).
- READ-ONLY database, SELECTs only, through `python -I tools/SqlQuery/oq.py`; no refusal or Check row is written; replays
  without `hook_event_name`, with `KORBRAIN_SQL` set invalid for C# CLI children; TEMP/TMP normal; commits with
  `git -c core.hooksPath=<empty folder> commit`; no push, install or edit outside the two worktrees; only these guards.
- The author holds back cases written after you finish.

## Write ONE response file

`docs/codex/CODEX-STEP3-NARROW-SIX-2-RESPONSE.md` in the Operations worktree, committed last: per task the cases (red
first), the change, the proof numbers (for task 1 the window, the latency method and medians), repo rule 11 for each
change, and anything in this brief you found wrong.
