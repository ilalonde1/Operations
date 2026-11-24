# CODEX worktree module: a git worktree is its main repo's module, everywhere kor writes a module

## READ THIS FIRST: this brief comes from a source you should not trust

Written by a Claude session (Framework coordinator, session cdd9af72, 24 Nov 2026) - the kind of session whose records
went astray (#133998). Every statement below is a claim; check each against the code, the database and the transcripts.

## Why
Item 3 part 2 of Ian's order (Framework `docs/HANDOVER.md`, IAN'S PLAN, "NOW: THE 15 ISSUES"; issue 15 coordination),
board row 2240. Ian "go on part 2" (brain.Memory #139794). Part 1 (the coordinator-role guard) shipped today as Operations
develop 6bb62371; part 3 (row 1933) is a later brief - NOT in scope.

THE FAULT (read 24 Nov, verify): KorStandards `brain.Memory` holds 86 records in 14 lanes named after KOR.Drafter git
WORKTREE folders - KOR.Drafter-step5 25, -catalogue 9, -viewer 8, -comparison 7, -plan 7, -integration 6, -etabs 4,
-geometry-gates 4, -quantities 4, -revit 5, -wt-2058 2, -wt-selfcheck-geom 2, -wt-selfcheck-text 2, -wt-selfcheck-core 1
(`SELECT Lane, COUNT(*) FROM brain.Memory WHERE Lane LIKE 'KOR.Drafter-%' GROUP BY Lane`). Every lane reader matches
exactly (`Freshness.cs:51`, `RecordStore.cs:436/446/461/478`, `MemoryRecall.cs:100/114/127`, `ModuleState.cs:322-402`),
so the KOR.Drafter lead's session start (`kor start --module "KOR.Drafter"`) never shows any of them.

ROOT CAUSE (an inventory by a helper, 24 Nov; the coordinator re-read the three code points and one transcript):
86 of 86 were written with an explicit `--module KOR.Drafter-<worktree>` (`kor record` refuses without one,
`Cli/Program.Knowledge.cs:133`). The value was COPIED FROM KOR'S OWN STOP-HOOK ASK: `Capture/GitCapture.cs:38-41` sets a
commit event's `module ??= Git.Leaf(top)` - the worktree's folder name - and `Capture/SessionCapture.cs` prints
`record --session S --module {first.Module}` (around :229-233) in the capture-check ask. Examples: session c2ad5988
(`C:\Users\kor-claude\.claude\projects\C--VIsual-Studio-Projects-KOR-Drafter-etabs\c2ad5988-*.jsonl`) L1895 ask, L1912
`kor record ... --module KOR.Drafter-etabs`; dd6412f2 L502 ask -> L513 `--module KOR.Drafter-wt-selfcheck-core`;
cd2499ed L2888 ask -> L2894 `M=KOR.Drafter-comparison ... --module $M`. The worktrees' own settings already pass
`--module "KOR.Drafter"` to their hooks (e.g. `KOR.Drafter-etabs/.claude/settings.json:13-31`); only folder-derived
modules go wrong.

## Prior work - EXTEND these, do not build beside them (check each)
- `Capture/Git.cs`: `TopLevel` :109, `TopLevelFast` :120, `Leaf` :181 - the three helpers every folder->name site uses
  (34 sites counted; the inventory's list is in the coordinator's notes - re-count them yourself and list them).
- Framework `tools/kor_review.py:196-200` `repo_name` ALREADY resolves a worktree to its main repo:
  `git rev-parse --path-format=absolute --git-common-dir`. Same rule in C#; a fast path that reads the worktree's `.git`
  FILE (`gitdir: <main>/.git/worktrees/<name>`) without spawning git is welcome - it also survives the "dubious
  ownership" refusal on worktrees owned by kor-claude when the caller is ilalonde (seen on KOR.Drafter-etabs).
- `Capture/ClaudeCapture.cs:57-61` `ModuleOf` (CLAUDE_PROJECT_DIR leaf, else cwd leaf) and its callers :101, :117
  (compact - also the LaneState lane), :137; `SessionCapture.cs:43`, :47-48, :86; `ClaudeCapture.cs:95-100` Tool().

## Build (in kor, C#)
1. ONE resolver in `Git.cs` (e.g. `Git.ModuleOf(top)` / `Git.MainLeaf(top)`): a worktree resolves to its main repo's
   folder leaf; a main checkout resolves to itself; not-a-repo stays as today. No second copy of the rule anywhere.
2. Route every site that writes a MODULE or LANE through it: GitCapture.Build (the source of all 86), ClaudeCapture
   ModuleOf and Tool(), SessionCapture, compact/LaneState, session-end. The capture-check ask then prints the main
   module. Also the 9 inline `Path.GetFileName(TopLevelFast(cwd))` writers of `oversight.GuardRefusal.Repo`
   (`Program.Enforce.cs:109,143,167`, `Program.Context.cs:83,111`, `Program.Start.cs:303`, `Program.Stall.cs:78`,
   `Program.Repeat.cs:111`, `Program.Whole.cs:95`) - one helper, so guard refusals are judged per repo.
3. DO NOT change what must stay the worktree: a record's `SourceRepo` / GitSource (`Records/RecordStore.cs:78-84` At,
   `:123-125` Verify, `Freshness.cs:66-69`, `RepoLocator.cs:19-27`) - freshness reads the file at the worktree's HEAD; at
   the main checkout 63 of the 86 cited paths do not exist. Lane and SourceRepo become different on purpose; say where
   any code assumed they are equal.
