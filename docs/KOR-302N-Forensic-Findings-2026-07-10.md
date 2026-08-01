# KOR-302N — Forensic Findings & Security Incident Report

**Subject machine:** KOR-302N (192.168.1.91) — primary workstation of user `kor\mli` ("Michael Li"; profiles `mli` and `michael li.old`)
**Prepared:** 2026-07-10
**Prepared by:** Ian Lalonde, Operations / IT — KOR Structural
**Classification:** Confidential — Security Incident / potential HR & Legal matter
**Evidence store:** `C:\Escrow\FORENSICS-KOR-302N-2026-07-10\` (508 files, 639 MB, SHA-256 manifest + chain of custody)

---

## 0. Important framing — read first

This report documents **technical facts** observed on company-owned equipment, and separates them from **inference**. The evidence establishes, at a high level of confidence: (a) removal of company source code from the machine onto personal removable media; (b) an export of a company mailbox to a personal-format file; and (c) installation of unauthorized remote-access / mesh-VPN software tying the machine to a personal external account and an off-domain computer.

These are serious **data-governance, IP, and IT-security** issues on their own, and justify immediate protective action regardless of motive. This report does **not** establish motive, and it makes **no** finding about espionage or any actor's intent or affiliation. Characterizations based on national origin are not supported by evidence and should be kept out of the investigation; doing so also protects KOR legally. Recommended handling is through **management, HR, and legal counsel** (Section 10), who can decide on any escalation.

All collection was read-only over the `\\KOR-302N\c$` administrative share from an authorized IT workstation. Nothing on KOR-302N was modified. See `_CHAIN-OF-CUSTODY.txt`.

---

## 1. Executive summary

- KOR's Revit tooling (≈200 custom plugin DLLs) is **deployed and escrowed**, but the **source code is not on any company system**. The developer's working source folder on KOR-302N (`C:\My Apps\###_Business_LWJ`) was **emptied on 2026-07-08**, the same morning a **personal Kingston USB drive** was connected and Explorer was browsing his source projects on that USB (`D:\###_LWJ_Break_Thru\...`).
- The machine runs **Tailscale**, a mesh-VPN, joined to a tailnet associated with a **personal Google account, `elton.rheek@gmail.com`** — not a KOR account. Over that VPN, KOR-302N made **Remote Desktop connections to an off-domain computer at `100.87.155.69`** (the "test PC" that disappeared from KOR management tools).
- A custom-built tool, **`PSTExporterGUI`**, was used to **export the `mli@korstructural.com` mailbox to a 357 MB PST file** stored on the machine (`2026-06-29_...Emails.pst`).
- **TeamViewer** (a second remote-access product) is also installed.
- All recoverable artifacts have been **preserved with SHA-256 hashes**.

The immediate risks are **loss of company IP** (source code), **data exfiltration** (source + mailbox), and **standing unauthorized remote access** into the KOR environment via an external, personally-controlled network.

---

## 2. Timeline — morning of 2026-07-08 (all times local, machine KOR-302N, user `kor\mli`)

