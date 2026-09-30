# KOR NetworkOps — design and build plan

**Status:** planned 2026-09-28; Phases 0 and 1 built and live-proven the same night (§5).
**Owner:** Ian. **Replaces:** Tenacious Networks' (T-Net) NinjaOne + ScreenConnect stack.
**Sources this plan is built from, not re-derived:** the 2026-08-04 Ninja-replacement session,
`memory/project_network_management_module_foundation_2026_09_08.md`, the 2026-09-26
Infrastructure Roadmap (`Desktop\IT-Ops\KOR-Infrastructure-Roadmap-2026-09-26.html`, §3–§6),
`docs/KOR-WinRM-GPO-Plan-2026-07-28.md`, and a live read of Ninja's policy on KOR-216.

---

## 1. What it is, in one paragraph

A monitoring-and-management platform KOR builds and owns, in C#, that watches **everything** —
workstations, servers, the SAN and NASes, both UPSes, the ESXi hosts, the firewall and the
switches — from one command center; finds the problems that actually hurt this office (not
"CPU > 90 %"); acts on them safely; and says out loud when anything goes quiet. It is built,
not bought: our own endpoint agent, our own server, our own command center, speaking open
protocols (SMB/SCM, SNMP, SSH, syslog, HTTPS APIs, the Windows APIs). The single exception is
live screen-sharing, where the 2026-08-04 decision stands: self-hosted, open-source
**MeshCentral** on APP01, launched from our own Connect button.

## 2. The bar it has to clear (measured, not assumed)

Ninja's entire monitoring policy on KOR-216, read from the agent on 2026-09-28, is five
conditions: CPU ≥ 90 % for 15 min, disk ≤ 5 % free, SMART degraded, Spooler stopped, WMI
stopped. Its patching is Windows Update, scheduled by Ninja (17:00 daily, reboot 18:00 only if
nobody is logged on). It showed KOR-207 "No health issues" while Revit couldn't install and
Bluebeam crashed daily.

None of these were caught by it — every one is a requirement here:

| Found by hand, 2026 | Signal NetworkOps reads |
|---|---|
| Office/Access-Engine DLL skew, 13 of 27 PCs | `mso20win32client.dll` versions, `opushutil` 0xc06d007f |
| Outlook search failing, duplicate stores | Outlook event 36 by full store path, `(N).nst` age |
| Revit/Bluebeam crash loops, version drift | App Error 1000 by process vs fleet baseline |
| Dying data SSD with 13 months of unbacked work (206-N) | disk/7, storahci/129, WHEA, PnP present=False, data outside OneDrive |
| 722 GPU hangs in 45 days (206-N) | LiveKernelEvent 0x141 |
| Fans roaring on idle machines (305, 206-N) | Lenovo BIOS cooling mode, thermal zone |
| Webroot popups from unsigned tools / residue | WRLog "Monitoring process", residue folders |
| 8-day silent backup failure; 173-day dead PSU; SAN PSU alerts into a void since 2023 | Veeam freshness, host/SAN/UPS SNMP — **silence is a finding** |
| PRT/token storm (306) | AAD/Operational 1097/1098 |

## 3. Decisions already made (do not reopen)

| Decision | When / where |
|---|---|
| Build monitoring, health, drift, actions ourselves; C#, not PowerShell | 08-04; 09-07 ("PS is prototype only") |
| Screen-sharing, not RDP (RDP evicts the user); MeshCentral self-hosted | 08-04 / 08-05 ("my Ninja ScreenConnect replacement") |
| No off-the-shelf product for the platform itself | 09-28 |
| Work runs **on the target**; nothing chatty over the VPN | 09-10, 09-28 (binding) |
| Worker service on APP01 on the FileSync pattern; own SQL schema; Graph mail alerts | 09-08 |
| Freshness, not fetch-success; silence is a finding | Opportunities doctrine; roadmap §4 |
| Dead-man's switch first — the watcher must not be the patient | 08-04; `tools/Watch-OpportunitiesHeartbeat.ps1` precedent |
| Graceful UPS shutdown chain is the first *control* feature | roadmap §3 |
| Agents deploy by GPO **Startup Script**, not Software Installation (never fires on this fleet) | 08-05 |
| Least privilege; JEA-style allow-list for actions; propose-first for infra changes | 09-08, 09-10 |
| RD Gateway stays for remote users; no relay in their path | 08-05 |

