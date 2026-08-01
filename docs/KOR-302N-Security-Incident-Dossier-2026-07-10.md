# KOR-302N — Security Incident Dossier

**Subject:** `kor\mli` ("Michael Li"; profiles `mli`, `michael li.old`) — departing Revit/BIM lead
**Machine:** KOR-302N (192.168.1.91), KOR-owned workstation
**Prepared by:** Ian Lalonde, Operations/IT — KOR Structural
**Date:** 2026-07-10
**Classification:** Confidential — Security Incident / HR & Legal
**Evidence:** `C:\Escrow\FORENSICS-KOR-302N-2026-07-10\` — 1,273 files, 785 MB, SHA-256 manifest + chain of custody
**Supersedes/consolidates:** `KOR-302N-Workstation-Sweep-Findings-2026-07-09`, `KOR-302N-Forensic-Findings-2026-07-10`, `KOR-302N-Containment-Runbook-2026-07-10`, `KOR-Revit-Plugin-Continuity-Dossier-2026-07-09`
**Revised:** 2026-07-14 (Update below)

---

## 0. Update — 2026-07-14: access is cut; recovery underway

He gave notice on **2026-06-19** (effective 2026-07-31), stating he was "fully committed to helping with the handover… to make sure nothing is left undone." During the notice period, the workstation record shows: the source folders were emptied and those projects opened from a personal USB the same morning (**July 8**); 169 of ~195 tool DLLs had been run through an obfuscator (F12); an outside VPN tied to an unidentified account was active on the machine for about six weeks; his mailbox was exported (**June 29**); and both browsers' history was cleared (**night of July 9**). Each item is evidenced in the findings below. This dossier makes **no finding on motive** — that is for HR and legal.

**Access is now cut.** `kor\mli` is **disabled** in Active Directory (verified at the directory) and his **Microsoft 365 sign-in sessions have been revoked** — locked out of network and cloud (the directory syncs to the M365 tenant via KOR-DC01). On KOR-302N the unauthorized `Superuser` admin account (see F11) is **disabled and preserved**, Tailscale removed, RDP disabled, and the RMM agent restored. His **physical building access card has been disabled.** Network, cloud, and physical access are all now closed.

**Mailbox recovered, reviewed & being reconstituted (2026-07-26/27).** His deleted mail was found intact — **~78,646 items / 42.4 GB** preserved in the mailbox's Recoverable Items under a pre-existing organizational hold. It was placed under a dedicated Purview eDiscovery hold (case *MLi-Departure-2026*, hold *MLi-Hold*), exported to PST (~41 GB), and the account was **converted to a monitored shared mailbox** into which the recovered mail is being **imported** (2026-07-27) — restoring the full history for ongoing review. A 2026-06-29 357 MB PST snapshot is also held in escrow. (His live mailbox had been reduced to 226.9 MB; the ~42 GB difference is the recovered deleted content.) A metadata review of all 78,646 items was completed — see §5 for a material `elton.rheek@gmail.com` finding; it also surfaced an Outlook alert confirming he **"deleted a large number of files" from OneDrive**, and an **"Opportunity @ Krahn"** (competitor firm) thread.

**Recovery is underway.** A portion of his real, un-obfuscated source was recovered from shadow copies; every deployed plugin is preserved and organized; a full accounting is complete (195 files → **78 distinct tools → only ~22 actually used**); and a clean, KOR-owned, **version-agnostic replacement foundation (`KOR.RevitTools`) already builds for Revit 2024/2025/2026.** A drafter working session was held 2026-07-13 to scope the rebuild, and next steps are moving. **KOR does not depend on his cooperation to proceed.**

**Standing & partnership.** `kor\mli` is a member of KOR's **`Partners`** group. The findings above — removal of company source, obfuscation of company tools, and the outside VPN — bear on his standing as a partner. That is for the partners to decide, with advice; this write-up sets out the facts. (Financial detail is in the companion partnership note.)

---

## 1. Executive summary

KOR's sole Revit-tooling developer is departing. Investigation of his workstation established that **KOR's custom Revit source code is no longer on any company system** — his working source folders were emptied on 2026-07-08, minutes before/after a personal USB drive was connected and Explorer was open to those same source projects on that USB. In parallel, the machine shows a pattern of **unauthorized data handling and personal infrastructure**: a purpose-built tool used to **export his KOR mailbox to a 357 MB PST**, an **unauthorized personal Tailscale mesh-VPN** bridging the machine to an off-domain "test" PC, **TeamViewer**, and — the night of 2026-07-09 — **both browsers' history cleared**.

**What this means in plain terms:**
- **Unauthorized admin account + removed IT monitoring (F11):** a never-expiring local admin account (`Superuser`) on his machine only — active as recently as May 2026 and **confirmed not IT-created** — keeps working even after his normal account is disabled. The company RMM agent had also been taken off his machine (only his), removing IT's remote visibility. Both now handled (Superuser disabled/preserved, RMM restored).
- **Access risk:** the machine was also bridged to an outside, personally-controlled **VPN** tied to an unidentified account (`elton.rheek@gmail.com`) for ~6 weeks (last active 2026-07-03), plus TeamViewer.
- **IP loss:** the ~200 custom Revit plugins the drafting team depends on still run, but rebuilding/maintaining them without his cooperation is a real project — the source was removed from company systems. (Recovery is underway; see §0 Update and §7.)
- **Data handling / exfil indicators:** mailbox exported to PST; source moved to personal removable media; browser history wiped; data-archiving/compression tooling present.

None of this establishes motive, and this dossier makes **no** finding as to intent or any actor's affiliation. It is, however, more than enough to act on: **contain and preserve** (§6), **recover the plugins independently** (§7), and **loop in the partners and HR** to decide how it's raised with Michael and what comes next. Keep the matter strictly evidence-based; conclusions tied to national origin are unsupported and should be excluded (they also create risk for KOR).

All collection to date was **read-only over the SMB admin share**; nothing on KOR-302N was altered.

---

## 2. Scope, method & chain of custody

- **Collected:** 2026-07-09 evening → 2026-07-10 ~00:30, read-only over `\\KOR-302N\c$` from an authorized IT workstation. No software installed on KOR-302N; no files modified there.
- **Preserved with SHA-256 hashes** (`_SHA256-MANIFEST.csv`, `_CHAIN-OF-CUSTODY.txt`).
- **Constraint:** the `kor\mli` user session was **logged on throughout**, so files held open by live processes could **not** be copied: **registry hives** (NTUSER.DAT, UsrClass.dat), **Amcache.hve**, and **SRUM** (SRUDB.dat). These require an **offline disk image** and are the top items for the next collection step.
- Related escrows: `C:\Escrow\KOR-302N-2026-07-09\` and `C:\Escrow\KOR-307-N-2026-07-09\` (the deployed plugin DLLs from his machine and from a power-user's machine).

---

## 3. Consolidated timeline — 2026-07-08 (KOR-302N, user `kor\mli`, local time)

| Time | Event | Source |
|---|---|---|
| 06:25:06 | Working-source folder `C:\My Apps\###_Business_LWJ` last modified — now **empty** | folder metadata (verified empty; owner `kor\mli`) |
| 06:25:37 | 675 MB `OneDrive_1_2026-07-08.zip` in Downloads (contents = project Revit/IFC models — **inbound**, not source) | file + zip inspection |
| 06:37:48 | **Kingston DataTraveler 3.0 (28.8 GB)** connected — serial `E0D55E6CBD0FF640995012F0` | setupapi.dev.log; Partition/Diagnostic 1006 |
| 06:38:40 | Explorer open to `C:\My Apps\###_Business_LWJ`, `C:\ProgramData\2015_RevitCommands`, and **`D:\###_LWJ_Break_Thru\...\bin\Debug\net48`** (source projects on the USB) | jump list `f01b4d95` |
| 06:42:27 | `D:\` (KINGSTON) and `D:\Guid or Uuid.txt` opened; Edge Shortcuts touched | Recent shortcuts; Edge profile |
| 06:53:38 | `C:\My Apps\2026_InBox` last modified — now **empty** | folder metadata |
| 07:09:32 | USB disconnected | Partition/Diagnostic |
| **07-09 23:39** | **Edge browsing History last written and found empty (cleared)** | Edge `History` (0 URLs) + 0-byte journal |

Broader: personal **Tailscale** installed ~2026-05-24, active through **2026-07-03**; frequent use of large external drives (1 TB WD, 2 TB Samsung T7, Patriot, 128 GB Kingston) across May–July; a **357 MB mailbox PST** created 2026-06-29.

---

## 4. Findings

**F11 — Unauthorized persistent admin account, plus removed IT monitoring — unique to his machine (Critical).** A never-expiring local Administrator account, `Superuser`, is present on KOR-302N — password set 2025-08-28, **last logon 2026-05-16**, set never to expire, not IT-sanctioned, and present on **this machine only** (fleet sweep 2026-07-10). Being local and non-expiring, it **keeps working after his domain account is disabled**.

*Origin — resolved.* An earlier draft left this open in case IT had created it at build time. IT (the Ops lead) confirms it was **not created or used by IT**, and its **logon on 2026-05-16 shows the credential was authenticated ~9 months after it was created** — ruling out a dormant build-time account. On his workstation, with the IT origin excluded, it is an **unauthorized persistent-admin credential attributable to him**.

*Forensic note.* No user profile exists for the account (`C:\Users\Superuser` is absent), so its logon was **non-interactive** (network/service/credential use), not a desktop session — consistent with a standing credential rather than casual login. The retained Security event log covers only **2026-06-04→07-10**, so the account's creation event and logon sources are not in it; a **full disk image** would recover the SAM (creation date, RID) and archived logs.

Alongside it, the **NinjaOne RMM agent had been removed from KOR-302N** (uninstalled 2026-05-19) while every other machine still runs it — removing IT's remote visibility of the endpoint. With the personal Tailscale VPN (F4), that is a standing outside path *in*, an unauthorized privileged *local* login, and no monitoring to see either.

*Environmental, not his:* `NT AUTHORITY\INTERACTIVE` in the local Administrators group and **TeamViewer** appear on roughly half the fleet, including spare machines — a pre-existing image baseline, not his doing.

*Status:* `Superuser` disabled and preserved, RMM restored, Tailscale removed, RDP disabled, `mli` disabled as of 2026-07-14. Still to do: reset the built-in Administrator password and check other machines he set up.

**F1 — Company source code removed from company systems (Critical).** Working-source folders `###_Business_LWJ` and `2026_InBox` emptied 2026-07-08 (owner `kor\mli`, Recycle Bin empty → a *move*), with Explorer simultaneously open to the same source projects on the personal USB (`D:\###_LWJ_Break_Thru\{ColumnHeightUpdater,ConcreteVolumeCalculation}\bin\Debug\net48`). Only 5 stale, un-versioned template repos remain on the machine. The `\\KOR-FS01\Drafting` share holds **content only** (families/templates/configs), no source.

