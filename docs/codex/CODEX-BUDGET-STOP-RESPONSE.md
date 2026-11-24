# Budget stop response — 23 November 2026

Built locally on `budget-stop`. The worktree CLI now implements `budget check` and `budget show`, with a transactional row ledger, elapsed-time limit, typed approval lifts, and protection of existing-row Budget updates in `SqlWriteCutGuard`. The KOR-210 launch door is **not wired** by this change.

Commits, with the shared hooks disabled for each local commit:

- `0818034c` — `budget stop A: cases (codex)`. Tests only; 67 failures on the original implementation. This commit was never amended.
- `d58e38d5` — `budget stop B: enforce row run and elapsed-time budgets with typed lifts (codex)`.
- This response is committed separately, last.

## Prior art and evidence checked

Searched the Framework source/tests, Operations `tools/` and `docs/codex/`, and the named KOR.Drafter `db/tools` location before adding files. Read `StallGuard`, `StallStore.LogGuardRefusal`, `DrafterPythonGuard`, `SqlWriteCutGuard`, `Program.Enforce`, the existing prompt recorder, `BrainDb`, `BrainStore`, and `kor testdiff`. Read the sibling Framework scorecard and issue/hand-over documents, KOR.Drafter migration 163, the SAMPLE-FIRST STOP in `virtual_drafter.py`, and `send-set.ps1`. No sibling source was edited.

Read KorStandards rows 2248 and 2143, the relevant schema metadata and privileges, and decision/lesson records 133998, 134051, 124879 and 138092. The supplied Budget texts match the database. `Budget` is nvarchar(400); `CK_Commitment_Budget` exists. Its live definition is not visible to this login; migration 163 supplies the inspected definition. The Board login can read Commitment and insert Event and GuardRefusal, allowing one transaction for the check.

Read local runner `done.txt` artifacts, without contacting KOR-210:

| Row | Tag | Local completion marker |
|---|---|---|
| 2248 | step5-2248-storeys | DONE PASS-CHAIN |
| 2248 | step5-2248-b1l04-r2 | DONE PASS-CHAIN |
| 2248 | step5-2248-diag-a | DONE PASS-CHAIN |
| 2248 | step5-2248-diag-b | DONE PASS-CHAIN |
| 2248 | step5-2248-diag-c | DONE PASS-CHAIN |
| 2248 | step5-2248-refuse-changed | no done.txt |
| 2248 | step5-2248-refuse-nosample | no done.txt |
| 2143 | step5-b1-l02 | DONE SAFE-NOT-A-RESULT; B1_L02 no LOG |
| 2143 | step5-b1-l02-colstiff | DONE PASS-CHAIN |
| 2143 | step5-b1-l02-colstiff2 | DONE PASS-CHAIN |
| 2143 | step5-b1-l02-delivered | DONE PASS-CHAIN |
| 2143 | step5-b1-l02-jr | DONE PASS-CHAIN |
| 2143 | step5-b1-l02-pass3 | DONE PASS-CHAIN |
| 2143 | step5-b1-l02-r2 | DONE PASS-CHAIN |
| 2143 | step5-b1-l02-pass3-dry | no done.txt |

Thus 2248 has five completion markers; 2143 has seven, including one that explicitly did not produce a SAFE result. This establishes runner completions under the brief's done.txt criterion, not seven successful engineering results. Missing local markers alone do not prove that no remote process ever started.

## Files and behavior

| Files | Responsibility |
|---|---|
| `Kor.Operations.Framework/Budget/BudgetGuard.cs` | Budget grammar, row spend count, first-spend clock, refusal/lift decision |
| `Kor.Operations.Framework/Budget/BudgetApproval.cs` | Session prompt record plus typed-transcript provenance and 12-hour expiry |
| `Kor.Operations.Framework/Budget/BudgetStore.cs` | Board read, row lock, Event ledger, shared GuardRefusal logger |
| `Kor.Operations.Framework.Cli/Program.Budget.cs`, `Program.cs` | Verbs, options, help, nonzero spending failures even with `--hook` |
| `Kor.Operations.Framework/Enforce/SqlWriteCutGuard.Budget.cs`, `SqlWriteCutGuard.cs`, CLI `Program.Enforce.cs` | Extension of the existing SQL guard for Budget assignments |
| `Kor.Operations.Framework.Tests/BudgetStopTests.cs`, `BudgetLedgerTests.cs`, `BudgetBoundaryTests.cs` | Original cases, rollback ledger integration, SQL boundaries and concurrency |

Interfaces:

```text
kor budget check --row N --step kor210-run [--ref tag] [--session id]
kor budget show --row N
```

