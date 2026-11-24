# Worktree module response — 24 November 2026

Implemented on `worktree-module`: **A `5f8f4cf6`**, cases alone; **B `6e6cf133`**, implementation and test corrections;
**C `af81af09`** corrects one existing K7 assertion after the sole full-suite run. Production code remains exactly B.
No push, installation, deployment, committed lane move, or history rewrite. An unintended SQL validation write was
rolled back; the incident and independent readback are detailed below. No database row from it remains changed.

Goal: folder-derived module/lane identities belong to the main checkout; source identities retain the worktree.
Done when: red cases, one resolver, explicit-record refusal, source invariants, targeted checks, full-suite comparison,
Release timing, and a reviewed migration proposal. Budget: this worktree/Framework projects, one baseline and one final
full Framework run, sequential proofs; no numerical time/token budget was supplied. TEMP/TMP were not changed.

## Ground truth and prior art

Started clean at `37b84348`, two brief-only commits after `6bb62371`. `git diff 6bb62371 --stat` contained only the brief;
the baseline therefore tested exactly the requested production/test code. No other worktree was edited.
Searched `tools/`, `docs/codex/`, `../KOR.Drafter/db/tools/`, and both Framework C# projects for worktree, testdiff,
Git.Leaf/TopLevel/TopLevelFast and folder-name conversions. Reused Git, BrainDb.OpenRolledBack, the existing capture
tests and `kor testdiff`; checked Framework `tools/kor_review.py:196` and its absolute common-dir naming rule.
All fixtures and iteration artifacts are local. The differential harness is `WorktreeModuleTests`; the observable
output inspected is the capture-check command, source fields, refusal text and SQL readback, rather than a UI picture.

Read-only KorStandards snapshots confirm **86 records / 14 lanes**, **zero destination Name collisions**, and no duplicate
incoming live non-issue Names. All 86 are git-sourced: 48 findings, 28 lessons, 9 decisions, 1 dead end; no issues or
resolutions. Status is 54 current / 32 stale. Seven lane folders no longer exist, exactly the seven named in the brief.
At the main checkout's current HEAD, **64/86** cited paths are absent (brief: 63). SourceRepo must not be redirected.
The two cited decisions #139794/#139795 and the Framework handover's part-2 authorization were read.

Reread `c2ad5988` L1895/L1912 and `cd2499ed` L2888/L2894: the asks name the worktree, then the record commands copy it
(the latter via `$M`). `dd6412f2` is absent from the cited directory and a local transcript-name search; that regression
case is explicitly brief-supplied, not claimed as independently replayed. The tests exercise all three shapes through
real worktree commit capture, SessionCapture.Check, its stored ask event, and the formatted record instruction.
The settings in the existing worktrees name `KOR.Drafter`; the fault is folder-derived identity, not those overrides.

## Resolver, writers, and source boundaries

`Capture/Git.cs:ModuleOf` is the sole path-to-module rule. It walks to the nearest .git marker, follows gitdir and
commondir with their correct relative bases, and returns the main checkout's leaf. A normal checkout (including one
with dashes/spaces) keeps its own name; a non-repo keeps the old leaf fallback. Unreadable metadata falls back safely.
It launches **no git process**, including for linked worktrees. TopLevelFast shares the metadata reader but still
returns the WORKTREE top, and retains its git fallback for unsupported layouts. No global git configuration changed.

| Module/lane writer | Route after B |
|---|---|
| GitCapture.Build, all four git hooks | ModuleOf(top); event Repo/Commit unchanged |
| GitCapture.CommitAt | ModuleOf(top); source repo unchanged |
| ClaudeCapture.ModuleOf | Explicit override, then ModuleOf(CLAUDE_PROJECT_DIR), then ModuleOf(cwd) |
| ClaudeCapture.Tool, including background completion through SessionCapture.BackgroundEvent | ModuleOf(the proven source top); source Repo/Commit stay local |
| ClaudeCapture.Compact | Existing ModuleOf call feeds both LaneState.Lane and Event.Module |
| ClaudeCapture.End | Existing ModuleOf call feeds session-end |
| SessionCapture.Start / Check | Existing ClaudeCapture.ModuleOf calls feed start/ask; start Repo remains the worktree |
| SessionCapture.Reason | Uses the now-canonical captured Module; --dir still names the event's worktree Repo |
| Program.Enforce | All three existing Repo conversions use Git.ModuleOf, only when logging refusal |
| Program.Context | Both conversions use Git.ModuleOf |
| Program.Start / Stall / Repeat / Whole | Each of the four conversions uses Git.ModuleOf |

