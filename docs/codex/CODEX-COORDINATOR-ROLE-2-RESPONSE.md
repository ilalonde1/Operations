# Coordinator role, round 2 — response, 24 November 2026

Implemented locally on `coordinator-role`. The existing `kor enforce` guard now resolves same-command paths,
allows temp/check output and eligible worktree cleanup, and refuses coordinator self-demotion without the typed lift.
The full Framework comparison reports **NO NEW FAILURES**. There are **three remaining semantic false refusals**
against Ian's broad project-file line; their exact calls and the conflicting conservative requirements are below.
No push, installation, new hook, other guard/settings change, remote execution, or persistent database mutation.

Goal: correct the target-path boundary and close self-demotion. Done-when: frozen red cases, implementation, all three
complete replays with every changed decision and remaining refusal judged, targeted tests, one final Framework suite,
testdiff and Release timings. Scope: this worktree and Framework projects; no effort budget was specified.

| Commit | Contents |
|---|---|
| `309fdb32` | `coordinator role D: round-2 cases (codex)` — cases first, production unchanged from de3e853b |
| `00b2e853` | `coordinator role E: resolve write targets and guard self-demotion (codex)` |
| This response commit | Evidence only, committed last |

Starting HEAD was `72d50f49`: de3e853b plus **two** brief commits, f3e5d826 and 72d50f49. D was neither amended nor
edited after E began. Follow-up tests are in a separate file. No existing source outside the coordinator implementation
and its check-cache/enforce wiring was changed. Debug was used for tests; the brief specifically requires Release for timing.

**Ground truth and prior art.** Read both briefs, the round-one response, AGENTS.md, CLAUDE.md and HOW-WE-BUILD.md.
Searched Framework enforcement/capture/tests, `tools/`, `docs/`, `../KOR.Drafter/db/tools/`, and the existing `.codex_role`
evidence. Reused `CommandSegments`, `ShellText`, `DrafterPythonGuard.Parse`, `Git.Run`, the shared check snapshot,
`BrainDb` rollback transactions and `kor role replay`; no parallel guard/parser service was installed.
Read brain.Memory 139076 with `tools/SqlQuery/oqwide.py`. Ran
`python -I "C:/VIsual Studio Projects/Framework/tools/kor_review.py" verdicts 0fec30e4..de3e853b`, then read Summary/Findings
for review rows 3767–3770. All four contain the same range review. A bare `verdicts` on this branch listed zero commits,
so the explicit range was necessary. All transcripts and iteration data were local. The policy picture is the inspected
line-by-line replay, not a geometry/UI rendering. The single targeted filter `FullyQualifiedName~CoordinatorRole` measures
the old cases plus new path, role, approval and cache cases together.

**Implementation and review findings.**

| Files | Result |
|---|---|
| `Enforce/CoordinatorRoleGuard.cs`, `.Paths.cs` | Literal assignments, environment TEMP/TMP forms, chained directory state, temp/output exceptions, removal-only worktree-root cleanup, unresolved-variable remedy, self-demotion |
| `Enforce/CheckCache.Role.cs`, CLI `Program.Enforce.cs` | Shared checks survive a new session's DB outage; role/origin/approval remain per session; cache pruning; fresh shared checks reused on first role lookup |
| `Enforce/CoordinatorApproval.cs`, `SessionRoles.cs`, CLI `Program.Role.cs` | Incremental transcript validation and one lift event per session/approval, with transactional deduplication |
| `CoordinatorRoleCasesTests.cs` | Correct base comment to 0fec30e4; change the one incompatible other-session-temp assertion |
| `CoordinatorRoleRound2Tests.cs`, round-two fixture JSON | Frozen D cases and rollback integration checks |
| `CoordinatorRoleRound2FollowupTests.cs` | Later red gates for SQL-as-data, role argument ordering/DLL hosting, loop state, environment prefixes, product execution and shared snapshots |

Assignments are scoped to the command and copied into child shell/script frames. A child directory/assignment/output
allowance cannot change its caller. Bash literal, single/double quoted, export and PowerShell assignment forms work across
`;`, `&&`, `||` and newlines. `$TEMP`, `$TMP`, `${TEMP}`, `$env:TEMP`, `$env:TMP` and `%TEMP%` use the hook environment.
Unknown expressions and loop-assigned values remain unknown; later literal reassignment can establish them again.
Directory state preserves an unknown variable instead of silently retaining the preceding cwd.
Quoted SQL/code values stay data. An inline environment prefix still exposes the executable that follows it.

Temp allowances use normalized boundaries for `/tmp`, `C:/tmp`, TEMP and TMP, with traversal resolved first.
Any repo/worktree's `obj`, `bin`, `TestResults` segments and an allowed dotnet build/test's declared
`--results-directory`, `-o`, `--output` directory are allowed. Custom output allowances start at that command, remain
within that shell frame, and do not retroactively permit earlier writes. They do not exempt product execution.
`git -C ../KOR.Drafter check-ignore work/_runner/sets/probe/data.json` returned the path: ignored work/bundles are project
data, so **git-ignore is not used**. Repo roots are found from `.git` directories/pointer files; a historical sibling
worktree name can be associated with its existing main repo.

Cleanup applies only to a removal of a whole `<Repo>-<suffix>` sibling root. The main repo's successful
`git worktree list --porcelain` must omit it, or a listed root's successful `git ls-files -z` must be empty. A failed Git
query proves nothing and refuses. Live tracked roots, nested paths and creation of a sibling root stay refused.
`\\?\C:\...` normalization is supported. No actual worktree was removed during these proofs.