`check` exits 0 only for an allowed or lifted check whose event has been recorded and, in the standalone CLI, committed. It exits nonzero for refusal, invalid usage, unavailable storage, or failure to lock/write/commit. `--hook` cannot turn a budget failure into exit 0. `show` only reads; it prints Budget, parsed limits/errors, all recorded outcomes, remaining runs and clock time.

The session comes from `--session`, then `CLAUDE_CODE_SESSION_ID`, otherwise `unknown`. An unknown session cannot obtain a lift. `--prompts-dir` and `--projects-dir` permit isolated test fixtures; normal invocation uses the existing prompt recorder directory and `~/.claude/projects`.

Each evaluated check produces one `brain.Event`: Module `Framework`, Kind `gate`, Actor `kor budget`, Ref `budget:row:N`, Outcome `allowed`, `refused` or `lifted`. JSON Detail carries row, step, caller ref, session, Budget, prior spend, limits and, for a lift, its prompt timestamp. Lifted permissions count as allowed spending; they do not reset the allowance or clock. Refused events do not spend. `show` writes no event. Invalid invocations that never identify a check, or storage failures, cannot promise a ledger event and do not grant permission.

The first allowed/lifted event starts elapsed wall time. The next check refuses at or beyond the duration. A long-open row with no allowed event has not started its clock. Run count is per row across sessions. Every allowed check spends, including repeated refs and checks whose later launcher fails: this is permission accounting, not completion accounting.

An exclusive transaction application lock serializes each row, including a previously empty ledger. `UPDLOCK, HOLDLOCK` keeps its Budget stable during the decision. The Event and any refusal are written on the same Board-login transaction. Refusals use the existing `StallStore.LogGuardRefusal`, Guard `budget`, so the normal refusal reporting can count them. No schema migration or second ledger was added.

## Supported Budget wordings

Matching is case insensitive, with flexible whitespace. Exactly one run-count clause is required. It starts the Budget or follows a semicolon or newline. Supported templates are:

```text
[at most] <count> KOR-210 [SAFE|ETABS] run[s]
[at most] <count> [SAFE|ETABS] run[s] on KOR-210
```

`<count>` is `one`, `a`, or a nonnegative decimal integer up to Int32.MaxValue. Examples exercised include `one KOR-210 run`, `a KOR-210 SAFE run`, `at most one KOR-210 ETABS run`, `3 KOR-210 runs`, `2 KOR-210 SAFE runs`, `4 KOR-210 ETABS runs`, `One SAFE run on KOR-210`, `a ETABS run on KOR-210`, `2 runs on KOR-210`, and `0 KOR-210 runs`. Both complete historical Budget texts are cases.

One optional duration is supported:

- At the beginning: `half a day`, or a nonnegative integer/decimal followed by `min`, `mins`, `minute`, `minutes`, `h`, `hr`, `hrs`, `hour`, `hours`, `day`, or `days`.
- Anywhere after `~`: the same numeric duration forms, including the required mid-text `~4 h`; `~half a day` is also accepted.
- Minutes are 60 seconds, hours 3,600 seconds and days 86,400 seconds. **Half a day is 12 hours**, matching scorecard.py, rather than an assumed working half-day.

No duration means no time limit; the run limit still applies. No KOR-210 count means zero runs and a refusal saying `state the run count in the row's Budget`; a recognized duration is still reported. Competing counts/durations, ranges, negative/fractional run counts, integer overflow, additional KOR-210 claims, malformed tilde durations and immediate alternatives such as `one ... run or two more` refuse. Other prose is descriptive; this is not a general natural-language interpretation of prerequisites or conditional promises. Unrecognized count wording must be rewritten into the grammar or explicitly lifted.

Example second-run refusal, with the actual 2143 Budget:

> refused: row 2143, Budget "One SAFE run on KOR-210 for one building model plus one Codex review; sized before starting.", spent 1 of 1 KOR-210 runs. run allowance spent. Ian can lift it by typing IAN-APPROVED-OVER-BUDGET 2143 in this session (12 hours from that prompt).

For expiry the reason is `elapsed time allowance spent`; parsing failures give the specific ambiguity or missing-count reason. All remain one paragraph with row, original Budget, spent X of Y and the lift instruction.

## Approval and SQL protection

Ian's approval must be a line containing exactly `IAN-APPROVED-OVER-BUDGET <row>` in a recorded prompt of this session, corroborated by a matching transcript user entry with `entrypoint=cli` and `promptSource=typed` or `queued`. The transcript timestamp must be no older than 12 hours and not in the future. Another row/session, assistant/peer/system/SDK content, tool results, file contents, fenced/quoted mentions, corrupt/missing records and the shared `unknown` session do not lift. Exactly 12 hours is expired. A lift is recorded as the check's single `lifted` event; each lifted permission remains a spend.

