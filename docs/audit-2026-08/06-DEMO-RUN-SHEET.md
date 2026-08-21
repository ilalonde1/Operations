# 06 — Demo Run-Sheet: MVE

**For the owner, in the room.** Built from the eleven module audits, all compiled 2026-08-20.
Every fact here is `[RUN]`, `[QUERIED]` or `[READ]` from those reports. Nothing is guessed.

---

## The 60-second version

**Bring the VPN up BEFORE you launch the app.** Not after. Not "it'll connect." Before.

**Lead with transmittals and download telemetry.** Then email search. Then DXF→ETABS.
Money screens late and short. **Do not open Business Development at all.**

**Three sentences you must not say:** *"it's all in source control"* · *"ETABS opens it"* ·
*"Revit to DXF to ETABS works end to end."* All three are false today. All three are fixable.

**One screen that could end the meeting:** clicking **Business Development** lands on a dashboard
listing **25 named architecture firms with KOR's written plan to displace their engineers**. MVE is
an architecture firm, and MVE is in that database.

**If something dies mid-demo, go to §6.** There is a pre-generated fallback for every segment.

---

## 1. Pre-flight — before the laptop opens

Work top to bottom. Do not skip the ones marked ⛔.

### 1.1 Network — what actually needs the VPN

| Needs VPN? | What |
|---|---|
| ⛔ **YES** | **MCP `/ask` AI** — `http://kor-app01:5500`, plain HTTP on an RFC1918 address, no TLS, no internet route. `[QUERIED]` |
| ⛔ **YES** | **All SQL** — `KOR-APP01\SQLEXPRESS` TCP 1433: email index, transmittals, favourites, BD, MCP audit, **and `KorStandards`** |
| ⛔ **YES** | **`\\Kor-fs01\Projects\Projects`** — email filing destination, project autocomplete, DXF inputs |
| ⛔ **YES + local admin on KOR-APP01** | FileSync log viewer (`\\KOR-APP01\C$`), historical-opportunity documents |
| ✅ **NO** | **Deltek's own grids.** The ODBC DSN targets `vp-ca-hdp01.prd.mydeltek.com:443` — cloud, TLS, over the internet `[QUERIED]`. Financials populates from MVE's office *provided the DSN and credentials are on the laptop.* |
| ✅ **NO** | **The transmittal tracking site** — `https://tracking.korstructural.com`, public, TLS, `/health` 200 from anywhere |
| ✅ **NO** | **PDF→SAFE, rebar compare, quantity takeoff** — zero KOR network dependencies. Fully offline. |

> ⚠ **One disagreement between audits, and you should settle it yourself.** Module 04 marks Deltek
> ODBC "internal endpoint — VPN required"; modules 05 and 06 both `[QUERIED]` the DSN and it points
> at a public Deltek cloud host. **Check it on the demo laptop, off the KOR network, before you
> travel:** `Test-NetConnection vp-ca-hdp01.prd.mydeltek.com -Port 443`. Do not find out in the room.

### 1.2 ⛔ The AD gate fails OPEN — and this demo is the trigger condition

`HomeWindow.xaml.cs:295-308` `[READ]`. The Home screen hides tiles by AD group membership. Off the
KOR LAN the domain-controller lookup **throws**, and the `catch` does not re-apply the gate — it
**force-shows seven surfaces**:

> Financials · **Compensation (salary and bonus — the most sensitive screen in the suite)** ·
> PM Tools · Standard Details · General Tools · Fee Proposal Builder · Engineering Tools

Six others fail *closed* (FileSync, Monday Briefing, COO Card, Opportunities, Business Development,
BD Reports). **The inversion is worst exactly where it matters: the money surfaces open up.**

It is **silent**. Nothing warns you.

**What to do, in order:**
1. **VPN up first, app second.** Always. A cold launch on MVE's guest wifi or a hotel network is the
   exact trigger.
2. **Pre-flight the Home screen** the moment the app opens. Count the tiles. If Compensation is
   visible and you did not expect it, **the tunnel is not up — close the app, fix the VPN, relaunch.**
3. If a code fix ships before you travel, it is ~30 minutes: collapse `FinancialsTileHost` and
   `CompensationTileHost` in that `catch` instead of showing them.

### 1.3 ⛔ Software on the demo laptop

| Item | State on the dev machine `[QUERIED]` | Action |
|---|---|---|
| **CSI SAFE** | **NOT INSTALLED.** `C:\Program Files\Computers and Structures` does not exist | Install + licence it, **or drop the PDF→SAFE payoff shot**. Exporting a file to a folder is anticlimactic. |
| **CSI ETABS** | **NOT INSTALLED.** All five CSI COM ProgIDs unregistered | **Do not promise to open the `.e2k`.** Use the renderer (§3, Segment 3). |
| **SAP2000** | Not installed | Not needed |
| **Revit 2026** | Needed only for the ribbon segment. Building the add-in does *not* need Revit — the API comes from NuGet | Only if you run Segment 7 |
| **Deltek ODBC DSN `Deltek`** (64-bit, DataDirect HDP 4.6) | Must be on the **presenting** machine | Verify, plus machine env vars `KOR_ODBC_USER` / `KOR_ODBC_PASSWORD`. Without them every financial window fails to load. |
| **Outlook desktop + the VSTO add-in** | ClickOnce `EmailFilerv2 1.0.0.49` inside `V15.zip` | `Get-ItemProperty 'HKCU:\Software\Microsoft\Office\Outlook\Addins\EmailFilerv2'` → **`LoadBehavior` must be `3`** |
| **The WPF app path** | `HostExeResolver` order: appSetting → env var `KOR_OPERATIONS_APP_PATH` → next to the add-in DLL → `C:\Newerforma\Kor.Operations.App.exe` | On Ian's machine **only the env var resolves**. If none resolve, "File Selected Emails" silently does nothing. |
| **First SAFE OAPI push** | Triggers a one-time **UAC elevation** for `RegisterSAFE.exe` | Run it once in rehearsal. Never hit a UAC prompt live. |

