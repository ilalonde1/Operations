# Module 08 — BD Desktop Surface (Opportunities · BusinessDevelopment · Crm)

**Audited** 2026-08-20 · **Scope** `Kor.Operations.App/{Opportunities,BusinessDevelopment,Crm}`
**Auditor note:** this is the module MVE is most likely to see on screen after the AI. It was audited
as a user interface, not only as code.

> **One-line summary.** The code is unusually clean — it builds, it has zero `TODO`/`FIXME`/`HACK`
> markers in ~34,000 LOC, and no button is unbound. **The problem is not defects, it is disclosure:
> the default landing screen of the BD workspace renders, at load and with no click, a list of 25
> named architecture firms next to KOR's written plan to displace their structural engineers — and
> MVE is an architecture firm that is already in the database.**

---

## 1. What I searched

**Repo / paths**
- `Kor.Operations.App/Opportunities` (43 `.cs`, 19 `.xaml`, **13,139** LOC — task brief said 8,811)
- `Kor.Operations.App/BusinessDevelopment` (39 `.cs`, 16 `.xaml`, **14,127** LOC — brief said 11,014)
- `Kor.Operations.App/Crm` (21 `.cs`, 6 `.xaml`, **6,625** LOC — brief said 5,236)
- `HomeWindow.xaml{,.cs}`, `App.xaml.cs`, `App.config`, `KorDeepLink.cs`, `CompositionModules/OpportunitiesModule.cs`
- `Kor.Opportunities.Data/BdReports/Generators/*`, `.../MajorProjects/SqlPursuitBriefStore.cs`,
  `.../MajorProjects/SqlBdDashboardStore.cs`, `.../Awards/SqlKorClientBdIntelligenceStore.cs`
- `Kor.Operations.App/Services/AppAiService.cs`, `Kor.Operations.App/Financials/`, `Kor.Operations.App/PMTools/`

**Git**
- `git log -1 --date=short -- <folder>` → Opportunities **2026-07-13**, BusinessDevelopment **2026-07-13**,
  Crm **2026-07-09**, Briefs **2026-07-10**, Reports **2026-07-01**, Workspace **2026-07-13**
- `git log main..develop -- <folders>` → **0 unmerged commits**; tree clean apart from this audit dir
- `git show ee633975` (the "kill the dead Bazaar pill" commit) — verified what actually landed

**Greps**
- `TODO|FIXME|HACK|XXX|WIP|TBD|NotImplementedException|NotSupportedException` per folder
- `"C:\\|D:\\|E:\\|\\\\KOR|Program Files|OpsArchive"` (hardcoded paths)
- `coming soon|not implemented|not yet|placeholder|Lorem|Sample|No data|N/A|Coming next` (on-screen strings)
- `new <X>Window(|GetRequiredService<X>|AppServices.Get|Activator.CreateInstance` — reachability/orphans
- every `Click=` / `Command=` attribute in all 41 XAML files, each resolved to its handler body
- `DispatcherUnhandledException|AppDomain.CurrentDomain.UnhandledException` (→ **zero hits**)
- `ex.ToString()|StackTrace` in MessageBox arguments (→ **zero hits**)
- `WebView2|CoreWebView2`, `OdbcConnection|DSN=`, `MVE|McLarand|Vasquez Emsiek`

**Builds / tests `[RUN]`**
- `dotnet build Kor.Operations.App/Kor.Operations.App.csproj -c Debug` → **succeeded, 0 errors, 2 warnings**
  (both `NU1902`, AngleSharp 0.17.1 known-vulnerability, transitive via `Kor.Opportunities.Data`)
- `dotnet test .../Kor.Operations.App.Tests.csproj -c Debug --filter` over the nine architectural
  detectors → **20 tests, 18 passed, 2 failed** (~20 s)
- **Gotcha:** the test csproj is `Kor.Operations.App.Tests.csproj` **inside** the
  `Kor.Transmittals.App.Tests/` directory. A path built from the folder name fails with `MSB1009`.

**Live queries `[QUERIED]`** — `KorOpportunitiesDb` on `KOR-APP01\SQLEXPRESS`, **SELECT only**, via
`System.Data.SqlClient` using the connection string from the `KOR_OPPORTUNITIES_OPPORTUNITIESDB` **User**
env var (**not** set at Machine scope on this workstation). 67 base tables enumerated; row counts; the
production `SqlBdDashboardStore` queries re-executed verbatim; full MVE entity sweep across
`CanonicalOrg / OrgAlias / IntelPerson / IntelPersonAffiliation / KorPursuits / CrmEngagements /
MajorProjectsInventory / OrgFact / ArchitectDisplacementBriefs / OpportunityInterestedFirms /
CanonicalOrgEnrichment / OpportunityAwards`.

**Host probes `[QUERIED]`** — TCP 1433 → KOR-APP01 open. `http://kor-app01:5500/health` →
`200 {"status":"ok","service":"Kor.Operations.Mcp","version":"0.4.2+5b9535f7"}`;
`kor-app01` resolves to **192.168.1.32** (RFC1918). WebView2 Evergreen **151.0.4129.93** present.
Microsoft Edge present at the **x86 path only**. **Mulish font NOT installed** on this machine.

---

## 2. What this module is

This is the desktop front end over the **BD Brain** — the ingestion/enrichment/scoring pipeline that
pulls public procurement notices, building permits, major-project inventories and AI research into
`KorOpportunitiesDb`. The data is real and large `[QUERIED]`: 2,599 opportunities (921 unclaimed),
10,286 major projects (2,457 active), 10,033 historical opportunities, 139,472 award records, 778,931
canonical organisations, 12,974 people, 1,075 pursuits, 83 industry events. Three folders provide three
doors onto it. **Opportunities** is the inbound funnel — the live notice grid, scoring profile,
duplicate guard, ingestion-run history, and the deep-dive dossiers for an organisation, person, buyer,
competitor and historical RFP. **BusinessDevelopment** is the shell a BD person lives in: a 190px
left-hand nav rail (`BdWorkspaceWindow`, **not** a TabControl) of 14 buttons swapping a single
`ContentHost`, plus a reports factory (`BdReportsWindow`, 11 analytical + 11 sector reports rendered as
HTML in a WebView2 with DOCX/PDF export) and a six-shape brief generator. **Crm** is the pursuit-tracking
layer — engagements, contacts, activities, outcomes — joined to Deltek so a BD record carries the
client's real financial history.

