# Step 3, six guards, round 3 — response

22 November 2026. Framework fix **c24e257** is committed locally. The guard's final suite passes **250/251** checks: all **169 new checks** pass; pre-existing case 22 still fails. The same **29 historical plan_review rows refuse before and after**, with zero decision changes. This response is committed last in Operations. No push, installation, database write or other guard change was performed.

Starting heads were Framework **f1aa076** and Operations **0c316314**; both worktrees were clean. I read the brief, repository guidance, the earlier responses, the guard, its tests, the shared command splitter and logging gate. Searches for `plan_review`, `oq_board_files`, replay files and the earlier narrow-six artifacts in `tools/`, `docs/codex/` and `obj/step3-narrow6*` found the existing suite and frozen replay inputs. I extended those tests and reused round two's replay method; no new transcript population or database evidence was collected.

## Cases first, on the stated base

Framework tests-only commits, preceding the production commit:

| Commit | Cases / purpose | Observed against f1aa076 |
|---|---|---|
| **6b9f514** | Wrapper matrix, directory/read failures, reasons, bold verdicts; 151 added checks | 45 new failures; suite 187/233 |
| **1857b4d** | Whole-argument quote boundary, wrapper directory uncertainty, PowerShell; 14 more checks | 54 new failures cumulatively; suite 192/247 |
| **31224ac** | Every wrapper mention names the existing unreviewed SQL file; corrected nested shell fixture quoting | Same 54 new failures; suite 192/247 |
| **e7ddb26** | Command string ending before its external `-i` arguments; four more checks | 56 new failures cumulatively; suite 194/251 |

The first red run used the unchanged worktree guard. Later red runs used byte-for-byte `git show f1aa076:tools/plan_review_guard.py` and its splitter/logger dependencies, with the new test file and existing review fixtures copied into `obj/step3-narrow6-r3/base` in Operations. The final copied test file was checked against the committed test bytes. Every new execution/mention case calls the guard's real `--hook` subprocess; verdict cases also check the parser directly. No supplied shell command is executed. A nonzero hook exit or stderr now fails the test instead of being mistaken for an allow.

The wrapper matrix has **18 forms × 5 checks = 90 checks**: an unreviewed INSERT, reviewed GO INSERT, SELECT, whole quoted echo mention, and Python `-c` print mention. All 36 mention controls pass on the base and the fix, as do all 36 reviewed/SELECT controls. Required forms:

| Form, with an unreviewed board INSERT | f1aa076 | c24e257 | Echo / Python mention controls, both versions |
|---|---|---|---|
| `env PYTHONIOENCODING=utf-8 python -I oq.py -i ...` | allow | deny | allow / allow |
| `timeout 60 python -I oq.py -i ...` | allow | deny | allow / allow |
| `nice -n 5 python -I oq.py -i ...` | allow | deny | allow / allow |
| `command python -I oq.py -i ...` | allow | deny | allow / allow |
| `& python -I oq.py -i ...` | **deny already** | deny | allow / allow |
| `cmd /c python -I oq.py -i ...` | allow | deny | allow / allow |

The other forms are exec, sudo, uv run, stacked wrappers, an unfamiliar wrapper, python3 piped to tail, full executable/script paths, quoted pwsh/cmd/bash commands, an unfamiliar quoted wrapper, and nested shell commands. Three of the 18 unreviewed cases already refused: `&`, python3 with a pipe, and full paths. They remain controls, not claimed repairs.

Final added-check accounting:

| Group | Checks | New failures on f1aa076 | Passing after |
|---|---:|---:|---:|
| Wrapper matrix | 90 | 15 | 90 |
| Mention/input boundaries, repeated `-i`, multiple commands, partial quoted command | 16 | 8 | 16 |
| Bare/previous/variable directory changes, pushd/popd, absolute-path controls | 30 | 14 | 30 |
| Directory recovery, unreadable/missing inputs, wrapper cwd uncertainty | 19 | 11 | 19 |
| PowerShell execution/mention pair | 2 | 1 | 2 |
| Actionable refusal reasons | 2 | 2 | 2 |
| Bold-closed verdicts, precedence and prose controls | 10 | 5 | 10 |
| **Total added** | **169** | **56** | **169** |

