# 04 — Consolidated To-Do Register

Every to-do from the eleven module audits, de-duplicated and ranked. **2026-08-20. Demo to MVE in
under two weeks.**

`size` — **S** ≤2h · **M** ≤1d · **L** >1d.
`who` — **Ian** for anything needing server access, a config change, a deploy, a database write, or a
business conversation. **either** otherwise. *Ian runs all deploys and server changes himself; nothing
here assumes they have been done for him.*

`BEFORE-DEMO` items are ordered by **risk × cheapness** — highest value per hour first. Evidence tiers
are carried forward from the module reports and are never promoted.

---

## Do these five first

**1 — Land the BD workspace anywhere but Dashboard, and gate four XAML bindings.** *(items 1–2, S+S,
either)*
Clicking "Business Development" today opens on a screen that renders, **at load with no click**, 25
named architecture firms beside KOR's written plan to displace their structural engineers, and 12 named
rival SE firms with capacity reads `[QUERIED]`. MVE is an architecture firm. Four `Visibility` bindings
cover four of the six critical disclosures. This is the cheapest hour in the entire audit and it is the
only item that could end the meeting.

**2 — Redeploy the MCP service from HEAD, then verify the artifact, not `/health`.** *(items 3–4, S+S,
Ian)*
The deployed binaries are 34 days behind. Right now `get_wip` reports earned and overbilled transposed
(a $209,298 sign flip) and `get_cash_position` sums all 20 bank accounts instead of the 3 whitelisted
`[RUN: byte-scan of the deployed DLLs]`. Ask the virtual CFO "what's our cash position?" in front of MVE
and it states a figure that does not match the screen next to it. One build and one robocopy fixes both
plus the per-year FX. **Must include `Kor.Operations.Business.dll`, not just `Kor.Operations.Mcp.dll`** —
and the version string alone would not have caught this, so re-scan the DLL for `SplitWipNet` and
`CashAccountWhitelist` afterwards.

**3 — Make the AI agree with the screen: three values, two of them config.** *(items 5–7, S×3)*
Deployed `BilledDefaultOrg` is `""` where the app uses `CAD`, so `/ask` includes USA rows the P&L tab
excludes. `Mcp:EmployeeSummaryExcludedIds` is empty where the app excludes two people — and because the
score is a percentile, that shifts **everyone's** grade. And `Mcp/Program.cs:83` builds
`ProjectAnalyticsService` without the peer estimator the WPF window passes, so the AI and the grid
disagree about the same people and the same at-risk projects `[QUERIED + READ]`. The worst part: three
comments **inside the system prompt** tell the model the two match, so it asserts the parity
confidently.

**4 — Put the redirector in git and make it compile.** *(items 8–9, S+S)*
`tracking.korstructural.com` has been logging client transmittal evidence for nine months from a binary
whose source is in no repository and has not compiled since 2026-03-17 `[RUN]`. It is ~30 minutes plus a
small fix (construct `GraphFacade` locally instead of the removed static). It is also the difference
between an honest and a dishonest answer to the objection the battlecard rates most dangerous — *"what
happens when you leave?"* — which currently rests on "it's all in source control."

**5 — Stop the FileSync UI stating three things that are not true.** *(items 10–12, S×3, either)*
Register `KorMapSync` with Quartz — four lines — so the grid stops showing a live countdown to a fire
that has never happened and the public project map stops being 8 days stale. Fix the log tailer to use
`fs.Length` instead of `FileInfo.Length`, so "show me the logs" — the likeliest spontaneous demo request
— stops returning a blank grid on a healthy service. And relabel or honour `GlobalMode`, which currently
reads `Shadow` while all seven jobs run `Live` and move client files `[QUERIED + RUN]`.

---

## BEFORE-DEMO — ordered by risk × cheapness

