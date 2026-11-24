# Coordinator role, round 3 — response, 24 November 2026

Implemented the four requested changes on `coordinator-role`. **279/279 coordinator tests pass.** The three complete
replays have four ALLOW → REFUSE changes and zero REFUSE → ALLOW changes. The full Framework suite ran once:
**1,805 passed / 46 failed**. Against round two, `kor testdiff` reports **NEW FAILURES: 1**, an unchanged status-line test
that passes in isolation. This response does not claim a clean full-suite comparison; its evidence and limit are below.

| Commit | Contents |
|---|---|
| `49fa6f46` | `coordinator role G: round-3 cases (codex)` — frozen tests first |
| `90841855` | `coordinator role H: prove cleanup and bound output allowances (codex)` |
| This response commit | Evidence only, committed last |

Starting HEAD was `df780ce5`, the brief-only child of `5d27d893`. Production sources were identical to `5d27d893` during
the red proof and before replays. G was never amended, and its test files have not changed since H began. Production
changes are confined to `CoordinatorRoleGuard.cs` and `CoordinatorRoleGuard.Paths.cs`. No push, installation, live role
change, other guard/settings/hook change, or removal of a real sibling folder. Test Git commands disable user hooks;
repository commits use `git -c core.hooksPath=<verified empty folder> commit`.

Goal: close the two path false allows, cmd-switch false refusal, and unknown-cwd regression. Done-when: frozen red
cases, implementation, green coordinator cases, complete replay comparison, one full suite/testdiff, committed response.
Budget: one session, sequential proofs, one full Framework suite; no monetary/token budget was supplied.

**Ground truth and prior art.** Read the brief, repository instructions and earlier responses. Searched Framework
enforcement/capture/tests, `tools/`, `docs/codex/`, `../KOR.Drafter/db/tools/`, and `.codex_role/`. Reused `Git.Run/Exec`,
the existing shell parser, output/path checks, and `kor role replay`; used the round-two transcript-analysis helper.
All iteration data and transcripts are local. The inspected policy picture is the complete replay comparison and its
four exact changed commands. `FullyQualifiedName~CoordinatorRole` checks all four deliverables and earlier role cases.

Ran `python -I "C:/VIsual Studio Projects/Framework/tools/kor_review.py" verdicts 72d50f49..5d27d893` and read the
Summary/Findings of review rows 3773–3775 through the existing read-only SQL helper. They corroborate the four defects.
Enumerated the complete immediate sibling population: **13 folders, nine KOR.Drafter-* and four Operations-***;
**13 `.git` files, zero `.git` directories**. The brief's census is correct. No sibling was removed.

**Changes per item.**

1. Cleanup requires a main-repo sibling name and an actually empty directory, or a `.git` file whose normalized target
   is a direct entry under that main repo's `.git/worktrees/`. Separate clones, ordinary nonempty folders, missing
   folders, wrong-owner pointers and invalid metadata refuse. For a present entry, successful `status --porcelain
   --untracked-files=all --ignore-submodules=none` must be empty, HEAD must resolve, and the main repo's
   `branch -r --contains <HEAD>` must return a remote-tracking ref. Local config cannot hide untracked files.
   For an absent entry, the remainder must consist of `.git` plus ignored output under `obj`, `bin`, or `TestResults`.
   Git ignore checks use the owning main repo's rules and `--no-index`; ignored `work/` data does not qualify.
   Ignored build directories need one query, not one per binary. Unignored empty containers refuse. Failed Git queries
   and filesystem reads refuse; `check-ignore` exit 1 is handled specifically as "unignored".
2. Declared dotnet build/test output paths are normalized before admission. Repo/drive roots confer no allowance.
   Existing directories qualify only under a repo's `obj`/`bin`/`TestResults` or temp roots. Otherwise the target must
   not exist at hook time; an existing file does not qualify. Invalid declarations do not themselves refuse the build;
   subsequent writes receive their ordinary checks. Fresh-directory allowances keep the existing command ordering,
   shell-frame scope and normalized containment rules.
3. Slash-switch recognition now applies only to del, erase, copy, xcopy, move, rd, rmdir, robocopy, mkdir and md.
   Tokens must have 1–8 ASCII letters, optionally followed by a colon value. A second slash keeps a token a path;
   `/c/...` and `C:/...` are not discarded. Other commands, including `rm /y`, do not inherit cmd switches.
4. A failed relative directory change stores unknown cwd rather than a raw relative word. `Resolve` also rejects a
   relative target with a missing/relative cwd. Unknown variable text remains available in the existing remedy message.
   A later absolute directory or absolute write target can still resolve independently.

