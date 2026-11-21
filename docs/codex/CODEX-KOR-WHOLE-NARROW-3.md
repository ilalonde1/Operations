# CODEX kor whole, round 3: a run's own log is a result, and a commit message is a quote

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by the Claude session that built this gate (Framework coordinator b6568d08, 21 Nov 2026), and that
session is grading its own work. Ian asked that every Codex brief from a Claude session say so up front (#133998).
Treat everything below as claims. Verify each against the code and the transcripts, and say where the framing is wrong.
You write the cases, not the author.

## Where it stands

Round 1 (CODEX-KOR-WHOLE-NARROW.md / -RESPONSE.md, commits c802a4fb, 810604ed) is pushed to Operations develop and
installed as kor 1.0.0+810604ed. Round 2 (CODEX-KOR-WHOLE-NARROW-2.md / -2-RESPONSE.md, commits f772aa2a, 95833563,
ef4a3339, 782f3f2f, f5a49bfa, 59896b0b) is on branch `kor-whole-narrow` and is NOT shipped. Ian chose to hold it
and run this round, so rounds 2 and 3 ship together. Ian's goal is unchanged: "It's supposed to make sure you look at
the WHOLE of something ingested - and mitigate your most destructive habit - which is partial/cursory samples of
something and thinking it's EVERYTHING."

The author replayed 60 held-back transcripts (20-21 Nov) through the installed round-1 binary and through your
round-2 build. 40 refusals became 37. Of the 11 changed decisions, 8 were intended or disclosed by you. These 3 were
not.

## Evidence (claims: open each one yourself)

Both transcripts are kor-claude sessions in KOR.Drafter. They are append-only, so the line numbers hold:
- AC = `C:\Users\kor-claude\.claude\projects\C--VIsual-Studio-Projects-KOR-Drafter\ac80da06-010b-48fe-b321-e576eb44a116.jsonl`
- D7 = `C:\Users\kor-claude\.claude\projects\C--VIsual-Studio-Projects-KOR-Drafter\d7ad7e57-20c4-40bb-a8e4-c9466be464ca.jsonl`

If either path can't be read, stop and say so. Don't substitute another transcript.

**1. A REAL catch lost (round 2 loosened this).** AC L3696 (20 Nov 16:37:51Z), Bash:
`ls -la /c/kor-suite/lead-d0/ | grep -i g4-full; tail -5 /c/kor-suite/lead-d0/g4-full-7fa5e7d4.log 2>&1; tasklist ...`
The session reads the end of the log of a full test suite it had started in the background. The output shows
`Failed: 63` and `Skipped: 1`. Round 1 refuses at AC L3783 (Record, W1: L3696 `Failed: 63` | L3696 `Skipped: 1`).
Round 2 passes it. The author's claim: round 2 now treats `tail` of a stored file as a reader of old text, but this
file is the session's own fresh result and not a quote. A log, TRX or result file written by a run that this session
started is a check result, whichever program prints it. Check whether the session started that run in AC (look
before L3696 for the command that wrote g4-full-7fa5e7d4.log). If it did not, say so and say what the right decision
is.

**2. A NEW false refusal (round 2 added this).** D7 L1740 (20 Nov 11:25:17Z), Bash, a `;` chain that includes
`cd "C:/VIsual Studio Projects/KOR.Drafter-etabs" && git log --oneline -1 | cut -c1-80; git merge-base ...;
git ls-remote ...`. The output holds a commit subject, `7fa5e7d4 port g4: handover - 12 of 12 rows in C#, what is not
done and`. Round 2 refuses at D7 L1786 (Record, W1: L1740 `7fa5e7d4 port g4: handover - 12 of 12 rows in C#, what is not done and`).
Round 1 passed it.

**3. A NEW false refusal (round 2 added this).** D7 L2798 (20 Nov 13:39:54Z), Bash,
`cd ".../KOR.Drafter-portmerge" && git fetch ... && git merge --no-edit FETCH_HEAD 2>&1 | tail -1; ...; git log --oneline -3 | cut -c1-70`.
The output holds a commit subject, `47b33ec0 port-g6-8 handover (row 2202): session 2's unproven paths and`. Round 2
refuses at D7 L2854 (Record, W1: L2798 that subject). Round 1 passed it.

The author's claim about 2 and 3: commit subjects printed by `git log` or `git merge` are a quote of what someone
wrote earlier, not a check result. Round 2's cut (`cut -c1-80`, `cut -c1-70`) may be what turns them into W4 or W1
flags. Check whether the cause is the cut, the `cd` prefix, or the quote, and whether it is one cause or two.

**4. From the independent review of round 2 (kor_review row 2603, MINOR; a claim).** WholeGate.cs ~L284:
RecordReader matches case-insensitively, but `m.Value != ".jsonl"` compares case-sensitively. So a command naming
`X.JSONL` still gets the old command-wide reader exemption, and a real check later in the same command is hidden.
That is a false pass. Add a MUST REFUSE case and fix it, or show the claim is wrong.

**Known, not in scope unless it is the same cause:** your round-2 disclosure, A1 L2008 (a conservative false refusal
from lexical subject matching). If your fix for 2-3 also fixes it, say so. Don't widen the change to reach it.

The author is again holding back the other replayed decisions as a hidden check.

## Tasks (same method as rounds 1 and 2)

Work in `C:\VIsual Studio Projects\Operations-kor-whole-narrow` on branch `kor-whole-narrow`, on top of 59896b0b (or
the branch tip if the author has added a reviewed commit since; say which).

**A. Cases first, committed alone** (`kor whole narrow 3A: cases (codex)`):
- MUST REFUSE: AC L3783 at its real moment, plus the general forms: `tail`, `Get-Content -Tail`, `cat` or
  `Select-String` of a log or TRX that this session's own earlier command wrote, showing failures, then an unaccounted
  close. Include one where the run was started with `run_in_background` and the log is read later.
- MUST PASS: D7 L1786 and D7 L2854 at their real moments, plus the general forms: `git log`, `git show -s`, and `git
  merge` output carrying commit subjects with `N of N`, `failed`, `not done` and the like, cut or uncut.
- MUST REFUSE: a real check whose output happens to pass through `git` or `cut` (for example `dotnet test ... | cut
  -c1-200` showing `Failed: 3`), so the fix can't become "ignore anything after git or cut".
- Keep every round-1 and round-2 case unchanged.
- Do NOT amend commit A after you start B. If a case needs changing, make a new commit and say why.

**B. Narrow or tighten** WholeGate.cs and WholeTranscript.cs only (`kor whole narrow 3B: <one line> (codex)`). Do
not weaken W1-W4. Do not change a MUST REFUSE to pass. If two cases can't both hold, stop and explain why.

**C. Proof.** Use `set -o pipefail &&` on any piped build. Run:
- the WholeGate tests;
- the full Kor.Operations.Framework.Tests, compared with the base using `dotnet run --project
  Kor.Operations.Framework.Cli -- testdiff <base.trx> <new.trx>`;
- replays of AC, D7, W (round 2), and A1-A3, B1, B2 (round 1) through the BUILT CLI, on frozen copies you make.

For each replay, report before (at 59896b0b) and after, plus the individual decisions at the real moments, without
Comply.

## Must not

- Push, install, or edit outside this worktree.
- Change any other guard, settings file or git hook.
- Query or write the database.
- Run anything on another machine.
- Open C:\Users\ilalonde\Documents\KOR-holdout.
- If the post-commit hook writes to the database, you may skip hooks for your commit command as in rounds 1 and 2,
  but say so.

## Write ONE response file

`docs/codex/CODEX-KOR-WHOLE-NARROW-3-RESPONSE.md`, committed with B. It holds:
- your verdict on each cause;
- the cases (X refuse / Y pass, each with its source);
- what changed and why;
- the proof numbers;
- repo rule 11 (what it covers, what it does not, and one same-class fault it still misses);
- anything wrong or missing in this brief.
