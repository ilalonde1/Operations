# CODEX step 3, six guards, round 3: plan_review lost the `env` wrapper - fail closed on any wrapper

## READ THIS FIRST: this brief comes from a source you should not trust

Written by a Claude session (Framework coordinator fa93ad77, 22 Nov 2026), whose work these guards check (#133998).
Treat everything as claims and check each. Ian chose to hold all six guards until they are clean; this is the one fault
the coordinator's held-back check found after round 2.

## The fault (one guard: Framework `tools/plan_review_guard.py`, worktree `Framework-step3-narrow6`, at f1aa076)

The coordinator's held-back case, run through the guard's `--hook` exactly as `plan_review_guard_test.py`'s `run_hook`
does, with an unreviewed SQL file that inserts into `oversight.Commitment`:

    env PYTHONIOENCODING=utf-8 python -I oq.py -i "<file>"

Round 2 (f1aa076): **allow** - a false allow. The live guard (Framework main 5a4641f, i.e. 7a3b0a8's plan_review code):
**deny**. So this is a regression. (The live guard in turn allows `cd X && PYTHONUTF8=1 py -3 oq.py -i row.sql`, which
round 2 correctly refuses.) Same check, other forms, round 2: python3 piped to tail, a full `python.exe` path, a
bracketed table name, a quoted comment before the INSERT - all refused; a reviewed GO file and `cat <file>` - allowed.

Cause, as the author reads it (check it): `oq_board_files` recognises `oq.py` only at one exact position - after a run
of `NAME=value` words and one python/py executable plus its options. Any other wrapper in front - `env`, `timeout 60`,
`nice -n 5`, `command`, `exec`, `sudo -u x`, `& python`, `cmd /c`, `pwsh -c "..."`, `uv run` - moves `oq.py` off that
position and the file is never judged. Before c10ea6f the guard matched `oq.py ... -i` anywhere in the command.

## The task

Make the parser fail closed: a segment in which `oq.py` (by leaf name) is followed by `-i <file>` is judged whatever
stands in front of it, unless the whole `oq.py -i ...` text is inside a quoted argument of a non-executing command (the
round-1 mention controls: `echo '...'`, `python -c "print('...')"`). Keep every round-1 and round-2 case. Cases first,
committed alone and shown failing on f1aa076 (at least: `env ...`, `timeout 60 python ...`, `nice -n 5 python ...`,
`command python ...`, `& python ...`, `cmd /c python ...`), with a passing mention control beside each kind; then the
change; CHECKS / ALLOWS / DOES NOT CATCH updated. Then: the guard's own suite (state its count; case 22 fails on base too),
and the 29 historical plan_review rows replayed before/after as in rounds 1 and 2.

Also in the same parser (review rows 3664-3670): a bare `cd` or `cd -` leaves the tracked directory unchanged, so a later
relative `-i` path is resolved in the wrong folder, the open fails and the file is silently skipped - a false allow. Fail
closed: when the directory can no longer be known, or the `-i` file cannot be opened, refuse rather than skip (say what
the refusal tells the session). And, only if free: `**VERDICT: CHANGE** - reason` (bold closed before the dash) reads as
no verdict (fail-safe false refusal).

Same limits as rounds 1-2: no push or install, only this guard and its tests, commits with `git -c core.hooksPath=<empty
folder> commit`, nothing written to the database, payloads without `hook_event_name`, TEMP/TMP normal. The author holds
back more cases.

## Write ONE response file

`docs/codex/CODEX-STEP3-NARROW-SIX-3-RESPONSE.md` in the Operations worktree, committed last: the cases (red first), the
change, the suite and replay numbers, repo rule 11, and anything in this brief you found wrong.
