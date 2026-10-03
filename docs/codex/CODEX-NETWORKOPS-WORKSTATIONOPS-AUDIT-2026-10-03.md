# CODEX — whole-module adversarial audit: NetworkOps + WorkstationOps (2026-10-03)

**This is a WHOLE-MODULE audit, not a one-defect brief.** Ian asked (2026-10-02, going to bed) for a
deep adversarial audit of the entire NetworkOps / WorkstationOps module, with a report "that you
address point by point with verification steps to completion. Autonomously." This file is both the
audit and the response: every finding carries its verdict, the fix, and how the fix was verified.

## What the module is

KOR's own remote monitoring & management tool — the replacement for the MSP's agent. A Windows
service on **KOR-APP01** runs the census, health/rack sweeps, the port map, update scans and the
Command Center HTTP API; **agents** on workstations execute server-sent PowerShell as SYSTEM; a
**CLI** and a **WPF app** are the clients. State is in SQL (`KorNetworkOps`). "WorkstationOps" is the
workstation side: health probes, SMBIOS, updates, power chains, BIOS.

## Method

Seven adversarial reviewers, one per dimension, read the module in parallel and reported concrete
findings (file:line, failure scenario, severity) without editing:

1. Agent / enrolment / transport trust boundary (RCE surface)
2. Concurrency & shared state
3. SQL & data integrity
4. Network-map build correctness
5. Workstation probes + SMBIOS + updates + power + BIOS
6. Error handling, resource lifetime, the API host
7. The WPF app (NetworkOps windows)

Every finding was then verified against the code before any fix, fixed, and re-checked. The
`/agent/v1/package` DoS and the DI-gate blind spot were each found by **two** independent reviewers.

### Prior art, re-verified

The agent RCE boundary was audited through Codex on 2026-09-30
(`CODEX-NETWORKOPS-AGENT-AUDIT*.md`). This pass re-checked its "partly closed" items and found them
now solid: cert pinning in both directions with no fallback, the 256-bit CSPRNG key stored only as a
SHA-256 and compared with `FixedTimeEquals`, job authorization + `ProcessTree` kill-on-close
containment + SSH host-key pinning, revocation bound to the credential generation, and the
pre-existing-descendant / junction install hardening. The new work since that audit — the 0.26
enrolment feature, printers, the port map, the app UI — had never been audited; most new findings are
there.

## Headline

No finding rated **critical**. The two **high** correctness bugs are an SMBIOS parser that crashed
the whole fleet's hardware run on a short structure, and a DI-gate that only watched one of the four
files feeding its container (the exact shape of the Oct-2 outage). The rest are robustness, a
self-inflicted DoS on an unauthenticated route, two AD-sync data-integrity bugs, several "an exception
escapes the per-item isolation and freezes/aborts the whole sweep" bugs, and app-crash escape hatches.

**Shipped as service 0.27.0.** Tests: **499** in the NetworkOps suite (was 487; +12 new), **64** in the
app's NetworkOps tests — all green. Both projects build.

---

## HIGH

### H1 — SMBIOS: a short Memory Device (Type 17) crashed the whole fleet's hardware run
`Core/Smbios/SmbiosParser.cs:118` (`ParseDimm`)
`BitConverter.ToUInt16(d.Data, 0x0C)` ran with no length guard, unlike every other field. A Type-17
structure with a formatted area of 4–13 bytes passes the walk's `flen >= 4` check, then throws
`ArgumentOutOfRangeException` — and `Cli hardware` calls `Parse` with no catch, so one bad machine's
blob aborted the run for the whole fleet. **Fixed:** read the size field only when `Data.Length >= 0x0E`,
else treat the slot as empty (mirroring the existing field guards).
**Verified:** `SmbiosParserTests.A_memory_device_too_short_for_its_size_field_is_an_empty_slot_not_a_crash`.

### H2 — SMBIOS: a short Chassis (Type 3) crashed the same way
`Core/Smbios/SmbiosParser.cs:112` (`ParseChassis`)
`c.Data[0x05] & 0x7F` with no guard; a Type-3 of flen 4–5 throws `IndexOutOfRangeException`. **Fixed:**
read the type byte only when present (else Unknown). **Verified:**
`SmbiosParserTests.A_chassis_too_short_for_its_type_byte_is_unknown_not_a_crash`. The test file's
coverage note was corrected — it had claimed to cover "short or corrupt input" while exercising only
`flen < 4`, not short type-specific structures (a rule-11 broad-name/narrow-check slip).

