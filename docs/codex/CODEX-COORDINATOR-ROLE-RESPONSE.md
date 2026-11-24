# Coordinator role guard — response, 24 November 2026

Implemented and committed locally on `coordinator-role`. The guard is inside `kor enforce`; no new hook, Python guard,
settings change, migration, installation or push. The complete semantic boundary cannot be guaranteed by a command hook:
in particular, an allowed test can rewrite a fixture. The implemented boundary and its limits are stated below.

Goal: refuse coordinator project work while preserving coordination and checks. Done-when: the case gate, role history,
enforcement, lift, replay, suite comparison and timing are evidenced here. Scope/budget: this worktree and the three
Framework projects; no effort budget per the brief; proofs sequential; database mutations only in rollback-only tests.

Commits before this response:

| Commit | Contents |
|---|---|
| `3ac858e6` | `coordinator role A: cases (codex)` — 87 failing cases, committed alone |
| `6236ea4f` | `coordinator role B: enforce session roles and replay tool calls (codex)` |

A was not amended or changed after B began. Starting HEAD was `0fec30e4`, not literally `206615f3`:
`git diff 206615f3 0fec30e4 --name-only` lists only `docs/codex/CODEX-COORDINATOR-ROLE.md`. The baseline run therefore used
the exact production and test sources at `206615f3`, with the brief added. No other checkout was edited.

**Prior art and ground truth.** Searched `tools/`, `docs/`, `../KOR.Drafter/db/tools/`, Framework enforcement/capture/budget
code and tests, and the external Framework handover/review tools. Extended `DrafterPythonGuard.Parse`,
`ShellText`/`CommandSegments`, `CheckStore`/`CheckCache`, `BrainStore`/`BrainDb`, `StallStore.LogGuardRefusal`, and the existing
CLI dispatch. Used `BudgetApproval`'s provenance and expiry shape without changing that guard. Checked the named transcript
lines, Framework `CLAUDE.md` and `HANDOVER.md`, issue 15, and the earlier gap-analysis response. Read brain.Memory
133998, 134051, 134054, 138092, 138344, 138345 and 139076 through the existing `tools/SqlQuery/oqwide.py` helper.
The live `brain.LaneState` columns are Id, Lane, ResumeBlock, WrittenBy, WrittenAtUtc and Session: no role column.

The data was already local. The targeted command below measures the case gate plus role/approval/cache integration.
The replay is the inspectable policy picture: original line, ALLOW/REFUSE, rule and target. Its named rows were inspected
and compared, rather than relying only on aggregate counts. There is no geometry or UI artifact to render here.

**Files and verbs.**

| File | Change |
|---|---|
| `Kor.Operations.Framework/Enforce/CoordinatorRoleGuard.cs` | Role boundary, literal target judging, command directory state, product entry points, refusal text |
| `Kor.Operations.Framework/Enforce/SessionRoles.cs` | Read/set/history over existing brain.Event rows |
| `Kor.Operations.Framework/Enforce/CoordinatorApproval.cs` | Recorder plus typed transcript corroboration and 12-hour expiry |
| `Kor.Operations.Framework/Enforce/CheckCache.Role.cs` | Per-session extension of the existing check cache |
| `Kor.Operations.Framework/Enforce/CheckStore.cs` | Shared reader and one SQL command returning checks plus role |
| `Kor.Operations.Framework.Cli/Program.Enforce.cs` | Runs the coordinator guard in existing enforcement |
| `Kor.Operations.Framework.Cli/Program.Role.cs` | `role set`, `role show`, `role replay`, origin recovery and refusal/lift logging |
| `Kor.Operations.Framework.Cli/Program.cs` | Dispatch and usage |
| `Kor.Operations.Framework.Tests/CoordinatorRoleCasesTests.cs` | Frozen cases-first harness |
| `Kor.Operations.Framework.Tests/Fixtures/coordinator-role-cases.json` | 31 real calls: exact tool, operative input, cwd and session; file contents omitted |
| `Kor.Operations.Framework.Tests/CoordinatorRoleIntegrationTests.cs` | Storage, hook, rollback, approval, cache, replay and follow-up cases |

`kor role set worker|coordinator --session ID [--because TEXT]` inserts exactly one gate event, Ref `role:<role>`, with
session/role/reason in Detail and Session also populated. `role show --session ID` prints the role and its event or start
origin. These verbs were exercised through `Program.Run` with rollback-only transactions, not by changing a live role.
`role replay <jsonl>` was exercised as the built executable against all three real files.