The refusal retains the delegation/typed-lift text. Unresolved refusals also name the variable, including an unresolved
cwd, and say **"assign it in the same command or use a literal path"**. Self-demotion is checked using the hook's own
session id; other-session worker assignment and self coordinator assignment remain available. Repeated session options
follow the CLI's last-value behavior. Executable and `dotnet kor.dll` / `dotnet exec kor.dll` launches are checked.
The existing typed/queued CLI human prompt, recorder corroboration, session identity and 12-hour window apply unchanged.
An unlogged lift still refuses. Role setting for another session still uses the existing logged `SessionRoles.Set` path.

Review 7(a): the actual shared file is **kor-checks-cache.json**, not kor-checks.json. Successful combined refreshes update
it, and failures reuse it at any age with the existing outage backoff. New session files do not duplicate checks.
7(b): on creation of a session cache, stale files older than 30 days are pruned and at most 127 others retained (128
including the new one). This is best effort under I/O failures/concurrent writers. A new session still needs its **own**
role read; a shared role would let one session inherit another's authority. Fresh shared checks are reused rather than
queried again. First-call measurements are below.
7(c): each check rereads the small prompt recorder, but normal transcript growth is scanned from the prior byte offset.
Recorder changes, truncation and equal-size rewrites reset the scan. The cached timestamp is checked for expiry every time.
A transactional locked lookup/insert deduplicates `(session, approval timestamp)`; production cache records successful
logging only after commit. Repeated calls then do no DB write. Caller-owned rollback tests query the event within their
transaction rather than treating a rolled-back cached log marker as durable. 7(d) is corrected in D's test comment.

**Red/green evidence.**

The red test sources built against unchanged de3e853b production sources (verified by Git diff). An initial test-source
escape typo was corrected before D; that compiler error is not counted as red proof. The completed red run was
**137 passed / 47 failed / 184 total**, and D was then committed. These are actual policy failures, not the round-one
missing-API gate. Existing behavior/stay-refused cases appropriately passed in red.

| Group | Red passed / failed | Final passed / failed |
|---|---:|---:|
| Round-one cases (87; one temp assertion intentionally revised) | 86 / 1 | 87 / 0 |
| Round-one integration (20) | 20 / 0 | 20 / 0 |
| D path/role/real-call cases (74) | 31 / 43 | 74 / 0 |
| D cache/lift integration (3) | 0 / 3 | 3 / 0 |
| Later follow-up cases (21) | Separate draft gates below | 21 / 0 |
| Final targeted total | — | **205 / 0** |

Follow-up red runs on draft E observed 5/13 failures (SQL value and role-option placement), then 5/18 failures (DLL
launches, absolute `/project` target and loop lifetime), 2/20 failures (environment prefixes hiding executables), and
1/21 failure (product script under work/obj). All pass in final. Those last two findings were draft regressions, fixed
before E was committed: the invariant is that write-output exceptions cannot hide or exempt a refused executable.
No D case was rewritten to accommodate an implementation result.

The old F01 test could not both remain literally unchanged and satisfy the new TEMP policy: it asserted REFUSE for
another session's scratchpad under TEMP. Its same test name now asserts ALLOW for that path and retains the other
lookalike-path refusals. Thus all **87 named old cases and 20 integration cases pass**, with that explicit policy revision.

Real calls in D: b6568d08 L1072, L1095, L6533, L7070; fa93ad77 L1234, L1546, L2262, L4421, L4729, L4757.
All pass. L4729 and L4757 remain refused. L1234 deliberately expects refusal at its **later** unresolved `$id` filename,
not the now-correctly-resolved `mkdir held`; this is the brief's conflicting example described below.

**Final suite and testdiff.** The complete Framework test project ran **once**, alone after the implementation was committed;
no source changed during it. It completed in **4 m 57 s**, rather than round one's recorded 2 m 8 s.

```powershell
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --filter 'FullyQualifiedName~CoordinatorRole'
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --logger 'trx;LogFileName=final.trx' --results-directory .codex_role/round2/final
& ./Kor.Operations.Framework.Cli/bin/Release/net8.0-windows/kor.exe testdiff .codex_role/final/final.trx .codex_role/round2/final/final.trx
```

| Run | Passed | Failed | Total |
|---|---:|---:|---:|
| Round-one final | 1,634 | 45 | 1,679 |
| Round-two final | **1,732** | **45** | **1,777** |

`testdiff` exited 0: **NO NEW FAILURES**. 42 distinct failing names occur in both; zero new failing names;
98 added names, all passing; zero missing baseline names. Distinct-name totals are 1,673 and 1,771 because existing
theory display names collide. No blame sequences were supplied, so hung cases are **NOT MEASURED** by testdiff;
both complete runs finished normally. Unchanged failure groups are EnrollCheckTests (40 result rows), BrainRecallTests,
FileMemoryLiveTests, StartPagesLiveTests (two rows), and StartSplitLiveTests. No unrelated failures were changed.
Afterward, read-only SQL counted Session LIKE `zz-role-%`: **brain.Event 0; oversight.GuardRefusal 0**.

**Release timing.** Both builds succeeded with zero warnings/errors. The final assembly was checked as UTF-16 for the new
unresolved-target remedy. Same process-launch/stdin PowerShell harness, Framework-origin main session, `git status --short`,
real cache, one initial call then 40 sequential samples; TEMP/TMP remained normal. The initial call is excluded from median.

| Measurement | Before (de3e853b production) | After (E production) |
|---|---:|---:|
| Warm median | 162.4676 ms | 154.3295 ms |
| Mean | 185.6725 ms | 160.0815 ms |
| Minimum | 145.2900 ms | 145.8720 ms |
| Maximum | 275.5257 ms | 203.9196 ms |
| First call, empty cache | 619.2640 ms | 1,326.2321 ms |