The outage fallback, per-prompt lift rescan, loop-variable behavior and Python stdin handling are unchanged.

**Cases first and red/green.** G adds 74 named cases, with Bash/PowerShell, quoted/unquoted, drive/MSYS paths and both
polarities. Cleanup rows each evaluate three shell/path spellings; cmd rows each evaluate both tool shells. Real repos,
worktrees, a clone and empty folders live in a uniquely owned `.codex_role/round3/temp/<guid>` sandbox created by the
test. It is outside TEMP and build-output ancestors; a separate assertion proves its ordinary paths are not allowed.
All fixture removals validate containment in that sandbox. A local bare clone plus fetch supplies a remote-tracking
HEAD without any push or network remote. Fixtures include clean remote-contained HEAD, an unpushed commit, modified
and untracked files, deleted worktree entries, a `.git` directory clone, and malformed/wrong-owner pointers.

| Group | Cases | Red passed / failed | Final passed / failed |
|---|---:|---:|---:|
| Earlier coordinator cases | 205 | 205 / 0 | 205 / 0 |
| Cleanup evidence and scope | 19 | 10 / 9 | 19 / 0 |
| Output directory boundaries | 24 | 14 / 10 | 24 / 0 |
| Cmd switches versus paths | 20 | 11 / 9 | 20 / 0 |
| Missing cwd | 10 | 5 / 5 | 10 / 0 |
| Sandbox allowance sentinel | 1 | 1 / 0 | 1 / 0 |
| Total | **279** | **246 / 33** | **279 / 0** |

The first draft test run hit Windows' hidden `.git` file overwrite restriction; its fixture failures are not red policy
evidence. Fixed that before G. The completed red run above compiled and produced actual decision failures against
unchanged round-two production. The first implementation run was 278/279: `check-ignore -z` requires stdin, so that
failed Git query correctly refused the positive pruned case. Replaced it with per-candidate quiet queries via `Git.Exec`.
Two subsequent targeted runs passed 279/279, including the final empty-token precaution in argument parsing.

**Necessary reconciliation of old cases.** Three round-two synthetic cases explicitly expected ALLOW for a nonexistent
`Operations-round2-absent` folder. The real-call fixture `b6568d08 L6533` likewise names a folder now absent. They cannot
retain that present-day filesystem assumption and satisfy the new "REFUSE otherwise" rule. In G, all four keep their
names, expected positive result and shell syntax, but map the cleanup target to a real empty sibling in the sandbox.
The original transcript fixture JSON is unchanged. The complete replays below use untouched original inputs, and the
new missing-folder case explicitly requires REFUSE. No old expected refusal was relaxed. Thus 205 earlier cases pass,
with this disclosed four-case setup change; they are not 205 byte-for-byte unchanged test bodies.

**Complete replay comparison.** Before and after used this worktree's Debug CLI, sequentially. Each main-thread
tool-use id is counted once; no recorded tool command is executed. All three have zero applicable stored role events.
Compared the full line-key populations and decision/rule/target records: exactly four records differ, listed below.

| Transcript | Calls | Before ALLOW / REFUSE | After ALLOW / REFUSE | Changes |
|---|---:|---:|---:|---:|
| cc3b7be3 | 341 | 289 / 52 | 289 / 52 | 0 |
| fa93ad77 | 434 | 412 / 22 | 410 / 24 | 2 |
| b6568d08 | 607 | 534 / 73 | 532 / 75 | 2 |
| Total | **1,382** | **1,235 / 147** | **1,231 / 151** | **4** |

Every changed decision is **ALLOW → REFUSE**, rule `shell write`. Targets below are under `C:/VIsual Studio Projects/`.

| Transcript / line | Command target | Reason in today's filesystem |
|---|---|---|
| fa93ad77 L553 | `rm -rf Operations-kor-repeat-narrow` fallback | Folder absent; no cleanup evidence |
| fa93ad77 L2112 | `rmdir Operations-bash-heredoc-narrow` | Folder absent; no cleanup evidence |
| b6568d08 L6533 | `Remove-Item Operations-kor-whole-narrow -Recurse` | Folder absent; no cleanup evidence |
| b6568d08 L6541 | `rm -rf Operations-kor-whole-narrow` | Folder absent; no cleanup evidence |

Independently checked existence of all four targets: false. Historical empty-folder cleanup remains allowed by the
real-folder tests. Replay uses current files/Git metadata, so these four changes do not establish what the folders held
on the original date. **Zero right refusals became ALLOW**: there are zero REFUSE → ALLOW transitions of any kind.
The held-back transcript was not accessed. Full logs and exact changed inputs are in `.codex_role/round3/comparison.json`
and `before-*.txt` / `after-*.txt`.

