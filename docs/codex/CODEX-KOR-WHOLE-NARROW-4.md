# CODEX kor whole, round 4: the gate must never stop a session from working - it deadlocked a third coordinator

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by a Claude session (Framework coordinator cc3b7be3, 23 Nov 2026), the kind of session this gate checks, and
some of the refusals below were refusals of that session's own work. Ian asked that every Codex brief from a Claude session
say so up front (#133998). Treat every statement as a claim; check each against the code, the transcripts and the database.

## Why now
Ian, 23 Nov 13:2x, on the coordinator session that took over at 12:36 (80383e99, "framework-0a"): "The other session is
completely FUCKED" ... "It's stuck on a guard and seems to have been LOBOTOMIZED". It verified and pushed a Codex change and
applied a KorStandards ruling correctly, then `kor whole` W1 refused EVERY Bash call it made (its handover update in Framework
docs/HANDOVER.md at 4e2df0d says so; record #138102). That is the third coordinator this gate has stopped: b6568d08 on 21 Nov
(round 3's "deadlock", Framework HANDOVER plan-block lines 79-83), and cc3b7be3, whose every command naming KOR-210 has been
refused by W3 since 22 Nov 19:5x, so Ian had to run the KOR-210 sends by hand. HOW-WE-BUILD rule 7: a guard that costs more
than it catches is fixed or trimmed. The goal stays (Ian 21 Nov, round 1: "make sure you look at the WHOLE of something
ingested ... we NEED to mitigate that") - no CUT, no log-only. Narrow it so it refuses only what its goal needs.

## Prior work (searched by the author: `rg -il "kor whole|WholeGate|kor-whole" docs/` over the 470 top-level entries of this
worktree's docs/, recursive, uncut: 8 hits - rounds 1-3 and their 3 responses, plus CODEX-BASH-HEREDOC-NARROW.md and
CODEX-KOR-REPEAT-NARROW.md, which only mention the gate)
- `CODEX-KOR-WHOLE-NARROW.md`, `-2.md`, `-3.md` and their `-RESPONSE.md`: the gate's rules W1-W5, its cases, and the open
  points of review row 2611. Read them; do not undo their right refusals.
- Records: #138087 (accounting credited only in a reply that ENDS a turn), #138100 (the same lines cleared it at once in the
  next turn), #138102 (this deadlock). Framework docs/HANDOVER.md STATE NOW 22 Nov "KNOWN FALSE REFUSALS" and "CARRIED" lines.

## The false-refusal classes (each with live evidence; recover the exact moments from the transcripts)
Transcripts: `C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Framework\cc3b7be3-1a3f-4b77-a674-537c582d930a.jsonl`
and `...\80383e99-22b4-4bef-8ca8-dd95ed6a068f.jsonl`; refusals in KorStandards `oversight.GuardRefusal` (Guard 'kor-whole').
1. DEADLOCK: W1 refuses every Bash/PowerShell call, including the read-only looks that would open the flagged items and the
   `kor record` / `kor state` calls that save work (80383e99 after its proofs; cc3b7be3's `kor state` and `kor record` calls on
   22-23 Nov). A gate whose own discharge needs a command it refuses has no way out.
2. MID-TURN ACCOUNTING: `OPENED n of n:` / `NOT OPENED:` lines count only in a reply that ends a turn (#138087, #138100), so
   every flag raised mid-turn costs Ian a turn ("go").
3. PASS COUNTS READ AS FAULTS: '0 of 215' (expected FAIL states printed PASS - none did), 'files 63 missing []' / 'missing 0'
   (0 missing), '45 of 46 checks passed', 'Failed: 45' where the same 45 fail on the baseline and the line says so.
4. OTHER PEOPLE'S NUMBERS: numbers quoted from another session's handover, a reviewer's text or a Codex response are counted as
   this session's flagged results (cc3b7be3 transcript after reading 4e2df0d's handover lines: '23 fail', '49 failures'; round
   3's L2008/L4252, B1 L6267).
5. UNOPENABLE POPULATIONS: W3 refuses the expensive step until an item is OPENED by "an uncut look that shows them", but some
   populations are not listable item by item by any tool (e.g. '71 missing' coverage pieces where 11 are sub-1e-9 fragments
   the checker never lists), and NOT OPENED never lifts W3 - so the step is refused for 200 calls whatever the session does.

## Tasks
Work in `C:\VIsual Studio Projects\Operations-kor-whole-narrow-4` on branch `kor-whole-narrow-4` at develop 2763a763.
- For each class: recover its refusals from the transcripts and GuardRefusal, judge each RIGHT or WRONG yourself, and keep a
  held-out RIGHT refusal from rounds 1-3 refusing.
- CASES FIRST, committed alone (`kor whole round 4 A: cases (codex)`), each new case failing on the current code; then the
  change (`kor whole round 4 B: <one line> (codex)`) with WholeGate.cs's own rule comment updated (what it CHECKS, ALLOWS, DOES
  NOT CATCH). Never amend A after starting B.
- REQUIREMENTS the change must meet: (a) a read-only look and the work-saving `kor record` / `kor state` / `git` reads are never
  refused (only what the goal needs: the expensive step, a board close, and claims written without a look); (b) accounting lines
  count in the reply or tool call that carries them, mid-turn; (c) a count whose deficit is zero, or that a baseline comparison
  in the same output explains, is not a flag; (d) numbers inside text the session READ (a file, another session's message, a
  review) are not its flagged results; (e) an item that no tool can list one by one is discharged by `NOT LISTABLE: <key> -
  <why>` naming what was looked at, and W3 accepts it. State, for each, the same-class fault it would still miss.
- Every existing test in the gate's test files unchanged and passing; no MUST REFUSE case turned to pass. Full Framework.Tests
  (the Kor.Operations.Framework tests) run ONCE at the end, alone (lesson #138092), and compared by test name with a run at
  2763a763 (testdiff). Do not run any suite that writes KorStandards; SELECT-only on oversight.GuardRefusal through
  `tools/SqlQuery/oq.py`.

## What you may and must not touch
- Keep TEMP and TMP at their normal values. Commit with `git -c core.hooksPath=<an empty folder> commit ...` (the shared
  post-commit hook writes the database). Must not: push, install kor, edit outside this worktree, change any other guard,
  settings file or git hook, write the database, run anything on another machine. The author holds back further cases.

## Write ONE response file
`docs/codex/CODEX-KOR-WHOLE-NARROW-4-RESPONSE.md`, committed last: per class, the refusals judged (X right / Y wrong / Z not
settled of N, with ids), the cases, the change, the test and testdiff numbers, repo rule 11 (covers / does not / one same-class
miss), then anything wrong in this brief.