The three forks deferred on 09-09 are settled by the above plus today's scope:
**transport** = a transport interface, agentless SCM now and our pull agent next (WinRM+JEA
only where it already exists — servers); **focus** = monitoring + drift first, actions second;
**scope** = everything, starting with the rack and the workstations in parallel because the
workstation checks already exist as code.

## 4. Architecture

```
                       ┌──────────────────────── KOR-APP01 ────────────────────────┐
  Workstations ──HTTPS pull──▶ NetworkOps.Service (Worker)                           │
  (KOR agent)               │   • scheduling catalog → one dispatcher → SQL runs     │
                            │   • collectors: Agent | OnTarget(SCM) | SNMP | SSH |   │
  Workstations ◀─svcctl+c$──│     HTTPS-API | vSphere | syslog listener             │
  (agentless, today)        │   • rules engine → findings → dedup → Graph mail      │
                            │   • freshness + "went quiet" per device and per probe  │
  UPS / SAN / NAS ◀──SNMP───│   • actions: allow-listed, audited, propose→confirm    │
  Switches / FW  ◀─SNMP/SSH/syslog                                                   │
  ESXi / vCenter ◀──vSphere API / SSH                                                │
                            │   SQL: KorNetworkOps (schema NetworkOps.*)             │
                            └───────────────▲──────────────────────────────▲─────────┘
                                            │                              │
                      Command Center (Kor.Operations.App page)     External dead-man check
                      fleet grid · device window · Connect ▶ MeshCentral
```

### 4.1 Projects (house conventions: `Kor.Operations.<Area>[.X]`, tests as root siblings)

| Project | Kind | Purpose |
|---|---|---|
| `Kor.Operations.NetworkOps.Core` | `net8.0` library, **pure** | Device/observation/finding model, rules, drift, parsers (SMBIOS, event records), payload builder. No I/O → testable against captured fixtures. |
| `Kor.Operations.NetworkOps.Transport` | `net8.0-windows` | Channels: `OnTargetScmChannel` (Win32 SCM API, not sc.exe text-scraping), SMB reachability, later SNMP/SSH/HTTP/vSphere. |
| `Kor.Operations.NetworkOps.Cli` (`netops`) | console | Ian's hands-on tool and the Phase-1 deliverable: `census`, `run`, `probe`. |
| `Kor.Operations.NetworkOps.Service` | Worker | Phase 2: scheduling, SQL, alerts, freshness. FileSync blueprint. |
| `Kor.Operations.NetworkOps.Agent` | Worker, self-contained | Phase 4: the endpoint agent. |
| `Kor.Operations.NetworkOps.Tests` | xUnit | Pure tests; live tests gated and `Speed=Slow`. |

### 4.2 The endpoint agent (Phase 4) — why ours, and what "lightweight" means

- **Pull, zero inbound ports.** The agent polls APP01 over HTTPS with mutual TLS (per-device
  certificate issued at enrolment). Works for laptops on the VPN, dual-homed Perform boxes and
  anything that was "offline" at sweep time — today 8 of 38 machines are missed by every
  agentless run.
- **Collects on the box.** Every probe is local API calls (registry, event log, WMI, WUA COM),
  so the VPN only ever carries results. The same probe code runs agentless via
  `OnTargetScmChannel` until the agent is on a machine — one probe library, two transports.
- **Acts only from an allow-list.** Typed commands (restart service, reboot with warning, set
  add-in LoadBehavior, rebuild search index, uninstall product X…), each with a rollback
  record. Arbitrary scripts only when signed by KOR's code-signing key.
- **Budget:** < 1 % CPU averaged, < 60 MB RAM, idle between polls; self-updating from APP01.
- **Signed** with a KOR code-signing certificate — also the durable fix for the Webroot
  "undetermined software" class (unsigned KOR tools).

### 4.3 Remote screen

MeshCentral (Apache-2.0, release 1.2.6 on 2026-09-24) self-hosted on APP01; its agent is
deployed alongside ours. Our device window's **Connect** opens the MeshCentral session for that
node (stable per-device URL; node id stored on the device record). Shared console session, the
user stays logged in and sees a privacy bar. Must be live **before** T-Net's ScreenConnect is
removed — that's the cliff (24 of 25 machines lose remote support the same day).

