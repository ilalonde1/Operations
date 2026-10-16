# NetworkOps self-audit, 16 October 2026

Read-only. Three parallel code audits (Core data + rules; Service + data flow; App UI code) plus a visual pass: every
NetworkOps screen rendered (27 fixture + 3 against the live fleet, `NetworkOpsWindowsRenderTests` with
`KOR_NETWORKOPS_RENDER_LIVE=1`) and looked at. About 110 raw findings, merged and de-duplicated below. **Nothing has been
changed yet.** Scope: NetworkOps end to end — WorkstationOps is its PC-health half (no screens of its own).

| # | Tier | The one-line answer | Count |
|---|---|---|---|
| 1 | Wrong today | Features that fail or state that is wrong now — incl. "install and restart" failing on every idle machine since 10-06, a down switch port clearing itself, restarts offered as the fix for problems a restart can't clear | 15 |
| 2 | Stale shown as current | The class that misled us on 10-15: values reach the screen without the time they were read; the fleet is assembled five different ways | 11 |
| 3 | One language | Four units for "what's wrong", 7 date formats, 3 reds, 68 of 125 rules filed under "Other", raw keys and SHOUTING in text people read | 9 |
| 4 | Flows | The landing tab can't open a machine; fixing, acknowledging, refreshing and status each work differently per screen | 10 |
| 5 | Plumbing | The console rebuilds the fleet ~6× per 15–20 s; serial trigger queue; no debounce; orphans; no error middleware | 9 |
| 6 | Harness | Why nobody saw most of this: the landing screen and every error/empty/stale state are never rendered; nothing checks the language | 3 |

Marks: **✔** re-verified in code by Claude this session · **A** from an audit pass with quoted code, not re-verified ·
**P** plausible, needs the stated confirmation · **👁** seen in a render.