| # | item | module | size | tag | why it matters | who | status |
|---|---|---|---|---|---|---|---|
| 1 | Land `BdWorkspaceWindow` on a view other than `DashboardView`, **or** suppress the `DisplacementRead` column (`DashboardView.xaml:589`) and the `CapacityRead` column (`:659`) | 08 | S | BEFORE-DEMO | The default BD screen. 25 named architecture firms + KOR's displacement strategy, 12 named rival SE firms, rendered at load with no click `[QUERIED]` | either | verified 2026-08-21 |
| 2 | Gate the `DISPLACEMENT BRIEF` panel (`OrgDossierView.xaml:610`) behind a demo flag; hide the `Competes` and `Agent Profile (AI)` columns (`CompetitionInfoView.xaml:125,144`) | 08 | S | BEFORE-DEMO | Four `Visibility` bindings cover four of the six critical disclosures. 97 displacement briefs live, 3 of them San Diego. **2026-08-21: widened — the two Awards columns do not stop the double-click drill-down to `CompetitorProfileWindow`, which chips NORR and ROBSON as DIRECT COMPETITOR and names their executives. That panel is gated too.** | either | verified 2026-08-21 |
| 3 | **Redeploy `Kor.Operations.Mcp` from HEAD**, including `Kor.Operations.Business.dll` | 05 | S | BEFORE-DEMO | Single action that fixes `get_wip`, `get_cash_position` and per-year FX. Converts the suite's worst live defect into a non-issue `[RUN]` | **Ian** | open |
| 4 | Verify the redeploy by re-scanning the deployed DLLs for `SplitWipNet` (UTF-8) and `CashAccountWhitelist` (UTF-16) — not by `/health` alone | 05 | S | BEFORE-DEMO | The stamped version already disagrees with the binary's contents once; a version string would not have caught it `[RUN]` | **Ian** | open |
| 5 | Set `"BilledDefaultOrg": "CAD"` in the deployed MCP `appsettings.Production.json` | 04, 05 | S | BEFORE-DEMO | One value. Stops `/ask` returning a P&L that includes USA rows the tab beside it excludes — sized at +$77,620 for one month by App.config's own comment `[QUERIED]` | **Ian** | open |
| 6 | Set `Mcp:EmployeeSummaryExcludedIds` on KOR-APP01 to match `App.config:149` | 06 | S | BEFORE-DEMO | The AI ranks two people the screen deliberately hides, and shifts every other employee's percentile grade while doing it `[QUERIED]` | **Ian** | open |
| 7 | Pass `EstimatePeerBudget` at `Mcp/Program.cs:83` **or** drop it from `HistoricalAnalyticsService.cs:28` — pick one budget basis | 06 | S | BEFORE-DEMO | AI and screen currently disagree on grades and at-risk projects while the system prompt tells the model they match `[READ]` | either | open |
| 8 | `git init` `Redirector/`, add `.gitignore`, commit the 2026-03-05 state as the baseline, push | 02 | S | BEFORE-DEMO | The one untracked thing external parties actually touch. ~30 minutes. Blocks the honest answer to the key-person objection `[RUN]` | **Ian** | open |
| 9 | Fix the redirector's 5 compile errors — construct `GraphFacade` locally instead of the removed `Instance`/`Initialize` static | 02 | S | BEFORE-DEMO | Until this is done, **no** other fix can be shipped to the live service. Broken since `981907f5`, 2026-03-17 `[RUN]` | either | open |
| 10 | Register `KorMapSync` with Quartz in `QuartzInstaller.cs` (mirror the existing 5-job pattern) | 03 | S | BEFORE-DEMO | Four lines. The UI shows a countdown to a fire that has never happened; the public map is 8 days stale `[QUERIED]` | either | open |
| 11 | Fix `FileSyncLogTailer` to use `fs.Length` instead of `FileInfo.Length` | 03 | S | BEFORE-DEMO | "Show me the logs" returns a blank grid on a healthy service. Measured: `FileInfo.Length` 0 vs `Stream.Length` 43,165, same file, same instant `[RUN]` | either | open |
| 12 | Honour `KOR_FILESYNC_MODE`, or relabel the heartbeat `GlobalMode` column | 03 | S | BEFORE-DEMO | The panel states the opposite of the truth: `Shadow` while all 7 jobs are `Live` and moving client files `[QUERIED]` | either | open |
| 13 | Fix the `4501-01-01` filename guard at `EmailFilerRibbon.cs:794` — test `SentOn.Year > 4000` as well as `DateTime.MinValue` | 01 | S | BEFORE-DEMO | **872 of 2,220 emails filed in the last 30 days (39%) carry the prefix**, most recent 23:37 tonight. First thing visible if anyone opens File Explorer `[QUERIED]`. Note the rebuild/republish needs Ian's signing cert | either (rebuild: **Ian**) | open |
| 14 | Fix the AI error-as-success path (`PursuitBriefWindow.Approach.cs:65-77`): detect the three `AppAiService` error prefixes, render a real failure state, add a cancel affordance | 08 | S | BEFORE-DEMO | A down gateway currently paints `"Unable to reach AI service: No such host is known."` under a green *"Drafted 14:32 from live intel"*, and can hang for **4 minutes** with no spinner `[READ]` | either | open |
| 15 | Disable or caveat MCP `get_wip` until a real WIP source exists | 04 | S | BEFORE-DEMO | With Revenue Generation off, both WIP branches draw from 238 residual rows (0.5% of the table). The WPF tile is already correctly hidden — the MCP tool is the exposed edge `[QUERIED]` | **Ian** | open |
| 16 | Fix the fail-open `catch` at `HomeWindow.xaml.cs:295-308` — collapse `FinancialsTileHost` and `CompensationTileHost` on exception instead of showing them | 04, 06 | S | BEFORE-DEMO | An AD lookup failure force-shows **seven** surfaces including salary data. The trigger condition is launching off-LAN at MVE before the VPN is up `[READ]` | either | open |
| 17 | Stop rendering raw exception text: `OrgDossierView.xaml:925`, `OrgDossierViewModel.cs:501,543,556`, and the nine status-line sites. Map `SqlException` → "Can't reach the BD database — check VPN". **2026-08-21: "nine sites" is an undercount — 72 across the App `[RUN]`. Brief 2 scopes this to the org dossier (the demo path) and adds a shared mapper the rest can adopt; the sweep is item 183.** | 08 | S | BEFORE-DEMO | Off-LAN the audience reads `SqlException: Login failed for user 'opportunities_app'` — or an interface name and a method name `[READ]` | either | open |
| 18 | Replace *"coming with the AI Crucible."* at `PursuitBriefViewModel.cs:125,129`, or delete the card | 08 | S | BEFORE-DEMO | Renders in **every** Pursuit Brief, 100% of the time, **and exports into the PDF** `[READ]` | either | verified 2026-08-21 |
| 19 | Wrap the fallback `Process.Start` at `EmailSearchWindow.xaml.cs:412` in its own try/catch + message box | 01 | S | BEFORE-DEMO | Turns a whole-window crash into a dialog when "Open" hits an unreachable file. Directly on the demo path `[READ]` | either | open |
| 20 | Fix or remove the two `$185` tooltips (`HistoricalAnalyticsWindow.xaml:395,1087`) | 06 | S | BEFORE-DEMO | The tooltip says *"the $185/hr portfolio median"* while the KPI strip above it shows ~**$380** `[RUN]`. Falsifiable in one hover | either | open |
| 21 | Set the Opportunities Hub default view: `Status=New AND deadline in future`; exclude `BDALERTS-*`; collapse `BCBID-*`/`BCBIDENG-*` same-tender duplicates | 07, 08 | S | BEFORE-DEMO | The top of the board is the same White Rock tender 3–4 times at different scores, one buyer `Unknown`, beneath a row titled *"APC – Notification of New Postings"* `[QUERIED]`. One view change kills four ranked demo risks | either | open |
| 22 | Hide the "BD Scorecard" nav button (`BdWorkspaceWindow.xaml:96`) | 08 | S | BEFORE-DEMO | Win rate + per-staff won fee in front of a prospective client — and per §5(g) the number shown is **wrong and low** by the app's own methodology | either | verified 2026-08-21 |
| 23 | Cut `CompetitionInfoSourcesWindow.xaml:106,112-125` — the "Coming next" roadmap and the "future panel" clause | 08 | S | BEFORE-DEMO | A literal roadmap panel announcing three unbuilt features, one click from Market History `[RUN]` | either | verified 2026-08-21 |
| 24 | Re-render `docs/KOR-DxfToEtabs-onepager-web.pdf` and verify with `pdftotext`, not by opening it | 10 | S | BEFORE-DEMO | The committed file is an Edge **"File not found"** page, and `Publish-EtabsModel.ps1:231` copies it into client job folders `[RUN]` | either | open |
| 25 | Point the one-pager count gate at the shipped PDF, as the dossier gate already does (`Publish-EtabsModel.ps1:435`) | 10 | S | BEFORE-DEMO | The gate that exists to catch item 24 parses the source HTML instead. Otherwise the same class recurs unseen | either | open |
| 26 | Pass `RequiredRuleKeys` instead of `builtIn.Keys` at `DxfToEtabsService.cs:350` | 10 | S | BEFORE-DEMO | One word. Closes the only hole in the "a missing rule stops the run, there is no fallback" guarantee — the 3 layer-pattern keys that decide what counts as a wall `[READ]` | either | open |
| 27 | Fix the 3 failing `ModelQuestionnaireTests` left red by `72c1a2ca` | 10 | S | BEFORE-DEMO | "All tests green" is currently not true for the module with the best test apparatus. All three are stale tests, not product defects `[RUN]` | either | open |
| 28 | Delete `SetEnvironmentVariables.ps1` from `\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New\` | 01 | S | BEFORE-DEMO | Plaintext **two Entra client secrets, the Deltek ODBC password and an Anthropic API key** on the staff-readable install share. Deleting a file is free; rotation is a separate, slower item `[QUERIED]` | **Ian** | open |
| 29 | Strip the inherited `BUILTIN\Users` read ACE from the deployed MCP `appsettings.Production.json` (`icacls /inheritance:d` then `/remove:g`) | 04, 05 | S | BEFORE-DEMO | Four live secrets readable by any session on KOR-APP01. Verify the service account keeps Full control and restart afterwards. *(Modules 04 and 05 disagree on urgency — 04 says BEFORE-DEMO, 05 says SOON. The ACL half is cheap and safe; the rotation half is not — see "deliberately not doing".)* | **Ian** | open |
| 30 | Rotate the SAM.gov API key on KOR-APP01 | 07 | S | BEFORE-DEMO | HTTP 401 for 19 straight days. It is the **only US federal source** and MVE is a SoCal firm `[QUERIED]` | **Ian** | open |
| 31 | Seed favourites for the demo account in `KorTransmittals.dbo.UserFavorites` | 01 | S | BEFORE-DEMO | With no rows the favourites pane and the Quick File dropdown are both blank and the feature reads as half-built. Two minutes | **Ian** (data write) | open |
| 32 | Suppress org **76952** (`MVE + Partners`) and its 18 `IntelPerson` rows for the demo window — `RetiredAtUtc` / `EnrichmentSuppressedAtUtc` already exist as mechanisms | 08 | M | BEFORE-DEMO | Removes the one-keystroke catastrophe: 18 named MVE staff with real `@mve-architects.com` addresses incl. the President, 25 affiliation rows, 12 MVE projects with incumbent SEs named. Four one-keystroke paths reach it `[QUERIED]` | **Ian** (data write — this audit is read-only) | open |
| 33 | Put the as-of period on every stale-sourced Financials tile and tab ("Deltek posted through **Feb 2026**") | 04 | M | BEFORE-DEMO | Turns the most likely awkward question into a demonstration of rigour. Cheaper and more honest than hiding it `[QUERIED]` | either | open |
| 34 | Unblock the MCP smoke harness — add 3 BD calibrators or add the 3 names to `DefaultExemptToolNames` — then run it once against the redeployed service and record the pass rate | 05 | M | BEFORE-DEMO | The only evidence `/ask`'s numbers are right. Dead since 2026-06-09, which is exactly the window the WIP and cash defects shipped in `[RUN]` | **Ian** | open |
| 35 | Add smoke calibrators for `get_wip` and `get_cash_position` specifically | 05 | S | BEFORE-DEMO | The two tools that just shipped wrong. A ratchet, not a threshold | either | open |
| 36 | Decide the canonical FX regime and point every surface at it | 04 | M | BEFORE-DEMO | Partner Financials converts USA work at 1.378457 while the Billed P&L beside it uses 1.36 — a $35k gap on the same code `[RUN]`. The same split recurs between CRM and Financials | either | open |
| 37 | Correct the battlecard and the RG record: **Revenue Generation is OFF**, and `SUM(Revenue)` = **$69.06M**, not $0 | 04 | S | BEFORE-DEMO | The pulled claim was false in **both** directions. The replacement sentence is checkable in one query `[QUERIED]` | either | open |
| 38 | Ask Daler / DMCL why GL and summary posting stopped after 202602, and whether it can be run | 04 | S | BEFORE-DEMO | If the periods can be posted before the demo, the single worst factual risk evaporates. This is a business action, not code | **Ian** | open |
| 39 | Rehearse and **print** the fixed BD click-path and the §8 avoid-list; brief whoever drives on which buttons write, email or spend money | 08 | S | BEFORE-DEMO | Every critical risk in module 08 is a navigation choice. "Reassign…" sends a real Graph email; "Run Now" queues a live scraper; "Refresh intel" spends AI budget `[READ]` | **Ian** | open |
| 40 | Agree the PM Tools click-path: **do not open Employee Summary** | 06 | S | BEFORE-DEMO | 56 named KOR staff with 0–100 scores and letter grades, sortable — **five currently carrying an F** (33, 36, 41, 46, 46). BC employee personal information, and forced ranking by construction `[QUERIED]` | **Ian** | open |
| 41 | Decide and rehearse the transmittal demo path: dashboard-read (safe) vs live-send with the **External link** box ticked | 02 | S | BEFORE-DEMO | Unticked is the default, and an MVE recipient clicking the link hits a Microsoft sign-in wall on stage `[READ]` | **Ian** | open |
| 42 | Decide and rehearse the DXF→ETABS off-LAN story: VPN, or pre-generated outputs + the renderer PNG | 10 | S | BEFORE-DEMO | `RequireRuleSettings` is hardcoded `true` — no VPN, no run, and everything else in that demo hangs off it `[READ]` | **Ian** | open |
| 43 | Re-publish 31168 and 31138 from HEAD | 10 | S | BEFORE-DEMO | Both job folders are 5 commits stale: 31138's workbook is missing its *Rules in force* sheet and 31168 has no summary PDF at all `[QUERIED]` | **Ian** (writes to the share) | open |
| 44 | Confirm the presenting machine is on the KOR LAN and has the Deltek ODBC DSN + `KOR_ODBC_*` env vars; dry-run the BD dashboard, an org dossier, one brief export and the Financials window on it | 08, 04, 06 | S | BEFORE-DEMO | Repo rule 3 — never hand over what you have not run on the machine that will run it. Without the DSN the org dossier prints a raw `OdbcException` | **Ian** | open |
| 45 | Rehearse the email round trip end to end on the actual demo machine, off the dev box | 01 | S | BEFORE-DEMO | Exercises all four prerequisites at once; `HostExeResolver` failure is silent-ish and machine-specific `[QUERIED]` | **Ian** | open |
| 46 | Rehearse the MCP demo script against the redeployed service; time every question; drop anything over ~20 s | 05 | S | BEFORE-DEMO | Mean `/ask` latency 10.5 s, max **72.9 s** across 255 human questions `[QUERIED]`. A minute of spinner reads as "hung" | **Ian** | open |
| 47 | Rehearse the exact 31168 `dxf-to-etabs` command plus the renderer, timed | 10 | S | BEFORE-DEMO | 50.7 s + 9.2 s is a good number — know it cold `[RUN]` | **Ian** | open |
| 48 | Rehearse all four engineering tools end to end from the app with the named input files, including the 15–18 MB rebar compare, timed | 09 | S | BEFORE-DEMO | The consolidated window has **zero** App-side tests; a rehearsal is the only thing that covers the wiring, and it gets the real wall-clock number for the dead-air risk `[RUN]` | **Ian** | open |
| 49 | Write the one-page engineering-tools demo script: exact files, exact order, and the two sentences that frame the orange flags and the column fallback **before** the workbook opens | 09 | S | BEFORE-DEMO | Every top risk in that module is a framing failure, not a code failure. 19 of 54 plates are flagged orange by design and it reads as broken if unframed | **Ian** | open |
| 50 | Confirm SAFE is installed and COM-registered on the demo laptop; pre-run the registration UAC prompt once | 09 | S | BEFORE-DEMO | Zero CSI products are installed on the dev machine `[QUERIED]`. Without this the PDF→SAFE demo ends at a file on disk | **Ian** | open |
| 51 | Prepare a second marked-up PDF for PdfToSafe, **or** agree not to accept a live file from MVE | 09 | S | BEFORE-DEMO | There is exactly one known-good input — a 41 KB single typical floor dated 2026-04-14. A live MVE sheet has never been tried `[QUERIED]` | **Ian** | open |
| 52 | Confirm the demo machine has the Revit details palette **dormant** (no `detailsPalette` block in `%PROGRAMDATA%\KOR\kor-tools.json`) | 11 | S | BEFORE-DEMO | `detail.vw_PaletteCatalog` holds 1,079 rows and **0 placeable**, so with the documented default the palette opens empty `[QUERIED]` | **Ian** | open |
| 53 | Do not run `RemoveUnusedViews` in any live demo | 11 | S | BEFORE-DEMO | `ViewSheetExtraCommands.cs:37` fails **open** on schedules: a sheeted schedule can drop out of the protection set and become a delete candidate `[READ]` | **Ian** | open |
| 54 | Keep the `KOR.Drafter` repo off screen entirely | 11 | S | BEFORE-DEMO | Its own README marks it confidential from KOR's own drafting team. It has no UI and runs on one workstation | **Ian** | open |
| 55 | Close/hide `set-filesync-env*.ps1` before any screen-share of the repo root | 03 | S | BEFORE-DEMO | Live Entra client secret + SQL password visible in any folder listing or editor sidebar. Correctly gitignored, but on disk `[QUERIED]` | **Ian** | open |
| 56 | Clear or triage the 3 open `Watcher` failures so no red row is on screen — rename the leading-space PDF on 31056-01; do not change code under time pressure | 03 | S | BEFORE-DEMO | Removes the "what failed?" question the Command Center currently cannot answer `[RUN]` | **Ian** (share write) | open |
| 57 | Give `takeoff.exe` a help/usage list of its real commands | 09 | S | BEFORE-DEMO | Bare invocation prints the usage line for the oldest CSV-diff command, out of ~35 subcommands in a 2,971-line file `[RUN]` | either | open |
| 58 | Replace the two `WriteAsync("…error:" + ex)` at redirector `Program.cs:185,330` with a generic message + server-side log; set an explicit body-size limit with a friendly message | 02 | S | BEFORE-DEMO | Raw .NET stack traces on the partner-facing file-drop page. The ~28 MB framework default fires on a realistic drawing set `[READ]` | either | open |
| 59 | Rotate the redirector's hardcoded Graph client secret (`Program.cs:33`); move tenant/client/secret/drive to config; delete the hardcoded defaults | 02 | S | BEFORE-DEMO | An app-only SharePoint credential compiled into a deployed DLL, in a file with no version control. No `Graph:*` key exists anywhere, so **the fallback is what production uses** `[QUERIED]`. **2026-08-21: the same secret is committed in 24 tracked `.ps1` files in this repo `[RUN]` — it is in git history, and rotating it breaks those scripts if any still runs on a schedule. Check the box before rotating.** | **Ian** | open |
| 60 | Add a comment to `appsettings.Production.json` recording that `Mcp:AnthropicModel` must **not** change before the demo | 05 | S | BEFORE-DEMO | `AskService.cs:321` sends `temperature = 0`, which any 5-series model rejects with HTTP 400. A well-meant "let's use the newest model" hard-breaks every question `[READ]` | **Ian** | open |
| 61 | Decide, write down and rehearse the eight standing answers *(see the list below)* | all | S | BEFORE-DEMO | Each is near-certain to be asked, each currently has an unrehearsed answer, and each has a strong honest one available | **Ian** | open |
| 62 | Decide: publish the WIP tile, or have the one-line answer ready for why it is hidden | 04 | S | BEFORE-DEMO | *"Deltek isn't running revenue recognition, so we don't publish a WIP number we can't stand behind"* is a strong answer that shows rigour. Being caught without it is not. Finance sign-off, not engineering | **Ian** | open |

### The eight standing answers (item 61)

1. **"Does it suggest the project when filing?"** — No. There is no inference code of any kind. Claim
   the corpus instead: 372,370 emails, 955 projects, back to 2014 `[QUERIED]`.
2. **"Is the search semantic?"** — No, keyword: SQL Server full-text with prefix matching. The counter
   is that it searches message **bodies**, and `seismic review` returns 7,216 hits across the firm's
   whole history in under a second `[QUERIED]`.
3. **"Do the transmittal links expire? Are you tracking whether I opened your email?"** — No expiry, and
   yes, per recipient with IP and user agent, retained indefinitely, with no notice. Both need prepared
   answers; BC PIPA treats email+IP+timestamp as personal information.
4. **"How is the AI secured?"** — One shared password, in git, over plaintext HTTP, with client-asserted
   identity. Own it with a two-sentence roadmap (Windows Auth + TLS + per-user identity) rather than
   improvising `[QUERIED]`.
5. **"Is the AI research live?"** — No. It has produced nothing since 2026-06-27, and it is visible in
   the app's own Job Run History window `[QUERIED]`. Better pre-empted than discovered.
6. **The AI story for the engineering tools** — pick one. "AI never touches the measurement" (takeoff)
   and "ask the AI to set the slab thickness and export" (PdfToSafe) are both true of the same module.
   The precise honest sentence is in module 09 §5.1.
7. **Revit → DXF → ETABS** — *"Revit export and the storey mapping are done; layer mapping is the open
   piece."* The exported layers are `A-WALL`/`S-COLS`/`A-FLOR` against rules expecting
   `WALL`/`_COL`/`SLABEDG`, so a run today yields walls but no columns and no floors `[RUN]`. Since
   2026-08-15 those patterns are database rules, so closing it is a settings change, not a code change.
8. **"How many Revit tools?"** — 137. The playbook says 28 and `BUILD-STATUS.md` says 79; both are
   contradicted by the ribbon on screen `[RUN]`. Fix the playbook's opening line too.

---

## Deliberately NOT doing before the demo

Getting this right matters as much as the fix list. Each of these looks urgent and should wait.

| item | why it waits |
|---|---|
| **Rotating the `transmittals_app` SQL password** | It is the scaffold placeholder, it is in git, and it must be changed — but the VSTO add-in reads `ConfigurationManager` directly with **no override path** (`ItemsToFileProcessor.cs:66-71`), so rotating it silently breaks filing for all ~40 staff while the desktop app keeps working. It needs a coordinated rebuild and republish, and only Ian can sign the add-in. **After the demo.** |
| **Rotating the Entra / Graph client secrets in FileSync and the deploy share** | Module 03 says it explicitly: *"rotating a Graph secret on a two-week runway is its own risk."* Delete the share script now (item 28) and strip the ACL (item 29); rotate afterwards. *(Item 59 is the exception — the redirector's secret is compiled into a DLL whose source is untracked, and rotating it is a contained change to one service.)* |
| **Purging credentials from git history** | Necessary and unavoidable, and it reduces demo risk by exactly zero. A history rewrite across four repositories two weeks before a demo is the wrong order of operations. `SOON`. |
| **Building a real WIP from uninvoiced `tkDetail`** | `L`, and the correct two-week answer is item 15 — mute the tool and have the sentence ready. The tile is already correctly hidden. |
| **Fixing `BdResearchQueueBuilderJob` and reviving the AI research layer** | `M`, and it changes nothing on screen in two weeks. Deciding the **answer** (item 61.5) is BEFORE-DEMO; fixing the job is not. |
| **Populating `DeltekClientId` and linking `KorPursuits` to `Opportunities`** | The real prize — it closes the outcome→scoring loop — and it is `M`+`L`. It cannot be rushed and it is invisible in a demo. |
| **Cross-source dedup at ingest** | Fix the **view** (item 21), not the pipeline. Calling `FindPossibleDuplicatesAsync` on the ingest path is `M` and risks changing what gets ingested days before a demo. |
| **Wiring `SlabTakeoffEngine` into the WPF app** | `L`. The Core work is done and host-agnostic, but building a new app surface under time pressure is how demos break. Say "CLI today" and mean it. |
| **Building the Revit export-layer table** | `M`. The reframe (item 61.7) is free and honest, and it is a *better* answer than a rushed half-fix — "the rules side is already settable, so this is a settings change" is a credible next-sprint story. |
| **Changing `Mcp:AnthropicModel`** | Actively do not. Any 5-series model returns HTTP 400 on `temperature = 0` and every question breaks `[READ]`. Item 60 exists to stop a well-meant upgrade. |
| **Re-running the 31065 takeoff with vision** | ~$2 and needs spend sign-off. It matters (the shipped brief's category numbers do not reproduce in free mode) but it is a `SOON` reconciliation, not a demo blocker. Meanwhile: do not quote the brief's per-category figures. |
| **Merging `feature/details-palette`** | Do not merge until the live `standards_reader` password is scrubbed from `PALETTE-README.md:20`. It is confined to that branch today; merging makes it permanent history `[READ]`. |
| **Upgrading `AngleSharp` past 0.17.1** | A build-visible CVE warning is a fair question and a `SOON` answer. A transitive dependency bump across three projects two weeks out is not worth the regression surface. |
| **Moving the non-transmittal tables out of `KorTransmittals`** | `L`, and it only matters if the database schema is shown — which it should not be. |
| **Writing the missing test projects** (FileSync, Drafter bridge, RevitTools ribbon, BD ViewModels) | Each is `L`. The coverage gap is real and it is theme T4, but no test written this fortnight changes what MVE sees. |
| **Rewriting the stale architecture docs** (`Kor.Operations.Mcp.md`, `Takeoff-RESUME.md`, `PROTOCOL.md`) | `M` each and invisible on stage — with two exceptions already promoted: the tool-count numbers in `DEMO-PLAYBOOK.md` (item 61.8) and the `$185` tooltip (item 20), both of which can be contradicted by the screen. |
| **Running the full test suite** | 10–14 minutes because ~20 tests rebuild reference buildings over SMB. Targeted `--filter` runs only, per the repo's own rules. |
| **Dropping `opportunities_app` from `db_owner`** | The single best security-per-minute item in the audit and free of code changes — but it is a live permission change on the database three services read, and module 07 tagged it `SOON`. Do it the week **after**, not the week of. |

---

## SOON — matters within a quarter

| # | item | module | size | why it matters | who | status |
|---|---|---|---|---|---|---|
| 63 | Resolve `PR_SMTP_ADDRESS` in the VSTO filing path instead of `mail.SenderEmailAddress` (`ItemsToFileProcessor.cs:657`); backfill the 891 existing DN rows | 01 | M | 6.5% of recently-filed emails render an Exchange X.500 blob in the visible **From** column `[QUERIED]` | either | open |
| 64 | Populate `MessageId` in the VSTO path; drop the stale "older PIAs" comment at `ItemsToFileProcessor.cs:672` | 01 | S | All 8,378 VSTO rows have no `MessageId`, so threading or cross-writer dedupe can never work | either | open |
| 65 | Rotate `transmittals_app`; purge it from git history, `.config`, `_archive_EmailIndexer` and the share; give the add-in a real secret path | 01, 02, 05 | M | A live production password in a tracked repo and on every one of ~40 workstations `[QUERIED]` | **Ian** | open |
| 66 | Rotate the four secrets in `SetEnvironmentVariables.ps1` (deleted in item 28) | 01 | S | Two Entra secrets, the Deltek password and an Anthropic key were on a staff-readable share | **Ian** | open |
| 67 | Fix or delete `EmailMetadataExtractorTests`; add real tests for `EmailCommon.EmailParser` and the filename builder | 01 | M | The only tests in the module test a class with **zero production consumers**, and one of them is red on its own scaffolding `[RUN]` | either | open |
| 68 | Export the signing certificate to a `.pfx` in the password manager, or switch to `CN=KOR Structural Code Signing` (valid to 2031) | 01 | S | Only one person on one machine can produce a loadable add-in build; the current cert expires 2027-04-14 `[QUERIED]` | **Ian** | open |
| 69 | Delete `BasicEmailMetadataExtractor` (dead), or make it the single parser both paths use | 01 | M | Removes the third parser and the illusion of coverage | either | open |
| 70 | Add expiry to `RedirectTargets` (`ExpiresAt` + a `WHERE` in `/t/`) and set `ExpirationDateTime` on the anonymous Graph link | 02 | M | Turns "we have no expiry" from a gap into a policy, and closes the forwarded-link hole where a stranger's click is logged under the original recipient's email | either | open |
| 71 | Rate-limit `/t/`, `/o/` and `/filedrop`; validate that `{email}` in `/o/` matches the `RedirectTargets` row before inserting | 02 | M | Anyone on the internet can insert arbitrary rows into the evidence log that **is** this module's competitive claim `[READ]` | either | open |
| 72 | Either populate `TransmittalRecipients.ClickedAt`/`ViewedFileAt`/`LastActivityAt` from the event tables, or drop the three columns | 02 | M | 0 of 2,133 populated. A trap for the obvious next feature and it reads as unfinished `[QUERIED]` | either | open |
| 73 | Replace the redirector's `<HintPath>` references with `ProjectReference`s, or pull it into the `Operations` solution | 02 | M | The root cause of the five-month invisible build break | either | open |
| 74 | Log rather than swallow `InboundUploadService.cs:127,158` and `Program.cs:302`; enable `stdoutLogEnabled` or add Serilog | 02 | S | Cannot currently tell "file drop is unused" from "file drop's logging is broken" — the last `Upload` row is 2026-03-19 `[QUERIED]` | **Ian** (server config) | open |
| 75 | Correct `AGENTS.md` — all three of its known-broken claims are false | 01, 02 | S | It is the first document every session reads and it cost this audit real time in two modules `[RUN]` | either | open |
| 76 | Trim/sanitise filenames (leading and trailing whitespace) before Graph upload in `BucketSyncOp` | 03 | S | A recurring permanent-failure class — same signature on four projects across three months `[RUN]` | either | open |
| 77 | Call `f.Refresh()` before the size test at `BucketSyncOp.cs:118` | 03 | S | Large files silently never reach SharePoint when a multi-phase save is still in flight | either | open |
| 78 | Put the real exception, not the run summary, into `JobRuns.ErrorMessage` (`JobDispatcher.cs:115`) | 03 | S | Today a failure is undiagnosable from the Command Center | either | open |
| 79 | Make `publish.ps1` refuse, or loudly warn, on a dirty working tree | 03 | S | The deployed FileSync binary names a commit that does not contain the code it is running `[RUN]` | either | open |
| 80 | Write `docs/runbooks/Kor.Operations.FileSync.deploy.md` and the missing `deploy.ps1` | 03 | M | The deploy exists only in the owner's head; `publish.ps1` points at a script that is not there | **Ian** | open |
| 81 | Move `WeeklyPmDeadlines` `ExcelPath` to a knob or a UNC path | 03 | S | A hidden dependency on OneDrive being signed in under one profile on one server; no knob overrides it `[QUERIED]` | either | open |
| 82 | Create `Kor.Operations.FileSync.Service.Tests` with the three tests module 03 §7 names | 03 | M | Each one catches a defect found in this audit; the service has **zero** coverage today | either | open |
| 83 | Raise `UnbilledColumnHasAny()` above a bare non-zero test (ratio or row-count threshold) | 04 | S | 238 stray rows out of 47,366 currently flip the whole WIP service onto the Revenue-Generation branch on a tenant that has none `[QUERIED]` | either | open |
| 84 | Return `DataLoaded: false` when a loader falls back (`WipFinancialsService.cs:117-118`, `BacklogService.cs:131`) and badge it in the UI | 04 | M | Stops a transient Deltek blip rendering as a confident **$0** | either | open |
| 85 | Align the GL tile's `ScoreTable` with the tab's `PickBestDefaultTable`, or rewrite the "same source" caption | 04 | S | Removes a visibly false claim on a KPI tile | either | open |
| 86 | Add a data-freshness assertion to the test suite, as a ratchet | 04 | S | The gap that hid the six-month staleness. Checks belong in the build | either | open |
| 87 | Migrate the MCP secrets to Machine env vars on KOR-APP01 (the pattern the WPF app already uses) | 04, 05 | M | The durable fix once the rotate + ACL tourniquet is applied | **Ian** | open |
| 88 | Correct `WipTool`'s description, the July audit's F1a, and the three-part-not-four-part naming note | 04 | S | Two documents currently assert Revenue Generation is ON. Wrong facts propagate into every future session | either | open |
| 89 | Rename the Deltek integration-test env vars to `KOR_ODBC_*` so they can actually run; add MCP↔WPF parity tests | 04 | L | The SQL half of Financials is untested, and the parity gaps in T3 are exactly what a parity test catches | either | open |
| 90 | Rotate the MCP Basic-auth password; move it out of `App.config`; gitignore `App.config` with a committed `.template` | 05 | M | Rotation alone is insufficient while the file stays tracked `[RUN]` | **Ian** | open |
| 91 | Rotate the Deltek ODBC and `opportunities_app` credentials also exposed in `App.config` | 05 | M | Same blast radius, wider than MCP | **Ian** | open |
| 92 | Give `query_kor_data` its own read-only SQL login, separate from the audit-writer | 05 | M | Makes read-only a database guarantee rather than an application one `[QUERIED]` | **Ian** | open |
| 93 | Persist `InputTokens`/`OutputTokens` to `Mcp.AuditLog` | 05 | S | Turns the ≈$0.05–0.25/question cost estimate into a measurement; the values already exist and are discarded | either | open |
| 94 | Reconcile the system prompt's *"Revenue Generation is OFF at KOR"* line with the deployed tool's behaviour | 05 | S | After the redeploy the tool is right; the prompt should not contradict it | either | open |
| 95 | Write tests for `AskService`: token budget, circuit breakers, trace eviction, unknown-tool dispatch | 05 | L | 1,143 lines, zero coverage, and it is the centrepiece `[RUN]` | either | open |
| 96 | Re-run the April-2026 rate calibration against August data and reset `Vp.TargetBillingRate` | 06 | M | Drives every budget, health score and at-risk flag. Live median is ~2× the constant `[RUN]` | either | open |
| 97 | Snapshot employee scores from `_allRows`, not `visible`; move the write behind an explicit button | 06 | M | The stored quarterly score currently depends on whatever filter the user last touched — and opening the tab writes to production SQL `[READ]` | either | open |
| 98 | Add a parity test: both hosts construct `ProjectAnalyticsService` identically | 06 | S | Locks item 7 so it cannot silently regress | either | open |
| 99 | Add a parity test: the MCP exclusion list equals the `App.config` list | 06 | S | Locks item 6 | either | open |
| 100 | Demo-mode toggle to anonymise `EmployeeName` across PM Tools | 06 | M | Makes the strongest analytics screen showable at all — one bound property | either | open |
| 101 | Correct `HistoricalAnalyticsHelpWindow.xaml:81` and the three parity claims at `AskService.cs:1019`, `EmployeePerformanceTool.cs:15,44` | 06 | S | The prompt text makes the **model** assert a parity that does not hold | either | open |
| 102 | Fix `BdResearchQueueBuilderJob` (stuck `NextFireAtUtc = 2026-07-19`, zero rows in `JobRuns`) | 07 | M | Root cause of the dead AI research layer | either | open |
| 103 | Deploy current `develop` to APP01 — production is at 2026-07-18 | 07 | S | Post-July-18 Data fixes are not live `[QUERIED]` | **Ian** | open |
| 104 | Call `FindPossibleDuplicatesAsync` on the ingest path, not just the manual-entry UI | 07 | M | Fixes duplication at source rather than hiding it. ~12% of active opportunities are redundant rows `[QUERIED]` | either | open |
| 105 | Populate `DeltekClientId` — promote `BdDeltekLinkDryRunJob` past dry-run for the 8 auto-link matches | 07 | M | Unlocks the dormant scoring block at `RuleBasedOpportunityScoringService.cs:101` — KOR's own client history, currently worth zero points | **Ian** | open |
| 106 | Link `KorPursuits` (177 Won / 85 Lost) to `Opportunities` — currently 0 of 1,075 linked | 07 | L | Closes the outcome→scoring feedback loop. The real prize | either | open |
| 107 | Backfill historical `~WDEF~` (won) pursuits from Deltek | 07 | M | The won-transition sweep only catches **future** conversions, so live win history is still 0 and the 177 wins rest on a frozen May import `[QUERIED]` | either | open |
| 108 | Reclassify `COVAWARD-*` rows as historical awards, not New opportunities | 07 | S | 80 already-awarded 2018–2019 contracts sitting in the active pile, several with encoding damage | either | open |
| 109 | Replace the 27 empty catches in `Ingestion/Scraping` with logged degradation | 07 | M | Still-open from the 2026-07-01 audit. This is how a portal markup change becomes silent under-collection `[RUN]` | either | open |
| 110 | Drop `opportunities_app` from `db_owner` on `KorOpportunitiesDb`; revoke its `KorStandards` reader/writer roles entirely | 07 | S | No code change. The service needs neither — its only DDL is a temp table, and nothing references `KorStandards`. **Biggest blast-radius reduction per minute spent** `[QUERIED]` | **Ian** | open |
| 111 | Then move to `Trusted_Connection=True` as `KOR\app-admin` and drop the SQL login | 07 | M | Removes the plaintext secret rather than hiding it — nothing left to store, rotate or leak | **Ian** | open |
| 112 | Annotate the 2 `async void` methods and comment the 3 cleanup catches | 08 | S | Five minutes; turns the module's test signal green | either | open |
| 113 | Bind `IsEnabled` to `HasSelected` (already exists, unbound) on the ~19 silently-no-op controls, or use the existing `"Select an opportunity first."` pattern | 08 | M | Includes **"Start Pursuit"**, the primary styled button on the Opportunities screen `[READ]` | either | open |
| 114 | Reconcile the win rate with `Definitions.Bd.cs:46-47` (honour `WonLostOutcome`, exclude NoBid/Withdrawn); then reconcile Lifetime Fee (3 ways), the two win ledgers, and the FX rate | 08 | L | Two screens in one app show different numbers for the same thing, and the app's own dictionary says the displayed win rate is wrong and low `[READ]` | either | open |
| 115 | Add a global `DispatcherUnhandledException` handler | 08 | S | There is currently **none anywhere in the app**; five `async void` handlers are safe only by coincidence `[RUN]` | either | open |
| 116 | Fix `Chase RongÃ©` and `crongé@`; dedupe the 3× McLarand / Gura / Rongé / Berger `IntelPerson` rows; resolve `WR26-021` ×4 on the first screen | 08 | M | In a demo whose headline is entity resolution, duplicate entities are the most on-the-nose possible failure | either | open |
| 117 | Register Mulish in the QuestPDF path (`PursuitBriefPdfExporter.cs:59`, `BriefPdfGenerator.cs:1388`) | 08 | S | Client-facing PDFs are currently branded **non-deterministically** — the font registers only if Brochures was opened earlier in the session `[RUN]` | either | open |
| 118 | Fix the 20 lost em-dash separators and the 2 buttons referenced by names they do not have | 08 | M | Date ranges currently render `2015-03-01  2024-11-20` with no separator. This is what "sloppy" looks like to a visiting lead `[RUN]` | either | open |
| 119 | Delete `CompetitionInfoWindow`, `MajorProjectsInventoryWindow`, `PrimePipelineWindow`/`ViewModel`, `RegionBriefDialog` and their DI lines; **fix `OrphanedPublicTypeTests` to not count a lone DI registration as a consumer** | 08 | M | ~1,409 LOC dead, and the gate that should have caught it is blind `[RUN]` | either | open |
| 120 | Add in-grid empty states (Bazaar / Overwatch / Attribution / Events); give `RelationshipsView.DossierHost` default content; bind `CrmViewModel.IsLoading` | 08 | M | Several screens open blank or render five giant em-dashes on failure | either | open |
| 121 | Route historical-document opens through `\\KOR-APP01\OpsArchive` before `C$`; reword the failure message | 08 | S | Currently requires local-admin on the server, and the failure prints `File not found: C:\OpsArchive\…`, blaming the user's own drive | either | open |
| 122 | Upgrade `AngleSharp` past 0.17.1 | 05, 07, 08 | S | Known moderate advisory (GHSA-pgww-w46g-26qg), visible as `NU1902` on every build | either | open |
| 123 | Re-run the 31065 set **with vision** (~$2, needs spend sign-off) and reconcile against the 2026-07-04 Results Brief's category numbers | 09 | S | The shipped brief is the firm's public accuracy claim and it does not currently reproduce in free mode `[RUN]`. Know before someone else checks | **Ian** (spend approval) | open |
| 124 | Fix or explicitly scope the column-schedule read on 31065 | 09 | M | +135% on a headline category, and it currently cancels the slab under-count in the whole-building total | either | open |
| 125 | Wire `SlabTakeoffEngine` into the app: app-side `IPlanVision`/`IPlanRaster` + a "Generate takeoff" button bound to `SlabTakeoffResult.Synopsis` | 09 | L | The Core work is done and host-agnostic. This is the step that turns the differentiator into a product `[RUN]` | either | open |
| 126 | Refresh `docs/Takeoff-RESUME.md` and `docs/Takeoff-Scorecard.md`, or mark them superseded | 09 | S | The RESUME is labelled "read this FIRST" and is wrong on five counts | either | open |
| 127 | App-side tests for the consolidated takeoff window: CSV→xlsx round trip, the `withWeight` branch, `PdfTextWithOcr` on a known image-only page | 09 | M | Closes the six-week code/test gap on the exact window the demo uses | either | open |
| 128 | Move the `KOR_ENGINEERINGTOOLS_STANDARDSDB` connection string off a persisted **User** env var; stop reusing the `opportunities_app` login for `KorStandards` | 10 | M | Plaintext SQL password readable by anything running as that user. The module's own audit doc says *"never setx it"* `[QUERIED]` | **Ian** | open |
| 129 | Put one unfamiliar building through DXF→ETABS end to end | 10 | L | Two buildings, both used to tune it, is not evidence about a third. Named as the single largest gap `[DOC]` | either | open |
| 130 | Have an engineer import a generated `.e2k` into ETABS and sign off | 10 | M | The largest untested surface in the module; nobody has done it `[DOC]` | **Ian** | open |
| 131 | Schedule `Measure-EtabsCorpusRules.ps1` / set `KOR_PORTFOLIO_CHECK` on FS01 | 10 | M | Nothing currently notices a rule drifting away from the 1,126-model portfolio; the variable is set by nothing in the repo `[RUN]` | **Ian** | open |
| 132 | Rotate `standards_reader`'s password and scrub it from `PALETTE-README.md:20` **before** merging `feature/details-palette` | 11 | S | The secret is confined to one unmerged branch. Merging makes it permanent history `[READ]` | **Ian** | open |
| 133 | Fix `ViewSheetExtraCommands.cs:37` to fail **safe** like `:63` | 11 | S | `placedViewIds` is the protection set for a destructive purge; a throw silently makes a sheeted schedule a delete candidate `[READ]` | either | open |
| 134 | Fix or delete the unreachable `result = 8` at `BridgeApp.cs:198-216` | 11 | S | A committed fix that does not fire is worse than a known bug — and `Dialog-Watchdog.ps1` exists on one workstation only `[READ]` | either | open |
| 135 | Rebuild and commit `KOR.Drafter/artifacts/<year>/` so they contain `exportdxf` and the dialog fix | 11 | S | Today, following `BRIDGE-READY.md` installs a bridge **without** the DXF export `[RUN]` | either | open |
| 136 | Correct `BRIDGE-READY.md` (Revit 2020, not 2025) and add `exportdxf` to `docs/PROTOCOL.md` (29 verbs documented, 56 dispatched) | 11 | S | Both are contradicted by live evidence `[QUERIED]` | either | open |
| 137 | Add a KOR Revit export-layer table mapping structural walls / columns / floors to distinct layers; ship it with the bridge; set it in `DXFExportOptions` | 11 | M | The actual fix for the broken Revit→DXF→ETABS chain. The rules side is already settable `[RUN]` | either | open |
| 138 | Teach `ExportDxf` to hide linked models itself, or refuse and say why, rather than depending on a `sethidden` that can be refused | 11 | M | The 2026-08-15 run exported with the architectural link visible after `sethidden` failed `[QUERIED]` | either | open |
| 139 | Count and report the skips in `CsvImportCommand.cs:231,234` | 11 | S | A partial Revit data import currently reports as clean `[READ]` | either | open |
| 140 | Report per-item failures in `RevisionCommands.cs:121` and the rebar parameter writers | 11 | M | Silent revision and rebar-value misses are drawing-issuance risk `[READ]` | either | open |

---

## LATER — real, not urgent

| # | item | module | size | why it matters | who | status |
|---|---|---|---|---|---|---|
| 141 | Extract the shared email-filing logic into one library both the add-in and the app call | 01 | L | Root cause of the filename and sender divergences. Blocked on the add-in being `net48` in a separate solution — needs a `netstandard2.0` shim | either | open |
| 142 | Bound `pageSize` at ~500 in `EmailSearchWindow.xaml.cs:131` | 01 | S | Currently unbounded; `999999` binds the whole result set into a `DataGrid` | either | open |
| 143 | Shrink the 10.2 GB `KorEmailIndex_log` (55 MB used); resolve the Developer-Edition-in-production licensing exposure | 01 | M | If licensing ever forced real Express, the 10 GB cap is **already exceeded** and filing stops dead `[QUERIED]` | **Ian** | open |
| 144 | Make `ItemsToFileProcessor.ProjectsRoot` configurable like the WPF side | 01 | S | The add-in cannot be pointed at a different share without a rebuild | either | open |
| 145 | Decide the fate of `_archive_EmailIndexer`: keep as the documented re-index path, or rebuild it properly | 01 | M | It is the **only** tool that can rebuild the 372k-row index from the share, and nobody knows that `[QUERIED]` | **Ian** | open |
| 146 | Replace timestamp "numbering" (`GraphFacade.cs:352`) with a real per-project sequence | 02 | M | Client-citable transmittal numbers are a Newforma behaviour KOR does not have. Needed for the pitch to be literally true | either | open |
| 147 | Fix `href="/favicon.ico?v=2""`; update the stale `:8000` URL comment at `Program.cs:154`; decide on "NewerForma" in `DashboardWindow.xaml:5` | 02 | S | All three are visible to someone looking closely, and "NewerForma" in front of a firm that pays for Newforma should be deliberate | either | open |
| 148 | Move the non-transmittal tables (`FileSync.*`, `PMTool*`, proposals, identity) out of `KorTransmittals` | 02 | L | 52 tables including three `*_Backup_20251215`. It works; it does not look designed | **Ian** | open |
| 149 | Drive Quartz registration from the `FileSync.Jobs` rows instead of a hardcoded list | 03 | M | Makes the `KorMapSync` class of defect structurally impossible for job #8 | either | open |
| 150 | Drop `FileSync.JobLogTail`, or implement it and stop tailing over `C$` | 03 | M | Dead table (0 rows, 0 code references); implementing it would also make the log viewer work off-LAN | either | open |
| 151 | Lift the credential-redaction classes into `Kor.Operations.Core` (`CredentialPatterns.cs:2`) | 03 | S | The module's only `TODO` | either | open |
| 152 | Build WIP from uninvoiced `tkDetail` labour and expense rather than `PRSummaryMain` | 04 | L | The only route to a real WIP number while Revenue Generation stays off | either | open |
| 153 | Populate `CFGAcctngCalendarData`, or assert the fallback deliberately | 04 | S | 0 rows; benign today because `PeriodEnd` falls back to parsing `YYYYMM`, but it is an undetected empty dependency `[QUERIED]` | either | open |
| 154 | Fix `DeltekSchemaValidator`'s connection capture (`:84-86`), the `ExecutiveSummaryDeltekLoader.Catalog` process-global static, and `FinancialsService`'s transient-registered cache | 04 | M | Hygiene; none currently biting | either | open |
| 155 | Remove the committed Deltek username in `20260807_filesync_kormapsync.sql:36` | 04 | S | Username only, no password | either | open |
| 156 | Wire `AuditContext.ClientApp` (the header already exists client-side) | 05 | S | The column is always null | either | open |
| 157 | Delete `Kor.Operations.Mcp/Vocabulary/` | 05 | S | A 4,142-line abandoned scratch artifact whose header instructs a review that never happened; reads as unfinished work `[RUN]` | either | open |
| 158 | Rewrite `docs/architecture/Kor.Operations.Mcp.md` to describe the 23-tool service that exists | 05 | M | 2.5 months stale; describes "Phase 11b in progress" | either | open |
| 159 | Persist conversation traces, or document the restart behaviour | 05 | M | A service restart silently drops every in-flight conversation's context and the next follow-up answers against nothing | either | open |
| 160 | Execute the AI consolidation roadmap (fold `AnalyticsAiService` into MCP tools) | 05 | L | The plan is sound and explicitly unstarted `[READ]` | either | open |
| 161 | Restore the ellipsis at `CalendarHeatmapPanel.xaml.cs:202` | 06 | S | One lost Unicode character silently chops long project names with no indicator | either | open |
| 162 | Reconcile "utilization" definitions A/B/C, or rename the columns so they cannot be confused | 06 | M | Three definitions, one word, one application | either | open |
| 163 | Batch `SaveSnapshotsAsync` into a table-valued MERGE | 06 | M | ~1,000 round trips if the backfill ever fires | either | open |
| 164 | Add a date bound or a "since year" filter to the project query (`ProjectAnalyticsService.cs:55`) | 06 | M | Unbounded today at 9,904 rows; the 300 s timeout degrades a slow link into a hang, not an error | either | open |
| 165 | Fix CS8629 / CS8604 in `Crm/SqlCrmEngagementStore.cs:107-109` | 07 | S | Live nullable warnings on every build | either | open |
| 166 | Integration tests for `Awards/` (8,073 LOC) and `Ingestion/Providers/` (6,791 LOC) | 07 | L | The 3.1% coverage gap where it actually matters — the code that writes the 139,472-row corpus | either | open |
| 167 | Retire or document `Kor.Opportunities.Capture` as a break-glass operator tool | 07 | S | Unused since 2026-05-21, but it solves an anti-bot problem likely to recur. **Document, do not delete** | either | open |
| 168 | Push Bazaar's unclaimed/undismissed filter into SQL; cap the CRM engagement and Deltek project queries | 08 | M | 5,000 rows pulled and filtered in LINQ, against the repo's own working rule 4 | either | open |
| 169 | Populate `ThePlay` / `FitScore`, or remove both fields from `PursuitBrief` | 08 | L | The real fix behind item 18. Also revives two of eight dead AI prompt context sections | either | open |
| 170 | Add ViewModel tests for `Crm`, `Opportunities` and `Workspace` (currently **0**), starting with `CrmAnalyticsService` | 08 | L | ~34,000 LOC with no behavioural coverage, and it is exactly where the metric contradictions live | either | open |
| 171 | Rationalise folder-vs-navigation naming; add Reports to the BD rail | 08 | L | Reports is currently unreachable from the workspace without backing out to Home | either | open |
| 172 | Unify the two `.e2k` writers behind `Core/Dxf/E2kDocument` | 09 | L | Same file format, two implementations, no shared model, no shared tests | either | open |
| 173 | Delete the two orphan windows (`QuantityTakeoffWindow`, `RebarChangeWindow`) and their DI registrations | 09 | S | Visible leftovers if anyone inspects the app's surface | either | open |
| 174 | Split `TakeoffCli/Program.cs` (2,971 L, ~35 commands, shared with the DXF module) into per-command files | 09 | L | Two modules ship in one binary from one file | either | open |
| 175 | Make the seven keyless DECIDED workbook rows (`C1 F2 M1 M2 O1 P1 S2`) learnable | 10 | L | Answering them today changes nothing in the next model | either | open |
| 176 | Tighten `LiveProjectBaselineTests` tolerance below 10%, or delete it in favour of the hard ratchets | 10 | S | ±10% on 31168 is ±112 walls and ±246 columns — not a regression test | either | open |
| 177 | Make the publish gate fail loudly when `pdftotext` is absent | 10 | S | It currently no-ops silently on any machine without winget Poppler | either | open |
| 178 | Read `CIRCLE` entities, or name the skipped circle-drawn column in the report | 10 | M | Matters only for foreign drawing sets | either | open |
| 179 | Add a smoke-test project for the destructive Revit ribbon commands, or a documented manual checklist | 11 | L | 137 commands, 0 automated coverage, and that is where every defect in module 11 §5 lives | either | open |
| 180 | Decide whether `standards/RULINGS.md` moves into SQL or is declared a human-only register; fix the `E1`/`D1` ID collisions either way | 11 | L | Not a demo risk; a real long-term correctness risk | **Ian** | open |
| 181 | Correct `standards/markup-corpus/LEXICON.md`'s stated SQL home and status banner | 11 | S | It points at a database and a migration that do not exist | either | open |
| 182 | Delete or gitignore `CODEX-PALETTE-SQL-PROMPT.txt` | 11 | S | Dirty working tree on the branch about to be merged | either | open |

