# Budget stop round 2 response

Completed on branch `budget-stop`. The targeted live-database tests passed **137/137**. The full Framework suite ran once against the live database: **1,488 passed, 46 failed, 0 skipped**. Compared with `obj/coord/coord-live.trx`, `kor testdiff` returned **NO NEW FAILURES**, exit 0. The previously failing budget ledger test now passes. An independent read after the suite found **0 budget-test Commitments, 0 Events and 0 GuardRefusals**.

Local commits, in order:

- `0323bbe8` — `budget stop round 2 A: cases (codex)`; tests only, unchanged after starting B.
- `c20eee57` — `budget stop round 2 B: retry deadlocked checks and preserve client flag case (codex)`.
- This response is committed last.

## Grounding and prior art

Read both briefs, the first response, repository guidance, the named coordinator log and TRX, and the affected implementation. Searched `tools/`, `docs/codex/` and the Framework projects for budget cases, deadlock handling, transaction users and testdiff. Reused `BrainDb.RetryOnceOnDeadlock`, `BrainDb.IsDeadlock`, `DeadlockRetryTests.SqlError`, the existing `LiveDb` collection, `kor testdiff` and `tools/SqlQuery/oq.py`; no replacement database or comparison tooling was introduced.

The coordinator TRX contains 1,510 results, 47 failed. Its log records the budget ledger failure at line 38 with a raw deadlock message instead of `spent 1 of 1`. Source inspection confirmed that the standalone budget check did not invoke the retry helper, both budget database classes lacked collection membership, and the budget SQL inspection lowercased `-I` into `-i`.

## 1. Retry the whole budget check

`Kor.Operations.Framework.Cli/Program.Budget.cs` now calls the existing retry helper around an attempt that opens a fresh Board transaction, locks and reads the row, decides, writes the event/refusal, commits and disposes. A failed attempt is disposed before the next attempt starts. Permission is printed only after a successful attempt returns, including its commit.

A first SQL 1205 waits the helper's existing random 200–600 ms and retries once. A second returns exit 1 and exactly one line, without raw exception details:

```text
refused: row 2248, budget check deadlocked (SQL 1205) after one retry; no spend recorded.
```

Caller-owned transactions, including the uncommittable integration-test fixtures, cannot be replayed after SQL Server rolls back their setup. They are never retried or committed here; a 1205 returns a separate clear refusal asking the caller to retry the whole transaction. Other errors retain the existing nonzero CLI failure behavior. The existing protection against `--hook` turning a budget failure into success is unchanged.

`BudgetLedgerTests` and `BudgetConcurrencyTests` now both use `[Collection(LiveDb.Name)]`. This existing collection disables parallelization and also contains the other live transaction tests. Searching the test project found these were the budget transaction users requiring membership.

Six new cases cover first-1205 recovery to both allowed and refused decisions, second-1205 exhaustion, caller-owned transaction behavior, non-1205 errors, and collection membership. **Before B: 6 failed. After B: 6 passed.** The five retry-boundary cases use reflection so the absent boundary fails an assertion on the original assembly; they inject real `SqlException` objects carrying error 1205 using the existing factory. They do not force a real server deadlock. The production transaction scope was inspected, and the existing live rollback ledger/lock cases passed.

## 2. Preserve client flag case and skip Python options

`Kor.Operations.Framework/Enforce/SqlWriteCutGuard.Budget.cs` now distinguishes sqlcmd's case-sensitive `-I`, `-i`, `-q` and `-Q`. PowerShell query/input parameters keep their case-insensitive handling. For Python/py, inspection finds the script after interpreter options and starts scanning SQL arguments after `oq.py`. Cases exercise `-I`, `-X utf8`, `-W ignore`, `-u`, `py -3`, and `--`; `-c`/`-m` are not mistaken for script invocation.

Fourteen paired cases cover harmless SQL versus an existing-row Budget UPDATE, row-specific approval, and actual SQL input files. They include Bash and PowerShell, three-part `KorStandards.oversight.Commitment` names, and oq.py paths that do not exist locally. Thus allowing a harmless query no longer depends on accidentally reading the Python script as SQL. Protected updates must identify row 2248 and the missing approval, rather than merely return a generic file refusal. **Before B: 12 failed, 2 PowerShell controls passed. After B: 14 passed.**

oq.py remains inspected. Its source forwards SQL to sqlcmd and converts `-q` to `-Q`; its default database does not make three-part KorStandards writes irrelevant. The final read-only cleanup query also used this tool with three-part names successfully.

## 3. Require both approval records for a valid session

Four new cases use a valid session ID and test both `IAN-APPROVED-OVER-BUDGET` and `IAN-APPROVED-BUDGET`:

- A matching typed transcript entry with no prompt record does not lift.
- A prompt record with no corroborating transcript entry does not lift.

Each case supplies the missing half afterward and confirms the same session then obtains approval. **Before B: 4 passed. After B: 4 passed.** These are additional negative coverage, not newly fixed production behavior. `BudgetApproval.cs` required no change.

## Proofs and database cleanup

All builds/tests were Debug, limited to `Kor.Operations.Framework.Tests` and its Framework/CLI references. Test invocations ran sequentially. The targeted and full runs used the live database, with no isolation overrides; TEMP/TMP were unchanged.

