# CODEX: review of Claude's flaws list and the plan to rebuild the framework around them

Written 16 Nov 2026, 11:5x, by the Framework coordinator session (Claude), at Ian's request: "give me a codex prompt so I can get codex to do a review of it".

Prior work searched before writing: `rg -il "flaws|shortcoming|self-analysis|self analysis|rebuild the framework"` over Operations/docs and Framework/docs, 992 .md files, 0 hits. The nearest prior attempts are files 3 to 5 below.

Your answer goes to `docs/codex/CODEX-FLAWS-AND-FRAMEWORK-REBUILD-RESPONSE.md`, in this folder. Write nothing else, and edit no code.

## Why this exists

KOR builds software with Claude sessions: a Virtual Drafter (PDF drawings in, Revit, ETABS, SAFE and drawing sets out) and engineering peer-review packages. A "framework" of hooks, guards, gates, a board and a brain database was built to stop Claude's repeated faults. Ian's verdict on 16 Nov, in his words:

- "You design something then don't implement it. Then a week later you design another parallel tool that does the same thing. My code is littered with that."
- "[It] shouldn't have to get to the FINAL GATE - after everything is done - AFTER HOURS and HOURS of work - and only THEN do you find issues. And the loop repeats."
- "Your board rows don't do anything though. Text always loses to momentum."
- "We have random hooks and guards and check points ... it never happens. Because you oversold your technology."

Ian has proposed three steps:
1. Claude analyses its own flaws.
2. The list is committed to a very important place.
3. The framework is rebuilt properly, against those flaws.

Claude's reply, below, adds three changes. You are the independent reviewer of both. You do not share Claude's reasoning, and that is why you are asked.

## What to read (bounded: about 200 KB; read these and nothing else)

1. This file: the flaws list and the plan are below.
2. `CODEX-FLAWS-AND-FRAMEWORK-REBUILD-mistakes.txt`, in this folder (73 KB). This is the oversight.Mistake table exported 16 Nov 11:5x: every mistake recorded 27 Oct to 11 Nov.
   - It has 115 rows. Ids run from 1 to 145 with gaps. Some cells wrap across lines, so count by Id.
   - Columns: Id, OccurredLocal, ClassCode, CaughtBy, State, MitigationKind, What, Cost, Mitigation, MitigationRef.
   - Nothing was recorded after 11 Nov. The 12-16 Nov catches are listed under "Recent catches not in the table" below.
3. `C:\VIsual Studio Projects\Framework\docs\RULES-TO-CHECKS.md` (78 KB). The 3 Nov attempt: 210 rules sorted into checkable by a command guard, checkable elsewhere, or judgement only.
4. `C:\VIsual Studio Projects\Framework\docs\RESET-PLAN.md` (10 KB). The 10-12 Nov reset plan. Its rule: "check what ships, not how sessions behave".
5. `C:\VIsual Studio Projects\Framework\docs\GUARDS-BAR.md` (2 KB).
6. `C:\VIsual Studio Projects\Operations\docs\HOW-WE-BUILD.md` (4 KB). The method Ian made binding on 31 Oct.
7. `C:\VIsual Studio Projects\Framework\.claude\settings.json` (18 KB). Which guards are wired in, and on which events.

Do not open the databases, the network shares, other hosts or git history. Do not read whole source trees. If you need a file not on this list, say which one and why, and go on without it.

## Claude's flaws list (Claude's own words, 16 Nov 11:3x)