| Time (approx.) | Event | Evidence source |
|---|---|---|
| 06:25:06 | `C:\My Apps\###_Business_LWJ` (his working source folder) last modified — folder is now **empty** | Folder metadata; verified empty (child count 0), owner `kor\mli` |
| 06:25:37 | 675 MB `OneDrive_1_2026-07-08.zip` present in Downloads (contents = project **Revit/IFC models**, not source — see note) | File metadata; zip inspected |
| 06:37:48 | **Kingston DataTraveler 3.0 USB (28.8 GB)** connected | Partition/Diagnostic event 1006; setupapi.dev.log (serial `E0D55E6CBD0FF640995012F0`) |
| 06:38:40 | Explorer browsing: `C:\My Apps\###_Business_LWJ` **and** `D:\###_LWJ_Break_Thru\ColumnHeightUpdater\bin\Debug\net48`, `D:\###_LWJ_Break_Thru\ConcreteVolumeCalculation\bin\Debug\net48` (his **source projects, on the USB**) | Jump list `f01b4d95…` (mtime 06:38:40) |
| 06:42:27 | `D:\` (KINGSTON) and `D:\Guid or Uuid.txt` opened | Recent shortcuts (absolute `D:\` targets) |
| 06:53:38 | `C:\My Apps\2026_InBox` last modified — folder is now **empty** | Folder metadata; verified empty, owner `kor\mli` |
| 07:09:32 | USB disconnected | Partition/Diagnostic event (capacity 0 entry) |

**Interpretation.** The sequence — source folders emptied, personal USB connected, Explorer open to the *same-named source projects on that USB* — is strong evidence that his working source tree was **moved off KOR equipment onto personal removable media**. What was copied to the USB cannot be proven byte-for-byte from the machine alone (that requires the USB itself, or an offline `$MFT`/`$UsnJrnl` examination), but the folder-access record on D:\ is direct evidence that source-project directories existed on the USB during this session.

**Note on the OneDrive zip.** The 675 MB `OneDrive_1_2026-07-08.zip` extracted to project deliverables (`SUR-191_revit 2026_IFC.rvt`, `BLDG 2 IFC_DRAFT.pdf`). It appears to be an **inbound project download**, not source exfiltration. Flagged for completeness; not treated as exfil.

---

## 3. Findings

### F1 — Company source code removed from the machine (HIGH)
His working source lived under `C:\My Apps\###_Business_LWJ` ("LWJ" appears to be the developer's initials). That folder and `C:\My Apps\2026_InBox` are now **empty**, both stamped 2026-07-08, both owned by `kor\mli`. The Recycle Bin is empty (consistent with a *move*, not a delete). During the 07-08 USB session, Explorer was open to source-project build folders on the USB (`D:\###_LWJ_Break_Thru\...`). **Net effect: KOR has essentially no source code for its ≈200 custom Revit plugins on any company system.**

### F2 — Removable-media history (HIGH)
KOR-302N shows heavy use of **personal external drives** (from `setupapi.dev.log` and Partition/Diagnostic event 1006):

| Device | Capacity | Serial | Seen |
|---|---|---|---|
| Kingston DataTraveler 3.0 | 28.8 GB | `E0D55E6CBD0FF640995012F0` | **2026-07-08 06:37 → 07:09** |
| Kingston DataTraveler 3.0 | 115 GB | `EE03DA5152E9` | 2026-05-29, 06-10, 06-11 |
| Kingston DataTraveler 3.0 | 28.8 GB | `E0D55E6B6453E3A1295C0284` | 2026-05-24 |
| WD (X0C-00SJG0) external | 931 GB (1 TB) | — | 2026-06-29, 06-30, 07-03 (frequent) |
| Samsung PSSD T7 Shield | 1.86 TB (2 TB) | `D432104Y0SNLN6S` | 2026-05-26 |
| Patriot | 931 GB (1 TB) | `1000000000CA` | 2026-06-22 |

### F3 — What was accessed during the USB window (HIGH)
Explorer jump list written at 06:38:40 (inside the USB window) records navigation to, among others: `C:\My Apps\###_Business_LWJ`, `C:\ProgramData\2015_RevitCommands` (the plugin hub), `C:\Autodesk\RevitHealthCheck_Advanced\bin\Debug\net48`, `C:\Q\2026-05-20_Family_Test\PSTExporterProject`, and the D:\ source projects above. This ties the USB session directly to source-code and plugin locations.