Warm median difference: -8.1381 ms (-5.0%). This is a local measurement including process startup, not a claim of a general
speedup. Cold calls are much slower and variable. An additional sequence with a new session each time took 596.0990 ms
to refresh expired shared checks plus role, then **519.3541, 529.7966, 527.6917 ms** using fresh shared checks plus just
the new role read (median **527.6917 ms**). CLI source text confirmed those paths. The role lookup cannot be omitted
merely because another session populated the check cache. Targeted tests prove one role read/window, shared fallback,
backoff, and absence of duplicated check payloads in role files.

**Replay method and limits.** Before replay used the Release binary built before D/E, with production identical to
de3e853b; final replay used the final Release build. All three complete transcripts were replayed sequentially. Each
main-thread tool_use id is counted once, sidechains excluded, stored roles applied at their event times; zero applicable
stored role events were found in each. No recorded tool call was executed. Current shell-wrapper text and current Git
worktree metadata are read, so historical checkout cleanup state is not independently reconstructed. The fourth transcript
was neither accessed nor inferred. Raw logs, TRXs, timings, exact call inventory and analysis scripts are under ignored
`.codex_role/round2/`. The following tables and complete decision lists are the committed evidence.

| Transcript | Calls | Before ALLOW / REFUSE | After ALLOW / REFUSE | Decision changes |
|---|---:|---:|---:|---:|
| cc3b7be3 | 341 | 278 / 63 | 289 / 52 | 11 |
| fa93ad77 | 434 | 375 / 59 | 412 / 22 | 37 |
| b6568d08 | 607 | 483 / 124 | 534 / 73 | 51 |
| Total | **1,382** | **1,136 / 246** | **1,235 / 147** | **99** |

All 99 decision changes are REFUSE → ALLOW. **Zero right refusals became ALLOW in the final replay.** A changed first
write target does not necessarily change the whole-call decision: b6568d08 L1383 permits the temp patch but still refuses
the later project mkdir/copy; L1431 permits scratch output but still refuses the later removal of project source by mv.
All refusal calls named in round one's 31-call table remain refused. The phrase "every cc3b7be3 refusal" cannot mean
every old replay refusal: that would retain known false refusals of temp writes, SQL data and the CODEX brief at L4417.

Refusal classes below classify the **reported refusing target at each revision**, not just the first command's purpose.
An unresolved target can move to the literal-project class after resolution. Scratch and temp classes are separated to
show the originally reported mechanism. There are no `obj/coord` refusal examples in these three transcripts; that class
is covered by the synthetic gates, and the brief's three examples presumably belong to the withheld fourth transcript.

**cc3b7be3 — REFUSE by class.**

| Class | Before | After |
|---|---:|---:|
| Project file Edit/Write | 27 | 27 |
| Product run | 8 | 8 |
| Literal project shell write | 17 | 17 |
| Own scratch through an earlier assignment | 7 | 0 |
| Temp literal/environment target | 2 | 0 |
| Build/test output | 0 | 0 |
| Whole worktree root cleanup | 0 | 0 |
| Other unresolved target | 1 | 0 |
| Other literal location outside allowed roots | 0 | 0 |
| SQL text misparsed as shell output | 1 | 0 |
| Total | 63 | 52 |

**fa93ad77 — REFUSE by class.**

| Class | Before | After |
|---|---:|---:|
| Project file Edit/Write | 8 | 8 |
| Product run | 4 | 4 |
| Literal project shell write | 0 | 6 |
| Own scratch through an earlier assignment | 21 | 0 |
| Temp literal/environment target | 12 | 0 |
| Build/test output | 0 | 0 |
| Whole worktree root cleanup | 2 | 0 |
| Other unresolved target | 11 | 3 |
| Other literal location outside allowed roots | 1 | 1 |
| SQL text misparsed as shell output | 0 | 0 |
| Total | 59 | 22 |

**b6568d08 — REFUSE by class.**

| Class | Before | After |
|---|---:|---:|
| Project file Edit/Write | 69 | 69 |
| Product run | 0 | 0 |
| Literal project shell write | 1 | 3 |
| Own scratch through an earlier assignment | 40 | 0 |
| Temp literal/environment target | 11 | 0 |
| Build/test output | 0 | 0 |
| Whole worktree root cleanup | 2 | 0 |
| Other unresolved target | 0 | 1 |
| Other literal location outside allowed roots | 0 | 0 |
| SQL text misparsed as shell output | 1 | 0 |
| Total | 124 | 73 |

**Every changed decision (99).** `P/` means `C:/VIsual Studio Projects/`. `S(prefix)` means that transcript session's
own `C:/Users/ilalonde/AppData/Local/Temp/claude/5102428d3890278f/<full-session-UUID>/scratchpad`.
TEMP means the hook's user temp root. Targets are the former refusing destination, resolved from the command where
available; SQL rows explicitly have no real output target. The raw replay's ALLOW target is its final cwd, not a write log.

