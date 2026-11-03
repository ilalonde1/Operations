# Operations - HANDOVER

## STATE NOW - 2 Nov 2026 20:55 BCDT (`date`), written by the Framework lead. Read first.

**The Operations lead has not run a session under this handover yet.** At its first close it replaces this block
with its own state. Until then, these are the verified facts (20:55):
- **Git:** `develop` = `origin/develop` = f716a752, 0 ahead, working tree clean. Everything is pushed.
- **Board:** Owner `Operations` has 0 open rows. When Ian asks for a change or an addition, FIRST add it as a row
  (What = the goal, DoneWhen = the finish line, Budget = time / runs / what it may read - the board refuses a row
  without a Budget), then work it, then close it with its evidence. Your rows show at every session start.
- **Memory:** in the database, lane "Operations" (562 notes; the BD section is the memory `index-bd` -
  `python -I "C:\VIsual Studio Projects\Framework\tools\kor_brain.py" show --lane Operations --name index-bd`).
  Write new memory with kor_brain (`.claude/CLAUDE.md` says how); the file memory folder is a frozen fallback.
- **Before any push:** every commit needs a fresh-session review -
  `python -I "C:\VIsual Studio Projects\Framework\tools\kor_review.py" review` (about $0.3-0.6 each); fix what it
  finds. The pre-push hook refuses an unreviewed commit. Pushing to GitHub: Ian has told the Framework lead to do it.
- **BD:** never the Anthropic API (a guard refuses it in the Director / Knowledge / CLI code and in Codex briefs).
  Deploying the BD app to APP01 is Ian's call.

Facts from enrollment, 16:01 (history):

- **Scope:** BD (BD Director, opportunities, research, dossiers), Finance (financials engine, Deltek data), the
  Operations app core (FileSync, email filing, transmittals, WPF), the MCP server (`Kor.Operations.Mcp`), contracts.
- **Moved out on 2 Nov:** the Virtual Drafter product (start in `KOR.Drafter`), NetworkOps + IT (start in
  `NetworkOps`), JoeBrain, IanBrain, Contract Radar, Deltek deadlines (each its own folder). Their code may still be
  in this repo; their plans and memory are not here.
- **Git, read 16:00:** branch `develop`, 3 commits ahead of `origin/develop` and NOT pushed: 55256aef (13:32),
  655e0454 (14:54), 88dd6483 (14:58) - path repoints and the hook ledger path from the 2 Nov folder moves.
  `origin` is GitHub, so pushing them is Ian's call. 9 untracked entries under `docs/` (Codex audits and
  `docs/agent-platform/`), not yet owned by anyone.
- **Board:** no rows with Owner `Operations` yet (`SELECT Owner, State, COUNT(*) FROM oversight.Commitment GROUP BY
  Owner, State`, 16:00). Rows owned by `operations-e4` are Halifax / Drafter work done in this code: the Virtual
  Drafter lead's, not yours.
- **Framework:** enrolled 2 Nov - start routine in `.claude/CLAUDE.md`, the guard hooks in `.claude/settings.json`,
  this handover. Framework pieces still to come (goal/done-when/budget, review before merge, stage gates,
  scorecard, work through the board) arrive from the Framework lead (`C:\VIsual Studio Projects\Framework`).

## Five questions to answer before any work
1. Where do things stand (the STATE NOW block, the board, the unpushed commits)?
2. What is next, in order?
3. Three traps (start with: this checkout is shared with the NetworkOps and Drafter leads - commit only your own
   paths and read the push range; `MEMORY.md` is shared - never trim it; never the Anthropic API - everything
   in-session).
4. Who owns what (you: BD, Finance, app core, MCP, contracts; NetworkOps/IT lead; Virtual Drafter lead; Framework
   lead; Codex: bounded tasks from a lead only; Ian: pushes to GitHub and live-system changes)?
5. What must you not touch (other leads' paths and commits; the Drafter's confidential product beyond its code
   here; live systems, migrations and GitHub pushes without Ian's yes)?