They are **not three coherent features; they are one feature that grew three UIs**, and the seam is in
the *navigation*, not the code. The folder a file lives in tells you almost nothing about which screen
it is: `Crm/CrmView` is the **"Pursuits"** button, `Crm/BdTrackingView` is **"BD Tracking"**,
`Opportunities/MajorProjectsInventoryView` is **"Future Projects"**, and
`Opportunities/CompetitionInfoView` is **"Market History"** — all four hosted inside the
*BusinessDevelopment* workspace. Conversely `BdReportsWindow` — the entire Reports module — has **no
entry in the BD rail at all**; its only open site is `HomeWindow.xaml.cs:194`. A demo driver who opens
"Business Development" cannot reach Reports without backing out to Home. The genuinely healthy part is
`OrgDossierWindow`, deliberately reused as the single organisation renderer from **eight** call sites
across all three folders, with explicit comments saying so (`BazaarView.xaml.cs:139`: *"reusing
OrgDossierWindow, never re-rendering intel here"*). The duplication that does exist is **analytical, not
visual** — see §5(g).

---

## 3. How you would demo it

**Prerequisites**
- **KOR LAN or VPN — non-negotiable.** `KorOpportunitiesDb` (TCP 1433, verified open `[QUERIED]`) and the
  MCP AI gateway at `http://kor-app01:5500` = **192.168.1.32**, an RFC1918 address over **plaintext
  HTTP**. There is no offline mode and no internet route. At MVE's office or on a hotspot, this module
  shows nothing and the AI button fails.
- The **Deltek ODBC DSN** (`App.config:5`, `Vp.Dsn=Deltek`) must exist **on the presenting machine** for
  every financial panel. Without it the org dossier prints a raw `OdbcException` (§5b).
- WebView2 runtime for `BdReportsWindow` (present here, degrades gracefully).
- Microsoft Edge for brief PDF export (present at the x86 path; QuestPDF fallback otherwise).

**Click path** `[READ]` — three entry points, all on `HomeWindow` (`HomeWindow.xaml:330/348/366`):

1. **Home → "Opportunities"** (`HomeWindow.xaml.cs:187`) → `OpportunitiesWindow` → `OpportunitiesView`.
   The live funnel grid, promote-to-pursuit, duplicate guard, scoring profile editor.
2. **Home → "BD Reports"** (`:194`) → `BdReportsWindow`. 11 analytical + 11 sector reports, rendered
   live into a WebView2, exportable to DOCX/PDF. **The strongest visual in the module.**
3. **Home → "Business Development"** (`:201`) → `BdWorkspaceWindow`. The 14-button rail:
   *FIND* — Dashboard · Opportunities · Future Projects · *PURSUE* — Pursuits · Pursuit Monitor ·
   *KNOW* — Relationships · BD Scorecard · Market History · Events · BD Tracking · *TOOLS* —
   Make a Brief… · Proposals · Brochures · Admin.

**⚠️ The default landing screen is the most dangerous screen in the module.** `BdWorkspaceWindow` opens
on `DashboardView`, which renders "Open Structural Seats" and "Competitor Watch" **at load, with no
click** — see Risk 1. **Do not open Business Development in front of MVE without the mitigation in
item 3 of §9.**

**The safest demo** is `Home → BD Reports → Executive Overview`, then `→ Teaming Heat-Graph`, then
`→ Priority Treemap`. If the workspace must be shown, navigate immediately to **Pursuit Monitor** or
**Events** and never return to Dashboard. **Read §8 in full before demoing anything else.**

**It can be demoed today.** The build is green, the data is populated, the services are up.

---

## 4. Completeness

| Capability | State | Evidence |
|---|---|---|
| App builds | `WORKING` — 0 errors, 2 NU1902 warnings | `[RUN]` |
| Opportunities funnel grid + promote-to-pursuit | `WORKING` — 2,599 rows | `[RUN]` + `[QUERIED]` |
| Scoring profile editor | `WORKING` — `ScoringProfile` table has **0 rows**, but the VM falls back to `ScoringOptions.KorDefaults()` (`ScoringProfileViewModel.cs:113-116`), so the window is populated, not blank | `[READ]` + `[QUERIED]` |
| Org dossier (shared renderer, 8 call sites) | `WORKING` | `[READ]` |
| Person / Buyer / Competitor / Historical-RFP dossiers | `WORKING` | `[READ]` |
| Major Projects Inventory + REF# jump box | `WORKING` — 10,286 rows | `[READ]` + `[QUERIED]` |
| BD Workspace rail (14 buttons, 15 handlers) | `WORKING` — every handler exists with a real body; all 6 `IsEnabled="False"` are enabled by code | `[READ]` |
| BD Reports — 11 analytical + 11 sector | `WORKING` — none are stubs | `[READ]` |
| BD Reports — 3 HTML visuals | `WORKING` — **contradicts the "PENDING COMMIT" doc** (§STALE-DOC). Caveat: DOCX export of these three is a **bitmap screenshot**, not text (`BdReportsWindow.xaml.cs:302-307`) | `[READ]` |
| Brief generator — 6 shapes on the canonical template | `WORKING` — headless-Edge print, 60 s timeout, QuestPDF fallback. **But "Make a Brief…" exposes only 5**; the Opportunity shape has no tab | `[READ]` |
| CRM engagements / contacts / activities | `WORKING` — 255 / 97 / 114 rows | `[QUERIED]` |
| Deltek financial panels | `PARTIAL` — works on-LAN with the DSN; renders a raw exception string otherwise | `[READ]` |
| **Pursuit Brief — "The Play"** | **`STUBBED`** — `SqlPursuitBriefStore.cs:82` hardcodes `ThePlay: null`. Always renders *"coming with the AI Crucible."* — **and exports into the PDF** | `[READ]` |
| **Pursuit Brief — "Fit Score"** | **`STUBBED`** — `SqlPursuitBriefStore.cs:83` hardcodes `FitScore: null`. Same string, same PDF | `[READ]` |
| **Pursuit Brief — "Draft approach" (AI)** | **`PARTIAL` / dangerous** — works on-LAN, but **renders its own error text as a successful draft** (§5c) | `[READ]` |
| `CompetitionInfoWindow` | **`DEAD`** — DI-registered `OpportunitiesModule.cs:216`, **zero resolutions**. Superseded by `CompetitionInfoView` | `[RUN]` |
| `MajorProjectsInventoryWindow` | **`DEAD`** — registered `:124`, zero resolutions. Superseded by the View | `[RUN]` |
| `PrimePipelineWindow` + `PrimePipelineViewModel` | **`DEAD`** — registered `:126-127`, zero resolutions | `[RUN]` |
| `Briefs/RegionBriefDialog` | **`DEAD`** — registered `:94`, zero resolutions (161 LOC). Absorbed into `BriefsMakerWindow`'s Region tab | `[READ]` |
| `BazaarView` | `WORKING` — the *pill* was removed 2026-07-09, but the **view is the default content of the "Opportunities" rail button** (`OpportunitiesHubView.xaml.cs:26`). Not orphaned; 3 stale user-visible "Bazaar" strings remain | `[RUN]` |
| Typeahead single-click commit | `WORKING` — **shipped 2026-06-20** | `[RUN]` |

**≈1,409 LOC unreachable** across the four dead surfaces (1,248 in Opportunities + 161 in Briefs).

**Marker counts** `[RUN]` — all three folders:

| Marker | Opportunities | BusinessDevelopment | Crm |
|---|---|---|---|
| `TODO` / `FIXME` / `HACK` / `XXX` | **0** | **0** | **0** |
| `NotImplementedException` | **0** | **0** | **0** |
| `NotSupportedException` | 0 | 4 | 1 |
| Empty/comment-only `catch` | 16 | 16 | ~16 |
| Total `catch` sites | 104 | — | 83 |
| `MessageBox.Show` | 31 | 40 | 23 |

**Zero `TODO`/`FIXME`/`HACK` in ~34,000 LOC** is genuinely exceptional. All five
`NotSupportedException`s are the standard `IValueConverter.ConvertBack` stub. Of the ~48 empty catches,
the large majority are correct `catch (OperationCanceledException) { }`; the repo's own detector counts
only **3** as violations (§5a).

---

## 5. What is broken or risky

### (a) Two failing architectural tests — the repo's own gate is red `[RUN]`

`EmptyCatchBlockTests` — *"Found 3 empty catch block(s)"*: `HtmlBriefPdfGenerator.cs:347`, `:362`, `:375`.
**Cosmetic.** All three are cleanup guards in the headless-Edge path — `File.Delete`, `proc.Kill(true)`,
`Directory.Delete(scratch)`. They need an explanatory comment, not a redesign.

`AsyncVoidTests` — *"Found 2 `async void` method(s)"*: `BdWorkspaceWindow.xaml.cs:157`
(`OpenOrgDossier`) and `MajorProjectsInventoryView.xaml.cs:131` (`OpenRef`).
**Cosmetic — I read both.** Each wraps its entire awaited body in `try/catch(Exception)` with a logged
warning and a user-facing `MessageBox`; neither can escape. They lack the `// async-void OK:`
annotation. **A two-minute fix that turns the module's signal green.**

**But the wider `async void` picture is not clean.** There is **no global unhandled-exception handler
anywhere in the app** `[RUN]` — `DispatcherUnhandledException` / `AppDomain.CurrentDomain.UnhandledException`
return **zero hits**; `App.xaml.cs:25-46` guards only `OnStartup`. Five `async void` handlers have work
*outside* their try block and are safe only because their callees happen to self-guard today:
`HistoricalOpportunityDetailWindow.xaml.cs:22` (no try/catch at all),
`CompetitorProfileWindow.xaml.cs:31`, `OrgDossierWindow.xaml.cs:38`, `IngestionRunsWindow.xaml.cs:31`,
`KorPursuitDialog.xaml.cs:51`. **Safe by coincidence, not by design.**

### (b) Raw exception text rendered on screen — the most likely live failure `[READ]`

Nine ViewModels catch broadly and write `$"{ex.GetType().Name}: {ex.Message}"` to a user-facing status
line — `OpportunitiesViewModel.cs:447`, `MajorProjectsInventoryViewModel.cs:402`,
`OrgDossierViewModel.cs:554`, `PersonDossierViewModel.cs:144`, `IngestionRunsWindow.xaml.cs:63`,
`CrmViewModel.cs:456/616/626`, `BdTrackingViewModel.cs:306`, `ClientIntelligenceViewModel.cs:278`.

Worse, three render the text in the body of the UI rather than a status bar:
- `OrgDossierView.xaml:925` binds `DeltekSnapshot.ErrorMessage`, set at `OrgDossierViewModel.cs:543` to
  `ex.GetType().Name + ": " + ex.Message`.
- `OrgDossierViewModel.cs:501` hardcodes a developer string: *"IDeltekClientContextService.LoadAsync
  returned null - Clendor has no row for this ClientId on the App's ODBC connection."* — **an interface
  name and a method name, on screen, in front of a client.**
- `HistoricalOpportunityDetailViewModel.cs:123` prints **`File not found: C:\OpsArchive\Opportunities\…`**
  — a raw server-local path that blames the user's own C: drive.

Off-LAN, the audience reads `SqlException: A network-related or instance-specific error occurred…` or
`SqlException: Login failed for user 'opportunities_app'.` **A ten-line `SqlException` → "Can't reach
the BD database — check VPN" mapping removes the worst live failure mode.**

**Credit where due:** **no `MessageBox` anywhere prints a stack trace** `[RUN]` — all 94 `MessageBox.Show`
calls across the three folders pass `ex.Message` only.

### (c) 🔴 "Draft approach" reports failure as success `[READ]`

`AppAiService.AskAsync` **never throws**; it returns its error as the answer string —
`AppAiService.cs:72` *"AI is not configured…"*, `:135` *"AI service returned HTTP {status}…"*,
`:149` *"Unable to reach AI service: {ex.Message}"*.

The caller checks only for whitespace (`PursuitBriefWindow.Approach.cs:67`), then renders the string and
sets `ApproachStatus.Text = $"Drafted {DateTime.Now:HH:mm} from live intel — regenerate any time…"`.

**So a down or unreachable gateway paints `Unable to reach AI service: No such host is known.` into the
Approach card under a confident green "Drafted 14:32 from live intel."** This is the worst possible live
failure: it looks like it worked. Compounding it, `_mcpHttp.Timeout = TimeSpan.FromMinutes(4)`
(`AppAiService.cs:31`) and the call passes `CancellationToken.None` (`Approach.cs:65`) with **no cancel
button and no spinner** — worst case the demo sits frozen on one sentence for **four minutes**.

### (d) Buttons that silently do nothing — the real "dead button" risk `[READ]`

**No control in this module is unbound** (§7 proves it with a passing detector). But **~19 controls
`return;` silently when nothing is selected**, with no `IsEnabled` guard and no message. Clicked live
they are indistinguishable from broken:

*Opportunities* — **"Start Pursuit"** (the *primary* styled button, `OpportunitiesView.xaml:48` →
`.xaml.cs:278`), "Edit…" (`:227`), "Move to → New/Pursuing/Submitted/Won/Lost" (`:575`),
"Open in Pursuits…" (`:296`), MPI "Own"/"Dismiss" (`:174`/`:197`).

*Crm* — "Set stage" (`CrmView.xaml.cs:452`), "Edit…" (`:507`), Contacts **Add** (`:555` — the guard fires
*before* the "enter a name" validation at `:562`, suppressing even that message), Contacts Remove
(`:594`), Activity **Log** (`:622`), Files Open (`:762`), Files Remove (`:784`).

The right pattern already exists three feet away — `OpportunitiesView.xaml.cs:104-109` shows
`MessageBox.Show("Select an opportunity first.", …)`. And `MajorProjectsInventoryViewModel.cs:225`
already exposes `HasSelected`, which **nothing binds**.

Two more that *look* dead but aren't:
- **"Refresh intel"** (`OrgDossierView.xaml:251` → `OrgDossierViewModel.cs:660`) only *queues* a trigger;
  the status reads *"…Worker picks up within 30s; research takes ~6 min."* It also **spends AI research
  budget on one click**, with no cost confirmation.
- **"Done"/"Dismiss"** on recommended actions (`OrgDossierViewModel.cs:128-131`): on DB failure the catch
  **only logs** — no status, no dialog. The row stays and the button looks dead.

And one that is genuinely redundant: `DashboardView.xaml:256` **"→ Dossier"** is byte-for-byte identical
to the org-name button four columns left (`DashboardView.xaml.cs:143-145` vs `:121-138`), per its own
comment *"the dedicated CRM panel hookup is covered by the separate CRM rebuild task"*.

### (e) 🔴 Buttons that write, send email, or spend money — brief whoever drives `[READ]`

| Control | file:line | Effect |
|---|---|---|
| "Grab selected" | `BazaarView.xaml:21` | **DB write** — claims the opportunity firm-wide |
| "Not for us…" | `BazaarView.xaml:25` | **DB write** — removes from the pool |
| **"Reassign…"** | `OverwatchView.xaml:21` → `.xaml.cs:187` | **DB write + sends a real Microsoft Graph email** to a KOR staffer |
| "Own it" / "Not for us…" | `PursuitBriefWindow.xaml:42,48` | **DB writes** |
| "✓ Done" / "✕ Dismiss" | `DashboardView.xaml:250,253` | **DB writes** |
| "Run Now" / Enabled checkbox | `AdminView.xaml:130,117` | **Queues a live scraper job** / toggles a schedule |
| "Run Source ▾" | `OpportunitiesView.xaml:80` | Live scrape against BC Bid / APC |
| "Recalc all" | `ScoringProfileWindow.xaml:38` | Loops **2,599 rows**, one `UpdateAsync` each (confirm-gated) |
| "Refresh intel" | `OrgDossierView.xaml:251` | Spends AI research budget |

### (f) Hardcoded absolute paths `[RUN]` — 5 total, exactly matching the cross-cutting scan (3 + 2)

| Location | Path | Breaks elsewhere? |
|---|---|---|
| `HistoricalOpportunityDetailViewModel.cs:145,146,148` | translates `C:\OpsArchive\…` → `\\KOR-APP01\C$\OpsArchive\…` | **Yes.** `C$` is an **administrative share** — needs local-admin on KOR-APP01 *and* LAN. A normal BD user gets `File not found: C:\OpsArchive\…` on screen. **`CompetitionInfoSourcesWindow.xaml:49` advertises a non-admin share `\\KOR-APP01\OpsArchive\…` that the code never tries.** |
| `HtmlBriefPdfGenerator.cs:332-333` | the two standard Edge install paths | **No** — `FirstOrDefault(File.Exists)` → QuestPDF fallback. Caveat: a **per-user** Edge install (`%LOCALAPPDATA%`) misses both and silently downgrades. Edge is present here at the **x86 path only**, so the two-path probe is load-bearing. |

Not literal paths but same class: `OpportunitiesView.xaml.cs:126`, `OrgDossierWindow.xaml.cs:56`,
`BriefsMakerWindow.xaml.cs:404` and `RelationshipsView.xaml.cs:153` write briefs **straight to the
Desktop with no Save-As dialog**, then `Process.Start` them. On a shared screen that litters a visible
Desktop and opens the PDF in the default handler over the app. `PursuitBriefWindow.xaml.cs:172` and
`BdReportsWindow.xaml.cs:284` do it properly with a `SaveFileDialog` — level the inconsistency.

### (g) 🔴 The same number, computed two or three ways, on screens one click apart `[READ]`

The premise that `CrmAnalyticsService` is "a third analytics implementation alongside Financials and
PMTools" is **off by one axis**: `PMTools/HistoricalAnalyticsService.cs` computes *nothing* (a 73-line
facade over `Kor.Operations.Business/Analytics/*`), and **neither PMTools nor `Financials/` contains any
win-rate, won-count or proposed-fee math** — those concepts live only in the CRM/BD stack. The real
duplication is worse, because it is *within* this module:

1. **Win rate contradicts the app's own written methodology.**
   `Financials/MetricDefinitions/Definitions.Bd.cs:46-47` states in bold that *"a loss counts ONLY where
   WonLostOutcome = Lost (2) … **Including [NoBid or Withdrawn] in the denominator drastically
   understates the real win rate**."* `CrmAnalyticsService.cs:76` ignores `WonLostOutcome` entirely:
   `var lost = live.Count(e => e.Stage == CrmEngagementStage.Lost);`
   The module *actively collects* the distinction — `CrmOutcomeDialog.xaml:30-35` offers **Lost /
   No-bid / Withdrawn** — and the enum folds all three into `Lost = 7`
   (`CrmEngagement.cs:129`). **Every no-bid and withdrawal inflates the loss denominator.** The Pursuits
   screen reports a win rate the app's own dictionary says is wrong and low.
2. **"Client Lifetime Fee" means three different things, two of them on screen.**
   Crm = `Σ PR.Fee`, hourly revenue **excluded** (`DeltekClientContextService.cs:329-331`); Financials
   Clients tab = `Σ (PR.Fee + HourlyRevenue)` (`FinancialsService.cs:1070`); the tooltip explaining it =
   `Σ PRSummaryMain.BilledFee` (`Definitions.Bd.cs:127`). Client attribution differs too: Financials
   falls back to `PR.ClientID` with a comment that without it *"~2,146 projects with $10.4M of contract
   fee … get bucketed as (unknown)"*; the CRM path has no fallback. **The CRM client dossier is
   structurally low twice over.**
3. **Two "wins with this client" counters, from two ledgers, in the same window.**
   `ClientIntelligenceWindow`'s BD Intelligence tab reads `opportunities.KorPursuits` with a *string*
   `Stage` (`SqlKorClientBdIntelligenceStore.cs:60-68`); the Pursuits screen reads `CrmEngagements`.
   The app's own AI prompt names the hazard: `AskService.cs:1029` — *"DOUBLE-COUNT WARNING: 177 Won
   engagements are a flat historical backfill … **NEVER add CRM win counts to Deltek/KorPursuits win
   counts**."* One client can read `Won 5 / Lost 0` on one tab and `0W / 0L` one click away.
4. **FX: flat 1.36 in CRM** (`DeltekClientContextService.cs:129` ← `App.config:60`) **vs a per-year table
   in Financials** (2026 = **1.378457**, `PartnerFinancialsViewModel.cs:585-587` ← `App.config:64`).
5. **Zero-loss renders differently:** `CrmAnalyticsService.cs:77` yields a bare **`100%`**;
   `AttributionViewModel.cs:65-68` hedges with **`"100% (no losses recorded yet)"`**.