### 4.4 Security model

- Service identity: gMSA on APP01 (proposed 09-08; until then `KOR\app-admin` as FileSync).
- Secrets: `KOR_NETWORKOPS_*` machine env vars (house rule), never in SQL knobs or the repo.
- The credential vault Ian asked for (logins, key contacts) is its own phase, encrypted at rest
  with a key the service holds, never plaintext — and it retires the plaintext rack password
  currently sitting in the 09-26 roadmap HTML (§5, "Rotate the reused password").
- Every action: who/what/when/before/after, in SQL, visible in the command center.
- Transport scope: APP01 + Ian's VPN /32 only, as the WinRM GPO plan specified.

## 5. Phases — each ends with something running and a measurement

| Phase | Deliverable | Done when |
|---|---|---|
| **0** ✅ 09-28 | `Invoke-KorOnTarget` in WorkstationOps; reachability tries every A record | 54/54 PS tests; live on 3 PCs incl. a dual-homed one; zero leftovers |
| **1** ✅ 09-28 | Core + Transport + `netops` CLI: channel census, run-on-target via the SCM API, hardware probe (SMBIOS port with its fixture) | 16/16 xUnit, incl. the payload run under real PowerShell 5.1 (break-tested). First census: **30 of 38 reachable, 30 of 30 run-on-target ready, WinRM open on 5, dual-homed 3**, 52 s. `hardware` on 3 PCs in 31 s, zero leftovers. Rules engine moves to Phase 2 with the store it writes to. |
| **2** | Service on APP01: nightly sweep → SQL → rules → Graph alerts; freshness; external dead-man check | Runs alongside Ninja for a **comparison month**; every finding it raises is logged against what Ninja showed |
| **3** ◐ 09-30 | The rack: UPS/SAN/NAS SNMP, ESXi/vCenter, Veeam freshness, firewall + switches; **graceful shutdown chain** | ✅ 11 rack devices watched every 5 min (§9), first sweep 11 of 11 and it caught a failing backup. ✅ Chain built, dry run proven end to end, rehearsed daily. ⬜ Chain proven by a pulled plug, then armed |
| **4** | KOR agent (signed), GPO startup-script deploy, allow-listed actions | Agent on all 38, < 1 % CPU; offline machines report when they reappear |
| **5** | Command Center page + MeshCentral + Connect | Ian runs a day of support from it without opening Ninja |
| **6** | Patching cutover: WUfB policy via GPO **first**, remove Ninja's `NoAutoUpdate=1`, verify a full patch cycle | A Patch Tuesday lands fleet-wide with Ninja disabled |
| **7** | Retire T-Net stack: Ninja, ScreenConnect (all three generations), TeamViewer where unused | Nothing phones home to tenacious.support |
| **8** | Vault + contacts | Rack passwords rotated into it; plaintext copies destroyed |

Phases 2 and 3 can overlap; 6 and 7 are gated on 4 and 5 being proven, never on a date.

## 6. What this plan covers, and what it does not

**Covers:** every device class in the roadmap §4 map; every problem class in §2 above;
remote screen via MeshCentral; patch scheduling handover.

**Does not cover (yet, deliberately):** antivirus/EDR (Webroot stays, managed in its console);
Microsoft 365/Entra monitoring beyond what the endpoint sees (sign-in risk, mailbox rules —
candidate for a later phase, reading Graph); backup *execution* (Veeam keeps doing it; we
watch its freshness); a web front end (the command center is a page in the WPF app first).

**A same-class fault it would not catch at Phase 2:** anything on a machine that is off during
the sweep and never reachable agentlessly — the reason Phase 4 exists.

## 8. The intelligence layer and the overnight build (planned 2026-09-28, for Ian's review)

Ian's vision, 2026-09-28: *"an app that tracks all issues with a PC so it knows the history, and
it also knows about issues on OTHER PCs — a learning, intelligent app."* Five capabilities:

1. **History** — every observation and every finding kept per PC (opened / worsened / cleared),
   plus a daily **inventory** snapshot (hardware, drivers, BIOS, Office build, key app versions),
   so a PC's timeline shows what it was *and* what went wrong.