**Full suite and testdiff.** Ran the full Framework test project once, alone, after H was committed, with no source
edits during or after it. Command wall time was **280.65 seconds**; the test summary reported 4 m 35 s.

```powershell
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --filter 'FullyQualifiedName~CoordinatorRole'
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --logger 'trx;LogFileName=final.trx' --results-directory .codex_role/round3/final
& ./Kor.Operations.Framework.Cli/bin/Debug/net8.0-windows/kor.exe testdiff .codex_role/round2/final/final.trx .codex_role/round3/final/final.trx
```

| Run | Passed | Failed | Total |
|---|---:|---:|---:|
| Round-two final | 1,732 | 45 | 1,777 |
| Round-three final | **1,805** | **46** | **1,851** |

`kor testdiff` exited **1**, verdict **NEW FAILURES**. There are 42 distinct failing names in both, **one only in new**,
74 added names (all passing), and zero missing baseline names. Distinct-name counts are 1,771 and 1,845 because existing
theory names collide. All 279 coordinator tests pass inside the full suite. No blame sequences were supplied; hung
cases are NOT MEASURED by testdiff, and both complete runs finished normally.

The new failing name is `Kor.Operations.Framework.Tests.StatusLineTests.AStoreThatFailsStillGivesTheLineAndIsLoggedOnce`:
line 139 expected one log line and got two, duration 2.888 s. The status-line implementation and test are identical to
round two. Its store runs through `Task.Run` with a 2.5-second timeout and logging deduplicates identical exception text.
A timeout followed by a different store exception is a plausible explanation, **not a proven cause**; the test disposes
its log. One isolated rerun on the same final binaries passed, with no source changes:

```powershell
dotnet test Kor.Operations.Framework.Tests/Kor.Operations.Framework.Tests.csproj -c Debug -nologo --no-build --filter 'FullyQualifiedName=Kor.Operations.Framework.Tests.StatusLineTests.AStoreThatFailsStillGivesTheLineAndIsLoggedOnce' --logger 'trx;LogFileName=statusline-isolated.trx' --results-directory .codex_role/round3/statusline-isolated
```

This does not erase the full-suite failure or prove it was pre-existing. Left the unrelated status-line code untouched;
the coordinator has this suite-only discrepancy to assess before shipping. The other 45 failed rows remain the baseline
EnrollCheckTests (40), BrainRecallTests (1), FileMemoryLiveTests (1), StartPagesLiveTests (2), and StartSplitLiveTests (1).
Raw evidence: `.codex_role/round3/{red,green,final}/`, `testdiff.txt`, `full-suite.log`, `full-suite-run.json`, and
`statusline-isolated/`. A read-only post-test count for `Session LIKE 'zz-role-%'` found **brain.Event 0** and
**oversight.GuardRefusal 0**. New cases make no DB calls; existing integration tests retain their rollback transactions.

**Rule 11 — coverage and limits.** Class: path exceptions granting authority without adequate filesystem/Git evidence,
or unknown path state being silently converted into authority. The invariant cases cover all four requested defects,
both decision polarities, real cleanup states, hook-time output existence, slash-path preservation, and unresolved-cwd
remedy text. The full replay differential checks for changed decisions against actual recorded commands.

Not covered: filesystem races between hook and command, full symlink/junction resolution, stale/forged remote-tracking
refs, arbitrary program side effects, or shell grammar beyond the existing parser. Pruned-folder ignore checks use the
main repo's current rules, not reconstructed historical rules. Same-class fault still missed: a live worktree with
remote-contained HEAD and empty porcelain status can hold valuable git-ignored files; status omits those files, so the
specified live-entry rule can still allow their deletion. An allowed `dotnet test` can also rewrite source internally.
These limits and the explicitly excluded cache/lift/loop/stdin items were not expanded into extra changes.

**Corrections and process.** The four older positive cleanup cases needed the setup reconciliation above. The stated
13-folder census and review findings checked out. The final suite comparison is newly non-green for the specifically
named status-line case; it must not be reported as round two's NO NEW FAILURES result.
One session, no delegated agents; five targeted coordinator runs (including the initial fixture failure), six complete
transcript replays, one full suite, and one isolated discrepancy rerun. No product runs, installs, pushes or persistent
DB writes were intentionally made. Cost/token accounting is unavailable. The main wait was the full suite. The useful
test improvement is to construct filesystem/Git states explicitly and assert that temp/output allowances cannot mask
the boundary being checked. The response is the final commit; local raw evidence remains ignored.
