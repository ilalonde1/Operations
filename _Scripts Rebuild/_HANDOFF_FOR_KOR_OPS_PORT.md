# FileSync → KOR Ops Migration — Handoff Brief

**Audience:** the next Claude Code session, on Ian's dev box, working in the **KOR Ops** .NET solution.
**Author:** prior Claude Code session, on the production server `KOR-FS01`-adjacent host (the box at `C:\_APPS\FileSync\Production`).
**Generated:** 2026-05-01.
**Companion document:** `FileSync_Analysis_Report_2026-05-01.docx` (in the same folder) — the formatted human-readable version of the initial deep analysis. This brief is the technical addendum.

---

## 0. Read this first

You have been opened in the KOR Ops .NET solution to **rebuild a rickety production PowerShell tool-chain into a robust .NET service**. The PS1 scripts in this folder are the **source-of-truth behavioral spec** — they are not to be ported syntactically. They encode ~6 months of production race-condition fixes, magic numbers, and SharePoint quirks; you are translating *intent*, not *code*.

Your first move should be:

1. Read this file end-to-end.
2. Skim `FileSync_Analysis_Report_2026-05-01.docx` for the executive summary, runtime-status table, and prioritized bug list.
3. Read `watcher.ps1` and one of the `SINGLE_SYNC_*` scripts (Stickfile is the most evolved) to ground the spec in real code.
4. Then ask Ian which migration step he wants to start. The agreed order is in §12.

**Critical constraint from Ian (verbatim, prior session):**
> "Again, this is all production stuff so I can't mess it up. Would have to be very surgical."
> "I'm not worried about client secrets and security credential hardening right now."

Translate that to: don't propose big-bang rewrites; favor evidence; preserve subtle production behavior; skip security cleanup unless asked. Ian is production-paranoid in a good way. Confirm before touching anything that affects shared systems.

---

## 1. Project context

KOR Structural Engineering uses a Windows file server (`\\KOR-FS01\Projects\Projects`) as the canonical location for project data. Inspectors and engineers add PDFs and photos there. Some of those files need to appear in a SharePoint site (the **ActiveProjects** site) so external parties (EORs — Engineers of Record), PMs, and the office can interact with them.

The current production system is a stack of PowerShell scripts that:

- **Watch** the file server in real time and one-way-sync four specific subfolder types into the matching SharePoint location (per project).
- **Run periodically** (Windows Scheduled Tasks) to do month-end and weekly housekeeping: move reports between SharePoint locations, copy them back to the file server, normalize filenames, email PMs/EORs.

It's working but brittle. Recent incidents:

- **Mar–Apr 2026 scheduled-task blackout (~9 weeks of missed runs).** Root cause confirmed by Ian: the user account that owns the scheduled tasks (Ian's account) had its password rotated and the tasks ran under stale credentials. There are **no failure notifications configured**, so no one noticed. The tasks scripts themselves are fine.
- **Silent data-loss-risk bug in `SINGLE_SYNC_Photos.ps1`** (see §11) that has been latent since deployment.
- **Slow temp-dir leak in `Move_Reports_To_EOR.ps1`** (1.36 GB / 1,496 files accumulated since 2025-10; cleared 2026-05-01 but the underlying bug is still present).

**Goal of the rebuild:** move every responsibility off this PowerShell + Scheduled-Tasks combo and into KOR Ops as a proper hosted .NET service with structured logging, missed-run alerting, centralized config, one Graph client, and proper retry/refresh.

---

## 2. Architecture overview

The PS1 system has **three logical layers**.

### Layer A — Real-time per-project sync ("hot path")

`watcher.ps1` is a long-running PowerShell host. It registers a `System.IO.FileSystemWatcher` on `\\KOR-FS01\Projects\Projects` (recursive) and reacts to `Created` / `Changed` / `Renamed` / `Deleted` events. For each event it determines whether the path is inside one of four "interesting" subfolder patterns under a project, and if so, launches a per-folder helper script as a **separate `powershell.exe` process** with a 15-minute hard timeout.

```
\\KOR-FS01\Projects\Projects\<Category>\<Project>\
    04 Construction Admin\
        02 SSI (Structural Site Instructions)\        → SINGLE_SYNC_SSI.ps1       (pdf)
        03 RFI (Request for Info)\Sent to Inspectors\ → SINGLE_SYNC_RFI.ps1       (pdf)
        07 Photos\                                    → SINGLE_SYNC_Photos.ps1    (image)
    05 Stickfile\                                     → SINGLE_SYNC_Stickfile.ps1 (pdf)
```

Plus a **gating** mechanism via a control file `NOT SYNCED TO ACTIVE PROJECTS.txt`:
- File appears anywhere in the project ancestry → `SINGLE_CLEAN_Projects.ps1` runs (moves the SharePoint project folder to `_ToBeDeleted`).
- File is removed from a project → all four helpers run fresh on that project (initial seed).

### Layer B — Bulk scripts (legacy / dead)

`_SYNC_SSI.ps1`, `_SYNC_RFI.ps1`, `_SYNC_Photos.ps1`, `_SYNC_Stickfiles.ps1`. Each iterates *every* project under `\\KOR-FS01\Projects\Projects` and syncs everything. **Their log files were last written 2025-04-28/29 — over a year ago. They are not in use.** They were superseded when the watcher went live. They use PowerShell-7-only `ForEach-Object -Parallel { … $using:Headers }` which makes them incompatible with Windows PowerShell 5.1 anyway.

`Paths.txt` is Ian's personal cheat-sheet of one-shot commands he's run. It is **not loaded by anything** and references stale paths (`C:\Ian\FileSync\Production\…` no longer exists).

You may safely treat all of Layer B as dead reference material. Ian acknowledged this and said pruning is for later.

### Layer C — Periodic (Scheduled Task) jobs

| Script | Cadence (intent) | Purpose (one line) |
|---|---|---|
| `Move_Reports_To_EOR.ps1` | 1st of month @ 00:00 | Move each project's `Reports/` PDFs to the matching EOR's SP folder; email each EOR. |
| `Move_Reports_To_ToSend.ps1` | After EOR ack (manual or scheduled?) | Pull EOR-acknowledged reports back to the file server. |
| `RenameReportsUploads.ps1` | Daily (?) | Normalize SharePoint `<project>/Reports/` filenames to `NNNNN-NN CRM YYYY-MM-DD Report NN.<ext>`. |
| `Send-Weekly-PM-Deadlines.ps1` | Mondays @ 05:00 | Read `Project Deadlines.xlsx`; email each PM their week's deadlines. |
| `RenameReportsDate.ps1` | (legacy) | Older variant of the rename script using `NNNNN-NN-CRM-YYYY-MM-DD-Report NN` (dash-separated) — superseded. |

---

## 3. Source tree on production

```
C:\_APPS\FileSync\Production\
├── watcher.ps1                          ← main runtime (Layer A)
├── watcher_old.ps1                      ← legacy
├── watcher_working.ps1                  ← legacy
├── SINGLE_SYNC_SSI.ps1                  ← Layer A helper
├── SINGLE_SYNC_RFI.ps1                  ← Layer A helper
├── SINGLE_SYNC_Photos.ps1               ← Layer A helper (HAS BUG, §11.1)
├── SINGLE_SYNC_Stickfile.ps1            ← Layer A helper (most evolved; has retry)
├── SINGLE_CLEAN_Projects.ps1            ← Layer A cleaner (control-file ADD)
├── _SYNC_SSI.ps1                        ← legacy bulk (Layer B, dead)
├── _SYNC_RFI.ps1                        ← legacy bulk (Layer B, dead)
├── _SYNC_Photos.ps1                     ← legacy bulk (Layer B, dead)
├── _SYNC_Stickfiles.ps1                 ← legacy bulk (Layer B, dead)
├── Move_Reports_To_EOR.ps1              ← Layer C (HAS LEAK, §11.9)
├── Move_Reports_To_ToSend.ps1           ← Layer C
├── RenameReportsUploads.ps1             ← Layer C (current)
├── RenameReportsUploads_working.ps1     ← legacy
├── RenameReportsDate.ps1                ← legacy
├── Send-Weekly-PM-Deadlines.ps1         ← Layer C
├── Paths.txt                            ← Ian's scratch notes (ignore)
├── FileSync_Analysis_Report_2026-05-01.docx   ← prior-session analysis
├── _HANDOFF_FOR_KOR_OPS_PORT.md         ← THIS FILE
└── Logs\
    ├── Watcher_Log.txt                  ← active (HEARTBEAT every 5 min)
    ├── Single_SSI_Sync_Log.txt          ← active
    ├── Single_RFI_Sync_Log.txt          ← active
    ├── Single_Photos_Sync_Log.txt       ← active (but bug → mostly "Photos to sync: 0")
    ├── Single_Stickfile_Sync_Log.txt    ← active
    ├── Single_Project_Clean_Log.txt     ← active
    ├── Move_Reports_Log.txt             ← month-end runs
    ├── Move_Reports_ToSend_Log.txt      ← post-ack runs
    ├── Rename_Uploads_Log.txt           ← rename runs
    ├── Weekly_Deadlines_Send_Log.txt    ← Monday emails
    ├── SSI_Sync_Log.txt                 ← STALE (Apr 2025) — Layer B
    ├── RFI_Sync_Log.txt                 ← STALE (Apr 2025) — Layer B
    ├── Photos_Sync_Log.txt              ← STALE (Apr 2025) — Layer B
    └── Stickfile_SYNC_Log.txt           ← STALE (Apr 2025) — Layer B
```

Working directory referenced by every script: paths are absolute, hard-coded to `C:\_APPS\FileSync\Production\Logs\`. The watcher uses `\\KOR-FS01\Projects\Projects` as `$watchPath`.

---

## 4. Authentication and Microsoft Graph

**Same single Entra app reg is used by every script.** Identifiers are repeated verbatim in each script (§11.7 in the analysis docx flags this as a duplication-risk; Ian explicitly deferred that).

```
TenantID: d9be1f7f-aacf-461a-8d1b-5528b86d540f
ClientID: 5b20a407-0b59-4c75-b2e5-d2cf970c5dbd
SiteID:   e197528f-6707-4dd5-afec-04964a94c294    (ActiveProjects site)
DriveID:  b!j1KX4Qdn1U2v7ASWSpTClCkgewh88axOppiZwdiZiLrmnMMBC2KqRKuvmOcSYyYA
```

**ClientSecret** is hardcoded in 13 scripts in plaintext. Read it from any of them when porting; for KOR Ops, it goes into your secret store (Key Vault, user-secrets, whatever the existing pattern is). Ian said don't make the migration about secret hardening, but the *new* code should not duplicate the secret.

**Auth pattern (every script):**
```
POST https://login.microsoftonline.com/{TenantID}/oauth2/v2.0/token
  grant_type=client_credentials
  scope=https://graph.microsoft.com/.default
```
Token is cached in a global with a 60-second pre-expiration buffer:
```
$global:TokenExpiration = (Get-Date).AddSeconds($TokenResponse.expires_in - 60)
```

**Graph endpoints used:**
- `GET    /v1.0/sites/{site}/drives/{drive}/root:/{path}` — folder existence test
- `POST   /v1.0/sites/{site}/drives/{drive}/root:/{parent}:/children` — create folder
- `GET    /v1.0/sites/{site}/drives/{drive}/root:/{path}:/children` — list
- `PUT    /v1.0/sites/{site}/drives/{drive}/root:/{path}:/content` — small upload (<3.5 MB)
- `POST   /v1.0/sites/{site}/drives/{drive}/root:/{path}:/createUploadSession` — large upload (used in Photos)
- `PUT    {uploadUrl}` (with `Content-Range`) — chunked upload (5 MB chunks)
- `DELETE /v1.0/sites/{site}/drives/{drive}/items/{id}` — delete by item id (preferred)
- `DELETE /v1.0/sites/{site}/drives/{drive}/root:/{path}` — delete by path (fallback)
- `PATCH  /v1.0/drives/{drive}/items/{id}` `{ name }` — rename
- `PATCH  /v1.0/drives/{drive}/items/{id}` `{ parentReference: { id } }` — move
- `POST   /v1.0/$batch` — batch delete (used in legacy `_SYNC_*.ps1`; not in current SINGLE_ helpers)
- `GET    /v1.0/sites/{site}/drives/{drive}/items/{id}/content` — download
- `POST   /v1.0/users/{from}/sendMail` — Graph mail (from `ilalonde@korstructural.com`, with Ian as CC)

**Pagination:** the legacy and Move_Reports scripts use `@odata.nextLink` follow-loops; the SINGLE_SYNC_* scripts ask `$top=200` and don't paginate (see §11.10).

**TLS:** several scripts force `[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12`. In .NET 6+ this is automatic, ignore.

---

## 5. SharePoint target structure

The SharePoint drive root contains **one folder per project**, named exactly the same as the file-server folder:

```
{drive root}/
├── 01797-01 (1111 West Georgia LM Sites Sign Design)/
│   ├── SSI/         ← from \\KOR-FS01\…\02 SSI (Structural Site Instructions)
│   ├── RFI/         ← from \\KOR-FS01\…\03 RFI (Request for Info)\Sent to Inspectors
│   ├── Photos/      ← from \\KOR-FS01\…\07 Photos
│   ├── Reports/     ← inspection reports (mobile-app uploads, before rename)
│   └── *.pdf        ← Stickfile PDFs (NOT in a subfolder; uploaded to project root)
├── 30461-17 (4080 Bayview St Richmond Anytime Fitness)/
│   └── …
├── _Archived/                            ← excluded from all sweeps by name
├── _ToBeDeleted/                         ← target of SINGLE_CLEAN_Projects (control-file gate)
├── _FIELD REVIEWS TO INITIAL/
│   ├── EOR.csv                           ← project# → EOR-last-name mapping
│   ├── CatchAll/                         ← unmatched projects land here
│   ├── Atkinson/
│   ├── DesRoches/
│   ├── Markulin/
│   ├── Wurmlinger/
│   ├── Beirne/
│   ├── Pastrana/
│   └── Zickmantel/
└── (other site assets)
```

**Note on Stickfile destination:** unlike the others, Stickfile PDFs are placed at the **project root** on SharePoint (`<Project>/`), not in a subfolder. The `SINGLE_SYNC_Stickfile.ps1` ensures both `<Project>` and `<Project>/Reports` exist (Reports is created proactively because the mobile app uploads land there).

**Project folder naming on file server (must be parsed):**
```
NNNNN-NN (Free-form Project Name)
^^^^^ ^^   ^^^^^^^^^^^^^^^^^^^^^^^
  |   |    project name (anything, may contain ' & ', commas, periods, apostrophes, em-dashes)
  |   sub-number (always 2 digits)
  base project number (always 5 digits)
```

Examples: `30461-17 (4080 Bayview St Richmond Anytime Fitness)`, `60061-03 (Hyatt Place Phase 3 Richmond Redesign)`, `31091-01 (Burke Mountain – Parcel PC-33, Coquitlam)`.

Regex used in scripts: `^[0-9]{5}-[0-9]{2}` (start anchor; sometimes followed by space-then-paren, sometimes just space).

**Project category (parent dir on file server):** `01 Small Jobs`, `03 Residential`, `04 Commercial`, `05 Office`, `06 Hotel`, `07 Industrial-Garage`, `08 Inst-Rec-Church`, `09 ...`. The SharePoint drive does **not** have category folders — projects are flat at the root.

---

## 6. File naming conventions

### Inspection reports (after `RenameReportsUploads.ps1`)

```
NNNNN-NN CRM YYYY-MM-DD Report NN.<ext>
^^^^^^^^     ^^^^^^^^^^         ^^
  |              |               |
  project       date (createdDateTime)  counter (zero-padded, per project)
  number
```

Example: `31077-01 CRM 2026-04-30 Report 07.pdf`

Older legacy variant (used by `RenameReportsDate.ps1`, no longer applied to new files):
```
NNNNN-NN-CRM-YYYY-MM-DD-Report NN.<ext>
```

`RenameReportsUploads.ps1` is forgiving:
- **Case A:** if the filename already matches the new format but the date is wrong, it rewrites the date to `createdDateTime`'s `yyyy-MM-dd`.
- **Case B:** anything else gets the standard pattern with `counter = max(existing) + 1`, where existing counters are scanned from current folder contents.

### Stickfile PDFs

No enforced format from these scripts; filenames are user-set. Watcher only filters by `.pdf` extension. Examples seen:
- `31155-01 2026-04-30 3066-3086 Gladwin Rd. Bldg 4 Stickfile.pdf`
- `40117-01 - 2026-05-01 - 6871 - 153 St - Struct. BLDG 2 Stickfile.pdf`

### Control files (gates)

| File name | Location | Effect |
|---|---|---|
| `NOT SYNCED TO ACTIVE PROJECTS.txt` | Anywhere in project ancestry on file server | Add → run `SINGLE_CLEAN_Projects.ps1`; remove → run all four helpers (`Trigger-ProjectSync`). |
| `Acknowledge and Move To Server <Month>.txt` | Inside an EOR folder on SharePoint | Created by `Move_Reports_To_EOR.ps1`. While present → `Move_Reports_To_ToSend.ps1` skips that EOR. EOR deletes it to acknowledge → next ToSend run pulls files back to file server. |

### Ignored extensions / prefixes (watcher)

```
imageExts:         .jpg .jpeg .png .heic .bmp .tif .tiff
ignoredExts:       .tmp .bak .log .rws .dat .dwgtmp
ignoredNamePrefix: ~$, pulse-, n4newforma-
ignoredDirRegex:   \\Newforma\\email($|\\)
```

---

## 7. The Watcher — full behavioral spec

**This section is the most important part of this brief.** When you build the watcher in .NET, every magic number and every special case below is there because of a real production incident. Don't lose them.

### 7.1 Initialization

- `New-Watcher` creates a `FileSystemWatcher` with `IncludeSubdirectories = $true`, `InternalBufferSize = 65536`, `NotifyFilter = FileName | DirectoryName | LastWrite`.
- A **generation counter** (`$script:watcherGen`) increments on each (re)bring-up. Useful in logs.
- After `New-Watcher`, `Register-WatcherEvents` registers `Created`, `Renamed`, `Deleted`, `Changed`, **and `Error`** handlers under `SourceIdentifier`s `FSW.*`.
- A separate `System.Timers.Timer` runs the **lock poller** every `$LockPollSeconds = 10`.

### 7.2 Main loop

```
while ($true) {
    $evt = Wait-Event -Timeout 2     # 2 s blocking wait
    if ($evt) {
        if ($evt.SourceIdentifier -eq 'FSW.Error') {
            Restart-WatcherWithBackoff   # share dropped — wait up to 300 s for it
        } else {
            $script:lastRealEventAt = Get-Date
            Process-Event ...
        }
        Remove-Event -EventIdentifier $evt.EventIdentifier
    }
    if (5 minutes since last heartbeat) { log HEARTBEAT; check liveness }
}
```

**Liveness nudge:** if no real file events for **24 hours**, cycle the watcher. This catches "watcher is alive but quietly stopped delivering events" — a real failure mode of `FileSystemWatcher` over SMB.

**Restart-WatcherWithBackoff:** polls `Test-Path $watchPath` every 5 s for up to 300 s (5 minutes). When the share is back, `New-Watcher` + `Register-WatcherEvents`. Logs gen advance.

### 7.3 Per-event processing (`Process-Event`)

Order of checks:

1. Reject if dir matches `ignoredDirRegex`.
2. **Control-file branch:** if filename equals `NOT SYNCED TO ACTIVE PROJECTS.txt`:
   - On `Deleted` or `Renamed` (file gone): determine project dir, call `Trigger-ProjectSync` which fires all four helpers with `-Force -IgnoreControl`.
   - On `Created` / `Changed` / `Renamed` (file present): launch `SINGLE_CLEAN_Projects.ps1 -GivenPath <projectDir>` as a separate process (no waiting, fire-and-forget).
   - Return.
3. **Resolve target:** `Resolve-Target` does a lowercased substring match on the path against the four patterns and returns `@{ Script; Root; Kind }` or `$null`. **Match is on `\<pattern>` (with leading backslash) to avoid false positives.**
4. **Exact-root rule:** the event's directory must equal the resolved `Root` exactly (no subfolders). This prevents events deep inside `05 Stickfile\some-subfolder\…` from triggering the project-level sync.
5. **Filter:** drop if extension is in `ignoredExts`, or filename starts with one of `ignoredNameStarts`, or extension doesn't match `Kind` (pdf vs image).
6. **Deleted event:** call `Try-Run -Force` immediately and return.
7. **Created/Changed/Renamed event:** call `Test-FileUnlockedStable`. If unlocked-and-stable: run sync now AND register a **PostRun** entry in `$global:LockedFiles` with `ExpireAt = now + 120s` and `PostRun = $true`. If currently locked: register a normal lock entry (no run yet).

### 7.4 `Test-FileUnlockedStable` (the stability check)

```
1. Try open with [System.IO.File]::Open(path, 'Open', 'ReadWrite', 'None')   # exclusive
   - if it throws: file is locked → return $false
   - else close immediately
2. len1 = (Get-Item).Length
3. Sleep 1000 ms
4. len2 = (Get-Item).Length
5. return (len1 -eq len2)
```

**Known issue (§11.4 of analysis):** the 1-second sleep is too short for large multi-page PDFs being written by Newforma — the helper can fire while the writer is still in mid-flush, causing "process cannot access the file" errors in the helper's upload step. Recommend bumping to 2–3 s in the .NET version, AND keep the PostRun retry as a safety net.

### 7.5 The lock poller

Runs every 10 s. For each entry in `$global:LockedFiles`:

- **PostRun entries** (set when sync ran successfully — covers save-after-close races):
  - If `now > ExpireAt`: drop entry, log `POSTRUN drop (expired)`.
  - If file exists and `Test-FileUnlockedStable`: run sync again, drop entry. Log `POSTRUN verify -> invoking sync …`.
  - If file is gone: run sync (delete propagation), drop entry.
- **Normal locked entries** (set when an event arrived but file was locked):
  - If older than 12 h: drop, log `LOCK drop (stale > 12 h)`.
  - If file gone: run sync (deletion case), drop.
  - If `Test-FileUnlockedStable`: run sync, drop.

Why both kinds of entries: the **PostRun** entry catches the very common case where Newforma writes the file in two phases (initial write + reorganization), the first sync runs successfully on the partial file, then 30–90 s later the writer finishes and modifies the file again without ever firing a `Changed` we caught in time. The PostRun re-run picks that up.

### 7.6 `Try-Run` (helper invocation)

```
key = "$root|$scriptPath"
if key in $script:inflight: log COALESCE skip; return
if (not -Force) and (key in $script:recent within 5 s): log DEBOUNCE skip; return

mark recent[key] = now
add inflight[key]
log RUN $scriptPath -GivenPath "$root" (timeout 15 m)

start powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$scriptPath" -GivenPath "$root"
wait with stopwatch up to 15 m, polling 5 s
if process didn't exit: kill it, log ERROR exceeded
else log DONE / ERROR with exit code

remove key from inflight
```

Both `inflight` (case-insensitive HashSet) and `recent` (case-sensitive Dictionary, key-format above) are critical. The COALESCE catches "user saves three SSIs in 5 seconds" — only the first run actually executes; the second/third skip because still in-flight; the lock-poller / post-run paths re-fire if needed afterwards.

### 7.7 Magic numbers (preserve these as named config)

| Constant | Value | Reason |
|---|---|---|
| `MaxSyncMinutes` | 15 | Hard timeout per child run; Stickfile uploads can be slow on large PDFs. |
| `DebounceSeconds` | 5 | Per-folder/per-script. Filters duplicate FSW events for the same save. |
| `LockPollSeconds` | 10 | Poller cadence. Balances responsiveness vs CPU/log noise. |
| `MaxLockTrackHours` | 12 | Drop unlock-never-happened entries (user closed editor without saving). |
| `PostRunRetrySeconds` | 120 | Window in which the post-run re-verify fires. Tuned for Newforma's two-phase saves. |
| `Test-FileUnlockedStable` sleep | 1000 ms | **Too short — bump to 2000–3000 ms in .NET.** |
| Heartbeat interval | 5 min | Just for log breadcrumbs. |
| Liveness threshold | 24 h | Cycle watcher if no events for this long. |
| Restart backoff max wait | 300 s | Polls share for 5 minutes after FSW.Error. |
| Image upload chunk size | 5 MB | Used in `SINGLE_SYNC_Photos.ps1` for files >3.5 MB. |
| Simple-vs-chunked threshold | 3,670,016 bytes (~3.5 MB) | Below this, single PUT; above, upload session. |
| Photos `$top` | 200 | Pagination size. **Doesn't paginate — bug §11.10.** |

### 7.8 Observable event amplification

A typical user save of one stickfile PDF produces these FSW events:

```
EVT Created   (temp file)
EVT Deleted   (move-to-recycle of previous version)
EVT Created   (final filename)
EVT Changed   (close-after-write)
```

The watcher logs *each* of these. Combined with PostRun, that means a single save typically fires 2–3 actual sync invocations. The COALESCE/DEBOUNCE absorb most duplicates but every successful run is a full Graph round-trip. In .NET, consider a smarter event aggregator (250–500 ms debounce window, with the same lock-stability + post-run gates).

### 7.9 Error / abort cases observed in production

- `WATCHER ERROR: System.ComponentModel.Win32Exception - The specified network name is no longer available` — share dropped. Self-heal fired and worked. Happens when the file server reboots or has an SMB hiccup.
- `ABORT: root does not exist` — event arrived for a folder whose parent has already been deleted/moved. Harmless; helper would have nothing to do.
- `LOCK drop (stale > 12 h)` — user opened a file, never saved, never closed (or editor crashed). Correct cleanup.
- `COALESCE skip (in-flight)` — multiple events in rapid succession. Working as intended.

---

## 8. The four sync helpers

All share the same shape:
```
param([string]$GivenPath)
1. Get token
2. Parse $GivenPath → projectName, build SP destination path
3. Ensure SP folder exists (with Test-then-Create, idempotent)
4. List local files (filter by extension)
5. List SP files at destination (size and timestamp)
6. Compute upload set (new + size mismatch [+ newer mtime for Stickfile])
7. Upload each (small PUT or chunked upload session for Photos)
8. Compute delete set (SP files not in local set, scoped to relevant extension)
9. Delete each
10. Log sync complete
```

### 8.1 `SINGLE_SYNC_SSI.ps1` (`02 SSI (Structural Site Instructions)`)

- Parses: `ProjectDir = parent.parent of $GivenPath` (skip "04 Construction Admin").
- SP target: `<Project>/SSI`
- Filter: `*.pdf` only
- **No upload retry** on transient locks → 263 ERROR lines in `Single_SSI_Sync_Log.txt`. (Bug §11.3.)

### 8.2 `SINGLE_SYNC_RFI.ps1` (`03 RFI (Request for Info)\Sent to Inspectors`)

- Parses: `ProjectDir = parent.parent.parent of $GivenPath` (skip "04 Construction Admin" and "03 RFI…").
- SP target: `<Project>/RFI`
- Filter: `*.pdf` only
- **No upload retry.**
- **Latent bug §11.5:** does not ensure project root folder before subfolder.

### 8.3 `SINGLE_SYNC_Photos.ps1` (`07 Photos`)

- Parses: `ProjectDir = parent.parent of $GivenPath`.
- SP target: `<Project>/Photos`
- Filter: image extensions
- Has chunked upload (5 MB chunks via `createUploadSession`) for files >3.5 MB.
- **No upload retry.**
- **CRITICAL BUG §11.1**: `Get-ChildItem -LiteralPath $GivenPath -File -Include $ImageExtensions` returns 0 items always (because `-Include` is silently ignored without `-Recurse` or path-glob). This script has been functionally non-operational since deployment. The prune block would also delete every SP photo if a project ever does have SP-side photos.
  - Fix shape (PS): `Get-ChildItem -Path "$GivenPath\*" -File -Include $ImageExtensions` or drop `-Include` and filter manually.
  - In .NET: just enumerate by extension explicitly.

### 8.4 `SINGLE_SYNC_Stickfile.ps1` (`05 Stickfile`)

- Parses: `ProjectDir = parent of $GivenPath`.
- SP target: **`<Project>` (root)**, NOT a subfolder. Stickfile PDFs land at the project's SharePoint root level.
- Also ensures `<Project>/Reports` exists (because mobile-app uploads go there).
- Filter: `*.pdf` only
- **HAS upload retry** via `Invoke-UploadWithRetry` — 4 tries, 750 ms backoff, specifically for "being used by another process" / "process cannot access the file" errors. **This is the pattern to copy into the SSI/RFI/Photos helpers when porting.**
- **HAS smarter "should upload" logic** via `Should-Upload`: skips if size matches AND local mtime is not newer than SP `fileSystemInfo.lastModifiedDateTime` + 15-second skew tolerance. Avoids re-upload storms after timestamp jitter.
- **HAS UTF-8 BOM logging** with explicit `Monitor.Enter/Exit` lock — the only helper that does this. The others use `Out-File -Append` which can produce mixed encodings.
- Best-organized of the four. Use as the reference for shape.

### 8.5 `SINGLE_CLEAN_Projects.ps1`

- Triggered by control-file ADD/CHANGE.
- Locates the project folder at the SP drive root by name (or by 5-2 project number prefix). Uses `?$top=200` — does not paginate.
- Creates `_ToBeDeleted` at the SP drive root if missing.
- `PATCH /items/{id}` with `parentReference.id` = `_ToBeDeleted.id` → moves the project folder.
- Has a **per-project mutex** (`Global\CLEAN_<targetName>`) to prevent duplicate invocations from the watcher coalescing imperfectly.
- On 409 conflict (name already in `_ToBeDeleted`), retries with a timestamped rename: `<name> (archived <yyyyMMdd_HHmmss>)`.

---

## 9. The four periodic jobs (Layer C)

### 9.1 `Move_Reports_To_EOR.ps1`

**Cadence:** 1st of month at 00:00 (Scheduled Task on this server).

**Flow:**
1. Read `_FIELD REVIEWS TO INITIAL/EOR.csv` from SharePoint. CSV columns: `ProjectNumber`, `EOR` (last name only).
2. List `_FIELD REVIEWS TO INITIAL/` children (paginated). Build a word-keyed lookup: any word in any folder name → list of folder names containing that word. So "DesRoches" can be matched whether the folder is `DesRoches`, `Jim DesRoches`, or `DesRoches Reports`.
3. List drive root (paginated). For each folder matching `^[0-9]{5}-[0-9]{2}` (skip `_Archived`):
   - Look up project's EOR by number from CSV.
   - Resolve EOR-last-name → SP folder name via the word-lookup. If not found, use `CatchAll`.
   - List `<project>/Reports/` (paginated). For each non-folder file:
     - Download to `C:\_APPS\FileSync\Temp\<filename>` (uses `Join-Path`).
     - Upload to `_FIELD REVIEWS TO INITIAL/<EOR>/<filename>`.
     - Delete the original from `<project>/Reports/`.
     - **BUG §11.9: temp file is never removed.** 1.36 GB / 1,496 files accumulated since 2025-10. Cleared 2026-05-01; underlying script unchanged.
     - On first successful upload per EOR: create control file `Acknowledge and Move To Server <Month>.txt` in the EOR folder (skipped for `CatchAll`).
4. Write audit CSV: `\\KOR-FS01\Projects\Reporting\Number Of Reports\Move_Reports_Audit_<MM-yyyy>.csv`.
5. For each EOR with files moved, send Graph mail (to EOR, cc `ilalonde@korstructural.com`).

**EOR email map (verbatim):**
```
Jeremy Atkinson       → jatkinson@korstructural.com
Conor Murtagh         → cmurtagh@korstructural.com
Jim DesRoches         → jdesroches@korstructural.com    ← note: "Jim" here, "James" in PM map (§11.8)
John Markulin         → jmarkulin@korstructural.com
John Zickmantel       → admin@korstructural.com
Kevin Wurmlinger      → kevinw@korstructural.com
Omar Alcazar Pastrana → omara@korstructural.com
Rory Beirne           → rbeirne@korstructural.com
```

### 9.2 `Move_Reports_To_ToSend.ps1`

**Cadence:** Manual / not currently on a schedule? (logs show ~monthly cadence with manual catchup runs.)

**Flow:**
1. Enumerate file-server categories under `\\KOR-FS01\Projects\Projects` (skipping anything starting with `00*`).
2. List `_FIELD REVIEWS TO INITIAL/` EOR folders.
3. For each EOR folder, GET the control file `Acknowledge and Move To Server <CurrentMonth>.txt`.
   - **If 404** → control file is gone → EOR has acknowledged → process this folder.
   - If 200 → control file still there → skip this folder.
4. List files in EOR folder. For each file with name matching `^[0-9]{5}-[0-9]{2}` (skip control-file leftovers from prior months):
   - Parse `projectNum = filename.Substring(0,8)`.
   - Find the project folder on file server by category-walk + `Name -like "$projectNum*"`.
   - Compute target: `\\KOR-FS01\…\<Project>\04 Construction Admin\01 Inspection Reports\To Send`. Create if missing.
   - Download from SP to `C:\temp\ToSend\<filename>`.
   - Copy to target (`Copy-Item`).
   - Delete from SP.
   - **`Remove-Item $tempFile` — does** clean up properly. (Pattern to copy into 9.1.)
5. Write audit CSV; email summary to `admin@korstructural.com` (cc Ian).

### 9.3 `RenameReportsUploads.ps1`

**Cadence:** appears to run frequently (Rename_Uploads_Log was 1.0 MB on 2026-04-30 — many runs).

**Flow:**
1. Paginate drive root, filter to `^[0-9]{5}-[0-9]{2}` folders (skip `_Archived`).
2. For each `<project>/Reports/`:
   - Paginate children.
   - Compute `maxCounter` from filenames already matching the new format.
   - For each file:
     - **Case A:** if filename matches `^<projectNumber> CRM \d{4}-\d{2}-\d{2} Report \d{2}\.<ext>$` and the date portion ≠ `createdDateTime.ToString('yyyy-MM-dd')`: PATCH name to fix the date.
     - **Case B:** otherwise: PATCH name to `<projectNumber> CRM <createdYYYY-MM-DD> Report <maxCounter+1>.<ext>`. Increment counter.

`$TestMode` flag at the top — when `$true`, just logs `[TEST MODE] Would rename …`.

`RenameReportsDate.ps1` is a legacy version using dash separators. Don't port it.

### 9.4 `Send-Weekly-PM-Deadlines.ps1`

**Cadence:** Mondays @ 05:00 (Scheduled Task).

**Source:** `C:\Users\app-admin\KOR - Structured Engineering\Kor Hub - Deltek Connection\Project Deadlines.xlsx`, sheet `Projects_Deadlines`. This is a OneDrive/SharePoint-synced Excel pulled from Deltek.

**Read mechanics:** copies the xlsx to `$env:TEMP\WeeklyDeadlines_Project Deadlines.xlsx` first (defeats SharePoint placeholder/lock issues), opens with Excel COM (`New-Object -ComObject Excel.Application`), reads `UsedRange.Value2` as a 2D array, returns rows as PSCustomObjects keyed by header. Falls back to `CorruptLoad=1` repair mode if normal open fails.

**Columns used:** `PM`, `CustDateExpected`, `WBS1`, `CustIssue`, `CustRemarks`.

**Filtering:**
- Skip rows where `IgnorePMs` matches the PM name (currently `Adrian Crowder`, `John Zickmantel`).
- Convert `CustDateExpected` from OADate (double) or string → `[datetime]`.
- Compute Mon→Sun week starting from today's Monday.
- Keep rows with date in window.

**Sending:** group by PM; for each PM, build an HTML table (Date, Project, CustIssue, CustRemarks); send via Graph mail from `ilalonde@korstructural.com` with `ilalonde@korstructural.com` as global CC. Subject template: `Weekly Project Deadlines for <PM> (Week of <Mon d> - <Mon d, yyyy>)`.

**PM email map (verbatim):**
```
Andrea Neuviale       → andrean@korstructural.com
Conor Murtagh         → cmurtagh@korstructural.com
Griffin Dow           → gdow@korstructural.com
James DesRoches       → jdesroches@korstructural.com    ← note: "James" here, "Jim" in EOR map (§11.8)
Jason Stuart          → jstuart@korstructural.com
Jeremy Atkinson       → jatkinson@korstructural.com
John Bryson           → jbryson@korstructural.com
John Markulin         → jmarkulin@korstructural.com
Katherine Reid        → kreid@korstructural.com
Kevin Wurmlinger      → kevinw@korstructural.com
Omar Alcazar Pastrana → omara@korstructural.com
Rory Beirne           → rbeirne@korstructural.com
```

`-WhatIf` parameter: when set, logs `WHATIF send From=… To=… Subj=…` instead of sending.

---

## 10. Operational health snapshot (as of 2026-05-01 11:30)

- **Watcher:** alive, gen advancing, HEARTBEAT every 5 min, no recent errors.
- **Single_SSI_Sync_Log:** 263 lifetime ERROR lines (race-condition uploads). No retry helper present → these are dropped uploads picked up only on the next file event.
- **Single_Stickfile_Sync_Log:** few/no ERROR lines (retry catches the race).
- **Single_Photos_Sync_Log:** "Photos to sync: 0" on every run — confirms §11.1 bug.
- **Move_Reports (EOR):** 2026-05-01 run completed cleanly, 70+ files moved, 5 EOR emails sent.
- **Move_Reports (ToSend):** last meaningful run 2026-04-15.
- **RenameReportsUploads:** running normally, 2026-04-30 23:31 last entry.
- **Send-Weekly-PM-Deadlines:** 2026-04-27 ran cleanly. **Previously dark from 2026-03-02 to 2026-04-20** (8 missed Mondays, ~9 weeks). **Root cause confirmed by Ian: his account password rotated; tasks were running as him; no failure notifications were configured.** Tasks now working again.

**FileSync\Temp:** emptied 2026-05-01 (1,496 files / 1.36 GB removed). Underlying leak in `Move_Reports_To_EOR.ps1` is unfixed.

---

## 11. Bugs and risks (full list, prior-session findings)

Cross-references match `FileSync_Analysis_Report_2026-05-01.docx` section 3.

| # | Severity | Location | Status | Summary |
|---|---|---|---|---|
| 11.1 | **HIGH** (silent data loss) | `SINGLE_SYNC_Photos.ps1:270` | **Unfixed** | `-Include` ignored; always 0 local files; SP photos would be deleted as "missing locally" if any existed. |
| 11.2 | HIGH (monitoring) | Scheduled Tasks | **Root cause known, no monitoring added** | Mar–Apr 2026 blackout from password rotation. Add missed-run alerting in KOR Ops port. |
| 11.3 | MED | `SINGLE_SYNC_SSI/RFI/Photos.ps1` | **Unfixed** | No upload retry on `being used by another process`. Stickfile has the pattern (`Invoke-UploadWithRetry`) — copy it. |
| 11.4 | MED | `watcher.ps1:225-244` | **Unfixed** | `Test-FileUnlockedStable` 1-second window too short for Newforma. Bump to 2–3 s in .NET. |
| 11.5 | LOW (latent) | `SINGLE_SYNC_RFI.ps1:199-200` | **Unfixed** | Doesn't ensure project root before creating RFI subfolder. Other helpers do `Ensure-SharePointFolder $SPRoot` first. |
| 11.6 | LOW (perf) | `watcher.ps1` | Acceptable | One save fires 2–3 helpers due to FSW event amplification. COALESCE absorbs most. |
| 11.7 | LOW (cred mgmt) | All scripts | Deferred by Ian | ClientSecret hardcoded in 13 scripts. New code should not duplicate. |
| 11.8 | LOW (drift) | EOR map vs PM map | Tolerated | "Jim DesRoches" vs "James DesRoches". One person, two strings. |
| 11.9 | LOW–MED (storage leak) | `Move_Reports_To_EOR.ps1` | **Unfixed** (temp dir cleared) | Downloaded temp PDFs never removed. Pattern is in `Move_Reports_To_ToSend.ps1:223`. |
| 11.10 | LOW | `SINGLE_SYNC_*.ps1` (Photos esp.) | Unfixed | Don't paginate `?$top=200`. If a folder ever has >200 children, prune logic breaks. |
| 11.11 | INFO | Several | Acknowledged | Dead/superseded scripts (legacy `_SYNC_*`, `watcher_old`, `RenameReportsUploads_working`, `RenameReportsDate`, `Paths.txt`). Pruning deferred. |

---

## 12. Migration plan and order

**Agreed in prior session:**

1. **`Send-Weekly-PM-Deadlines`** — first. Smallest, isolated, no file-server dependency, exercises Graph mail + Excel-read patterns you'll reuse. Good first piece to validate KOR Ops conventions on (logging shape, config shape, secret store wiring).
2. **`Move_Reports_To_EOR`** — second. Patch the temp-file leak (§11.9) inherently as part of the rewrite. Solid Graph CRUD exercise (list, download, upload, delete, batch).
3. **`Move_Reports_To_ToSend`** — third. Like (2) but with file-server interaction.
4. **`RenameReportsUploads`** — fourth. Pure Graph PATCH loop.
5. **The watcher (Layer A)** — last. Build as a robust .NET hosted service (not a script). This is where §7's behavioral spec matters most. Can run side-by-side with the PS watcher for a soak period before cutover.

For each step:
- Build it in KOR Ops with structured logging (Serilog?), DI, config, and **missed-run / failed-run alerting**. The lack of alerting is the actual cause of the Mar–Apr blackout — fix it once in KOR Ops, every job benefits.
- Add a `-WhatIf`-equivalent dry-run mode (preserve the existing pattern Ian relies on).
- Mirror the PS1 behavior file-for-file for the first cut. Don't refactor or "improve" until parity is proven.
- Run the new code alongside the old PS1 (different Graph identity, different log path) and reconcile output before cutting over.
- Cut over by **disabling** the PS Scheduled Task (don't delete) and having the new service run. Keep the PS1 as rollback for one full cycle (one month for monthly jobs, one week for weekly).
- Once stable, prune the legacy file from the production folder.

**Don't port the watcher until 1–4 are stable in KOR Ops.** It's the most delicate piece and benefits most from "we've already shaken out the KOR Ops conventions on the easy stuff."

---

## 13. User profile and preferences

- **Identity:** Ian Lalonde, `ilalonde@korstructural.com`, KOR Structural Engineering.
- **Role:** senior dev. Owns the **KOR Ops** .NET ecosystem (their internal application), which is the migration target.
- **Working style:**
  - Production-paranoid in a healthy way. "MAKE NO CHANGES" was his explicit opening instruction. "I can't mess it up. Would have to be very surgical."
  - Prefers evidence-based answers over opinion. Responds well to "here's what the log says" and "here's the line number."
  - Concise communication — doesn't need long preambles. A direct recommendation + tradeoff is the ideal shape.
  - Calls his system "rickety" — he's aware of the debt and is clearly past the point of nursing the PS scripts forward.
- **Constraints he's stated:**
  - Skip security/secrets hardening for now ("I'm not worried about client secrets and security credential hardening right now").
  - Migration must be surgical, not big-bang.
  - Doesn't want to port PS → PS. Wants a real .NET rebuild in KOR Ops.
- **Communication preferences observed:**
  - Asks "what are your thoughts on …" expecting a recommendation, not a survey.
  - Says "a" or short answers when he means "yes, do option A". Don't over-confirm.
  - Comfortable with markdown formatting.

---

## 14. Decisions and actions in the prior session

1. **Deep analysis performed, no changes made** to PS code. Findings written to `FileSync_Analysis_Report_2026-05-01.docx`.
2. **`C:\_APPS\FileSync\Temp` emptied** — 1,496 files / 1.36 GB removed. Directory left in place. Underlying leak in `Move_Reports_To_EOR.ps1` not yet patched.
3. **Decision: rebuild in KOR Ops, not port to new PS.** Ian's call.
4. **Decision: incremental migration in the order above (§12).** Started by recommending periodic jobs first (lower risk, observability win), watcher last.
5. **Decision: copy the production folder to dev box** for VS-based porting work. Ian to do this himself.
6. **Marker-file gate idea was discarded** — was an answer to "what if I accidentally run a PS1 on dev?" but the dev box won't be running these scripts at all (KOR Ops is the runtime). Recommended instead: drop the copies under `_reference/legacy-ps1/` in the KOR Ops repo and/or rename `.ps1` → `.ps1.txt` so a stray double-click can't fire them with prod credentials.
7. **This handoff brief written** (`_HANDOFF_FOR_KOR_OPS_PORT.md`).

---

## 15. Open / next steps when you resume on dev box

In rough priority order:

1. **Confirm the migration order with Ian** (§12). He's already agreed but it's been ~minutes-to-days; reconfirm before starting.
2. **Review KOR Ops conventions** with Ian: how does the existing app handle config (appsettings? user-secrets?), DI lifetime patterns, logging (Serilog likely?), Graph SDK choice (`Microsoft.Graph` vs raw `HttpClient`?), background-service pattern (`IHostedService`? Quartz? Hangfire?), missed-run alerting target (Slack? Teams? email?), and where secrets live.
3. **Set up the first migration target** (`Send-Weekly-PM-Deadlines`) as a new component in KOR Ops. Mirror the behavior in §9.4. Add structured logging and a "did I send anything this week?" health check.
4. **Cut over piece-by-piece** per §12. Disable each PS Scheduled Task as you go (don't delete; keep as rollback).
5. **Plan the watcher port last** as a hosted service. Use §7 as the spec.

**Things you should NOT do without explicit Ian confirmation:**
- Touch any PS1 file in `C:\_APPS\FileSync\Production\` on the production server.
- Disable any Scheduled Task.
- Delete any log file.
- Push to KOR Ops main branch.
- Make any cred / secret rotation move.

---

## 16. Pointers to primary sources

If you want to verify any claim in this brief, the source of truth is:

| Topic | File |
|---|---|
| Watcher behavior | `watcher.ps1` (550 lines) |
| Best-organized helper (use as shape reference) | `SINGLE_SYNC_Stickfile.ps1` |
| Photos bug | `SINGLE_SYNC_Photos.ps1:270` (`-Include` glob) |
| EOR move flow | `Move_Reports_To_EOR.ps1` (lacks temp cleanup) |
| ToSend flow + good temp cleanup pattern | `Move_Reports_To_ToSend.ps1:223` |
| Rename rules | `RenameReportsUploads.ps1` |
| Weekly email rules | `Send-Weekly-PM-Deadlines.ps1` |
| Auth pattern | any of the above (all use the same code shape) |
| Live runtime evidence | `Logs\Watcher_Log.txt` (HEARTBEATs, errors, lock-poller traces) |
| 263 race-condition errors | `Logs\Single_SSI_Sync_Log.txt` |
| Latent photo-bug evidence | `Logs\Single_Photos_Sync_Log.txt` (every entry "Photos to sync: 0") |
| Scheduled-task gap | `Logs\Weekly_Deadlines_Send_Log.txt` (gap 2026-02-23 → 2026-04-27) |
| Scheduled-task gap | `Logs\Move_Reports_Log.txt` (no 2026-04-01 run; manual 2026-04-15) |
| Prior-session deep analysis | `FileSync_Analysis_Report_2026-05-01.docx` |

---

## 17. Glossary

| Term | Meaning |
|---|---|
| **EOR** | Engineer of Record. Each project has one assigned (CSV in SharePoint). |
| **PM** | Project Manager. |
| **SSI** | Structural Site Instruction. PDFs issued to the field. |
| **RFI** | Request for Information. PDFs from inspectors back to the office. |
| **Stickfile** | Project's drawing-set PDF (whole structural set). Lives at `<Project>\05 Stickfile\` on file server, project root on SharePoint. |
| **Reports** | Inspection reports (CRM PDFs from a mobile-app upload pipeline outside the scope of these scripts). Live at `<Project>/Reports` on SharePoint. |
| **Newforma** | Project information management tool used by KOR. Writes some of the PDFs that the watcher syncs (and is the cause of the file-lock races in §11.4). |
| **Deltek** | The ERP/PM system that produces `Project Deadlines.xlsx` consumed by `Send-Weekly-PM-Deadlines.ps1`. |
| **KOR Ops** | Ian's existing .NET application. Migration target. |
| **CRM** | The mobile-report convention prefix (in the rename pattern `… CRM YYYY-MM-DD Report NN`). Stands for "Construction Report Mobile" / similar — used as a literal token in filenames. |
| **CatchAll** | The fallback EOR folder when project-to-EOR mapping is missing. |
| **`_ToBeDeleted`** | SharePoint root folder where `SINGLE_CLEAN_Projects.ps1` parks projects that have a `NOT SYNCED…` control file. |
| **Control file** | A specific filename whose presence/absence gates a behavior (see §6 for the two used here). |

---

*End of handoff brief. If you find anything in this document that contradicts the actual code or logs, trust the code and logs and update this file (or flag it to Ian). Production reality is the source of truth; this document is a map.*