6. Also: `ByStage` is un-segmented while every sibling field excludes backfill
   (`CrmAnalyticsService.cs:61` vs `:68`); the backfill literal is matched **untrimmed** in C#
   (`:49-50`) but **trimmed** in T-SQL (`SqlBdAttributionStore.cs:57`), so `" Deltek.CustomProposal"`
   counts as *live* in one and *backfill* in the other; `ProposedFee` is a bare `decimal?` with **no
   currency dimension** yet is rendered as CAD (`CrmViewModel.cs:342`); and `BdTrackingViewModel.cs:190-193`
   adds a **fourth**, dollar-weighted "CAPTURE RATE" on a 22pt tile beside the count-based win rate.

**In fairness, `CrmAnalyticsService` is the best-engineered of the set** — the only one using UTC date
arithmetic (`:104`), the only one returning `null` rather than `0` for an undefined average (`:106-108`),
it segments populations and documents why inline (`:13-18`), and with zero DB coupling it is trivially
unit-testable. Which makes the total absence of tests for it the more frustrating.

### (h) Copy defects a technical lead will read as sloppiness `[RUN]`

- **20 user-visible strings lost their em-dash separators**, codepoint-verified — a lossy edit left bare
  `U+0020 U+0020`. Proof: `OrgDossierView.xaml:785` still has a surviving `<Run Text=" — ">` while its
  siblings do not. Worst cases are date ranges that now read **`2015-03-01  2024-11-20`** with no
  separator (`BuyerProfileViewModel.cs:72`, `CompetitorProfileViewModel.cs:207`) and
  **`12 contracts  $4,300,000 lifetime`** (`:78`, `:232`). Also
  `OrgDossierViewModel.cs:675,683`, `CompetitionInfoSourcesWindow.xaml:4,10`, and 12
  whitespace-only `Run` separators across `CompetitorProfileWindow.xaml` / `OrgDossierView.xaml` /
  `PersonDossierWindow.xaml` (note `OrgDossierView.xaml:536` is `"  "` but `:538` is `" "` — inconsistent,
  confirming loss).
- **Two buttons referenced by names they don't have.** `OpportunitiesViewModel.cs:440` says *click
  "New Opportunity"* — the button is labelled **"New"**. `OpportunitiesView.xaml:374` says *Click
  'Promote to Pursuit'* — the button is labelled **"Start Pursuit"**.
- `MajorProjectsInventoryViewModel.cs:389` renders **`0 projects - `** with a trailing dash on empty.
- **Three stale docstrings** naming nav buttons that no longer exist: `BazaarView.xaml.cs:16` ("Bazaar"),
  `OverwatchView.xaml.cs:20` ("Overwatch" → now "Pursuit Monitor"), `AttributionView.xaml.cs:14`
  ("Attribution" → now "BD Scorecard"). Plus `HtmlBriefPdfGenerator.cs:17-18` claims only Org and Region
  render there when in fact **all six shapes do**.
- **Three stale "Bazaar" strings still reach the user** despite the 2026-07-09 cleanup commit:
  `BazaarView.xaml:24` (tooltip), `BazaarViewModel.cs:187` (*"removed from the Bazaar"*), `:245` (AI context).

### (i) Blank and unhelpful empty states `[READ]`

- **`RelationshipsView` opens two-thirds blank** — `RelationshipsViewModel.cs:89-94` clears `Orgs` and
  sets `CountDisplay = "Type to search…"`; `DossierHost` (`RelationshipsView.xaml:167`) has no default
  content.
- **`CrmView` has no loading indicator and no empty state** — `CrmViewModel.IsLoading` (`:284-288`) is
  **never bound**; a failed load is a blank grid plus a 12px status line.
- `BazaarView`, `OverwatchView`, `AttributionView`, `EventsView` have **no in-grid empty state**.
- **`AttributionView` renders five giant em-dashes** across 24px bold stat tiles when the load fails
  (`AttributionViewModel.cs:54-57,64` → `AttributionView.xaml:43-67`), reason buried in 12px grey.
- Two **unreachable** error dialogs: `BdTrackingView.xaml.cs:47-50` and `:81-84` can never fire, because
  `BdTrackingViewModel.LoadAsync` swallows and never rethrows.
- Three **invisible-in-Release** failures: `ClientIntelligenceViewModel.cs:239,252,265` swallow to
  `Debug.WriteLine`.
- **Silent degradation that reads as data:** `PursuitBriefViewModel.cs:244-248,268-271,291-294` — a
  Deltek failure nulls the fields, so the UI shows *"No prior KOR work on record with this owner."*
  **indistinguishable from "the query failed."**

### (j) Credentials in clear text — committed to the repository `[READ]`

`Kor.Operations.App/App.config` carries three plaintext passwords, all the same string:
- `:148` `McpServer.Password` — the AI gateway
- `:162` the `KorTransmittals` DB
- `:170` `Server=KOR-APP01\SQLEXPRESS;…;Password=…` for `KorOpportunitiesDb`

The same value also sits in the `KOR_OPPORTUNITIES_OPPORTUNITIESDB` user env var `[QUERIED]`. Not
displayed by any UI, but it ships in the app zip and is one Alt-Tab away if the config is opened.
**Flagging for the cross-cutting report; not this module's to fix.**

### (k) Non-deterministic PDF branding `[RUN]`

`PursuitBriefPdfExporter.cs:59` and `BriefPdfGenerator.cs:1388` both request
`TextStyle.Default.FontFamily("Mulish")`. **Neither registers the font.** The only registration in the
solution is a **static constructor of a different class in a different assembly**
(`Kor.Operations.Rendering/Brochure/BrochureRenderer.cs:25-34`), which runs only if the user opens the
Brochure Builder first. **Mulish is not installed on this machine** `[RUN]`. So an exported Pursuit Brief
falls back to QuestPDF's default face — **or not — depending on whether Brochures was clicked earlier in
the session.** Client-facing artifact, non-deterministic branding.

### (l) Performance and query hygiene `[READ]`

- **Every content view is `AddTransient`** (`OpportunitiesModule.cs:129-142`), so each rail click
  constructs a fresh view and re-runs its full load — including Bazaar's ~1,600-row fetch. Back-nav
  rebuilds too, discarding scroll, selection and filters. **Expect a visible pause on every nav click.**
- `BazaarViewModel.cs:108-115` pulls up to 5,000 rows and filters unclaimed/undismissed **in LINQ**
  — against the repo's own working rule 4. Push it to SQL.
- `SqlCrmEngagementStore.cs:48-51` is an **uncapped** `SELECT … FROM opportunities.CrmEngagements`.
- `DeltekClientContextService.cs:376-377` returns **ALL** of a client's master projects, uncapped.
- `_backStack` (`BdWorkspaceWindow.xaml.cs:265`) is unbounded.

### (m) Data quality visible on the first screen `[QUERIED]`

- **The top 8 rows of the Opportunities grid by relevance score contain the same White Rock tender
  `WR26-021` four times** (scores 40, 40, 35, 35). That is the module's first screen.
- `CanonicalOrg` holds **778,931** rows with gross duplication — `Mbm Ventures Inc.` appears at least 12
  times, `CAROLINE HACHEM-VERMETTE` a dozen more.
- **Duplicate people:** Matthew McLarand exists **twice** (`IntelPerson` 13493, 19248); Carl McLarand
  twice; **Chase Rongé three times** (13491, 19251, 20042); Daniel Gura three times; Pieter/Peter Berger
  three times.
- `IntelPerson` 20042 is stored as **`Chase RongÃ©, AIA, NCARB`** — UTF-8/CP1252 double-encoding mojibake.
- `IntelPerson` 19251 holds **`crongé@mve-architects.com`** — a non-ASCII character in an email local
  part; 13491 has the correct `cronge@`.

**In a demo whose headline is entity resolution, duplicate and mojibake'd entities are the most
on-the-nose possible failure.**

---

## 6. Dependencies

| Dependency | Detail | Reachable off the KOR LAN? |
|---|---|---|
| **`KorOpportunitiesDb`** on `KOR-APP01\SQLEXPRESS` | The entire module. SQL auth (`opportunities_app`) | **NO** — TCP 1433, verified open on-LAN `[QUERIED]`. No offline cache. |
| **Deltek Vantagepoint via ODBC System DSN `"Deltek"`** | `Crm/DeltekClientContextService.cs`, `DeltekLookupService.cs` (12+ `OdbcConnection` sites); catalog `C0000052267P_1_KOR00000000`. Tables: `Clendor`, `PR`, `PRSummaryMain`, `AR`, `Contacts`, `PRContactAssoc`, `Activity` | **NO** — machine-level DSN + DataDirect driver; host not internet-routable |
| **MCP AI gateway `http://kor-app01:5500`** | `AppAiService.cs:111` `/ask`, Basic auth. **= 192.168.1.32, plaintext HTTP** | **NO** — RFC1918, LAN-only. `/health` → 200 `[QUERIED]` |
| **SMB `\\KOR-FS01\BD Brain`** | `App.config:119` → pursuit file attachments (`PursuitFileStorage.cs`) | **NO** — SMB 445. Note `FilesEnabled` is true if merely *configured*, so an unreachable share still shows an empty Files panel |
| **`\\KOR-APP01\C$\OpsArchive`** | Historical-opportunity documents | **NO** — **admin share**, LAN + local-admin |
| **Microsoft Graph** | "Reassign…" sends a real email (`OverwatchView.xaml.cs:187`) | Cloud — works off-LAN, which is its own risk |
| **WebView2 Evergreen** | `BdReportsWindow.xaml:240`. Degrades gracefully (`.xaml.cs:73`) | Local. **151.0.4129.93 present** `[RUN]` |
| **Microsoft Edge (headless print)** | `HtmlBriefPdfGenerator.cs:325`, 60 s timeout, QuestPDF fallback | Local. **Present at x86 path only** `[RUN]` |
| **Mulish font** | QuestPDF PDF branding | **NOT INSTALLED** `[RUN]` — see §5(k) |