Recount: the pre-fix C# inventory contains **42 call sites** of the three named helpers (21 Leaf, 11 TopLevelFast,
10 TopLevel), not 34; nine TopLevelFast sites also contain the inline GetFileName conversion. Baseline line inventory:

| File (under Framework / Framework.Cli) | Baseline lines | Treatment |
|---|---|---|
| Capture/ClaudeCapture.cs | 59,60,96 | First two canonicalized; 96 remains source Repo, separate sourceModule added |
| Capture/GitCapture.cs | 38,39,69,70,137 | Top/source leaves and remote URL leaf retained; Module defaults split out |
| Capture/SessionCapture.cs | 48 | Source Repo retained; module flows through ClaudeCapture.ModuleOf |
| Program.Enforce.cs | 109,143,167 | Canonical refusal Repo |
| Program.Context.cs | 83,111 | Canonical refusal Repo |
| Program.Start.cs / Stall.cs / Repeat.cs / Whole.cs | 303 / 78 / 111 / 95 | Canonical refusal Repo |
| Records/RecordStore.cs | 78,81,83,84,123,124,125 | Source location, diagnostics, GitSource creation/verification retained |
| Records/Freshness.cs | 66,69,115 | Working file's source identity and source-location diagnostic retained |
| Records/RepoLocator.cs | 19,21,26,27 | Exact physical checkout lookup retained |
| Records/IssueCheck.cs | 106,124,178 | Check execution repo and lane-based execution lookup retained |
| Program.Knowledge.cs | 216,220 | Lesson-check code citation retained |
| Program.cs | 345 | Freshness file-change source lookup retained |
| Program.Enroll.cs | 23,42,71 | Enrollment target location and diagnostics retained |
| Enroll/EnrollCheck.cs | 360 | Exact enrolled checkout lookup retained; a new explicit-module validator is separate |

The old equalities were GitCapture's `module ??= repo`, ClaudeCapture.Tool's `module = repo`, and two existing
CaptureTests expecting Module == source leaf; those expectations now distinguish the fields. IssueCheck.ModuleDir
explicitly locates a checkout by issue.Lane before falling back to cwd: canonical lanes therefore select the main
checkout for test/gate/query execution. This move contains zero issues, and that execution policy was not changed.
HandoverState also locates by module unless a handover's source names its repo; that is module content lookup.
Freshness groups by SourceRepo, not Lane. The new tests verify a file present only at the worktree's HEAD remains current.

`kor record` validates before opening its write unit of work. Exact entries in EnrollCheck.Repos are allowed. Otherwise
an enrolled name plus `-suffix` is refused even after deletion; an arbitrary sibling name is refused when its gitdir
metadata resolves through Git.ModuleOf to an enrolled module. Other independent names with dashes remain allowed.
The one stderr line and exit 1 are exactly:

```text
--module KOR.Drafter-etabs is a worktree of KOR.Drafter: use --module "KOR.Drafter"
```

Explicit overrides on other verbs are preserved. This task does not silently remap arbitrary supplied module strings.

## Proposed lane move — coordinator only, after Virtual Drafter lead agreement (row 1194)

Reviewed against the reader/writer code and migration definitions 162/168/171/172/173. Live metadata exposes trigger
names but not their bodies to these logins, so current trigger bodies were not independently verified. The observed
86 contain no issue/resolution rows subject to frozen-evidence rules. There are **zero linked brain.Check rows** and
**zero LaneState rows in KOR.Drafter-* lanes**. Recheck these facts when applying; this response authorizes no move.

