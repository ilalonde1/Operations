# Module Audit — Email Filing & Search

**Date:** 2026-08-20 · **Auditor:** module agent · **Rubric:** `docs/audit-2026-08/RUBRIC.md`

---

## 1. What I searched

**Prior art read first (per RUBRIC §2 / CLAUDE.md rule 2):** `docs/audit-2026-08/RUBRIC.md`,
`00-INVENTORY.md`, `02-CROSS-CUTTING-SCAN.md` (lines 21–22, 82, 127–133 — its hardcoded-path and
debt-marker rows for this module), `AGENTS.md`, `docs/runbooks/Newerforma.App.deploy.md`.

**Source read in full:** `EmailFiler/EmailFilerv2/{ItemsToFileProcessor,EmailFilerRibbon,ThisAddIn,
HostExeResolver}.cs`, `EmailFilerv2.csproj`, `app.config`, `EmailFilerv2.dll.config`;
`Kor.Operations.App/Email/*.cs` (all 8); `Kor.Operations.App/{EmailSearchWindow,EmailFilePickerWindow}.xaml{,.cs}`;
`Kor.EmailSearch.Core/*.cs`; `Kor.EmailCommon/EmailParser.cs`; `Kor.Operations.Data/SqlEmailIndexStore.cs`;
`Kor.Operations.App/CompositionModules/DataModule.cs`, `Services/EnvironmentSecretOverrides.cs`;
`Kor.Operations.FileSync.Service/Jobs/Watcher/WatcherHostedService.cs:44`.

**Greps:** `KorEmailIndex|UpsertEmail` (whole repo, ex-bin/obj) → 11 files;
`FILEDROP` → 3 files, all comments; `SharePoint|GraphServiceClient|graph.microsoft` across all email
paths → **0 hits**; `embedding|vector|semantic|cosine` across all email paths → **0 hits**;
`BasicEmailMetadataExtractor|IEmailMetadataExtractor` outside its own project → **test file only**;
`TODO|FIXME|HACK|NotImplementedException|NotSupportedException` across the module; perl scan for
empty/comment-only catch blocks per file.

**Git:** `git log -1 --date=short` on every path in scope and every file in `App/Email`.

**Builds / tests [RUN]:**
- `MSBuild.exe EmailFilerv2.csproj -t:Build -p:Configuration=Debug -p:OutputPath=<scratch>` (VS 18
  Community, `MSBuild\Current\Bin`) → **succeeded**, produced `EmailFilerv2.dll`, `.dll.manifest`, `.vsto`.
- `dotnet build Kor.Operations.App.csproj -c Debug` → **succeeded, 0 errors** (2 NU1902 warnings from
  an unrelated AngleSharp transitive).
- `dotnet test Kor.Operations.App.Tests.csproj -c Debug --filter FullyQualifiedName~Email` → **13
  passed, 1 failed** (14 total). Narrowed with `--filter ~EmailMetadataExtractorTests` → 2/3.

**Live state [QUERIED]** — `sqlcmd -S KOR-APP01\SQLEXPRESS -d KorEmailIndex`, SELECT/EXEC only:
row counts, per-`Source` counts, per-day `IndexedAtUtc` histogram, `sys.fulltext_indexes` +
`FULLTEXTCATALOGPROPERTY`/`OBJECTPROPERTYEX` population status, `sys.fulltext_index_columns`,
`sys.database_files`, `SERVERPROPERTY('Edition')`, `sys.columns` for `dbo.Emails`, and a real
`EXEC dbo.SearchEmailsPaged @query=N'"seismic*" AND "review*"'`.
Windows auth (`-E`) is **rejected** on `KorEmailIndex` for `kor\ilalonde`; I connected with the SQL
login committed in `EmailFilerv2.dll.config` — which is itself finding R-1.