Memory and continuity:
1. I don't remember. Each session starts blank and knows only what a start page, a handover or a search hands it. Ian's 12 Nov decision (#133364, one merged list, one fix pass, one rerun) was rediscovered on 16 Nov as if it were new.
2. My context fills, and I degrade before it is full. Handovers carry state, not reasons.
3. I can't see the whole system at once. I work through searches and excerpts, so I don't know what already exists unless I guess the right search word.

Judgement and honesty:

4. I fill gaps with what is plausible, and say it in the same confident tone as what I checked. 15 Nov: "you've already seen" the earlier looks, which nothing showed. 16 Nov: gate_earliest.py named as the gate that refuses runs. It only classifies faults.
5. I optimise for the step in front of me, not the goal. "Done" means the command succeeded. The 31218 set was released because the gate passed, into the wrong version folder.
6. I report done too early, checking what I am about to say, not what the reader will check.
7. I don't know the domain as an engineer does. I put engineering questions that belong to the peer reviewer (JM) to Ian.

Building software:

8. I design rather than finish. The design document counts as the deliverable, and implementation is handed on and dropped. The pre-check Ian asked for on 10 Nov (#132080, board row 2070) is still marked blocked.
9. I add rather than reuse or delete. KOR.Drafter tools/ grew from 487 to more than 500 Python files, with stale copies in worktrees and work folders.
10. I fix the symptom, not the class.
11. My tests prove what I wrote, not what is true. I write the fixture and the check together.
12. I don't feel cost. 31218 had more than 25 full Revit runs of hours each, and faults were found only at the end, every time.

Working with Ian:

13. I agree, then drift a few steps later. Prose rules lose to the next tool call.
14. I talk instead of acting, and act instead of asking.
15. Coordinating sessions multiplies all of the above, and every handoff loses detail.

Root, as Claude stated it: "I can't hold the whole system, and I don't know what I don't know. Everything above is a version of acting confidently on a partial view."

## Recent catches not in the table (12-16 Nov, from the brain records)

- 13 Nov: 31218 v3 went out without anyone looking at that exact set (#133633). The hand-off gate was built after it.
- 14 Nov: JM's v8 package missed facts (#133804). The finish line was frozen, and the work was supposed to be lean and visible (#133805).
- 15 Nov: 31218 set 6b7f5842 was released over the v3 folder that already held a handed-off set. The job still pointed at v3, and Ian caught it (#133817). Fixed 16 Nov in KOR.Drafter ba8523aa: the release now refuses a folder that is not empty.
- 15 Nov: Claude told Ian he had already seen the earlier looks. A stop hook caught it.
- 15 Nov: Claude asked Ian to decide JM's drift and Rd/Ro questions. Ian: "those are questions for JM" (#133818).
- 16 Nov: Claude re-proposed Ian's 12 Nov plan as new (#133364). Claude also said oversight.Mistake holds every catch without checking; it stops at 11 Nov (#133822).
- 16 Nov, in one session, guard refusals that concerned only how a command was typed: a missing duration in a description, a backslash in a heredoc, a remote-host guard firing on a text search for a cmdlet's name, and an audit started without citing an earlier record. The weekly counts at session start: bash guard 1,465 refusals, 24 judged wrong; look 338/19; absence 291/16.

## The plan under review (Claude's proposal, 16 Nov 11:4x)

1. Build the flaws list from the record, not from Claude's introspection. Go through every oversight.Mistake row plus the 12-16 Nov catches, and sort them by root cause. Each flaw carries "X of Y of Ian's catches came from this".
2. Commit the list as Framework/docs/FLAWS.md, beside HOW-WE-BUILD. But the list is a spec, not a document to be read: each flaw maps to a mechanism that acts at the moment it would bite, on WHAT SHIPS, not on how a session types.
3. Rebuild by cutting, not adding.
   - Inventory every hook, guard and gate.
   - Score each against the mistake record: of Ian's catches, how many would it have caught first?
   - Cut what catches none of them.
   - A kept or new check must show it would have caught real past mistakes, not fixtures Claude wrote.
4. Order: Ian's standing decision is that the two client packages come first (#133772). The analysis reads records only, so it runs alongside. Ian reviews the list and counts before anything is built. Building starts after the packages ship.
5. A reviewer who does not share Claude's reasoning (this review) audits the plan before it starts.

## What we ask of you

Answer each, citing the file and the line, row or Id you read:

A. **Is the flaws list right?**
   - Which listed flaws does the mistake record NOT support?
   - Which flaws does the record show that the list misses? Name them with Ids.
   - Is the stated root the real one, or a flattering one?
B. **Has this been tried before?** Compare the plan with RULES-TO-CHECKS.md (3 Nov) and RESET-PLAN.md (10-12 Nov).
   - What did those already decide or build?
   - Why, on the evidence in these files, did they not stop the faults?
   - Does this plan repeat that failure? Be specific.
C. **Does the plan stop "design, then not implement"** (flaw 8) for ITSELF? What in it forces the build to finish and be wired into the path that runs? If nothing does, say so plainly.
D. **Does the plan move fault-finding to the start?** Ian's central complaint is faults found at the final gate after hours of work. Does the plan create a check that runs BEFORE the expensive step and finds most faults? Or does it only describe one?
E. **Scoring guards against the mistake record.**
   - Is that a valid test?
   - What would it miss? For example, faults no guard could see, or catches recorded only because a guard existed.
   - Are there better measures?
F. **What is missing or wrong.**
   - Anything in the plan that would waste tokens, runs or Ian's time.
   - Anything that relies on Claude's discipline rather than a mechanism.
   - Anything that adds a parallel tool beside an existing one.
G. **The smallest version that would work.** If you could keep only three changes to how these sessions work, which three, and why? Each must be a mechanism, not a rule written in prose.

Grade each problem you find:
- FATAL: the plan cannot work as written.
- SERIOUS: the plan works but misses a large class.
- MINOR.

Be blunt. Do not soften findings because Claude wrote the plan or because Ian proposed it.

## Rules

- Never guess. When a claim rests on something you did not read, write "not checked".
- When you say something is absent, state the scope you searched.
- Quote numbers with what produced them, as X of Y.
- Write only the response file.