### 1.4 ⛔ Seed the favourites — 2 minutes, and it is visible if you skip it

Insert rows into `KorTransmittals.dbo.UserFavorites` for **the demo account**. With none, the
favourites pane and the Quick File dropdown are **both blank**, and the whole filing feature reads as
half-built. `[QUERIED]`

### 1.5 ⛔ Clear the FileSync Watcher failure

There are 3 open Watcher failures in 7 days. The one on screen is a PDF with a **leading space** in
its filename `[RUN]`:

```
' 31056-01 2026-08-21 10th & Highbury Issued for Draft IFC.pdf'
  ↑ leading space → Microsoft.Graph ODataError: Invalid request
```

Nothing trims the filename before upload, so **it has failed on every pass since it appeared and will
keep failing forever.** **Rename the source file on the share. Do not change code under time
pressure.** A red `Failed` row dated today invites "what failed?" — and the UI cannot answer, because
`JobRuns.ErrorMessage` stores the run *summary*, not the exception.

### 1.6 Redeploy the MCP service from HEAD — the single highest-value hour

The deployed build is **34 days behind HEAD** `[RUN]`. That is stranding three fixes:

- **`get_wip`** returns earned and overbilled **transposed** (a $209,298 net sign flip).
- **`get_cash_position`** sums **all 20 bank accounts instead of the 3 whitelisted** — it pulls in
  petty cash and USD savings.
- **`get_billed_pnl`** uses flat 1.36 FX where the app uses 1.378457 — USD figures drift ~1.4%.

**Rebuild and robocopy — and it must include `Kor.Operations.Business.dll`, not just the MCP DLL.**
Then **verify the artifact, not the version string**: `/health` alone would not have caught this.
Re-scan the deployed DLL for `SplitWipNet` (UTF-8) and `CashAccountWhitelist` (UTF-16).

**If it does not ship: WIP and cash questions are barred from the room.** Both are natural questions
in a CFO-flavoured demo and both will contradict the screen beside them.

Also set `"BilledDefaultOrg": "CAD"` in the deployed `appsettings.Production.json` — one value.
Without it, asking the AI to confirm the P&L tab gives a **different number** than the tab.

### 1.7 Screen hygiene — 10 minutes, high payoff