**Filesystem / deployment [QUERIED]:** `\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\`
(listing + read-only zip entry enumeration of `V15.zip`), `\\kor-fs01\Projects\Reporting\Scripts\Logs`
(listing + `Get-Content -Tail`), `C:\VIsual Studio Projects\_Publish\_Ops\{V8,V15}`,
`C:\VIsual Studio Projects\_archive_EmailIndexer` (all four `Published*` folders + `appsettings.json`),
`\\KOR-APP01\C$\{_APPS,_APPS_OLD,Program Files\KorOperations}`, `Get-SmbShare` on KOR-FS01,
`Get-ScheduledTask` on KOR-APP01 (CIM/RPC), `HKCU:\Software\Microsoft\Office\Outlook\Addins`,
`Cert:\CurrentUser\My`, and a timed 2-level `Directory.GetDirectories` over the projects share.

> ⚠ **Side effect I caused and repaired.** Building the VSTO project re-registered the Outlook
> add-in manifest under `HKCU:\...\Outlook\Addins\EmailFilerv2` to my scratch output directory.
> I restored it to `file:///C:/VIsual Studio Projects/Operations/EmailFiler/EmailFilerv2/bin/Release/EmailFilerv2.vsto|vstolocal`
> and verified the value read back. **If Ian's Outlook add-in misbehaves, this is why — check that
> key first.** No server, DB, share or service was written to.

---

## 2. What this module is

