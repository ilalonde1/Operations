# Module 02 — Transmittals & the transfer tracking server

Audited 2026-08-20. Revised 2026-08-21. Rubric: `docs/audit-2026-08/RUBRIC.md`.

> **CORRECTION (2026-08-21) — retracted from the first version of this report.** The first version
> said the transmittal type "has exactly 3 values (Transmittal / Transfer / Upload)" and concluded there was
> **no RFI or submittal record type anywhere in the suite**. That measured the wrong field and the conclusion
> was wrong. `Type` is an internal routing discriminator; the **user-facing issue type is `Purpose`, and it has
> eight values** including "For Review", "For Approval" and "Issued for Construction (IFC)"
> (`MainWindow.xaml.cs:110`). Any downstream document repeating "no RFI/submittal type" must be corrected —
> see §4 and §5 defect 11 for what is genuinely absent, which is narrower and more specific: a **response
> record**. This revision also adds a capability the first pass missed entirely — **per-bookmark PDF
> commenting** (§2, §3, §4).

---

## 1. What I searched

**Prior art read first (CLAUDE.md rule 2):** `docs/audit-2026-08/00-INVENTORY.md` (row: Redirector, 3 .cs,
1,054 LOC), `01-DOC-TRUST.md`, `02-CROSS-CUTTING-SCAN.md` (already flags hardcoded `C:\Temp` paths in
`TransmittalServiceTests.cs:26,84,109`), and confirmed `docs/audit-2026-08/competitive/C2-newforma.md`
exists. Nothing in those files answered hosting, DB volume, build state, or link security — so I measured.

**Source read in full:** `Redirector/Kor.Transmittals.Redirector/Program.cs` (761), `InboundUploadService.cs`
(257), `ClientSecretGraphAuthProvider.cs` (36), `.csproj`, `.csproj.user`, `appsettings*.json`, `web.config`,
`Properties/launchSettings.json`, `Properties/PublishProfiles/FolderProfile.pubxml{,.user}`, `wwwroot/`.
`Operations/Kor.Operations.Core/*.cs` (4 files), `Kor.Operations.Graph/GraphFacade.cs` (1,252),
`Kor.Operations.Data/SqlTransmittalsStore.cs` (557), `Kor.Operations.App/Services/TransmittalService.cs` (384),
`UploadOrchestrator.cs` (114), `QuickTransferRunner.cs` (503), `InboundUploadRunner.cs` (348),
`MainWindow.xaml{,.cs}`, `DashboardWindow.xaml{,.cs}`, `HomeWindow.xaml`, `CompositionHelpers.cs`, `App.config`.

**Greps:** `transmittal` (case-insens., `.cs/.xaml/.csproj/.sql/.json`, obj/bin excluded); `\bRFI\b|submittal`;
`TransmittalNo`; `ReserveTransmittalNumber`; `RedirectTargets|LinkId|tracking.korstructural`;
`redirectorBase|RedirectorBaseUrl`; `TODO|FIXME|HACK|NotImplementedException|NotSupportedException`;
`catch\s*(\([^)]*\))?\s*\{\s*\}`; middleware probes (`AddRateLimiter`, `UseAuthentication`,
`MaxRequestBodySize`, `UseExceptionHandler`, `UseHsts`, `AddAntiforgery`, +7 more).

**Second pass (2026-08-21), prompted by the owner's correction:** read `MainWindow.xaml.cs:100-135, 290-300`
and `MainWindow.xaml:414-420`; `BookmarkNotesWindow.xaml{,.cs}`; `Kor.Operations.Rendering/CoverSheetRenderer.cs:190-225,
293-331, 514-526`; `PdfBookmarkExtractor.cs`; `Kor.Operations.Core/Models.cs:197,202`. Greps: `-i bookmark`
across `.cs/.xaml`; `PdfBookmarkNotes|TryGetBookmarks`; and for a two-way workflow,
`ballincourt|ball_in_court|respondby|responsedue|requiredby|markclosed|closeout|resolvedat|respondedat|transmittalresponse|awaitingresponse`
suite-wide. SQL: `INFORMATION_SCHEMA.COLUMNS` across all 52 tables filtered for
`%respon%|%repl%|%clos%|%resolv%|%due%|%requiredby%|%ballincourt%|%answer%|%status%|%assign%|%purpose%`.
Extracted the full `ITransmittalsStore` method surface from `SqlTransmittalsStore.cs:12`.

**Git:** `git rev-parse --show-toplevel` in `Redirector/` and its parent; `git log -1 --date=short` on
`AGENTS.md`, `TransmittalService.cs`, `SqlTransmittalsStore.cs`, `GraphFacade.cs`, the tests dir;
`git log -S "public static void Initialize" -- Kor.Operations.Graph/GraphFacade.cs`.

**Builds / tests run:** `dotnet build Kor.Transmittals.Redirector.csproj -c Debug`;
`dotnet build Kor.Operations.App.csproj -c Debug`;
`dotnet test Kor.Operations.App.Tests.csproj -c Debug --filter "FullyQualifiedName~TransmittalServiceTests|
FullyQualifiedName~SqlTransmittalsStoreTests|FullyQualifiedName~GraphFacadeTests"`.

**Network probes (GET only, no writes):** DNS for `tracking.korstructural.com` and `KOR-APP01`; TCP connect
to 80/443/8000/8080/5000 on the tracking host and 445/80/443/8000/8080/5500 on `192.168.1.32`;
`GET https://tracking.korstructural.com/` , `/health`, `/kor-logo.png`, `/filedrop?to=<kor addr>`,
`/filedrop?to=<non-kor addr>`, `/t/<freshly generated random GUID>`; TLS certificate inspection.
**I deliberately did not call `/o/{linkId}/{email}` — that endpoint INSERTs.**