Effects: vw_ModuleState and exact-lane memory/recall/start readers will see eligible moved records under KOR.Drafter.
Current/stale status is unchanged. LaneState is a separate append-only resume store and is untouched. Check.Module is
independent of Memory.Lane; existing linked checks would require separate review, so the script refuses such a mismatch.
Freshness now selects these records in CheckModule(KOR.Drafter) but still reads their original SourceRepo/HEAD; a removed
checkout stays unverifiable. Supersede-by-name now uses (KOR.Drafter, Name); the collision checks protect that identity.
Names, bodies, source fields, timestamps, status, ids and supersession pointers are not assigned by this UPDATE.

The coordinator must set @ExpectedCount to the reviewed count when running (currently 86). IDs are captured and listed
BEFORE the update; range/update locks hold the selection. A mismatch, collision, changed source lane, trigger error or
incorrect readback rolls back the transaction. Only Lane is assigned, and only for captured ids still in the source lanes.

```sql
USE KorStandards;
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ExpectedCount int = 86; -- coordinator's reviewed count at execution
DECLARE @Move TABLE (Id int NOT NULL PRIMARY KEY);
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT @Move (Id)
        SELECT Id FROM brain.Memory WITH (UPDLOCK, HOLDLOCK)
        WHERE Lane LIKE N'KOR.Drafter-%';
    DECLARE @Captured int = (SELECT COUNT(*) FROM @Move);
    SELECT m.Id, m.Lane AS OldLane, m.Name, m.SourceRepo
        FROM brain.Memory AS m JOIN @Move AS ids ON ids.Id = m.Id ORDER BY m.Id;
    IF @Captured = 0 OR @Captured <> @ExpectedCount
        THROW 51000, 'Lane move: captured count differs from reviewed count.', 1;
    IF EXISTS (
        SELECT 1 FROM brain.Memory AS m JOIN @Move AS ids ON ids.Id = m.Id
        JOIN brain.Memory AS d WITH (UPDLOCK, HOLDLOCK)
          ON d.Name = m.Name AND d.Lane = N'KOR.Drafter')
        THROW 51001, 'Lane move: destination Name collision; review required.', 1;
    IF EXISTS (
        SELECT m.Name FROM brain.Memory AS m JOIN @Move AS ids ON ids.Id = m.Id
        WHERE m.RetiredAtUtc IS NULL AND m.Kind <> N'issue'
        GROUP BY m.Name HAVING COUNT(*) > 1)
        THROW 51002, 'Lane move: duplicate incoming live Name; review required.', 1;
    IF EXISTS (
        SELECT 1 FROM brain.Memory AS m JOIN @Move AS ids ON ids.Id = m.Id
        WHERE m.Kind IN (N'issue', N'resolution'))
        THROW 51003, 'Lane move: frozen issue/resolution evidence needs separate review.', 1;
    IF EXISTS (
        SELECT 1 FROM brain.[Check] AS c JOIN @Move AS ids ON ids.Id = c.MemoryId
        WHERE c.Module <> N'KOR.Drafter')
        THROW 51004, 'Lane move: linked Check.Module needs separate review.', 1;
    UPDATE m SET Lane = N'KOR.Drafter'
        FROM brain.Memory AS m JOIN @Move AS ids ON ids.Id = m.Id
        WHERE m.Lane LIKE N'KOR.Drafter-%';
    DECLARE @Changed int = @@ROWCOUNT;
    IF @Changed <> @Captured OR @Captured <> @ExpectedCount
        THROW 51005, 'Lane move: update count mismatch; rolling back.', 1;
    IF (SELECT COUNT(*) FROM brain.Memory AS m JOIN @Move AS ids ON ids.Id = m.Id
        WHERE m.Lane = N'KOR.Drafter') <> @Captured
        THROW 51006, 'Lane move: readback count mismatch; rolling back.', 1;
    SELECT @Captured AS CapturedCount, @Changed AS UpdatedCount, COUNT(*) AS ReadBackCount
        FROM brain.Memory AS m JOIN @Move AS ids ON ids.Id = m.Id
        WHERE m.Lane = N'KOR.Drafter';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
```