| Proof | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| New cases against original production code | 6 | 18 | 0 | 24 |
| New cases after B, included in both final runs | 24 | 0 | 0 | 24 |
| Targeted live suite | 137 | 0 | 0 | 137 |
| Supplied coordinator live baseline | 1,463 | 47 | 0 | 1,510 |
| Final full live suite, once | 1,488 | 46 | 0 | 1,534 |

Commands run (console output was saved beside the TRX files):

```powershell
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug --filter 'FullyQualifiedName~BudgetRoundTwoTests' --logger 'trx;LogFileName=cases-red.trx' --results-directory obj/budget-stop-2 -nologo

dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug --filter 'FullyQualifiedName~BudgetStopTests|FullyQualifiedName~BudgetBoundaryTests|FullyQualifiedName~BudgetLedgerTests|FullyQualifiedName~BudgetConcurrencyTests|FullyQualifiedName~BudgetRoundTwoTests|FullyQualifiedName~SqlWriteCutGuardTests|FullyQualifiedName~DeadlockRetryTests' --logger 'trx;LogFileName=targeted-final.trx' --results-directory obj/budget-stop-2 -nologo

dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug --no-build --blame --logger 'trx;LogFileName=full-live.trx' --results-directory obj/budget-stop-2 -nologo

dotnet Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.dll testdiff obj/coord/coord-live.trx obj/budget-stop-2/full-live.trx
```

The full suite completed in 5 minutes 34 seconds and exited 1 because of existing failures. The testdiff output, saved as `obj/budget-stop-2/testdiff.txt`, says:

```text
failures in both: 43; only in new: 0; failing in base, passing in new: 1
tests only in new: 24 (0 of them failing); base tests missing from new: 0
VERDICT: NO NEW FAILURES - 43 of 1528 test names failing in new,
every one also failing in base (44 of 1504)
```

The one recovered test is `BudgetLedgerTests.CheckAndShowUseOneLedgerAndRefusalLogThenRollbackEverything`. Repeated theory display names explain the difference between failed-result and failed-name counts. Neither comparison input supplied a blame sequence; despite requesting `--blame`, the completed new run emitted no sequence. Started-but-unfinished cases therefore remain **NOT MEASURED** by testdiff. This is a by-name comparison, not a claim that the whole suite is green.

All budget integration writes use `OpenRolledBack`; the ledger test additionally reads back zero remaining records after disposal. After the full suite, a separate read through `python -I tools/SqlQuery/oq.py -q ...` counted Commitment rows by `RecordedBy` and Event/GuardRefusal rows by `Session`, across both `budget-test-%` and `budget-hook-%` prefixes. Results in `obj/budget-stop-2/leftovers.txt`: **0 / 0 / 0**, exit 0. No budget test row was left, no real spend was committed, and no live Budget was raised.

`git diff --check` passed. Changed scope is the new case file, the two collection annotations, the budget CLI, the budget SQL extension and this response. No other guard, settings, hook, migration or launcher was edited. Local commits used the existing empty hooks directory via command-local `core.hooksPath`, as instructed; no persistent Git configuration changed. Nothing was pushed or installed. No remote process or deployment was started.

## Rule 11: covers, does not cover, same-class misses

Class addressed here: a budget gate must preserve its decision through a transient SQL deadlock and harmless command-option changes, while requiring both records before accepting an approval.

Covered: one retry of an owned check transaction on SQL 1205; clear refusal on exhaustion; serial budget DB tests; paired harmless/protected SQL query and file cases for the listed clients/options; missing approval evidence for a valid session. Existing row counting, elapsed-time limits, lifted spending, refusal logging and SQL Budget protection retain their targeted tests.

Not covered: deterministic creation of a live-server deadlock, arbitrary interpreter/client options or SQL execution routes, hostile forgery of both approval files, retrospectively counting old runner artifacts, verifying the caller's chosen row, or wiring `send-set.ps1`. Revit runs, Codex passes, token spend and percentage stopping remain outside scope.

**Known gap deliberately retained:** an unexpected parser exception escaping the budget SQL guard reaches the existing enforce hook-failure path and allows the command. `Program.Enforce.cs` and that path were not changed; no logging was added for this gap. Such a crash is not guaranteed to enforce the Budget refusal.

Concrete same-class miss: a custom Python/SqlClient program or stored procedure can raise an existing Budget outside the inspected command shapes. Likewise, a second KOR-210 run through the still-unwired launcher never calls the budget check and is not stopped. Passing this suite does not establish protection at those doors.

## Corrections and limits of the brief

- Starting HEAD was `c974467b`, not `cbb8d063`. `git diff --stat cbb8d063 HEAD` before work showed only the round-two brief; executable code was unchanged. The red cases therefore ran against cbb8d063's production code without switching the worktree.
- “Each failing” cannot honestly describe the requested missing-evidence cases: production already rejects those inputs. They and two PowerShell controls were green before B, explicitly separated from the 18 observed failures above.
- A deadlock in a caller-owned fixture transaction cannot be repaired by rerunning its check on that doomed transaction. Collection membership fixes test scheduling; fresh-transaction retry applies to the standalone production check.
- The held-back-case results and prior review totals are coordinator context, not independently reproduced here. Held-back cases were not accessed or run. Coordinator review and the pre-push gate remain for publication.

Process count: one session, one completed full-suite run, no KOR-210 runs. Token/dollar cost was not measured. The two behavioral faults were supplied by the coordinator/review and reproduced by cases before the fix; the approval item added coverage to behavior already present.