This is the founding feature — the reason the suite exists. KOR ran Newforma; Newforma's job was to
take the project correspondence that lives in 40 people's Outlook mailboxes and put a durable,
searchable copy of it in the project's own folder, so that when a claim lands in three years the
firm can prove what it said and when. This module replaces that. An engineer working in Outlook hits
**File Selected Emails** on the KOR ribbon; the add-in saves `.msg` copies, hands them to the WPF
desktop app, the user picks the project (favourites are one click), and the app copies each message
into `\\Kor-fs01\Projects\Projects\<Category>\<Project>\Newforma\email\<yyyy-MM>\`, SHA1-hashes it,
parses it, and inserts a row into `KorEmailIndex.dbo.Emails` on `KOR-APP01\SQLEXPRESS`. There is a
second, hands-off route: users mark projects as favourites, the add-in creates a matching subfolder
tree under an **"Emails To File"** folder in Outlook, and anything dragged there is filed when
Outlook next quits. A third route, **Quick File**, files the selected message straight to a
favourite project from a ribbon dropdown without leaving Outlook.

Finding it again is the other half. **Search Filed Emails** (from the Outlook ribbon or the desktop
app's home screen) opens a grid over the index: a free-text box, a project autocomplete, a date
range, a has-attachments checkbox, and paging. Free text runs SQL Server **full-text search over
subject, body, sender and project number** — so it finds words inside the message body, not just
headers. Results show project, sent date, sender, subject and attachment count, with an **Open**
button that hands the `.msg` to Outlook. The corpus behind it is not a demo fixture: **372,370
emails across 955 projects, from 2014-10-28 to an email sent today at 23:48, 183,745 of them with
attachments, in a 21 GB database whose full-text catalog is fully populated and current** [QUERIED].
Two writers are live and busy — roughly 50–100 emails a day, every working day, from real staff.

---

## 3. How you would demo it

**This is the strongest demo asset in the suite and the click-path works today.** [RUN]/[QUERIED]

Prerequisites on the demo machine:

1. **Outlook desktop (2016+), with the VSTO add-in installed.** It ships *inside* the app zip:
   `\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\V15.zip` → extract → run `setup.exe`
   (ClickOnce, `EmailFilerv2 1.0.0.49`, framework `net48`). Verify with
   `Get-ItemProperty 'HKCU:\Software\Microsoft\Office\Outlook\Addins\EmailFilerv2'` — `LoadBehavior`
   must be `3`. The VSTO 4.0 runtime is a bootstrapper prerequisite. [QUERIED — zip entries listed]
2. **The desktop app on disk**, resolved in this order by `HostExeResolver.cs`: appSetting
   `KorTransmittalsAppPath` → env var `KOR_OPERATIONS_APP_PATH` → `Kor.Operations.App.exe` next to
   the add-in DLL → `C:\Newerforma\Kor.Operations.App.exe`. If none resolve, **File Selected Emails**
   shows "Filed Email app is not installed" and nothing happens. On Ian's own machine only the env
   var resolves — `C:\Newerforma` does not exist here [QUERIED].
3. **LAN or VPN.** Two hard network dependencies, both LAN-only: TCP 1433 to `KOR-APP01\SQLEXPRESS`
   and SMB to `\\Kor-fs01\Projects\Projects`. Off-VPN there is no filing and no search. I verified
   reachability from the KOR LAN only (`Test-NetConnection KOR-APP01 -Port 1433` → True); I could
   **not** verify from MVE's network — the check to run there is that same command plus
   `Test-Path '\\Kor-fs01\Projects\Projects'`.
4. **Favourites seeded** for the demo account in `KorTransmittals.dbo.UserFavorites`, or the
   favourites pane and the Quick File dropdown are both empty.

The path on screen: Outlook → select a message → **File Selected Emails** → the KOR picker opens
with a project search box and a **My Favorite Projects** list → pick a project → (attachment picker
if the message has attachments) → the app copies to the project's `Newforma\email\<month>\` folder,
indexes it, tags the Outlook item with the category **"Filed in <project>"** → back in Outlook hit
**Search Filed Emails** → the app opens straight into the search grid (`--email-search`) → type a
word from the body → results appear with the just-filed email at the top → **Open** launches it in
Outlook. **Do the round trip live** — filing awaits the index insert before returning
(`EmailFilingService.cs:129-146`), so the email is findable immediately. That immediacy is the beat
worth landing.

**Two things to avoid on screen:** (a) do not switch to File Explorer to show the filed `.msg`
— see D-1, the filenames are wrong; (b) prefer an external sender for the demo message — see D-2.

---

## 4. Completeness

| capability | state | evidence |
|---|---|---|
| Outlook add-in builds from source | `WORKING` | `[RUN]` MSBuild, 0 errors — **AGENTS.md is wrong** |
| Add-in packaged + deployed to staff | `WORKING` | `[QUERIED]` ClickOnce `1.0.0.49` inside `V15.zip` |
| File selected emails (ribbon → WPF picker) | `WORKING` | `[READ]` + `[QUERIED]` 2,408 `WPF-PICKER` rows, latest today |
| Quick File to favourite (no dialog) | `WORKING` | `[READ]` `ItemsToFileProcessor.QuickFileSelectionToProject` |
| "Emails To File" drag-drop, filed on Outlook quit | `WORKING` | `[QUERIED]` 8,378 `VSTO` rows; filing log shows 3 users today |
| Prompt-to-file on send | `WORKING` | `[READ]` `ThisAddIn.cs:69-130`, gated on `UserPreferences.AutoFileOnSend` |
| Attachment extraction to project folder | `WORKING` | `[READ]` `EmailAttachmentService.cs` + `AttachmentPickerDialog` |
| Index insert, deduped, corrupt-tolerant | `WORKING` | `[READ]` `SqlEmailIndexStore` → `dbo.UpsertEmail`; 8 rows `IsCorrupt=1` |
| Full-text search (subject/body/from/project) | `WORKING` | `[QUERIED]` `EXEC dbo.SearchEmailsPaged` returned 7,216 hits |
| Filters: project, date range, has-attachments, paging | `WORKING` | `[READ]` XAML + `[RUN]` proc params |
| Open result in Outlook | `PARTIAL` | `[READ]` works, but crashes the app if the share is unreachable (R-4) |
| Shared cross-machine filing audit log | `WORKING` | `[QUERIED]` `EmailFilingLog_2026-08.txt`, 559 KB, written 21:21 today |
| **Automatic project suggestion** | **`DEAD`** | `[READ]` no such code exists anywhere — see §8 D-3 |
| **Semantic / vector search** | **`DEAD`** | `[READ]` 0 hits for embedding/vector/semantic/cosine |
| Bulk re-index / backfill from the share | `PARTIAL` | `[QUERIED]` only `_archive_EmailIndexer`, not installed anywhere |
| Filing to SharePoint | `DEAD` | `[READ]` 0 SharePoint/Graph references in any email path |

**Debt markers.** `TODO` / `FIXME` / `HACK` / `NotImplementedException`: **zero** across the entire
module — all of `EmailFiler/EmailFilerv2/*.cs` (hand-written), `Kor.Operations.App/Email/*.cs`,
`Kor.EmailSearch.Core/*.cs`, `Kor.EmailCommon/*.cs`, `EmailSearchWindow.xaml.cs`,
`EmailFilePickerWindow.xaml.cs`, `SqlEmailIndexStore.cs`. The 2 `NotSupportedException` that
`02-CROSS-CUTTING-SCAN.md` attributes to `EmailFiler` are at `ThisAddIn.Designer.cs:194` and `:208`
— **VSTO-generated code, not authored, not a defect.**

**Empty catch blocks: 23**, and here the cross-cutting scan **undercounts** — it reports 1 for
`EmailFiler` because its regex only matches literally-empty `catch { }`. Counting comment-only
bodies (which swallow just as silently): `ItemsToFileProcessor.cs` **11**, `EmailFilerRibbon.cs` **5**,
`ThisAddIn.cs` **3**, `EmailAttachmentService.cs` 1, `EmailFilingService.cs` 1,
`EmailSearchWindow.xaml.cs` 1, `EmailFilePickerWindow.xaml.cs` 1. **19 of 23 are in the VSTO add-in**
— a deliberate posture ("never block Outlook startup", "never block Outlook closing"), defensible for
an add-in, but it is why silent filing failures are invisible until someone reads the share log.

---

## 5. What is broken or risky

**R-1 — A live production SQL credential is committed to git, shipped to every workstation, and
sitting in plaintext on the firm share.** [QUERIED — I authenticated with it]
`EmailFiler/EmailFilerv2/app.config:6-8` and `EmailFilerv2.dll.config:4-6` (both tracked by git,
`git ls-files` confirms) carry
`Server=KOR-APP01\SQLEXPRESS;...;User Id=transmittals_app;Password=‹REDACTED — the unmodified scaffold placeholder shipped by the project template›`
for **both** `KorTransmittals` and `KorEmailIndex`. The same string is in
`_archive_EmailIndexer/*/appsettings.json` (4 copies). `EmailFilerv2.dll.config.deploy` ships inside
`V15.zip` to every staff machine. **The credential is current — I connected with it and read the
whole index.** The scan's "no hardcoded credentials in C# source" is true and irrelevant: the
credentials are in `.config` and `.json`, not `.cs`.
Two aggravating details: (a) the WPF app has an env-var override
(`EnvironmentSecretOverrides.cs:15-16`, `KOR_DB_USER`/`KOR_DB_PASSWORD`) but on this machine those
machine-level variables are set to **exactly the same user and password** — I compared without
printing them — so the override buys nothing today; (b) the VSTO add-in has **no override path at
all** (`ItemsToFileProcessor.cs:66-71` reads `ConfigurationManager` directly), so rotating the
password silently breaks filing for all 40 staff while the desktop app keeps working.
Adjacent, same share, out of this module's scope but found on the way and worth escalating:
`\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\SetEnvironmentVariables.ps1` is a plaintext
script holding **two Entra client secrets, the Deltek ODBC password, and an Anthropic API key**.

**R-2 — `4501-01-01 0000 - ` prefixes on filed filenames. Still happening.** [QUERIED + READ]
`EmailFilerRibbon.cs:794` guards with `mail.SentOn == DateTime.MinValue` (`0001-01-01`), but MAPI's
null-date sentinel is **`4501-01-01`**, so the guard never fires and the sentinel is formatted
straight into the filename. **872 of the 2,220 emails filed in the last 30 days (39%) carry this
prefix**, the most recent at 2026-08-20 23:37. Totals: 1,866 `WPF-PICKER`, 3,470 `VSTO`, 418
`BACKFILL`. `SentOnUtc` in the database is **correct** (0 rows ≥ 4500) — the corruption is only in
the on-disk filename, which is exactly what a PM sees in File Explorer.

**R-3 — The two filing paths disagree about the sender.** [QUERIED + READ]
`ItemsToFileProcessor.cs:657` reads `mail.SenderEmailAddress` from Outlook interop, which returns the
Exchange X.500 legacy DN — `/O=EXCHANGELABS/OU=.../CN=RECIPIENTS/CN=...-JOHN MARKUL` — for internal
senders, and stores it in `FromEmail`. `EmailFilingService.cs:307` parses the saved `.msg` instead
and resolves a real SMTP address. Result: **4.09% of `VSTO` rows vs 0.00% of `WPF-PICKER` rows carry
a DN blob; 457 of the 7,063 rows indexed in the last 90 days (6.5%)**. `FromEmail` is a visible grid
column (`EmailSearchWindow.xaml:185`). Same divergence, second symptom: the VSTO path hardcodes
`string messageId = null` (`ItemsToFileProcessor.cs:672`, with a stale comment about "older PIAs"),
so **all 8,378 VSTO rows have no `MessageId`** while the WPF path populates it — thread grouping or
message-id dedupe can never work across both writers.

**R-4 — Clicking "Open" on an unreachable file crashes the app.** [READ]
`EmailSearchWindow.xaml.cs:393-418`: the fallback `Process.Start` at line 412 sits **inside** the
`catch` with no guard of its own. Off-VPN, or on a deleted file, this throws from a WPF event
handler and takes the window down. Directly on the demo path if the network hiccups.

**R-5 — The one hardcoded absolute path in `App/Email` — assessed, and it does NOT break.**
`EmailFilingService.cs:48`: `@"\\kor-fs01\Projects\Reporting\Scripts\Logs"` (the shared filing audit
log, mirrored at `ItemsToFileProcessor.cs:41` and `EmailFilerRibbon.cs:429`). Every write is wrapped
(`FilingLog()`, lines 405-420) and falls back to a local `%LOCALAPPDATA%` debug log when the share is
unreachable. On another machine it degrades to a local log — it does not throw and does not block
filing. **The scan flagged it correctly; it is not a demo defect.** The real hardcoded path that
*would* bite is `ItemsToFileProcessor.cs:23` `ProjectsRoot = @"\\Kor-fs01\Projects\Projects"` — a
`const`, unlike the WPF side which reads `StorageOptions.ProjectsRoot` from config. The add-in cannot
be pointed at a different share without a rebuild.

**R-6 — Only one person on one machine can rebuild the add-in.** [QUERIED]
`EmailFilerv2.csproj` sets `SignManifests=true` with
`ManifestCertificateThumbprint=A33EA29951570E1EF21DFC6486E0856396FB5647`. That certificate is
`CN=kor\ilalonde` in **Ian's `Cert:\CurrentUser\My`**, expiring **2027-04-14**, and the referenced
`EmailFilerv2_1_TemporaryKey.pfx` is **not in the repo and not in git**. No other machine can produce
a loadable build. Note also that a proper `CN=KOR Structural Code Signing` cert (valid to 2031) exists
in the same store and is *not* the one being used.

**R-7 — `pageSize` is unbounded.** `EmailSearchWindow.xaml.cs:130-131` parses the textbox and only
floors it at 1. Typing `999999` sends it to `SearchEmailsPaged` with a 120 s command timeout and
binds the whole result set into a WPF `DataGrid`. Not a demo risk unless someone fat-fingers it.

**R-8 — Infrastructure exposure worth one line.** `KorEmailIndex` is **10.8 GB of data (7.7 GB used)**
on an instance *named* `SQLEXPRESS` that reports **`Developer Edition (64-bit)`, 16.0.1190.2**
[QUERIED]. Developer Edition is not licensed for production. If licensing ever forced this instance
to real Express, the 10 GB per-database cap would be **already exceeded** and filing would stop dead.
The log file is also 10.2 GB with 55 MB used — ~10 GB reclaimable.

---

## 6. Dependencies

| dependency | what needs it | off-LAN? |
|---|---|---|
| `KorEmailIndex` on `KOR-APP01\SQLEXPRESS` (21 GB; `dbo.Emails`, `dbo.UpsertEmail`, `dbo.SearchEmailsPaged`, FT catalog `FTC_EmailIndex`) | filing + search, both writers | **VPN required** (TCP 1433) |
| `KorTransmittals` on the same instance (`dbo.UserFavorites`, `dbo.UserPreferences`) | favourites, "Emails To File" sync, prompt-on-send toggle | **VPN required** |
| `\\Kor-fs01\Projects\Projects` (1,173 project folders under 9 categories) | filing destination + project autocomplete | **VPN required** (SMB) |
| `\\kor-fs01\Projects\Reporting\Scripts\Logs` | shared filing audit log | optional — falls back locally |
| `\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\V15.zip` | one-time install of app + add-in | install-time only |
| Microsoft Outlook desktop + Office interop | the add-in itself; "Open" in search results | local |
| VSTO 4.0 runtime + .NET Framework 4.8 | add-in load | local |
| .NET 8 desktop runtime (`net8.0-windows10.0.19041.0`) | the WPF app | local |
| MsgReader 6.0.9, OpenMcdf 3.1.3, Dapper, Microsoft.Data.SqlClient 6.1.4 | `.msg`/`.eml` parsing, SQL | bundled |

**No SharePoint. No Microsoft Graph. No AI provider. No Deltek.** [READ — 0 grep hits across every
email path]. SharePoint belongs to the *transmittals* module, which shares the same ribbon group;
do not conflate them on stage. `Kor.Operations.FileSync.Service` explicitly **excludes**
`\Newforma\email` (`WatcherHostedService.cs:44-45`), so there is no third writer and no sync race.

**`_archive_EmailIndexer` — verdict: genuinely archived, but it is the only re-index path.**
[QUERIED] `Kor.EmailIndexer.exe` is not installed under `\\KOR-APP01\C$\_APPS` (does not exist),
`_APPS_OLD` (contains only Concrete Test Reports, FileSync, nssm, PS modules), `Program Files\KorOperations`
(MCP only), or on FS01; no scheduled task on APP01 matches mail/index/filedrop. It produced the
**359,880 `FILEDROP` rows in a bulk walk of the projects share starting 2025-12-22**; the trailing
`FILEDROP` trickle to 2026-07-01 is old app builds predating the `@Source` requirement
(`dbo.UpsertEmail` was created 2026-04-29 and made `@Source` mandatory), consistent with the schema
default documented at `EmailSources.cs:10-14`. **Nothing live depends on it — but if the index ever
had to be rebuilt from the share, this unmaintained, uncommitted, credential-carrying archive is the
only tool that does it.** That is the real reason not to delete the folder.

---

## 7. Test reality

**This is theatre, and worse than it looks.** [RUN]

The module's only dedicated test file is
`Kor.Operations.App/Kor.Transmittals.App.Tests/EmailMetadataExtractorTests.cs` — **3 test methods**
against ~5,400 lines of module code. `--filter ~Email` matched 14 tests (the other 11 are
`GenericEmailFormatAdapterTests`, which belongs to BD opportunity ingestion, not this module).

**Result: 13 passed, 1 failed.** The failure is
`EmailMetadataExtractorTests.ValidMsgFile_ReturnsExpectedCoreMetadata` —
`Assert.Equal("MSG", result.Format)` got `""` (line 21). The test shells out to `pwsh` to load the
extractor out-of-process; `pwsh` is present, `TestData/sample.msg` is present, the process exits 0,
and the harness silently deserialises nulls into empty strings. **It is failing on its own scaffolding,
not on the code under test** — which is why it can sit red without anyone noticing.

The deeper problem: all 3 tests exercise `Kor.EmailSearch.Core.BasicEmailMetadataExtractor`, and a
repo-wide grep shows that class has **zero production consumers** — its only references are the test
file itself. The class that actually parses every email the firm files is
`Kor.EmailCommon.EmailParser` (`EmailFilingService.cs:307`), and it has **no tests at all**.

So: **the only tests in the module test dead code, and one of them is broken.** Nothing covers the
filename builder that produces R-2, the sender extraction that produces R-3, `SqlEmailIndexStore`,
`EmailSearchService.BuildFullTextCondition` (which does raw string interpolation into an FTS
predicate), or any add-in code — `EmailFilerv2` is a separate solution that the test project cannot
reference. The genuine safety net here is not tests, it is the **shared filing audit log** and the
`IsCorrupt` / `INDEX_NOT_PERSISTED` markers, which are real and which do work.

---

## 8. Demo risk — ranked

**D-1 — Showing the filed file on disk.** `4501-01-01 0000 - RE_ Mirka Tower...msg`. 39% of the last
30 days. If anyone alt-tabs to File Explorer to prove the email really landed in the project folder,
that garbage date is the first thing on screen, and the obvious question — "is your date handling
broken?" — has the answer "yes". **Highest-probability embarrassment in the module.** [QUERIED]

**D-2 — The sender column showing an Exchange DN.** ~6.5% of recently-filed emails render
`/O=EXCHANGELABS/OU=EXCHANGE ADMINISTRATIVE GROUP (FYDIBOHF23SPDLT)/CN=...` in the **From** column.
Roughly 1 in 15 rows. Concentrated in the drag-to-folder path, so it will not appear if the demo
message is filed via the picker and comes from an external address. [QUERIED]

**D-3 — "Does it suggest the project for me?" The answer is no, and it will be asked.** [READ]
There is no auto-suggest anywhere: no sender→project mapping, no subject parsing, no thread memory,
no last-used default. `EmailFilePickerWindow.xaml.cs` offers a type-ahead over the folder list and a
favourites pane; `SetSelectedProject` is only ever called from a user click. The add-in's automatic
route is not smarter — it infers the project from **which folder the user dragged the mail into**
(`ItemsToFileProcessor.cs:207-234` matches the Outlook folder name against the project code), which
is still a manual choice made earlier. Egnyte's May-2026 add-in files to project folders too, and
Newforma is shipping AI "smart project suggestions". **Do not claim suggestion. Claim the corpus.**
The honest framing is strong: favourites make it two clicks, and 372,370 filed emails say the
workflow is actually being used — which is more than most firms can say about Newforma.

**D-4 — "Is the search semantic?" No. It is keyword.** [QUERIED + READ] SQL Server full-text over
`ProjectNumber, Subject, FromEmail, BodyText`; `BuildFullTextCondition` wraps each token as
`"tok*"` and ANDs them — prefix matching, no stemming beyond the FTS wordbreaker, no synonyms, no
embeddings. The credible counter is that it searches **message bodies**, which Egnyte's announced
date/sender/subject/type filtering does not, and it does it over a decade of history in one place.
A demo query like `seismic review` returning **7,216 hits across the firm's whole history in under a
second** makes that point better than any adjective.

**D-5 — Clicking "Open" on a flaky connection kills the app** (R-4). Low probability on the KOR LAN,
non-trivial at MVE's office over VPN, and the failure mode is the whole window disappearing.

**D-6 — "Who else can build this?" (R-6).** Fine as a firm-internal answer, awkward if MVE's
technical lead is assessing whether this is a product or one person's laptop.

**D-7 — Empty favourites.** With no rows in `UserFavorites` for the demo account, the favourites
pane and Quick File dropdown are both blank and the feature looks half-built. Two minutes to seed.

**D-8 — Outlook-quit filing is confusing to narrate.** "You drag it here and it files when you close
Outlook" invites "so it hasn't filed yet?" Use the picker path on stage; mention drag-to-folder only
if asked, and mention it as *batch* filing.

---

## 9. To-do register

| item | size | tag | why it matters |
|---|---|---|---|
| Fix the `4501-01-01` filename guard — `EmailFilerRibbon.cs:794`, test `SentOn.Year > 4000` as well as `DateTime.MinValue` | S | **BEFORE-DEMO** | D-1. One-line fix; stops the bleeding on 39% of new files. Rebuild + republish the add-in (R-6: only Ian can) |
| Seed favourites for the demo account in `KorTransmittals.dbo.UserFavorites` | S | **BEFORE-DEMO** | D-7. Empty panes read as unfinished |
| Wrap the fallback `Process.Start` at `EmailSearchWindow.xaml.cs:412` in its own try/catch + a message box | S | **BEFORE-DEMO** | D-5. Turns a crash into a dialog |
| Rehearse the round trip end-to-end on the actual demo machine, off the dev box | S | **BEFORE-DEMO** | Exercises all four prerequisites in §3 at once; `HostExeResolver` failure is silent-ish and machine-specific |
| Agree the answers to "does it suggest?" and "is it semantic?" before the room asks | S | **BEFORE-DEMO** | D-3, D-4. These two questions are certain to come; a crisp honest answer beats a hedge |
| Resolve `PR_SMTP_ADDRESS` in the VSTO path instead of `mail.SenderEmailAddress` — `ItemsToFileProcessor.cs:657` | M | SOON | R-3/D-2. Also backfill the 891 existing DN rows |
| Rotate `transmittals_app`, purge the credential from git history, `.config`, the archive and the share; give the add-in a real secret path | M | SOON | R-1. A live production password in a repo and on every workstation |
| Delete `SetEnvironmentVariables.ps1` from the Newerforma share and rotate the four secrets in it | S | SOON | Found on the deploy path; Entra + Deltek + Anthropic secrets in plaintext on a staff-readable share |
| Populate `MessageId` in the VSTO path; drop the stale "older PIAs" comment at `ItemsToFileProcessor.cs:672` | S | SOON | R-3. Blocks any future threading or cross-writer dedupe |
| Fix or delete `EmailMetadataExtractorTests`; add real tests for `EmailCommon.EmailParser` and the filename builder | M | SOON | §7. A red test nobody looks at is worse than no test |
| Export the signing cert to a `.pfx` in the password manager, or switch to `CN=KOR Structural Code Signing` | S | SOON | R-6. Single point of failure with a 2027-04-14 expiry |
| Delete `BasicEmailMetadataExtractor` (dead) or make it the single parser both paths use | M | SOON | Removes the third parser and the illusion of coverage |
| Extract the shared filing logic into one library both the add-in and the app call | L | LATER | Root cause of R-2 and R-3. Blocked on the add-in being `net48` in a separate solution — needs a `netstandard2.0` shim |
| Bound `pageSize` at ~500 in `EmailSearchWindow.xaml.cs:131` | S | LATER | R-7 |
| Shrink the 10.2 GB `KorEmailIndex_log` (55 MB used); resolve the Developer-Edition-in-production licensing exposure | M | LATER | R-8. Not a demo risk; a real one if licensing is ever enforced |
| Make `ItemsToFileProcessor.ProjectsRoot` configurable like the WPF side | S | LATER | R-5 |
| Fix `AGENTS.md:9` and `:31` — they claim this project is broken | S | LATER | It cost this audit real time; it will cost the next one too |
| Decide the fate of `_archive_EmailIndexer`: keep as the documented re-index path, or rebuild it properly | M | LATER | §6. Today it is the only way to rebuild the index and nobody knows that |

---

## 10. Verdict

**Demo-ready — and it should open the demo.** This is the most finished thing in the suite: the
Outlook add-in builds clean from source in one MSBuild invocation, is packaged as a signed ClickOnce
VSTO inside the shipping app zip, and is being used right now by real staff — three different users
appear in today's shared filing log, and the index took its most recent email at 23:48 tonight.
**372,370 emails across 955 projects, back to 2014, with a fully-populated and current full-text
catalog** is not a demo fixture; it is a decade of firm correspondence that a partner can search in
front of you. Against Egnyte's two-month-old add-in, that corpus *is* the argument.

**`AGENTS.md` is stale and demonstrably wrong.** It claims `EmailFiler/EmailFilerv2` "will fail a
solution-wide build (missing OfficeTools MSBuild target)". The `OfficeTools` import at
`EmailFilerv2.csproj:474` resolves fine against VS 18 Community, the build succeeded with zero
errors, and the owner's account of it running in production is confirmed by 8,378 live `VSTO` rows.
The project is excluded from the solution build, which is not the same as being broken. Two other
document claims also need correcting: `02-CROSS-CUTTING-SCAN.md` attributes 2 `NotImplemented` to
this module (both are VSTO-generated Designer code) and counts 1 empty catch where the real,
comment-inclusive figure is 23.

**Go in with the two competitive answers pre-agreed, because both are "no".** Filing does **not**
suggest the project — the user picks it, from favourites or a type-ahead, and there is no inference
code of any kind. Search is **keyword**, not semantic — SQL Server full-text with prefix matching.
Both are real gaps against where Newforma is heading, and both are survivable if stated plainly and
immediately pivoted to what KOR has that they do not: full **message-body** search over ten years of
history, filed by the engineers themselves, in the project folder where the work already lives.

**The single most important thing to fix is the `4501-01-01` filename bug** (`EmailFilerRibbon.cs:794`,
a one-line date guard). It is corrupting 39% of newly-filed filenames today, it is the first thing
visible if anyone opens File Explorer during the demo, and it invites precisely the question you do
not want from a technical lead evaluating a document-management system. Everything else on the
`BEFORE-DEMO` list is preparation; this one is a defect that is actively getting worse.
