# Module Audit — FileSync (service + Command Center)

**Auditor pass:** 2026-08-20 · **Rubric:** `docs/audit-2026-08/RUBRIC.md`

---

## 1. What I searched

**Repo (read):**
- `Kor.Operations.FileSync.Service/` — all 58 `.cs` (Program, Worker, ControlPlane, Scheduling,
  Logging, Options, Alerting, Jobs/*), `.csproj`, `appsettings*.json`.
- `Kor.Operations.App/FileSync/` — all 17 `.cs`, in particular `FileSyncControlPlaneReader.cs`,
  `FileSyncLogParser.cs`, `FileSyncLogTailer.cs`, `FileSyncLogViewerViewModel.cs`, `FileSyncRows.cs`.
- `Kor.Operations.App/Scripts/20260501_filesync_control_plane.sql`, `20260807_filesync_kormapsync.sql`.
- `publish.ps1`, `install-service.ps1`, `uninstall-service.ps1`, `set-filesync-env.ps1`,
  `set-filesync-env-server.ps1`, `monitor-drains.ps1`, `docs/runbooks/`.
- Prior art per repo rule 1: read `docs/audit-2026-08/{SCOPE,00-INVENTORY,01-DOC-TRUST,02-CROSS-CUTTING-SCAN}.md`
  **before** running anything; the 8 FileSync hardcoded-path hits and the "no hardcoded credentials in C#"
  claim come from that scan and are re-triaged (not re-scanned) in §5.

**Greps:** `JobLogTail` (0 hits in any `.cs`/`.xaml`), `TODO|FIXME|HACK|NotImplementedException` in both
paths, `catch` counts per file, `FileSyncCommandCenter` (launch + role gate), `UploadSimple|Threshold`
in `Jobs/Watcher/`, `CREATE TABLE FileSync` across `*.sql`.

**Git:** `git log` per file for `Kor.Operations.App/FileSync/*` and `Kor.Operations.FileSync.Service/**`;
`git ls-tree -d 3b5150eb`; `git merge-base --is-ancestor`; `git check-ignore -v`; `git ls-files --error-unmatch`.

**Builds / tests run:** `dotnet build Kor.Operations.FileSync.Service -c Debug` (clean, 0 warnings, 1.4 s).
PowerShell 7 **and** Windows PowerShell 5.1 `Parser::ParseFile` on `install-service.ps1` and `publish.ps1`.
Regex-parity harness: the exact `FileSyncLogParser.EntryHead` pattern applied line-by-line to three real
production log files.

**Live, read-only:**
- `Get-CimInstance -ComputerName KOR-APP01 Win32_Service` (filter `%Kor%`) and `Win32_Process` for uptime.
- `\\KOR-APP01\C$\Program Files\KorOperations\FileSync\` — file times + `VersionInfo`.
- `\\KOR-APP01\C$\ProgramData\KorOperations\FileSync\logs\filesync-2026081{8,9},20260820.log` — read via
  `File.Open(..., FileShare.ReadWrite)`.
- Remote registry `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` — enumerated
  `KOR_*` names + lengths; read two values to build the SQL connection.
- SQL `SELECT`-only against `KorTransmittals` on `KOR-APP01\SQLEXPRESS` (via the service's own
  `transmittals_app` connection string): `FileSync.Jobs`, `JobKnobs`, `JobRuns`, `JobTriggers`,
  `ServiceHeartbeat`, `JobLogTail`. **No writes of any kind. Service never touched.**

Not done: nothing was deployed, restarted, rotated, or written. `Get-ChildItem -Recurse` was never
issued against a share.

---

## 2. What this module is

FileSync is the firm's unattended back-office robot. It is a .NET 8 Windows service
(`Kor.Operations.FileSync`, running as `KOR\app-admin` on KOR-APP01) that replaces a pile of PowerShell
Scheduled Tasks that used to move engineering documents between the file server
(`\\KOR-FS01\Projects\Projects`), SharePoint, Deltek and people's inboxes. Seven jobs are registered.
Six are scheduled; one — the Watcher — is a long-running `FileSystemWatcher` that reacts within seconds
of a file landing on the share. Everything it does is recorded in a SQL **control plane**
(`KorTransmittals.FileSync.*`): one row per job, one row per run, one row per manual trigger, plus a
60-second heartbeat. There is no HTTP API — the database *is* the IPC channel.

The human-facing half is the **FileSync Command Center**, a WPF window inside `Kor.Operations.App`,
reached from a Home-screen tile gated on the AD group `FileSyncCommandCenter`. It shows a host-health
panel (mode, version, uptime, jobs registered, seconds since heartbeat), a job grid with a human-readable
schedule and next-fire countdown, per-job run history with duration and summary, an editable
name/value "knobs" panel per job, a Shadow/Live toggle, a **Fire now** button that queues a trigger the
service picks up within 5 seconds, and a log viewer that tails the service's Serilog file off the
`C$` admin share. Every job supports **Shadow mode** — it computes exactly what it *would* do and writes
the plan to `%ProgramData%\KorOperations\FileSync\shadow\<Job>\` without touching anything. That is the
single best thing in this module and the thing to lead with in a demo.

**What each job does, one line each** `[READ + QUERIED live cron/mode]`:

| job | business meaning |
|---|---|
| `Watcher` | Real-time: a drawing, photo, RFI or stickfile saved into a project folder on KOR-FS01 is mirrored to that project's SharePoint folder within seconds. This is the Newforma Info Exchange replacement's file half. |
| `MoveReportsToEor` | 1st @ 00:00 — pushes each project's `Reports/` folder into the Engineer-of-Record's `_FIELD REVIEWS TO INITIAL` SharePoint folder so the EOR can review and initial field-review reports. |
| `MoveReportsToToSend` | 5th @ 08:00 — pulls EOR-acknowledged reports back to the file server into `04 Construction Admin\01 Inspection Reports\To Send`, ready to issue to the client. The 5-day gap is the EOR's ack window. |
| `RenameReportsUploads` | Nightly @ 23:30 — normalises filenames that people uploaded into `<project>/Reports/` so the monthly movers can parse them. |
| `ConcreteTestReports` | 1st @ 00:30 — reads the CTR mapping workbook on `\\KOR-FS01\Library\ADMIN\Concrete Test Reports`, files each lab PDF to its project, emails each PM. |
| `WeeklyPmDeadlines` | Mondays @ 05:00 — reads `Project Deadlines.xlsx` (Deltek export) and emails each PM their week's deadlines. |
| `KorMapSync` | Daily @ 03:00 *in the DB* — reads Deltek over ODBC, geocodes new project addresses, pushes the pin set to the korstructural.com project map. **Does not actually fire on that cron — see §5.1.** |

---

## 3. How you would demo it

**Prerequisites:** on the KOR LAN or VPN; signed in as a member of AD group `FileSyncCommandCenter`;
**local administrator on KOR-APP01** (the log viewer reads `\\KOR-APP01\C$\...`); `Kor.Operations.App`
installed. Service is already running — nothing to start.

1. Launch `Kor.Operations.App` → Home → **FileSync Command Center** tile.
2. Host panel shows one row: `KOR-APP01 · 1.0.12.0 · 7 jobs · WatcherGen 1 · heartbeat <60 s ago`.
   `[QUERIED]` this is live and correct right now.
3. Job grid shows all 7 jobs with human-readable schedules and next-fire countdowns.
4. Click a job → **Job Detail**: run history ribbon, last-run summary, knobs. `Watcher` is the good one —
   it has 600 successful runs in 30 days with real summaries (`local=…  uploaded=…  deleted=…`).
5. **The set-piece:** flip a job to `Shadow`, press **Fire now**, watch the run appear in ≤5 s, open the
   shadow output folder and show the plan file it wrote instead of moving anything. Flip back to `Live`.
   This is a genuinely strong "we don't guess in production" story.
6. **Do not open the Log Viewer on today's date.** See §5.2 — over SMB it displays an empty log while the
   service is actively writing. Selecting *yesterday* works correctly.

**Demoing off the KOR LAN (at MVE's office):** the Command Center needs SQL 1433 to
`KOR-APP01\SQLEXPRESS` and SMB 445 for logs. Over VPN it works; over MVE's guest wifi without VPN it is
dead. Screenshot or record this one in advance.

---

## 4. Completeness

| capability | state | tier |
|---|---|---|
| Windows service installed, running, auto-start, crash-recovery | **WORKING** — `Running`, `Auto`, as `KOR\app-admin`, PID 15540, up **8 d 09 h** since 2026-08-12 13:40:04 | `QUERIED` |
| SQL control plane (Jobs/Knobs/Runs/Triggers/Heartbeat) | **WORKING** — 2,108 runs all-time, 135 in last 7 d, heartbeat fresh | `QUERIED` |
| Watcher real-time SharePoint sync | **WORKING** — 124 ok / 3 failed in 7 d; 600 ok / 5 failed in 30 d | `QUERIED` |
| `RenameReportsUploads` nightly | **WORKING** — 7/7 success in 7 d | `QUERIED` |
| `WeeklyPmDeadlines` weekly | **WORKING** — last success Mon 2026-08-17 05:00 | `QUERIED` |
| `ConcreteTestReports` / `MoveReportsToEor` / `MoveReportsToToSend` monthly | **WORKING** — each last succeeded on its August date (08-01, 08-01, 08-05) | `QUERIED` |
| `KorMapSync` scheduled execution | **PARTIAL** — runner works when fired manually; **never registered with Quartz**, so its daily 03:00 cron has never fired. Last run 2026-08-12 (manual). | `QUERIED` + `READ` |
| Shadow mode (plan-don't-do) on every job | **WORKING** — proven by KorMapSync runs 11780/11781/11783 recording `Mode=Shadow` with plan output | `QUERIED` |
| Failure alert email via Graph | **WORKING** — two alerts sent to ilalonde@ today at 12:49 | `QUERIED` |
| Command Center: heartbeat / jobs / runs / knobs / fire / mode toggle | **WORKING** — SQL contract read-side matches the service write-side column-for-column | `READ` |
| Command Center log viewer, **current day** | **DEAD over SMB** — stale directory length ⇒ shows nothing (§5.2) | `RUN` |
| Command Center log viewer, **previous days** | **WORKING** — parser matched 100 % of entry lines in three real logs | `RUN` |
| `FileSync.JobLogTail` table | **DEAD** — created by the migration, zero references in any `.cs`/`.xaml`, 0 rows live | `RUN` + `QUERIED` |
| Automated tests for the service | **NONE** — no test project exists (§7) | `RUN` |
| Deploy script / runbook | **STUBBED** — `publish.ps1` exists and works; the `deploy.ps1` it tells you to run does not exist, and there is no FileSync runbook | `RUN` |

**Debt markers** `[RUN]` — the service is unusually clean for 6,919 LOC:
`TODO/FIXME/HACK` = **1** (`Logging/CredentialPatterns.cs:2` — "lift these three redaction classes into
Kor.Operations.Core"; cosmetic). `NotImplementedException` / `NotSupportedException` = **0**.
Empty catch blocks = **0** (84 `catch` blocks, every one logs or rethrows).
`Kor.Operations.App/FileSync/` (3,303 LOC): **0** TODO/FIXME/HACK, **0** NotImplemented, **0** empty catch.

---

## 5. What is broken or risky

### 5.1 `KorMapSync` has a cron in the database that Quartz never registered — `BEFORE-DEMO`

`Scheduling/QuartzInstaller.cs:16-86` registers exactly five jobs: WeeklyPmDeadlines,
ConcreteTestReports, MoveReportsToEor, RenameReportsUploads, MoveReportsToToSend. `KorMapSync` is
**absent** — it was added to the DB by `Scripts/20260807_filesync_kormapsync.sql` with
`CronExpression = '0 0 3 ? * *'`, `Enabled = 1`, and (live) `Mode = Live`, but the code that turns a DB
cron into a Quartz trigger is a hardcoded list, not a query. `[READ]`

Live proof `[QUERIED]`: all six `KorMapSync` rows in `FileSync.JobRuns` have `TriggerSource = 'Manual'`.
Zero `Cron`. The last one is `RunId 11903, 2026-08-12 13:40:40`. The public project map on
korstructural.com has therefore not been refreshed from Deltek for **8 days**, and will not refresh again
until someone presses Fire now.

**Why this is the worst one for a demo:** `FileSyncRows.cs:83-113` computes `NextFireAt` client-side by
parsing the DB `CronExpression` with Cronos. The Command Center has no idea what Quartz actually
registered. So the grid will confidently display *"Sync project map to korstructural.com — Daily at
3:00 AM — next fire in 4 h"* for a job that has never fired on a schedule in its life. That is the
"looks healthy, is wrong" failure mode, on screen, with a countdown timer.

Fix is four lines in `QuartzInstaller.cs` mirroring the existing pattern.

### 5.2 The Command Center log viewer shows an empty log for the current day — `BEFORE-DEMO`

`FileSyncLogTailer.cs:67-76` decides whether new data exists using `new FileInfo(path).Length`, and
returns `Array.Empty<FileSyncLogLine>()` when the length has not changed. Windows does not update a
file's directory entry while another process holds it open, so over SMB the *current* log file reports a
frozen size.

Measured on the live server today `[RUN]`:

```
FileInfo.Length  = 0
Stream.Length    = 43,165      (same file, same instant, opened FileShare.ReadWrite)
```

Because the tailer starts at `_lastLength = 0`, `current == lastLen` is true on the first tick and every
tick after: it never opens the stream at all. **The Log Viewer renders a blank grid for today while the
service is writing hundreds of lines.** Yesterday's file (closed by the daily roll) reports its true
length and works fine.

Fix: open the `FileStream` first and use `fs.Length`, which is authoritative — a ~6-line change, and the
tailer already opens with the right share flags.

### 5.3 The log *format* has not drifted — the 3-month gap is not a format gap `[RUN]`

The headline worry going in was that the app, last touched 2026-05-15, no longer understands what the
service, last touched 2026-08-15, emits. **It does.** Verified, not assumed:

- `Logging/SerilogBootstrap.cs` has **one** commit in its entire history — `fbabb559`, 2026-05-01. The
  output template has never changed.
- `ControlPlane/SqlControlPlaneStore.cs` last changed **2026-05-01**; the schema script last changed
  **2026-05-01**. Every column the app's `FileSyncControlPlaneReader` selects exists and is written.
- Parity harness: the exact `FileSyncLogParser.EntryHead` regex against three real production logs —

  | file | entry lines matched | unmatched |
  |---|---|---|
  | `filesync-20260818.log` | 208 / 208 | 4 (all exception/stack continuation lines — by design folded into `Details`) |
  | `filesync-20260819.log` | 93 / 93 | 0 |
  | `filesync-20260820.log` | 130 / 130 | 30 (all continuation lines) |

  Zero entry-head lines failed to parse. `SourceContext` is emitted unquoted, exactly as the regex assumes.

The gap between the two commit dates is real but benign: the service grew a seventh **job**, and the
control plane is data-driven, so the UI picked it up with no code change. What the UI did *not* pick up
is that nobody taught Quartz about it (§5.1).

### 5.4 The Watcher fails on two concrete, reproducible file conditions — `SOON`

3 failures in 7 days, 5 in 30. Alert emails did fire. Causes, from the logs `[RUN]`:

**(a) Filenames with a leading space.** `filesync-20260820.log`, two entries at 12:49:05 and 12:49:11:

```
[ERR] ...WatcherSyncRunner Upload failed ' 31056-01 2026-08-21 10th & Highbury Issued for Draft IFC.pdf'
      -> '31056-01 (3803 W 10th Ave Vancouver)'
Microsoft.Graph.Models.ODataErrors.ODataError: Invalid request
```

Note the leading space in the filename. Graph rejects it. Nothing in `BucketSyncOp` trims or sanitises
`f.Name` before upload, so this file has failed on every pass since it appeared and will keep failing
forever. Same signature on 31039-01, 31084-01, 31053-01 in earlier months — this is a recurring class,
not a one-off.

**(b) A stale `FileInfo.Length` sends large files down the wrong upload path.**
`BucketSyncOp.cs:118-121` chooses simple-PUT vs. chunked upload from `f.Length`, where `f` comes from a
`.ToList()` snapshot (`BucketSyncOp.cs:~60`) taken *before* several Graph round-trips. `FileInfo.Length`
is cached at first access. If the file grows in between — exactly what a multi-phase Bluebeam/Newforma
save does, and the reason `FileStabilitySleepMs` was already bumped to 2500 — the wrong branch is taken:

```
filesync-20260818.log:
System.InvalidOperationException: UploadSimpleAsync called with a 14,665,819-byte file (max 4,194,304).
   at Kor.Operations.Graph.GraphFacade.UploadSimpleAsync(...)
   at Kor.Operations.FileSync.Service.Jobs.Watcher.BucketSyncOp.RunAsync(...)
```

Fix: `f.Refresh()` immediately before the size test. One line. (Same root cause as §5.2 — cached length
metadata — in a completely different place.)

### 5.5 A failed run records the *summary*, not the *cause* — `SOON`

`Scheduling/JobDispatcher.cs:115` — when a runner returns `Success = false` it wraps
`result.Summary` in an `InvalidOperationException`, so `FileSync.JobRuns.ErrorMessage` reads
`"Synced bucket=Stickfile root='…' local=2 remote=1 uploaded=0 skipped=1 deleted=0 failed=1 deferred=0"`.
That tells an operator *that* one file failed and nothing about *which* or *why*. The real cause is only
in the log file — which, per §5.2, the Command Center cannot show for the current day. The two defects
compound: **the UI can tell you a job failed today and give you no way to find out why.**

### 5.6 The heartbeat panel says "Shadow" while all seven jobs run Live — `SOON`

`KOR_FILESYNC_MODE` on KOR-APP01 is `Shadow`, and `appsettings.Production.json` on the server also says
`Shadow`. The heartbeat row therefore reports `GlobalMode = Shadow` `[QUERIED]`. But `JobDispatcher`
uses `config.Mode` from the `FileSync.Jobs` row, and **all 7 rows are `Live`** `[QUERIED]`. The global
setting is decorative. On screen this reads as "the whole service is in dry-run" while it is in fact
moving client files. Either honour it, remove it, or relabel the column.

### 5.7 Hardcoded paths — re-triage of the 8 flagged by `02-CROSS-CUTTING-SCAN.md`

The cross-cutting scan listed 8 path literals under `Jobs/`. Read in context, most are not defects:

| location | verdict |
|---|---|
| `ConcreteTestReportsOptions.cs:18,19,21` | **Legitimate.** `public const string Default*`, overridable by `FileSync.JobKnobs`. Real UNC share paths that exist. |
| `MoveReportsToEorOptions.cs:24`, `MoveReportsToToSendOptions.cs:22,27`, `WatcherOptions.cs:27` | **Legitimate.** Same `Default*` + knob-override pattern. |
| `Watcher/WatcherHostedService.cs:45` | **False positive.** `@"\\Newforma\\email($|\\)"` is a *regex*, not a path. |
| `WeeklyPmDeadlinesOptions.cs:21` | **Genuine defect.** `C:\Users\app-admin\KOR - Structured Engineering\Kor Hub - Deltek Connection\Project Deadlines.xlsx` — a per-user OneDrive profile path. It works only under the `app-admin` profile on KOR-APP01, and only while OneDrive is signed in and syncing under that profile. I confirmed **no knob overrides it** — `FileSync.JobKnobs` holds 19 rows, none for `WeeklyPmDeadlines` `[QUERIED]`. A silent OneDrive sign-out breaks the Monday PM emails with no other symptom. |

### 5.8 Where the secrets actually live, and how well protected `[QUERIED]`

The cross-cutting scan's "no hardcoded credential in ~1,300 C# files" is **true and holds for FileSync** —
`FileSyncOptions` has 13 properties bound from `KOR_FILESYNC_*` environment variables, and `Program.cs:47-56`
fails fast at startup naming any that are missing. Startup logs a redacted 4-character tail of each so an
operator can confirm a rotation landed. `Logging/CredentialRedacting*.cs` scrubs secrets out of log output.
That is a good design and worth saying so.

Where the values live at runtime: **machine-scope environment variables in the registry** on KOR-APP01
(`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`). I enumerated 13 `KOR_FILESYNC_*`
values — all present, including the four `KorMapSync` credentials. They are stored **in plaintext, not
DPAPI-protected**; anyone with administrative access to KOR-APP01 can read them.

Two caveats worth recording:

- The same values exist in plaintext in `set-filesync-env.ps1` and `set-filesync-env-server.ps1` in the
  **repo root** on the developer workstation. Both are correctly excluded from version control
  (`git check-ignore -v` → `.gitignore:73: set-filesync-env*.ps1`; `git ls-files --error-unmatch` fails on
  both). **They are not in git history.** But they sit unencrypted in the folder you would screen-share,
  and the User-scope variant puts the Entra client secret on a workstation as well as the server.
- I verified by exact comparison (without printing either) that the live `KOR_FILESYNC_CLIENTSECRET` and
  `KOR_FILESYNC_KORTRANSMITTALSDB` on KOR-APP01 are **byte-identical** to the values in those scripts. The
  SQL password is still the literal placeholder string shipped with the template — it was never changed.

### 5.9 The deployed binary does not correspond to any commit — `SOON`

`Kor.Operations.FileSync.Service.exe` on KOR-APP01 is `1.0.12+3b5150eb971f0d45a804848b6106db8dbaff7810`,
file-stamped 2026-08-12 13:38 `[QUERIED]`. Commit `3b5150eb` is dated 2026-08-09 and
`git ls-tree -d 3b5150eb -- Jobs/` shows **no `KorMapSync` directory** `[RUN]` — yet the running service
executed KorMapSync successfully on 2026-08-12 and reports `JobsRegistered = 7`. The `KorMapSync` source
was not committed until `ce6202a4` on 2026-08-15.

Conclusion: the 2026-08-12 publish was built from a **dirty working tree**, so the git hash baked into
the binary names a commit that does not contain the code that is running. There is no way to prove what
is on the server matches anything in the repository. Cheap fix: have `publish.ps1` refuse to publish, or
loudly warn, when `git status --porcelain` is non-empty.

### 5.10 `publish.ps1` ends by telling you to run a script that does not exist — `SOON`

`publish.ps1:90` prints ``Next: .\deploy.ps1 -Source "$out"``. There is no `deploy.ps1` anywhere in the
repo (`Test-Path` → `False` `[RUN]`). See §6 for what the deploy actually is.

---

## 6. Dependencies

| dependency | detail | reachable off the KOR LAN? |
|---|---|---|
| **SQL Server** `KOR-APP01\SQLEXPRESS`, database **`KorTransmittals`**, schema `FileSync` | The control plane and the app-to-service IPC channel. Login `transmittals_app`, granted `SELECT/INSERT/UPDATE/DELETE ON SCHEMA::FileSync` only — never DDL. | **No** — needs LAN/VPN. Note `ilalonde` has *no* rights on this DB; only the service login does. |
| **Microsoft Graph** (app-only) | SharePoint file upload/delete/list via `DriveId`; failure-alert mail via `Users[app-admin].SendMail`. `Kor.Operations.Graph.GraphFacade`. | Yes (internet). |
| **SMB `\\KOR-FS01\Projects\Projects`** | The Watcher's watch root; source/target for all four report jobs. Server-local path is `E:\Projects\Projects`. | **No.** |
| **SMB `\\KOR-FS01\Library\ADMIN\Concrete Test Reports`** | CTR source, mapping workbook, `_Processed` output. | **No.** |
| **SMB `\\KOR-APP01\C$`** | How the Command Center reads logs (`FileSyncLogViewerViewModel.cs:289`). Requires **local admin on KOR-APP01**, not just LAN access. | **No.** |
| **Deltek ODBC** (DSN `Deltek`, catalog `C0000052267P_1_KOR00000000`) | `KorMapSync` only, read-only. | **No.** |
| **Mapbox geocoding API** | `KorMapSync`, capped at 400 geocodes/run. | Yes (internet). |
| **korstructural.com** (WordPress) | `KorMapSync` push target, authenticated with `KOR_FILESYNC_KORSYNCSECRET`. | Yes (internet). |
| **OneDrive under the `app-admin` profile** | `WeeklyPmDeadlines` reads `Project Deadlines.xlsx` from it — an undeclared dependency (§5.7). | **No.** |
| **.NET 8 runtime on KOR-APP01** | Publish is framework-dependent (`--self-contained false`). | n/a |

No licensed desktop software. No AI provider. Nothing in FileSync calls an LLM — and there is a test
(`FileSyncExcludedFromAiTests.cs`) that deliberately keeps it that way.

**Deploy path (item 6 of the brief) — publishing is not deploying** `[READ` + `QUERIED` on the result]:

1. **From the developer workstation (KOR-1001):** `.\publish.ps1` → `dotnet publish -c Release
   -r win-x64 --self-contained false` into a timestamped folder under
   `C:\VIsual Studio Projects\_Publish\_Ops\FileSync\<yyyyMMdd_HHmmss>\`, keeping the last 3. It copies
   `install-service.ps1` / `uninstall-service.ps1` into the output and writes `publish-info.json`
   (`GitCommit`, `GitBranch`, timestamp). **This step ships nothing to any server.**
2. **The actual deploy is manual and undocumented.** There is no `deploy.ps1`, and `docs/runbooks/`
   contains runbooks for the MCP server and the Newerforma app but **none for FileSync**. The evidence on
   the server (all four DLL/EXE files stamped within 3 seconds of each other at 2026-08-12 13:38, while
   `appsettings.Production.json` still carries its original 2026-05-01 stamp) is consistent with a
   `robocopy` of the publish folder into `C:\Program Files\KorOperations\FileSync\` that skips config —
   i.e. the same stop-service / robocopy / start-service pattern the other KOR services use. **I could
   not verify the exact command; it exists only in the owner's head.** The check that would confirm it:
   `Get-WinEvent -ComputerName KOR-APP01 -FilterHashtable @{LogName='System'; Id=7036} -MaxEvents 200 |
   Where-Object Message -match 'FileSync'` for the stop/start pair around 2026-08-12 13:38.
3. **On the server, as admin:** run `install-service.ps1` from
   `C:\Program Files\KorOperations\FileSync\`. It prompts for the `KOR\app-admin` password (never stored),
   registers the service as Automatic-Delayed-Start, sets crash recovery (5 s / 30 s / 60 s, counter reset
   daily), sets an unrestricted per-service SID, creates `logs\` and `shadow\` under ProgramData and ACLs
   them to `NT SERVICE\Kor.Operations.FileSync`. Only needed on a fresh install or a service-definition
   change, not on every binary update. It parses cleanly under both PowerShell 7 and Windows PowerShell
   5.1 `[RUN]`.

---

## 7. Test reality

**There is no test project for `Kor.Operations.FileSync.Service`.** `[RUN]` — the solution has five
`*.Tests.csproj` (EngineeringTools ×2, App, Mcp, Opportunities.Data); none references FileSync.
6,919 lines of production code that moves client documents between a file server and SharePoint, deletes
remote files, and sends email on behalf of the firm, with **zero automated coverage**.

The only FileSync-named test in the repository is
`Kor.Operations.App/Kor.Transmittals.App.Tests/FileSync/FileSyncExcludedFromAiTests.cs` — a single `[Fact]`
asserting that `FileSyncCommandCenterViewModel` does **not** implement `IAiContextProvider`. It is a
genuinely useful architectural guard (it prevents FileSync data leaking into AI prompts) but it is not
functional coverage of anything.

This is coverage theatre by absence rather than by padding. Nothing tests: the Serilog↔parser contract
(§5.3 — I had to build that harness by hand for this audit), the control-plane SQL contract, cron→Quartz
registration (which is exactly where §5.1 hides), `ShouldUpload` skew logic, the upload-path size
threshold (§5.4b), knob parsing and defaults, or trigger claim/recovery races.

I did not run the App test suite — per `AGENTS.md` and the rubric, WPF app tests hang headless and the
full suite takes 10–14 minutes. What I did run: `dotnet build` on the service in Debug — **clean, 0
warnings, 0 errors** with `TreatWarningsAsErrors=true` and StyleCop analyzers active. The code quality
is high; the verification is absent.

The three cheapest tests that would have caught real bugs, in order:
1. Assert every `Enabled` row in `FileSync.Jobs` with a non-null `CronExpression` has a matching Quartz
   trigger key → catches §5.1.
2. Feed a checked-in sample log through `FileSyncLogParser` and assert entry/continuation counts →
   locks the contract in §5.3.
3. Write a file, take a `FileInfo`, grow the file, assert the upload branch still routes correctly →
   catches §5.4b.

---

## 8. Demo risk — ranked

1. **`KorMapSync` shows a live countdown to a fire that will never happen.** If anyone asks "so what
   runs at 3 a.m.?" the honest answer is "nothing." Worse if the MVE lead asks to see the map and it is
   8 days stale. *(§5.1)*
2. **Opening the Log Viewer on today's date shows a blank grid.** The single most likely spontaneous
   action during a live demo — "show me the logs" — produces an empty window on a healthy service. It
   looks broken and it is very hard to explain gracefully in the moment. *(§5.2)*
3. **The health panel says `Shadow` while everything is `Live`.** A technical lead reading that column
   will either think nothing is really running, or catch the inconsistency and ask what else the UI is
   telling them wrong. *(§5.6)*
4. **A red `Failed` row on `Watcher` dated today, whose error text looks like a success message.**
   `Synced bucket=Stickfile … failed=1` in the *Error* column invites "what failed?" and the UI cannot
   answer. *(§5.4, §5.5)*
5. **"Do you have tests?"** — for this module the answer is no, and it is the module that deletes files
   from SharePoint. Have the Shadow-mode story ready as the compensating control, because it is a real
   one. *(§7)*
6. **Demoing remotely.** Off the KOR LAN the Command Center cannot reach SQL or the log share and the
   window is inert. Needs VPN or a pre-recorded walkthrough. *(§6)*
7. **"How do you deploy it?"** — there is no runbook and `publish.ps1` points at a nonexistent
   `deploy.ps1`. A low-stakes question with an embarrassing answer. *(§5.10, §6)*
8. **Screen-sharing the repo root.** `set-filesync-env*.ps1` are sitting there with a live Entra client
   secret and a SQL password in plaintext. Correctly gitignored, but visible in any folder listing or
   editor sidebar. *(§5.8)*

---

## 9. To-do register

| item | size | tag | why it matters |
|---|---|---|---|
| Register `KorMapSync` with Quartz in `QuartzInstaller.cs` (mirror the existing 5-job pattern) | S | `BEFORE-DEMO` | The map is 8 days stale and the UI shows a countdown that is fiction. Four lines. |
| Fix `FileSyncLogTailer` to use `fs.Length` instead of `FileInfo.Length` | S | `BEFORE-DEMO` | "Show me the logs" is the likeliest ad-hoc demo request and it currently returns a blank grid. |
| Either honour `KOR_FILESYNC_MODE` or relabel the heartbeat `GlobalMode` column | S | `BEFORE-DEMO` | The panel currently states the opposite of the truth about production behaviour. |
| Clear or triage the 3 open `Watcher` failures so no red row is on screen (the leading-space PDF on 31056-01) | S | `BEFORE-DEMO` | Removes the "what failed?" question the UI cannot answer. Rename the source file; do not change code under time pressure. |
| Close/hide `set-filesync-env*.ps1` before any screen-share of the repo root | S | `BEFORE-DEMO` | Live client secret + SQL password visible in a folder listing. |
| Trim/sanitise filenames (leading & trailing whitespace) before Graph upload in `BucketSyncOp` | S | `SOON` | Recurring permanent-failure class — same signature on four projects over three months. |
| `f.Refresh()` before the size test at `BucketSyncOp.cs:118` | S | `SOON` | Large files silently never reach SharePoint when a save is still in flight. |
| Put the real exception, not the run summary, into `JobRuns.ErrorMessage` (`JobDispatcher.cs:115`) | S | `SOON` | Today a failure is undiagnosable from the Command Center. |
| Rotate the SQL password off the shipped placeholder `‹REDACTED — the unmodified scaffold placeholder shipped by the project template›`; rotate the Entra client secret | M | `SOON` | Placeholder credential in production. Do this **after** the demo — rotating a Graph secret on a two-week runway is its own risk. |
| Make `publish.ps1` refuse (or loudly warn) on a dirty working tree | S | `SOON` | The deployed binary currently names a commit that does not contain the code it is running. |
| Write `docs/runbooks/Kor.Operations.FileSync.deploy.md` and the missing `deploy.ps1` | M | `SOON` | The deploy exists only in the owner's head; `publish.ps1` points at a script that isn't there. |
| Move `WeeklyPmDeadlines` `ExcelPath` to a knob (or a UNC path) | S | `SOON` | Removes a hidden dependency on OneDrive being signed in under one profile. |
| Create `Kor.Operations.FileSync.Service.Tests` with the three tests listed in §7 | M | `SOON` | Each one catches a defect found in this audit. |
| Drive Quartz registration from the `FileSync.Jobs` rows instead of a hardcoded list | M | `LATER` | Makes §5.1 structurally impossible to repeat for job #8. |
| Drop `FileSync.JobLogTail`, or implement it and stop tailing over `C$` | M | `LATER` | Dead table; implementing it would also make the log viewer work off-LAN. |
| Lift the credential-redaction classes into `Kor.Operations.Core` (`CredentialPatterns.cs:2`) | S | `LATER` | The module's only TODO. |

---

## 10. Verdict

**Demo-able with care — and it deserves to be demoed.** FileSync is the most operationally mature module
I have looked at: it has been running unattended on KOR-APP01 for 8 days straight, it has 2,108 recorded
runs, a 97.8 % success rate over the last 7 days (135 runs, 3 failures, all the same Watcher edge case),
zero empty catch blocks in 6,919 lines, a real Shadow/Live safety story, and failure alerts that
demonstrably landed in the owner's inbox today. The much-feared 3-month UI/service drift **is not real**:
I verified the log format and the SQL contract against live artefacts, and both have been frozen since
2026-05-01 while the service grew a seventh job that the data-driven UI absorbed for free.

The two things that will hurt on screen are small and both are "the UI states something untrue while
looking healthy": `KorMapSync` advertises a daily 3 a.m. cron that Quartz was never told about, and the
log viewer shows an empty file for the current day because it trusts a stale SMB directory length. Both
are a few lines each and both are `BEFORE-DEMO`.

**The single most important thing to fix: register `KorMapSync` with Quartz** (`QuartzInstaller.cs`).
It is the newest, most demo-friendly job — Deltek to a live public map — it is set to `Live` and
`Enabled` in the database, and it has not run on its own since 2026-08-12. Everything else on the screen
is telling the truth; that row is not.
