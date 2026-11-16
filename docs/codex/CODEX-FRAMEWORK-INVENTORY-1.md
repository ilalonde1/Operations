# CODEX increment 1: inventory of every control mechanism, with facts only and no verdicts

Written 16 Nov 2026, 13:0x, by the Framework coordinator session (Claude). This is increment 1 of a series Ian set out on 16 Nov:
"Codex runs the tasks and Claude verifies. Logical increments ... smaller targeted prompts that you verify results then we build off of one at a time until this thing is RIGHT."

Write your answer to `docs/codex/CODEX-FRAMEWORK-INVENTORY-1-RESPONSE.md`. Put your scripts in `docs/codex/CODEX-FRAMEWORK-INVENTORY-1-work/`. Write nothing else, edit no code, and commit nothing.

## Why

Your earlier analysis, `docs/codex/CODEX-CLAUDE-WEAKNESSES-RESPONSE.md`, found the root weakness: "unreliable judgment about the sufficiency of its own evidence". Read sections 4 and 5 of it first.

Ian, 16 Nov: "there MUST be components of the already built framework we can use ... I don't want to waste time ... building something in parallel. We have SO MUCH of that and it needs to stop." So before anything is designed, we need an exact list of what already exists.

This increment is the LIST ONLY:
- no verdicts;
- no keep, fix or cut;
- no mapping to W1-W9.
Those are increment 2, and they build on this list once Claude has verified it.

## What counts as a control mechanism

Anything that can REFUSE, WARN about, RECORD or REQUIRE something in how work is done or what ships. Cover all six classes:

1. **Claude Code hooks.** Every hook entry in `C:\VIsual Studio Projects\{Framework,Operations,KOR.Drafter}\.claude\settings.json`, all events. Claude counted the entries by event on 16 Nov 13:0x:

   | Repo | SessionStart | PreToolUse | Stop | UserPromptSubmit | PostToolUse | FileChanged | PostToolUseFailure | PostCompact | SessionEnd |
   |---|---|---|---|---|---|---|---|---|---|
   | Framework | 7 | 29 | 7 | 4 | 4 | 1 | 1 | 1 | 1 |
   | Operations | 6 | 26 | 7 | 5 | 3 | 1 | 1 | 1 | 1 |
   | KOR.Drafter | 6 | 22 | 7 | 5 | 3 | 1 | 1 | 1 | 1 |

   Your list must reconcile with these counts entry for entry. If it differs, say where.
   Also cover the user-level `C:\Users\ilalonde\.claude\settings.json`, if it has hooks.
2. **Git hooks.** `.git\hooks\*` in the three repos: pre-push, post-commit and the rest. Skip `*.sample` and `*.bak*`.
3. **The `kor` command's verbs that act as controls:** enforce, recall, repeat, capture, start, state, record and any others. Source: Operations `Kor.Operations.Framework` (C#), and the built exe at `C:\KOR-Operations\kor\kor.exe`, whose verbs you can list with `--help`.
4. **Product gates:** code in the product's run, package or release paths that refuses to go on. At least:
   - KOR.Drafter `tools\virtual_drafter.py`: the pre-run gate, sweep_gate, plot_gate_stop, the stale-set waiver, release and promote;
   - `tools\gate_earliest.py`;
   - the takeoff `handoff-gate` verb (C#);
   - Operations `tools\build_peer_review_package.py` and its hand-off gate;
   - Framework `tools\blind_look_check.py` and `tools\kor_review.py`.
   Find the rest by searching for refusals: `SystemExit`, `raise SystemExit`, "refused", "REFUSED", "WITHHELD", an `exit 1` used as a gate, and `return 1` in gate verbs. State the search you ran and its scope.
5. **Database constraints and tables used as gates.** Read the migration SQL in the repo, not the live database. For example: CK_Commitment_Budget, CK_SetLook_Mode, oversight.Review used by pre-push, oversight.SetLook used by handoff-gate, and brain.Check. Migrations live in `C:\VIsual Studio Projects\KOR.Drafter\db\`.
6. **Sanctioned bypasses.** Every typed phrase or token that lifts a control, such as IAN-APPROVED-STATED, IAN-APPROVED-SKIP-SAMPLE and IAN-APPROVED-API-USE, and every "pass by name", "stated", "waiver", "allowance", "--because", "accept", "known_differences" or "answer" route that lets a refusal be cleared. Show where each is read in code.

## For each mechanism, one row

| Field | What goes in it |
|---|---|
| id | your own, e.g. H-FW-PRE-07 or G-VD-prerun |
| class | 1 to 6 |
| where wired | the file and line where it is registered or called. For a product gate: the CALLER, i.e. which command or run path reaches it. Name the call chain up to the user-facing command (e.g. `virtual_drafter.py run` -> run_stages -> prerun_precheck). If you cannot find a caller, write "no caller found" and the search you ran. |
| trigger | the event or condition that fires it |
| what it reads | the evidence it judges: the command text, a file's bytes, a DB row, a transcript, a hash... |
| what it does | refuse, warn, record, inject context, or require a phrase |
| bypass routes | every way past it: a phrase, a flag, a decision record, a waiver, an answer, an env var, or simply not running the route it sits on. File and line for each. |
| repos | where it is active (the same script may be wired in some repos and not others) |
| source | the file and line of its implementation |

## Rules

- Facts read from the source only. Every row cites file and line. When something is not read, write "not checked"; never infer.
- Reuse what exists rather than redoing it: `CODEX-CLAUDE-WEAKNESSES-work/history-stats.json` (hook counts), `Framework\docs\RULES-TO-CHECKS.md` (its guard tables), `Framework\docs\PHASE2-CENSUS.md` (reachability: which product code any job reaches). Cite them where they answer a field.
- Read-only. Do not run guards, hooks or product commands, except `--help` and `git` read commands.

## How Claude will verify this, before increment 2 builds on it

- A script re-parses the three settings.json files and checks that every hook entry appears in your list, and that the counts match.
- Every file:line you cite is opened, to check it says what you say.
- Every bypass route you list is read at its line.
- A content search is re-run for refusal points you may have missed. Anything it finds that your list lacks is reported back to you as a miss.

So write the response so this is easy: put the full table in the response, and also save it as `CODEX-FRAMEWORK-INVENTORY-1-work/inventory.csv`, with the same columns.

## Output

1. The table, as above.
2. Counts by class and by repo, X of Y.
3. A list of mechanisms whose caller you could not find.
4. A list of every bypass route, with where it is read.
5. What you did not cover, and why.
