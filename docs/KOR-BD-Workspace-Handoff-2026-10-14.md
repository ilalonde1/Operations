# BD workspace — state at 2026-10-14, and the seed prompt for the next session

Paste this whole file as the first message of a new session. It was written by the session
that ended on 2026-10-14 at commit `415b8d5e`, for the session that starts the next round of
changes. Everything in it was paid for. Where a rule is a hook, the hook is named; where a
number is given, the command that produced it ran on 2026-10-14. **Everything in sections 1
to 5 is SETTLED. Do not re-derive it, re-prove it, or relitigate it.**

---

## 0. Read these first, in this order, before touching anything

1. `CLAUDE.md` in the repo root. Twelve rules; each one records what breaking it cost. Rules
   1, 7 and 12 and the test and SQL limits are **hooks**, not advice.
2. `MEMORY.md` hot block, then these memory files by name:
   `feedback_professional_standard`, `feedback_app_zip_is_ians_call`,
   `feedback_verify_wpf_by_running`, `feedback_codex_briefs_must_be_bounded`,
   `feedback_look_before_rebuilding_post_compaction`, `reference_bd_opportunity_pipeline_map`,
   `reference_published_clock_source_timezones`, `reference_bd_opportunities_worker_deploy`.
3. `git log --oneline -20`. The last sixteen commits (2026-10-13 and 14) are the state this
   file describes.
4. Run the render harness once and open the PNGs before planning any screen change:
   `dotnet test Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj --filter "FullyQualifiedName~BdScreensRenderTests"`
   writes `%TEMP%\kor-bd-screens\*.png`. Read every one.

## 1. Who and where

- **Ian Lalonde**, IT + .NET developer + BD + Ops Lead at KOR Structural. Not a P.Eng. He
  reads code, runs Codex, owns deploys to the firm, and is the only user who looks at the
  BD workspace before it ships. Address him as a peer; he does not want narration, hedging,
  menus of options, or questions he has already answered.
- **This machine is KOR-1001.** Never "your PC". Act on it locally.
- Repo `C:\VIsual Studio Projects\Operations`, branch `develop`, main branch `main`.
  Three untracked files are not ours; leave them: `docs/codex/CODEX-VEEAM-CLOUD-ORPHAN-DELETE-RUNBOOK.md`,
  `docs/codex/CODEX-VEEAM-CLOUD-ORPHAN-DELETE-WEDGE.md`, `tools/RelevanceGateDiff/candidate-calgary-vocabulary.txt`.
- The WPF app is `Kor.Operations.App` (assembly `Kor.Operations.App`, `UseWPF=true`,
  net8.0-windows10.0.19041.0). Ian runs it from `C:\KOR-Operations\Kor.Operations.App.exe`
  via the "KOR Operations" shortcuts (Desktop and Start Menu). It is **single-instance**
  (mutex `Kor.Transmittals.SingleInstance.Mutex`): a second launch logs "Another instance is
  already running; forwarding args and exiting" and quits. Its log is
  `%APPDATA%\KorOperations\logs\app-YYYYMMDD.log` (Serilog, daily). Several services log
  nothing, so an absent log line proves nothing.
- Tests: `Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj`
  (namespace `Kor.Operations.App.Tests`), Moq 4.20, xUnit 2.9. Internals are visible to it.
- Database `KorOpportunitiesDb` on `KOR-APP01\SQLEXPRESS`. Windows auth cannot open it; the
  app's service login can. The connection string is in the env var
  `KOR_OPPORTUNITIES_OPPORTUNITIESDB`. **Never echo it, never paste it, never put a
  Windows or AD user in a database.**
- Deltek via ODBC DSN `Deltek`, credentials in `KOR_ODBC_USER` / `KOR_ODBC_PASSWORD`,
  catalog `C0000052267P_1_KOR00000000`. Never assert Deltek state from memory; probe it.
  `PRSummaryMain` is live (MaxPeriod 202610, measured 2026-10-13). Proposals are not jobs
  (read `PR.Stage`). Aggregate by `LEFT(WBS1,5)` first or every total is ~2.5x high.