**Bottom line for a remote or at-MVE demo: this module shows nothing useful without VPN, and the AI
"Draft approach" button will fail *while claiming to have succeeded*.** Present from a machine on the
KOR LAN with the Deltek DSN configured, or not at all.

---

## 7. Test reality

**Test project:** `Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj` — **78
test files** for the whole app.

**Behavioural coverage of this module is lopsided and largely absent** `[RUN]`:

| Area | Dedicated test files |
|---|---|
| `BdReports/` (report generators) | **14** |
| `Opportunities/` | **0** |
| `BusinessDevelopment/` (Workspace, Briefs) | **0** |
| `Crm/` | **0** |

The only near-miss is `Financials/MetricDefinitions/BdMethodologyKeysTests.cs`, which asserts key
*strings* — not behaviour. **Nothing exercises the win-rate math, the BD-tracking SQL, the share-copy
path, or `CrmAnalyticsService` at all** — which is precisely where §5(g) found the contradictions.

What partially rescues this is a set of **nine repo-wide architectural detectors** that scan all XAML and
C# rather than testing behaviour. I ran them `[RUN]`: **20 tests, 18 passed, 2 failed** (~20 s).

The **18 passing** are the best evidence in this audit against the "dead button" fear:
- `UnboundCommandPropertyTests` — **passes**: no XAML binds a command that does not exist.
- `XamlBindingPathTests` — **passes**: every binding path resolves to a real property.
- `UnusedViewModelPropertyTests`, `UnusedAppResourceKeyTests`, `DuplicateResourceKeyTests`,
  `XamlStaticResourceOrderTests`, `SilentBroadCatchTests` — all pass.

I independently confirmed by hand: **all 50 `Click=` handlers in Opportunities, all 60 in
BusinessDevelopment, and all handlers in Crm resolve to real methods with real bodies.** Zero missing.

**One detector has a proven blind spot.** `OrphanedPublicTypeTests.cs:441` (`CountRealLocalUses`) is a
*textual identifier* heuristic, so the line `services.AddTransient<PrimePipelineWindow>();` counts as a
consumer. That is exactly why **four DI-registered-but-never-resolved surfaces (~1,409 LOC) pass the
gate** (§4). Fix: treat a lone DI registration as *not* a consumer.

**Blunt read:** behavioural coverage outside `BdReports/` is **theatre** — but the structural detectors
are unusually good, and they are why I can state with `[RUN]` evidence that there are **no unbound
commands and no broken bindings**. The real exposure is what a binding detector cannot see: the two
hardcoded-null Pursuit Brief fields, the AI error-as-success path, the ~19 silent no-op controls, and
the analytics contradictions.

---

## 8. Demo risk — ranked

### 🔴 RISK 1 — The default BD screen leaks competitor strategy **at load, with no click** — CRITICAL

`BdWorkspaceWindow` opens on `DashboardView`. Two panels render immediately:

**"Open Structural Seats"** (`DashboardView.xaml:561-600`) — columns `Architect`, `Market`, `Status`, and
**`DisplacementRead`**. I executed the exact production query (`SqlBdDashboardStore.cs:28-42`) `[QUERIED]`:
**25 named architecture firms with KOR's written displacement strategy in a visible column.** Verbatim:

> **`Perkins&Will`** — *"Perkins+Will uses five different structural firms across their portfolio —
> explicitly rotating by project typology and scale. Fast + Epp is …"*
> **`Christopher Bozyk Architects`** — *"…KOR should proactively reach out to **Christopher Bozyk and
> Sandra Bai**…"* ← **named individuals at another architecture firm**
> **`MCM Architects (Musson Cattell Mackey)`** — *"MCM has no in-house structural and is the architect on
> a multi-tower Broadway Plan pipeline (~5 towers). They hire external SE each p…"*

**"Competitor Watch"** (`:633-670`) — `Firm` + **`CapacityRead`**. 12 rows `[QUERIED]`:

> **`Bush, Bohlman & Partners`** — *"at capacity — three major hospital projects running simultaneously…"*
> **`Herold Engineering`** — *"in transition — Englobe acquisition (May 2025) is a material ownership
> event; integration dynamics uncertain…"*
> **`RJC Engineers`** — *"growing fast — 27 open roles, 12 new principals/associates in one year…"*
> plus `Fast + Epp`, `Entuitive`, `Aspect`, **`Glotman Simpson`**, `Equilibrium`, `Stantec`, `AECOM`, `Kontur`, `Quaak`

**"Priority Actions"** (`:210-264`) also renders `Recommendation` and **`TargetPersonName`** as
*"target: {name}"* — named individuals at target firms. Collapsed by default, but the count badge
advertises it and the state persists per session.

**This is a target list of architecture firms with the incumbent engineer named and the plan to unseat
them. MVE is an architecture firm.** Clicking "Business Development" in front of them is the single
highest-risk action in this audit.

---

### 🔴 RISK 2 — MVE is in the database as a target, with 18 named staff — CRITICAL

| What exists `[QUERIED]` | Detail |
|---|---|
| `CanonicalOrg` **Id 76952** | `MVE + Partners`, **`Kind = Architect`**, **`ClendorClientId = 326C3B2E9C844E35BE9DDF544CEF7409`** (so MVE is also a **Deltek client**) |
| `OrgAlias` | 3 rows — `MVE`, `MVE + Partners` |
| **`IntelPerson`** | **18 rows with real `@mve-architects.com` addresses** — **Matthew McLarand** (President) + LinkedIn, **Carl McLarand**, **Daniel Gura**, **Chase Rongé**, **Pieter Berger**, **Sherwin Pineda**, Mark S. Kim, Luis Arambula, Kara Dunne, Kenneth Nilmeier, David Arnold, Charles Pigg |
| `IntelPersonAffiliation` | **25 rows** — titles and departments, i.e. a reconstructed MVE org chart |
| **`CanonicalOrgEnrichment`** | 8 providers incl. `FirmNarrative`, whose payload holds `decisionMakers: [{"name":"Matthew McLarand","title":"President + Director of Design","linkedinUrl":…` |
| **`MajorProjectsInventory`** | **12 MVE projects**, each with the **incumbent structural engineer named** |

The 12 projects, exactly as the grid renders them:

| Id | Project | `StructuralEngineerName` |
|---|---|---|
| 8281 | Ritz-Carlton Residences Newport Beach | **Glotman Simpson Consulting Engineers** |
| 8330 | Irvine Co. UCI Research Park | **Glotman Simpson (confirmed)** |
| 8325 | Rafferty (Santa Ana) | **Englekirk/WSP** |
| 8280 | OC Vibe Residential | **John A. Martin & Associates** |
| 8278 | Gateway Crossing (Orlo + Alba) | **Nelson Structural Engineers** |
| 8334 | Gateway Crossing NorCal (San Jose) | **Nelson Structural + FBA Inc.** |
| 8279, 8293, 8313, 8329, 8333, 10709 | 2518 Mission College · The Becker · Onni Mid-Wilshire · Discovery Park · Mission College (SC) · Riverwalk Studio City | *(blank)* |

**Four one-keystroke paths reach it:**

1. **BD Workspace global search** — `BdWorkspaceWindow.xaml:71` hosts an `OrgSearchTypeahead`;
   `.xaml.cs:69` wires `OrgSelected → OpenOrgDossier`. Typing `MVE` matches org 76952, and since
   **single-click-to-commit shipped** (`b8ef48f0`), a *single click* opens it. **There is no longer a
   highlight-without-opening safety margin.**
2. **`ClientIntelligenceWindow`'s free-text Deltek search** — `ClientIntelligenceWindow.xaml:67-70`
   queries the **whole Clendor client master** on every keystroke (`DeltekLookupService.cs:68-75`), and
   its tooltip *invites* it: *"Type any part of a client name (e.g. 'vancouver', 'NSDA', 'bird')"*.
   `OpportunitiesView.xaml.cs:410-421` opens this window **with no client preselected**. MVE has a
   `ClendorClientId`, so MVE is in Clendor.
3. **`RelationshipsView`** — the `Kind` filter (`RelationshipsView.xaml:82-88`) explicitly includes
   **`Architect`** and **`Competitor`**.
4. **Deep link** — `KorDeepLink.cs:39` resolves `kor://org/76952`; BD reports embed `kor://` links.

**Verified absent `[QUERIED]` — the good news:**
`ArchitectDisplacementBriefs` for org 76952 → **0**. No brief `BriefJson` mentions MVE → **0**.
`KorPursuits` and `CrmEngagements` tied to 76952 → **0** and **0**. `OrgFact` → **0**.
**There is no KOR fee, margin or win/loss record against MVE.**