## History and limits of the brief

brain.Event.Module and GuardRefusal.Repo are unchanged. A snapshot matching the seven enrolled-name-plus-dash prefixes
finds **2,335 events / 48 names** and **193 refusals / 17 names**, not the brief's 1,563/26 and 192/16.
KOR.Drafter-* alone has 1,992 events / 24 names. These are current counts, not a claim that the earlier census was wrong.
BrainStore.Last, ModuleState's recent/end reads and EnrollCheck's activity check filter Event.Module exactly: historical
worktree rows remain outside main-module-only views. CommitsToAsk still finds a session's own historical commits across
modules, but sessionless old worktree commits are not main-module commits. An ask based on an old event can still show
its old module; `record` now refuses that name with the corrective command. No history is rewritten to hide this.
CheckStore's refusal counts group by Guard/Rule, and Framework guard_log recent/week reads are guard-based, so those
totals retain the old rows. Any analysis filtering Repo = the main module still excludes old worktree-labelled rows;
per-repo historical analysis must explicitly include those aliases. The changed logging affects future C# refusals.

Out of scope: Framework Python `guard_log.py:repo_of` and `stage_gate.py` folder attribution; applying the lane move;
row 1933; changes to other guards, settings or installed hooks. EnrollCheck.RepoOf is still an exact checkout reader.

## Validation and timing

Commands used only the Framework test project in Debug; Release was used solely for the explicitly requested timing.
Raw logs/TRXs, the baseline source archive, SQL snapshots and timing harness are in ignored `.codex_worktree/`.

| Proof | Passed | Failed | Total |
|---|---:|---:|---:|
| A cases, unchanged production at 6bb62371 | 9 | 28 | 37 |
| Corrected cases against a fresh archive of 6bb62371 | 9 | 28 | 37 |
| Final targeted: WorktreeModuleTests, CaptureTests (also TurnCaptureTests), K4Tests, FreshnessTests, K5bTests | 307 | 0 | 307 |
| Baseline full suite | 1,805 | 46 | 1,851 |
| Sole final full suite, at B | 1,841 | 47 | 1,888 |
| After correcting the old K7 assertion: entire K7Tests group | 35 | 0 | 35 |

A was never amended. Its refusal-logging fixture initially exercised the earlier missing-SQL-file budget guard (Repo
null), not the intended SQL-cut guard. B uses a literal SQL query and asserts that guard's actual message; the corrected
test was rerun red on the archived baseline, showing the worktree Repo versus expected main Repo. B also confines its
cache to the fixture, shortens fixture paths/enables git longpaths for the archive location, and fixes the two existing
CaptureTests that conflated module and source leaf. Initial setup attempts also caught an xUnit analyzer warning and
Windows' hidden .git-file overwrite behavior before A; those setup failures are not presented as regression evidence.

```powershell
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --filter 'FullyQualifiedName~WorktreeModuleTests|FullyQualifiedName~CaptureTests|FullyQualifiedName~K4Tests|FullyQualifiedName~FreshnessTests|FullyQualifiedName~K5bTests'
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --logger 'trx;LogFileName=final.trx' --results-directory .codex_worktree/final
& ./Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.exe testdiff .codex_worktree/baseline/baseline.trx .codex_worktree/final/final.trx
```