For existing-row Budget assignments the separate token is `IAN-APPROVED-BUDGET <row>`, subject to the same provenance and expiry. The spend token does not authorize a Budget edit.

The extension inspects literal sqlcmd/Invoke-Sqlcmd queries, SQL input files, Invoke-KorSqlScript `-Path`, and oq.py `-q`/`-i`, with the existing shell wrapper/script walker. It recognizes direct, qualified, bracketed and alias UPDATE targets, TOP/table-hint UPDATEs, updatable CTE references, chained assignments, subquery assignments and MERGE Budget updates. SQL comments and string values are excluded from assignment matching. New-row INSERTs and MERGE branches that only insert are allowed.

Approval requires a provably single literal **target** Id: `WHERE Id=N` on the direct target, a correctly qualified target alias, or a MERGE `ON target.Id=N`. A variable, multi-row predicate, unknown CTE scope, source-table Id, or MERGE update of unmatched target rows refuses. Narrow/rewrite those statements first; an approval for one row never grants an unknown row set. Ordinary non-Budget writes keep their previous treatment, including the original SQL output-cut protection.

SQL refusal format: `Budget change refused on oversight.Commitment row N: Ian has not typed approval for this row's Budget change. Ian must type IAN-APPROVED-BUDGET N as its own line in this session (valid for 12 hours). New-row INSERT budgets are allowed.` Unknown row sets instead explain that the write must be narrowed.

## Proofs

All builds/tests were Debug and limited to `Kor.Operations.Framework.Tests` and its Framework/CLI references. Suites ran sequentially. TEMP/TMP were unchanged. The baseline executable source is exactly 0883c0ff: at the initial HEAD, `837a8b38`, `git diff --stat 0883c0ff HEAD` showed **only the supplied brief**, with no source/test/build changes. The baseline ran in this worktree before A was written; no claim is made that HEAD itself was 0883c0ff.

| Proof | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| A cases against original code | 0 | 67 | 0 | 67 |
| Additional boundary characterization against initial B implementation | 5 | 11 | 0 | 16 |
| Final targeted suite, including actual rollback database tests and existing SQL-cut cases | 109 | 0 | 0 | 109 |
| Baseline full Framework suite | 1,167 | 257 | 0 | 1,424 |
| Final full Framework suite, once | 1,250 | 257 | 3 | 1,510 |

A uses reflection so absent production contracts produce assertion failures on the original assembly, rather than failing compilation. It covers the exact historical Budget texts with spend lists and second-run refusals, all listed unit spellings, absent/ambiguous counts, cross-session/row counting, elapsed time, lifted spending, token rules and SQL cases. B adds 19 cases: 17 boundaries and two more database cases. Eleven of the first 16 boundary cases were observed failing before correction; five were passing controls. The final subquery case was added during review and checked green, not claimed as a separately observed red gate. A remains unchanged in history.

The full runs used process-local invalid values `budget-stop-offline` for `KORBRAIN_SQL`, `KOR_ENGINEERINGTOOLS_STANDARDSDB` and `KOR_OPPORTUNITIES_OPPORTUNITIESDB`. This prevents registry fallback and database connections from the pre-existing suite. Existing failures were preserved, not edited or suppressed. The three new database cases explicitly skip only under this named isolation value; each passed separately and in the final targeted run with real, uncommittable rollback transactions.

The database proofs check allowed/refused/lifted events and fields, `show`, GuardRefusal, transaction lock exclusion, the actual enforcement hook and the distinction between approval tokens. The ledger test disposes its transaction and reads back zero remaining records. A final independent read found **0 test Commitments, 0 test Events and 0 test GuardRefusals** across the test session prefixes. No test row was left. No live Budget was raised, no real spend was committed, and no database migration was applied.

Commands actually run (each test invocation was alone):

```powershell
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug --filter 'FullyQualifiedName~BudgetStopTests|FullyQualifiedName~BudgetLedgerTests' --logger 'trx;LogFileName=cases-red.trx' --results-directory obj/budget-stop -nologo
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug --filter 'FullyQualifiedName~BudgetStopTests|FullyQualifiedName~BudgetBoundaryTests|FullyQualifiedName~BudgetLedgerTests|FullyQualifiedName~BudgetConcurrencyTests|FullyQualifiedName~SqlWriteCutGuardTests' --logger 'trx;LogFileName=targeted-final.trx' --results-directory obj/budget-stop -nologo
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug --logger 'trx;LogFileName=base.trx' --results-directory obj/budget-stop -nologo
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug --no-build --logger 'trx;LogFileName=new.trx' --results-directory obj/budget-stop -nologo
dotnet Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.dll testdiff obj/budget-stop/base.trx obj/budget-stop/new.trx
```