**Role decision and storage.** A nonempty `agent_id` or a `/subagents/` transcript path exempts the helper. For a main
session, the newest matching gate event wins, ordered by AtUtc then Id. Explicit role events and non-test handover
`opened` events participate; recognized handover values are `worker`, `coordinator`, and `Framework coordinator`.
Other free-form handover roles are not guessed. The handover reader uses the existing Session column as well as
Detail.session; its writer is unchanged. In particular, an opened handover describes the outgoing session, not an
unidentified successor.

Without an event, a start path containing the Framework directory is coordinator; other starts have no role. The first
lookup prefers `CLAUDE_PROJECT_DIR`, then the first main-thread transcript cwd when available, then hook cwd. That origin
is persisted in the session cache and survives later directory changes and refreshes. This names the existing Framework
folder convention; a renamed checkout must set its role explicitly. A `role show` for an unknown session does not invent
an origin from the directory in which someone happens to run the command.

Checks and role are read in one SQL command/round trip and stored together for 30 seconds. There is no database read on
a fresh cached call. Expiry uses the existing 60-second outage-backoff policy. Role set invalidates the local cache after
commit; caller-owned rollback transactions do not change a production cache. Other cache directories can be stale for
30 seconds. During a database outage a cached worker remains worker; without a cache the Framework default still holds.

**Refusal and lift.** The refusal is:

> Coordinator role refuses {rule}: {target}. Delegate this work to a worker. You may write briefs and handovers, run
> git/kor, read files, and check a worker with dotnet build/test or install-kor.ps1. Only Ian's typed
> IAN-APPROVED-COORDINATOR-WORK in this session lifts this boundary for 12 hours; the lift is logged.

Refusals use `oversight.GuardRefusal`, Guard `coordinator-role`. Failure to log a refusal does not turn it into an allow.
The lift requires the exact token on an unfenced, unquoted line of a recorded prompt, corroborated by the same session's
typed/queued CLI human transcript entry. Tool results, peer/meta/sidechain entries, quoted/fenced text, future timestamps,
missing/malformed records and ages of 12 hours or more do not lift. This follows BudgetApproval's conservative whole-line
format. The original prompt recorder retains only 20 prompts capped at 4,000 characters: a token rotated out of that
record stops lifting even within 12 hours. No other guard is lifted.

Each exercised lift inserts a gate event with Ref `role:lift`, Outcome `lifted`, session, approval/expiry times, rule and
target. A lift whose logging fails is not applied. The 12 hours starts at the human prompt, not at the tool call.

**Cases-first red/green.** The A commit builds against the old code: reflection makes the missing policy an assertion
failure rather than a compiler failure. All 87 cases failed before implementation: 86 on the absent policy API, one on
the absent replay verb. This is an absence-of-capability red gate, not a claim that 86 old hook decisions were executed.
All 87 pass after implementation. The later integration tests additionally exercise actual `kor enforce` dispatch and
the database logs. No recorded product command was executed.

The following table lists every named real call. A red/green cell applies to the individually named test; expected/got
are the final replay decisions. `cc`, `fa`, and `b6` abbreviate the three UUID prefixes from the brief.