---

## Things that are not faults

**Carried forward so no future session re-raises a settled question.** Every entry below was
investigated and found correct, deliberate, or benign.

### DXF → ETABS — the owner's standing NOT-faults list

- **Coincident distinct joints** in generated `.e2k` files. Normal.
- **No diaphragm on generated plates.** Deliberate — the tool does geometry, not engineering.
- **Members reading under 6 ft in the renderer.** Checked; the renderer now prints `under 6ft: 0` itself.
- **`1 0 0 1` skewed panels.** Handled. `ReferenceModelShapeTests.cs` proves the reader does not
  misparse them, and says so in its own doc-comment. The `PLAN-AND-GAPS` C2 entry claiming otherwise is
  **stale**.
- **2″ sliver storeys in site models are real**, not a parsing artifact — site models interleave towers.
- **Unit declaration across the corpus is already measured**: of 1,126 reference models, 936 in inches,
  159 in feet, 29 in millimetres, 2 in metres. Do not re-scan the share for this.
- **A circle drawn as a polyline** now *is* flagged — `StructuralPlanClassifier.cs:628-638` prints
  *"drawn with no arc … Modelled square — check whether it is round."* The `C4` gap entry is **stale**.

### Idiomatic C# and WPF that scanners flag

- **30 of the 33 `NotImplementedException`/`NotSupportedException` in the entire suite are
  `IValueConverter.ConvertBack` / `IMultiValueConverter.ConvertBack` stubs** — the standard WPF idiom
  for a one-way binding converter. The 31st, `CompositionModules/RenderingModule.cs:12`, is a
  **deliberate DI guard** carrying an explanatory message. **Zero of the 33 represent unfinished
  functionality** `[RUN]`.
