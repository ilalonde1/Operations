# CODEX coordinator role, round 3: close two false allows and one false refusal, nothing else

## READ THIS FIRST: this brief comes from a source you should not trust

Written by a Claude session (Framework coordinator, session cdd9af72, 24 Nov 2026) - the kind of session this guard
controls (#133998). Every statement below is a claim; check each against the code and the repos.

## Why this round
Round 2 (`docs/codex/CODEX-COORDINATOR-ROLE-2-RESPONSE.md`, 309fdb32..5d27d893) was checked by the coordinator: held-back
transcripts right, full suite NO NEW FAILURES against the item-2 baseline. The independent review (kor_review rows
3773-3775, APPROVE with MINORs; read with `python -I "C:/VIsual Studio Projects/Framework/tools/kor_review.py" verdicts
72d50f49..5d27d893`) found two FALSE ALLOWS and one new FALSE REFUSAL. Ian 24 Nov (brain.Memory #139554): "Round 3 then
ship" - one narrow round on exactly these, then the coordinator verifies and ships. SCOPE IS ONLY THE FOUR ITEMS BELOW.

## Fix
1. FALSE ALLOW - cleanup root (`CoordinatorRoleGuard.Paths.cs` CleanupRoot). Today any `<Repo>-<suffix>` sibling that
   `git worktree list` does not list may be removed (rm -rf / Remove-Item -Recurse). A separate clone, or a worktree whose
   branch has commits on no remote, could be deleted. New rule - ALLOW the removal of a sibling root only when ONE holds:
   (a) the folder is empty (0 entries - the case seen on 21-22 Nov: an emptied worktree folder held busy by another window);
   (b) its `.git` is a FILE pointing into `<main repo>/.git/worktrees/<name>` of the repo its name names, and EITHER that
       entry no longer exists (already pruned) and the folder holds nothing but `.git` and git-ignored build output, OR the
       entry exists and `git -C <folder> status --porcelain` is empty AND its HEAD commit is contained in a remote-tracking
       ref of the main repo (`git branch -r --contains <HEAD>` non-empty) - nothing unpushed, nothing uncommitted.
   REFUSE otherwise; in particular a folder holding a `.git` DIRECTORY (a separate clone) is never cleanup. A failed git
   query proves nothing and refuses (as now). Facts 24 Nov (verify): 13 sibling folders under `C:\VIsual Studio Projects`
   (`KOR.Drafter-*` 9, `Operations-*` 4), every one a worktree (`.git` file), no clone.
2. FALSE ALLOW - output directory (`CoordinatorRoleGuard.Paths.cs` CheckOutputs + `CoordinatorRoleGuard.cs` Write).
   `dotnet build -o .` (or `-o` / `--output` / `--results-directory` naming a repo root, a drive root, or any existing
   directory that is not itself under `obj`/`bin`/`TestResults`/scratch/temp) currently allows every later write in the same
   command, e.g. `dotnet build -o .; echo x > src/a.cs`. New rule: a declared output directory is an allowance ONLY when it
   is under `obj`, `bin` or `TestResults` of a repo, under the scratchpad/temp roots, or did not exist before the command
   (a fresh folder - judge by the filesystem at hook time) AND is not a repo root or drive root. Otherwise it gives no
   allowance and the later writes are judged on their own.
3. FALSE REFUSAL - cmd switches (`CoordinatorRoleGuard.cs` Arguments). The general `/switch` skip became a fixed list
   (/y /s /e /q /i /h /r), so `del /F /Q x`, `xcopy a b /D`, `robocopy a b /MIR /XD obj` read `/F`, `/D` as a rooted target
   `C:/F`. For cmd-style commands (del, erase, copy, xcopy, move, rd, rmdir, robocopy, mkdir, md) treat a token matching
   `^/[A-Za-z]{1,8}(:\S*)?$` as a switch; a token with a second `/` or a drive (`/c/...`, `C:/...`) stays a path.
4. Regression from round 2 (`CoordinatorRoleGuard.cs` ChangesDir): `dir = Resolve(target, dir) ?? target` keeps a raw
   relative word when cwd is null, so a later relative target resolves against the hook process's CurrentDirectory; round
   1 refused it. Restore: an unresolvable cwd makes later relative targets UNRESOLVED (refuse with the remedy text).
NOT in scope (leave as they are; the coordinator carries them): the outage fallback when kor-checks-cache.json is
missing, the lift scan resetting each prompt, loop-variable targets, python stdin in a work/ folder.

## Proof
- Cases first (`coordinator role G: round-3 cases (codex)`), red on 5d27d893: per item at least 4 cases (both polarities,
  Bash and PowerShell where it applies, quoted/unquoted, `/c/` and `C:/`), built on real folders made in a temp root inside
  the test (a worktree with a pushed HEAD; one with an unpushed commit; one with an uncommitted file; a pruned worktree
  entry; a clone with a `.git` directory; an empty folder). Then the change (`coordinator role H: ... (codex)`). Never
  amend G after starting H.
- All earlier cases still pass (205). Replay the same three transcripts: before/after REFUSE counts and every changed
  decision listed (expected: few or none; any right refusal that became ALLOW is a regression - name it).
- Full Kor.Operations.Framework suite ONCE, alone, at the end; `kor testdiff` against your round-2 final trx.
- Same rules as rounds 1-2: this worktree, branch `coordinator-role`; no push, no install, no live DB writes, no other
  guard/settings/hook; proofs one at a time; `git -c core.hooksPath=<an empty folder> commit`; never remove a real folder
  outside your test's temp root. The coordinator holds back further cases.

## Write ONE response file
`docs/codex/CODEX-COORDINATOR-ROLE-3-RESPONSE.md`, committed last: what changed per item, red/green counts, the replay
before/after and changed decisions, suite numbers and testdiff, rule 11 (covers / does not / a same-class fault still
missed), and anything wrong in this brief. Keep it under 250 lines.