| Transcript line | Call | Expected | Got | A red / final |
|---|---|---|---|---|
| cc L2574 | Write CODEX coverage brief | ALLOW | ALLOW | fail / pass |
| cc L2578 | Commit brief, review command | ALLOW | ALLOW | fail / pass |
| cc L2664 | kor record | ALLOW | ALLOW | fail / pass |
| cc L2680 | Edit Framework HANDOVER | ALLOW | ALLOW | fail / pass |
| cc L2682 | Commit handover, review into own scratch | ALLOW | ALLOW | fail / pass |
| cc L2741 | Edit README-base | REFUSE | REFUSE | fail / pass |
| cc L2743 | Edit README-base | REFUSE | REFUSE | fail / pass |
| cc L2745 | Edit README-base | REFUSE | REFUSE | fail / pass |
| cc L2747 | Edit README-base | REFUSE | REFUSE | fail / pass |
| cc L2803 | Edit EVIDENCE-README | REFUSE | REFUSE | fail / pass |
| cc L3827 | install-kor.ps1 and version | ALLOW | ALLOW | fail / pass |
| cc L3981 | mkdir into bundle, product run | REFUSE | REFUSE | fail / pass |
| cc L4073 | Write fix_refusal_headers.py | REFUSE | REFUSE | fail / pass |
| cc L4077 | Run that script under work | REFUSE | REFUSE | fail / pass |
| cc L4235 | dotnet test redirected into project file | REFUSE* | REFUSE | fail / pass |
| cc L4251 | Write checker-acceptance.json | REFUSE | REFUSE | fail / pass |
| cc L4270 | Edit build_bundle.py | REFUSE | REFUSE | fail / pass |
| cc L4309 | Run build_bundle.py | REFUSE | REFUSE | fail / pass |
| cc L4326 | Remove bundle cache, run bundle builder | REFUSE | REFUSE | fail / pass |
| cc L4403 | sed -i read_rulings.py, product runs | REFUSE | REFUSE | fail / pass |
| cc L4574 | Write Framework lane brief | ALLOW | ALLOW | fail / pass |
| cc L4578 | Commit lane brief, review, push command | ALLOW | ALLOW | fail / pass |
| fa L1898 | Edit bash guard test | REFUSE | REFUSE | fail / pass |
| fa L1936 | Edit bash guard | REFUSE | REFUSE | fail / pass |
| fa L1938 | Edit bash guard | REFUSE | REFUSE | fail / pass |
| fa L2002 | Edit bash guard | REFUSE | REFUSE | fail / pass |
| fa L2004 | Edit bash guard | REFUSE | REFUSE | fail / pass |
| fa L2006 | Edit bash guard | REFUSE | REFUSE | fail / pass |
| fa L2008 | Edit bash guard test | REFUSE | REFUSE | fail / pass |
| fa L2033 | Edit bash guard | REFUSE | REFUSE | fail / pass |
| b6 L5412 | Worker build and test | ALLOW | ALLOW | fail / pass |

*The exact L4235 command has `> C:/VIsual Studio Projects/KOR.Drafter/work/_acceptance/2248/fixtures-run-g.txt`.
The brief's expected ALLOW conflicts with its explicit refusal of shell writes into project files. The write-target rule
wins here. The same fixture test without that redirect is allowed and tested; fixture writes inside it remain a miss.

The remaining 56 A cases are individually listed below. R = KOR.Drafter, F = Framework, S = this session's scratchpad.
The full inputs, including quoting and slash variants, are in the frozen test source. Every row was red before B and
green in the final run.

