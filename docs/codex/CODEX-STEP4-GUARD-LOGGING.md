# CODEX plan step 4: commit_scope_guard and board_time_guard refuse but never log, so neither can be shown live

## READ THIS FIRST: this brief comes from a source you should not trust

Written by a Claude session (Framework coordinator fa93ad77, 22 Nov 2026), whose work these guards check (#133998).
Treat everything as claims and check each. Ian, 22 Nov: no guard change without a Codex round.

## The finding (claims: check each)

Plan step 4 is "KOR.Drafter guards wired and live". The author checked on 22 Nov (`date` 16:20):
- **prior_art `--content-search`** is on KOR.Drafter's Write hook and fired live in a headless session started in
  KOR.Drafter: GuardRefusal #10684 (NO CONTENT SEARCH) and #10683 (PRIOR ART NOT CHECKED); nothing was written.
- **run_sample_guard**: GuardRefusal rows with Repo KOR.Drafter: 2 (last 12 Nov), Framework 20, Operations 2.
- **commit_scope_guard** and **board_time_guard** (Framework `tools/`, wired in KOR.Drafter and Framework
  `.claude/settings.json` as `python -I ".../commit_scope_guard.py" --hook` and `.../board_time_guard.py --hook`):
  **0 GuardRefusal rows under any name** (searched Guard LIKE '%scope%' OR '%time%' OR the exact names: 0 rows). Their
  code has no `guard_log.record` call (`grep -n "guard_log.record\|record(" commit_scope_guard.py board_time_guard.py`:
  nothing); each returns the PreToolUse deny JSON directly (commit_scope_guard.py ~252, board_time_guard.py ~707).
  So HOW-WE-BUILD F11 ("every guard is measured", Framework row 1159) does not hold for them, and no live refusal can
  ever prove them. board_time_guard itself refuses closing a guard row whose Evidence lacks a live GuardRefusal
  (~line 621) - a rule it cannot meet for its own row.

## Tasks

Work in `C:\VIsual Studio Projects\Framework-step4-guard-logging` (branch `step4-guard-logging`, at Framework main
a367fc3). Write the response in `C:\VIsual Studio Projects\Operations-step4-guard-logging\docs\codex\` (branch
`step4-guard-logging` at Operations develop 80935097).

1. **Cases first, committed alone** (in each guard's test file, red on a367fc3): a refusal of a live-shaped payload
   (with `hook_event_name`) calls the logger once with the guard's name and a short rule; a payload without
   `hook_event_name` (the tests' shape) logs nothing; a logging failure never changes the decision (the guard still
   denies and exits as before). Use an injected/monkeypatched recorder, never the database.
2. **Then the change:** each guard logs every deny through Framework `tools/guard_log.py` `record(...)` exactly as the
   other Python guards do (read how `Operations/tools/claude_bash_guard.py` `deny()` and `claude_absence_guard.py` call
   it: wrapped so any exception is swallowed). Guard names: use the names the board and `guard_log.py week` will show;
   say which you chose and why. No change to what either guard allows or refuses.
3. **Proof:** each guard's own suite before/after (state counts), and the existing suites of `guard_log.py` if any.
   Do NOT run a live probe: the author proves each guard live after install, in a session started in KOR.Drafter.

## Limits

READ-ONLY database (SELECTs only, via Operations `python -I tools/SqlQuery/oq.py -q`, if you need to see GuardRefusal's
columns); nothing written to it; payloads in tests without `hook_event_name`; TEMP/TMP normal; commits with
`git -c core.hooksPath=<empty folder> commit`; no push, install or edit outside the two worktrees; only these two
guards and their tests. The author holds back cases.

## Write ONE response file

`docs/codex/CODEX-STEP4-GUARD-LOGGING-RESPONSE.md` in the Operations worktree, committed last: the cases (red first),
the change, the suite numbers, repo rule 11, and anything in this brief you found wrong.
