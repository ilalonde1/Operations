# CODEX: an independent, evidence-based analysis of Claude's weaknesses (and strengths) as a software developer

Written 16 Nov 2026, 12:0x, by the Framework coordinator session (Claude), at Ian's request.

Your answer goes to `docs/codex/CODEX-CLAUDE-WEAKNESSES-RESPONSE.md` in this folder. Scripts and working files go under `docs/codex/CODEX-CLAUDE-WEAKNESSES-work/`. Write nothing else, edit no code, and commit nothing.

## Why this exists, and why the first attempt failed

Ian runs a structural engineering firm, KOR. For four months he has built software with Claude Code sessions. The products:

- The Virtual Drafter: PDF drawings in; Revit, ETABS and SAFE models and drafted drawing sets out.
- Engineering peer-review packages.
- A business-development platform, and others.

To stop Claude's repeated faults, a "framework" of hooks, guards, gates, a work board and a "brain" database was built around the sessions. Ian, 16 Nov:

- "Your limited looks do SO MUCH DAMAGE IT'S INCALCULABLE. I NEED THAT TO STOP. WHICH IS THE VERY REASON I WANT TO BUILD THIS FRAMEWORK - amplify your strengths and mitigate your weaknesses. But the first step in that is identifying your weaknesses."
- "It's the one opportunity to do a true self analysis using another model."

A first Codex review was run an hour ago: `CODEX-FLAWS-AND-FRAMEWORK-REBUILD.md` and its `-RESPONSE.md`, in this folder. Claude limited it to seven files and excluded the code, the history and the conversations. That limit was itself the weakness being studied: a narrow look, chosen by Claude, that could not see the answer. **This brief does not limit you. Read the first review and its response first, then go far beyond them.**

The only goal of this pass is to IDENTIFY. Do not design the framework. That comes after Ian has read your analysis.

## The evidence (all prepared for you; nothing here needs a database or the network)

Folder `docs/codex/CODEX-CLAUDE-WEAKNESSES-evidence/`:

1. **`ian-typed-prompts.jsonl`** (13.6 MB, 8,310 lines, 12 Jul to 16 Nov 2026). Every message Ian typed to a Claude session, from 2,645 top-level session transcripts. Each line has:
   - `ian`: his words exactly;
   - `claude_before`: the last 2,500 characters of what Claude said just before;
   - `claude_before_chars`, and `tool_calls_before` (tool calls since his previous message);
   - `ts`, `project` (the working folder), `session`;
   - `how`: typed, mid-turn (typed while Claude was working), queued, or suggestion_accepted.

   **This is the primary evidence.** Ian's reply to what Claude just did is the most direct record of where Claude failed him. Mid-turn messages are often interruptions ("What??????????", "WTF", "You were supposed to be doing a better job of updating me?").

   Extraction rule: entrypoint cli; promptSource typed, queued or suggestion_accepted; plus mid-turn queue entries and attachments. Peer-session messages, helper briefs, hook text and tool results are excluded. A 14-message random sample of the mid-turn set was checked by hand: all were Ian's.
2. **`brain-memory.txt`** (3.0 MB): every record in the brain database, 5,438 of them, live, retired and superseded. Columns: Id, CreatedUtc, Lane, Kind (decision, lesson, finding, feedback, dead-end, project, reference, issue, other), Status, Retired, SupersededBy, Name, Description, Body (first 2,000 characters). Lessons and feedback are faults already written down once; count how often the SAME lesson was re-learned.
3. **`reviews.txt`** (1.0 MB): 2,045 independent reviews of Claude's commits before push. Columns: Id, RecordedUtc, Repo, Sha, Verdict (approve or changes), Summary, Findings (first 3,000 characters). These are faults another reader found in Claude's own code.
4. **`board-rows.txt`** (0.3 MB): 545 work-board rows (ids 1 to 2,138). Columns: Id, RecordedUtc, ClosedUtc, Owner, State (open, doing, blocked, done...), What, DoneWhen, BlockedOn, Evidence. Rows opened and never closed, and rows closed without evidence, measure "design, then not implement".
5. `../CODEX-FLAWS-AND-FRAMEWORK-REBUILD-mistakes.txt` (73 KB): the recorded-mistake table, 115 rows to 11 Nov, with who caught each.

