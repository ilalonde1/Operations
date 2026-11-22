# Step 4 guard logging response

Implemented and tested locally on 22 November 2026. Both guards now attempt one log record per live-shaped hook denial, without changing their decisions, refusal JSON or exit behavior. Installation and live KOR.Drafter proof remain with the brief's author.

## Commits and scope

Both worktrees use branch `step4-guard-logging`.

1. Framework `cb0f855` — cases committed alone, with both production guards still byte-identical to `a367fc3`. Also adds the board suite's `--offline` switch and disables hooks for its sibling suite's temporary Git commits.
2. Framework `12697c2` — logging implementation, ten added lines in each guard.
3. This response is committed last, in Operations.

Framework's complete change set is `tools/commit_scope_guard.py`, `tools/commit_scope_guard_test.py`, `tools/board_time_guard.py` and `tools/board_time_guard_test.py`. Operations changes only this response. `git diff --check` passed. No full solution build, push, installation, settings edit, live hook probe or database write was performed. TEMP/TMP were not changed. Repository commits used `git -c core.hooksPath=<verified empty folder> commit`; temporary fixture commits also use an empty hooks folder.

## Checked before changing code

Searched Framework `tools/` for both guards, `guard_log`, recorder calls and related tests; Operations `tools/claude_bash_guard.py`, `tools/claude_absence_guard.py`, prior-art and run-sample logging; and Operations `docs/codex/` for the brief and response conventions. Read the two guards and their suites, `guard_log.py` and its suite, repository guidance, and the relevant hook configurations. Existing logging calls supply the implementation pattern; no new logger or shared tool was needed.

The source at Framework `a367fc3` has one deny-JSON return in each hook and no recorder call in either guard. Both configuration files examined wire commit scope to Bash/PowerShell and board time to Bash/PowerShell/SendMessage using `python -I ... --hook`. KOR.Drafter's Write hook includes prior art `--content-search`.

Read-only queries used Operations `python -I tools/SqlQuery/oq.py -q`. Results:

- The predicate `Guard LIKE '%scope%' OR Guard LIKE '%time%' OR Guard IN ('commit_scope_guard','board_time_guard')` returned zero rows before the change; a final `COUNT(*)` with the same predicate also returned zero. This confirms that predicate, not an exhaustive search for arbitrary alternative names.
- GuardRefusal 10683 is `prior_art`, `PRIOR ART NOT CHECKED`, Repo `KOR.Drafter`, with a session ID; 10684 is `prior_art`, `NO CONTENT SEARCH`, also KOR.Drafter with a session ID.
- `Guard LIKE '%sample%'`, grouped by Guard and Repo, returned `run-sample`: KOR.Drafter 2 (latest 12 November), Framework 20, Operations 2, and NULL Repo 2. The three named-repo counts in the brief are correct.
- Commitment 1159 names F11, every guard measured through refusal logging. Rows 1211, 1966 and 1967 identify the commit-scope and board-time work/install requirements.

The stored prior-art rows and configuration support the reported refusals and wiring. I did not independently inspect those probe sessions' launch conditions or filesystem effects, so the additional claims that they were headless and wrote nothing are not asserted as newly verified here.

## Cases first, red on the original guards

The same differential contract is added to each guard's existing test file. Every case runs through `main(['--hook'])`, capturing the full stdout, stderr and returned exit code. A fixed clock makes board-time output deterministic.

| Guard | Denial cases | Allowed controls |
|---|---|---|
| Commit scope | Bash and PowerShell; each command contains two offending commits, requiring one recorder call for the hook refusal | A session's named move; an unrelated tool |
| Board time | Bash and PowerShell future Evidence; unreadable evidence file; SendMessage future time, unread ID, and both faults together; W7 close without a live refusal; W7 unreadable board | Past Evidence; a read message ID; W7 close citing a fixture refusal |

Each case compares a fixture baseline with six modes: missing `hook_event_name`, empty marker, recorder returns True, returns False, raises RuntimeError, and logger import fails. Live-shaped denied inputs must call the injected recorder exactly once with the expected guard name, short rule, matched command/message and complete payload. Missing/empty markers and allows must make no call. All modes must preserve the complete output and exit code. Assertions check the attempted call even when the recorder raises, so silently skipping the logger cannot pass the failure test.

Each suite also starts an isolated `python -I` subprocess. It imports the actual sibling `guard_log.py`, intercepts the import to replace `record` before any call, and uses a stub judgement. This verifies sibling-module resolution without exercising a live hook or database. It would catch an import that succeeded only because the parent test suite had added `tools/` to `sys.path`.