- **The 2 `NotSupportedException` attributed to `EmailFiler`** are at `ThisAddIn.Designer.cs:194,208` —
  **VSTO-generated code**, not authored.
- **`catch (OperationCanceledException) { }`** and best-effort cancellation callbacks
  (`ct.Register(() => { try { cmd.Cancel(); } catch { } })`) are benign. They are the large majority of
  the 44 "empty catches" counted in Financials.
- **The 3 empty catches in `HtmlBriefPdfGenerator.cs:347,362,375`** are cleanup guards in the
  headless-Edge path — `File.Delete`, `proc.Kill(true)`, `Directory.Delete(scratch)`. They need an
  explanatory comment to satisfy the detector, **not a redesign**.
- **The 2 `async void` methods flagged by `AsyncVoidTests`** each wrap their entire awaited body in
  `try/catch(Exception)` with a logged warning and a user-facing dialog. Neither can escape. They lack
  an annotation, not a guard.
- **19 of the 23 empty catches in the Outlook add-in** are a deliberate posture — "never block Outlook
  startup", "never block Outlook closing". Defensible for a VSTO add-in. *(It is still why silent filing
  failures are invisible until someone reads the share log — that consequence is real; the pattern is
  not a defect.)*
- **`BridgeExec.cs:2711`** — `try { accepted = p.SetValueString(text); } catch { }` inside `setparams`
  **looks** like a swallowed failure and is not: `accepted` stays false, the code falls through to a raw
  `Set` or throws, and `setparams` is all-or-nothing.