2. **Knows about other PCs** — on every sweep, for every problem type, compare the affected
   machines with the unaffected across the inventory and surface the attribute that explains it
   (the GPU analysis done by hand on 09-28: driver 32.0.15.8142 → 4 of 4 hang, GeForce 0 of 5).
   Each finding shows "same problem on N other PCs".
3. **Change tracking** — what changed on a PC (driver, app, Windows build, BIOS) lined up against
   when findings started or cleared: "started after X", "cleared after Y".
4. **Learning what fixes things** — a knowledge base per problem type (explanation + known fixes,
   seeded from 2026's diagnoses). When a finding clears, what changed around it is recorded and
   fixes are ranked by what actually worked; the best one is suggested on the next PC.
5. **Baselines** — each PC against its own history (spikes) and against its peers (drift).

Overnight order (each step ends with a proof; milestone commits on develop):
1. Prove the DB + service: run-once census and health sweep against KorNetworkOps; start the
   service with alerts OFF; heartbeat fresh.
2. Dead-man watcher on FS01: `netops watchdog`, self-contained exe, scheduled task every 10 min,
   mails via Microsoft 365 direct send (no credentials stored on FS01) if the heartbeat is stale.
3. All-C# probe: the health + hardware probes as one C# program on .NET Framework 4.8 (built into
   every Windows 10/11 PC), run the same way. Switched over only after a differential run on the
   whole fleet shows identical snapshots from the PowerShell and C# probes.
4. The intelligence layer, capabilities 1–5 above, in Core (pure, tested) + the store.
5. The Command Center page in Kor.Operations.App (FileSync Command Center pattern; tile visible to
   Ian only): fleet grid, PC window (Ninja's layout plus history, "same on other PCs", likely
   cause, what fixed it before, "run health check now"), fleet insights. Connect → MeshCentral later.
6. Morning report.

Not overnight without Ian: alerts on (other than the dead-man); any change to a PC; GPO or
firewall changes; MeshCentral or agent installs; pushing or merging; releasing the Ops app.

## 7. Needs Ian (everything else proceeds)