| Case | Input/property | Expected | Red / final |
|---|---|---|---|
| D01 | Edit R/src/existing.cs | REFUSE | fail / pass |
| D02 | Write R/work/bundle/manifest.json | REFUSE | fail / pass |
| D03 | MultiEdit R/tests/Test.cs | REFUSE | fail / pass |
| D04 | NotebookEdit R/work/analysis.ipynb | REFUSE | fail / pass |
| D05 | Write F/tools/new.cs | REFUSE | fail / pass |
| D06 | cd quoted R && redirect relative src/new.cs | REFUSE | fail / pass |
| D07 | cd R; tee relative src/new.cs with heredoc | REFUSE | fail / pass |
| D08 | cd R && cat heredoc into relative src/new.cs | REFUSE | fail / pass |
| D09 | /c/ path with spaces, mkdir relative work/bundle | REFUSE | fail / pass |
| D10 | pushd R; rm -f relative work/x.json | REFUSE | fail / pass |
| D11 | cd R; cd F; cd -; sed -i | REFUSE | fail / pass |
| D12 | pushd R; pushd F; popd; touch work/x.json | REFUSE | fail / pass |
| D13 | cp S/x.cs into R/src/x.cs | REFUSE | fail / pass |
| D14 | mv R/src/x.cs into S (source removal) | REFUSE | fail / pass |
| D15 | cd R; cp a.cs b.cs | REFUSE | fail / pass |
| D16 | append to R/data/file.txt | REFUSE | fail / pass |
| D17 | Set-Location -Path R; Set-Content -LiteralPath src/x.cs | REFUSE | fail / pass |
| D18 | PowerShell pushd R; Out-File -Encoding relative path | REFUSE | fail / pass |
| D19 | Out-File -FilePath absolute project path | REFUSE | fail / pass |
| D20 | New-Item -Path R/src -Name x.cs | REFUSE | fail / pass |
| D21 | Copy-Item S/x.cs -Destination R/src/x.cs | REFUSE | fail / pass |
| D22 | Move-Item R/src/x.cs -Destination S/x.cs | REFUSE | fail / pass |
| D23 | Remove-Item -LiteralPath project file | REFUSE | fail / pass |
| D24 | cd R; pwsh -File send-set.ps1 | REFUSE | fail / pass |
| D25 | PowerShell absolute start-on-210.ps1 | REFUSE | fail / pass |
| D26 | takeoff.exe model-audit | REFUSE | fail / pass |
| D27 | dotnet run --project TakeoffCli | REFUSE | fail / pass |
| D28 | python -I R/work/_acceptance/build_bundle_v2.py | REFUSE | fail / pass |
| D29 | cd R/work; python -I fix.py | REFUSE | fail / pass |
| D30 | python -I /c/other/build_bundle_new.py | REFUSE | fail / pass |
| D31 | dotnet test redirected into work/test-log.txt | REFUSE | fail / pass |
| P01 | Write R/docs/codex/CODEX-A.md | ALLOW | fail / pass |
| P02 | Edit F/docs/HANDOVER.md | ALLOW | fail / pass |
| P03 | Write F/lanes/date/brief.txt | ALLOW | fail / pass |
| P04 | Write push-exceptions.txt | ALLOW | fail / pass |
| P05 | Write held-back.cs in own S | ALLOW | fail / pass |
| P06 | git status/diff/add/commit/push sequence | ALLOW | fail / pass |
| P07 | worker build plus fixture test without project redirect | ALLOW | fail / pass |
| P08 | install-kor.ps1; kor --version | ALLOW | fail / pass |
| P09 | kor record with a product-run mention | ALLOW | fail / pass |
| P10 | python -I Framework kor_review.py | ALLOW | fail / pass |
| P11 | Framework board_render.py | ALLOW | fail / pass |
| P12 | Framework guard_log.py | ALLOW | fail / pass |
| P13 | cat bundle script; rg product name | ALLOW | fail / pass |
| P14 | echo quoted sed command | ALLOW | fail / pass |
| P15 | git commit heredoc mentioning rm | ALLOW | fail / pass |
| P16 | Out-File into F/lanes | ALLOW | fail / pass |
| P17 | cd R; cd F; redirect into lanes | ALLOW | fail / pass |
| P18 | /dev/null and stream duplication | ALLOW | fail / pass |
| R01 | explicit worker started in Framework edits project | ALLOW | fail / pass |
| R02 | explicit coordinator started elsewhere edits project | REFUSE | fail / pass |
| R03 | helper in Framework edits project | ALLOW | fail / pass |
| R04 | other-repo main session with no role edits project | ALLOW | fail / pass |
| R05 | Framework origin, current cwd elsewhere, edits project | REFUSE | fail / pass |
| F01 | other session scratch; .md.cs lookalike; lanes/../tools; other repo HANDOVER | REFUSE each | fail / pass |
| F02 | replay CLI emits line/rule/target | REFUSE sample edit | fail / pass |

The 20 post-A integration/follow-up cases are below. “Not run” means no pre-implementation red is claimed for that added
integration case. The direct-entry and shell-wrapper gaps were separately observed red on the initial B implementation
before their fixes; neither caused a previously passing case to regress.

| Case | Red evidence | Final |
|---|---|---|
| role set/show, latest handover, later explicit role, ignore test handover, enforce/subagent/log/rollback | Not run on A | pass |
| typed lift logged with expiry; no refusal row; rollback | Not run on A | pass |
| typed CLI human, age 0 | Not run on A | pass (lift) |
| queued CLI human, age 11.99 hours | Not run on A | pass (lift) |
| typed CLI human, age 12 hours | Not run on A | pass (no lift) |
| future timestamp | Not run on A | pass (no lift) |
| SDK entry | Not run on A | pass (no lift) |
| peer origin | Not run on A | pass (no lift) |
| sidechain entry | Not run on A | pass (no lift) |
| blockquoted token | Not run on A | pass (no lift) |
| quoted token | Not run on A | pass (no lift) |
| fenced token | Not run on A | pass (no lift) |
| prose mentioning token | Not run on A | pass (no lift) |
| tool result, malformed recorder, mismatched recorder | Not run on A | pass (no lift) |
| cache reads/window, persisted origin, outage backoff, invalidation | Not run on A | pass |
| replay duplicates/sidechains/first cwd | Not run on A | pass |
| direct Kor.Operations.EngineeringTools.TakeoffCli.exe | failed before direct-entry fix | pass |
| direct ./build_bundle_v3.py | failed before direct-entry fix | pass |
| shell wrapper writes; installer exemption | failed before wrapper inspection | pass |
| scratchpad root creation; drive-root normalization | Not run on A | pass |

