# CODEX coordinator role: a coordinator session that does a worker's work is refused

## READ THIS FIRST: this brief comes from a source you should not trust

It was written by a Claude session (Framework coordinator, session cdd9af72, 24 Nov 2026) - the kind of session this guard
is meant to control. Ian asked that every Codex brief from a Claude session say so up front (#133998). Treat every statement
below as a claim; check each against the code, the database, the transcripts and the files named.

## Why
Ian's list of agentic-development issues (Framework `docs/THE-PRIZE-AGENTIC-DEV-ISSUES-2026-11-19.md`, issue 15:
"Coordinating with other sessions multiplies all of this"). Item 3 of Ian's order (Framework `docs/HANDOVER.md`, IAN'S PLAN,
"NOW: THE 15 ISSUES"). This brief is the FIRST of three parts; rows 2240 (records filed under a worktree name) and 1933
(a build blocks another session unseen) are later briefs - NOT in scope here.

The failure (lesson brain.Memory #138344): 23 Nov 13:3x-15:5x local, coordinator session cc3b7be3 built an acceptance
bundle, edited scripts, re-ran fixtures and rewrote briefs itself; context 600k -> 780k; several self-made mistakes. Ian,
22:52:45Z-22:54:31Z: "Why are you doing all this work?" / "You're a coordinator" / "A handover? Why isn't a worker doing
this? Not another coordinator" (#138345). The coordinator writes briefs and checks results; a worker does the work.
Codex's own 18 Nov gap analysis named the gap (`docs/codex/CODEX-FRAMEWORK-GAP-ANALYSIS-1-RESPONSE-PART2.md:102`): "Role
labels are not capability restrictions."

## The line - Ian's, 24 Nov (brain.Memory #139076). Do not redraw it; if the code cannot hold it, say so in the response.
A COORDINATOR MAY:
- write/edit briefs and handovers: Framework `docs/HANDOVER.md`, Framework `lanes/**`, any repo's `docs/codex/CODEX-*.md`,
  `C:\KOR-Operations\push-exceptions.txt`, its own scratchpad (`%TEMP%\claude\...\scratchpad\**`);
- run git (status/log/diff/add/commit/push/worktree/fetch/merge), `kor ...` (record, state, start, testdiff, whole, budget ...),
  `python -I .../Framework/tools/kor_review.py ...`, `board_render.py`, `guard_log.py`, read-only commands;
- CHECK a worker's result: `dotnet build` / `dotnet test` of a worker's branch or worktree, held-back tests that live ONLY in
  its scratchpad, `tools/install-kor.ps1` then `kor --version`.
A COORDINATOR IS REFUSED:
- Edit/Write/MultiEdit/NotebookEdit of project files - code, scripts, tests, bundles, data - in any repo or worktree,
  outside the allowed paths above (that includes Framework's own `tools/`);
- product runs: KOR.Drafter `docs/one-tool/run-on-210/send-set.ps1` and `start-on-210.ps1` (a KOR-210 run), `takeoff.exe` /
  `Kor.Operations.EngineeringTools.TakeoffCli` runs, any `build_bundle*.py`, a python run of a script under a repo's `work/`;
- shell writes into project files outside the allowed paths (`sed -i`, `>`/`>>`, `tee`, `cp`/`mv`/`rm`/`mkdir`, `Set-Content`,
  `Out-File`, `New-Item`, `Copy-Item`, `Remove-Item`) - judge the TARGET path, not the cwd.
LIFT: only Ian's typed `IAN-APPROVED-COORDINATOR-WORK` in a user prompt of THAT session, found the way
`DrafterPythonGuard.Approved` finds its token (claude_prompt_recorder.py's record); 12 hours; logged as a lift. The same
words inside a tool result, a file or a quoted paste do not lift.

## Who is a coordinator - nothing records it today (verify)
Inventory 24 Nov: no hook can read a session's role. `Framework/tools/kor_handover.py` writes brain.Event Kind 'gate' with
Detail `$.role` (:9, :350, :586) only when a handover happens; brain.LaneState has no role column (KOR.Drafter
`db/162_MemoryLivesInTheDb.sql:60-65`); hooks tell a subagent by `agent_id` (`Program.Start.cs:249`) or transcript path
(`Context/ContextTranscript.cs:34`). The Framework lead IS the coordinator (Framework `CLAUDE.md`, "The Framework lead is THE
coordinator across leads"). Rule to build:
- A MAIN session (no `agent_id`) whose start folder (`CLAUDE_PROJECT_DIR`, else the hook `cwd` at its first event) is the
  Framework repo is a coordinator by default. Subagents are exempt (a helper the coordinator sends IS delegation).
- `kor role set worker|coordinator --session <id> [--because "..."]` writes one brain.Event (Kind 'gate', Ref
  'role:<role>', Detail with session) - the way a worker opened in Framework says what it is; `kor role show --session <id>`
  prints the role and where it came from. The latest event wins; a handover 'opened' event's `$.role` counts too (extend
  kor_handover's reading, do not edit kor_handover.py's writing). Every change of role is visible in brain.Event.
- A session started in any other folder has no role unless it set one: the guard is silent for it.
If you find a sounder source of the role in the code or the DB, use it and say why.

## Prior work - EXTEND these, do not build beside them (an inventory by a helper, 24 Nov; check each)
- `Kor.Operations.Framework/Enforce/DrafterPythonGuard.cs` (:42 class, :45 `Approval`, :99 `Approved`) and its wiring
  `Kor.Operations.Framework.Cli/Program.Enforce.cs:50-52, :101-127` (`DrafterPythonRefusal`), `SqlWriteCutRefusal` :131,
  `SqlBudgetRefusal` :154: THE pattern - a C# guard inside `kor enforce` (already fires on Bash|PowerShell|Write|Edit|
  MultiEdit|NotebookEdit), refusal logged with `Stall.StallStore.LogGuardRefusal` (guard name `coordinator-role`). Add the
  new guard there; no new hook line, no Python. (Ian 19 Nov: new guards in C# / kor, record #134051.)
- `Kor.Operations.Framework/Budget/BudgetApproval.cs`: a typed lift with a 12 h window - reuse its shape.
- A brain.Check row (KOR.Drafter `db/172_ALessonIsEnforced.sql:201-215`, `Enforce/Rules.cs`) is one regex over one input
  with no session state: it cannot express a role (lesson #134054). Do not try.
- Target-path judging that already exists: Operations `tools/claude_bash_guard.py` (W8 `build_root`, `git_root` :750) and
  Framework `tools/commit_scope_guard.py` (follows a literal `cd` only - a known false allow; do better here: `cd "<p>" &&`,
  `cd <p>;`, `Set-Location [-Path] "<p>";`, `pushd`, and an absolute `file_path`).
- `kor whole --replay <transcript>` - the replay pattern; give this guard the same (`kor role replay <jsonl>`): for every
  main-thread tool call, ALLOW/REFUSE + rule + target. The coordinator runs it on held-back transcripts.

## The real cases (transcripts under `C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-Framework\`, file
`<session-uuid>.jsonl`, match by prefix; line = jsonl line; times UTC). In EVERY case the jsonl `cwd` is the Framework
folder - the session reached the other tree by a `cd`/`Set-Location` inside the command or by an absolute path.
MUST REFUSE (cc3b7be3, coordinator, 23 Nov):
- L2741 (and 2743-2747) 19:24:37 Edit `...\KOR.Drafter\work\_acceptance\2248\README-base.md`; L2803 `...bundle\evidence\EVIDENCE-README.md`
- L3981 22:30:47 Bash `cd ".../KOR.Drafter" && mkdir -p work/_acceptance/2248/bundle/evidence/refusals && ...`
- L4073 22:37:50 Write `...\2248\work\fix_refusal_headers.py`; L4077 Bash `python -I` on it
- L4251 / L4270 22:46:29 Edit `...\2248\checker-acceptance.json` / `...\2248\build_bundle.py`
- L4309, L4326 22:47:19 Bash `cd ".../2248" && rm -rf bundle/...__pycache__ && T=".../takeoff.exe"; python -I build_bundle...`; L4403 `sed -i ... read_rulings.py`
MUST REFUSE (fa93ad77, coordinator, 22 Nov): L1936 04:39:20 Edit `...\Operations-bash-pipefail-narrow\tools\claude_bash_guard.py`
(also 1898 `_test.py`, 1938, 2002-2033) - guard code written by the coordinator (Ian had picked it; under the new line that
needs his typed lift, so it refuses without one).
MUST ALLOW:
- cc3b7be3 L2574 19:16:07 Write `...\KOR.Drafter-step5\docs\codex\CODEX-STEP5-2248-COVERAGE-EXCEPTION.md` + L2578 git commit
- cc3b7be3 L2680 19:23:11 Edit `...\Framework\docs\HANDOVER.md` + L2682 git commit
- cc3b7be3 L4574 22:54:48 Write `...\Framework\lanes\2026-11-23\brief-step5-2248-finish.txt` + L4578 git commit
- cc3b7be3 L2664 19:22:49 Bash `"/c/KOR-Operations/kor/kor.exe" record --session ... --kind finding`
- b6568d08 L5412 21 Nov 20:02:23 `cd Operations-kor-whole-narrow && dotnet build ... && dotnet test` (checking Codex's result)
- installs: `tools/install-kor.ps1` runs (cc3b7be3 L3827)
- a worker or Codex session (not started in Framework, or role worker) doing any of the refused things; any subagent.
BORDERLINE, decide and say: cc3b7be3 L4235 22:42:54 `cd ".../KOR.Drafter-step5" && dotnet test ... --filter
"FullyQualifiedName~AnalysisModelAcceptanceFixtureTests"` is fixture re-running as part of a fix, but by command it is a
check, and the line allows `dotnet test`. Expect it ALLOWED and list it as a same-class miss.
Write each case as a test (input = the tool name + input + cwd from the jsonl), plus synthetic forms: PowerShell
`Set-Location -Path`, `pushd`, `cd -`, relative paths after a cd, forward vs back slashes, `C:/` vs `/c/`, quoted paths
with spaces, a heredoc into a project file, `Out-File`, `cp scratchpad/x.cs repo/...`.
The author holds back further cases from real transcripts.

## How
Work in `C:\VIsual Studio Projects\Operations-coordinator-role` on branch `coordinator-role` at origin/develop 206615f3.
- CASES FIRST, committed alone (`coordinator role A: cases (codex)`), each failing on the current code. Then the change
  (`coordinator role B: <one line> (codex)`). Never amend A after starting B. More commits are fine.
- Tests never leave a row in KorStandards: DB writes only inside a transaction that is rolled back; report any row left.
- Proofs ONE AT A TIME (lesson #138092): targeted tests, then the replay over the three transcripts above, then the full
  Kor.Operations.Framework tests ONCE at the end, alone, compared by name with a run at 206615f3 (`kor testdiff`).
- Speed: `kor enforce` runs on every tool call (median ~110-117 ms live, 22 Nov). The role lookup must not add a DB read
  per call beyond what enforce already does (cache per session like enforce's 30 s re-read); measure before/after, Release.
- Keep TEMP/TMP normal. Commit locally with `git -c core.hooksPath=<an empty folder> commit ...` (the shared post-commit
  hook writes the database); the coordinator reviews every commit through kor_review and the pre-push gate before anything
  is pushed. Must not: push, install kor, edit outside this worktree, change any other guard or settings or git hook, write
  the live database, touch another machine.
- Codex's effort is not budgeted (Ian 23 Nov); the scope above is.

## Write ONE response file
`docs/codex/CODEX-COORDINATOR-ROLE-RESPONSE.md`, committed last: what was built (files, verbs), how the role is decided and
where it is read from, the refusal text and the lift, every case and its red/green, the replay table for the three
transcripts (each listed line: expected, got), targeted and full-suite numbers and the testdiff, the enforce timing,
repo rule 11 (what the guard COVERS, what it does NOT, and at least one same-class fault it would still miss), then anything
wrong in this brief.