| Transcript / line | Before | After | Target |
|---|---|---|---|
| cc3b7be3 L784 | REFUSE | ALLOW | SQL comparison, no shell destination (P/Operations/n*300) |
| cc3b7be3 L839 | REFUSE | ALLOW | C:/tmp/ip.txt |
| cc3b7be3 L1927 | REFUSE | ALLOW | S(cc3b7be3)/build-base.txt (and other sibling test logs) |
| cc3b7be3 L2616 | REFUSE | ALLOW | C:/tmp/rv.txt |
| cc3b7be3 L3561 | REFUSE | ALLOW | $S/kw4-new.txt → S(cc3b7be3)/kw4-new.txt |
| cc3b7be3 L3571 | REFUSE | ALLOW | $S/kw4-base.txt → S(cc3b7be3)/kw4-base.txt |
| cc3b7be3 L3613 | REFUSE | ALLOW | $S/kw4-base.txt → S(cc3b7be3)/kw4-base.txt |
| cc3b7be3 L3643 | REFUSE | ALLOW | $S/held → S(cc3b7be3)/held |
| cc3b7be3 L3670 | REFUSE | ALLOW | S(cc3b7be3)/held/old.txt |
| cc3b7be3 L3688 | REFUSE | ALLOW | S(cc3b7be3)/held/new-full.txt |
| cc3b7be3 L4417 | REFUSE | ALLOW | P/KOR.Drafter-step5/docs/codex/CODEX-STEP5-2248-DISPOSITIONS-2.md |
| fa93ad77 L261 | REFUSE | ALLOW | $S → S(fa93ad77) |
| fa93ad77 L300 | REFUSE | ALLOW | $S/m5575.jsonl → S(fa93ad77)/m5575.jsonl |
| fa93ad77 L331 | REFUSE | ALLOW | $S/moments-out.txt → S(fa93ad77)/moments-out.txt |
| fa93ad77 L353 | REFUSE | ALLOW | $S/trx → S(fa93ad77)/trx |
| fa93ad77 L450 | REFUSE | ALLOW | $S/testdiff.txt → S(fa93ad77)/testdiff.txt |
| fa93ad77 L553 | REFUSE | ALLOW | P/Operations-kor-repeat-narrow |
| fa93ad77 L1297 | REFUSE | ALLOW | $S/find_unc_heredoc.py → S(fa93ad77)/find_unc_heredoc.py |
| fa93ad77 L1368 | REFUSE | ALLOW | $S/heredoc-tests.txt → S(fa93ad77)/heredoc-tests.txt |
| fa93ad77 L1546 | REFUSE | ALLOW | C:/tmp/dirty.txt |
| fa93ad77 L1590 | REFUSE | ALLOW | $S/live-probe.md → S(fa93ad77)/live-probe.md |
| fa93ad77 L1850 | REFUSE | ALLOW | $S/pipefail → S(fa93ad77)/pipefail |
| fa93ad77 L2112 | REFUSE | ALLOW | P/Operations-bash-heredoc-narrow |
| fa93ad77 L2262 | REFUSE | ALLOW | C:/tmp/hits.txt |
| fa93ad77 L2448 | REFUSE | ALLOW | C:/tmp/abs_t.py |
| fa93ad77 L2479 | REFUSE | ALLOW | $S/heredoc → S(fa93ad77)/heredoc |
| fa93ad77 L2492 | REFUSE | ALLOW | $S/recover_rule.py → S(fa93ad77)/recover_rule.py |
| fa93ad77 L2528 | REFUSE | ALLOW | C:/tmp/shas.txt |
| fa93ad77 L2541 | REFUSE | ALLOW | $S/push3.txt → S(fa93ad77)/push3.txt |
| fa93ad77 L2706 | REFUSE | ALLOW | C:/tmp/h6.txt |
| fa93ad77 L2851 | REFUSE | ALLOW | $S/rec137842.md → S(fa93ad77)/rec137842.md |
| fa93ad77 L3032 | REFUSE | ALLOW | $S/n6-build.txt → S(fa93ad77)/n6-build.txt |
| fa93ad77 L3042 | REFUSE | ALLOW | $S/trx/n6-new.log → S(fa93ad77)/trx/n6-new.log |
| fa93ad77 L3059 | REFUSE | ALLOW | TEMP/pr-base |
| fa93ad77 L3158 | REFUSE | ALLOW | $S/startdocs_replay.py → S(fa93ad77)/startdocs_replay.py |
| fa93ad77 L3220 | REFUSE | ALLOW | TEMP/kor-n6-publish |
| fa93ad77 L3319 | REFUSE | ALLOW | C:/tmp/n6shas.txt |
| fa93ad77 L3443 | REFUSE | ALLOW | $S/n6r2-build.txt → S(fa93ad77)/n6r2-build.txt |
| fa93ad77 L3460 | REFUSE | ALLOW | $S/trx/n6r2-new.log → S(fa93ad77)/trx/n6r2-new.log |
| fa93ad77 L3669 | REFUSE | ALLOW | C:/tmp/r2.txt |
| fa93ad77 L4079 | REFUSE | ALLOW | $S/probe-cs.txt → S(fa93ad77)/probe-cs.txt |
| fa93ad77 L4097 | REFUSE | ALLOW | $S/probe-cs2.txt → S(fa93ad77)/probe-cs2.txt |
| fa93ad77 L4154 | REFUSE | ALLOW | C:/tmp/h4.txt |
| fa93ad77 L4399 | REFUSE | ALLOW | TEMP/kor-scope-probe-literal |
| fa93ad77 L4421 | REFUSE | ALLOW | TEMP/kor-scope-probe-other |
| fa93ad77 L4441 | REFUSE | ALLOW | TEMP/kor-scope-probe-* |
| fa93ad77 L4713 | REFUSE | ALLOW | TEMP/b1l04-src |
| fa93ad77 L5212 | REFUSE | ALLOW | TEMP/state-final2.md |
| b6568d08 L158 | REFUSE | ALLOW | SQL comparison, no shell destination (=$s;) |
| b6568d08 L1072 | REFUSE | ALLOW | $S/r1.txt → S(b6568d08)/r1.txt |
| b6568d08 L1095 | REFUSE | ALLOW | $S/r1.txt → S(b6568d08)/r1.txt |
| b6568d08 L1114 | REFUSE | ALLOW | $S/dry.txt → S(b6568d08)/dry.txt |
| b6568d08 L1141 | REFUSE | ALLOW | $S/w4.txt → S(b6568d08)/w4.txt |
| b6568d08 L1190 | REFUSE | ALLOW | $S/dry-out.txt → S(b6568d08)/dry-out.txt |
| b6568d08 L1224 | REFUSE | ALLOW | $S/flags.txt → S(b6568d08)/flags.txt |
| b6568d08 L1311 | REFUSE | ALLOW | $S/flags-v2.txt → S(b6568d08)/flags-v2.txt |
| b6568d08 L1334 | REFUSE | ALLOW | $S/dry-out.txt → S(b6568d08)/dry-out.txt |
| b6568d08 L1359 | REFUSE | ALLOW | $S/w4.txt → S(b6568d08)/w4.txt |
| b6568d08 L1883 | REFUSE | ALLOW | $S/dry-out4.txt → S(b6568d08)/dry-out4.txt |
| b6568d08 L1908 | REFUSE | ALLOW | $S/dry-out5.txt → S(b6568d08)/dry-out5.txt |
| b6568d08 L1934 | REFUSE | ALLOW | $S/proof → S(b6568d08)/proof |
| b6568d08 L1952 | REFUSE | ALLOW | $S/hookproof.py → S(b6568d08)/hookproof.py |
| b6568d08 L2053 | REFUSE | ALLOW | $S/proof-result.txt → S(b6568d08)/proof-result.txt |
| b6568d08 L2073 | REFUSE | ALLOW | $S/dry-out6.txt → S(b6568d08)/dry-out6.txt |
| b6568d08 L2089 | REFUSE | ALLOW | $S/trx → S(b6568d08)/trx |
| b6568d08 L2093 | REFUSE | ALLOW | $S/trx/base.log → S(b6568d08)/trx/base.log |
| b6568d08 L2117 | REFUSE | ALLOW | $S/state-3.md → S(b6568d08)/state-3.md |
| b6568d08 L2544 | REFUSE | ALLOW | $S/proof-installed.txt → S(b6568d08)/proof-installed.txt |
| b6568d08 L2629 | REFUSE | ALLOW | $S/dry-out7.txt → S(b6568d08)/dry-out7.txt |
| b6568d08 L2663 | REFUSE | ALLOW | $S/selfclose.json → S(b6568d08)/selfclose.json |
| b6568d08 L2674 | REFUSE | ALLOW | $S/selfclose-out.txt → S(b6568d08)/selfclose-out.txt |
| b6568d08 L2694 | REFUSE | ALLOW | $S/dry-out8.txt → S(b6568d08)/dry-out8.txt |
| b6568d08 L2739 | REFUSE | ALLOW | $S/dry-out9.txt → S(b6568d08)/dry-out9.txt |
| b6568d08 L4449 | REFUSE | ALLOW | S(b6568d08)/gr-since.sql |
| b6568d08 L5421 | REFUSE | ALLOW | $S → S(b6568d08)/holdout |
| b6568d08 L5544 | REFUSE | ALLOW | S(b6568d08)/trx/narrow.log |
| b6568d08 L5864 | REFUSE | ALLOW | $S/old.txt → S(b6568d08)/dry/old.txt |
| b6568d08 L6347 | REFUSE | ALLOW | C:/tmp/r3build.log |
| b6568d08 L6367 | REFUSE | ALLOW | $S/r3.txt → S(b6568d08)/dry/r3.txt |
| b6568d08 L6440 | REFUSE | ALLOW | C:/tmp/r3review.txt |
| b6568d08 L6455 | REFUSE | ALLOW | $S/r3.log → S(b6568d08)/trx/r3.log |
| b6568d08 L6479 | REFUSE | ALLOW | C:/tmp/r3push.txt |
| b6568d08 L6485 | REFUSE | ALLOW | TEMP/r3install.txt |
| b6568d08 L6502 | REFUSE | ALLOW | $S/inst.txt → S(b6568d08)/dry/inst.txt |
| b6568d08 L6533 | REFUSE | ALLOW | //?/P/Operations-kor-whole-narrow |
| b6568d08 L6541 | REFUSE | ALLOW | P/Operations-kor-whole-narrow |
| b6568d08 L6585 | REFUSE | ALLOW | C:/tmp/msg.txt |
| b6568d08 L6598 | REFUSE | ALLOW | C:/tmp/msg.txt |
| b6568d08 L6721 | REFUSE | ALLOW | $S/rep.sql → S(b6568d08)/rep.sql |
| b6568d08 L6736 | REFUSE | ALLOW | $S/rep.sql → S(b6568d08)/rep.sql |
| b6568d08 L6857 | REFUSE | ALLOW | C:/tmp/msg.txt |
| b6568d08 L6867 | REFUSE | ALLOW | C:/tmp/rpush.txt |
| b6568d08 L6875 | REFUSE | ALLOW | C:/tmp/msg.txt |
| b6568d08 L6898 | REFUSE | ALLOW | C:/tmp/msg.txt |
| b6568d08 L7070 | REFUSE | ALLOW | $S/ev.sql → S(b6568d08)/ev.sql |
| b6568d08 L7074 | REFUSE | ALLOW | $S/ev.sql → S(b6568d08)/ev.sql |
| b6568d08 L7090 | REFUSE | ALLOW | $S/ev2.sql → S(b6568d08)/ev2.sql |
| b6568d08 L7488 | REFUSE | ALLOW | $S/row2139check.sql → S(b6568d08)/row2139check.sql |
| b6568d08 L7561 | REFUSE | ALLOW | $S/msg.txt → S(b6568d08)/msg.txt |