### H3 — the DI-binding gate watched one of four files feeding the container
`Tests/ApiHostServicesTests.cs`
The gate exists because of the Oct-2 exit-1067 outage (an endpoint asked for a service ApiHost's
container didn't have, so minimal-APIs inferred it as the body and the host refused to build). But it
read only `Api/ApiHost.cs`, while `SessionApi.cs`, `Updates/UpdatesApi.cs` and `Agents/AgentApi.cs`
map endpoints into the **same** container — a latent reintroduction of the outage in three of the four
files, and the exact "broad name on a narrow check" failure rule 11 warns about. No live mis-binding
today (verified). **Fixed:** the gate now scans all four files, checks each file was seen, and allows
interface registrations. **Verified:** the broadened gate passes (no unregistered endpoint service
across the four files), and it fails loudly if a listed file goes missing.

---

## MEDIUM

### M1 — `/agent/v1/package` was an unauthenticated CPU/memory amplifier (found twice)
`Agents/AgentApi.cs:111`, `Agents/AgentEnrolment.cs:66`
The package route is before-key (unauthenticated by design) and rebuilt the whole agent zip at
`CompressionLevel.Optimal` into a `MemoryStream` on **every** request, uncapped — anyone on the
LAN/VPN could loop it into CPU/GC pressure on APP01, degrading the whole service. **Fixed:** the zip is
built once and cached, rebuilt only when a deploy changes the package (file count / newest write
time); and a new `UseBeforeKeyGate` bounds the body (8 KiB) and caps concurrency (8) on the two
before-key routes. **Verified:** build + the existing `AgentEnrolment`/`AgentApi`/`AgentEndToEnd` tests
(14) green; the cache is a single lock-guarded build keyed on a directory signature.

### M2 — one bad probe output froze the whole port map, forever
`Service/Network/NetworkMapService.cs:110` and `Core/Network/NetworkMap.cs:36,47`
Two ways the map build threw and `RackSweepJob` caught it as "Port map not built", leaving the last
map frozen: (a) `ParseLeases` was not isolated and `_leasesUtc` was set only *after* it, so a junk
DHCP payload re-threw every sweep forever; (b) `site.Devices.ToDictionary(d => d.Mac)` and the live
`liveUp` dictionary threw on a duplicate MAC (a re-adoption artifact, or the same MAC in two cases) —
the only two MAC-keyed maps in `Build` that didn't dedup. **Fixed:** the lease read degrades to the
last-good leases with a note (like the fleet read); both dictionaries use `GroupBy`. **Verified:**
`NetworkMapTests.A_controller_that_lists_a_device_twice_does_not_throw`; lease isolation by code
inspection (the catch mirrors the adjacent fleet-read catch).

### M3 — a junk-but-valid probe payload aborted the whole sweep/scan
`Core/Health/HealthSnapshot.cs:61`, `Core/Updates/UpdateRules.cs:25`
`(root is JsonArray a ? a[0] : root)?.AsObject()` threw `ArgumentOutOfRangeException` on `[]` and
`InvalidOperationException` on a scalar — neither is the `JsonException` the per-machine catch in
`HealthSweeper`/`UpdateScanner` handles, so one machine's junk output aborted the entire sweep and
every other PC silently lost its check that cycle. **Fixed:** both `Parse` methods now fail as
`JsonException` on those inputs. **Verified:**
`HealthRulesTests.A_probe_that_returns_junk_but_valid_JSON_fails_as_a_JsonException` and
`UpdateTests.Junk_but_valid_JSON_fails_as_a_JsonException_the_scanner_isolates` (4 inputs each).

### M4 — `/api/network` could serve a torn (map, time, notes) triple
`Service/Network/NetworkMapService.cs`
The published map, its build time and its notes were three separate unsynchronized writes, read by the
HTTP handler with no lock; and `SetLive` wrote two fields a refresh could read half-updated. A reader
could get a fresh map stamped with a 30-minute-old time and notes from a different build. **Fixed:** the
three are now one immutable `Snapshot` swapped in a single `volatile` write; the live read is one
immutable `LiveState` (json, why-not, when). **Verified:** build; atomic by construction.

### M5 — on-demand triggers bypassed `[DisallowConcurrentExecution]`
`Service/Jobs/Scheduling.cs`, `Service/Sweep/TriggerPoller.cs`
The attribute serializes only the scheduled (Quartz) path; `TriggerPoller` runs the same singleton job
on demand, invisible to Quartz, so a manual "check now" and the cron run could execute together and
both read-modify-write the same device's findings at the same instant (one's raise racing the other's
clear). **Fixed:** a per-job-name gate in `JobDispatcher.RunAsync`, the one path both share.
**Verified:** build + the job/sweep tests green.