- **Clear the Desktop.** Several brief exports write **straight to the Desktop with no Save-As
  dialog** and then open the PDF over the app `[READ]`. Your Desktop currently holds `Jim\`,
  `Rory\`, `Network Overview\`, `Structural Quantity Takeoff Demo\`, `CSI Ingestor\`.
- **Close the repo.** Close the editor. Close file dialogs pointed anywhere near it. See §4.
- **Close `set-filesync-env*.ps1`** in the repo root — they carry a **live Entra client secret and a
  SQL password in plaintext** and are visible in any folder listing `[QUERIED]`. (Correctly
  gitignored. Still on screen.)
- **Close Slack, Teams, and email notifications.**
- **Decide about the window title.** The Transmittals Dashboard is still titled *"KOR NewerForma —
  Transmittals Dashboard"* `[READ]`. In front of a firm that pays for Newforma that is either a
  deliberate joke you land, or an accident you get asked about. Pick one now.
- **If you will export any branded PDF: open the Brochure Builder once first.** The Mulish font is
  **not installed**, and the only place it gets registered is a static constructor in the Brochure
  renderer `[RUN]`. Open Brochures once and every later PDF is branded correctly; skip it and the
  branding silently falls back.

### 1.8 Rehearse — on the actual demo laptop, off the dev box

The consolidated engineering-tools window has **zero app-side tests**; the wiring is only covered by
you running it. Time these three:

1. The email round trip (file → search → open), end to end.
2. The rebar compare on the two 15–18 MB PDFs. **Get the real wall-clock number.** There is no
   progress bar beyond a wait cursor.
3. Three AI questions. **Mean latency is 10.5 s; the observed max is 72.9 s** `[QUERIED]`.
   **Drop any question that takes more than ~20 seconds.**

---

## 2. Running order — and why

**Total ≈ 70 minutes plus Q&A. Segments 6 and 7 only if there is time and appetite.**

| # | Segment | Min | Why here |
|---|---|---|---|
| **0** | **Ask the five questions** | 10 | You do not yet know their stack. KOR's own MVE dossier records **nothing** about their document or PIM stack. Ask before you build a segment on ground they already own. |
| **1** | **Client transmittals + per-recipient download telemetry** | 8 | **Deltek PIM already files Outlook email in one click — but PIM's own 26.0 help topic list has no Transmittals entry at all.** So filing is table stakes if they run PIM; transmittal evidence is not. And this is the claim with the best evidence in the whole suite: 829 transmittals, 2,682 click events, **zero nulls in IP, user agent or recipient email** `[QUERIED]`. It is also **read-only** — nothing here writes, sends or spends. Open where you are strongest and safest. |
| **2** | **Email search — corpus depth** | 7 | Concede filing immediately, then win on the corpus: **372,370 emails across 955 projects back to 2014-10-28**, and `seismic review` returns **7,216 hits in under a second** `[QUERIED]`. Against Egnyte's two-month-old add-in, the decade of history *is* the argument. Second because it is the most finished thing you own and it builds trust cheaply. |
| **3** | **DXF → ETABS** | 10 | **The rarest thing you have, and the only one aimed at their deliverable rather than your books.** No commercial equivalent was found across four research passes. It is deterministic, pre-generatable, and visual. Third, while attention is still high. |
| **4** | **Rebar steel-weight delta** | 5 | Stays in the engineering lane you just opened, and closes with a physical artifact — a marked-up drawing, added work in green, removed in red. Needs **no VPN and no network at all**, so it is your safest segment if the tunnel is unstable. |
| **5** | **Financials + the AI** | 8 | **Late and short, deliberately.** MVE is an architecture firm — your P&L is the least relevant thing you own to them, and it carries the most data risk (a six-month ledger gap and, until the redeploy, two tools that answer wrong). Include it because it answers *"isn't this just an MCP wrapper?"* — but scope it to AR, collections and utilisation, which are genuinely current to this morning. |
| **6** | *(optional)* FileSync Shadow mode | 5 | Only if asked "how does this run unattended?". The Shadow/Live set-piece is a genuinely good "we don't guess in production" story. Three landmines — see the segment card. |
| **7** | *(optional)* Revit tools ribbon | 6 | Only if Revit is on the laptop. It is your best **continuity** story: 137 tools, one codebase, seven Revit versions, deployed from a share with 27 rollback snapshots — replacing 195 obfuscated DLLs a departed developer left behind. That is the honest answer to "what happens when you leave," and it is worth telling out loud. |
| **✕** | **BD Brain / Business Development** | — | **Recommended: omit entirely.** See §4.1. This is your call, not the audit's, and the choice is laid out there with the four mitigations. |

**Why not lead with email filing:** if they run Deltek PIM, filing is solved for them and you would be
opening on their strongest ground. Ask question 3 in Segment 0 first. If they *don't* run PIM, filing
becomes a live capability rather than table stakes — but even then, transmittals is the better opener,
because the evidence is stronger and nothing on that screen can write.

---

## 3. The segments

### Segment 0 — Ask, before you show (10 min)

Neutral, casual, in the first ten minutes:

1. *"What are you filing project email into today?"*
2. *"Newforma — Project Center or Konekt?"* (Project Center has **no AI at all**; every AI feature is Konekt-only.)
3. *"Do you run Deltek PIM?"* — **this one reshapes Segments 1 and 2.**
4. *"Are you on Bluebeam Max?"* (If yes, pitch rebar on the **steel-weight delta**, never the change list.)
5. *"Do your GCs run Procore or Trunk Tools?"*

---

### Segment 1 — Transmittals + download telemetry · 8 min · **read-only**

**Entry point:** `Kor.Operations.App` → Home → **"Search Transmittals"** card
(`HomeWindow.xaml:98`) → the Transmittals Dashboard.

**Do this:**
1. Search with an **empty box**. The grid returns Created / Sent / Project # / Type / Subject /
   **Opens** / **Clicks**. Real rows verified live `[QUERIED]`: `31183-01 "updated Foundation plan"`
   → **22 opens / 7 clicks**; `30978-01 "Dilworth - SSI#32"` → **30 opens / 10 clicks**.
2. **Select a row.** The **Activity** list below shows individual events: Open/Click, timestamp,
   **recipient email, client IP, user agent.** *This is the moment. Slow down here.*
3. Browser → `https://tracking.korstructural.com/health` → `OK`. Real, public, TLS-terminated, IIS.
   Not a localhost toy.
4. Optional: `https://tracking.korstructural.com/filedrop?to=<a KOR address>` — the branded inbound
   upload page, **mobile-responsive**, that lives in staff email signatures.

**The line:** *"For a delivery date defended in a dispute, 'downloaded three times' isn't evidence.
'Ravi, 10:42, from this IP' is."*

**What could go wrong:**
- ⛔ **Do not live-send.** The "External link" checkbox defaults to **OFF**, which creates an
  organisation-scoped SharePoint link — **an MVE recipient clicking it lands on a Microsoft sign-in
  wall, on stage** `[READ]`. If you must send live, tick it, or use Quick Transfer.
- ⛔ **Do not quote 8,947 opens.** Opens run 3.3:1 against clicks and much of that is mail-scanner
  prefetch. Anyone who knows email tracking will discount your whole evidence claim. **Clicks.**
- ⛔ **Do not show the database schema.** `KorTransmittals` is a 52-table junk drawer including
  identity tables, fee proposals, and three `*_Backup_20251215` tables.
- If asked about numbering: *"project number plus timestamp."* Give it; don't let it be discovered.

---

### Segment 2 — Email search · 7 min

**Entry point:** Outlook ribbon → **Search Filed Emails** (or the app's home screen; it opens
straight into the grid via `--email-search`).

**Do this:**
1. Type **`seismic review`**. **7,216 hits, under a second** `[QUERIED]`.
2. Say the corpus out loud: **372,370 emails · 955 projects · back to 2014-10-28 · 183,745 with
   attachments · full-text catalog fully populated and current.** Two writers are live and busy —
   ~50–100 emails a day from real staff.
3. Filter by project, by date range, by has-attachments.
4. **Optional round trip, only if they don't run PIM:** file a message via the picker, then search
   and find it immediately. Filing waits on the index insert, so it is findable the moment it lands.
   **Use an external sender.**