**But the repo carries the strategy document**, outside the app:
`docs/bd-dossier-mve-mclarand-2026-06-17.md` names Matthew McLarand as *"Jim's personal contact"*, states
*"in KOR's ERP the MVE relationship is **dormant — 2 prior projects, none active**"*, estimates MVE's
revenue at *"~$22–23M"*, and sets the play: *"Displacing Glotman on MVE's next OC luxury high-rise,
through Jim ↔ Matthew, is the #1 play."* **Do not open this repository or a file dialog near it.**

---

### 🔴 RISK 3 — "Generate Brief" on MVE prints KOR's receivables against them — CRITICAL

`RelationshipsView.xaml.cs:230-257` builds an `OrgBriefDeltekSection` with `LifetimeFee`,
`ArOutstanding`, `Ar90Plus` and per-project `Fee`/`FeeBilled`. Rendered at
`HtmlBriefPdfGenerator.cs:481,486,500-501`:

```csharp
Fact("Lifetime fee",  dk.LifetimeFee.ToString("C0", …));
Fact("AR outstanding", $"{dk.ArOutstanding:C0} ({dk.Ar90Plus:C0} at 90+)");
```

— identically in the DOCX (`BriefGenerator.cs:1092,1101-1104`) and the QuestPDF fallback
(`BriefPdfGenerator.cs:1159,1168-1171`).

**MVE has a `ClendorClientId`.** Selecting MVE in Relationships and clicking "Generate Brief" produces a
PDF stating **what KOR has billed MVE over their lifetime and how much of MVE's money is more than 90
days past due**, writes it **to the Desktop**, and **opens it**. And because
`BriefPdfGenerator.cs:1635-1644` parses the `FirmNarrative` `decisionMakers` shape into a **"Key people
on file"** section, the same PDF prints **MVE's own executives back at them under a KOR letterhead.**

---

### 🔴 RISK 4 — The "DISPLACEMENT BRIEF" panel — CRITICAL

`OrgDossierView.xaml:610-800` renders a section literally headed **`DISPLACEMENT BRIEF`** (`:618`), with
**`KOR'S DISPLACEMENT ANGLE`** (`:663`), **`RECOMMENDED FIRST MOVE`** (`:685`), **`Current structural
incumbents`** + `ExploitableWeakness` (`:701`), **`Architect's active pipeline`** (`:737`),
**`Decision-makers`** with *"Approach via:"* / *"Picks structural:"* (`:773,790,795`), and
`VERIFY BEFORE ACTING` (`:814`). The source comment is explicit: *"the per-architect BD playbook … **Architects only.**"*

**97 rows live** `[QUERIED]`. Verbatim from the DB:

> `Andrew Cheung Architects` — *"korDisplacementAngle": "Lead with: (1) No structural incumbent is
> publicly identified across ACA's active Richmond, Surrey, and Vancouver portfolio — **the seat is open
> and contested by no entrenched firm**…"*

Gated on `HasDisplacementBrief`, and MVE has none — **so MVE's own dossier will not show it.** But the
market split includes **3 San Diego briefs**, among them **JWDA Inc.** (priority `high`) and **Carrier
Johnson + Culture** (priority `high`). Per KOR's own MVE dossier, **Chase Rongé — MVE's San Diego
director — is ex-Carrier Johnson.** Opening any SoCal architect's dossier shows MVE a recommended first
move against a firm in their home market with a personnel tie to their own principal.

**Any org dossier for a `Kind=Architect` org is unsafe.**

---

### 🔴 RISK 5 — `CompetitorProfileWindow` labels architecture firms "DIRECT COMPETITOR" — CRITICAL

`CompetitorProfileViewModel.cs:168-179` maps `AgentKorOverlapScore` to a coloured chip rendered at
`CompetitorProfileWindow.xaml:60-67`: **`DIRECT RIVAL`** (≥9, Crimson) · **`DIRECT COMPETITOR`** (≥7) ·
`PARTIAL OVERLAP` (≥5) · `ADJACENT` (≥3) · `NOT COMPETING`.

**Architecture firms are in this dataset with hostile labels** `[QUERIED]`:

| Vendor | overlap | chip rendered |
|---|---|---|
| **NORR ARCHITECTS & ENGINEERS LIMITED** | 7 | **DIRECT COMPETITOR** |
| **ROBSON DESIGN BUILD LTD.** | 7 | **DIRECT COMPETITOR** |
| ARCHITECTURE49 INC | 6 | PARTIAL OVERLAP |
| ZEIDLER ARCHITECTURE INC. | 3 | ADJACENT |
| Diamond And Schmitt Architects Inc | 3 | ADJACENT |
| Adamson Associates Architects | 3 | ADJACENT |
| MORIYAMA & TESHIMA, ARCHITECTS | 2 | NOT COMPETING |

The window also shows `AgentCompetitionNotes` verbatim and **named executives at the other firm**
(`AgentVendorLeadership`, `:176-188`) — e.g. `Adamson Associates Architects → [{"Name":"Marc Salette","Title":"Principal"},…]`.
Scale: **5,000 award rows carry competition notes; 4,867 carry an overlap score; 41,238 distinct
vendors** `[QUERIED]`.

**One click reaches it from the demo path:** BD Workspace → **Market History** → Awards tab → click any
name in the **Winner** column (`CompetitionInfoView.xaml.cs:91-99`). The Awards grid itself already
carries **`Header="Competes"`** (`:125`) and **`Header="Agent Profile (AI)"` at `Width="380"`** (`:144`) —
the AI's verdict on each competitor, inline, no drill-down needed.

*(This corrects my earlier read that Market History was "reasonably safe as public procurement data".
The public award rows are safe; the two AI columns bolted onto them are not.)*

---

### 🟠 RISK 6 — `BdTrackingView` and `ClientIntelligenceWindow` — HIGH

**`BdTrackingView`** opens with `"All Regions"` / `"All"` (`BdTrackingViewModel.cs:55-56`) — a scrollable
grid of **every firm KOR tracks**, with columns `Region · Initiator (KOR staff) · **Contact (named
person at the client firm)** · Company · **Submitted $** · **Accepted $**` (`BdTrackingView.xaml:149-170`).
Click a row and the detail pane adds `POTENTIAL PROJECTS` free text and **the full Activities feed
including `{Binding Body}`** (`:297-299`) — **verbatim internal notes about that client, written by KOR
staff, unfiltered and unbounded.** That `Body` field is the highest-risk single control in the module.

**`ClientIntelligenceWindow`** is a full commercial dossier: **Lifetime fee** (`:129`), per-project
**Fee/Fee billed** uncapped (`:228-237`), **AR Total Outstanding + 90+ in red** (`:283-304`), **up to 200
named contacts with Title/Email/Phone/CellPhone** (`:262-278`), the **raw `Clendor.Memo`** free-text
internal commentary in a 320px box (`:133-136`), classification pills including **`Recommended`** and a
red **`Competitor`** (`:94-113`), annual revenue (`:154-169`), the Deltek **activity log with KOR staff
names** (`:309-313`), **"Lost to"** per pursuit (`:357-368`), and an **"Awarded to"** grid naming other
firms and their contract values (`:433-462`).

`DeltekClientIntelligenceFormatter.cs` puts the **same material into the AI context** — lifetime fee
(`:28`), AR (`:60-62`), contact emails (`:84`), and **the raw Memo truncated to 400 chars** (`:100-107`).

**And `CrmView` leaks per-client revenue with no window at all**: `CrmViewModel.cs:373-377` renders
*"{Client}: {N} project(s), lifetime fee {£}, last opened {date}"* as a persistent amber strip beside
**every selected pursuit** (`CrmView.xaml:208-212`), with the `Competitor`/`Recommended` pills beneath
(`:221-235`), a Contacts grid showing Name/Role/Email/Phone (`:283-286`), and a plan-takers line naming
**up to 12 competing firms bidding the same RFP** (`CrmViewModel.cs:720`).

---

### 🟠 RISK 7 — `PursuitBriefWindow` puts named people and fee data on screen and into the AI — HIGH

`ArchitectWarmthDisplay` (`PursuitBriefViewModel.cs:379-402`) renders *"KOR knows {N} people at {firm}"*
plus **up to 12 named individuals with titles**; `ArchitectContacts`/`OwnerContacts` grids show
**Name/Title/Role/Email** (`:214-244`); `KorEdgeDisplay` shows *"KOR client: {N} projects · {$X}
lifetime"*; `OwnerBdRecordDisplay` shows *"{W} won · {L} lost"*; `CompetitorNotes` and
`RecurringStructuralPartners` name the incumbent SE firms to displace; `DisplacementRead` repeats the
strategy. **All of it exports via "Export PDF"** (`PursuitBriefPdfExporter.cs:149-203`).