**Every remaining refusal, judged against Ian's line (147).** These judgments inspect the call, not merely its rule label.
"RIGHT" means project edits/data or named product execution. "WRONG" identifies a semantically legitimate scratch
operation still blocked by the specified conservative path model. The two unresolved scratch calls obey items 1/5 but
overblock Ian's broader intent; they are not presented as accurate project-work detections.

| Transcript | Remaining | Right | Wrong |
|---|---:|---:|---:|
| cc3b7be3 | 52 | 52 | 0 |
| fa93ad77 | 22 | 20 | 2 |
| b6568d08 | 73 | 72 | 1 |
| Total | **147** | **144** | **3** |

| Transcript / line | Rule | Target | Judgment | Reason |
|---|---|---|---|---|
| cc3b7be3 L351 | product run | P/KOR.Drafter-step5/Kor.Operations.EngineeringTools.TakeoffCli/bin/Release/net8.0/takeoff.exe | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| cc3b7be3 L367 | project file edit | P/KOR.Drafter/work/_acceptance/2248/build_bundle.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L374 | product run | P/KOR.Drafter/work/_acceptance/2248/build_bundle.py | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| cc3b7be3 L399 | project file edit | P/KOR.Drafter/work/_acceptance/2248/bundle/run-record.json | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L403 | project file edit | P/KOR.Drafter/work/_acceptance/2248/bundle/roles.json | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L419 | shell write | P/KOR.Drafter/work/_acceptance/2248/model-audit-1-core.txt | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L770 | shell write | P/KOR.Drafter/work/_acceptance/2248/fixtures-run.txt | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L939 | project file edit | P/KOR.Drafter/work/_acceptance/2248/checker-acceptance.json | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L1083 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L1115 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L1131 | project file edit | P/KOR.Drafter/work/_acceptance/2248/build_bundle.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L1135 | product run | P/KOR.Drafter/work/_acceptance/2248/build_bundle.py | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| cc3b7be3 L1453 | project file edit | P/KOR.Drafter/work/_acceptance/2248/work/mesh_coverage.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L1457 | product run | P/KOR.Drafter/work/_acceptance/2248/work/mesh_coverage.py | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| cc3b7be3 L1867 | shell write | P/KOR.Drafter/work/_acceptance/2248/model-audit-5-coverage.txt | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L1901 | shell write | P/KOR.Drafter/work/_acceptance/2248/fixtures-run-b.txt | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L2140 | shell write | P/KOR.Drafter/work/_acceptance/2248/model-audit-6-db190.txt | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L2344 | project file edit | P/KOR.Drafter/work/_acceptance/2248/diag/make_variants.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L2345 | product run | P/KOR.Drafter/work/_acceptance/2248/diag/make_variants.py | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| cc3b7be3 L2447 | shell write | P/KOR.Drafter/work/_acceptance/2248/diag/res-a/mesh | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L2478 | shell write | P/KOR.Drafter/work/_acceptance/2248/diag/res-b/mesh | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L2497 | shell write | P/KOR.Drafter/work/_acceptance/2248/diag/res-c/mesh | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L2741 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L2743 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L2745 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L2747 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L2803 | project file edit | P/KOR.Drafter/work/_acceptance/2248/bundle/evidence/EVIDENCE-README.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L3390 | shell write | P/KOR.Drafter/work/_acceptance/2248/model-audit-7-db191.txt | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L3405 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L3413 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L3940 | product run | P/KOR.Drafter/work/_acceptance/2248/bundle/evidence/registration/cadcmp_partial_test.py | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| cc3b7be3 L3973 | shell write | P/KOR.Drafter/work/_acceptance/2248/diag/refuse-nosample | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L3981 | shell write | P/KOR.Drafter/work/_acceptance/2248/bundle/evidence/refusals | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L4073 | project file edit | P/KOR.Drafter/work/_acceptance/2248/work/fix_refusal_headers.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4077 | product run | P/KOR.Drafter/work/_acceptance/2248/work/fix_refusal_headers.py | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| cc3b7be3 L4105 | project file edit | P/KOR.Drafter/work/_acceptance/2248/bundle/evidence/refusals/R-bulk-without-sample-refused.txt | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4107 | project file edit | P/KOR.Drafter/work/_acceptance/2248/bundle/evidence/refusals/R-bulk-changed-sample-refused.txt | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4111 | shell write | P/KOR.Drafter/work/_acceptance/2248/work/fix_refusal_headers.py | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L4132 | project file edit | P/KOR.Drafter/work/_acceptance/2248/bundle/evidence/refusals/R-bulk-without-sample-refused.txt | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4235 | shell write | P/KOR.Drafter/work/_acceptance/2248/fixtures-run-g.txt | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L4251 | project file edit | P/KOR.Drafter/work/_acceptance/2248/checker-acceptance.json | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4255 | shell destination | P/KOR.Drafter/work/_acceptance/2248/bundle/checker-fixtures-run.txt | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L4270 | project file edit | P/KOR.Drafter/work/_acceptance/2248/build_bundle.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4272 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4287 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4289 | project file edit | P/KOR.Drafter/work/_acceptance/2248/README-base.md | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4309 | product run | P/KOR.Drafter/work/_acceptance/2248/build_bundle.py | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| cc3b7be3 L4318 | project file edit | P/KOR.Drafter/work/_acceptance/2248/checker-acceptance.json | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4322 | project file edit | P/KOR.Drafter/work/_acceptance/2248/build_bundle.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| cc3b7be3 L4326 | shell write | P/KOR.Drafter/work/_acceptance/2248/bundle/evidence/registration/__pycache__ | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L4387 | in-place edit | P/KOR.Drafter/work/_acceptance/2248/work/read_rulings.py | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| cc3b7be3 L4403 | in-place edit | P/KOR.Drafter/work/_acceptance/2248/work/read_rulings.py | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| fa93ad77 L251 | shell write | C:/Users/ilalonde/AppData/Local/claude/5102428d3890278f/fa93ad77-7578-4f8b-a60b-b8ad6945c7b2/scratchpad/build-cli.txt | WRONG | Non-project scratch-like location; TEMP/../ escapes the allowed temp root. Deliberate bounded path policy, not a project-file finding. |
| fa93ad77 L1234 | unresolved shell write | S(fa93ad77)/held/p$id.txt | WRONG | Actual destination is scratch; loop/substitution target remains unresolved, so brief items 1/5 require conservative refusal. |
| fa93ad77 L1898 | project file edit | P/Operations-bash-pipefail-narrow/tools/claude_bash_guard_test.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| fa93ad77 L1936 | project file edit | P/Operations-bash-pipefail-narrow/tools/claude_bash_guard.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| fa93ad77 L1938 | project file edit | P/Operations-bash-pipefail-narrow/tools/claude_bash_guard.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| fa93ad77 L2002 | project file edit | P/Operations-bash-pipefail-narrow/tools/claude_bash_guard.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| fa93ad77 L2004 | project file edit | P/Operations-bash-pipefail-narrow/tools/claude_bash_guard.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| fa93ad77 L2006 | project file edit | P/Operations-bash-pipefail-narrow/tools/claude_bash_guard.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| fa93ad77 L2008 | project file edit | P/Operations-bash-pipefail-narrow/tools/claude_bash_guard_test.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| fa93ad77 L2033 | project file edit | P/Operations-bash-pipefail-narrow/tools/claude_bash_guard.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| fa93ad77 L4729 | product run | P/KOR.Drafter/docs/one-tool/run-on-210/send-set.ps1 | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| fa93ad77 L4740 | shell write | P/KOR.Drafter/work/_runner/sets/step5-2248-b1l04 | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| fa93ad77 L4757 | shell write | P/KOR.Drafter/work/_runner/sets/step5-2248-b1l04 | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| fa93ad77 L4763 | product run | P/KOR.Drafter/docs/one-tool/run-on-210/send-set.ps1 | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| fa93ad77 L4851 | unresolved shell destination | $to | RIGHT | fetch-set.ps1 copies into KOR.Drafter/work/_runner/results; $to is computed in its loop. |
| fa93ad77 L4867 | shell write | P/KOR.Drafter/work/_runner/sets/step5-2248-b1l04 | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| fa93ad77 L4873 | product run | P/KOR.Drafter/docs/one-tool/run-on-210/send-set.ps1 | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| fa93ad77 L4931 | shell write | P/KOR.Drafter/work/_runner/sets/step5-2248-b1l04 | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| fa93ad77 L4959 | shell write | P/KOR.Drafter/work/_runner/sets/step5-2248-b1l04 | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| fa93ad77 L4980 | shell write | P/KOR.Drafter/work/_runner/sets/step5-2248-b1l04 | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| fa93ad77 L4988 | product run | P/KOR.Drafter/docs/one-tool/run-on-210/send-set.ps1 | RIGHT | Explicit product entry point, including model-audit --help under the requested command rule. |
| fa93ad77 L5180 | unresolved shell destination | $to | RIGHT | fetch-set.ps1 copies into KOR.Drafter/work/_runner/results; $to is computed in its loop. |
| b6568d08 L854 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeTranscript.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L870 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L892 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L896 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L930 | project file edit | P/Operations/Kor.Operations.Framework.Cli/Program.Whole.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L946 | project file edit | P/Operations/Kor.Operations.Framework.Cli/Program.Context.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L948 | project file edit | P/Operations/Kor.Operations.Framework.Cli/Program.Context.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L950 | project file edit | P/Operations/Kor.Operations.Framework.Cli/Program.Context.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L952 | project file edit | P/Operations/Kor.Operations.Framework.Cli/Program.Context.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L954 | project file edit | P/Operations/Kor.Operations.Framework.Cli/Program.Whole.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L969 | project file edit | P/Operations/Kor.Operations.Framework.Cli/Program.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L971 | project file edit | P/Operations/Kor.Operations.Framework.Cli/Program.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L991 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1045 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1057 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1059 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1068 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1070 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1091 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1093 | project file edit | P/Operations/Kor.Operations.Framework.Cli/Program.Whole.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1168 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1170 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1188 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1290 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1292 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1294 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1309 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1330 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1332 | project file edit | P/Operations/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1383 | shell write | P/Operations-2139/Kor.Operations.Framework/Whole | RIGHT | Temp patch is allowed; later mkdir/copy writes project source in Operations-2139. |
| b6568d08 L1424 | shell write | P/Operations/Kor.Operations.Framework.Cli/Program.Whole.cs | RIGHT | Writes/removes project or bundle data outside permitted output directories. |
| b6568d08 L1431 | shell source removal | P/Operations/Kor.Operations.Framework.Cli/Program.Whole.cs | RIGHT | Scratch output is allowed; later mv removes project source. |
| b6568d08 L1661 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeTranscript.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1677 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1679 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1681 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1683 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1685 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1723 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1760 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1762 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1764 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1809 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1838 | project file edit | P/Operations-2139/tools/claude_bash_sample_guard.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1840 | project file edit | P/Operations-2139/tools/claude_bash_sample_guard.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1863 | project file edit | P/Operations-2139/tools/claude_bash_sample_guard_test.py | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1904 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1906 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1984 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1986 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1988 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1990 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L1999 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2001 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2047 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2049 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2051 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2109 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2298 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2300 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2315 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2322 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2330 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2358 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeTranscript.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2360 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeTranscript.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2623 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2625 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2627 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2690 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2692 | project file edit | P/Operations-2139/Kor.Operations.Framework.Tests/WholeGateTests.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2721 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L2737 | project file edit | P/Operations-2139/Kor.Operations.Framework/Whole/WholeGate.cs | RIGHT | Project code, tests, scripts or bundle data; outside the permitted brief/output paths. |
| b6568d08 L5832 | unresolved shell destination | S(b6568d08)/dry/t/$i-$ | WRONG | Actual destination is scratch; loop/substitution target remains unresolved, so brief items 1/5 require conservative refusal. |