Against the unchanged production guards, commit scope failed six recorder assertions plus the isolated import/call case; board time failed 24 recorder assertions plus its isolated case. The differential diagnostics reported `output_unchanged: True, calls: []`. These failures were observed before commit `cb0f855`, and before either implementation edit. `git diff a367fc3 cb0f855 -- tools/commit_scope_guard.py tools/board_time_guard.py` is empty.

## Implementation

Each hook calls the sibling `guard_log.record(...)` immediately before its existing deny return, only when `hook_event_name` is truthy. Import, argument preparation and recording are inside `try/except Exception`, following the existing Python guards. No judgement/parser code or deny JSON changed.

The names are **`commit_scope`** and **`board_time`**: the descriptive script stems without `_guard`, consistent with short logger identifiers such as `prior_art` and `bash`. These exact strings become the board/weekly grouping names. W7 and SendMessage refusals belong to `board_time`; their rule text distinguishes them.

The rule is the first line of the refusal, capped at 200 characters, matching Framework's existing board-guard convention. Matched text is the command, or `tool_input.message` for SendMessage. Passing `payload=payload` preserves the logger's existing session/repository extraction and redaction path. The sibling directory is made importable inside the protected block so `python -I` works. The existing logger remains unchanged.

## Proof numbers

Commands run from the Framework worktree:

```text
python -I tools/commit_scope_guard_test.py
python -I tools/board_time_guard_test.py --offline
```

| Stage | Commit scope | Board time |
|---|---:|---:|
| Existing cases against `a367fc3`, before adding logging cases | 18 passed / 0 failed | 328 passed / 0 failed |
| Added cases against unchanged `a367fc3` production code | 40 passed / 7 failed | 381 passed / 25 failed |
| Implementation `12697c2` | 47 passed / 0 failed | 406 passed / 0 failed |

The baseline used the empty-hooks fixture adjustment and offline test setup, with no logging cases or production edits. Final additions are 29 commit-scope checks and 78 board-time checks. The red runs exited 1; baseline and final runs exited 0.

Board time's live audit was explicitly skipped in every run. `--offline` supplies an empty board for unmodelled rows and still executes the existing W7 file fixtures, including the unreadable-board branch. These counts are offline suite counts, not live-board coverage.

**Existing logger suite:** `tools/guard_log_test.py` is a live integration script. Its full run INSERTs refusal rows, UPDATEs judgements, attempts another INSERT and writes an error log outside these worktrees. Running it conflicts with the brief's limits. It was not run in full and is not reported green.

Executed its unchanged pure section, bounded by the explicit `# ---------------------------------------------------------------- live round trip` marker: **9/9 passed** (seven redaction cases, truncation and None). The execution supplied the original test filename as `__file__` and replaced `subprocess.run` with a forbidden-access stub. Separately, the real `record()` returned False for a payload without `hook_event_name`, with both connection lookup and subprocess calls asserted untouched: **1/1 passed**. No logger source/test edits were made. Database round trips, table constraints, judgement updates, DB timeout behavior and the suite's live subprocess section remain unrun.

## Repo rule 11

**Fault class:** a guard emits a hook denial without traversing the refusal logger, leaving the denial unmeasured.

The two test matrices check both known instances before fixing either, then compare output/exit across logging outcomes. They cover the enumerated hook routes, call cardinality and arguments, fixture suppression, allowed controls, exception/import isolation and actual sibling resolution under `-I`. They compare the full refusal text, not merely the decision field. The parsed JSON and differential diagnostics were inspected; there is no graphical deliverable.

They do not cover hook installation, a fresh KOR.Drafter session, SQL persistence or weekly aggregation, logger latency/redaction failures, every shell/grammar spelling, or other guards. Same-class miss: a third guard can still deny without calling `record`; these two suites would not detect it. An untested new denial return inserted before the logging block could also escape this matrix. The existing logger's shared limitations, including a DB outage losing a row while preserving the refusal, remain.

## Corrections and qualifications to the brief

- Framework started at the stated `a367fc3`. Operations actually started at **`07a4ed01`**, the brief commit whose parent is the stated `80935097`.
- The logger suite exists and is database-writing. The board-time suite also performs live board reads and an audit by default. The offline switch and partial logger run are necessary to honor the stricter read-only/no-live-probe limits; they are disclosed rather than presented as full integration proof.
- W7 does **not** verify that a cited GuardRefusal belongs to the control being closed. Its current code accepts any qualifying cited live refusal, a limitation already stated in its header. Thus “cannot meet its own rule” is too strong as a description of current enforcement. Without logging, these guards cannot produce their own refusal evidence; the underlying finding is correct.
- The brief's “payloads in tests without `hook_event_name`” is followed for ordinary fixtures. Its explicitly requested live-shaped cases use the marker only with an injected recorder, including the isolated import checks.

No live proof is claimed. The author can install and verify the two exact logger names in a session started in KOR.Drafter, as the brief assigns.