- Commit attribution, every commit:
  `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` and
  `Claude-Session: <the session URL>`.

## 2. The rules that were paid for. Most are hooks now.

1. **A question from Ian is a STOP.** Answer it before doing anything else. "Asking me if I
   want to fix something broken sounds stupid." "You're not listening. Yes! I want it
   changed!!!" Do not ask permission for reversible work that follows from the ask.
2. **Everything Ian catches is a claim about absence.** "There is no X", "US coverage is
   zero", "no feed found", "no usage log" were all wrong. Trust a number shown with the
   command that produced it; state it as X of Y; never trust "there is no".
   Hook: `tools/claude_bash_sample_guard.py` stamps any truncated output as a sample.
3. **Search before you build, and say what you searched.** Hook:
   `tools/claude_prior_art_guard.py` denies a Write that creates a file under `docs/`,
   `tools/`, `Schema/*.sql` or `db/*.sql` unless this session listed that directory AND
   opened a sibling of the family by exact name. Listing alone does not count.
4. **Render and look before shipping a screen.** On 2026-10-13/14 three BD screens shipped on
   string gates alone and Ian's eyes were the first render, three times ("I don't see ANY
   difference", "still ugly as fuck", "neverending fixes"). `BdScreensRenderTests` now
   renders the dashboard, the RFP window (live and removed), Pursuits and the pursuit-health
   board to PNGs. Run it, read the PNGs, fix what you see, then ship. **A screen with no
   harness gets one first** (copy `BdScreensRenderTests`; the pattern is
   `NetworkOpsWindowsRenderTests`). The harness cannot judge looks and never fires Loaded;
   it puts the picture where a person can see it.
5. **Two symptoms with the same shape: stop fixing, name the class in one sentence, build
   the check that fails on every instance, then fix.** (CLAUDE.md rules 10 and 11.)
6. **The local app install on KOR-1001 is mine to run, unasked, after App commits.**
   `tools/release-kor-ops-app-local.ps1` (~2.5 min) publishes Release self-contained, stops
   the copy running FROM `C:\KOR-Operations` (filtered on Path, nobody else's), mirrors with
   `/XD cli`, rewrites the shortcuts. Say first that it closes his running copy. Then verify
   the **installed DLL** carries the change (grep the binary for a XAML string), not the
   build log. Ian: "DEPLOY IT LOCALLY FFS" / "Why do I need to ask for this??" when I
   offered instead of doing it. The **firm-share zip** (`tools/deploy-newerforma-app.ps1`,
   `V<N>.zip`) stays Ian's call; never on my own initiative.
7. **The Worker deploy** (KOR-APP01) is `publish-opportunities.ps1 -SkipTests` then
   `deploy-opportunities.ps1`, both in the repo root. Commit BEFORE publishing so the
   version stamp carries the hash; read ProductVersion and the robocopy `N changed` after.
   `robocopy` must `/XF appsettings.Production.json`. Worker on APP01 as of 2026-10-13:
   `1.0.9782.1088` / commit `8ae3efd1`. Nothing in the App-side commits since touches
   the Worker (grep confirmed 0 references).
8. **Tests: targeted, never the suite.** Hook: `tools/claude_bash_guard.py` denies
   `dotnet test` naming the App test project without `--filter` unless prefixed
   `FULL_SUITE=1`. The App suite hangs headless and holds the build lock. A filtered run
   is 1 to 3 minutes including the build. Use `FullyQualifiedName~<Class>` filters.
9. **SQL: one file, run once.** Hook: the fourth `sqlcmd` / `oq.py` / `oqwide.py` call in
   ten minutes is denied. Batch every question into one `.sql` with `PRINT` headers.
   Migrations are `Schema/NNN_*.sql` (latest applied: 346), mandatory `-v Apply=0|1`,
   dry-run by default, an audit/undo table and an extended property `Rollback`, idempotent
   on the VALUE. Compile new statements with `SET NOEXEC ON` before running them.
10. **Say the duration before any step over a minute.** Hook: a command with
    `timeout > 120000` ms is denied unless its description starts with a duration
    ("~3 min: …"). Going silent for long steps is the complaint behind half of this file.
11. **Never kill a process by name.** Only your own, by CommandLine or Path. Never touch
    another session's in-flight files. Never revert or mutate the live tree: differentials
    run in a worktree at a short path (`~/wt-<name>`).
12. **No chatty work over the VPN.** Never content-grep OneDrive; never `-Recurse` over
    SMB; probes run on the target machine; copy data local first ("PULL THE DXFs LOCAL").
13. **Bash heredocs containing backslash-letter sequences are blocked** (`\n`, `\K`,
    Windows paths). Use the Edit and Write tools for code, XAML and anything with a path or
    a regex. A C# regex through a non-raw Python string turns `\b` into a backspace.
14. **`UseWPF=true` drops `System.IO` from implicit usings.** The four UseWPF projects
    carry `<Using Include="System.IO" />` now (commit `de7c0ecb`); Codex still writes files
    without it, so fix the paste rather than sending it back.
15. **Codex is Ian's to run.** I write the brief to `docs/codex/CODEX-<TOPIC>.md` (one
    place, one name, no exceptions, ONE file per brief, never "slices"), bundle 3 to 6 fixes
    per brief (the bound is bytes read, not fix count), hand over, and STOP. When he says
    "applied", I verify: the worktree differential (`verify_gates3_worktree.py` with a
    `gatesN.json` spec: each case reverts or mutates one fixed file and asserts the named
    gate FAILS), filtered tests, then a path-scoped commit. Codex's recurring misses:
    `using System.IO;`, nullable derefs in tests, `FOR JSON` NULL read with `GetString`.