**Rule 11: class, coverage and limits.** The class is confusing a command's spelling/context with the destination or
capability it actually names: a variable/temp/check-output spelling must not become a project write, and an allowed
prefix/output location must not hide a later project operation or role escape. D contains every named new example plus
at least three forms for edit, shell/project, product, scratch, temp, output, cleanup and unresolved classes, spanning
Bash/PowerShell, quoting, Windows and /c/ paths. It compares the real hook policy. Integration covers shared outage
fallback, cache bounds, role history/setting, typed lift/refusal and rollback. The complete transcript differential is
paired with the explicit judgments above, since a differential alone cannot detect a fault shared by both versions.

Does not cover: arbitrary program effects; complete shell/control-flow semantics; aliases/functions built at runtime;
filesystem junctions/symlinks; shell wrappers beyond the existing depth/file/size limits; forged recorder/cache/transcript
files; other tools not enrolled in enforce; proof that a dotnet check is checking a worker's branch. Incremental approval
scans assume append-only transcript growth; a larger non-append replacement is not detected by the cheap length/time
check. Role caches can be stale for 30 seconds and through DB backoff; cleanup replay sees today's Git state.
The CLI role setter remains an audited primitive: the boundary is its enrolled hook call, not OS-level access control.

Concrete same-class misses: an allowed `dotnet test` can rewrite project fixtures; Python `-c` can write files without a
recognized shell destination; a runtime-generated executable/command can escape name recognition. An explicitly chosen
dotnet output directory can name existing project data, and directories named obj/bin/TestResults are allowed by the
requested rule even if they contain source-like data. None of these was silently claimed covered.

