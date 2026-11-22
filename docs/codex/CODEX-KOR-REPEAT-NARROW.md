# CODEX kor repeat: keep catching real repeats, stop refusing new work that shares a job's name

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by a Claude session (Framework coordinator b6568d08, 21 Nov 2026), and Claude sessions are the ones this
gate refuses. Ian asked that every Codex brief from a Claude session say so up front (#133998). Treat everything below
as claims. Verify each against the code, the transcripts and the gate's own dry run, and say where the framing is
wrong. You write the cases, not the author.

## The goal, which must stay covered

Ian, 10 Nov: "This is THE issue. You repeat and duplicate and triplicate work." Board row 1998. The gate is
`Kor.Operations.Framework/Recall/RepeatGate.cs`. Its summary says what it looks at, what refuses, how it is lifted
and what it does not do. Read that summary first. Ian has ruled that this guard is narrowed, not cut: a cut leaves
his "THE issue" with no check.

## Evidence (claims: open each one yourself)

Refusals from oversight.GuardRefusal, as judged on 21 Nov by a worker session. `RefusedAtUtc` actually holds LOCAL
time (UTC-7); use the transcript's own UTC `timestamp` for `--as-of`.

**Right (MUST REFUSE)**, 10 Nov, all re-runs of the token breakdown already recorded as #129845:
- 5565 and 5572 (session `zz-red-r...`, the live trap tests in RepeatGateTests).
- 5573: `break this week's tokens down by session, kind and model (raw and new per transcript)`, session 7e017d71,
  `C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Framework\7e017d71-e1bf-4e24-9265-585a0e21d5ce.jsonl`.

**Wrong (MUST PASS)**: 21 refusals of new work. Each was named by shared job or set words (31097, B1, L02, SAFE,
bundle, model-audit, README) and not by the measured quantity:
- a0ed29c9 (`...Framework\a0ed29c9-a893-4264-b14d-14cca0d980b5.jsonl`):
  - 8236 `Measure the merge diff sizes and read the --piece docs` => #133797;
  - 8237 (the same, with "inputs changed since #133797") => #134064, #134056;
  - 8240 `Run the two never-finishing PdfToSafe measurement cases alone on both trees, 90-min limit`;
  - 8243 `Compare the long measurement-case runs with testdiff` => #134068.
- c5eddf43 (`...Framework\c5eddf43-6013-474a-8392-6c886f21fb51.jsonl`):
  - 8540 `Survey which Framework tool files kor-claude can write` => #134026 (one file; the survey was every gate file);
  - 8541 (the same, with "inputs changed since #134026") => three unrelated records.
- ee8754e5 (`C:\Users\kor-claude\.claude\projects\C--VIsual-Studio-Projects-Framework\ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl`):
  - 8962 `Measure file sizes in the triage folder` => #131675.
- e7287342 (`...Framework\e7287342-6067-4ac2-818a-e923a9744797.jsonl`), 20 Nov:
  - 8958 `Measure every column top against the slab outlines in model and source`;
  - 8960 `Measure the CAD slab edge at C14-C19 and draw the crop`;
  - 8964 (a Write CREATING a new README for a new bundle);
  - 8966 and 8970 (model-audit on a new bundle / with Codex's new disposition);
  - 8974 `Run fixture and model-audit tests against the change`;
  - 8975, 8976, 8986 and 8993 (each already says `inputs changed since #N: ...` and is refused again, naming
    DIFFERENT records);
  - 8984 `Build takeoff and audit the 2143 bundle`;
  - 8995 and 8996 (a pad census on another job and other storeys);
  - 9001 `Characterise each overlapping pad group on B1 L02`.

Some refused calls are in a session's `subagents\agent-*.jsonl`, not its main transcript. Search both.

**How to reproduce a decision (checked by the author on one held-back refusal).** `kor repeat "<description>" --command
"<the call's command>" --as-of <the call's UTC timestamp>` builds the recall index from the database as of that time
and prints REFUSE or ALLOW, naming the records. The description alone does not reproduce it: the gate ranks on the
description plus the command (`RankText`). `kor repeat --replay <transcript>` builds ONE index now, so it can name
records written after the call. Read each line with that in mind, and prefer `--as-of` per moment.

**Claims about the causes**, from the worker's reading. Check them:
- (a) Job and set identifiers count toward the 3 topic words, so any work on a job with a recorded finding is refused.
  The shared words should have to include the measured quantity, not only the job.
- (b) A stated "inputs changed since #N" lifts only for #N. When the re-ranked index then names other records, it
  refuses again (8541, 8975, 8986, 8993), so the lift does not lift.
- (c) A Write that creates a new document for new input (8964) is treated as a repeat of the records about the old
  one.

The author is holding back further real refusals, right and wrong, as a hidden check.

## Tasks (the method used for kor whole, CODEX-KOR-WHOLE-NARROW*.md)

Work in `C:\VIsual Studio Projects\Operations-kor-repeat-narrow` on branch `kor-repeat-narrow`, at develop 59b76e06.

**A. Cases first, committed alone** (`kor repeat narrow A: cases (codex)`):
- MUST REFUSE: 5565/5572/5573 at their real moments, plus general forms you construct: the same quantity measured
  again on the same job with nothing changed, re-worded; and a lift naming a record that does not answer what is
  being lifted.
- MUST PASS: every wrong refusal above at its real moment (description + command + as-of), and the general forms: new
  work on a job that has findings about something else; a lift that names the record it changes, followed by an
  unrelated re-rank.
- Keep every existing RepeatGateTests case unchanged.
- Do NOT amend commit A after you start B. If a case needs changing, make a new commit and say why.

**B. Narrow** RepeatGate.cs, and Program.Repeat.cs only if the lift needs it (`kor repeat narrow B: <one line>
(codex)`). Do not change a MUST REFUSE to pass. Do not add a new tool or gate. If two cases can't both hold, stop and
explain why.

**C. Proof.** Use `set -o pipefail &&` on any piped build.
- Run RepeatGateTests and the full Kor.Operations.Framework.Tests, and compare with the base using `dotnet run
  --project Kor.Operations.Framework.Cli -- testdiff <base.trx> <new.trx>`.
- Run every evidence moment through the BUILT CLI before and after, with `--as-of`.
- Run `--replay` on the four transcripts above. Report before and after, refusal by refusal.

## What you may and must not touch

- READ-ONLY database access is allowed for this task, and only through `kor repeat` (dry run, `--as-of`, `--replay`)
  and RepeatGateTests' Live tests. The author checked that the dry run and replay only build the index and write
  nothing. If you find a path that writes, stop and say so.
- Must not: push, install, or edit outside this worktree; change any other guard, settings file or git hook; write
  the database; run anything on another machine; open C:\Users\ilalonde\Documents\KOR-holdout.
- If the post-commit hook writes to the database, skip hooks for your commit command as in the kor whole rounds, and
  say so.

## Write ONE response file

`docs/codex/CODEX-KOR-REPEAT-NARROW-RESPONSE.md`, committed with B. It holds:
- your verdict on causes (a)-(c);
- the cases (X refuse / Y pass, each with its source);
- what changed and why;
- the proof numbers;
- repo rule 11 (what it covers, what it does not, and one same-class fault it still misses);
- anything wrong or missing in this brief.
