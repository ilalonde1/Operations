# CODEX kor whole, round 2: a reader behind a `cd` or a variable is still read as a check

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by the Claude session that built this gate (Framework coordinator b6568d08, 21 Nov 2026), and that
session is grading its own work. Ian asked that every Codex brief from a Claude session say so up front (#133998).
Treat everything below as claims. Verify each against the code and the transcripts, and say where the framing is wrong.
You write the cases, not the author.

## Where it stands

Round 1 (your brief docs/codex/CODEX-KOR-WHOLE-NARROW.md, your response CODEX-KOR-WHOLE-NARROW-RESPONSE.md, commits
c802a4fb and 810604ed) is reviewed, pushed to Operations develop 810604ed, and installed as kor 1.0.0+810604ed. Ian's
goal is unchanged: "It's supposed to make sure you look at the WHOLE of something ingested - and mitigate your most
destructive habit - which is partial/cursory samples of something and thinking it's EVERYTHING."

The author held back transcripts you did not see in round 1 and replayed them on your build. One was the worker
session's transcript. Its 2 refusals were the same before and after your change. Both are wrong refusals, and they
share one cause with a third, seen in the author's own transcript.

## Evidence (claims: open each one yourself)

Transcript W:
`C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Operations\b49d2807-d400-4747-b0b9-bcc5e7e7af50.jsonl`
(the worker judging guard refusals, 21 Nov).
- L281: the command is `cd <its scratchpad> && tail -n +3 all.txt | awk -F'|' '$2=="kor-whole"{print ...}'`, which
  lists its own earlier dump of GuardRefusal rows. Their quoted Matched text (`4-sided 176 of 180`, `4 FAIL`, ...) is
  read as fresh flags. Replay refuses at L806 (Record, W1).
- L1047: the command is `$S="..."; python -I tools/SqlQuery/oq.py -i "$S\recs.sql" ... ; Get-Content "$S\recs.txt"`,
  which reads 44 brain records. Lines quoted from those records (`gates 47 of 47`, `50 of 50 frozen bundles
  refused`) are read as fresh flags. Replay refuses at L1158 (SendMessage, W1).

The author's claim about the cause: a command's first program decides whether it is a reader. `cd`, a variable
assignment (`S=...;`, `$S=...;`) or another prefix in front of a reader makes the whole command count as a check.
Your round-1 response already names a related limit (reader exemptions untouched; SQL read provenance needs a
visible Write). Check whether this is one cause or several.

Also from round 1's independent review (kor_review rows 2594/2595, MINOR notes; claims):
1. WholeGate.cs Account (~L560-564): ratio accounting marks a flag Accounted when ANY door line has a ratio with the
   same population and deficit, with no label or key check. An unrelated "2 of 15 pass" accounts a 13-of-15 flag at
   W1. This is a loosening with no counterexample test, and it touches the gate's purpose. Decide whether it needs a
   label or key match.
2. ListedBaselineFailures (~L502-509): one uncut listing can open a flag from a different TRX pair with the same
   generic basenames (base.trx/new.trx).
3. AssessedQuote (L137): "'...' is a judgement/claim/summary" blanks a quoted span in ANY run's output, including a
   checker's own line.
4. WholeTranscript.cs PathKey (L178-182): relative and absolute spellings of one path miss each other. This is a
   false refusal, not a false pass.
5. The Narrow_BaselineSummaryCannotSubstituteForOpeningEveryFailure test does not reproduce "real summary, then a
   partial list", which its comment claims.

The author is holding back further real refusals as a hidden check again.

## Tasks (same method as round 1)

Work in `C:\VIsual Studio Projects\Operations-kor-whole-narrow` on branch `kor-whole-narrow`, which is at develop
810604ed.

**A. Cases first, committed alone** (`kor whole narrow 2A: cases (codex)`):
- MUST PASS: W L806 and W L1158 at their real moments, plus the general forms you find: a reader behind `cd ... &&`,
  behind a variable assignment, in a `;` chain of readers, and in a pipe of readers.
- MUST REFUSE: a REAL check behind the same prefixes (for example `cd x && dotnet test ...`, or `$S=...; python
  check.py`), so the fix cannot become "ignore anything after cd". Keep every round-1 MUST REFUSE unchanged.
- For review notes 1-3 and 5: a case that pins the boundary, MUST REFUSE where the gate's purpose needs it.
- Do NOT amend commit A after you start B. If a case needs changing, make a new commit and say why. (In round 1 you
  amended A after B had started. The change was a strengthening and it was checked, but cases fixed before code is
  the point.)

**B. Narrow or tighten** WholeGate.cs and WholeTranscript.cs only (`kor whole narrow 2B: <one line> (codex)`). Do
not weaken W1-W4. Do not change a MUST REFUSE to pass. If two cases cannot both hold, stop and explain why.

**C. Proof.** `set -o pipefail &&` on any piped build. Run the WholeGate tests, the full
Kor.Operations.Framework.Tests (compare with the base using `dotnet run --project Kor.Operations.Framework.Cli --
testdiff <base.trx> <new.trx>`), and replays of A1-A3, B1, B2 and W through the BUILT CLI on frozen copies. Report
before/after for each, plus individual decisions at the real moments, without Comply.

## Must not

Push, install, or edit outside this worktree. Change any other guard, settings file or git hook. Query or write the
database. Run anything on another machine. Open C:\Users\ilalonde\Documents\KOR-holdout. If the post-commit hook
writes to the database, you may skip hooks for your commit command as in round 1, but say so.

## Write ONE response file

`docs/codex/CODEX-KOR-WHOLE-NARROW-2-RESPONSE.md`, committed with B. It holds: your verdict on the cause; the cases
(X refuse / Y pass, each with its source); what changed and why; the proof numbers; repo rule 11 (what it covers,
what it does not, and one same-class fault it still misses); and anything wrong or missing in this brief.