`kor testdiff` exits **1, NEW FAILURES** on the preserved full-suite TRXs: 43 distinct failing names in both,
one only in new (`K7Tests.Live11_TheRefusalIsLoggedRedacted`), 37 added names all passing, zero missing baseline names.
That assertion expected `Operations-worktree-module`; the actual required value is `Operations`. C changes only its
expected value to Git.ModuleOf and the whole K7 group passes. The original comparison was not edited or relabelled
green. There is **no second full-suite run after C**, honoring the one-final-run limit; final production is identical to B.
Baseline failures remain: 40 EnrollCheck rows; BrainRecall's worked-jobs hook; FileMemory's imported-memory coverage;
StartPages whole-memory wiring for KOR.Drafter and Framework; StartSplit's side-parts budget; and CoordinatorRoleRound2's
`ProjectWorkAndUnresolvedTargetsStillRefuse` Bash `rm -rf '{R}-coordinator-role'` case. No unrelated guard was changed.
Full durations: baseline 4m35s test time (285.52s command); final 4m44s test time (289.77s command). Both ran alone.
No blame sequences were supplied: testdiff marks started-but-unfinished cases NOT MEASURED.
All new test SQL writes use rollback-only BrainDb transactions. Independent post-targeted reads find **0 test events,
0 test memories, 0 test LaneStates, 0 test GuardRefusals** for `zz-worktree-%`; final readback confirms the same zeroes.

**SQL validation incident:** I wrapped the displayed script in SET PARSEONLY ON/OFF in one batch, incorrectly expecting
syntax-only validation. It actually executed and reported captured/updated/readback counts **86/86/86** inside the
helper's already-open outer transaction. The script's inner COMMIT did not commit that outer transaction; the helper's
finally block rolled it back. This was an unrequested live transactional write, despite no committed move. I reported it
immediately. A separate connection then compared all 86 original IDs, lanes, names, kinds, statuses, source repos/paths,
source kinds and retirement fields with the pre-check snapshot: **identical**. Another read after the full suite confirmed
the same. Bodies were not exported for that comparison; transaction rollback covers all database changes. The script
was therefore exercised under rollback, not merely parsed, and no further lane-update execution was attempted.

Release timing: same isolated console harness calling the actual CLI enforce dispatcher, process startup included,
real warm check cache, this worktree cwd, `git status --short` hook input, one warm-up then 40 sequential calls.
Both Release builds: zero warnings/errors; the final DLL contains the new refusal literal as UTF-16 bytes.

| Milliseconds | Before | After |
|---|---:|---:|
| Median | 141.06425 | 142.6242 |
| Mean | 141.2966525 | 143.444015 |
| Min / max | 135.0806 / 148.864 | 139.9751 / 153.3577 |

Median difference **+1.55995 ms (+1.11%)**, a sequential local measurement, not a statistical throughput guarantee.
The added resolution happens only in refusal logging; the allowed hot path adds no git call. The no-git-executable
worktree/nested-cwd case proves both the new resolver and TopLevelFast use metadata, not a spawned git process.

Rule 11 coverage: one fixture class checks the three transcript ask shapes, four git hooks, source-aware tool events,
start/end/compact, main/non-repo/dashed/spaced names, absolute/relative gitdir, no-git nested cwd, deleted-name refusal,
unprefixed sibling refusal, all nine known refusal conversion sites, real refusal-row attribution and rollback.
It does not test Python, hook installation, inaccessible metadata/ACLs, network filesystems, bare repos/submodule layouts,
all explicit overrides, or the migration's live triggers. A same-class fault still missed: direct SQL or a non-record
verb with an explicit worktree module can create another split lane; unreadable git metadata can also retain a leaf.
The static site gate covers the nine inventoried writers, not arbitrary future writers in new files.

Scorecard: one session, no agents; two full-suite runs total including baseline, two Release builds, nine targeted
invocations including setup failures and the corrected baseline replay; no product runs/install/push. Checks found the
test setup faults and module/source assumptions; no user intervention was needed. Token/dollar accounting unavailable.
Review lessons: assert the specific guard's message before treating a refusal-row failure as evidence for that guard;
review every consumer's test expectations before the sole full run; never rely on paired parse-mode SET statements in
one batch as a write barrier. The existing outer rollback limited this incident; it does not excuse executing the write.
Automatic approval review rejected cleanup of two failed-constructor fixture folders, including a retry with verified
literal paths (reason: "blocked by policy"). They remain in ignored test bin/base-source output under this worktree.