Logs, TRX files and the testdiff output remain under ignored `obj/budget-stop`. The base/red/final-full commands used the isolation overrides above; the final targeted command did not. Both full test processes exited 1 due to the baseline failures. `kor testdiff` exited **0**, reporting:

```text
failures in both: 254; only in new: 0; failing in base, passing in new: 0
tests only in new: 86 (0 of them failing); base tests missing from new: 0
VERDICT: NO NEW FAILURES - 254 of 1504 test names failing in new,
every one also failing in base (254 of 1418)
```

Theory results account for the difference between 257 failed results and 254 failed names. Both full runs completed; started-but-unfinished cases are **NOT MEASURED**, because no blame sequence was supplied. This is a by-name regression comparison under isolation, not a claim that the entire live suite is green.

Also ran the built worktree CLI's `budget show --row 2248`: it prints one run, four hours, no recorded spends and an unstarted clock. Its zero-spend output is real: historical runner completions are not automatically imported into the new ledger. `git diff --check` passed.

## Rule 11: scope and remaining same-class misses

Class: the next expensive action is permitted repeatedly after the row's stated allowance is spent. Both historical row wordings are checked by the same count/clock gate; the ledger and transaction tests check persistence and exclusion, rather than only the parser.

Covered: instrumented `kor budget check` calls for `kor210-run`, shared row counting, elapsed time from first spend, fail-closed unknown budgets/storage, typed row-specific lifts, refusal reporting and the stated SQL Budget assignment routes.

Not covered: wiring `send-set.ps1`, retrospectively counting existing runner artifacts, interrupting a run already in progress, verifying the truth of the caller's row/ref, enforcing completion/state/prerequisites on the board, interpreting arbitrary natural-language conditions, every possible SQL execution route, or authenticating files against a hostile session. The existing recorder retains only 20 prompts; losing the corroborating approval from that record can end availability of a lift before 12 hours. The files remain forgeable, as with the existing guard model.

Concrete same-class overspends still missed:

- A second KOR-210 run started directly through the currently unwired launcher never calls this check and is not stopped. Wiring that door is explicitly a later KOR.Drafter task.
- A caller can charge a different row with an unused allowance. One allowed set can also contain multiple models or internal retries; this ledger counts starts, not the computation inside a set.
- A custom Python/SqlClient program or stored procedure can raise Budget outside the inspected SQL command shapes. Dynamic SQL, SQLCMD includes/variables and database synonyms are not evaluated by this hook. This is not a database permission/trigger boundary.

Revit runs, Codex passes, token spend and percentage-based stopping are **out of scope**. SpendMeter remains unchanged. No run was launched, kor installed, source edited outside this worktree, shared hook/settings changed, remote execution/deployment performed, or commit pushed. Coordinator review and the pre-push gate remain outstanding before publication.

## Corrections and ambiguities in the brief

- The branch was correct, but starting HEAD was `837a8b38`, not `0883c0ff`; the intervening commits only add/update this brief. The unchanged executable source made the requested baseline comparison possible without switching the worktree.
- Row 2143's eight tags are seven local completed runner attempts and one tag without done.txt; the initial attempt explicitly says SAFE-NOT-A-RESULT. A tag count is not a count of successful SAFE results.
- Row 2248 has five completion markers as the brief says. The sibling HANDOVER still says four; that summary is stale.
- DrafterPythonGuard's recorder has neither timestamps nor typed-origin metadata, and its existing approval reader fails open on some read errors. Copying it literally cannot meet this brief's 12-hour, typed-only requirement. The new reader retains the same prompt record source but corroborates provenance/time in the session transcript and fails closed.
- `scorecard.py` is in the sibling Framework repository, not this Operations `tools/` directory. Its leading-duration limitation and its 12-hour interpretation of half a day were confirmed.
- The words “spent = allowed events” and “one event per lifted check” need a consistent interpretation. Here, both `allowed` and `lifted` are permissions and count as spending; one lifted check writes one event, never an extra allowance reset.
- The no-live-writes rule was applied as no committed production writes, with the brief's explicit exception for tests inside transactions that roll back. All three new integration cases use `OpenRolledBack`; the full legacy suite was isolated to avoid unreviewed writes.

The author's held-back cases were not accessed or run. Their independent review is still needed; the passing cases above do not claim that unseen checks passed.