**Filesystem (read-only):** `\\KOR-APP01\C$\inetpub\` (depth 0), `\\KOR-APP01\C$\inetpub\Kor.Transmittals.Redirector\`,
`\\KOR-APP01\C$\Windows\System32\inetsrv\config\applicationHost.config`,
`C:\VIsual Studio Projects\_Publish\_Redirector\`, `Redirector\Publish\`. SHA-256 hash comparison of
deployed vs. local publish output for 4 assemblies.

**SQL (SELECT only):** `sys.databases` on `KOR-APP01\SQLEXPRESS` (Windows auth); then `KorTransmittals` via the
`transmittals_app` SQL login found in config — `sys.tables`/`sys.partitions` row counts,
`INFORMATION_SCHEMA.COLUMNS` for 4 tables, and aggregate SELECTs over `Transmittals`, `RedirectTargets`,
`ClickEvents`, `OpenEvents`, `TransmittalRecipients`. I re-ran the app's own `SearchSummaryAsync` SQL verbatim
against production to confirm the dashboard grid would populate.

---

## 2. What this module is

This is KOR's replacement for Newforma Info Exchange, and it is the one part of the suite that has been in
continuous production use with real clients for nine months. A KOR engineer opens the desktop app, picks
"Create Transmittal", chooses a project and a set of drawings or reports, types a subject and remarks, and
picks recipients. The app uploads the files to a per-project dated SharePoint folder
(`{project}/Transmittals/{yyyy}/{yyyy-MM-dd_HHmm}` — `MainWindowWorkflowService.cs:212`), renders a branded
cover-sheet PDF listing every file, uploads that too, creates a SharePoint sharing link, and then sends **one
email per recipient** from the sender's own mailbox via Microsoft Graph. Each recipient's email carries a link
that is unique to them.

That per-recipient link is what the tracking server is for. It points at
`https://tracking.korstructural.com/t/{GUID}` — a small ASP.NET Core service on KOR-APP01 behind IIS. When the
recipient clicks, the service looks the GUID up, writes a `ClickEvents` row recording *who* (their email),
*when*, *from what IP* and *with what browser*, then 302-redirects them to SharePoint. The email also embeds a
1×1 pixel at `/o/{GUID}/{email}` that writes an `OpenEvents` row. Back in the desktop app, the Transmittals
Dashboard lists every transmittal with **Opens** and **Clicks** columns, and selecting a row shows an Activity
list of individual open/click events with recipient email, IP and user agent. The same server hosts a second,
inbound feature: `/filedrop?to=someone@korstructural.com` renders a KOR-branded upload form (the link lives in
staff email signatures) so external collaborators can push large files *in*, which land in
`Incoming Files/{recipient}/{yyyy}/{stamp}` on SharePoint and trigger a notification email to the KOR staffer.

Two things in that flow are more than file movement, and the first version of this report missed both. When
the engineer creates the transmittal they pick a **Purpose** from eight structural-engineering issue types —
"Site Instructions", "For Review", "For Approval", "For Information", "For Comment", "For Permit", "For Bid",
"Issued for Construction (IFC)" (`MainWindow.xaml.cs:110`, default "For Review"). And if the Purpose is
**Site Instructions** and a PDF is attached, an **"Add bookmark notes..."** button appears. It reads the bookmark
outline out of the attached PDF and presents one row per bookmark, so the engineer can write a specific note
against each one — each detail, each sheet, each instruction — and those notes are printed under the file name
on the cover sheet the recipient receives. That is a structural issue workflow, not a document-management
feature; nothing in a general file-transfer tool does it.

**Commercially this matters.** Newforma's current cloud product logs a download *count* and expires links after
two weeks. KOR logs a named, timestamped, IP-attributed event per recipient and keeps it forever. I confirmed
that is real, not aspirational — see §4 and §5 for exactly how far the claim can be pushed and where it breaks.

---

## 3. How you would demo it

**Prerequisites.** (a) On the KOR LAN or VPN — the desktop app reads `KorTransmittals` on
`KOR-APP01\SQLEXPRESS` directly, and there is no remote/API path. (b) A signed-in M365 account for the Graph
device/interactive auth. (c) The redirector needs nothing done to it — it is already running. **[QUERIED]**

**The strong demo — reading, not sending. This is the one to do.**

1. Launch `Kor.Operations.App`. Home screen → **"Search Transmittals"** card (`HomeWindow.xaml:98`) opens the
   Transmittals Dashboard (`DashboardWindow.xaml`, title "KOR NewerForma — Transmittals Dashboard").
2. Search with an empty box, or by project number. The grid returns Created / Sent / Project # / Type /
   Subject / **Opens** / **Clicks**. I ran the dashboard's exact SQL against production: the top rows today are
   real jobs — `31183-01 "updated Foundation plan"` shows **22 opens / 7 clicks**;
   `30978-01 "Dilworth - SSI#32"` shows **30 opens / 10 clicks**. **[QUERIED]**
3. Select a row. The **Activity** list below shows individual events: `Open`/`Click`, timestamp, recipient
   email, client IP, user agent (`SqlTransmittalsStore.cs:362` `LoadActivityAsync`). This is the moment that
   beats Newforma — a named external person, at a named IP, at a named minute. **[READ + QUERIED]** (I verified
   the underlying rows are fully populated: 2,682 click rows, **zero** null IP, zero null user-agent, zero null
   recipient email.)
4. Open a browser to `https://tracking.korstructural.com/health` → `OK`, served by IIS over a valid Let's
   Encrypt certificate. It is a real, public, TLS-terminated service, not a localhost toy. **[QUERIED]**
5. Optionally show `https://tracking.korstructural.com/filedrop?to=<a KOR address>` — the branded inbound
   upload page (see §5 for an honest read on how it looks). **[QUERIED]**

**The live-send demo — possible, but carries a specific trap.** Home → "Create Transmittal" → MainWindow
wizard → pick project, add files, add recipients, Send. It works; 829 of them have been sent. The trap is the
**"External link" checkbox** (`MainWindow.xaml.cs:166`). Unticked — which is the default — the SharePoint link
is created with `Scope = "organization"` (`GraphFacade.cs:610`), and an MVE recipient clicking it lands on a
Microsoft sign-in wall, not the files. If you send live to an MVE address, **tick that box**, or use Quick
Transfer, which always requests an anonymous link (`QuickTransferRunner.cs:150`). **[READ]**

**The differentiator worth demoing — per-bookmark PDF commenting.** In the Create Transmittal wizard set
**Purpose = "Site Instructions"** and attach a bookmarked PDF. An **"Add bookmark notes..."** button appears
(`MainWindow.xaml:415`), shown only when both conditions hold (`MainWindow.xaml.cs:124`). Click it: the tool
walks the PDF's `/Outlines` catalog via `PdfBookmarkExtractor.TryGetBookmarks`, lists every bookmark as a row
grouped by file, and lets the engineer type a note against each. On OK the notes are written back to
`TransmittalFile.PdfBookmarkNotes` by index (`MainWindow.xaml.cs:297`). **They then appear on the issued cover
sheet** — `CoverSheetRenderer.cs:314-328` emits a bullet per bookmark with the note beneath it in smaller
italic grey. **[READ]** Prerequisite for the demo: the PDF must actually contain bookmarks, and Purpose must be
exactly "Site Instructions" — the gate at `CoverSheetRenderer.cs:515-525` is an exact string equality, so
"Site Instruction" singular renders nothing. **Test the specific PDF beforehand.**