**What could go wrong:**
- ⛔ **Do not alt-tab to File Explorer.** **39% of emails filed in the last 30 days carry a
  `4501-01-01 0000 - ` prefix** on the filename `[QUERIED]` — a MAPI null-date sentinel the guard
  misses. The database dates are correct; the filenames are not. It is the highest-probability
  embarrassment in this segment and it is one glance away.
- ⛔ **Use an external sender for any live filing.** ~6.5% of recently-filed rows render an Exchange
  X.500 DN (`/O=EXCHANGELABS/OU=…`) in the **From** column. It is concentrated in the drag-to-folder
  path, so the picker path with an external sender avoids it.
- ⚠ **Do not click "Open" if the connection is flaky.** The fallback `Process.Start` at
  `EmailSearchWindow.xaml.cs:412` sits **inside** the catch with no guard of its own — on an
  unreachable file it throws from a WPF event handler and **takes the window down** `[READ]`.
- Do not type a large page size. It is unbounded above 1.

---

### Segment 3 — DXF → ETABS · 10 min · **pre-generate everything**

**Entry point:** a terminal. **There is no button.** Say that before you open it — *"this one runs
from a command line today"* — rather than letting a console window after a polished WPF app read as
"prototype."

**The command, verified end to end today `[RUN]`:**

```
takeoff.exe dxf-to-etabs "<dxfFolder>" "<reference.e2k>" "<out.e2k>" `
    --rules-db $env:KOR_ENGINEERINGTOOLS_STANDARDSDB --report r.txt --questions q.xlsx