**And most of it is transmitted** by `PursuitBriefWindow.Approach.cs:102-131` — including architect
contact **email addresses** (`:145-147`) and competitor capacity reads (`:127-129`) — over **plaintext
HTTP** to `http://kor-app01:5500`. The prompt (`Approach.cs:25-38`) instructs the model to produce
*"## Who to call"*, *"## Call script"* and *"## Draft email"* — **a ready-to-send cold-call script
targeting a named person at another firm**, rendered inline at `PursuitBriefWindow.xaml:80`. Run it on a
pursuit where MVE is the architect and the app writes a script targeting the people in the room.

---

### 🟠 RISK 8 — "BD Scorecard" publishes KOR's win rate and per-staff performance — HIGH

`AttributionView.xaml` shows *Wins (CRM)* · **Won fee (proposed)** · *Active pursuits* · *Submitted* ·
**Win rate** (`:44-68`), plus **"Wins by owner"** — **named KOR staff** with **Won fee** (`:118-120`).
**177** `CrmEngagements` carry both `ProposedFee` and `TargetMargin`, and 177 carry `OutcomeNotes`
`[QUERIED]`. Showing a prospective client your win rate and target margins is a negotiating handicap —
and per §5(g) the win rate shown is **wrong and low** by the app's own methodology.

`OverwatchView` (Pursuit Monitor) additionally renders `OwnerDisplay` as a **raw KOR staff UPN/email**
(`OverwatchRowView.cs:59`) beside a red "cold" badge — a public staleness scoreboard for named employees.

---

### 🟠 RISK 9 — A literal "Coming next" roadmap panel, one click from Market History — HIGH

`CompetitionInfoSourcesWindow.xaml:114` is a section header reading **`Text="Coming next"`**, followed at
`:116-122` by three **unbuilt** features: *"Bidders tab — all bidders + their bid prices…"*, *"Detail
panel on row-click…"*, *"CSV export of any filtered view."* Line `:106` adds *"The **future** row-detail
panel will surface the join automatically…"*.

The same dialog also discloses KOR's methodology and infrastructure to an outside audience: `:41` *"We
**scrape** the public list page…"*, `:53` *"The enrichment scraper runs every 5 minutes, the document
downloader every 10"*, `:78` *"bid prices disclosed by buyers, **not yet legally finalized**"*, and `:49`
prints the internal server path **`\\KOR-APP01\OpsArchive\Opportunities\<Id>\`** on screen.

Reached from the Awards tab "About sources" button (`CompetitionInfoView.xaml:49,114`).

---

### 🟡 RISK 10 — "coming with the AI Crucible." on the flagship BD screen — MEDIUM

`SqlPursuitBriefStore.cs:81-83` hardcodes `KorEdge: null, ThePlay: null, FitScore: null`. `KorEdge` is
recomputed at the window layer, but **`ThePlay` and `FitScore` are never populated by anything.** So in
**every** Pursuit Brief, 100% of the time, the "The play / fit" card (`PursuitBriefWindow.xaml:286-299`)
renders — greyed by `PlaceholderBrushConverter.cs:13` — as:

> **The play / fit** · *coming with the AI Crucible.*
> Fit score: *coming with the AI Crucible.*

**And it exports into the PDF** (`PursuitBriefWindow.xaml.cs:191-192` → `PursuitBriefPdfExporter.cs:213-224`).
**A brief handed to MVE would carry the phrase "coming with the AI Crucible."** Knock-on: because both
are null, `Approach.cs:133-134` never appends the `KOR'S EDGE` or `THE PLAY` blocks to the AI prompt —
two of eight intended context sections are permanently dead.