**What cannot be demoed:** there is no way to record a *response* to a transmittal, and no closure,
ball-in-court or response-due state (§5 defect 11). And do not offer to make a code change to the redirector
on the day — its source does not compile (§5, defect 1).

---

## 4. Completeness

| Capability | State | Tier |
|---|---|---|
| Multi-file upload to per-project dated SharePoint folder | `WORKING` | QUERIED — 829 rows, latest 2026-08-20 23:39 UTC |
| Branded cover-sheet PDF generated and uploaded | `WORKING` | READ — `UploadOrchestrator.cs:79-95`, `CoverSheetRenderer` |
| Per-recipient email sent from sender's own mailbox via Graph | `WORKING` | QUERIED — 812/829 have `SentAt`; 11 recorded `EmailSendError` |
| Per-recipient tracking link (`/t/{guid}`) | `WORKING` | QUERIED — 4,284 `RedirectTargets`, 774 distinct recipients |
| Click logging: email + IP + user agent + referer | `WORKING` | QUERIED — 2,682 rows, 0 nulls in ip/ua/email |
| Open-pixel logging (`/o/{guid}/{email}`) | `WORKING` | QUERIED — 8,947 rows, latest 2026-08-21 03:12 UTC |
| Dashboard: Opens/Clicks columns + per-event Activity list | `WORKING` | READ (SQL re-run live: QUERIED) |
| Transmittal search (project / subject / date / type) | `WORKING` | READ — `SqlTransmittalsStore.cs:230`, bounded `TOP (@Take)`, LIKE-escaped |
| Inbound file drop (`/filedrop`) + reCAPTCHA + notify email | `WORKING` | QUERIED (page live) / QUERIED (13 `Type='Upload'` rows) |
| Anonymous external SharePoint link | `WORKING` (opt-in, default OFF) | READ — `GraphFacade.cs:610-622`, `MainWindow.xaml.cs:166` |
| **Per-project transmittal *numbering*** | **`PARTIAL`** | **READ — `GraphFacade.cs:352`; see below** |
| `TransmittalRecipients.ClickedAt` / `ViewedFileAt` / `LastActivityAt` | **`DEAD`** | **QUERIED — 0 of 2,133 rows populated** |
| Link expiry | **`STUBBED`** (absent) | QUERIED — no expiry column on `RedirectTargets`; no `ExpirationDateTime` set on Graph links |
| Reminder / nudge for unopened transmittals | **`DEAD`** (absent) | READ — no such code anywhere |
| **Issue type / `Purpose` — 8 values** | `WORKING` | READ — `MainWindow.xaml.cs:110`; default "For Review" (`:112`) |
| **Per-bookmark PDF commenting (Site Instructions)** | `WORKING` | READ — `MainWindow.xaml.cs:124,297`; `BookmarkNotesWindow.xaml.cs`; rendered onto the cover sheet at `CoverSheetRenderer.cs:293-331` |
| `Purpose` persisted to the database | **`DEAD`** (absent) | QUERIED — no `Purpose` column in any of the 52 tables; `SqlTransmittalsStore` never writes it |
| **Response record** (recipient’s answer captured against the item) | **`DEAD`** (absent) | QUERIED + READ — see §5 defect 11 |
| Closure / resolved state, ball-in-court, response-due date | **`DEAD`** (absent) | QUERIED — no such column anywhere in `KorTransmittals` |
| Redirector builds from source | **`DEAD`** | **RUN — 5 compile errors** |
| Redirector under version control | **`DEAD`** | RUN — `git rev-parse` fails in dir and every parent |