### M6 — a partial AD query could retire every live PC it omitted
`Service/Store/NetworkOpsStore.cs:36` (`SyncDirectoryAsync`)
The sync guarded against an *empty* AD result but not an *incomplete* one (a transient DC error, LDAP
paging truncation): it set `InDirectory = 0` for every managed PC not in the list, blacking out
monitoring for those machines silently until the next full sync. **Fixed:** a sanity floor — a drop to
under half the currently-managed count is treated as a failed query and the whole sync is refused
(rolls back; `JobDispatcher` logs + alerts the throw, as it already does for the empty case).
**Verified:** by code (the floor + the existing empty-list guard; the throw is surfaced by the
dispatcher, confirmed by the error-handling reviewer).

### M7 — AD sync keyed on Name only and could absorb a rack/manual device
`Service/Store/NetworkOpsStore.cs:42` (`SyncDirectoryAsync`)
The per-name upsert ignored `Source`, so a `Rack`/`Manual` row sharing a name with an AD workstation
was flipped to look like a domain PC and could never be cleared. **Fixed:** both the EXISTS and the
UPDATE are qualified `Source = 'AD'`, and the INSERT runs only when the name is free — a collision is
left untouched, not mislabeled, and does not throw. **Verified:** by code.

### M8 — `EsxiShell.Connect` leaked the SSH client on every non-host-key failure
`Transport/EsxiShell.cs:28`
`ssh` was disposed only in the host-key-mismatch branch; a timeout/auth/socket failure leaked the
client (socket + session threads) — ~288/day while a host like KOR-UNIFI01 stays unreachable on the
5-minute sweep. **Fixed:** a catch-all that disposes before rethrow. **Verified:** build (the agent-trust
reviewer confirmed host-key pinning still refuses when none is pinned).

### M9 — on a core port with only VMware MACs, a VM was drawn as the attached device
`Core/Network/NetworkMap.cs:196`
The core switch gives an unordered MAC set with no "switch's own last device" signal, so a port whose
MACs are all VMware OUIs fell to "lowest MAC", which can be a VM — drawing a VM as the hardware on a
map an engineer trusts. **Fixed:** a VMware-only core port shows "VMs (host not seen)" as the occupant
with the VMs listed behind it. **Verified:** build + the existing core-switch test (host, not a VM, on a
mixed port) still green.

---

## LOW (all fixed)

- **Enrolment rate-limit was global** (`Agents/AgentEnrolment.cs`) — one PC's wrong guesses blocked
  every PC's enrolment. Now per-PC. Also the wrong-name path removed-then-re-added the code, which
  could transiently fail a racing valid redeem; now peek-first, atomic claim only on a confirmed match.
  **Verified:** `AgentEnrolmentTests.One_PCs_wrong_guesses_do_not_block_another_PCs_valid_code`.
- **`MarkForDelete` return discarded** (`Transport/OnTargetChannel.cs`) — a failed delete left a
  `KorRun` service installed on a target, unlogged. Now surfaced via a host-wired `CleanupProblem` hook
  (Serilog warning).
- **`SqlException 208` swallowed unlogged** at 9 store sites — a table dropped/renamed *after* its
  migration read identically to "migration not run", forever. Now reported once per table via a
  `SchemaGap` hook.
- **`RackCollector` swallowed shutdown cancellation** (`:140`, `:152`) — a stop became a misleading
  "could not be read" note / the internet check kept going. Now a genuine stop propagates; a real
  timeout is still handled.
- **Move-detection dictionary was case-sensitive** (`Store/NetworkOpsStore.Network.cs:95`) while the DB
  matches MAC case-insensitively — a MAC differing only in case would lose its `NetworkMoves` history
  row. Now `OrdinalIgnoreCase`.
- **Text render joined with `" | "` without sanitizing names** (`Core/Network/NetworkMapText.cs`) — a
  DHCP/UniFi hostname carrying the delimiter or a newline broke the columns and the one-port-per-line
  layout. Now neutralized.
- **Box host shown "not connected now"** (`Core/Network/NetworkMap.cs`) when `stat/sta` and
  `stat/device` disagreed on the port. Now folded in.
- **Expired DHCP leases named devices** (`Core/Network/NetworkMap.cs`) — a stale hostname since
  reassigned to another device. Now dropped from naming. **Verified:**
  `NetworkMapTests.A_live_lease_names_a_device_but_an_expired_one_does_not`.
