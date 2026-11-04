# Operations - HANDOVER

## STATE NOW - 4 Nov 2026 09:43 BCDT (`date`), Operations lead, BD. Read first.

Evidence was read between 09:24 and 09:43 on 4 Nov, read-only. Nothing was built or deployed.

**Read before acting today: the morning hour is wrong on Banff.** Today's hour (DirectorHour 18) and its draft to Islam
say Banff Centre (#1142) closes Thu 5 Nov. APC's public feed says it was amended 26 Oct 21:24 UTC and closes
2026-11-19 14:00; `opportunities.Opportunities` 41243 agrees. Peace River (#1163): questions to 2026-11-06 17:00, closes
2026-11-13 14:00, and an amendment was opened on APC at 2026-11-04 16:32 UTC. Query:
`https://purchasing.alberta.ca/api/opportunity/public/2026/6968` and `/6999`. Cause of the hour's date: not traced.

**Where BD stands (live reads):**
- Board: 0 open rows for Operations. None of the items below is on it.
- Git: nothing unpushed at 09:43. Newest commits on BD code on any branch: f08f764e V30, 22 Oct. Later: contracts
  (f4feedfa, 939209dc, 33f5cbad, 28 Oct).
- Worker: version 1.0.9790.1290 = built 21 Oct 21:30. V30's email-format fix (22 Oct) has never been deployed. Result: 11
  BDALERTS-* junk rows since 22 Oct, 10 undismissed. Deploying is Ian's word.
- APC sign-in DEAD since 21 Oct 23:13 (`PortalSession.LastError` invalid_grant). ApcDocumentJob reports Success=1 on
  every run with "APC sign-in needed" in Summary, 1,002 runs in 7 days, so it reads green. Last APC fetch 21 Oct 14:48. A
  person must sign in: My Pursuits, More, Sign in to APC. Grabbed APC tenders get no documents or addenda until then.
- Morning hour: written every weekday, ids 11-18 for 26 Oct to 4 Nov. Emailed 06:05 to 30 Oct, 07:05 on 2, 3, 4 Nov, so the
  email has run an hour late since the zone change. Cause not checked. `DirectorHourMove` has 0 rows: no move ever recorded.
- Pipeline: 161 live, 0 without owner, 151 with no next action date, 5 tenders. `CrmActivities` since 22 Oct: 0.
  BdAsk: 39 open, 2 done, newest 21 Oct. BdMeeting: 1 row, the 9 Oct meeting. DirectorBrief: one week only, 2026-10-19.
- Scoring: 5 Brief decisions Pending, first due 2026-11-17 UTC.
- Screen opens since 22 Oct: Ian 29, unnamed 11, Conor 4 (last today 07:54), Rory 3, Islam 1. Omar, Jim, JM: 0.
- Other: SamGov 401 on the 07:00 run. Three jobs failed once in 7 days (KorPursuitDeltekSync and
  CanonicalOrgKorProjectSignalRefresh 2 Nov, LiveOppDetailEnrichment 30 Oct).
- AR on inactive projects: $816,243 today (ImpactSnapshot ArInactive); total owed $2,228,254.

**OPEN items in the records, checked 4 Nov:**
- Closed: Leduc submission recorded (#1112 Stage 3, SubmittedFileId 18, 13 files). Residue: opportunities 39322 and 39332
  still carry WonLostOutcome 3 (No Bid) with Status 5, and #1112 hangs off dismissed row 39322, not live 39332.
- Closed: doctrine cards awaiting Accept/Reject (BD 184 accepted, 30 retired, none proposed). Worker waiting on GovCanada
  (later deploys went out).
- Still open, read live: tier-4 rows 76; pursuits 860, 930, 973, 1003, 1033 Lost with reason "Fee proposal accepted"; claim
  22760 not retired or reviewed; Making Things Happen not in knowledge.Source; no NBC 2020 source; no weekly M: sweep job and
  newest claim 18 Oct; contracts page 2 not built (only a comment names it); held duplicates K173-00 and Whitehorse.
- Still open, nothing recorded to close them: EGBC template markups, fee-review thresholds, inactive-AR decision, standard
  KOR proposal plan, live sweep write paths (no sweep script in tools/), engineer review of the course.
- NOT VERIFIED: the email to the six BD people. Outlook would not attach (0x800401E3) and I did not launch it. KorEmailIndex
  holds 0 of Ian's mail since 20 Oct, so finding none proves nothing. Ian's Sent Items settle it.
- Not checked: Rory's app log (needs a run on his PC); EmailFiler disabled list; restyle dark mode. Drafter's, not checked:
  Bluebeam shape reader, Omar v6, Sheets tab release, spellcheck dictionary.

**The uncommitted Kor.Operations.Framework work is not BD's.** It is G5 row 1474 (context cliff), by a helper of the
FrameWork Design lead, session d0313f99. 12 of 12 changed files match its patch list (scratchpad g5\ops-g5-code.patch, 21:52
on 3 Nov) and Framework docs/HANDOVER.md item 2 claims it. Settings hooks are in stash@{0}. Leave all of it.

**Next, in order:** (1) Tell Ian the Banff date. (2) Peace River go/no-go, questions by 6 Nov. (3) APC sign-in. (4) Ian's
word on a Worker deploy. (5) Put the open items on the board. (6) Trace the hour's Banff date, the 07:05 email and the
green-but-dead APC job, one check each.

**Dates ahead:** 13 Nov Peace River; 17 Nov first brief scoring; 19 Nov Banff; 21 Nov E&O and GL policies expire (COI read 14 Oct).

**Memory read for this block:** 321 of 684 lane rows in full (320 live + retired project_bchousing_source_2026_10_16); every
line of the list judged; 363 left unread as not BD. Treat the to-do note project_bd_director_todo as history: it still says
Banff closes 5 Nov.

## STATE at 2 Nov 2026 20:55 BCDT (history), written by the Framework lead.

**The Operations lead has not run a session under this handover yet.** At its first close it replaces this block
with its own state. Until then, these are the verified facts (20:55):
- **Git:** everything up to this handover commit was pushed (origin/develop f716a752 at 20:55). Read
  `git status -sb` for now - this line cannot describe its own commit.
- **Board:** Owner `Operations` has 0 open rows. When Ian asks for a change or an addition, FIRST add it as a row
  (What = the goal, DoneWhen = the finish line, Budget = time / runs / what it may read - the board refuses a row
  without a Budget), then work it, then close it with its evidence. Your rows show at every session start.
- **Memory:** in the database, lane "Operations" (562 notes; the BD section is the memory `index-bd` -
  `python -I "C:\VIsual Studio Projects\Framework\tools\kor_brain.py" show --lane Operations --name index-bd`).
  Write new memory with kor_brain (`.claude/CLAUDE.md` says how); the file memory folder is a frozen fallback.
- **Before any push:** every commit needs a fresh-session review -
  `python -I "C:\VIsual Studio Projects\Framework\tools\kor_review.py" review` (about $0.3-0.6 each); fix what it
  finds. The pre-push hook refuses an unreviewed commit. GitHub pushes stay Ian's (question 4 below); he has had the
  Framework lead push for him - commit, get the review, and leave the push to the Framework lead or Ian.
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