**Corrections and unresolved contradictions in the brief.**

- The four-transcript 257-refusal inventory cannot be independently reproduced from the three supplied transcripts.
  These three have 246 old refusals, including 104 file edits and 12 product-run first refusals. The fourth stays held back.
- Git Bash on this machine maps `/tmp` to `C:/Users/ilalonde/AppData/Local/Temp`, **not C:/tmp**:
  `C:/Program Files/Git/bin/bash.exe -lc 'cygpath -w /tmp'` verified it. C:/tmp is nevertheless allowed because the brief
  explicitly requests both. Inside Git Bash TEMP and TMP are `/tmp`; the hook process has its own Windows temp values.
  `cygpath -w "$TEMP/../claude"` inside Bash yields `C:/Program Files/Git/claude`, whereas resolving the hook's Windows
  TEMP value yields `C:/Users/ilalonde/AppData/Local/claude`. Either is outside the allowed temp tree. Thus fa93ad77 L251
  cannot honestly be described as a write *under* TEMP. It is non-project scratch-like work still conservatively refused.
- fa93ad77 L1234 does resolve S/cwd and mkdir correctly now, but its later `$S/held/p$id.txt` depends on a loop variable.
  b6568d08 L5832 similarly copies to `$S/t/$i-$(basename "$f")`. Requiring every such call ALLOW conflicts with items 1/5
  requiring loop/substitution values to remain unresolved and still-unresolved targets to refuse. I kept the explicit
  conservative requirements and counted both calls as semantic false refusals, rather than claiming they were fixed.
- The single old "other session scratch is refused" assertion contradicts the new whole-TEMP allowance and was revised
  openly in D. All other old case assertions were retained.
- Review rows 3767–3770 share one range review, rather than four independently different reviews. The shared cache's
  real filename is kor-checks-cache.json. The starting HEAD contains two brief commits, not one.
- No right refusal in the inspected three transcripts became ALLOW in final. There is no claim about the withheld
  fourth transcript, arbitrary program effects, or the three remaining semantic false refusals.

**Process scorecard.** One session, no delegated agents, one complete Framework run in this round, two Release builds,
read-only before/final replay sets plus intermediate diagnostic replays, zero actual recorded product commands, zero
installs/pushes. D's red gate and later follow-up gates found the defects before final verification. The two draft
regressions described above were caught by added gates, not by the user. Eleven completed targeted runs (including
red and follow-up gates) plus two compiler-only attempts preceded the full run; no full-suite rerun was used to erase a
failure. Exact logs remain local. Token/dollar accounting is unavailable. The main reusable improvement is keeping
path-write permission separate from executable/role capability, with environment-prefixed and hosted invocations in
the same regression filter. Review/board writes were not made because this brief forbids live database writes.