**Marker counts (this module's scope):** `TODO` **0**, `FIXME` **0**, `HACK` **0**, `NotImplementedException`
**0**, `NotSupportedException` **0** — across `Redirector/*.cs`, `Kor.Operations.Core`, `Kor.Operations.Graph`,
`Kor.Operations.Data/SqlTransmittalsStore.cs`, and the seven App transmittal files. **[RUN]** That is genuinely
unusual and worth knowing: this code is not littered with unfinished markers.

**Empty / comment-only catch blocks: 4.** `Program.cs:322` (`catch { }` swallowing temp-file delete failure —
benign); `InboundUploadService.cs:127` (`// Logging failure should not break upload`),
`InboundUploadService.cs:158` (`// ignore` — swallows `MarkSentAsync` failure), and
`Program.cs:302` (comment-only catch around `SqlTransmittalsStore` construction). The two in `InboundUploadService`
mean an inbound upload can succeed with **no database record at all** and nobody is told.

**On "per-project transmittal numbering" — correct the claim before it reaches a partner.** KOR does *scope*
the number to the project, but it is not a sequence. `GraphFacade.ReserveTransmittalNumberAsync`
(`GraphFacade.cs:352-358`) returns `$"{projectNumber}-{DateTime.UtcNow:yyyyMMdd-HHmmss}"` — a UTC timestamp
suffix, three lines, no database read, no counter, no collision check. **[READ]** So `31195-01-20260820-230647`,
not `31195-01-0042`. `CoverSheetRenderer.cs:684` (`AdjustTransmittalNoForPacific`) exists solely to re-render
that UTC stamp in Pacific time for the cover sheet — which confirms it is understood as a timestamp, not an
identifier. Sequential numbers that a client can cite in correspondence ("per your Transmittal 42") are a thing
Newforma does and this does not. If MVE's technical lead asks "how do you number transmittals", the honest
answer is "project number plus timestamp", and it should be given rather than discovered.

---

## 5. What is broken or risky

**1. `BEFORE-DEMO` / `SOON` — the redirector source does not compile, and has not since 2026-03-17.**
`dotnet build Kor.Transmittals.Redirector.csproj -c Debug` → **5 errors** **[RUN]**:

```
Program.cs(44,13):              error CS0117: 'GraphFacade' does not contain a definition for 'Initialize'
InboundUploadService.cs(67,54): error CS0117: 'GraphFacade' does not contain a definition for 'Instance'
InboundUploadService.cs(88,44): error CS0117: ... 'Instance'
InboundUploadService.cs(99,43): error CS0117: ... 'Instance'
InboundUploadService.cs(136,31):error CS0117: ... 'Instance'
```

Cause: `git log -S "public static void Initialize" -- Kor.Operations.Graph/GraphFacade.cs` returns commit
**`981907f5`, 2026-03-17, "refactor: ISSUE-003 remove GraphFacade static singleton; go fully DI-only"**. **[RUN]**
The redirector was last published **2026-03-05 16:01** — twelve days *before* that refactor. Because the
redirector sits outside the `Operations` solution, outside its git repo, and consumes
`Kor.Operations.Graph` by `<HintPath>` to a `bin\Release` folder rather than a `ProjectReference`
(`Kor.Transmittals.Redirector.csproj:22-31`), nothing failed at the time and nobody noticed. **The service
that has been logging client transmittal activity for nine months cannot currently be rebuilt from its own
source.** The running binary is fine — it ships its own March-vintage `Kor.Operations.Graph.dll` alongside — but
any change, any redeploy from source, any "let's fix that quickly", is blocked until this is repaired. The fix
is small: reinstate a locally-constructed `GraphFacade` in the redirector instead of the removed static.

**2. `BEFORE-DEMO` — the redirector is not in version control, and the deployed copy is byte-identical to one
developer's local publish folder.** `git rev-parse --show-toplevel` fails in
`C:\VIsual Studio Projects\Redirector` and in every parent directory; there is no `.git` anywhere on that path,
and `C:\VIsual Studio Projects\` shows repos for `App Demo Maker`, `KOR Inspections Bookings`, `KOR.Drafter`,
`KOR.RevitTools` and `Operations` but **not** `Redirector`. **[RUN]**

I did establish that source and deployment agree. SHA-256 of
`\\KOR-APP01\C$\inetpub\Kor.Transmittals.Redirector\Kor.Transmittals.Redirector.dll` is
`F9B31814DFF7F9BC512364C6F12429D2620CD3A8FF2F568B3B235FC49201C166`, **identical** to
`C:\VIsual Studio Projects\_Publish\_Redirector\Kor.Transmittals.Redirector.dll`; `Kor.Operations.Core.dll`,
`Kor.Operations.Data.dll` and `Kor.Operations.Graph.dll` also hash-match. **[RUN]** So the deployed service is
the 2026-03-05 publish of the `Program.cs` on disk (dated 2026-03-05 15:32, i.e. just before the publish), and
that source is authentic. Beware a decoy: the sibling `Redirector\Publish\` folder the task notes points at is
**stale** — it is a 2025-12-04 build whose `deps.json` still names the pre-rename `Kor.Transmittals.Core/Data/Graph`
assemblies, and its DLL hashes do **not** match production. The real publish target is
`C:\VIsual Studio Projects\_Publish\_Redirector` (per `FolderProfile.pubxml:12`). **[RUN]**

The exposure is not drift — it is that a public, internet-facing production service exists as untracked files
on one laptop with no history, no branch, no diff, and no way to answer "what changed and when". Do not put
this on screen as an engineering asset while that is true.

**3. `BEFORE-DEMO` — an Azure AD client secret is hardcoded in source as a default.**
`Program.cs:33`:

```csharp
var graphClientSecret = (builder.Configuration["Graph:ClientSecret"] ?? "<REDACTED — live Entra client secret; see brief/worklog>").Trim();
```

with tenant (`:31`), client id (`:32`) and drive id (`:34`) likewise. No `Graph:*` keys exist in any
`appsettings*.json` on disk **or** on the server (`appsettings.json`, `appsettings.production.json` both read —
neither has a `Graph` section), so **the fallback is what the running service uses.** **[QUERIED]** This is an
app-only Graph credential that uploads to and shares from KOR's SharePoint tenant. It is in a file that is not
in git — which is the only reason it is not in a repo — and it is compiled into the deployed DLL. Treat as
disclosed and rotate.

**4. `BEFORE-DEMO` — SQL credentials and the reCAPTCHA *secret* key are in plaintext in four places, one of
them a tracked git file.** The connection string
`Server=KOR-APP01\SQLEXPRESS;Database=KorTransmittals;User Id=transmittals_app;Password=‹REDACTED — the unmodified scaffold placeholder shipped by the project template›`
appears verbatim in `Redirector/.../appsettings.json:14`, `_Publish/_Redirector/appsettings.production.json`,
the deployed `\\KOR-APP01\C$\inetpub\Kor.Transmittals.Redirector\appsettings.{json,production.json}`, in
`FolderProfile.pubxml.user` (which Visual Studio wrote), and — the one that matters — in
**`Operations/Kor.Operations.App/App.config:162`, which `git ls-files` confirms is tracked.** **[RUN]** The
password is literally the scaffold placeholder `‹REDACTED — the unmodified scaffold placeholder shipped by the project template›` and it was never changed; I
connected with it. The same App.config also carries `McpServer.Password` (`:148`) and the
`KorOpportunitiesDb` password (`:170`). The reCAPTCHA **SecretKey** `6Lc0ziAsAAAAABPt05cB2dL4q68nP0yv7nmT6NAw`
sits beside it in both redirector appsettings files — that is the server-side key; disclosing it lets anyone
verify their own captcha tokens against KOR's site. Note the cross-cutting scan (`02-CROSS-CUTTING-SCAN.md`)
covers hardcoded paths in this area but does **not** flag these credentials.

**5. `SOON` — unauthenticated writes: anyone on the internet can insert arbitrary rows into `OpenEvents`.**
`Program.cs:119` maps `GET /o/{linkId:guid}/{email}` with no auth, no rate limit, no recipient validation and
no dedup — it takes the `{email}` segment straight from the URL and inserts it (`Program.cs:137-145`). The
`linkId` need not even exist: `transmittalId` is simply left null and the insert proceeds. A script can add
rows indefinitely, poisoning the evidence log that is precisely the thing being sold as better than Newforma,
and filling the disk. Anyone who has ever received a KOR transmittal has a valid pixel URL in their inbox. I
did **not** exercise this endpoint — the defect is read from source; the conclusion is that a single
unauthenticated GET writes a row.

**6. `SOON` — raw .NET stack traces are returned to external users.** `Program.cs:185` and `Program.cs:330` (verified):

```csharp
context.Response.StatusCode = 500;
context.Response.ContentType = "text/plain; charset=utf-8";
await context.Response.WriteAsync("FileDrop POST error:\r\n\r\n" + ex);
```

`ex.ToString()` on an ASP.NET Core exception yields the full type, message, inner exceptions and stack frames
with source file paths. There is **no** `UseExceptionHandler` and **no** `UseDeveloperExceptionPage` guard —
this is unconditional, in production. **[READ]** A collaborator at a partner firm uploading a file that trips
any server-side error — a Graph throttle, a SharePoint permission blip, an oversize body — sees a wall of C#.
Related: there is no `MaxRequestBodySize`, `RequestSizeLimit` or `MultipartBodyLengthLimit` anywhere
(all greps = 0), so the framework default (~28–30 MB) applies silently, and a large drawing set will hit it
and produce exactly that stack trace instead of "file too large".

**7. `SOON` — no rate limiting, no auth, no HSTS, no antiforgery, anywhere.** Grepped `Program.cs` for
`AddRateLimiter`, `UseRateLimiter`, `UseAuthentication`, `UseAuthorization`, `RequireAuthorization`, `AddCors`,
`UseHttpsRedirection`, `UseHsts`, `UseExceptionHandler`, `AddAntiforgery` — **all zero**. **[RUN]** The `/filedrop`
POST accepts unauthenticated multipart uploads from anywhere on the internet that will be written into KOR's
SharePoint tenant; the only gates are (a) the `to=` address must end `@korstructural.com`
(`IsKorRecipient`, `Program.cs:353`) — trivially satisfied, staff addresses are public — and (b) reCAPTCHA,
which *is* wired and verified server-side (`Program.cs:235-246`, `VerifyRecaptchaAsync`, `Program.cs:379`) and is the only
thing standing between the internet and unbounded writes to KOR SharePoint. There is no file-type allow-list
and no malware scan. `AllowedHosts` is `"*"` in both appsettings.

**8. `SOON` — `TransmittalRecipients` carries three dead tracking columns.** `ClickedAt`, `ViewedFileAt` and
`LastActivityAt` are populated in **0 of 2,133 rows**, while `PersonalShareLink` is populated in all 2,133.
**[QUERIED]** Nothing writes them (the redirector only touches `ClickEvents`/`OpenEvents`; grep for
`ViewedFileAt` outside the store returns nothing). This is an abandoned first design of per-recipient tracking
sitting next to the working one. Harmless today because the dashboard reads the event tables — but a genuine
landmine for the obvious next feature ("show me which recipients haven't opened it"), and it will read as
half-finished to anyone shown the schema.

**9. `LATER` — inbound uploads can silently lose their database record.** `InboundUploadService.cs:127` and
`:158` swallow `LogTransmittalAsync` and `MarkSentAsync` failures with a bare comment, and
`Program.cs:295-303` swallows store-construction failure entirely, passing `store = null` onward. The files
still reach SharePoint and the notification email still sends, so nobody notices — but there is no row. Only
13 `Type='Upload'` rows exist, and the most recent is **2026-03-19**, five months ago **[QUERIED]**; I cannot
distinguish "nobody uses file drop" from "the logging is silently failing" without server logs, and
`stdoutLogEnabled="false"` in the deployed `web.config` means there are none.

**10. `LATER` — fragile cross-repo build coupling.** The redirector references three assemblies by
`<HintPath>..\..\Operations\Kor.Operations.{Core,Data,Graph}\bin\Release\...` — build *outputs* of a different
repository. All three exist today **[RUN]**, but a `git clean` or a Debug-only build in `Operations` breaks the
redirector build, and (as defect 1 proves) an API change in `Operations` breaks it invisibly. The build also
emits `MSB3277` version conflicts on `Microsoft.Graph.Core` (3.1.22 vs 3.2.5) and
`Microsoft.Kiota.Abstractions` (1.13.0 vs 1.21.1) — the redirector pins `Microsoft.Graph 5.60.0` while
`Kor.Operations.Graph` was built against a newer one.

**11. `SOON` — the workflow is issue-side only. There is no response record. This is the real gap, and it is
narrower and more defensible than the "no RFI/submittal type" claim it replaces.** Two different things get
confused here, so state them separately:

- **Response *tracking* — KOR has this, and it is excellent.** Who opened it, who clicked, from what IP, at
  what minute, per recipient, retained indefinitely. Proven in §4 with 2,682 click rows and 8,947 open rows.
- **A response *record* — KOR has none.** Nothing anywhere captures the recipient's actual answer against
  the item. I checked both halves:
  - **Schema [QUERIED].** `INFORMATION_SCHEMA.COLUMNS` across all 52 tables of `KorTransmittals`, filtered for
    `%respon%`, `%repl%`, `%clos%`, `%resolv%`, `%due%`, `%requiredby%`, `%ballincourt%`, `%answer%`,
    `%status%`, `%assign%`, `%purpose%`, returns **five columns, none of them transmittal-related**:
    `DocumentVersions.Status`, `JobRuns.Status`, `JobTriggers.Status` (FileSync/DMS),
    `PMToolProjectStatus.HealthStatus` and `PMToolTasks.DueDate` (PM Tools). The transmittal tables
    — `Transmittals`, `TransmittalRecipients`, `RedirectTargets`, `ClickEvents`, `OpenEvents` — have **no**
    response, reply, answer, closure, resolved, due-date, required-by, ball-in-court or responsible-party
    column of any kind.
  - **Application [READ].** The complete write surface of `ITransmittalsStore` (`SqlTransmittalsStore.cs:12`)
    is `LogTransmittalWithRecipientsAsync`, `LogTransmittalAsync`, `AddRecipientsAsync`, `MarkSentAsync`,
    `UpdateEmailStatusAsync` — plus three read methods (`SearchSummaryAsync`, `LoadActivityAsync`,
    `SearchHintsAsync`). **Every write is issue-side.** No method records anything the recipient does beyond
    the passive open/click telemetry the redirector writes. A suite-wide grep for
    `ballincourt|respondby|responsedue|requiredby|markclosed|closeout|resolvedat|respondedat|transmittalresponse|awaitingresponse`
    returns nothing in transmittal code; the only `ResolvedAt` hits are the **Collections** (accounts-
    receivable) module, which is unrelated.

**So the honest one-liner is: KOR issues and proves delivery; it does not close the loop.** A transmittal has
no state after "sent" — it cannot be answered, closed or handed back, and nothing tells you which items are
still awaiting a reply. That *is* what Newforma sells with RFIs and submittals, and it is the substantive
functional gap in this module. Say it that way rather than the retracted version: the issue **types** exist
(eight of them, §4) and the per-item **annotation** exists and beats the competition; what is missing is
the return leg.

**12. `SOON` — `Purpose` is never persisted, so none of it is reportable.** The eight issue types drive real
behaviour — the bookmark-notes gate and the cover sheet — but `Purpose` exists only in app state and in the
rendered PDF. There is **no `Purpose` column in any of the 52 tables [QUERIED]**, and `SqlTransmittalsStore`
never writes it (grep: zero hits). Consequences: you cannot query "show me every Site Instruction on
31084-01", the dashboard cannot filter or group by issue type, and the eight-value vocabulary is invisible to
every downstream report. One `nvarchar(64)` column and one parameter would fix it. If MVE asks "can you show
me all outstanding Issued-for-Construction packages", the answer today is no — not because the concept is
missing, but because it was never stored.

**13. `LATER` — the two bookmark gates disagree, and the mismatch loses data silently.**
`CoverSheetRenderer.cs:515-525` gates rendering on `purpose.Trim().Equals("Site Instructions",
OrdinalIgnoreCase)` — exact equality, plural — while the button's visibility gate at
`MainWindow.xaml.cs:124` uses `IndexOf("Site Instruction") >= 0`, a substring match on the **singular**. A
Purpose of "Site Instruction" would show the button and let the engineer type notes against every bookmark,
then render **none of them** on the cover sheet. Today the combo box only offers the plural so this cannot
fire, but `PurposeBox.Text` is read as a fallback at `:124`, and the comment at `:523` ("You can add more
aliases here if you ever rename the purpose") shows the author saw the fragility. Silent data loss on the one
feature that most differentiates the product.

---

## 6. Dependencies

| Dependency | Detail | Reachable off the KOR LAN? |
|---|---|---|
| **SQL Server `KorTransmittals`** on `KOR-APP01\SQLEXPRESS` (16.00.1190) | The tracking store. Also, oddly, home to `FileSync.*`, `PMTool*`, `AspNet*` identity, `UserTeams`, `EmployeeScoreSnapshots`, `FeeProposals` — 52 tables, most unrelated to transmittals. | **No.** 1433 is LAN-only; the desktop app needs LAN or VPN. **[QUERIED]** |
| **IIS on KOR-APP01** | Site `Kor.Transmittals.Redirector`, app pool of the same name, vdir `/` → `C:\inetpub\Kor.Transmittals.Redirector`, single binding `https 192.168.1.32:443:tracking.korstructural.com` (HTTPS only, host-header bound), `hostingModel="inprocess"`, `stdoutLogEnabled="false"`. **[QUERIED]** — read from `applicationHost.config` | **Yes.** NAT'd to public `184.67.29.86:443`. `GET /health` → `200 OK`, `GET /` → `Kor.Transmittals.Redirector OK`, `Server: Microsoft-IIS/10.0`. **[QUERIED]** |
| **TLS certificate** | `CN=tracking.korstructural.com`, Let's Encrypt (`CN=YR2`), **expires 2026-10-09**. **[QUERIED]** | Valid through the demo window; renews well after. |
| **SharePoint / Microsoft Graph** | Site `https://bmzse.sharepoint.com/sites/NewerForma`; app-only client-credential auth (`ClientSecretGraphAuthProvider`) for the redirector, interactive MSAL for the desktop app. Files, folders, sharing links and outbound mail all go through Graph. | **Yes** (cloud), but the *desktop app* still needs LAN for its SQL. |
| **Google reCAPTCHA** | `https://www.google.com/recaptcha/api/siteverify`, called from `/filedrop` POST. Note `VerifyRecaptchaAsync` (`Program.cs:379-384`) does `using var http = new HttpClient()` with **no timeout set** — default 100 s, and a new handler per request (socket-exhaustion pattern). | Yes — but it is an external hard dependency on the inbound path. |
| **`RedirectorBaseUrl`** | `App.config:88` = `https://tracking.korstructural.com` — matches the live host exactly. Required at startup (`GetRequiredAppSetting` throws if absent), so a misconfigured machine fails loudly rather than silently sending untracked links. **[RUN]** | — |
| **Ports 8000 / 8080 / 5000** | Closed/filtered on both the public host and `192.168.1.32`. **[QUERIED]** The comment at `Program.cs:154` still says `https://tracking.korstructural.com:8000/filedrop` — **stale**; the real URL has no port. | — |

Nothing here needs Deltek ODBC, an AI provider, or licensed desktop software. **If the demo is done at MVE's
office, the desktop app needs VPN.** The redirector and the file-drop page need only the internet and will look
identical from anywhere.

---

## 7. Test reality

**`AGENTS.md:10` says `Kor.Transmittals.App.Tests` is "stale test stubs". That is false — flag as STALE-DOC.**
`AGENTS.md` was last committed **2026-06-30**; the test directory was last committed **2026-08-01** — the code
is *newer* than the document describing it, exactly the pattern rule 2 of the rubric warns about. I ran them:

```
dotnet test Kor.Operations.App.Tests.csproj -c Debug --filter "...TransmittalServiceTests|...SqlTransmittalsStoreTests|...GraphFacadeTests"
Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 255 ms
```
**[RUN]** They compile, they run in a quarter of a second, they pass. Note the project is named
`Kor.Operations.App.Tests.csproj` inside a directory still called `Kor.Transmittals.App.Tests` — a leftover of
the rename, and possibly why the AGENTS.md claim was never rechecked.

**But the coverage is thin where it counts, and the numbers flatter it.** The project holds **322** `[Fact]`/
`[Theory]` attributes across 78 files, but only **7** touch transmittals: 3 in `TransmittalServiceTests` (happy
path; one-of-two-recipients-fails aggregates; empty project number throws), 3 in `SqlTransmittalsStoreTests`
(insert / mark-sent / update-email-status, against **SQLite**, not SQL Server), 1 in `GraphFacadeTests`
(mail serialization). Everything else in that 322 is BD, FileSync, financials, and a large family of
*source-analysis* tests (`EmptyCatchBlockTests`, `AsyncVoidTests`, `UnusedPrivateMethodTests`,
`XamlBindingPathTests` …) that lint the codebase rather than exercise it. Those are useful, but they are not
transmittal coverage and should not be counted as such.

**What is not covered at all:** the redirector has **zero tests** — no test project exists for it, and it could
not be referenced by one anyway since it does not compile. So `/t/`, `/o/`, `/filedrop`, the reCAPTCHA path,
`NormalizeUrl` and `IsKorRecipient` are entirely untested. Nothing tests link-GUID generation, the
`RedirectTargets` insert, the open-pixel URL construction, the External-link checkbox behaviour, or the
dashboard's Opens/Clicks aggregation. `SqlTransmittalsStoreTests` running on SQLite means the actual production
SQL (`COUNT_BIG`, `SYSUTCDATETIME`, `TOP (@Take)`, the CTE join in `SearchSummaryAsync`) is never executed by a
test against the engine it targets. Blunt version: the seven tests are real and honest, but they cover the
service class and skip every part of the system that is on the internet.

---

## 8. Demo risk — ranked

1. **"Can we see the code / is it in source control?"** The redirector is untracked *and* uncompilable. If the
   conversation turns to engineering process — and with a technical lead it will — this is the question that
   lands badly. It is also the one that is genuinely fixable in an afternoon (§9).
2. **"Do the links expire?"** No — and the honest answer is worse than "no expiry on our redirect". The
   redirect GUID is only a *tracker*; the thing that actually grants access is the SharePoint sharing link
   behind it, created with `Type="view"` and **no `ExpirationDateTime`** (`GraphFacade.cs:606-624`). When
   `needExternal` is used it is `Scope="anonymous"` — a forwarded email grants a stranger the files, forever,
   and the redirector will faithfully log that stranger's click under the *original* recipient's email,
   silently corrupting the very evidence being demonstrated. Whether the tenant enforces a default expiry on
   anonymous links is a SharePoint admin setting I could not read; **could not verify — check with
   `Get-SPOTenant | Select ExternalUserExpirationRequired, ExternalUserExpireInDays`, or Graph
   `GET /admin/sharepoint/settings`.**
3. **"Are you tracking whether I opened your email?"** Yes — per recipient, with IP and user agent, retained
   indefinitely, with no notice to the recipient anywhere in the email or on any page. That is a defensible
   product decision (it is the headline feature) but it needs a prepared answer, not an improvised one, and BC
   PIPA treats email+IP+timestamp as personal information. An architecture firm's technical lead may well ask.
4. **Overselling "Opens".** 8,947 opens vs 2,682 clicks — a 3.3:1 ratio, with single transmittals showing 22
   and 30 opens **[QUERIED]**. Much of that is corporate mail-scanner and image-proxy prefetch, not humans.
   **Clicks are the defensible evidence; opens are not.** If the number 8,947 is quoted as "times clients
   opened our transmittals" and someone who knows email tracking is in the room, the whole evidence claim gets
   discounted. Lead with clicks and the named Activity list.
5. **The External-link checkbox trap.** Live-send to an MVE address with the box unticked → they hit a
   Microsoft sign-in wall on stage (§3). Entirely avoidable, easily fatal to the moment.
6. **A stack trace on the file-drop page.** If the inbound upload is demoed and anything goes wrong — including
   a file over ~28 MB, which for drawing sets is not unlikely — the partner-facing page returns raw C#
   (§5 defect 6).
7. **"68% of your links were never clicked."** 2,894 of 4,284 `RedirectTargets` have no click **[QUERIED]**.
   There are good reasons (internal CC copies — 1,276 of the rows are `@korstructural.com`; recipients who read
   the attached cover PDF instead). But if this figure surfaces unprepared it reads as a broken product rather
   than a normal distribution list.
8. **"Where do responses live?"** The gap is real, but it is *the return leg*, not the issue types — KOR
   has eight issue types and per-bookmark annotation (§4). What it has no record of is the recipient's
   answer: no closure, no ball-in-court, no response-due date, nothing after "sent" (§5 defect 11). For an
   architecture partner whose daily Newforma workflow is RFIs and submittals this is the substantive
   functional difference, and it should be stated first, by KOR, rather than discovered. The strong framing:
   *"we issue, we annotate per detail, and we prove delivery per recipient — we don't yet track the reply."*
   Note also that `Purpose` is not stored (defect 12), so "show me all outstanding IFC packages" cannot be
   answered from data today.
9. **Cosmetic: the file-drop page.** It is honestly decent — KOR logo, slate `#435363` header, white card,
   orange `#f97316` action button, real client-side upload progress bar, mobile breakpoint at 640px, reCAPTCHA
   present, sensible copy ("Files are stored securely in KOR SharePoint and linked to your KOR contact"), and
   a matching branded thank-you and error page. I fetched it live: 200, 6,938 bytes, logo serves. It will not
   embarrass anyone. Two nits visible in the markup: a duplicated stray quote in the favicon tags
   (`href="/favicon.ico?v=2"" />`, `Program.cs:433-434`) — harmless, browsers recover, but it is in the shipped
   HTML — and every page is built by ~200 lines of `StringBuilder.Append("<div ...>")`, so any future styling
   change means editing C# string literals. Also note the dashboard window title is still
   **"KOR NewerForma — Transmittals Dashboard"** (`DashboardWindow.xaml:5`); "NewerForma" as a name in front of
   a firm that pays for Newforma is a choice worth making deliberately rather than by accident.
10. **The `KorTransmittals` database is a junk drawer.** If the schema is shown, 52 tables appear, including
    `FileSync.*`, `PMTool*`, `AspNetUsers`, `FeeProposals`, `EmployeeScoreSnapshots`, and three
    `*_Backup_20251215` tables left in place. It works, but it does not look designed.

---

## 9. To-do register

| Item | Size | Tag | Why it matters |
|---|---|---|---|
| Put `Redirector/` under git — init, `.gitignore`, commit the 2026-03-05 state as the baseline, push | S | `BEFORE-DEMO` | A public production service with no history is the single least defensible fact in this module, and it is ~30 minutes of work |
| Rotate the Graph client secret at `Program.cs:33`; move tenant/client/secret/drive to env vars or `appsettings.production.json`; delete the hardcoded defaults | S | `BEFORE-DEMO` | An app-only SharePoint credential is disclosed in a file with no version control and compiled into a deployed DLL |
| Change the `transmittals_app` SQL password from `‹REDACTED — the unmodified scaffold placeholder shipped by the project template›`; purge it (and `McpServer.Password`, the Opportunities password, the reCAPTCHA SecretKey) from the **tracked** `Kor.Operations.App/App.config:148,162,170` and both redirector appsettings | M | `BEFORE-DEMO` | The scaffold placeholder password is live in production and committed to git; I connected with it |
| Fix the 5 build errors — construct `GraphFacade` locally in the redirector instead of the removed `Instance`/`Initialize` static — and confirm a clean `dotnet build` | S | `BEFORE-DEMO` | Until this is done no fix to *anything* else on this list can be shipped to the running service |
| Agree the answer to "do the links expire / are you tracking my opens", and check the tenant's anonymous-link expiry setting | S | `BEFORE-DEMO` | Both questions are near-certain from a technical lead; both currently have an unrehearsed answer |
| Decide and rehearse the demo path: dashboard-read (safe) vs. live-send (needs the External-link box ticked) | S | `BEFORE-DEMO` | Avoids a sign-in wall in front of the audience |
| Replace the two `WriteAsync("...error:" + ex)` at `Program.cs:185,330` with a generic message + server-side log; set an explicit body-size limit with a friendly over-limit message | S | `BEFORE-DEMO` | External-facing stack traces on a partner-visible page; ~28 MB is a realistic drawing set |
| Correct every downstream doc repeating "no RFI/submittal record type" — the true gap is **no response record**; issue types exist and there are eight | S | `BEFORE-DEMO` | A wrong disqualifying claim was about to be said to an architecture firm |
| Rehearse the bookmark-notes demo on the **specific** PDF you will use — confirm it has bookmarks and that Purpose is exactly "Site Instructions" | S | `BEFORE-DEMO` | It is the strongest differentiator in the module and it silently renders nothing if either precondition is off |
| Persist `Purpose` — one `nvarchar(64)` column on `Transmittals`, one parameter through `LogTransmittal*Async`, one dashboard filter | S | `SOON` | Eight issue types drive behaviour but are unqueryable and unreportable (defect 12) |
| Reconcile the two bookmark gates (`CoverSheetRenderer.cs:515` exact-plural vs `MainWindow.xaml.cs:124` substring-singular) | S | `SOON` | Silent data loss on the differentiating feature (defect 13) |
| Design a response record: recipient reply, closure state, ball-in-court, response-due date | L | `LATER` | The return leg is the substantive functional gap vs Newforma (defect 11); too large for this runway |
| Add expiry to `RedirectTargets` (`ExpiresAt` column + a `WHERE` clause in `/t/`) and set `ExpirationDateTime` on the anonymous Graph link | M | `SOON` | Turns "we have no expiry" from a gap into a deliberate, configurable policy — and closes the forwarded-link hole |
| Rate-limit `/t/`, `/o/` and `/filedrop`; validate that `{email}` in `/o/` matches the `RedirectTargets` row for that `linkId` before inserting | M | `SOON` | Stops anyone on the internet poisoning the evidence log that is this module's competitive claim |
| Either populate `TransmittalRecipients.ClickedAt`/`ViewedFileAt`/`LastActivityAt` from the event tables, or drop the three columns | M | `SOON` | 0 of 2,133 populated; it is a trap for the next feature and reads as unfinished |
| Replace the `<HintPath>` references with `ProjectReference`s, or pull the redirector into the `Operations` solution | M | `SOON` | The root cause of defect 1 — an API change in another repo broke this one invisibly for five months |
| Log, rather than swallow, the failures at `InboundUploadService.cs:127,158` and `Program.cs:302`; enable `stdoutLogEnabled` or add Serilog to the redirector | S | `SOON` | Cannot currently tell "file drop is unused" from "file drop's logging is broken" — last `Upload` row is 2026-03-19 |
| Correct `AGENTS.md:10` — the tests are not stale stubs; 7 of 7 pass in 255 ms | S | `SOON` | The claim is actively steering people away from a working test project |
| Replace timestamp "numbering" (`GraphFacade.cs:352`) with a real per-project sequence | M | `LATER` | Client-citable transmittal numbers are a Newforma behaviour KOR does not have; not needed for the demo, needed for the pitch to be literally true |
| Fix `href="/favicon.ico?v=2""`; update the stale `:8000` URL comment at `Program.cs:154`; decide on "NewerForma" in `DashboardWindow.xaml:5` | S | `LATER` | Cosmetic, but all three are visible to someone looking closely |
| Move the non-transmittal tables (`FileSync.*`, `PMTool*`, proposals, identity) out of `KorTransmittals` | L | `LATER` | Schema hygiene; only matters if the database is ever shown |

---

## 10. Verdict

**Demo-able with care — and it is the strongest thing in this audit so far, provided the right half is shown.**
The production evidence is real and I verified it directly: 829 transmittals across 137+ project numbers,
running continuously from 2025-11-28 to today, with the most recent sent hours ago; 4,284 per-recipient
tracking links to 741 distinct external addresses at firms including Arcadis, Greystar, Wesbild, Anthem and
JWDA; 2,682 click events with zero missing IP, user-agent or recipient email; and **730 of 829 transmittals
(88%) carry at least one recorded open**. The competitive claim holds: KOR really does log a named,
IP-attributed, per-recipient event where Newforma's cloud product logs a download count — and unlike Newforma
those links do not expire, which is simultaneously the advantage and the security gap. Two corrections before anyone repeats the brief to a partner firm. Transmittal "numbering" is a
**UTC timestamp**, not a per-project sequence (`GraphFacade.cs:352`). And the first version of this report was
wrong about issue types: KOR has **eight** (`Purpose`, `MainWindow.xaml.cs:110`), plus a genuine
differentiator the first pass missed — **per-bookmark PDF commenting** on Site Instructions, where the
engineer annotates each bookmark inside the attached PDF and those notes print on the cover sheet the client
receives (`CoverSheetRenderer.cs:314-328`). The real functional gap is narrower and worth stating plainly:
**the workflow is issue-side only.** There is no response record — no closure, ball-in-court or
response-due field in any of the 52 tables, and every write method on `ITransmittalsStore` is issue-side
[QUERIED + READ]. KOR issues, annotates and proves delivery; it does not close the loop. Pitch it that way.

**The single most important thing to fix is the redirector's build and version-control status**, together,
because they are one problem: the service has been outside git and uncompilable since commit `981907f5` on
2026-03-17 removed `GraphFacade.Instance` from a repository the redirector consumes by `bin\Release` HintPath.
Nine months of client transmittal evidence currently depends on a binary whose source cannot be rebuilt and
whose history does not exist. That is perhaps a day's work to resolve — `git init` plus reinstating a
locally-constructed `GraphFacade` — and until it is done, this module should be demonstrated as a *working
product* (the dashboard, the Activity log, the live `/health` and file-drop pages) and not opened up as an
*engineering artifact*. Rotate the hardcoded Graph secret and the placeholder SQL password in the same pass;
both are live, and one of them is committed to git.