```

**Use 31168 — smaller input, more impressive output (a rebuild, not a gap-fill):**

| | Path |
|---|---|
| DXF folder | `…\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\_DXF-plans-for-rebuild` — **62 files, 16.6 MB** |
| Reference | `31168-reference.e2k` (43 KB) |

**Copy both to the laptop. It is 17 MB.**

**What appears:** 62 of 62 sheets placed → **63 storeys · 1,119 wall panels · 2,462 columns ·
82 floor plates · 4,233 joints**, in **50.7 seconds** wall clock reading over SMB, exit 0.

**Then — the picture, and it needs no ETABS licence:**
`tools\Render-E2kModel.ps1 -E2k <model> -OutPng <png>` → a **four-panel isometric / two elevations /
composite plan PNG in 9.2 seconds**, generated members in red, the engineer's own in grey `[RUN]`.

**Then finish on the honesty, because that is the selling point:** open the 173-line report that says,
location by location, everything it could **not** do. And the questions workbook — every judgement
call, each answerable in one cell, and answering one **banks it as a database rule that every later
job uses with no code change**. 35 rules live as database rows; **none is a constant in C#, and a
missing rule stops a production run by design.**

**If they mention 31138:** it merged into the engineer's own export without duplicating anything —
*"306 walls and 316 columns were already modelled … not added again."* **29 / 242 / 390 / 13.**

**What could go wrong:**
- ⛔ **It needs `KorStandards` over VPN.** `RequireRuleSettings` is hardcoded `true` and there is
  **no offline mode** `[READ]`. If the tunnel drops, the headline demo dies with a SQL error.
  **Pre-generate the `.e2k`, the PNG, the report and the workbook before you travel.**
- ⛔ **Do not claim ETABS opens it.** **Nobody has imported a generated model into ETABS**, and ETABS
  is not installed. Say: *"it writes the `.e2k` in ETABS's own format with the geometry already
  entered — and I'll be straight with you, we haven't yet sat an engineer down to import one and sign
  it off. That's the next thing on my list."*
- ⛔ **Do not invite an MVE drawing set.** Against a US National CAD Standard set, `_COL` misses
  `S-COLS` on the underscore and `SLABEDG` misses `S-SLAB-EDGE` on the hyphen — the model *"comes
  back with walls and no columns or floors, which looks like a building rather than like a failure"*
  `[READ]`. The per-job layer overrides exist but have **never been run on a foreign set.**
- ⛔ **Do not open the job folder.** The artifacts on the share are five commits stale — 31138's
  workbook is missing its *Rules in force* sheet and 31168 has no summary PDF at all.
- ⛔ **Do not hand anyone the one-pager.** `docs/KOR-DxfToEtabs-onepager-web.pdf` is currently an Edge
  **"File not found" error page** committed to the repo, and the publish script copies it into job
  folders `[RUN]`. Re-render it and verify with `pdftotext`, not by opening it in a browser.
- ⚠ **Type the command correctly.** `takeoff.exe` with no arguments prints the **wrong usage line** —
  an old CSV-diff command, out of ~35 subcommands with no help and no version. Keep the command in a
  text file and paste it.
- **Say "two buildings, and both were used to tune it."** Do not imply a track record you don't have.

---

### Segment 4 — Rebar steel-weight delta · 5 min · **no network needed**

**Entry point:** Home → **Engineering Tools** → *Structural Quantity Takeoff* → **second tab
(Compare Two Issues)** → Pick BEFORE / Pick AFTER → **Generate change report** and **Generate visual
markup**.

**Files, verified on disk `[QUERIED]`:**
- BEFORE: `…\Structural Quantity Takeoff Demo\Inputs\31065 - BEFORE (IFT Addendum 2025-10-07).pdf` (17.8 MB)
- AFTER: `…\Inputs\31065 - AFTER (IFC 2026-03-06).pdf` (15.2 MB)
- Known-good outputs sit beside them — use these if the live run is slow.

**The line:** *"Generic change detection will be a checkbox in software you already own within a
year, and I'm not going to argue with that. Ours isn't a change list — it's a steel-weight delta off
the bar list. That's the number a detailer prices."*

**What could go wrong:**
- ⚠ **Dead air.** Two 15–18 MB PDFs, read fully, OCR'd where a page is image-only, then diffed —
  with **no progress bar beyond a wait cursor**. **Time it in rehearsal. If it runs over ~90 seconds,
  open the pre-generated outputs instead and say you prepared them.**
- If asked about scanned drawings: *"we read the drawing's own call-out text; a flattened sheet gets
  OCR'd and flagged 'verify'. We don't read pixels the way a vision model does."* **Do not say
  "blind to scanned sets" — that is wrong.** The OCR path has **zero test coverage**; don't stress it.
- ⚠ **If Bluebeam Max comes up, concede it fast and pivot to the weight delta.** Trunk Tools is ahead
  on the general problem and pretending otherwise loses the room.

---

### Segment 5 — Financials + the AI · 8 min · **short and scoped**

**Entry point:** Home → **Financials** tile. The window opens on Overview in about **1.5 seconds**
against a live ODBC connection `[RUN]` — cold connect 960 ms, four loaders 252/82/180/77 ms.

**Do this, in this order:**
1. **Executive Summary → AR Outstanding, DSO, collections exposure, utilization.** These are
   **genuinely current to today** `[QUERIED]`. Click a tile → the drilldown grid → down to the
   invoice row. The drill-through is the impressive part.
2. **Say the freshness boundary out loud, before anyone reads it off a column:** *"the pipeline is
   real-time — a second and a half end to end. The ledger behind it is only as fresh as our
   accountant's last posting. AR, utilisation and collections are current to this morning; the
   summary ledgers stop at February."* Turning that into a statement of rigour costs nothing.
   Discovering it costs the segment.
3. **Run the schema validator** if the conversation turns to durability. `DeltekSchemaValidator`
   comes back **CLEAN across all 34 expected columns against 676 live columns** `[RUN]`. Microsoft's
   own docs say a renamed source column **fails** a Power BI refresh. This survives it, and it is
   demonstrable on screen.
4. **The AI panel** is docked in this window. **Two or three questions, all rehearsed and timed.**

**Safe questions** (they route to structured tools whose deployed code matches the dashboard):
AR and aging · backlog · firm health / net multiplier · utilization · **billed P&L for a CAD-org
period** · at-risk projects · per-PM performance · project deep-dive.

**What could go wrong:**
- ⛔ **Never ask about WIP or cash position** until the redeploy of §1.6 has shipped and been
  artifact-verified. Both currently return numbers that contradict the screen beside them.
- ⛔ **Do not open the P&L tab and then ask the AI to confirm it.** The deployed service uses a
  different Org scope than the app (`BilledDefaultOrg` `""` vs `CAD`) — you get two different numbers
  on stage. Fix the config or skip the flourish.
- ⛔ **Do not compare USA figures across Partner Financials and the Billed P&L.** They convert at
  **1.378457 and 1.36** respectively `[READ]`. They will not tie.
- ⚠ **Latency.** Mean 10.5 s, max 72.9 s. Ask one-tool questions, never multi-period comparisons.
- ⚠ If a loader hiccups, it returns a confident **$0** rather than an error `[READ]`. Low
  probability, high embarrassment. If you see a $0 backlog, say the query failed and move on.
- **If asked "why is there no WIP tile?"** — this is a good question with a good answer:
  *"Deltek isn't running revenue recognition in our tenant, so we don't publish a WIP number we
  can't stand behind."* That shows rigour. Being caught without the answer does not.

---

### Segment 6 *(optional)* — FileSync Shadow mode · 5 min

**Entry point:** Home → **FileSync Command Center** tile. Needs AD group `FileSyncCommandCenter`
**and local admin on KOR-APP01**.

**The set piece:** flip a job to **Shadow** → press **Fire now** → the run appears in ≤5 seconds →
open the shadow output folder and show the **plan file it wrote instead of moving anything** → flip
back to **Live**. That is a real "we don't guess in production" story, and it is the compensating
control to point at when someone asks about tests — because **this module has none.**

**Live and true:** the service has been up **8 days straight**, 2,108 recorded runs, **97.8% success
over the last 7 days**, failure alerts that demonstrably landed in the inbox today `[QUERIED]`.

**What could go wrong:**
- ⛔ **Do not open the Log Viewer on today's date.** It shows a **blank grid on a healthy service**
  — the tailer trusts a stale SMB directory length. Yesterday's log works correctly. "Show me the
  logs" is the single most likely spontaneous request in this segment. `[RUN]`
- ⛔ **Do not linger on `KorMapSync`.** It advertises *"Daily at 3:00 AM — next fire in 4 h"* for a
  job **Quartz was never told about**. It has never fired on a schedule in its life, and the public
  project map is 8 days stale. The countdown is fiction. `[QUERIED]`
- ⚠ The health panel reads **`Shadow`** while all seven jobs are **`Live`**. If anyone reads that
  column they will either think nothing is running or ask what else the UI is getting wrong.

---

### Segment 7 *(optional)* — Revit tools ribbon · 6 min

**Entry point:** Revit 2026 → the **KOR Tools** tab, on Autodesk's own *Snowdon Towers Sample
Structural* model (deliberately chosen so nothing depends on KOR's template). Playbook:
`KOR.RevitTools/docs/DEMO-PLAYBOOK.md` — Select Similar → Hide/Show Elements → 3D Box on Selection →
Change Beam Type. **No SQL, no VPN, no services.**

**The story to tell, out loud:** 137 tools on one ribbon, built from one codebase across seven Revit
versions, **buildable on a machine with no Revit installed** because the API comes from NuGet,
deployed firm-wide from a share with **27 rollback snapshots** `[RUN]`. It replaced 195 obfuscated
DLLs with no source, left behind by a developer who is gone. **The thing that made the old estate
un-inheritable has been designed out.** That is your best answer to "what happens when you leave."

**What could go wrong:**
- ⚠ **Say 137.** The playbook says 28 and the build-status doc says 79. The ribbon on screen says
  137, and it will contradict you.
- ⛔ **Never run `RemoveUnusedViews`.** It **fails open** on sheeted schedules — a schedule that *is*
  placed can silently drop out of the protected set and become a delete candidate `[READ]`.
- ⛔ **Confirm the details palette is dormant** — no `detailsPalette` block in
  `%PROGRAMDATA%\KOR\kor-tools.json`. Otherwise it opens an **empty list**: 1,079 catalog rows, **0
  placeable** `[QUERIED]`.

---

## 4. Screens to avoid — and exactly why

### 4.1 ⛔⛔ Business Development — the highest-risk surface in the suite

**Three facts, all `[QUERIED]` against live data.**

**(a) The default landing screen leaks competitor strategy with no click at all.**
`BdWorkspaceWindow` **opens on `DashboardView`**, and two panels render **at load**:

- **"Open Structural Seats"** — **25 named architecture firms** with KOR's written displacement
  strategy in a visible column. One row reads, verbatim: *"…KOR should proactively reach out to
  **Christopher Bozyk and Sandra Bai**…"* — named individuals at another architecture firm.
- **"Competitor Watch"** — **12 named rival SE firms** with capacity assessments
  (*"at capacity — three major hospital projects…"*, *"in transition — Englobe acquisition…"*).
- "Priority Actions" additionally renders `TargetPersonName` as *"target: {name}"*.

**This is a target list of architecture firms with the incumbent engineer named and the plan to
unseat them. MVE is an architecture firm.**

**(b) MVE is in the database as a target.** `CanonicalOrg` **76952 — "MVE + Partners"**,
`Kind = Architect`, carrying a `ClendorClientId` (so also a Deltek client record). Attached:
**18 `IntelPerson` rows with real `@mve-architects.com` addresses**, including President **Matthew
McLarand** with his LinkedIn URL; **25 affiliation rows** — a reconstructed MVE org chart; an
enrichment payload naming McLarand a decision-maker; and **12 MVE projects with their incumbent
structural engineers named** (Glotman Simpson, Englekirk/WSP, Nelson, John A. Martin).
**MVE ranks #14 in a shipped report whose subtitle is *"Warm-intro priority list."***

**(c) Four one-keystroke paths reach that record — and single-click-commit shipped, so there is no
longer a highlight-without-opening margin. One stray click opens all of it.**

> **The one mitigating fact, and state it plainly if this ever comes up:** there are **0 displacement
> briefs, 0 pursuits, 0 engagements and 0 fee or margin records against MVE.** Nothing derogatory
> about them is stored anywhere. The exposure is not that you hold dirt on them — it is that they
> would see themselves **catalogued as a target alongside their competitors**, and see other
> architects' names with strategy attached.

**The recommendation is to omit the BD module entirely. That is your call, not the audit's.** If you
want it in, here are the four mitigations, all small:

1. **Suppress the two Dashboard panels** — `DisplacementRead` at `DashboardView.xaml:589` and
   `CapacityRead` at `:659` — **or land the workspace on a different view.** Two `Visibility`
   bindings. This is the single highest-value fix in the module.
2. **Gate the `DISPLACEMENT BRIEF` panel** behind a demo flag (`OrgDossierView.xaml:610`).
3. **Hide the `Competes` and `Agent Profile (AI)` columns** (`CompetitionInfoView.xaml:125,144`).
4. **Suppress org 76952 and its 18 person rows** for the demo window. The mechanisms already exist
   (`RetiredAtUtc` / `EnrichmentSuppressedAtUtc`). ⚠ **This is a data write, so it needs your explicit
   authorisation — the audit was read-only and could not do it.**

**Even with all four, still do not open:** BD Scorecard · BD Tracking · Client Intelligence ·
Relationships · Pursuit Monitor · Market History Awards · any `Kind=Architect` org dossier · any
`PersonDossierWindow` · `BdReports` → Architect Frequency / Competitor Intelligence / Strategic
Relationships / Pursuit Dossiers / Opportunity Attack Cards.

**Never type** `MVE`, `McLarand`, `Glotman`, `Englekirk`, `WSP`, `Nelson`, `Carrier Johnson` or
`JWDA` into the workspace global search or the client search box.

Two more BD screens worth naming:

- **"BD Scorecard"** — publishes KOR's **win rate** and **per-staff won fee** in currency, with 177
  engagements carrying `ProposedFee` and `TargetMargin`. Showing a prospective client your win rate
  and target margins is a negotiating handicap. ⚠ **And the number is provably wrong** — it counts
  no-bids and withdrawals as losses, which the app's own written methodology says *"drastically
  understates the real win rate."*
- **`CompetitionInfoSourcesWindow`** ("About sources", one click from Market History) — a literal
  **"Coming next"** roadmap of three **unbuilt** features, plus KOR's scrape cadence (*"the
  enrichment scraper runs every 5 minutes, the document downloader every 10"*) and the internal
  server path `\\KOR-APP01\OpsArchive\Opportunities\<Id>\` printed on screen. **Announcing unbuilt
  features to the audience is the opposite of the goal.**

### 4.2 ⛔ The Employee Summary tab (Historical Analytics)

**56 named KOR staff, each with a 0–100 productivity score and a colour-coded letter grade. In the
latest stored quarter, five real people carry an F — scores 33, 36, 41, 46, 46.** `[QUERIED]` The
grid is **sortable**, so one click puts the firm's worst-graded employees at the top of the screen in
front of an outside architecture partner. There is no role gate at the tab.

It is also **forced ranking by construction** — the efficiency component is a percentile, so somebody
is always at zero no matter how well the firm performs. That is exactly the question a sharp lead asks.

**And opening it writes to production SQL.** `RecomputeEmployeeSummary` fires from **all nine filter
setters**, and ends in a fire-and-forget task that MERGEs a snapshot row **per employee** for the
current quarter. There is no Q3 row yet, so **opening the tab on stage inserts one for ~40 staff** —
computed over whatever filter was last clicked. Failures are caught and logged, so nothing visible
goes wrong. It just quietly happens.

**Tell the productivity story from PM Summary or DM Summary instead** — role-level, not person-level.
The Projects, Fee Band, Construction Type and Year-over-Year views are genuinely strong and carry no
personnel data at all.

### 4.3 ⛔ The KOR.Drafter repository

Its own README opens: *"PRIVATE virtual-drafter workstation kit — Do not publish, reference, or copy
any part of this repo."* **It is confidential from KOR's own drafting team.** It has no UI, it runs on
one workstation, and it should not be on screen or in a conversation with an architecture partner at all.

### 4.4 ⛔ `get_wip` in the AI — and `get_cash_position` until the redeploy

With Revenue Generation off, **both WIP branches draw their entire signal from 238 residual rows —
0.5% of the table.** Any WIP figure `/ask` returns is a residue, not a measurement, and nobody can
defend it. The WPF tile is already correctly hidden; **the MCP tool is the exposed edge.**
`get_cash_position` is summing 20 bank accounts instead of 3 until §1.6 ships.

### 4.5 ⛔ Anything carrying competitor intelligence or MVE-adjacent records

- **The Operations repository, and any file dialog near it.**
  `docs/bd-dossier-mve-mclarand-2026-06-17.md` names Matthew McLarand as *"Jim's personal contact"*,
  estimates MVE's revenue at *"~$22–23M"*, and states the play: *"Displacing Glotman on MVE's next OC
  luxury high-rise, through Jim ↔ Matthew, is the #1 play."*
- **`CompetitorProfileWindow`** — it labels architecture firms with hostile chips. NORR Architects &
  Engineers and Robson Design Build both render as **"DIRECT COMPETITOR"**; it shows AI competition
  notes verbatim and **names executives at the other firm.**
- **Any San Diego architect dossier.** Three displacement briefs cover San Diego, including **JWDA**
  and **Carrier Johnson + Culture** — both priority `high`. **Chase Rongé, MVE's San Diego director,
  is ex-Carrier Johnson.**
- **The Compensation tile.** Salary and bonus data — and it is one of the seven surfaces that **fail
  open** when the AD lookup fails (§1.2).

### 4.6 Smaller things, same rule

- **File Explorer during the email segment** — the `4501-01-01` filenames.
- **The FileSync Log Viewer on today's date**, and the **KorMapSync** row.
- **Any database schema** — `KorTransmittals` is a 52-table junk drawer.
- **Unfiltered org or people lists** — `Chase RongÃ©` renders as mojibake, Matthew McLarand exists
  twice, `Mbm Ventures Inc.` appears a dozen times. **In a demo about entity resolution, duplicate
  entities are the most on-the-nose possible failure.**
- **The Opportunities Hub default view** — the top eight rows by relevance contain the same White
  Rock tender **four times**, alongside rows literally titled *"APC – Notification of New Postings."*

---

## 5. What MVE will ask — and what you say

Say these close to verbatim. Every one is true and checkable.

**"Do the links expire?"**
> "No, and I'd rather tell you that than have you find out. Our redirect link is a tracker; the thing
> that actually grants access is the SharePoint sharing link behind it, and we don't set an expiry on
> it. Which means a forwarded email gives a stranger the files — and our log would attribute that
> click to the original recipient. That's a gap, not a feature. Newforma's Info Exchange has had
> expiry and reminders for years. Retention is on my list; it isn't shipped."

**"Are you tracking whether I opened your email?"**
> "Yes — per recipient, with the IP and the browser, and we keep it. That's the whole point of the
> product: when a delivery date is disputed two years later, 'downloaded three times' isn't evidence
> and 'Ravi, 10:42, from this IP' is. I'll also say plainly that there's no notice to the recipient
> anywhere in the email today, and for BC privacy law an email address with an IP and a timestamp is
> personal information. That's a decision we made deliberately; I'm not going to pretend we didn't."

**"Is the search semantic?"**
> "No. It's SQL Server full-text — fast and exact, not semantic. Newforma's Smart Search isn't out
> yet, and when it ships they're ahead of us on search. Here's what ours does have —" *(type
> `seismic review`)* "— 7,216 hits in under a second, across 372,370 emails and 955 projects going
> back to 2014. And it searches message bodies, not just headers and metadata."

**"Does it suggest the project automatically?"**
> "No. The user picks it — from favourites or a type-ahead. There's no inference code of any kind in
> there. Konekt does smart project suggestions off email content and sender, and Newforma says their
> AI filing is in customers' hands today. Theirs is ahead of ours. Ours is deterministic, which I
> like for auditability, but it isn't smarter. What I'd point at instead is that 372,370 filed emails
> say people actually use it — which is more than most firms can say about Newforma."

**"What happens when you leave?"**
> "It's real and I'm not going to talk you out of it. Here's what we did about it: the whole Revit
> estate used to be 195 obfuscated DLLs left behind by a developer who's gone, with no source at all.
> That's now one codebase, 137 tools, seven Revit versions, and it builds on a machine that doesn't
> even have Revit installed — the deploy is a copy to a share with 27 rollback snapshots. We
> deliberately designed out the thing that made the old estate un-inheritable. The surface is small,
> it's tested, and the record lives in storage we already own — if every line of my code vanished
> tonight, the project files are exactly where they are this morning. Then I'd ask you the same
> question back about Project Center — Newforma's on its second private-equity owner and every 2026
> AI feature is in Konekt only."
>
> ⚠ **Do not say "it's in git."** The redirector is not, and it has not compiled since March. If you
> fix that before you travel — it's about thirty minutes — put the sentence back in.

**"Isn't this just an MCP wrapper?"**
> "The protocol isn't the achievement and I'd never sell it as one — MCP is table stakes now.
> What's behind it is 23 typed functions that return the same record for the same inputs, over 29
> Deltek tables, and the model never computes a number — it picks a tool, our audited SQL computes,
> and it narrates. Microsoft's own docs push you to exactly that shape, because their SQL connector
> can't express a GROUP BY. And because everyone standardised on MCP, you could call ours from your
> Teams tomorrow — that's a URL and a token."
>
> **If they push on security, own it rather than improvising:** *"Today it's a single service
> credential on a LAN-only endpoint with a full audit trail — every question and every tool call
> logged against the caller. Per-user Windows Auth and TLS are the next step. What it is not, and
> never was, is our financial data leaving our building on somebody else's key."*

---

## 6. Abort list — when something dies mid-demo

**The universal rule: name it, move on, do not debug on stage.** *"That's the VPN — I've got the
output here"* costs you nothing. Twenty seconds of confused clicking costs you the room.

**Prepare this folder before you travel. One folder, on the Desktop, called `Demo`:**

| Pre-generate | For |
|---|---|
| Screenshot of the Transmittals Dashboard + an expanded Activity list | Segment 1 |
| Screenshot of `seismic review` → 7,216 hits | Segment 2 |
| **31168 `.e2k`, the four-panel PNG, `r.txt`, `q.xlsx`** | Segment 3 — **non-negotiable** |
| `31065 - Rebar Takeoff & Change (IFT to IFC) FULL.xlsx` + the 2.3 MB markup PDF | Segment 4 |
| Screenshots of the Exec Summary KPI wall and one AR drilldown | Segment 5 |
| A short screen recording of the FileSync Shadow set-piece | Segment 6 |

| If this fails | Do this |
|---|---|
| **VPN drops** | ⛔ **First: close the app.** Do not keep clicking — the AD gate has already failed open and Financials and Compensation are now visible to whoever is at the keyboard. Then switch to Segment 4 (rebar) or Segment 7 (Revit), **neither of which needs the network at all**, while you fix the tunnel. |
| **The AI panel is slow or hangs** | It has a **4-minute timeout and no cancel button**. Do not wait. Say *"that one's reaching for ad-hoc SQL — let me take a different route"* and answer it from the dashboard tile instead. Every one of the 39 logged failures in the last 60 days was this same timeout. |
| **The AI answers with an error message that looks like an answer** | ⚠ **Know this shape.** The AI client **never throws — it returns its error as the answer string.** So *"Unable to reach AI service: No such host is known."* gets painted into the card **under a confident green "Drafted 14:32 from live intel."** If an answer starts with *"Unable to reach"*, *"AI service returned HTTP"* or *"AI is not configured"*, **that is a failure, not a draft.** Say so and move on. |
| **A financial number looks wrong or renders $0** | Say *"that loader didn't come back — it reports zero rather than erroring, which is a thing I need to fix"* and move to AR or utilisation. Never argue with a number on screen. |
| **`takeoff.exe` errors, or prints the wrong usage** | Do not retype it. Open the pre-generated PNG and report and narrate those. *"I ran this before we started — here's the output and here's the timing."* |
| **The rebar compare runs long** | At 90 seconds, stop waiting and open the pre-generated workbook and markup PDF. |
| **The email round trip fails** | Fall back to search-only. Search is the stronger half anyway. **Do not go looking for the file on disk.** |
| **The Transmittals window won't load** | Go to `https://tracking.korstructural.com/health` and `/filedrop` in a browser. **Those are on the public internet and work from anywhere**, including MVE's guest wifi. |
| **Anything crashes to the desktop** | There is **no global unhandled-exception handler anywhere in the app**. Relaunch, and pick up at the next segment rather than the same one. Do not retry the click that killed it. |
| **You land somewhere on the avoid-list by accident** | Close the window. Do not narrate it, do not explain it, do not scroll to prove it's harmless. Go to the next segment. |

---

## 7. The five things that must be true before you travel

1. **VPN comes up before the app. Every time.** (§1.2)
2. **The MCP service is redeployed from HEAD and artifact-verified** — or WIP and cash are barred. (§1.6)
3. **Favourites seeded; the Watcher failure cleared; the Desktop and the repo closed.** (§1.4, §1.5, §1.7)
4. **The `Demo` folder exists with every pre-generated fallback in it.** (§6)
5. **You have decided about Business Development** — omit, or apply all four mitigations including
   the data write only you can authorise. (§4.1)

**And the thirty-minute fix worth doing above all others: put the redirector in git.** It is the one
untracked thing external parties actually touch, and it is the difference between a strong answer to
"what happens when you leave" and a hedge.
