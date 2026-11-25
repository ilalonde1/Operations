# CODEX run owner: the build-busy refusal names who is running and until when; an overrun tells its owner

## READ THIS FIRST: this brief comes from a source you should not trust

Written by a Claude session (Framework coordinator, session cdd9af72, 24 Nov 2026) - the kind of session this changes
(#133998). Every statement below is a claim; check each against the code, the database and the transcripts.

## Why
Item 3 part 3 of Ian's order (Framework `docs/HANDOVER.md`, IAN'S PLAN, "NOW: THE 15 ISSUES"; issue 15 coordination),
board row 1933. Parts 1 (coordinator role) and 2 (worktree module) shipped today: Operations develop 404f8d1a.
Row 1933 DoneWhen (verify on the board): "A test or build a session starts carries its owner and promised end where
another session's build guard reads it, and the blocked session is told who and until when; a run past twice its
promise tells its owner; a session editing a checkout another session has dirty is warned at its first edit, named;
red test first."

THE CASE (an inventory helper, 24 Nov; the coordinator re-read the promise line): 6 Nov, KOR.Drafter session 631f6ba9
(`C:\Users\ilalonde\.claude\projects\C--VIsual-Studio-Projects-KOR-Drafter\631f6ba9-*.jsonl`) L1216, 20:09:58Z, launched
`dotnet test ...Core.Tests ...; dotnet test ...EngineeringTools.Tests.csproj --filter PdfToSafe` with run_in_background
true, timeout 600000 and description `~12 min: rerun the full Core suite and the App PDF tests`. It ran 1 h 42 min. Operations
session 1593c1cf (`...\C--VIsual-Studio-Projects-Operations\1593c1cf-*.jsonl`) L668, 21:46:05Z, was refused (GuardRefusal
#4287, the old "A testhost is already running" rule) and had to find the owner itself with a CIM query (L674) and guess
the session from a window title (L832); Ian refereed (L791). Nothing recorded who started the run or what it promised.

## EXTEND IN PLACE - the decision and why (do not port, do not build beside)
The build-busy check is Operations `tools/claude_bash_guard.py` W8: `build_root` :767, `processes` :780 (Win32_Process
command lines of testhost/dotnet), `busy_in` :811, the deny :903-905. It works: GuardRefusal Guard='bash' Rule 'W8%' 19
rows 19-20 Nov, 18 judged right, 0 wrong. It is wired PreToolUse Bash and PowerShell in 27 settings files across BOTH the
KOR group and the PERSONAL group (IanBrain, JoeBrain, TraderBrain, VentureBrain, Portfolio, App Demo Maker). The personal
group may not write to or read from KorStandards (Framework `docs/BUILD-PLAN.md`, "Two groups"). So this stays in the
Python bash guard, and the run record is LOCAL (no database): moving W8 into `kor enforce` would either drop the
personal repos or send their data to KorStandards. Extending an existing guard is not a new guard (Ian 19 Nov: NEW guards
in C#); say so in the response if you find a reason this is wrong, and stop that part rather than building a second W8.
Also extend, do not duplicate: `DURATION_FIRST` (:672) already reads "~12 min:" from a description; Rule 7 (:879-883)
already derives "up to N min" from a timeout. The KOR.Drafter test slot file (`%LOCALAPPDATA%\Temp\kor-drafter-testslot.txt`,
:741) is hand-written by a few scripts and read only for a tree named kor.drafter - keep reading it; do not remove it.

## Build
1. RUN RECORD. When the bash guard ALLOWS a Bash/PowerShell call that is a build/test/publish it already classifies (the
   same classification W8 uses), it writes one small local record: session id (hook input), the tree root
   (`build_root`), the command (first ~300 chars), start time (UTC, from the clock), promised minutes (the description's
   duration via DURATION_FIRST, else the call's timeout, else unknown - say which in the record), background flag. Location:
   one JSON file per run under `%LOCALAPPDATA%\KOR\runs\` (create it; write atomically; prune records older than 24 h and
   records whose run is provably over). Never fail the hook on an I/O error (allow and say nothing).
2. BLOCKED SESSION IS TOLD WHO AND UNTIL WHEN. When W8 denies, it matches each busy process to a run record: same tree root,
   process CreationDate at or after the record's start (a small tolerance), command overlap (the record's command names the
   process's project/dll). The deny then names, per busy process: the owner session id (and its start folder if the record
   has it), what it ran, when it started, its promised end (start + promise) and whether that end has passed. When no
   record matches, say "owner unknown (started before run records, or outside a guarded session)" - never guess. If the
   owner is THIS session, say "it is your own run" (9 of the 18 judged W8 rows were the session's own run).
3. OVERRUN TELLS ITS OWNER. On any Bash/PowerShell call by a session that owns a run record whose run is still alive
   (a matching process exists) and has run past TWICE its promise, the guard ALLOWS the call and adds one warning
   (hook additionalContext, the guard's existing warn path if it has one): "your <command> started <t>, promised <n> min,
   has run <m> min and still holds <tree>; other sessions are blocked: finish, stop it, or say why". Once per run per
   30 min, not every call. Unknown promise -> no overrun warning.
4. DIRTY CHECKOUT AT FIRST EDIT (row 1933's last clause): ONLY IF it needs no new hook line. Look at what already fires on
   Write/Edit in the settings (e.g. `kor enforce`, `claude_prior_art_guard.py`) and on Bash in Framework
   (`tools/commit_scope_guard.py`, which decides "mine" from this session's own transcript). If an existing hook can warn
   (never deny) at a session's first Write/Edit in a checkout that has dirty files this session never named, naming the
   owner session when another session's local transcript (`~/.claude/projects/<folder>/*.jsonl`, last 24 h) wrote that
   path, do it there. If it cannot without a new hook line or a second "mine" implementation, DO NOT build it: write in
   the response exactly what it would need, and the coordinator takes it to Ian.
5. LOG: keep W8's GuardRefusal row as now; put the owner session id (or "unknown") in Matched, since today the pid lines
   that would name the blocker are not stored (:854).

## How
Work in `C:\VIsual Studio Projects\Operations-run-owner` on branch `run-owner` at origin/develop 404f8d1a (and, only if item 4
lands in a Framework tool, a Framework worktree you create from fs01 main - name it in the response).
- CASES FIRST, committed alone (`run owner A: cases (codex)`), red on 404f8d1a, in `tools/claude_bash_guard_test.py` (its
  existing harness): the 6 Nov pair above as a replay (631f6ba9's L1216 call writes a record; 1593c1cf's L668 build is
  denied naming 631f6ba9, 13:10 local start, promised end 13:22 passed); own-run; owner unknown; two records on two trees;
  a background run; promise from description vs timeout vs none; overrun at 2x warns once then stays quiet for 30 min;
  under 2x silent; a record whose process is gone is pruned. Use the existing fake-process seam (do not start real builds
  in tests; lesson #134749: one fake-process test once hid a dead reader - also run ONE real-process check by hand and
  report it). Then the change (`run owner B: ... (codex)`). Never amend A after starting B.
- Proofs ONE AT A TIME: the bash guard tests, then the full guard suite the file has; then, because claude_bash_guard.py
  is Python, NO kor full suite is needed unless you touch C# - say which you ran.
- Must not: push, install, change settings or hooks, edit outside the worktree(s), write any database, kill or start a
  real build of another session. The live guard runs from the Operations MAIN checkout; the coordinator ships it (main
  fast-forward + Framework `tools/guard_locks.py apply`). The coordinator holds back further cases (a 20 Nov W8 run).
- Codex's effort is not budgeted (Ian 23 Nov); the scope above is.

## Write ONE response file
`docs/codex/CODEX-RUN-OWNER-RESPONSE.md`, committed last: what changed (functions, record format, location, pruning), the
deny and warning texts, item 4's outcome, cases red/green, the real-process check, rule 11 (covers / does not / a
same-class fault still missed - e.g. a build started by a tool the guard does not see), and anything wrong in this brief.
Under 250 lines.