## Change

`oq_board_files` scans literal `oq.py` leaf tokens followed by `-i` throughout each segment. Detection no longer depends on an executable-position whitelist. A local word scanner preserves quote boundaries and Windows paths, while the existing shared segment splitter stays unchanged. Executable quoted command arguments are scanned recursively, including a command string whose `-i` arguments follow outside its quotes.

Known non-executing argument positions preserve whole quoted echo/printf/cat/Write-Output/Write-Host/Get-Content/type and Python `-c` mentions. Unknown wrappers do not exempt a candidate. Recognizing cwd-preserving prefixes is used only to permit relative-path resolution; an unfamiliar prefix or directory-changing option cannot certify a file in the old cwd. An absolute input remains checkable.

Bare `cd`, `cd -`, ambiguous location arguments and an unavailable directory stack now invalidate the tracked directory. An explicit absolute location can restore it; relative changes cannot. `pushd`/`popd` preserve known or unknown saved state. Nested command directory state does not leak into the parent.

Unresolved paths, missing `-i` arguments and file-open failures raise an explicit input-check refusal. They no longer silently disappear. The session is told either:

- The working directory or path expression is unknown: use a literal absolute input path, or first change to an explicit absolute working directory.
- The guard cannot read the named input path: make the file readable and retry with an explicit path; its contents must be checked before execution.

An unreadable file is refused even if it might contain SELECT-only SQL: its contents cannot establish that allowance. Tests also put a reviewed file in the old cwd to prove that unknown directory state cannot borrow its GO.

The small verdict repair accepts `**VERDICT: CHANGE** - reason` and its GO counterpart. Anchoring and last-verdict precedence remain. A final bold-closed CHANGE now overrides an earlier GO; prose and quotation still do not certify a review. CHECKS / ALLOWS / DOES NOT CATCH were updated in the guard header.

## Suite and historical replay

Commands actually run, from the respective worktrees:

```powershell
# Framework
python -I tools/plan_review_guard_test.py

# Operations: frozen f1aa076 guard with the final tests
python -I obj/step3-narrow6-r3/base/plan_review_guard_test.py
python -I obj/step3-narrow6-r3/replay.py
```

| Run | Passed | Failed | Total |
|---|---:|---:|---:|
| Unedited f1aa076 suite | 81 | 1 | 82 |
| Final tests against f1aa076 | 194 | 57 | 251 |
| Final tests against c24e257 | 250 | 1 | 251 |

Suite commands exit 1; case 22 is the sole failure in the unedited-base and final runs. Its bounded prior-art search omits the guard filename from its first twelve reported Framework matches; it failed before any edit. No other old case fails. The replay exits 0. Only Python code and documentation changed; no .NET project or solution was built/tested.

Replay uses the original 29 recovered actions and recorded negative review state (CHANGE for 8442, absent otherwise). SQL inputs 7749, 8442, 8980 and 8989 use the frozen round-one file copies, verified against their source hashes. Only input paths are relocated. Every yielded text hash must match the historical replay's expected hash. No historical command or reviewer runs, and today's mutable review store is not used.

The recursive scanner can encounter a quoted script's input twice. The replay records match counts and validates every emitted hash; 8442 and 8989 have counts 1 before / 2 after, with identical contents. An initial replay assertion requiring exactly one match was too restrictive and was replaced by the content-hash invariant. The final decision comparison below includes every row, not the partial run preceding that assertion.