### Deliberate design decisions, not gaps

- **`CanonicalOrgDedupJob` is deliberately retired** (2026-06-15), registered as a no-op and gated by a
  default-false flag. Dedup is supervised CLI-only via `tools/BdCanonicalDedup`, because the old job's
  FK list had drifted ~10 FKs behind the schema. **Do not "fix" it by re-enabling it.**
- **769,290 retired `CanonicalOrg` rows are tombstones by design** — *"born-archived on intake: orphan
  procurement vendor; resurrects on any future reference."* The live set of 9,641 is provably
  duplicate-free.
- **FileSync's `Default*` path constants are legitimate** — `ConcreteTestReportsOptions.cs:18,19,21`,
  `MoveReportsToEorOptions.cs:24`, `MoveReportsToToSendOptions.cs:22,27`, `WatcherOptions.cs:27` are all
  `public const string Default*`, **overridable by `FileSync.JobKnobs`**, pointing at real UNC shares
  that exist. *(The one genuine defect in that set is `WeeklyPmDeadlinesOptions.cs:21`, a per-user
  OneDrive profile path with no knob overriding it — item 81.)*
- **`WatcherHostedService.cs:45`** — `@"\\Newforma\\email($|\\)"` is a **regex**, not a path. False
  positive in the cross-cutting scan.