*(The four other placeholder strings are honest empty states and are fine on screen: "No prior KOR work
on record with this owner.", "No KOR contacts on record at this owner.", "No KOR pursuit history with
this owner.", "No KOR contacts on record at this firm.")*

### 🟡 RISK 11 — ~19 controls silently no-op with no selection (§5d) — MEDIUM
Including **"Start Pursuit"**, the primary styled button on the Opportunities screen.

### 🟡 RISK 12 — Off-LAN or missing DSN → raw `OdbcException`/`SqlException` on screen (§5b) — MEDIUM

### 🟡 RISK 13 — Duplicate and mojibake'd data on the first screen (§5m) — MEDIUM
`WR26-021` four times in the top 8; `Chase RongÃ©`; Matthew McLarand twice.

### 🟡 RISK 14 — 20 lost em-dash separators and 2 wrong button names (§5h) — MEDIUM
The details a visiting technical lead reads as sloppiness.

### 🔵 RISK 15 — ~1,409 LOC of dead windows (§4) — LOW
Invisible in a demo; a code reviewer would find it.

### 🔵 RISK 16 — `AngleSharp 0.17.1` CVE warning on every build — LOW

---

### Concrete list — screens to AVOID or FILTER in front of MVE

**Do not open:**
1. **`BdWorkspaceWindow` → Dashboard** — the default screen. `DisplacementRead` × 25 firms + `CapacityRead` × 12 rivals, **at load**.
2. **Relationships** — org list filtered to `Architect`/`Competitor`; "Generate Brief" emits AR + lifetime fee + their own execs.
3. **BD Tracking** — grid of firms, named contacts, $ figures, and verbatim internal notes.
4. **Client Intelligence** — free-text Clendor search + full commercial dossier.
5. **Market History → Awards** — the `Competes` and `Agent Profile (AI)` columns; **never click a Winner or Buyer cell**.
6. **BD Scorecard** — win rate, per-staff won fee.
7. **Pursuit Monitor** — KOR's live pipeline with staff UPNs.
8. `BdReports` → **Architect Frequency** (MVE ranks **#14** `[QUERIED]`), **Competitor Intelligence**, **Strategic Relationships**, **Pursuit Dossiers**, **Opportunity Attack Cards**.
9. `OrgDossierWindow` for **any `Kind=Architect` org** or **any org with a `ClendorClientId`**.
10. `PersonDossierWindow` for anyone at another firm (12,974 `IntelPerson` rows).
11. **"About sources"** from Market History — the "Coming next" roadmap.
12. `MajorProjectsInventoryView` searched for "MVE" — 12 rows with incumbent SEs.

**Never type `MVE`, `McLarand`, `Glotman`, `Englekirk`, `WSP`, `Nelson`, `Carrier Johnson` or `JWDA`**
into the workspace `GlobalSearch` (`BdWorkspaceWindow.xaml:71`) or the `ClientSearchBox`
(`ClientIntelligenceWindow.xaml:67`).

**Reasonably safe:**
- `BdReports` → **Executive Overview**, **Teaming Heat-Graph**, **Priority Treemap** (aggregate — confirm no label resolves to a SoCal firm first)
- **Events**, **Admin** *(caveat: Admin's `Base URL` column discloses KOR's full source list and crawl schedules)*, **Ingestion Runs**, **Job Run History**
- **Opportunities** funnel grid — public notices *(caveat: the `WR26-021` duplicate is visible in the top 8)*

---

## 9. To-do register

| # | Item | Size | Tag | Why it matters |
|---|---|---|---|---|
| 1 | **Rehearse a fixed click-path; print the §8 avoid-list.** Have someone drive who knows which buttons write, email or spend (§5e). | S | `BEFORE-DEMO` | Every critical risk here is a navigation choice, not a code defect. |
| 2 | **Suppress the two Dashboard panels** — "Open Structural Seats" `DisplacementRead` (`DashboardView.xaml:589`) and "Competitor Watch" `CapacityRead` (`:659`) — or land the workspace on a different view. | S | `BEFORE-DEMO` | The default screen. Highest-risk item in the audit. |
| 3 | **Gate the `DISPLACEMENT BRIEF` panel** behind a demo flag (`OrgDossierView.xaml:610`), and hide the `Competes` + `Agent Profile (AI)` columns (`CompetitionInfoView.xaml:125,144`). | S | `BEFORE-DEMO` | Four `Visibility` bindings cover four of the six critical disclosures. |
| 4 | **Suppress org 76952 (MVE + Partners) and its 18 `IntelPerson` rows** for the demo window — `RetiredAtUtc` / `EnrichmentSuppressedAtUtc` already exist. | M | `BEFORE-DEMO` | Kills the one-keystroke catastrophe. **Owner's call — this is a data write; this audit is read-only.** |
| 5 | **Fix the AI error-as-success path** (`Approach.cs:65-77`) — detect the three `AppAiService` error prefixes, show a real failure state, add a cancel affordance. | S | `BEFORE-DEMO` | A dead gateway currently renders its own error under "Drafted 14:32 from live intel", and can hang 4 minutes. |
| 6 | **Replace *"coming with the AI Crucible."*** (`PursuitBriefViewModel.cs:125,129`) or delete the card. | S | `BEFORE-DEMO` | Guaranteed on screen **and in every exported PDF**. |
| 7 | **Stop rendering raw exception text** — `OrgDossierView.xaml:925`, `OrgDossierViewModel.cs:501,543,556`, and the nine status-line sites. Map `SqlException` → "Can't reach the BD database — check VPN". | S | `BEFORE-DEMO` | The most likely thing an off-LAN audience reads. |
| 8 | **Confirm the demo runs on the KOR LAN** from a machine with the Deltek DSN; dry-run Dashboard, an org dossier, and one brief export on it. | S | `BEFORE-DEMO` | Repo rule 3 — never hand over what you have not run on the machine that will run it. |
| 9 | **Hide the "BD Scorecard" nav button** (`BdWorkspaceWindow.xaml:96`). | S | `BEFORE-DEMO` | Win rate + per-staff won fee — and the number shown is wrong (§5g). |
| 10 | **Cut `CompetitionInfoSourcesWindow.xaml:106,112-125`** — the "Coming next" roadmap and the "future panel" clause. | S | `BEFORE-DEMO` | Announcing unbuilt features to the audience is the opposite of the goal. |
| 11 | Annotate the 2 `async void` methods; comment the 3 cleanup catches. **Turns the suite green.** | S | `SOON` | 5 minutes; removes a red gate a reviewer would notice. |
| 12 | Bind `IsEnabled` to `HasSelected` (already exists, unbound) on Start Pursuit / Edit / Move to / Own / Dismiss / Set stage / Add / Log / Open, or add the existing `"Select an opportunity first."` pattern. | M | `SOON` | ~19 controls that look broken when clicked. |
| 13 | **Reconcile the win rate with `Definitions.Bd.cs:46-47`** — honour `WonLostOutcome`, exclude NoBid/Withdrawn. Then reconcile Lifetime Fee (3 ways), the two win ledgers, and the FX rate. | L | `SOON` | Two screens in one app show different numbers for the same thing. A technical lead will find this. |
| 14 | Add a global `DispatcherUnhandledException` handler. | S | `SOON` | Currently **none anywhere in the app**; five `async void` handlers are safe only by coincidence. |
| 15 | Fix `Chase RongÃ©` + `crongé@`; dedupe the 3× McLarand/Gura/Rongé/Berger rows; resolve `WR26-021` ×4 on the first screen. | M | `SOON` | Duplicate entities undercut the entity-resolution story. |
| 16 | Register Mulish in the QuestPDF path (`PursuitBriefPdfExporter.cs:59`, `BriefPdfGenerator.cs:1388`). | S | `SOON` | Client-facing PDFs are currently branded non-deterministically. |
| 17 | Fix the 20 lost em-dashes and the 2 wrong button names (§5h). | M | `SOON` | An hour's work; these are what "sloppy" looks like. |
| 18 | Delete `CompetitionInfoWindow`, `MajorProjectsInventoryWindow`, `PrimePipelineWindow`/`ViewModel`, `RegionBriefDialog` + their DI lines. **Fix `OrphanedPublicTypeTests` to not count a lone DI registration as a consumer.** | M | `SOON` | ~1,409 LOC dead, and the gate that should have caught it is blind. |
| 19 | Add in-grid empty states (Bazaar/Overwatch/Attribution/Events); give `RelationshipsView.DossierHost` default content; bind `CrmViewModel.IsLoading`. | M | `SOON` | Several screens open blank. |
| 20 | Upgrade `AngleSharp` past 0.17.1. | S | `SOON` | Build-visible CVE. |
| 21 | Route document opens through `\\KOR-APP01\OpsArchive` before `C$`; reword the failure. | S | `SOON` | Currently requires local-admin on the server. |
| 22 | Push Bazaar's unclaimed/undismissed filter into SQL; cap the CRM engagement and Deltek project queries. | M | `LATER` | Repo working rule 4. |
| 23 | Populate `ThePlay`/`FitScore`, or remove them from `PursuitBrief`. | L | `LATER` | The real fix behind item 6. |
| 24 | Add ViewModel tests for `Crm`, `Opportunities`, `Workspace` (currently **0**), starting with `CrmAnalyticsService`. | L | `LATER` | ~34,000 LOC with no behavioural coverage; it is exactly where §5(g) lives. |
| 25 | Rationalise folder-vs-navigation naming; add Reports to the BD rail. | L | `LATER` | Reports is currently unreachable from the workspace. |

---

## 10. Verdict

**Demo-able with care — but the care required is unusually specific, and the default screen is the
problem.** The engineering is better than the brief implied: the build is green `[RUN]`, there are
**zero** `TODO`/`FIXME`/`HACK`/`NotImplementedException` markers in ~34,000 LOC `[RUN]`, the repo's own
detectors confirm **no unbound commands and no broken XAML bindings** `[RUN]`, and I hand-verified that
every one of the ~110 `Click` handlers across the three folders resolves to a real method with a real
body. The genuine defects are narrow: two hardcoded-null Pursuit Brief fields that always print *"coming
with the AI Crucible."* into the UI **and the exported PDF**, an AI call that renders its own error text
under a green "Drafted… from live intel" banner, ~19 controls that silently no-op with no selection, and
raw `SqlException` strings surfacing off-LAN.

**The single most important thing to fix is not a bug — it is that clicking "Business Development" lands
on a screen listing 25 named architecture firms beside KOR's written plan to displace their structural
engineers, and MVE is an architecture firm already in the database.** Org 76952 `MVE + Partners`,
`Kind=Architect`, carries **18 named MVE staff with real `@mve-architects.com` addresses** including the
President, **25 affiliation rows**, an enrichment payload naming Matthew McLarand as a decision-maker,
and **12 MVE projects with their incumbent structural engineers named** `[QUERIED]`. MVE ranks **#14** in
a shipped report subtitled *"Warm-intro priority list"*, and "Generate Brief" on their record would emit
a PDF carrying KOR's lifetime billings and 90-day-aged receivables against MVE **plus MVE's own
executives**, straight to the Desktop.

Do items 1–3 of §9 — land the workspace somewhere other than Dashboard, gate four `Visibility` bindings,
and rehearse a fixed path — and this is one of the strongest modules in the suite to show. Skip them and
it is the one most likely to end the meeting.

---

## STALE-DOC register

| Document / claim | Asserts | Reality | Evidence |
|---|---|---|---|
| `docs/BD-Enrichment-Session-2026-06-23.md:7` | *"In-app BD visuals (built, render-verified, **PENDING COMMIT**)"* | **Committed and shipped.** `BdReportsViewModel.cs:110-112` + dispatch `:133-135` + tests `BdReportsViewModelTests.cs:98-101`. | `[READ]` |
| Reported context: *"a typeahead/single-click UX change was **specified**"* | implies outstanding | **Shipped 2026-06-20**, `b8ef48f0`. `PreviewMouseLeftButtonUp` in **all three** controls. `docs/BD-Typeahead-SingleClick-Prompt-2026-06-20.md` is a **completed** prompt. **This has a security consequence** (Risk 2): there is no highlight-without-opening margin any more. | `[RUN]` |
| Reported context: *"a deferred-work register **D1–D13**"* | D1–D13 | **`D13` does not exist anywhere in `docs/`.** The register runs **D1–D12**. A register cited by a range that overshoots its extent should not be called authoritative without re-reading it. | `[RUN]` |
| Reported context: *"BD Reports **A–C** are done"* | 3 reports | **11 analytical + 11 sector reports ship**, none stubs (`BdReportsViewModel.cs:100-113`, `SectorReportDefinitionCatalog`). A/B/C is `BD-UI-Plan` *phase* vocabulary (`CompetitorIntelReportGenerator.cs:9` cites *"BD-UI-Plan Phase B item 8"*), **not** a report count. Materially understates what exists. | `[READ]` |
| Task brief LOC figures | 8,811 / 11,014 / 5,236 | **13,139 / 14,127 / 6,625** — ~34k total, not ~25k. | `[RUN]` |
| Task brief: *"`BusinessDevelopment` … subfolders"* / "tabbed shell" | TabControl | `BdWorkspaceWindow` is a **190px left nav rail of Buttons** driving one `ContentHost`. **There are no `TabItem`s.** | `[READ]` |
| Task brief: *"`CrmAnalyticsService` is a THIRD analytics implementation alongside `Financials` and `PMTools`"* | three implementations | **`PMTools/HistoricalAnalyticsService` computes nothing** (a facade over `Kor.Operations.Business`), and **neither PMTools nor Financials computes any win-rate/won-count/fee metric.** The real duplication is *inside* this module — see §5(g). | `[READ]` |
| Commit `ee633975` *"kill the dead Bazaar pill"* | Bazaar removed | **Pill removed; `BazaarView` is alive and is the default content of the "Opportunities" rail button** (`OpportunitiesHubView.xaml.cs:26`). Three user-visible "Bazaar" strings survive (`BazaarView.xaml:24`, `BazaarViewModel.cs:187,245`). | `[RUN]` |
| `HtmlBriefPdfGenerator.cs:17-18` (code comment) | *"Org and Region briefs render here; the remaining shapes delegate to QuestPDF"* | **All six shapes render on the HTML template** (`:26,35,44,53,208,217`). | `[READ]` |
| `BazaarView.xaml.cs:16`, `OverwatchView.xaml.cs:20`, `AttributionView.xaml.cs:14` | name "Bazaar"/"Overwatch"/"Attribution" nav buttons | Those labels are now **"Opportunities" / "Pursuit Monitor" / "BD Scorecard"**. | `[READ]` |
| `RegionBriefDialog.xaml.cs:13` | *"Saves a .docx"* | `FormatCombo` defaults to **PDF** (`:32`) — and the dialog is unreachable anyway. | `[READ]` |
