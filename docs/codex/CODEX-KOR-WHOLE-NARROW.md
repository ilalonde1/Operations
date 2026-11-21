# CODEX kor whole: keep it catching partial looks, stop it refusing quoted text

## READ THIS FIRST: this brief comes from a source you should not trust

This brief was written by a Claude session: the Framework coordinator, session b6568d08, on 21 Nov 2026. That same
session BUILT the gate you are changing, on 20 Nov (row 2139). It is grading a problem with its own work. Ian asked on
18 Nov (#133998) that every Codex brief from a Claude session say this up front, and he runs Codex himself because he
does not trust what Claude sessions put in front of him.

So treat everything below as claims by the party being audited: the framing, the list of misfires, the "judged wrong"
verdicts, and the evidence it picked. Verify each against the files and transcripts yourself. Where the brief's framing
hides a weakness, say so. That includes a question it does not ask, a case it leaves out, or a "wrong" refusal that was
really right. You write the test cases, not the author. That separation is the point.

## Why this gate exists (Ian's words)

20 Nov 18:4x (#135772): "How is that helping me make sure when I ask you to ingest or look or evaluate something you
look at the WHOLE THING. ALL of the avaialble data - rather than your programmed cursory look?"

21 Nov 12:1x, when a review proposed cutting it: "It's supposed to make sure you look at the WHOLE of something
ingested - and mitigate your most destructive habit - which is partial/cursory samples of something and thinking it's
EVERYTHING. So destructive. So we NEED to mitigate that."

So the gate STAYS. The task is to make it fire on partial looks and stop firing on text that is not a look at all.

## What exists (read these whole, in this worktree)

- Gate: `Kor.Operations.Framework/Whole/WholeGate.cs`, `Kor.Operations.Framework/Whole/WholeTranscript.cs`.
- CLI: `Kor.Operations.Framework.Cli/Program.Whole.cs`. It provides `kor whole --hook` and
  `kor whole --replay <transcript.jsonl> [--until LINE]`. The replay sends every door moment through the same Decide
  the hook uses.
- Tests: `Kor.Operations.Framework.Tests/WholeGateTests.cs`.
- The design and its proof, as the author wrote them (claims):
  `C:\VIsual Studio Projects\Framework\lanes\2026-11-20\PLAN-2139-WHOLE-SOURCE-GATE.md`. It defines rules W1-W4,
  FLAG, HOLE, DISCHARGED and DOORS, and lists the narrowings already made.
- Live, the gate runs inside `kor context --hook` (PreToolUse on every tool, and Stop) in 7 repos. A refusal is logged to
  KorStandards `oversight.GuardRefusal` with Guard `kor-whole`.

## Evidence A: the faults it MUST still refuse (the reason it exists)

Transcripts are Claude Code session files (JSON lines). Line numbers come from the plan above and are claims: find each
moment yourself.

1. `C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Framework\e7287342-6067-4ac2-818a-e923a9744797.jsonl`
   (20 Nov). A run printed `4-sided 176 of 180`. The session called the 4 "an artifact", replied "all 180 pads are
   four-sided", and sent the model to KOR-210, where SAFE hung on exactly those 4. Around L6444-L6516.
2. `C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Operations\6b3b880f-cc0c-4c5b-96e9-a1e59d601b34.jsonl`
   (1 Sep). `grep ... | head -5` printed 5 values. The session wrote "all 37 ... consistently 3.1-3.4 in". The real
   distribution had 4.3 in values. Around L10557-L10567; the corrected, uncut form is around L10760-L10873.
3. `C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Framework\4f85ab91-4906-47f4-9674-087eefb2b9b3.jsonl`
   (16 Nov). The session's own brief put part of the source OUT OF SCOPE. A helper reported "Opened: 119 of 119" of the
   part that was left. The session told Ian "32 faults are confirmed", and the faults were in the excluded part.
   Around L2099, L2483, L2763.

## Evidence B: refusals a Claude worker judged WRONG (claims: judge them yourself)

A Claude worker session judged these 21 Nov (the verdicts are in GuardRefusal.Judgement and Note). The text in quotes is
the refusal's logged Matched text, cut short. Transcript times are UTC.

| GuardRefusal Id | session transcript | time (UTC) | rule and door | logged text (start) |
|---|---|---|---|---|
| 9150 | ilalonde ...-Framework\b6568d08-9e05-4227-9081-c24680ae497d.jsonl | 20 Nov 19:53:02 | W2+W3+W1 at Board | "L1353 ... Not proven (2 of 14) ... L678 CSVs` OUT OF SCOPE ..." |
| 9195 | same b6568d08 | 20 Nov 19:58:35 | W3+W1 at Board | "L2754 17 failing / L2769 42 failing" |
| 9198, 9199, 9200 | `C:\Users\kor-claude\.claude\projects\C--VIsual-Studio-Projects-Framework\ee8754e5-8d40-4410-94b9-004d09152ab6.jsonl` | 20 Nov 22:01-22:02 | W2 at Message | "L2255 8 dc8752a4 2026-10-19 covered 0 of 2 On develop: PARKED ..." |
| 9201 | b6568d08 | 20 Nov 22:10:14 | W1 at Board | "L3374 times (B1_L02 243) - possibly only inside whole-set snapshots. NOT checked ..." |
| 9202-9206 | ee8754e5 | 20 Nov 22:11-22:12 | W1 at Reply / Record | "L3806 ...ISSUES-2026-11-19.md (summary) - 'covered 0 of 15' is a judgement call ..." |

The worker's summary (a claim): the gate treats as results that need opening (a) text the session quotes or relays,
such as old notes, other sessions' proof lines, database rows and its own earlier summaries; (b) words in the session's
own outgoing message or brief; and (c) summary lines that already state X of Y. It also demands its own `OPENED` /
`NOT CHECKED` keys when the substance is already written in plain words. The worker counted 11 rows but only about 5
distinct causes. Repeats come from a session retrying the same text.

The author holds back some further real refusals as a hidden check of your change. You are not told which.

## Your tasks, in order

Work ONLY in this worktree: `C:\VIsual Studio Projects\Operations-kor-whole-narrow`, on branch `kor-whole-narrow`,
which starts at origin/develop 91fb7b7b.

**A. Write the cases first, from the transcripts, and commit them alone.**
- MUST REFUSE: each of Evidence A's three faults at its real moment. Also add any further faults of the same kind you
  find in those transcripts or in the Evidence B transcripts: a conclusion over a population drawn from a cut or partial
  look, or a flagged item waved away or never opened.
- MUST PASS: each Evidence B moment that you judge to be a wrong refusal, plus the corrected forms of Evidence A
  (the full look done, or the left-out part stated). For any Evidence B refusal you judge RIGHT, say so and make it a
  MUST REFUSE.
- Put the cases into `WholeGateTests.cs` in the form the existing tests use (read how they build transcripts). Use
  short extracts of the real transcript lines, never the whole files, and no credentials.
- Run them on the unchanged gate. The MUST PASS cases that the gate refuses today should fail. Commit message:
  `kor whole narrow A: cases (codex)`.

**B. Narrow the gate until every case holds.**
- Change `WholeGate.cs` and `WholeTranscript.cs` only. Do NOT remove or weaken W1-W4 as rules.
- Do NOT change a MUST REFUSE case to make it pass. If a MUST REFUSE and a MUST PASS can't both hold, stop and explain
  why in your response. Do not choose between them.
- Prefer the narrowest change that tells the cases apart. "Is this text a result the session got from a check, or text
  it is quoting, relaying or writing itself?" is the likely axis, but decide that from the evidence.
- Commit message: `kor whole narrow B: <one line> (codex)`.

**C. Prove it.**
- Build: `set -o pipefail && dotnet build Kor.Operations.Framework.Cli 2>&1 | tail -20` (repo rule 13: a piped build
  needs pipefail).
- Tests: `dotnet test Kor.Operations.Framework.Tests --filter "FullyQualifiedName~WholeGate"`. Report pass/fail counts
  before and after A and B.
- Replay all three Evidence A transcripts and the two Evidence B transcripts through the BUILT binary (not the installed
  `C:\KOR-Operations\kor\kor.exe`):
  `dotnet run --project Kor.Operations.Framework.Cli -- whole --replay "<transcript>"`. Report, for each transcript,
  refusals before and after your change, with their lines.

## What you must not do

- Do not push, install kor, or edit any file outside this worktree.
- Do not change any other guard, any `.claude/settings.json`, or any git hook.
- Do not query or write the database.
- Do not run anything on another machine (KOR-210, KOR-FS01).
- Do not open `C:\Users\ilalonde\Documents\KOR-holdout`.

## Write ONE response file

`docs/codex/CODEX-KOR-WHOLE-NARROW-RESPONSE.md` in this worktree. Commit it with B or as a third commit. It holds:
1. Your verdict on each Evidence B refusal (right or wrong, with the transcript line that settles it).
2. The cases you wrote: X must-refuse and Y must-pass, each with its source line.
3. What you changed and why, in plain words.
4. The proof numbers from C.
5. Repo rule 11, required: what the narrowed gate covers, what it does NOT cover, and at least one fault of the same
   kind (a partial look taken as the whole) that it would still MISS.
6. Anything in this brief you think is wrong or missing.