- **`EmailFilingService.cs:48`** — the hardcoded audit-log share path is wrapped and falls back to a
  local `%LOCALAPPDATA%` log when unreachable. It degrades; it does not throw and does not block filing.
- **`FirmDefaultsEdgeCaseTests.cs:211`** — the hardcoded SAFE 22 path is a *string value* round-tripped
  through a settings-persistence assertion, **never touched on the filesystem**. The test passes on a
  machine with no such path.
- **KOR.RevitTools' "3 hardcoded test paths"** are inline JSON fixture strings never touched on disk,
  plus two `StartsWith(@"\\Kor-fs01", …)` assertions against a tracked config file. All 79 tests pass.
- **`HtmlBriefPdfGenerator.cs:332-333`** — the two Edge install paths are probed with
  `FirstOrDefault(File.Exists)` and fall back to QuestPDF. Correct.
- **`OdbcType.Date` vs `DateTime`** — both forms appear in working production paths;
  `OdbcType.DateTime` **threw** a DataDirect protocol error where `OdbcType.Date` returned rows.
  **Leave every date binding alone.**
- **Hiding the WIP tile** (`ExecutiveSummaryService.cs:398`) is **correct**, not a gap. With Revenue
  Generation off, `PRSummaryMain` carries no usable unbilled figure.