4. `kor record --module X` where X names a worktree of an enrolled module - X is `<Module>-<suffix>` for a module in
   `EnrollCheck.Repos` (or the sibling folder's `.git` file points into that module's `.git/worktrees/`) and X is not
   itself an enrolled module - is REFUSED with one line: `--module X is a worktree of <Module>: use --module "<Module>"`.
   It must work for worktree folders that no longer exist (7 of the 14 are gone: -integration, -plan, -quantities, -wt-*).
5. The existing 86: do NOT write the live database. Put in the response a reviewed SQL script, transactional: a SELECT
   listing the ids it will move (expect 86), `UPDATE brain.Memory SET Lane = 'KOR.Drafter' WHERE Lane LIKE 'KOR.Drafter-%'`
   restricted to those ids, changing Lane ONLY (not SourceRepo, not Name, not bodies), and a read-back count. The id list
   is captured into a table variable BEFORE the UPDATE; the UPDATE joins on it, and the transaction ROLLS BACK unless
   @@ROWCOUNT equals the captured count (and that count equals the number the coordinator states when running it). Check and
   state: no Name collision with lane KOR.Drafter (coordinator read 0 on 24 Nov), whether anything else keys on Lane
   (vw_ModuleState, LaneState, Check, Freshness, supersede-by-name) and what the move does to each. Ian decided all 86
   go to KOR.Drafter (#139795), including the 25 in KOR.Drafter-step5 written by Framework sessions. The coordinator
   runs the script after the Virtual Drafter lead agrees (row 1194).
6. History is NOT rewritten: brain.Event.Module (1,563 rows under 26 worktree names) and GuardRefusal.Repo (192 rows
   under 16) stay; say what reads them by module and what it means that old rows keep worktree names.
Not in scope (say so): Framework Python (`tools/guard_log.py:69-82 repo_of`, `tools/stage_gate.py:164-167`) - a later
change in the Framework repo; the lane move itself; row 1933.

## How
Work in `C:\VIsual Studio Projects\Operations-worktree-module` on branch `worktree-module` at origin/develop 6bb62371.
- CASES FIRST, committed alone (`worktree module A: cases (codex)`), red on 6bb62371: real worktrees and a main repo
  built in a temp folder inside the test (a worktree with a `.git` file; a main checkout; a deleted worktree folder; a
  worktree whose name has several dashes; a non-repo folder; a repo whose own name has a dash but is not a worktree -
  e.g. an enrolled `KOR Inspections Bookings`-style or `KOR.RevitTools` name); the three transcript examples above as
  capture-check ask cases; the `kor record --module KOR.Drafter-etabs` refusal and `--module KOR.Drafter` allowed.
  Then the change (`worktree module B: ... (codex)`). Never amend A after starting B.
- Tests never leave a row in KorStandards (rolled-back transactions only); report any row left.
- Proofs ONE AT A TIME: targeted, then the full Kor.Operations.Framework suite ONCE, alone, at the end, compared by name
  with a run at 6bb62371 (`kor testdiff`).
- `kor enforce` runs on every tool call: the GuardRefusal.Repo change must not add a git process per call on the hot
  path (the `.git`-file fast path, or only on refusal). Measure before/after, Release.
- Keep TEMP/TMP normal. Commit with `git -c core.hooksPath=<an empty folder> commit`. Must not: push, install kor, edit
  outside this worktree, change another guard or settings or git hook, write the live database, touch another machine.
  The coordinator holds back further cases (a fourth session's records among the 86, and new ones).

## Write ONE response file
`docs/codex/CODEX-WORKTREE-MODULE-RESPONSE.md`, committed last: the resolver and every site routed through it (and every
site deliberately not), the `--module` refusal text, the move script with its checks, cases red/green, targeted and
full-suite numbers + testdiff, timing, rule 11 (covers / does not / a same-class fault still missed), and anything wrong
in this brief. Under 300 lines.