**Replay of the complete transcripts.** Final runs used the Debug executable's `role replay`, sequentially, with output
under `.codex_role/`. Each main-thread tool_use ID is emitted once; nested tool-result copies and sidechains are excluded.
Stored role history is read once and applied by event time. Historical typed prompts reconstruct approval because today's
20-prompt recorder cannot represent the old recorder. Replay executes no tool and inserts no log/event.

| Transcript | Main calls | ALLOW | REFUSE | Stored applicable role events |
|---|---:|---:|---:|---:|
| cc3b7be3-1a3f-4b77-a674-537c582d930a | 341 | 278 | 63 | 0 |
| fa93ad77-7578-4f8b-a60b-b8ad6945c7b2 | 434 | 375 | 59 | 0 |
| b6568d08-9e05-4227-9081-c24680ae497d | 607 | 483 | 124 | 0 |

All three start in Framework. All 31 named calls matched the policy table. The other replay decisions are an inventory,
not an independently labelled accuracy claim. Shell wrapper inspection reads the script currently on disk, not a
historical blob. Replay cannot reconstruct historical cache staleness or arbitrary program effects.

**Build/test evidence.** Commands below were run from this worktree; the full solution was never built or tested.

```powershell
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --filter FullyQualifiedName~CoordinatorRole
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --logger 'trx;LogFileName=final.trx' --results-directory .codex_role/final
& ./Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.exe testdiff .codex_role/baseline/baseline.trx .codex_role/final/final.trx
dotnet build Kor.Operations.Framework.Cli/Kor.Operations.Framework.Cli.csproj -c Release -nologo
```

| Proof | Passed | Failed | Total | Result |
|---|---:|---:|---:|---|
| A cases against unchanged code | 0 | 87 | 87 | red, compiled successfully |
| Final targeted tests | 107 | 0 | 107 | green, 963 ms test duration |
| Baseline full Framework suite, code at 206615f3 | 1,527 | 45 | 1,572 | 2 m 16 s |
| Final full Framework suite, run once after final changes | 1,634 | 45 | 1,679 | 2 m 8 s |
| Release CLI builds before/after | — | 0 | — | both zero warnings/errors |

`kor testdiff` exited 0: **NO NEW FAILURES**. It compared 1,566 baseline and 1,673 final distinct display names;
42 failing names occur in both, zero only in new, zero baseline tests missing, and 107 new tests with zero failures.
The 45 failed result rows correspond to 42 distinct names because some parameterized display names collide.
No blame sequences were supplied: testdiff explicitly says hung cases are NOT MEASURED; both runs completed normally.

Unchanged failures: EnrollCheckTests (40 result rows); BrainRecallTests.TheHookBringsAWorkedJobsNamingRowsAndTheStandingAsksAsOneLine;
FileMemoryLiveTests.LiveEveryImportedMemoryIsOnTheMemoryPartOnceAndGoesStaleWhenItsFileChanges;
StartPagesLiveTests.TheLanesWholeMemoryIsOnThePagesAndWithinTheWiring (KOR.Drafter and Framework rows);
StartSplitLiveTests.LiveTheSidePartsPrintSessionStartJsonWithinBudgetAndWriteNothing. The exact named comparison is in
`.codex_role/testdiff.txt`, with the two TRX files under `.codex_role/baseline/` and `.codex_role/final/`.

All new database writes went through `BrainDb.OpenRolledBack`; tests check zero rows after disposal. After the final suite,
an independent read counted Session LIKE `zz-role-%`: **brain.Event 0; oversight.GuardRefusal 0**. No live role was set,
and no persistent database write was intentionally made. Existing full-suite tests retain their own rollback conventions.

**Enforce timing, Release.** Same PowerShell process-launch/stdin harness, Framework-origin main session, `git status
--short`, real check cache, one warm-up then 40 sequential calls. No refusal/product operation was invoked. TEMP/TMP stayed
normal. The final Release assembly was read as UTF-16 and verified to contain the new refusal string.