1. **Code-signing certificate** for KOR (agent + the Revit suite) — a purchase.
2. **MeshCentral on APP01** — an infrastructure install (port, TLS name, firewall scope).
3. **T-Net timeline** — when the contract ends decides how early Phases 5–7 must land.
4. **SPF for the dead-man mail** — ✅ NOT NEEDED: the watcher's test mail from FS01 landed in Ian's
   inbox on 2026-09-29 (as the UPS cards' direct-send mail had). Adding the office IP stays good
   hygiene, not a requirement. Measured 2026-09-28 from KOR-FS01: port 25 to
   `korstructural-com.mail.protection.outlook.com` is open and answers, but the office's public
   address, 184.71.160.54, is not in `v=spf1 a include:spf.protection.outlook.com -all` (the `a` is
   the website host, 104.244.120.38). DMARC is `p=none`, so the mail is not rejected, but a hard SPF
   fail on our own domain will most likely land in Junk. Fix: add `ip4:184.71.160.54` to the SPF
   record — one DNS edit, which also covers any other device in the office that mails by direct send.
5. ✅ **Watcher INSTALLED on FS01 2026-09-29** (task "KOR NetworkOps Watchdog", SYSTEM, every 10 min;
   `C:\Program Files\KorOperations\NetworkOps\netops.exe`). The steps were: run `003_WatchdogLogin.sql`; set machine variable
   `KOR_NETWORKOPS_WATCHDB` on FS01 (networkops_watch); copy the single-file `netops.exe`
   (self-contained, no .NET needed); scheduled task as SYSTEM every 10 minutes running
   `netops watchdog`; then `netops watchdog --test` to prove the mail lands in the inbox.

**Entra (created 2026-09-29 on Ian's GO; verified by reading back):**

| Object | Value |
|---|---|
| Group "NetworkOps Admins" (cloud-only) | `d74eb7e8-a87e-4dcd-93c2-06a02ce63b59`, member: Ian |
| App "KOR NetworkOps API" | appId `1ba6790b-5f6b-4538-aad5-5d6949720385`, `api://1ba6790b-…/NetworkOps.Access`, role `NetworkOps.Admin`, v2 tokens |
| Its service principal | `daf32544-1888-4093-a590-79ee6b305078`, assignment REQUIRED, group → NetworkOps.Admin |
| Pre-authorised client | Kor.Operations.App `69b68cd2-a051-4782-a45e-4f1276942c06` (no consent prompt) |
| CA "NetworkOps API - require MFA" | `5fba24d8-a6d5-4eab-9d51-4a9690296efe`, enabled: this app only, all users, MFA always (no location exemption), sign-in every 12 h, `CA-BreakGlass-Exclude` excluded |

**Writing a probe: return PLAIN values.** A probe runs on the PC and its output is serialised with
`ConvertTo-Json -Depth 8`. PowerShell's rich objects (a string from `Get-Content`, a `FileInfo`, a
service object) carry hidden PSPath/PSDrive/PSProvider graphs that serialise too: on 2026-09-29 a probe
meant to return 1 KB published 105 MB, and a `Get-Item` result takes minutes of CPU on the PC just to
serialise. Build new strings (`"$(...)"`, `-join`), numbers and `[pscustomobject]`s of those. The payload
now refuses any result over 8 MB on the target (`OnTargetPayload.MaxResultChars`), and the client refuses
to read an oversized file -- but a probe that trips the limit has still burned the PC's CPU.

Known, not NetworkOps: `EngineeringTools.Tests` finishes all its tests and then its testhost never
exits (the same on a clean checkout of `54a8e97f`). Run it with `--blame-hang-timeout 90s` until found.

## 9. The rack, as built (2026-09-29/30)

Every piece of infrastructure is a device in `NetworkOps.Devices` (Source `Rack`, a Kind), read every 5 minutes by
`RackSweepJob` and written through the SAME facts / metrics / findings / digest path as a PC, so acknowledge,
snooze, notes, history and learned fixes work on the rack unchanged. Served at `/api/rack`, separate from
`/api/fleet` so an app that predates the rack never lists a host as a PC. Each device is read through its own
least-privilege channel; nothing NetworkOps holds can change a device.

| Device | Channel | Credential / pin |
|---|---|---|
| ESXi .10, .16 | SSH, then `Core/Rack/esxi-health.py` in hostd with a LOCAL TICKET | key `keys\esxi-root` (authorized_keys `from="192.168.1.32"`); host keys pinned |
| UC3200, NAS01, Synology02 | SNMPv3 walk, Synology MIBs | `networkops` SHA/AES |
| Veeam (BK01) | REST :9419, jobs + repositories state | local non-admin `KOR-BK01\netops-veeam`, Veeam role **BackupViewer only** (a job start is refused, 403); certificate SHA-256 pinned |
| UniFi (13 devices) | SSH as `netops@KOR-UNIFI01`, whose ONLY command prints the controller's status | key `keys\unifi-status`, forced command + `from=`; sudo allows that one script |
| Core switch (ES-16-XG) | SNMPv3 | `networkops` SHA/**DES** (all firmware 1.8.1 offers); the open `public` v2c community was removed |
| UPS ×2 | the in-process watcher (§ Power) | — |
| Internet line | from APP01: public IP must be the Shaw static, DNS, pings | — |

**What it covers:** hardware sensors (PSU, fans, temps), clock/NTP/PTP, datastore space and access, production VMs
running with Tools, maintenance mode; Synology system, power, fans, disks, RAID, volumes, DSM updates; Veeam
failed / warning / **stale** (the silent-failure class) jobs and repository space; UniFi devices that stop checking
in, firmware, open alarms; core-switch reboots and ports that drop; UPS battery, bypass, output, runtime; traffic
leaving by the wrong WAN, loss, DNS.

**What it does not cover:** the Netgate firewall box itself (CPU, states, its own gateways) -- that needs its admin
login or an API package on it; vCenter; the Windows servers' own OS health (the PC probe could run on them);
per-port switch error RATES (counters are recorded as metrics, not yet a rule). **A same-class fault it would not
catch:** a device that answers with stale data (a hung UniFi controller still serving its last database state).

**Found by building it (2026-09-29/30):** host .10's clock 35 min slow with PTP on (fixed: NTP, persisted); host .16
blind to its hardware, CIM off (fixed: 0 → 249 sensors); the core switch answering `public` to anyone (fixed);
Veeam `Kor-FS01` failing three times on stuck VSS writers on FS01 (the writers' services were restarted at 03:17 and
the stalled retry started moving; its result is the proof).