| Row | Before | After |
|---:|---|---|
| 7452 | deny | deny |
| 7465 | deny | deny |
| 7466 | deny | deny |
| 7481 | deny | deny |
| 7483 | deny | deny |
| 7555 | deny | deny |
| 7575 | deny | deny |
| 7576 | deny | deny |
| 7577 | deny | deny |
| 7582 | deny | deny |
| 7595 | deny | deny |
| 7666 | deny | deny |
| 7749 | deny | deny |
| 7984 | deny | deny |
| 7986 | deny | deny |
| 7989 | deny | deny |
| 8208 | deny | deny |
| 8209 | deny | deny |
| 8210 | deny | deny |
| 8442 | deny | deny |
| 8611 | deny | deny |
| 8946 | deny | deny |
| 8947 | deny | deny |
| 8948 | deny | deny |
| 8949 | deny | deny |
| 8950 | deny | deny |
| 8951 | deny | deny |
| 8980 | deny | deny |
| 8989 | deny | deny |

Local evidence is under Operations `obj/step3-narrow6-r3`: `base.log`, `cases-red.log`, `expanded-red.log`, `final-red.log`, `final-green.log`, `replay.py`, `replay.log`, `replay.json`, and `regression-probes.json`. These are ignored working evidence, not additional response files.

## Rule 11 and limits

**Invocation/input class:** a literal board-input candidate must not become an allow merely because a prefix, quote boundary or unknown file location prevents inspection. The known-answer matrix checks these together before the fix; the historical differential checks decisions and input hashes on the frozen population. It covers the named wrappers, quoted/partial command arguments, mention boundaries, every input flag, directory tracking, read failures and corrective messages. It does not execute shell syntax, evaluate substitutions/functions/Python code, expand variables, follow dynamic SQL, or model arbitrary startup scripts. Same-class miss: a shell function or Python source constructs the executable name and invokes oq without a literal candidate in the hook payload. Unfamiliar data-only programs can incur conservative false refusals. A file changed after preflight is not protected by this check.

**Verdict class:** standalone formatting must preserve the final decision without converting prose into authority. The tests cover bold closed before the explanation, both outcomes, final CHANGE after GO, and prose/quotation controls. They do not authenticate the reviewer or assess review quality. Same-class miss: someone writes their own standalone GO into the review store. The historical differential cannot expose a fault shared by both versions; the new known-answer cases are the first gate.

## Verified claims and corrections

- The reported env regression is reproduced through `--hook`: live **deny**, f1aa076 **allow**, candidate **deny**. The `cd ... && PYTHONUTF8=1 py -3 oq.py -i ...` probe is live **allow**, f1aa076 **deny**, candidate **deny**.
- Framework main was **91fda24** when inspected, not 5a4641f. Its plan-review source still matches **7a3b0a8** after line-ending normalization, so the brief's code comparison is valid.
- `& python ...` is not a failing base case: the shared segment splitter already removes `&`. It is included with passing mention controls, but cannot honestly be shown red on f1aa076.
- Bare `cd` does leave the old tracked directory unchanged. `cd -` instead appends a literal `-` directory on the base. Both can result in a wrong-path lookup and false allow; the latter mechanism was misstated.
- Ignoring a bold-closed CHANGE is not always merely a fail-safe false refusal: after an earlier GO it causes a false allow. The new precedence check reproduces that failure.
- The private review rows 3664–3670 and additional held-back cases were not queried or claimed verified. The supplied faults were checked directly in code and offline cases.

Every hook payload omits `hook_event_name`; inspection of `guard_log.record` confirms it returns before database access in that case. TEMP/TMP remained `C:\Users\ilalonde\AppData\Local\Temp`. No database connection was needed. Commits use `git -c core.hooksPath=obj/step3-narrow6-r3/empty-hooks commit` with empty directories; shared hooks and settings were not edited. Final committed scope is Framework `tools/plan_review_guard.py` and `tools/plan_review_guard_test.py`, plus this Operations response. Both worktrees pass `git diff --check`.