| Measurement | Before | After |
|---|---:|---:|
| median | 140.4033 ms | 152.4599 ms |
| mean | 141.6023 ms | 154.5996 ms |
| minimum | 127.1738 ms | 135.5414 ms |
| maximum | 182.2982 ms | 210.2127 ms |

Median increase: 12.0566 ms (8.6%). These include process startup and differ from the brief's earlier live 110–117 ms
measurement. Raw samples are `.codex_role/timing-before.json` and `timing-after.json`. The cache test proves one read for
20 calls inside the freshness window, then one read at expiry. Each real refresh is one SQL command returning both result
sets, so role lookup adds no per-call database round trip. Refresh latency and approval scans are not represented by this
warm-cache timing.

**Rule 11: class, coverage and limits.** The class is a coordinator doing literal project edits or named product runs
despite its role. The harness compares tool decisions across actual transcripts, synthetic shell/path forms, roles and
subagent boundaries, then tests storage, approval, hook JSON/logging, cache and rollback. It does not establish whether a
test is checking a worker's branch, whether an installation is appropriate, or whether the result of a permitted program
will modify project data. Coverage is explicitly documented in both the guard and tests.

Covered: Write/Edit/MultiEdit/NotebookEdit; relative and absolute Windows and /c/ paths; separators and quotes; cd,
Set-Location -Path, pushd/popd, cd -; redirection/heredoc targets, common write/copy/move/delete commands, source removal
on moves, sed -i, named product scripts and direct executables; literal shell wrappers within the existing parser limits.
Git commands and the named installer remain allowed. Any later forbidden segment or external project redirect is still
judged. Unresolvable write targets are conservatively refused. Writes outside the permitted paths also include other
temporary locations: use the session's own scratchpad.

Not covered: arbitrary Python/node/program side effects, runtime-built commands/aliases, filesystem junctions/symlinks,
shell syntax outside the existing segment model, scripts outside its depth/file/size limits, unenrolled tools, forged
prompt/transcript/cache records, or a renamed Framework checkout without an explicit role. Shell scripts are inspected
from current disk; installer internals are intentionally exempt. Permissions are not an OS sandbox. A session can set
itself worker, as the requested verb permits, but that transition is a visible event. Unknown non-worker/coordinator
handover labels do not establish a role. No new guard settings were installed to make this build live.

Concrete same-class misses: `dotnet test` can regenerate project fixtures; `python -c` can open and overwrite a project
file without a named write target; a product executable held in a runtime variable can evade product-name recognition.
The no-redirect form of L4235 is specifically an allowed fixture-rerun miss. Cache staleness can temporarily retain worker
authority after a role change, and an outage can extend that until a successful refresh. The strict semantic line therefore
cannot be fully held by this hook alone; this response does not claim otherwise.

**Corrections to the brief.**

- L4235 includes the forbidden project redirect, so its proposed ALLOW is inconsistent with the target-write rule.
- L4251 is Write, not Edit. L2744 and L2746 are tool-result lines, not additional calls. In fa93ad77, L2010 contains nested
  copies of earlier tool calls without a cwd; replay correctly uses the original assistant calls, not those duplicates.
- The actual opened handover Detail lacks `session`; the existing Session column supplies it. Free-form role strings
  exist, so only the explicit coordinator/worker meanings above are recognized, rather than guessing every lead name.
- Memory 134054 documents the one-segment/pipe limitation of brain.Check, not a role-state experiment. Its broader
  relevance follows from inspecting Rules and the absence of session state; the quoted characterization was imprecise.
- This worktree started at the brief-only child of 206615f3. The baseline was code-identical, as checked above.
- The baseline Framework suite is not green on this machine: 45 existing failed result rows. No unrelated tests or
  guard/settings files were changed to make that number disappear.

**Process scorecard and review.** One session, no delegated agents, seven targeted test runs, six transcript replays
(three repeated after the direct-entry/wrapper refinements), two complete Framework runs including the baseline, two
Release CLI builds, zero product runs, zero installs/pushes. The checks found the initial rm -f argument-consumption bug
and the two direct-entry plus shell-wrapper gaps; zero user-reported implementation faults. Token/dollar accounting is
not available here. The main cost was establishing real cases and the two complete suite runs. The useful process change
is to test direct execution and wrapper execution alongside interpreter forms. No board/review writes were made because
this brief prohibits live database writes; the coordinator's kor_review/pre-push review remains before any push.

The response is the final commit. Local verification artifacts remain in the ignored `.codex_role/` directory.