### F4 — Mailbox exported to PST via a custom tool (HIGH)
`C:\Q\2026-05-20_Family_Test\PSTExporterProject\` is a C# project (`PSTExporterGUI.csproj`, `Form1.cs`, `Program.cs`) — a **purpose-built email-export utility**. A resulting file, **`C:\Q\2026-06-29_mli@korstructural.com_Emails.pst` (357 MB)**, is an export of his KOR mailbox. Both the tool source and the PST are preserved in evidence.

### F5 — Unauthorized mesh-VPN (Tailscale) on a personal account (CRITICAL)
`C:\ProgramData\Tailscale\` and `C:\Users\mli\AppData\Local\Tailscale\` show a running **Tailscale** installation (installed ~2026-05-24; logs through 2026-07-03). Tailscale creates a private encrypted network ("tailnet") that bridges machines across the internet, **bypassing the corporate firewall and domain controls**. The tailnet is tied to a **personal Google identity, `elton.rheek@gmail.com`** (evidenced by a Taildrop inbox folder `files\elton.rheek-gmail.com-uid-6339171258025277`). KOR-302N's own tailnet address was `100.72.43.71`.

### F6 — Off-domain "test" computer reachable only via the tailnet (CRITICAL)
His `Default.rdp` targets **`100.87.155.69`** — a Tailscale (100.64.0.0/10 CGNAT-range) address. The Tailscale service log confirms KOR-302N opened **RDP (TCP 3389) sessions to `100.87.155.69`** on 2026-07-03. This is consistent with the **test PC that was removed from the domain** and vanished from management tools: it is off-domain but remains reachable to him over the personal mesh. Its Tailscale hostname was not extractable from the copied state offline; it is obtainable from the Tailscale admin console for the `elton.rheek@gmail.com` tailnet, or by running `tailscale status` on KOR-302N live.

### F7 — Second remote-access product installed (MEDIUM)
**TeamViewer** is installed (`C:\Program Files (x86)\TeamViewer` and `mli\AppData\Local\TeamViewer`), providing an additional independent remote-access channel.

### F8 — Plugin & deployment landscape (context)
- Deployment is **per-workstation local copies** of `C:\ProgramData\2015_RevitCommands\` (≈200 DLLs incl. core libs `ML.dll`/`RvtLib2025.dll`, and 2027-targeted builds). No network-share deployment.
- The `\\KOR-FS01\Drafting` share is **content only** (families, templates, configs) — **no source code**.
- Only **5 stale template repos** (2024–2025, not under version control) remain on the machine.
- **Both machines' DLL sets are escrowed**; C# decompilation (ILSpy) can recover working source as a fallback.

---

## 4. Plain-language: what is Tailscale, and why it matters

**Tailscale** is a commercial "mesh VPN." Once installed and logged in to an account, a computer joins a private virtual network and can reach — and be reached by — any other machine on that same account's network, **from anywhere on the internet, encrypted, and without going through the company firewall or requiring domain membership**. It is legitimate software widely used by IT teams — but here it is running **on a personal account (`elton.rheek@gmail.com`), not a KOR-managed one**. The practical consequences:

- KOR-302N (and anything it can reach on the LAN) has been **bridged to an outside, personally-controlled network** for ~6 weeks.
- The **off-domain test PC** at `100.87.155.69` is reachable to that account holder even though KOR can no longer see or manage it.
- This is a **standing unauthorized access path into KOR's environment** that persists until Tailscale is removed and the machine is isolated.

## 5. `elton.rheek@gmail.com` — what is known and unknown

**Known (fact):** it is a personal Google/Gmail identity that owns or administers the Tailscale tailnet this machine is joined to; a Taildrop (Tailscale file-transfer) inbox on the machine is named for it. **Unknown (not established):** who the person is, and their relationship to the employee (it could be an alias/alternate account of the employee himself, or a separate third party). This should be treated as an **investigative lead**, not a conclusion. It can be pursued via: the Tailscale admin console for that tailnet; searching his mailbox/PST and browser artifacts for the address; and Google/Tailscale legal process if the matter escalates.

## 6. Live status at time of collection

The `kor\mli` session was **logged on** during collection (registry hives locked). A scan for files modified in the 30 minutes prior to 2026-07-10 00:18 found **only a background Autodesk log** — i.e., no active user file edits at that moment; the session appears **left logged on** rather than actively in use at midnight. Continuous live monitoring is not possible over the file share alone; if required, it needs the machine isolated and examined directly (or EDR tooling).

---

## 7. Risk assessment

| Risk | Severity | Basis |
|---|---|---|
| Loss of company IP (all custom Revit source) | Critical | F1, F3 — source removed to personal USB; none left on company systems |
| Data exfiltration (source + full mailbox) | Critical | F1, F4 — source on USB; 357 MB mailbox PST built with a custom tool |
| Standing unauthorized remote access | Critical | F5, F6, F7 — personal Tailscale mesh + off-domain PC + TeamViewer |
| Inability to rebuild/maintain plugins | High | F8 — only stale template repos remain (decompilation is the fallback) |
| Unknown external party (`elton.rheek@gmail.com`) with network access | High | F5 — personal account controls the tailnet |

---

## 8. Evidence preserved (`C:\Escrow\FORENSICS-KOR-302N-2026-07-10\`)

- `logs\` — Windows event logs (Partition/Diagnostic, Kernel-PnP, Ntfs, Security, System)
- `setupapi\setupapi.dev.log` — USB device install history
- `recent\` — all Recent LNK shortcuts + jump lists (source of the 07-08 timeline)
- `source-Q\` — PSTExporter tool source + the exported mailbox PST (357 MB)
- `tailscale\` — full Tailscale state and 6 weeks of service logs
- `My-Apps\` — residual `C:\My Apps` (the source folders were already empty)
- `_SHA256-MANIFEST.csv`, `_CHAIN-OF-CUSTODY.txt`
- Related: `C:\Escrow\KOR-302N-2026-07-09\` and `C:\Escrow\KOR-307-N-2026-07-09\` (deployed plugin DLLs).

**Still to collect (needs offline image or logged-off session):** registry hives (`NTUSER.DAT`, `UsrClass.dat`) for shellbags / RDP MRU / TypedPaths; the physical **Kingston USB** and other external drives; the **test PC** at `100.87.155.69`; live `tailscale status` output; browser history; mailbox audit logs (M365).

---

## 9. Recommended immediate actions

**Containment (today):**
1. **Isolate KOR-302N** from the network while preserving it powered-on if a live examination (memory, `tailscale status`, open handles) is wanted first; otherwise disconnect its network. Do **not** wipe or re-image.
2. **Disable the `kor\mli` account** and revoke M365/VPN/remote sessions; force-expire tokens.
3. **Remove/hard-block Tailscale and TeamViewer** at the firewall (block Tailscale coordination/DERP and TeamViewer endpoints) to kill the standing external access path; then uninstall on the machine during examination.
4. **Locate and secure the physical Kingston USB** (serial `E0D55E6CBD0FF640995012F0`) and the other external drives listed in F2.

**Investigation & preservation:**
5. Engage **management, HR, and legal counsel** now; given probable IP theft and unauthorized access, counsel may advise outside forensics and/or law enforcement.
6. Preserve **M365 mailbox + audit logs** for `mli@korstructural.com` (litigation hold); the PST export and any forwarding/rules should be reviewed.
7. Take a **full disk image** of KOR-302N and of the **test PC** (`100.87.155.69`) once accessed; collect the registry hives then.
8. Pull the **Tailscale admin console** for the `elton.rheek@gmail.com` tailnet to enumerate every node (identify the test PC and any other bridged machines), and identify the account owner.

**Recovery:**
9. Proceed with the **plugin continuity plan** independent of his cooperation: the DLLs are escrowed; begin **ILSpy decompilation** of the core libraries (`RvtLib2025.dll`, `ML.dll`) and key tools to rebuild a source baseline KOR controls.

---

## 10. Handling note

Treat this as an evidence-based **security and HR incident**. Keep findings factual and avoid conclusions about motive or identity that the evidence does not support. Route decisions through management, HR, and legal — they own any confrontation, disciplinary, or law-enforcement step. IT's role is containment, preservation, and recovery, all of which are underway.
