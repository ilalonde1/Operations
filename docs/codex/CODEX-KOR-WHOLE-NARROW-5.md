# CODEX kor whole, round 5: it still refuses work that has looked, and still misses two ways round it

## READ THIS FIRST: this brief comes from a source you should not trust
Written by a Claude session (Framework coordinator framework-31, 23 Nov 2026) - the kind of session this gate checks
(#133998). Treat every statement as a claim; check each against the code, the transcripts and the database.

## Why
Ian's 15-issues plan, item 2 (Framework docs/HANDOVER.md, IAN'S PLAN "NOW: THE 15 ISSUES"): issue 3 "I can't see the whole
system at once" - this gate is its mitigation, and Ian kept it (21 Nov: "It's supposed to make sure you look at the WHOLE of
something ingested ... we NEED to mitigate that") - no CUT, no log-only. But it costs more than it catches: 7 days to 23 Nov,
`python -I tools/guard_log.py week` (Framework): kor-whole 199 refusals, 0 judged right, 11 wrong. Round 4 (develop 0883c0ff,
`CODEX-KOR-WHOLE-NARROW-4.md` + `-RESPONSE.md`) shipped 23 Nov 14:34; installed kor is now 1.0.0+7d759ec1 (round 4 + an
unrelated budget verb). Narrow it again so it refuses only what its goal needs.

## Live evidence since round 4 (KorStandards `oversight.GuardRefusal`, Guard 'kor-whole', Id 10840-10850; RefusedAtUtc is LOCAL)
- A. A WORKER COULD NOT REPORT: session 905b4bf0 (transcript `C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-
  Framework\905b4bf0-4ae8-4560-88b7-364519cd1af1.jsonl`), refusals 10846-10849 (SendMessage) and 10850 (Reply), W1, all on its
  transcript L237: "3 FAIL", "4 FAIL", "5 FAIL", "9 FAIL", "91 DIFFERS", "92 DIFFERS" and the ACCEPTANCE RESULT line. Those
  are model-audit ITEM LABELS ("ACCEPTANCE 3 FAIL" = item 3 failed), not counts of 3/4/5/9 failures; the worker had run the
  audit and was reporting exactly those items. It wrote its report to a file instead (KOR.Drafter work\_acceptance\2248\
  worker-report-pass2.txt line 2: refused "3 times by kor whole W1 ... and did not accept OPENED lines in its own format").
  Coordinator's judgement: WRONG x5. Verify.
- B. cc3b7be3 refusals 10840 (W3), 10841, 10842 (W1): not judged by the author - judge them.
## Owed from earlier rounds (Framework HANDOVER STATE NOW, UPDATE 14:35, the round-4 coordinator's list; verify each)
1. kor's OWN `kor testdiff` verdict is still a flag: `Failed: N` from a dotnet test run followed, in the SAME output or the
   next completed call, by testdiff's "failures in both: X; only in new: 0 ... VERDICT: NO NEW FAILURES - X of Y test names
   failing in new, every one also failing in base (X' of Y')" is a by-name comparison with zero new names - round 4's own
   discharge condition - yet its summary lines are themselves read as fresh failure counts. Round 4 kept "testdiff-summary-
   only refusal cases" unchanged on purpose: re-decide that with cases, and say why.
2. Reviewer-quoted numbers are still flagged (a kor_review / plan_review verdict's text is the reviewer's, not this session's
   result).
3. `kor record ...; git commit ...` - the work-saving exemption for kor record lets a joined commit skip the commit door (a
   FALSE ALLOW - a hole). Same for any work-saving verb joined with `;`, `&&`, `|`.
4. `awk`/`sed` with `system()` (or `e` in GNU sed) runs a step the gate does not see (FALSE ALLOW).
5. "N missing <x>" misread; review sentence stripping; the empty-label regex; a refusal Account-ed twice.
6. Accounting format: a session's `OPENED n of n: <key> - <what>` line must be accepted when the key it names is the key the
   refusal printed, even when the key itself contains " - " (e.g. `VERDICT: NO NEW FAILURES - 43 of ...`). Write the case.

## Requirements (round 4's stay: read-only looks and work-saving calls never refused; no MUST REFUSE case turns to pass)
- Item labels (`ACCEPTANCE <id> FAIL|PASS|REVIEW REQUIRED`, `<id> FAIL` where id is an item name/number in a list of items)
  are not counts; the flagged population is the item, keyed by its label.
- Items 1-6 above, each cases first. 3 and 4 are holes: they must REFUSE after the change.
- For each class: the same-class fault it would still miss (repo rule 11).
- The author holds back further live refusals from after round 4 (not named here) and cases written at the check.

## How
Worktree `C:\VIsual Studio Projects\Operations-kor-whole-narrow-5`, branch `kor-whole-narrow-5`, at origin/develop 7d759ec1.
- CASES FIRST, committed alone (`kor whole round 5 A: cases (codex)`), each new case failing on 7d759ec1; then the change
  (`kor whole round 5 B: <one line> (codex)`) with WholeGate.cs's rule comment updated (CHECKS / ALLOWS / DOES NOT CATCH).
  Never amend A after B starts.
- Proofs ONE at a time (lesson #138092): the gate's tests; then the full Kor.Operations.Framework suite ONCE, alone, against
  the LIVE database (no isolation overrides - the coordinator's comparison run is live), compared with `kor testdiff`
  against a live run you make first at 7d759ec1. Report any test row left in the database.
- Replay: kor-whole refusals Id 10840-10850 (and rounds 1-4's held populations), recovered from transcripts, old -> new.
  Do NOT read or replay any kor-whole refusal with Id > 10850: those are the author's held-back check.
- READ BOUND: transcripts 905b4bf0 and cc3b7be3 only (same projects folder), and in them only the lines each refusal cites
  plus their tool calls/results; rounds 1-4's populations only through their RESPONSE files and existing cases. No other
  transcript, share or database beyond SELECTs on oversight.GuardRefusal / brain.Memory.
- Keep TEMP/TMP normal. Commit locally with `git -c core.hooksPath=<an empty folder> commit ...` (the shared post-commit hook
  writes the database); the coordinator reviews every commit (kor_review) and pushes through the pre-push gate. Must not:
  push, install kor, edit outside this worktree, change any other guard/settings/hook, write the live database (SELECT only
  through `tools/SqlQuery/oq.py`), touch another machine.

## Response
`docs/codex/CODEX-KOR-WHOLE-NARROW-5-RESPONSE.md`, committed last: per class the refusals judged (X right / Y wrong / Z not
settled of N, with ids), the cases (red/green), the change, test + testdiff numbers, the replay table old -> new, rule 11,
then anything wrong in this brief.