**F2 — Removable-media history (High).** Multiple personal external drives used; the 2026-07-08 device was a **Kingston DataTraveler 3.0, 28.8 GB, serial `E0D55E6CBD0FF640995012F0`**, connected 06:37→07:09. Others: WD 1 TB (`X0C-00SJG0`), Samsung T7 Shield 2 TB (`D432104Y0SNLN6S`), Patriot 1 TB (`1000000000CA`), Kingston 128 GB (`EE03DA5152E9`).

**F3 — Custom mailbox-export tool + PST (High).** `C:\Q\...\PSTExporterProject\` (`PSTExporterGUI.csproj`, `Form1.cs`, `Program.cs`) is a purpose-built email exporter; `C:\Q\2026-06-29_mli@korstructural.com_Emails.pst` (357 MB) is a resulting mailbox export. His PowerShell history also contains **Outlook COM mailbox-archiving scripts** (bulk-moving mail into `Archive_YYYY` folders). Both tool source and PST preserved.

**F4 — Unauthorized personal mesh-VPN, Tailscale (Critical).** Installed ~05-24, active through 07-03, on a **personal Google account `elton.rheek@gmail.com`** (tailnet `tailbc55d0.ts.net`; KOR-302N = `100.72.43.71`). Tailscale creates an encrypted network that bypasses the corporate firewall and domain. The tailnet has **only two nodes**: KOR-302N and an off-domain "test" PC at `100.87.155.69` (which egresses via KOR's own Shaw line `184.71.160.54` — i.e., on KOR premises but removed from the domain and invisible to management tools). KOR-302N made **RDP (3389) sessions to the test PC**. `RouteAll=true`/`CorpDNS=true` on the profile.

**F5 — Second remote-access product, TeamViewer (Medium).** Installed under Program Files (x86) and per-user; an independent external access channel. `MSTSC.EXE` (RDP) and `TAILSCALE` also confirmed run via Prefetch.

**F6 — Browser history cleared on both browsers (High).** Chrome and Edge `History` databases are valid SQLite but contain **zero URLs**, while their **Login Data** stores remain populated — a pattern consistent with the history having been **cleared** rather than never recorded (Edge `History` last written 07-09 23:39). Clearing removes the record of web uploads/webmail/cloud activity.

**F7 — Code-signature removal (Medium).** PowerShell history shows `signtool remove /s` and `Remove-AuthenticodeSignature` removing the Authenticode signature from a Graitec Advance PowerPack `Test.dll` (a commercial plugin).

**F8 — Personal cloud + external sharing (Medium).** A **personal OneDrive** profile is configured on the machine (in addition to business). Saved browser logins include several external **ShareFile** tenants (`bha.sharefile.com`, `taylorkurtz.sharefile.com`, `tkad.sharefile.com`) and a non-KOR **home router** (`192.168.0.1`), alongside expected work services (Autodesk, Bluebeam, Procore, Deltek `kor.`/`bmzse.deltekfirst.com`, EGBC, GitHub).

**F9 — Programs of interest run (context).** Prefetch confirms **WinRAR** (archiver — data staging/compression), **DEVENV** (Visual Studio — he builds on this machine), **MSTSC** (RDP), **Tailscale**, PowerShell, and Defender (`MPCMDRUN`). No rclone/7-Zip/Mega/SCP seen.

**F10 — Plugin & deployment landscape (context/recovery).** ~200 custom DLLs deploy as **per-workstation local copies** of `C:\ProgramData\2015_RevitCommands\` (core libs `ML.dll`, `RvtLib2025.dll`; builds targeting Revit 2020–2027). No network-share deployment. **Both machines' full DLL sets are escrowed**, so ILSpy/dotPeek can recover a source baseline (§7).

**F12 — Company tools obfuscated with Obfuscar (Critical).** Of the ~195 deployed plugin DLLs, **169 were run through the code obfuscator `Obfuscar`**; the obfuscator's configuration/artifacts are present on the machine. Obfuscation scrambles compiled code (names, control flow, metadata) so it **cannot be decompiled back into usable source.** These are KOR's own tools, built for KOR's use; with the plain source also removed (F1), the obfuscated binaries and the physical USB are the only routes to their source. **Effect on KOR:** the obfuscated tools can be run but not read or edited — they must be rebuilt from observed behavior. The ~26 non-obfuscated DLLs (including the core `RvtLib2025`) were decompiled to a clean baseline (§7). Interpretation of intent is for HR and legal.

---

## 5. What is established vs. what is not — and the `elton.rheek@gmail.com` lead

**Established (fact):** the timeline, device serials, the emptied source folders, the mailbox PST + exporter tool, the Tailscale install/account/peer and the RDP sessions, TeamViewer, the cleared browser histories, the signtool actions, the Obfuscar obfuscation of 169 DLLs, and the unauthorized `Superuser` admin account (logon 2026-05-16, confirmed not IT-created). These are drawn from logs, file metadata, and preserved artifacts.

**Not established (inference / open):** exactly which files were written to the USB (provable only from the USB itself or an offline `$MFT`/`$UsnJrnl`); motive; and the exact real-world person behind `elton.rheek@gmail.com` (now linked to Michael's own correspondence — see the update below).

**Identity research on `elton.rheek@gmail.com`** (public sources + KOR-licensed enrichment): decoded from the machine — display "Elton Rheek", Google user-ID `6339171258025277`, tailnet `tailbc55d0.ts.net`. Web search: no attributable results. **Apollo**: returns the name but an otherwise **empty professional record**. **Hunter**: **valid/deliverable** Gmail, **0 public web sources**. Assessment (as at 2026-07-10): a real Google identity with **essentially no public footprint — likely pseudonymous**; a Gmail alias is trivial to create. **A lead, not a conclusion.**

**Update (2026-07-27) — his own recovered mailbox links him to the address.** The reconstituted mailbox contains **2019-09-15 correspondence from `Michael Li` → `ELTON.RHEEK@GMAIL.COM`** (personal — regarding an Amazon purchase). The address is therefore **within his personal orbit and connected to him since at least 2019**, superseding the earlier "does not appear to match Michael Li." Whether it is his own alias or a close associate's remains open — the person behind it still needs legal process to Google (the user-ID) and Tailscale (the tailnet) — but it is **no longer an unconnected third party.**

---

## 6. Recommended immediate actions (containment & preservation)

Sequence matters — preserve before you disable. Full detail in the companion **Containment Runbook**.

1. **Preserve mailbox first:** litigation hold on `mli@korstructural.com`; export mailbox audit, message trace (90 d), and forwarding/inbox rules **before** touching the account.
2. **Cut the standing access:** firewall-block Tailscale (`*.tailscale.com`, UDP 41641) and TeamViewer; then, during examination, uninstall both. If you want the live tailnet roster, run `tailscale status --json` on the machine first (it must be run **at** KOR-302N — remote execution is firewalled).
3. **Disable & rotate:** disable `kor\mli` (don't delete), revoke M365 sessions/tokens, rotate secrets he could read (incl. `KOR_APOLLO_APIKEY`, `KOR_HUNTER_APIKEY`, service accounts).
4. **Seize/image:** the Kingston USB (`E0D55E6CBD0FF640995012F0`) and other drives; the **off-domain test PC** (`100.87.155.69`); and a **full disk image of KOR-302N** — then collect the live-locked hives/Amcache/SRUM.
5. **Bring in the partners & HR:** and get outside advice if they want it before any next steps.

## 7. Recovery (independent of the employee)

The plugins keep running in production (self-contained local DLLs) — no drafting outage. Rebuild the source baseline KOR owns: **ILSpy/dotPeek decompile** `RvtLib2025.dll` and `ML.dll` (core libs) and the top tools from escrow, commit to a **KOR-controlled Git repo**, and adopt his documented per-Revit-year build pattern for 2020–2027.

## 8. Going forward — controls to detect/prevent a repeat

For this individual, the right move is **contain, not observe** — don't leave a suspected actor active to watch him unless law enforcement directs it. For the **environment** (and any successor), stand up:

- **Centralized logging (SIEM + Windows Event Forwarding)** so local logs can't be wiped; deploy **Sysmon** for process/network/file telemetry; enable audit policy via GPO (**process-creation 4688 with command line**, logon events, **removable-storage auditing**, PnP).
- **PowerShell script-block + transcription logging** (GPO).
- **EDR** (Defender for Endpoint P2 or equivalent) — behavioral detection + one-click remote isolation.
- **Egress control:** block/alert on unsanctioned tunnels/remote tools (Tailscale, TeamViewer, AnyDesk, ngrok); DNS filtering; egress logging.
- **Removable-media control:** GPO to block or read-only USB mass storage for at-risk roles; USB auditing; consider DLP.
- **M365:** verify mailbox auditing on; alert policies for large exports / eDiscovery / PST; restrict external auto-forwarding; **block Outlook PST export** (`DisablePST`); DLP for source/sensitive data; Conditional Access.
- **IP governance:** mandatory company Git (Azure DevOps / GitHub org) — **no local-only source**; backups; restrict local admin and use app allow-listing.
- **Process:** a documented joiner/mover/leaver offboarding checklist (account disable, device return, USB/cloud audit).
- **Policy/legal:** base all monitoring on a clear, uniformly applied Acceptable-Use/Monitoring policy on company-owned devices; in BC, keep employee monitoring reasonable and proportionate (PIPA); route targeted monitoring through HR/legal.

## 9. Evidence inventory (`C:\Escrow\FORENSICS-KOR-302N-2026-07-10\`, 785 MB, hashed)

`logs` (event logs) · `setupapi` (USB installs) · `recent` (shortcuts + jump lists) · `exec` (PowerShell history, 397 Prefetch) · `tasks` (scheduled tasks) · `browser` (Chrome/Edge History+Login Data) · `onedrive` (settings incl. Personal) · `timeline` (ActivitiesCache) · `outlook` (OST+NST, 127 MB) · `srum` (partial — SRUDB.dat locked) · `startup` · `source-Q` (PSTExporter source + 357 MB mailbox PST) · `tailscale` (state + 6 weeks logs) · `identity-research-elton.rheek.txt`.
**Still to collect (offline image):** registry hives (shellbags / RDP MRU / TypedPaths / USB reg / Run keys), Amcache.hve, SRUDB.dat, the USB and the test PC.

## 10. Handling note

This is a security and people matter. IT has handled containment, preservation, and recovery (underway); how it's raised with Michael and what happens next is for the partners and HR. Keep findings factual; don't record conclusions about motive or identity the evidence doesn't support.
