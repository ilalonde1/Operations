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
| **3** | The rack: UPS/SAN/NAS SNMP, ESXi/vCenter, Veeam freshness, firewall + switches; **graceful shutdown chain** | Each §4 roadmap row flips "silent" → watched; shutdown chain proven in a window |
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

## 7. Needs Ian (everything else proceeds)

1. **Code-signing certificate** for KOR (agent + the Revit suite) — a purchase.
2. **MeshCentral on APP01** — an infrastructure install (port, TLS name, firewall scope).
3. **T-Net timeline** — when the contract ends decides how early Phases 5–7 must land.