16. **Instruments ship as C# or a KorStandards row, never scripts in `docs/`.** PowerShell
    is prototype only; port to C#.
17. **"See SS"** means the newest file in `C:\Users\ilalonde\Pictures\Screenshots\`. Read it
    before answering.
18. **Memory hygiene.** `MEMORY.md` is an index, one line per memory, ~17.8 KB now; it must
    not grow into a document. Correct a stale memory the moment it misleads (two were
    corrected this week: "one defect per Codex prompt", and "the local install is Ian's").
19. **Name the thing plainly.** "Clarity is King." Every list says what it is and what a
    click does; every input says what it is for; a developer's caveat ("(stored offset)") is
    not UI text; "Removed" is the one word for a dismissed record.
20. **Report like a colleague.** Lead with the outcome. Numbers in a table or on their own
    line, as X of Y. If something could not be verified, say so first. Finish the whole
    task; what is left out is named, with why.

## 3. The loop that worked

**Before:** read the artifact Ian gave you (the screenshot, the log line, the report).
Grep for prior art and say what you searched. Measure the population with one SQL file.
Run the render harness for the screen.

**Change:** Edit/Write tools. Every change carries a gate in the test project that states
what it covers, what it does not, and one same-class fault it would miss. Behaviour that the
old gates pinned (a string format, a column) gets its gate rewritten, not deleted.

**Verify:** the filtered tests; the PNGs, read; `tools/BdIntegrityCheck` when data rules
change (Errors 0, Warnings 39, CheckFailures 0 at the 2026-10-13 close); the installed DLL's
strings after a release.

**Ship:** path-scoped `git add`, commit with attribution, local release, relaunch the app
(it closes during the mirror). Worker deploy only when Worker code changed.

**Report:** what changed, what the pictures showed, what is still wrong and whose it is.

## 4. The BD workspace as it stands (commit `415b8d5e`)

### Pages and files

| Page | What it shows now | Files |
|---|---|---|
| Dashboard | Funnel row (Reachable Now / In Bidding / Watching, each clicks into the Pipeline window on that stage); **Closing soon** (next 30 days, buyer · province · local closing time, countdown pill, double-click opens the RFP); **New RFPs** (latest 15); Priority Actions collapsed with a count. **Dismissed records are excluded from both lists.** Two DB reads. | `BusinessDevelopment/Workspace/DashboardView.xaml(.cs)`, `DashboardViewModel.cs` |
| RFP window | Read view opened by double-click anywhere: fields, "What the posting says" (longest description across sources), source postings grid (hidden when empty), "Open source posting", "Edit record…" (the editor is the deliberate second step). A removed record opens with a red line: who removed it, when, why. Sizes to content. | `Opportunities/OpportunityDetailWindow.xaml(.cs)`, `OpportunityDetailViewModel.cs` (`Build` is pure) |
| Pipeline window | Collapsed "Overview" expander: four tiles and four charts behind one `ChartBarTemplate`; a bar click filters the list. Money shown only for CAD-valued rows. | `Opportunities/PrimePipelineWindow.xaml`, `PrimePipelineViewModel.cs`, `PipelineCharts.cs` |
| Relationships | Default dossier pane = "Architects without a settled structural partner" (double-click → OrgDossierWindow). | `Workspace/RelationshipsView.xaml`, `RelationshipsViewModel.cs` |
| Market History | Third tab "Competitor watch". | `Opportunities/CompetitionInfoView.xaml`, `CompetitionInfoViewModel.cs` |
| Pursuits (CRM) | Grid titled "Pursuits"; every input has a placeholder; contact and activity input rows are two lines that fit the pane; owners show "ilalonde", not the UPN; Updated is "Sep 9, 2026". | `Crm/CrmView.xaml(.cs)`, `CrmViewModel.cs`, `CrmEngagementRowView.cs` |
| Pursuit health (Overwatch) | "Every live pursuit, coldest first"; cold = 21 days, watch = 7 to 20 (`ColdDays`, `WatchDays` constants); columns widened. | `Workspace/OverwatchView.xaml`, `OverwatchViewModel.cs`, `OverwatchRowView.cs` |
| Shared | `Controls/Placeholder.cs` (attached `Placeholder.Text`, adorner-drawn, hidden while focused, `RefreshAll` for the harness); `BusinessDevelopment/StaffDisplay.cs` (`Short("ilalonde@…")` → `ilalonde`); `Opportunities/OpportunityDeadlineDisplay.cs` (`Date` = local time in words, `Stored` = wall clock + offset for the editor and tooltips, `Countdown` by instant). | |

Sidebar labels ("My Pursuits", "All Pursuits", "Engagements") were renamed on 2026-10-09;
deep-link aliases live in `BdWorkspaceWindow.NavigateByKey`. Untouched and large:
`OrgDossierView.xaml` (1,138 lines, 25 columns), `BdTrackingView.xaml` (332 lines).
`IBdUiOpenStore.RecordOpen(page, actor)` exists and CrmView calls it, so **page usage may
be measurable**: grep for `RecordOpen(` callers and query its table before calling any page
unused (Attribution, Overwatch, Brochures, Proposals are the suspects, unmeasured).

### Gates (all in `Kor.Transmittals.App.Tests/BusinessDevelopment/`)

`BdDashboardClarityRegressionTests` (four blocks, moved blocks have one home, only live
records listed, no "(stored offset)"), `BdCrmClarityRegressionTests` (placeholders on every
inline input, titles, deep-link aliases), `OpportunityDetailRegressionTests` (picking rules,
removed record, read view before editor), `BdPresentationRegressionTests` (one deadline
format in every consumer, briefs), `BdWriteBoundaryRegressionTests` (grab/claim SQL guards,
Admin toggles), `BdScreensRenderTests` (`Speed=Slow`; PNGs). Brief gates from the audits
are beside them.

### Data facts, measured 2026-10-14 on the live database

| Fact | Value |
|---|---|
| Open prime RFPs (deadline future or unknown) | 125 |
| …of which dismissed | 9 |
| …live with zero observations | 0 of 116 |
| Who dismissed, whole table | BdOpportunityDedup 421 · migration-321 20 · migration-327 1 · humans 0 |
| Prime RFPs closing in 30 days before the fix | 20, of which 6 were retired duplicates |
| Live duplicate pairs still showing (same posting under `BCBID-` and `BCBIDENG-` keys) | 2: Whitehorse General Hospital (234895 / 233883), SE02 patio (234526 / 234526) — the dedup tool's to retire, not the UI's |
| Deadlines still at midnight UTC with no zone evidence | 17 |

Schema you will touch: `opportunities.Opportunities` (`Status` New=1, Pursuing=4,
Submitted=5, Won=6, Lost=7; `DismissedAtUtc/DismissedBy/DismissedReason`;
`IsPrimeConsultantRfp`; `RelevanceTier` HardReject=0, Low=1, Medium=2, High=3;
`SubmissionDeadlineUtc` is a datetimeoffset whose offset is PER SOURCE),
`opportunities.OpportunityObservations` (one row per posting: `OpportunityId`,
`OpportunitySourceId`, `Title`, `Url`, `Description`, `PostedDateUtc`, `IngestedAtUtc`),
`opportunities.OpportunitySources`, `opportunities.CrmEngagements` (`CrmEngagementStage`
Drafting=1, Submitted=3, Won=6, Lost=7). `IOpportunityStore.ListAsync(includeClosed:false)`
adds `Status NOT IN (6,7)`; `includeNonPrime:false` adds `IsPrimeConsultantRfp = 1`; **it
does not exclude dismissed rows; every caller must.**

### This week's commits, newest first

`415b8d5e` render harness and what it showed · `33e49616` retired duplicates leave the
lists, local-time deadlines, removed banner · `e2d1b8da` CRM clarity · `42f643e6` dashboard
four blocks · `9becb3f4` RFP read view · `e3a340ec` brief 7 (exports and money) ·
`6cad8575` brief 6 (populations) · `0cd7b070` brief 5 (writes that skip the check) ·
`da1a8d4c` second adversarial audit (15 findings) · `3df392b8` billed-period measured, not
asserted · `dd61083a` brief 4 · `3e1256a7`, `dff95638` the bash-guard hooks · `8ae3efd1`
audit residue (verdict cases, abandoned-run sweeper, San Diego year token, prime-research
relabel) · `de7c0ecb` System.IO. Both BD audits are closed; briefs 1 to 7 are verified.

## 5. Helpers that live OUTSIDE the repo

They are in the previous session's scratchpad, which survives until Temp is cleaned:
`C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\dd5e3d5d-ec22-4052-96ba-a2511c574af2\scratchpad\`

- `oq.py` / `oqwide.py` (45 lines): parse `KOR_OPPORTUNITIES_OPPORTUNITIESDB` (the password
  key arrives with an EMPTY name in that variable), run `sqlcmd` with `-i file` or `-q`,
  pass extra args through (`-v Apply=1`). Copy them into the new scratchpad; do not put the
  credentials anywhere else. If gone, rebuild from that description.
- `verify_gates3_worktree.py` + `gates4.json` … `gates7.json`: the worktree differential
  for Codex pastes. Each case `{kind: revert|mutate, file, gate}`; the worktree is
  `~/wt-<name>`; the live tree is never touched.
- `parse_synth_sql.py`: compiles a C# SQL constant with `SET NOEXEC ON`.
- `rfp_234361_probe.sql`: the six-section probe that answered "who dismissed it" and
  measured the dashboard population; a template for the next population question.

## 6. Ian, in his own words

"Should I trust you? The answer is no. So how do we move forward constructively?" (the
answer was hooks, not promises). "I DON'T RUN PUBLISHES. YOU DO!!!" "DEPLOY IT LOCALLY FFS."
"Why do I need to ask for this??" "Clarity is King." "Fuck sakes - neverending fixes" (the
answer was the render harness, not the next patch). He wants the fixer, not the menu:
decide, do it, show the picture, say what is left.

## 7. First actions in the new session

1. `git status --short` and `git log --oneline -5`; confirm `415b8d5e` or later is HEAD.
2. Copy `oq.py`, `oqwide.py` and the gates scripts from the old scratchpad into the new one.
3. Run `BdScreensRenderTests`; read the five PNGs; note anything that still reads wrong.
4. Ask Ian nothing until you have done 1 to 3. Then take his list of changes, plan in chat
   in one message (what, where, which gate, which PNG proves it), and build.
