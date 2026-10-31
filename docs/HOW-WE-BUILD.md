# How we build - the operating method (EDICT)

Ian, 31 Oct 2026: "The ability to develop efficiently is just as or more powerful than the applications that are built."
"I'm only ok cutting spend so far as it doesn't cut IQ." This is binding on every session, as CLAUDE.md rule 15 says.

Why it exists: Halifax v4 (30 Oct) shipped right but cost far too much - six sessions days old, every message relayed
through the lead, four full Revit reruns, hundreds of full-page renders, and side threads reported as news. Ian ran
out of a 20x weekly allowance plus $1,000 of credits. The output was good; the process was not.

## 1. The swarm stays; it runs lean

Five or six sessions, one module each, converging on one plan with a lead holding it, is the strength. Keep it.
What is fixed is how it runs:
- **The lead** holds the plan, approves, merges and LOOKS. It keeps its own context small: large reads go to a
  background helper that returns the answer, not the files.
- **Workers** own one module each. They report to the lead only at a DONE point or for a DECISION. No "noted", no
  "go ahead", no progress chatter. Status, evidence and blockers go on the oversight board (KorStandards
  `oversight.Commitment`), which every session reads; messages are for decisions and hand-offs.
- **The right model per role:** judgement, review, characterisation and the lead on the top model; mechanical work
  (migrations, censuses, long builds) may run on a smaller one.
- **Ian hears three things only:** a change to a deliverable, a decision that is his, an answer to what he asked.
  Internal work stays on the board.

## 2. Wisdom lives outside sessions, so sessions can be short

A session's memory fades by compaction anyway. Lessons that last are written where every session inherits them:
CLAUDE.md rules, memory files, hooks, code (checks, red tests, gates), KorStandards rulings, the board, and each
module's RESUME / handover note.
- A session runs for a deliverable or a working day, not per task and not for days. Compact when heavy; restart
  only at a clean point.
- **Before any restart, a real handover:** state, decisions, traps, and the lessons of the stretch - in the RESUME
  note and memory, not left to a compaction summary.
- **A fresh session proves itself before it works:** it answers five questions from the handover (where things
  stand, what is next, the traps, who owns what, what not to touch). A wrong answer fixes the note first.
- **A lesson becomes a rule, a hook or a check the same day.** A lesson only a session remembers is lost with it.

## 3. Catch faults before the expensive step

The costliest thing we do is a full Revit run. Order every change: checks on the read (seconds) -> offline replay
(minutes) -> only then Revit. Every fault found on a plot becomes a check that runs earlier next time. A check must
read what was BUILT (Revit's readback, the e2k), not only our own requests - the 30 Oct pad turn passed every check
that read only our requests.

## 4. Understand before fixing

Two faults of one shape: stop, name the class in one sentence, write the check that finds every instance, then fix
once (CLAUDE.md rules 10 and 11). A measured cause before any code; if the measurement refutes the stated cause,
close it on the measurement and write no fix.

## 5. Every piece of work starts with three lines

Goal; done-when; budget (time, runs, and what it may read). One owner. A finish line written before the work.

## 6. The scorecard - measure the process, not only the product

For every deliverable, recorded on the board when it ships:
- cost (tokens / dollars), sessions used, session age at the end;
- full reruns of the expensive step;
- faults caught by a check vs caught by Ian or an engineer (target: zero that a check could have caught);
- messages per decision.
Driven down deliverable over deliverable.

## 7. Ten minutes after each deliverable

What cost most; what slipped past the checks; one change to the process (a check, a rule, a hook, a tool). Count
the guardrails too: keep the hooks that catch real faults, trim the ones that only cost rewording.

## 8. Spend is watched weekly

Ian checks claude.ai -> Settings -> Usage at a fixed time each week; the lead says early when a deliverable is
running heavy, before the allowance is gone, never after.
