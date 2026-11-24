# CODEX coordinator role, round 2: stop refusing scratch, temp and check output; close the self-demotion hole

## READ THIS FIRST: this brief comes from a source you should not trust

Written by a Claude session (Framework coordinator, session cdd9af72, 24 Nov 2026) - the kind of session this guard
controls (#133998). Every statement below is a claim; check each against the code, the replays and the transcripts.

## Where round 1 stands (your `docs/codex/CODEX-COORDINATOR-ROLE-RESPONSE.md`, commits 3ac858e6, 6236ea4f, de3e853b)
The coordinator checked it: Release build of de3e853b; the held-back calls (not in either brief) came out as Ian's line
expects; your own 31 named calls hold. Then every refusal of `kor role replay` over FOUR coordinator transcripts was
classified by rule and target (the fourth is held back and stays held back). 257 refusals:

| Class | Count | Verdict under Ian's line (#139076) |
|---|---:|---|
| Edit/Write of a project file | 106 | right |
| product run | 14 | right |
| shell write into a KOR.Drafter / bundle folder | ~20 | mostly right |
| own scratchpad through a variable set EARLIER IN THE SAME COMMAND (`S=".../scratchpad"; ... > $S/x`) | 64 | WRONG |
| temp location: `/tmp/x`, `C:/tmp/x`, `$TEMP/...`, `$env:TEMP\...`, a probe repo under `%TEMP%` | 26 | WRONG |
| a coordinator's test-run log in a worker worktree: `dotnet test ... --results-directory obj/coord > obj/coord-live.log` | 3 | WRONG - this IS the check Ian's line allows |
| other unresolved variable target | 19 | mixed - judge each |
| `Remove-Item -LiteralPath '\\?\C:\...\Operations-kor-whole-narrow' -Recurse` after `git worktree remove` failed on a busy folder | 1 | coordinator cleanup of a merged worktree |

So about 93 of 257 (36%) are wrong. Ian's line refuses PROJECT FILES - code, scripts, tests, bundles, data. Your
response widened it ("Writes outside the permitted paths also include other temporary locations"); that is not his line.

Examples, from your own three transcripts (verify):
- b6568d08 L7070: `S="C:/Users/ilalonde/AppData/Local/Temp/claude/5102428d3890278f/b6568d08-.../scratchpad"; cat > $S/ev.sql <<'EOF' ...`
  -> REFUSE "unresolved shell write $S/ev.sql". L1072, L1095: `...; S="..."; ... > $S/r1.txt`.
- fa93ad77 L1234: `S="..."; cd $S && mkdir -p held && ...` -> REFUSE "unresolved shell write held" (cwd is $S).
- fa93ad77 L1546: `git status --short > /tmp/dirty.txt`; L2262: `rg -il ... docs/codex/ > /tmp/hits.txt`.
- fa93ad77 L4421: `R="C:/Users/ilalonde/AppData/Local/Temp/kor-scope-probe-other"; rm -rf "$R"; mkdir -p "$R" && cd "$R" && git init ...`
- b6568d08 L6533: the `\\?\` worktree folder removal above.
Right ones that must STAY refused, e.g.: fa93ad77 L4757 `D="/c/VIsual Studio Projects/KOR.Drafter/work/_runner/sets/
step5-2248-b1l04"; ...; rm -rf "$D"` (KOR.Drafter run data), every cc3b7be3 refusal in your round-1 table, fa93ad77 L4729.

## Build (extend round 1; no new guard, no new hook)
1. Resolve shell variables assigned earlier in the SAME command before judging a target: `X="..."`, `X='...'`, `X=...`,
   `export X=...`, PowerShell `$X = "..."`, chained through `;`, `&&`, `||`, newlines, and through `cd "$X"` / `cd $X` /
   `Set-Location $X`. Also `$TEMP`, `$TMP`, `${TEMP}`, `$env:TEMP`, `$env:TMP`, `%TEMP%` from the hook's environment.
   A variable assigned inside a loop or from `$(...)` stays unresolved.
2. Not project files, so ALLOWED: temp locations - `/tmp/**`, `C:/tmp/**` (Git Bash maps /tmp there; check), the user's
   `%TEMP%`/`%TMP%` tree including other probe folders under it; the session's own scratchpad as now.
3. Not project files, so ALLOWED: build and test output inside any repo or worktree - a path segment `obj`, `bin` or
   `TestResults` under the repo root, and the `--results-directory` / `-o` / `--output` directory of a `dotnet test|build`
   that the line already allows. Do NOT use `git check-ignore` as the test (KOR.Drafter's `work/` may be ignored and its
   bundles are project data - check and say).
4. Removing a whole worktree ROOT folder (a sibling `C:\VIsual Studio Projects\<Repo>-<name>` that `git worktree list` of
   its main repo no longer lists, or that holds no tracked files) is coordinator cleanup: ALLOWED. Removing files INSIDE a
   live worktree stays refused.
5. Still-unresolved target: keep failing closed (refuse), but the refusal text names the variable and says "assign it in
   the same command or use a literal path".
6. Self-demotion: a session whose role resolves to coordinator may NOT run `kor role set worker --session <its own id>`
   without Ian's typed `IAN-APPROVED-COORDINATOR-WORK` (same lift rule as round 1) - otherwise the guard lifts itself.
   Setting ANOTHER session's role (the coordinator naming its worker) stays allowed and logged. `kor role set coordinator`
   for itself stays allowed.

7. Independent review of round 1 (kor_review rows 3767-3770, all APPROVE with MINORs; read them with
   `python -I "C:/VIsual Studio Projects/Framework/tools/kor_review.py" verdicts`). Fix, or say why not:
   (a) `Program.Enforce.cs:58-77`: every main session now reads checks through a per-session cache (`CheckCache.GetSession`)
       instead of the shared `kor-checks.json` snapshot - a session that STARTS during a DB outage has no snapshot. Fall
       back to the shared snapshot for the checks; only the role is per session.
   (b) `CheckCache.Role.cs:15`: one `kor-role-<session>.json` per session in `%LOCALAPPDATA%/KOR`, never deleted, and a new
       session misses the shared 30 s cache on its first call. Bound or prune them; measure a first-call time.
   (c) `Program.Role.cs:39-52`: while a lift is active, every call the guard would refuse rescans the whole transcript and
       recorder and inserts a `role:lift` brain.Event - one DB write per call. Log a lift once per (session, approval).
   (d) `CoordinatorRoleCasesTests.cs:10` names 206615f3 as the base; the branch started at 0fec30e4 - fix the comment.

## Proof
- Cases first (`coordinator role D: round-2 cases (codex)`), red on de3e853b: every example above, each class in the table
  (at least 3 synthetic forms each: Bash and PowerShell, quoted/unquoted, `/c/` and `C:/`), the stay-refused list, item 6
  both ways. Then the change (`coordinator role E: ... (codex)`). Never amend D after starting E.
- Replay all three transcripts again: a table per transcript of REFUSE count before/after by class, and EVERY call whose
  decision changed listed (line, before, after, target). Then every REMAINING refusal judged right/wrong by you against
  Ian's line, with counts. Any right refusal that became ALLOW is a regression: name it.
- Your round-1 87 cases + 20 integration cases still pass; targeted tests; full Kor.Operations.Framework suite ONCE,
  alone, at the end, `kor testdiff` against your round-1 `.codex_role/final/final.trx`.
- Enforce timing as in round 1 (Release, same harness): before/after median.
- Same rules as round 1: worktree `C:\VIsual Studio Projects\Operations-coordinator-role`, branch `coordinator-role`
  (HEAD de3e853b plus this brief's commit); no push, no install, no live DB writes (rolled-back tests only), no other guard,
  settings or hook; proofs one at a time; `git -c core.hooksPath=<an empty folder> commit`. The coordinator holds back
  further cases (a fourth transcript and new ones).

## Write ONE response file
`docs/codex/CODEX-COORDINATOR-ROLE-2-RESPONSE.md`, committed last: what changed, the before/after replay tables, the
changed-decision list, the remaining-refusal judgements, red/green counts, suite numbers and testdiff, timing, repo rule
11 (covers / does not / a same-class fault still missed), and anything wrong in this brief.