- **`ScoringProfile` having 0 rows** is not a broken editor — the ViewModel falls back to
  `ScoringOptions.KorDefaults()`, so the window is populated.
- **`Kor.Opportunities.Capture` is unused but not junk** — a deliberate one-shot operator tool for
  clearing Cloudflare Turnstile on APC. Document it as break-glass; do not delete it.
- **`_archive_EmailIndexer` is genuinely archived and nothing live depends on it** — but it is the
  **only** tool that can rebuild the 372k-row index from the share. That is the reason not to delete the
  folder.
- **`FileSyncExcludedFromAiTests`** asserts that `FileSyncCommandCenterViewModel` does *not* implement
  `IAiContextProvider`. It is not functional coverage, but it is a real and deliberate architectural
  guard keeping FileSync data out of AI prompts.
- **`WebView2` degrading to "Preview pane unavailable… DOCX export still works"** is correct handling,
  not a defect.
- **The `Kor.Operations.App.Tests.csproj` living inside a folder named `Kor.Transmittals.App.Tests/`**
  is a rename leftover, not a defect — but a path built from the folder name fails with `MSB1009`, which
  is probably why the stale `AGENTS.md` claim about it was never rechecked.

### Facts that have already been measured — do not re-measure

- **`AGENTS.md` is wrong on all three of its known-broken claims.** `EmailFilerv2` builds clean in one
  MSBuild invocation; `Kor.Transmittals.App.Tests` passes 7 of 7 in 255 ms `[RUN]`.
