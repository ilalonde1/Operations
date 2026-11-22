# CODEX step 3 guards review: four guards handled on 21 Nov, reviewed by you, and finished where the review finds a fault

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by a Claude session (Framework coordinator fa93ad77, 21 Nov 2026), and Claude sessions are the ones these
guards check. Ian asked that every Codex brief from a Claude session say so up front (#133998). Three of the four items
below were decided or built by that session, so this is an independent review of its own work: treat every statement as
a claim, check each against the code, the transcripts and the database, and say plainly where it is wrong. Ian, 21 Nov:
"I'd rather not skip the codex review component."

## Prior work (searched by the author: `rg -il "prior_art|absence-log|claude_absence_guard|PipedBuild|HeredocEscape"
docs/codex/` over 357 top-level entries, recursive: 66 files; none is a review of the 21 Nov changes)

- `CODEX-BASH-HEREDOC-NARROW.md` and `-RESPONSE.md`: item 1's own brief and report.
- `CODEX-FRAMEWORK-INVENTORY-2-RESPONSE.md`: earlier verdicts per guard, including prior_art "KEEP (provisional)" (five
  mistake records establish the class: 7, 44, 115, 141, 145) and bash "FIX" (narrow heredoc escaping and build pipelines).
- `CODEX-CLAUDE-WEAKNESSES-RESPONSE.md` (guard tables), `CODEX-FRAMEWORK-GAP-ANALYSIS-1-RESPONSE*.md` (look/absence made
  log-only; prior-art PARTLY), `CODEX-FRAMEWORK-STEP5-PLAN-AUDIT-RESPONSE.md` C5 (absence returns before blocking).
Read what bears on each item; do not redo their population counts unless you need them.

## The four items

Plan step 3 is: narrow each noisy guard so it stops refusing good work, but never leave its goal unchecked (Ian: no
guard is cut). All four live in `tools/` of this repo (Operations); refusals are logged in KorStandards
`oversight.GuardRefusal` (`RefusedAtUtc` holds LOCAL time, UTC-7; `Matched` is cut at about 400 characters, so full
commands come from the session transcripts under `C:\Users\ilalonde\.claude\projects\` or
`C:\Users\kor-claude\.claude\projects\`, main `<session>.jsonl` or `<session>\subagents\agent-*.jsonl`).

**1. bash guard rule 3b, heredoc escapes** (`tools/claude_bash_guard.py`, class `HeredocEscape`; goal: Operations
`CLAUDE.md` rule 7). Narrowed by a Codex session; commits 02e78c68, a2294314, 9d843e32. Claims: guard tests 222 of 222
then; replay of 119 of 119 logged refusals: 114 deny->allow, 5 still deny; 4970 allow->deny. A Claude reviewer approved
it with these open points (check each; a false ALLOW is a fault to fix, a false refusal or a comment is a note):
- a heredoc inside a double-quoted `"$(...)"` is no longer scanned at all (the old matcher scanned it);
- `$((1<<2))` on a header line can be read as a heredoc header and swallow the rest of the command as its body;
- `python_stdin` knows only the env/command/time/exec/timeout wrappers (not `uv run`, `sudo`, `nice`, `xargs`, a pipe);
- a closing delimiter with spaces around it is no longer recognised;
- the committed test fixtures hold real transcript text (paths, profile names, notes);
- the response's own same-class misses: `'@"\n"'` through Python, and `'work\new'`.

**2. bash guard rule 8, a piped dotnet build without pipefail** (same file, class `PipedBuild`; goal: Operations
`CLAUDE.md` rule 13). Changed by the Claude coordinator itself, not by Codex: commits 179d6230 (cases), 27318925, 8d654575.
It now accepts a read of `$PIPESTATUS` / `${PIPESTATUS[..]}` in the very next command, not inside a `( )` or `{ }` group;
a build's pipeline stops at `&&`; `{` is a command position. Claims: guard tests 237 of 237; replay of 113 of 116 logged
refusals: 14 of 14 judged right still refused, 10 of 10 judged wrong (since 12 Nov) now allowed, 6 unjudged deny->allow
(each a `${PIPESTATUS[0]}` in the next command), 18 unjudged still refused; not recovered 4290 (a PowerShell call, not
checked), 8447 and 8543 (probe sessions). Open points from its review: the `{ }` test case passes for a reason other than
the one its comment states, and the brace half of the group check may be dead code; the rule comment says a background
`&` does not end the pipeline while `END` treats it as an end; a `$( ... )` command substitution counts as a group.

**3. prior_art guard, KEPT unchanged** (`tools/claude_prior_art_guard.py`; goal: Operations `CLAUDE.md` rule 1, the
absence claims Ian caught on 8 Oct, 4 Nov and 10 Nov - read its header). Ian chose to keep it on the coordinator's
reasoning: 29 refusals in 16 days (5 Nov to 20 Nov); a worker judged 6 of them "wrong" because the forced search found
nothing and the session wrote the same file again; the coordinator called that the guard working as built (it forces a
look; a look that finds nothing is the expected case), said 27 of 29 hit exactly what the guard targets (new Codex
briefs, migrations, plans, guard tests), and that 8622/8623 are one Write logged twice. 23 of the 29 were never judged.
(It refused this very brief once, at 21 Nov 21:5x, for no content search; the author then ran the search above.)

**4. absence-log, SKIPPED** (`tools/claude_absence_guard.py`; goal: a reply that claims something is absent must show
the check). Since Ian's 17 Nov "Yes" it never blocks: it logs a row and prints nothing (the `KOR_GUARD_BLOCK` lines near
its end); his words then were that it stays that way "until rebuilt to check artifacts, not words". The coordinator
skipped it because it costs no turns. Its log since 19 Nov: 30 rows (Guard `absence-log`): 2 judged right, 26 wrong, 2
not judged.

## Tasks

Work in `C:\VIsual Studio Projects\Operations-step3-guards-review` on branch `step3-guards-review`, at develop 8d654575.

**For each item:** read the guard and its tests, recover and judge the refusals yourself from their transcripts (a
refusal is RIGHT when the command or reply really had the fault the guard names; WRONG when it did not), and give a
verdict: SOUND, or FAULT (a false allow that leaves the goal unchecked, or a misfire class that keeps costing turns),
with the cases that show it. For items 1 and 2, re-run their proof yourself (tests and replay); do not take the numbers
above on trust. For item 3, say whether "keep" is right. For item 4, say whether "skip" is right and whether its log is
measuring anything useful.

**Where you find a FAULT, finish it**, in the method of the earlier rounds (`docs/codex/CODEX-KOR-*-NARROW*.md`,
`CODEX-BASH-HEREDOC-NARROW.md`):
- cases first, committed alone (`step3 review <guard> A: cases (codex)`), each new case shown failing on the current code;
- then the change (`step3 review <guard> B: <one line> (codex)`), with the guard's own CHECKS / ALLOWS / DOES NOT CATCH
  comment updated to match; never amend A after starting B; if a case must change, a new commit saying why;
- every existing test in the guard's test file unchanged and passing; no MUST REFUSE case turned to pass;
- no new tool, guard or rule. For item 4: no rebuild, and it stays log-only (Ian's ruling) - a narrowing of its word
  list is allowed only if you show it misfires; otherwise report and change nothing.

A FAULT you judge not worth fixing now: say why in one line and leave it. Items you find SOUND: change nothing.

## What you may and must not touch

- READ-ONLY database access, and only SELECTs on `KorStandards.oversight.GuardRefusal`, through the SQL login in
  `tools/SqlQuery/oq.py` (`python -I tools/SqlQuery/oq.py -q "SELECT ..."`; `[Rule]` needs brackets).
- Replays and tests must not log refusals: Framework `tools/guard_log.py` writes nothing for a payload without
  `hook_event_name` (check it; build every replay payload without it). The absence guard is a Stop hook reading a
  transcript file: replay it on copies, never on a live session's transcript. Say how you made sure nothing was written.
- Keep TEMP and TMP at their normal values, not inside this worktree (a kor round on 21 Nov let a test write real rows).
- Commit with `git -c core.hooksPath=<an empty folder> commit ...` (the shared post-commit hook writes the database).
- Must not: push, install, or edit outside this worktree; change any guard not named here, any settings file or git
  hook; write the database; run anything on another machine; open C:\Users\ilalonde\Documents\KOR-holdout.

The author is holding back further cases for a check after you finish.

## Write ONE response file

`docs/codex/CODEX-STEP3-GUARDS-REVIEW-RESPONSE.md`, committed last. For each of the four items: the verdict (SOUND or
FAULT) with its evidence; the refusals you judged (X right / Y wrong / Z not settled of N, with ids); the re-run proof
numbers for items 1 and 2; what you changed (commits) or why not; repo rule 11 for anything you changed (what it covers,
what it does not, one same-class fault it still misses). Then anything wrong or missing in this brief, and every claim
above that you found false.