- **Live read had no freshness guard** (`Service/Network/NetworkMapService.cs`) — a stale live read
  could show "connected now". Now stamped and ignored past 15 min (mirroring the core's rule).
- **Update age compared local now to a UTC release date** (`Core/Updates/UpdateRules.cs`) — up to a day
  off every day. The caller now passes `DateTime.UtcNow`; the parameter is `nowUtc`.
- **One bad Lenovo descriptor nulled the whole type's BIOS catalog** (`Service/Bios/LenovoBiosCatalog.cs`)
  — suppressing `bios-behind` for every PC of that type. Now each descriptor is parsed on its own; a
  network failure still falls back to the last-good catalog.
- **`DrainAsync` took a one-shot snapshot** (`Transport/OnTargetChannel.cs`) — a launch in flight at
  shutdown could strand a `KorRun` service. Now loops until empty (the caller's timeout bounds it).
- **`ActionRunner` fire-and-forget fix could raise an unobserved exception** on shutdown
  (`Service/Sweep/ActionRunner.cs`). Now the shutdown cancel is swallowed cleanly (the fix is reopened
  next start, by design).
- **`SessionApi /run` left a "Running" row on abort** (`Api/SessionApi.cs`) — session-run rows aren't
  swept. Now completed as "aborted".
- **`DecideLearnedAsync` wrote two tables without a transaction** (`Store/NetworkOpsStore.Prompts.cs`)
  — a failure between them left the run accepted and its card still proposed. Now one transaction.

### App UI (all fixed; verified by build + the 64 app NetworkOps tests)

- **WebView2 environment poisoning / app crash** (`NetworkOps/KorRemoteViewerWindow.xaml.cs`) — a
  faulted `CreateAsync` (locked profile) was cached forever by `??=` and the catch covered only three
  exception types, so a non-listed failure escaped the async-void `Loaded` handler and crashed the app,
  and every later Connect threw the cached fault. Now the faulted environment is dropped so the next
  Connect rebuilds, and the catch covers everything but cancellation.
- **`Guard` filter too narrow** (`NetworkOps/NetworkOpsNetworkWindow.xaml.cs`,
  `NetworkOpsUpdatesWindow.xaml.cs`) — a `JsonException` from a changed DTO escaped the async-void
  handler and crashed the app. Now catches everything but cancellation.
- **Check and a fix could run together** (`NetworkOps/NetworkOpsDeviceViewModel.cs`) — two reloads
  clobbering each other's status. Centralized into one busy flag; both disable the other's buttons and
  guard their entry.
- **Fixed `Height` on multi-line themed tiles** (device component tiles, network port tiles + place
  rows, command-center summary tiles) — clips at higher text scaling (the CLAUDE.md rule). Now
  `MinHeight`.
- **Acknowledge/Snooze/Reopen/Add-note had no re-entrancy guard** — a double-click double-wrote. Now
  gated by the same busy flag.
- **Stale port card survived a map refresh** — the side panel kept showing a port from the old map.
  Now cleared on rebuild.

---

## Reported, NOT changed (design decisions — Ian's call)

- **MeshTrust accepts any publicly-trusted cert for MESH01** (`Transport/MeshTrust.cs`) — this is the
  intentional Let's Encrypt change (so the pin survives ~60-day renewals). It does broaden MITM surface
  for the remote-control channel to "any mis-issued cert for the name." If you want the renewal
  convenience *and* a tight pin, pin the issuer/public-key (SPKI) rather than the leaf. Flagged, not
  changed.
- **On-target scripts stage in `C:\Windows\Temp`** (`Transport/OnTargetChannel.cs`) — the network route
  (not the agent route) stages SYSTEM-run scripts there, relying on an unpredictable GUID id and
  default ACLs. Low exploitability; could be hardened to an admin-only dir later.
- **`disk-errors` is Critical on any lifetime uncorrected read count** (`Core/Learning/Predictions.cs`)
  — a drive with one old uncorrected error alarms Critical forever. This is defensible (uncorrected =
  real data loss, and it correctly caught KOR-208-N's 3904), so it's left as-is; if you'd rather it
  de-escalate, gate it on a *rise* like the correctable counters. Your call.

## What this audit covers, and does not

Covers: the agent/enrolment/transport trust boundary, service concurrency and shared state, the SQL
store and migrations' data-integrity invariants, the port-map builder, the workstation probes + SMBIOS
+ updates + power + BIOS, the API host + error handling + resource lifetime, and the WPF NetworkOps UI
— read adversarially, each finding verified against the code.

Does **not**: run against the live DB (the SQL findings are reasoned from the SQL text and the fixes
are guarded, not executed against production); exercise the GUI (the UI fixes are verified by build +
the headless app tests, not by rendering); or prove the deployed runtime beyond `/api/ping`. A
same-class fault it would miss: a defect present in a path no reviewer's scope included (e.g. the
Architecture/Visio side, out of scope here).

## Verification summary

- NetworkOps suite: **499 passed / 0 failed** (12 added for H1, H2, M2, M3, and the LOW enrolment and
  expired-lease fixes; the DI gate now scans all four API files).
- App NetworkOps tests: **64 passed / 0 failed**, in 3s (no headless hang).
- `Kor.Operations.NetworkOps.Service` and `Kor.Operations.App` both build clean.
- Shipped as service **0.27.0**.