- **The DXF→ETABS output counts are 31168 = 63 / 1,119 / 2,462 / 82 and 31138 = 29 / 242 / 390 / 13**,
  agreed by three independent sources. The figures 917 / 2,469 / 83 and 24 / 87 / 172 / 11 in
  circulation are **stale and appear nowhere in the current system**.
- **Revenue Generation is OFF** — `Revenue` equals `Billed` on 47,246 of 47,366 rows (99.75%), `Unbilled`
  populated on 0.5% `[QUERIED]`. And `SUM(Revenue)` is **$69,061,768.57**, not $0.
- **Deltek is reached three-part (`[catalog].dbo.Table`) over ODBC** and four-part
  (`[DELTEK_VP].[catalog].dbo.*`) over the linked server. Both are correct for their path.
- **Transmittal "numbering" is a UTC timestamp**, not a sequence.
- **The BD deferred register is D1–D12. D13 does not exist.**
- **11 analytical + 11 sector BD reports ship, none of them stubs.** "A–C" was phase vocabulary, never a
  count. The in-app visuals recorded as "PENDING COMMIT" are **shipped and unit-tested**.
- **The BD desktop surface is 13,139 / 14,127 / 6,625 LOC (~34k)**, not the ~25k in circulation.
- **The MCP exposes 23 registered tools.** The "25" counts 23 + `ServerInfoTool` (MCP-wire ping,
  deliberately outside the `/ask` registry) + `ToolErrorEnvelope` (a static helper).
- **`Kor.Operations.EngineeringTools.Core.Tests` holds 483 tests, not ~432.**
- **`claude-sonnet-4-6` is current and not deprecated.** No demo-killer there.
- **Poppler `pdftoppm` is no longer required** by the takeoff CLI — it renders with bundled PDFium.
- **The six May-2026 adversarial-audit findings on BD dedup are all fixed**, verified in current code,
  several with a comment citing the finding ID.
- **Empty-catch counts in `02-CROSS-CUTTING-SCAN.md` are floors, not counts** — its regex misses
  comment-only and bare-return bodies. The module hand-counts (23 in email, 83–129 in KOR.RevitTools)
  are the real numbers.
- **The "no hardcoded secrets in source" finding was retracted** in the scan itself. The secrets are in
  `.config`, `.json`, `.ps1` and one `.md` — not in `.cs`.