Prior art checked first: the 10-06 hardening audit, the 10-03 WorkstationOps audit + re-audit, the agent audit, the
mesh-wedge and KOR Remote responses. Items already open there are not repeated (hardening #4, #6, #7, #9, #10, #12, #13).

---

## 1. Wrong today (correctness / safety)

| # | Finding | Where | Mark |
|---|---|---|---|
| 1.1 | **"Install and restart if needed" fails on every machine nobody is actively using.** UpdateBatch only asks to confirm when presence is Active and queues the restart variant `confirmed=false`; the runner's 10-06 gate (654cc922) refuses every unconfirmed disruptive fix. A regression from the hardening fix. | `Service/Updates/UpdateBatch.cs:40-42`, `UpdatesApi.cs:46`, `Sweep/ActionRunner.cs:117-118` | ✔ |
| 1.2 | **An acknowledged finding stays hidden when it escalates** (Warning→Critical keeps AcknowledgedUtc). | `Store/NetworkOpsStore.cs:255-260` | ✔ |
| 1.3 | **A stale MeshCentral read clears every `mesh-*` finding**, writes resolutions for fixes that never happened, then re-raises them as New (new ids, acks lost, mailed again). BIOS has the right guard; Mesh doesn't. | `Sweep/HealthSweeper.cs:90,98-111` | ✔ |
| 1.4 | **A down switch port / firewall link clears itself on the next read while still down** (compares with the previous read, which is then overwritten). Includes the Critical LAN link-down. | `Core/Rack/NetworkRules.cs:245-248,367,390-393` | ✔ |
| 1.5 | **The one-click "fix" for `not-patched`, `gpu-hangs`, `crash-loop`, `outlook-index`, `probe-incomplete`, `*-rising` is a disruptive restart** (first catalog match); "Clear in one action" then claims one restart clears 14/60-day event counts. `free-disk-space` (C: only) is offered for D: and server drives. | `Core/Learning/ToClear.cs:69`, `Actions/FixCatalog.cs:44-50`, `ClearByAction.cs` | ✔ |
| 1.6 | **To clear's disruptive confirm defaults to Yes** (no owner, no default) — every other surface defaults No. | `App/NetworkOps/NetworkOpsToClearView.xaml.cs:239,277` | ✔ |
| 1.7 | **Server timeouts are silent** — the 30 s HttpClient timeout throws TaskCanceledException, caught as "superseded": status stays "Loading…", the connection-lost banner never shows. | `NetworkOpsClient.cs:58`, `CommandCenterWindow.xaml.cs:70,96,230` | ✔ |
| 1.8 | **"Last restart" is shifted 6–7 h** — LastBoot is PC-local, `ToLocalTime()` treats it as UTC. The `not-patched` evidence date is UTC while the tile is local. | `Core/Health/PcComponents.cs:84`, `HealthRules.cs:134` | ✔ |
| 1.9 | **The headline tiles don't add up** — live: 1 + 23 + 31 + 5 = 60 "of 59": a stale machine is counted twice; "Watch" counts as Healthy. Four units of "what's wrong" (things / issues / findings / machines) on one screen. | `CommandCenterViewModel.cs:302-316,102,233` | ✔👁 |
| 1.10 | **The port colour key is wrong** — says "Amber = link down"; the code paints down/bad **red** and suspect amber. Says "this PC" on servers. The Network tab gives amber a third meaning. | `DeviceWindow.xaml:204`, `DeviceViewModel.cs:289-295`, `NetworkView.xaml:152` | ✔👁 |
| 1.11 | **"Someone is using it right now" uses a presence reading of any age** (days old out of hours) to refuse or allow restarts. | `Store/NetworkOpsStore.CommandCenter.cs:32-34,316`, `ApiHost.cs:325`, `UpdatesApi.cs:42` | A |
| 1.12 | **`updates-due` can never clear** — the scan counts feature upgrades/Previews that the install skips. | `Probes/updates.ps1:13` vs `Actions/install-updates.ps1:15` | A |
| 1.13 | **A rack read failure looks like an empty filter** ("Nothing in the rack matches") — the error goes to properties nothing binds. | `CommandCenterViewModel.cs:221`, xaml `:258,307` | A |
| 1.14 | Workstation AV rules run on servers using the agent (`av-none` can fire on Server, which has no SecurityCenter2). | `HealthRules.cs:195-211` | P (BK01's findings) |
| 1.15 | A finding bouncing between severities is mailed on every rise (compares with the stored, not the notified, severity). | `FindingDiff.cs:30-31` | A |

## 2. Stale data shown as current

One structural fix: **every value served carries the time it was read; one fleet assembly feeds the App, MCP and CLI.**
The network map already does this (`NetworkMapService.Response`) — the model to copy.

- **The fleet is assembled five ways** — `/api/fleet` (agent + mesh), `/api/rack` (mesh only), MCP (raw → the known
  `agentConnected=false`, no mesh at all), `/api/to-clear` + `/api/updates` (raw), the prompt library (both).
  `ApiHost.cs:191-196,339-343`, `NetworkOpsMcpTools.cs:48`. A
- **Findings carry no "last seen" to the UI** — `LastSeenUtc` is stored and served, read by nothing in the app; a PC off
  for 6 days still counts in "Fix on all N"; a machine shows stale only after 3 days. `ToClear.cs:15`, `CommandCenterView.cs:46`. A
- **Mesh state falls back to the stored table without its age**, and the stored table holds raw (not de-duplicated)
  links. `ApiHost.cs:447-454`, `MeshSweepJob.cs:33-35`. A
- **`mesh-silent` and the agent findings are only re-judged by the weekday 07–18 hourly sweep** while the Connected flags
  update live — "not connected" beside a working Connect all weekend. `HealthSweeper.cs:89-90`. A
- **Job failures are invisible** — `JobRuns` is written, read by nothing; the heartbeat beats while jobs fail; the per-job
  gate waits with no timeout (a hung run blocks that job silently). `NetworkOpsStore.cs:302-326`, `Scheduling.cs:57`. A
- **A failed update scan wipes the last known list** (newest observation regardless of status). `NetworkOpsStore.Updates.cs:20-24`. A
- **Patch state "unknown" reads as healthy** — v14: no KB in the ~5 surviving entries → `LastPatch` null → no row, no
  finding. The pre-v14 fallback scalar is not classified (a driver can still show as the patch). `HealthSnapshot.cs:191`. A
- **The history window is too short** — `QueryHistory(0,50)` is mostly Defender signatures → ~5 real entries (~2 weeks).
  Feature updates ("Windows 11, version 26H2") carry no KB → "other". ✔ (live 10-16)
- **Windows Update install failures (event 20) are collected and never judged.** `Metrics.cs:64`. A
- **The device page never refreshes** after opening (no timer, no button); a fix run elsewhere never shows on it. A
- **Two readings of one UPS on one screen** (Power panel 52 min / 23 % vs the rack tile 48 min / 24 %). 👁

## 3. One language, one set of numbers, one palette

- **Units:** the headline counts machines, the nudge counts issues, the status bar counts findings, the chips count
  machines with an "issues" tooltip. → one unit (**issues**), machines as the second number; tiles partition (each
  machine counted once).
- **Terms:** PC / machine / device / computer / Workstation; finding / issue / problem / reason; Critical / Warning / Info
  vs Needs attention / to look at / Overdue; Not seen lately / STALE / Silent / quiet / Can't reach; Re-check all /
  Check this PC now / Read this device now / Search again now. → **PC** (workstation), **device** (rack), **machine**
  (both); **issue**; **Critical / Needs attention / Watch**; **Not seen in X**; **Check now**; **Refresh**; **restart**
  (never reboot).
- **Categories:** 68 of 125 rule families fall into "Other" (incl. `not-restarted`, `not-patched`, `ups.on-battery`
  Critical, `esxi.datastore-lost` Critical); `agent-silent` is filed under Updates; hardware errors under Failing
  storage; dead entries `volume-full`, `av-conflict`. `ToClearCategories.cs:20-33`. A
- **Overlapping rules:** patch is judged 3 ways (`updates-due` 3/14 d, `not-patched` 35 d, `server.unpatched` 45 d);
  restart 3 ways (`reboot-overdue` fires on uptime > 72 h, not pending > 72 h — a false positive; `not-restarted`;
  `server.reboot-pending`). → one patch issue and one restart issue per machine. A
- **Dates:** 7 formats (ISO, `ddd d MMM HH:mm` with no year, `MMM d HH:mm`, `d MMM`, `14 Oct 2025`, `HH:mm:ss`,
  "20 d ago"); "Ago" implemented 3× with different output; uptime 3×; truncation 4×; the disk-full rule copied twice
  and already drifted. A👁
- **Colour:** three reds, three ambers, three greens; 51 hex literals outside the style file (39 distinct). White on the
  amber pill ≈ 2.3:1 and on Watch blue ≈ 3:1 — both fail WCAG AA. The style test only scans `*Window.xaml` (To clear has
  5 primary buttons against the 2 allowed). A
- **Text:** "(s)" plurals ("1 devices", "1 other PC(s)", "device(s) have"); "--" in user text; SHOUTING ("ON BATTERY",
  "DRY RUN COMPLETE", "NOT running", "FAILED"); jargon evidence ("14 d: 0 bad-block…", "hrPrinterDetectedErrorState
  0400", "ntpd running=False", "{temp} C" with no °); repeated phrases ("no more security fixes" twice); raw model
  strings ("-[7X06CTO1WW]-"); "held 3 days" unexplained (and "released 0001-01-01" when no date is known). A👁
- **Raw keys reach people:** the digest's Cleared list (`low-disk:c`), fleet patterns, the rack System tile
  (`fw.read.ticks` as 18-digit ticks, `fw.up`). `AlertDigest.cs:61`, `RackComponents.cs:169-187`. A
- **The Windows tile shows any non-Win10 product as "11"** (Server 2019 → "11"). `PcComponents.cs:81`. A

## 4. Flows (clunk)

- **To clear — the landing tab — cannot open a machine.** It is built without the navigator, names are plain text, yet
  its copy says "open the PC to confirm". The same dead end on Updates, Deploy, the Network table, pattern members and
  "Also open on…". ✔👁
- **Fixing differs per surface:** To clear is one click; the device page is a modal; "Fix…" shows when the issue has no
  fix; an empty fix dialog is unexplained; the dialog offers unrelated fixes ("Start a service", "Run a command" for a
  restart). A👁
- **Acknowledge / snooze differ:** device page = a day / a week / reopen / note; To clear = a week only, no reopen, no note. A
- **Status:** two stacked status bars on hosted tabs; the device page has three feedback places; exception type names
  shown raw ("Load failed: {Type}"); the Prompt Library shows a SQL file path. A
- **Refresh:** the console polls fleet + rack + power + to-clear every 15 s on every tab and To clear separately every
  20 s (badge and headline disagree); Auto-refresh governs Fleet only; the 20 s refresh collapses an open "Show N
  reasons"; Updates / Network / Deploy / the device page never refresh; the banner promises auto-retry with
  auto-refresh off. A
- **The device page locks for minutes** while following a fix (up to 60 min plus a fixed 35 s wait). A
- **Duplicate windows:** every Open makes a new device window; a Network port click opens the device and offers a
  second; every Ask Claude opens a new window — 3 clicks and 2 windows to reach Claude. A
- **Layout:** To-clear cards use the left 600 px of 1400; Updates columns wrap while the detail pane is empty; a 52-port
  switch grid on every PC page (to say "port 29, 1G"); the "Patterns" panel takes half a column to say nothing; ~20
  rack tiles each repeat "read 4 min ago"; drive and system tiles truncate names; permanent instruction paragraphs. 👁
- **Blue dots on the Memory / BIOS / Windows tiles** with only one issue under "Open now" — the dots point at issues the
  list doesn't show, or mean something unexplained. 👁 P (confirm what the dot binds to)
- **Ask Claude** is the only button on a no-fix issue; the useful next step (open the machine) is under "More". 👁

## 5. Plumbing and performance

- The console rebuilds the whole fleet ~6× per 15–20 s; `/to-clear` builds 2 snapshots, `/updates` 3; MCP `device`
  builds 2 to find one machine. → `/api/summary`, a per-sweep cache, a single-device endpoint. A
- The trigger queue is serial: one dead PC's "check now" (300 + 40 s) holds every on-demand request. A
- The rack sweep has no debounce: one failed read = Critical `rack.unreachable`; acks are tied to the row id, so a
  flapping printer returns unacknowledged every morning. A
- Machines removed from AD are never retired → their findings never close; `/api/changes` counts them, the console doesn't. A
- No error middleware (empty 500s; `/api/network` returns ProblemDetails the client can't read; MCP returns errors as success). A
- `netops run` reports the expected route, not the route taken (agent → network fallback). A
- Renaming a rack device or a PC starts a new device (history, notes, acks lost). A
- A restart re-runs mid-run triggers, `AgentRollout` included, without asking. A
- Dead or stale: `/api/network/moves` (no caller), the appsettings `Rack` list (ignored once `RackInventory` has rows),
  the `Program.cs` "twice-daily" comment (the cron is hourly), the MCP "reads only" summary, unbound `RackHeadline` /
  `IsLoading` and others. A

## 6. The harness (why nobody saw most of this)

- The render test never shows the real landing screen (console on To clear), the Fleet table, the connection-lost
  banner, any error / empty / loading / stale state, the picker, the no-fix dialog, or the device page mid-fix. A
- The live render can't run from a test (the test host can't see the app's config) — worked around for this audit with
  a temporary config copy; it needs a proper switch.
- Nothing checks the language: nothing fails on "(s)", "--", a raw rule key in user text, a hex colour outside the
  palette, a date not through the shared formatter, a rule family filed under "Other", or an unlabelled fact key.

---

## Checked / not checked

Checked: every file in `App/NetworkOps` (except the KorRemote internals); Core Health / Learning / Actions / Probes /
Rack; the Service jobs, sweeps, API, MCP, store and schema indexes; the CLI's fleet / check / watchdog; 30 renders, 9 of
them looked at by Claude (all 3 live).
Not checked: the Power chain internals, NetworkMapService beyond `Response`, agent enrolment, SNMP / on-target timeouts,
live APP01 environment variables, SQL execution plans, and interaction (clicks) in the running app. Every **P** above is
unconfirmed.
