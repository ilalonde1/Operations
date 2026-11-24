# CODEX budget stop, round 2: three faults found at the coordinator's check

## READ THIS FIRST: this brief comes from a source you should not trust
Written by a Claude session (Framework coordinator framework-31, 23 Nov 2026), the kind of session this stop controls
(#133998). Check every statement against the code and the logs named.

## Context
Round 1 (`CODEX-BUDGET-STOP.md`, your response `CODEX-BUDGET-STOP-RESPONSE.md`, commits 0818034c d58e38d5 cbb8d063) was
checked: the coordinator's held-back cases (not shown to you) passed 9 of 10 - the one miss under-allows, which is safe;
targeted tests 109 of 109 against LIVE KorStandards; reviews 3746-3748 APPROVE with MINORs. Three things must be fixed
before it ships. Same worktree and branch: `C:\VIsual Studio Projects\Operations-budget-stop`, `budget-stop`, now at cbb8d063.

## 1. Deadlock: the budget check is not retried when SQL Server picks it as the deadlock victim
The coordinator ran the FULL Kor.Operations.Framework suite ONCE against the live database (not isolated), 23 Nov
18:26-18:32: 47 failed of 1510; `kor testdiff` against round 4's trx: ONE new failure,
`BudgetLedgerTests.CheckAndShowUseOneLedgerAndRefusalLogThenRollbackEverything` at BudgetLedgerTests.cs:38 - output began
"kor: Transaction (Process ID 61) was dead..." instead of "spent 1 of 1". It passes alone. Log:
`obj/coord-live.log` line 196, trx `obj/coord/coord-live.trx` in this worktree.
- Production meaning: two sessions (or a hook and a migration) checking at once can make a legitimate check refuse with a
  raw deadlock message. Office lesson #132331: "hooks retry once on 1205". The helper exists and is not used:
  `BrainDb.RetryOnceOnDeadlock` (BrainDb.cs:174) with `BrainDb.IsDeadlock` (:185). Use it for the whole check transaction
  (lock, read, decide, write - re-run whole in a new transaction). A second deadlock still fails closed with a clear
  one-line message naming the deadlock and that a retry was made - never a raw exception text.
- Tests: the DB test classes (BudgetLedgerTests, BudgetConcurrencyTests and any other that opens a budget transaction) go
  in ONE xunit collection so they never run in parallel with each other; plus a unit case proving a first 1205 is retried
  once and a second is refused with the clear message (inject the attempt, as RetryOnceOnDeadlock's signature allows).

## 2. False refusals from case-folded flags (review 3747 MINOR, verify it)
`Enforce/SqlWriteCutGuard.Budget.cs:44-46` lowercases flags before matching, so sqlcmd's `-I` (quoted identifiers, no
argument) is read as `-i <file>` and `sqlcmd -I -Q "..."` is refused as an uninspectable input file even with no Budget
in it; `python -I tools/SqlQuery/oq.py -q ...` passes only because the oq.py path happens to resolve as the "file". Every
board write this office makes goes through `python -I ...oq.py`. Match sqlcmd flags case-sensitively; skip interpreter
flags (`python -I`, `-X utf8`, `-u` ...) before the script path. Cases: both of the above ALLOW when no Budget is set;
the same commands carrying an UPDATE of Budget on an existing row still REFUSE.
NOTE: the reviewer also said oq.py "connects to KorOpportunitiesDb, so inspecting it guards a database that does not hold
the table". That is WRONG - oq.py is used with three-part names (`KorStandards.oversight.Commitment`) for every board write.
Keep oq.py inspected.

## 3. A missing negative test (review 3747 MINOR)
`NoUnknownSharedSessionApproval` returns on the session guard before the token path. Add: a VALID session id with no
prompt record (and one with a prompt record but no corroborating transcript line) does not lift.

## Known gap, NOT to fix now (state it in the response)
An unexpected parser exception in the enforce hook (Program.Enforce.cs:154-170) falls to the hook-failure path, which
allows. Kept deliberately: a guard that refuses everything on its own crash is the deadlock class Ian has already paid
for. Do not change it and do not add logging for it; state it in the response's rule 11.

## How (as round 1)
Cases first, committed alone (`budget stop round 2 A: cases (codex)`), each failing on cbb8d063; then the change
(`budget stop round 2 B: <one line> (codex)`). Proofs ONE at a time: targeted tests against the LIVE database (they roll
back), then the full Kor.Operations.Framework suite ONCE, alone, ALSO against the live database (no isolation values) with
`kor testdiff` against `obj/coord/coord-live.trx`. Report any test row left. Same must-nots as round 1: no push, no
install, nothing outside this worktree, no other guard/settings/hook, no live writes outside rolled-back tests.

## Response
`docs/codex/CODEX-BUDGET-STOP-2-RESPONSE.md`, committed last: per item the cases (red/green), the change, the numbers and
testdiff verdict, rule 11 (covers / does not / one same-class miss), anything wrong in this brief.