In all the .txt exports, a long record can wrap onto the next line, so count records by their leading Id.

You may also read, read-only:

- **The code and its history.** `C:\VIsual Studio Projects\KOR.Drafter`, `...\Operations` and `...\Framework`. Use `git log`, `git log --stat`, `git log --diff-filter=D` and blame: churn, reverts, duplicated tools, files added and never called, fixes of fixes.
- **The method documents.** `Operations\docs\HOW-WE-BUILD.md`, `Operations\CLAUDE.md`, `Framework\CLAUDE.md`, `Framework\docs\` (RULES-TO-CHECKS.md, RESET-PLAN.md, GUARDS-BAR.md), and each repo's `.claude\settings.json`. Operations CLAUDE.md is a list of rules, each written after a failure; read it as a fault history.
- **Full transcripts**, when one instance needs its whole context: `C:\Users\ilalonde\.claude\projects\<project>\<session>.jsonl`. They total 12 GB, so open the ones a finding points to, not all of them.

Do not open the live databases, the network shares or other hosts.

## How to work

- The evidence is too large to read by eye, and **too important to sample lazily**. Use scripts for the population: classify all 8,310 prompts, count, cluster and trace across time. Then read enough full instances of each class to be sure the class is real. Every count you report must come from a script you saved in the work folder.
- Do not start from Claude's own list. Build the classes from the evidence, then compare them with Claude's list (`CODEX-FLAWS-AND-FRAMEWORK-REBUILD.md`, "Claude's flaws list") and say what Claude got wrong, missed or flattered.
- A weakness is a pattern in what Claude DOES, with a mechanism you can state, not a mood. "Claude is careless" is not a finding. "Claude reports an outcome after reading only the command's exit status, not the produced artefact; 212 of 8,310 prompts are Ian finding the artefact wrong after a 'done'" is a finding.
- Distinguish the model's weaknesses from failures of the framework built around it, and from Ian's process: unclear asks, or changes of direction. Be honest where the cause is not Claude.
- Track repetition across time. A weakness that recurs after a lesson, rule or hook was written for it is the most important kind. Say which mechanism existed and why it did not hold.

## What to produce (`CODEX-CLAUDE-WEAKNESSES-RESPONSE.md`)

1. **Method, in 10 lines.** What you read, what your scripts did, and what you did NOT cover.
2. **Weaknesses, ranked by the cost they caused.** Cost means Ian's time and anger, reruns and hours of runs, wrong deliverables, and code waste. For each:
   - a name, and a one-sentence mechanism;
   - frequency as X of Y, with the script that counted it;
   - at least three cited instances, each with ts, session or Id, and Ian's words;
   - who usually caught it: Ian, a reviewer, a check, or Claude itself;
   - when it was first seen, and whether it is getting better or worse;
   - which existing rule, lesson, hook or gate targets it, and the evidence that it did or did not stop it;
   - whether a mechanism could plausibly catch it, or it needs a human or a different way of working. Name no designs.
3. **Strengths,** with the same evidence standard: what Claude reliably does well here, which Ian's framework should amplify.
4. **The root causes** under the weaknesses: few, and supported. Say plainly if a root is a limit of the model itself, one that no framework can remove, only route around.
5. **The framework's own record.** Which of its mechanisms caught real faults before Ian did, which cost more than they caught, and which faults passed straight through it. Use the guard counts, the reviews and the board.
6. **What Claude's own 15-item list and the first Codex review got wrong or missed.**
7. **The ten questions Ian should ask before any rebuild,** answered from the evidence where possible.

## Rules

- Never guess. Mark anything you did not read "not checked". When you say something is absent, state the scope you searched.
- Every number gives its Y and comes from a saved script.
- Be blunt. Do not soften a finding because Claude will read it, or because Ian built the framework. Ian asked for the truth.
- If you run out of room, finish sections 2 and 4 completely before the others, and say what was left out.
