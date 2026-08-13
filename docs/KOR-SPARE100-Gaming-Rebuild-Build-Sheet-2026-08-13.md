# KOR — KOR-SPARE100 Gaming Rebuild — Build Sheet

**Purpose:** retire KOR-SPARE100 from the fleet and rebuild it as a personal gaming PC. It is the only box on the network that was **already an enthusiast build** — ROG gaming board, unlocked K-series CPU, aftermarket RAM, DIY tower with a standard ATX PSU — so nothing about the chassis, board or power delivery is OEM-locked.
**Prepared:** 2026-08-13 · Hardware collected remotely via `tools/WorkstationOps` (SMBIOS blob over RemoteRegistry). One-page build reference.

> **Full scrub — no data harvest.** Directed 2026-08-13: everything on both drives is discarded. Do **not** spend time triaging the 625 GB currently on C: and D:.

---

## 1. Why this box (three candidates compared)

| | **KOR-SPARE100** ✅ | KOR-SPARE8 | KOR-EDMONTON-01 |
|---|---|---|---|
| Board | ASUS **ROG STRIX Z270H GAMING** | ASUS Z87-PLUS | Lenovo ThinkStation P340 (OEM) |
| CPU | i7-7700K 4C/8T @4.2, **unlocked** | i7-4790K 4C/8T @4.0 | i7-10700 8C/16T |
| RAM | 32 GB DDR4-**3000** (4×8 Corsair) | 32 GB DDR3-1866 | 32 GB DDR4 (8+16+8, 1 slot free) |
| GPU | GTX 970 | GTX 960 | **none** — Intel UHD 630 |
| Storage | WD SN550 500 GB NVMe + Crucial MX300 1 TB | Samsung 850 EVO 500 GB + SanDisk 240 GB | Samsung PM981a 512 GB |
| Chassis / PSU | **DIY tower, standard ATX** | DIY tower, standard ATX | OEM Lenovo tower |
| Status | idle since Dec 2025 | idle | **in service** — do not touch |

**KOR-EDMONTON-01 is excluded** even though it has the best CPU: it is a live machine (Windows 11 25H2, current, Visual Studio installed, profiles through May 2026), it has no discrete GPU, and an OEM Lenovo tower is the worst possible case for a GPU swap. Note it was formerly **KOR-SPARE9** — `inventory.csv` still carries the old name.

**KOR-SPARE8 is the fallback / parts donor.** A full generation behind on every axis. Its Samsung 850 EVO 500 GB is worth taking as a game-library drive; its DDR3 will not fit a Z270 board.

---

## 2. Raiding the fleet — findings

Swept the retired / past-EOS boxes. **Do not raid.** Detail:

| Box | GPU | RAM | Verdict |
|---|---|---|---|
| KOR-224 | **GTX 1060 6 GB** | 2×16 GB DDR4-**2133**, 2 slots free | only card better than the 970 |
| KOR-213 | Quadro P1000 | 4×8 DDR3 | worse than the 970 for games |
| KOR-SPARE2 | Quadro P620 | 1×16 SODIMM | Lenovo SFF, worse again |
| KOR-SPARE8 | GTX 960 | 4×8 DDR3 | downgrade |
| KOR-100, KOR-104 | unknown | unknown | **powered off — needs eyes-on** |

- **RAM: no.** The only DDR4 that physically fits is KOR-224's G.Skill at 2133 MT/s — *slower* than the 3000 already installed. Mixing yields 48 GB at 2133: real speed traded for capacity no game uses. SPARE100 is already full at 4/4 slots.
- **GPU: marginal.** GTX 970 → GTX 1060 6 GB is roughly 10–15% plus 2.5 GB more usable VRAM. Both sit on NVIDIA's legacy driver branch now, so neither has a future. Take it only if the goal is zero spend.
- **The real answer is one modern card.** A single current-gen GPU beats every card on this network combined.

**Shelf-spare fit rule for this board:** DDR4 **UDIMM, non-ECC**, 4 slots, **64 GB max, 16 GB per stick**. DDR3 and SODIMMs will not fit. Only worth swapping in 2×16 or 4×16 rated **3000+**.

---

## 3. Pre-wipe — licence & seat release

Verified remotely on 2026-08-13. **Almost nothing needs machine-side deactivation.**

| Software | Licensing | Action |
|---|---|---|
| **ETABS 19 / 21** (CSI) | Cloud sign-in | **None.** CSI is cloud sign-in now; standalone-key deactivation no longer applies. |
| **Revit 2020/21, AutoCAD 2021/22** | **Named-user** — no `CLM\LGS` path, `AdskLicensingService` + Identity Manager present | **None.** Seats are per-person in the Autodesk portal. |
| **Microsoft 365 Apps** | `ProductReleaseIds = O365ProPlusRetail` — named user | **None.** Remove device only if the user is at their device cap. |
| **Adobe Acrobat** | No serial in registry → named-user | **None.** |
| **Bluebeam Revu 21** | ⚠ **Unresolved.** A serial-shaped value `SWAO-1221-0007-2307-5946-1774` exists under `HKLM\...\Bluebeam Software` as `DebugSerialz` — name suggests diagnostic, not the live licence. Revu 21 supports **both** Bluebeam ID subscription and serial-based seats. | **Verify in-app before wiping:** Revu → **Help → Manage License**. If it shows a serial rather than a signed-in Bluebeam ID, **deactivate there**. This is the only item that may strand a seat. |
| **SAFI** | Product Key `XPITG-N3NYX-5A3KW-AI650-K9PJS` present, but only under the free *Concrete Calculator* / *Foundation Calculator* utilities — not GSE | Check the GSE seat separately if GSE is licensed per-machine. |
| WoodWorks 2019/2023, StructurePoint | No HKLM licence keys found (per-user or file-based) | Check in-app if these are paid seats. |

**Console-side removals (web consoles — not on the machine):**
- **ScreenConnect** — two agents installed (`5058618ac801adce`, `f626167a716ce0b9`). Remove both from the console.
- **Webroot** — release the seat. This is a recurring per-seat cost.
- **TeamViewer** — remove the device from the account.
- **Dropbox** — unlink the device.
- **AD** — delete the `KOR-SPARE100` computer object after unjoining.

> ⚠ **Entrust Entelligence Security Provider** is installed. That is a certificate client tied to a user's digital ID (engineering seal workflow). Confirm the credential exists elsewhere before scrubbing — it is the one item on this machine that a full wipe could make irrecoverable.

---

## 4. BIOS configuration — ASUS ROG STRIX Z270H GAMING

**As found:** BIOS **1009** (2017-07-23) · Secure Boot **disabled** (`UEFISecureBootEnabled = 0x0`) · **Legacy/MBR install** (`C:\boot` present, no ESP) · XMP **active** at 3000 MT/s.

> **Sequence matters.** Do not disable CSM until the UEFI installer USB exists. The moment CSM is off, the current MBR install stops booting.

**0 · Flash the BIOS first.** 1009 is from 2017; newer builds carry microcode and firmware fixes, and early Z270 BIOS can be unreliable exposing PTT. Download the latest from the ASUS **STRIX Z270H GAMING** support page — *the current version number is unverified here, read it off the ASUS page.* FAT32 USB → run the bundled **BIOSRenamer** utility → **Tool → ASUS EZ Flash 3**. Flashing resets everything below, so flash before configuring.

**1 · Enter BIOS** — `Del` at post, then **F7** for Advanced Mode.

**2 · TPM 2.0** — `Advanced → PCH-FW Configuration → TPM Device Selection → **PTT**`
Intel Platform Trust Technology is the firmware TPM. Z270 supports it; no physical TPM module needed on the header.

**3 · Boot mode** — `Boot → CSM → **Launch CSM = Disabled**`
This is the gate — Secure Boot options stay greyed out while CSM is enabled.

**4 · Secure Boot** — `Boot → Secure Boot → OS Type = **Windows UEFI mode**`, then `Key Management → **Install Default Secure Boot Keys**`
Secure Boot will not report *Enabled* until the default PK/KEK/db keys are provisioned.

**5 · XMP** — `Ai Tweaker → Ai Overclock Tuner → **XMP → Profile 1**`
Currently on; the Corsair kit is running its rated 3000 MT/s. **A BIOS flash drops it to 2133 — re-enable it afterward.** Free performance, easy to lose.

**6 · Optional free performance** — the 7700K is unlocked on a Z270 board. A mild all-core OC to ~4.7–4.8 GHz costs nothing and is the best available uplift on this platform. LGA1151 Kaby Lake is a dead-end socket; do not spend money on the CPU.

---

## 5. Windows 11 install

**The CPU is the one blocker BIOS cannot fix.** The i7-7700K (Kaby Lake, 7th gen) is not on Microsoft's Windows 11 supported-CPU list. Pick one bypass:

- **Rufus** (simplest) — when writing the USB, tick *"Remove requirement for 4GB+ RAM, Secure Boot and TPM 2.0"*.
- **At Setup** — on the *"This PC can't run Windows 11"* screen press `Shift+F10` and run:
  `reg add HKLM\SYSTEM\Setup\LabConfig /v BypassCPUCheck /t REG_DWORD /d 1 /f`
  then close, go back, and retry.

With PTT and Secure Boot configured per §4, **`BypassCPUCheck` is the only bypass actually required.**

**Disks:** delete **every** partition on both drives in Setup. Booted from a UEFI USB, Setup writes GPT automatically — no `MBR2GPT` needed on a wipe.

**Caveat, stated plainly:** Microsoft's position is that unsupported-CPU installs may not be entitled to updates. In practice they have continued to receive them. Acceptable risk for a hand-me-down gaming box, but it is a real caveat rather than a myth.

**Verify after install:** `msinfo32` → *BIOS Mode: UEFI*, *Secure Boot State: On*. `tpm.msc` → TPM **2.0** ready.

---

## 6. GPU purchase — size it to the PSU

⚠ **PSU wattage is unknown and cannot be read remotely.** SMBIOS carries no PSU data. **Eyes-on required:** open the side panel, read the wattage on the label, and count PCIe 6+2 connectors. That is the gate on everything below.

| PSU found | Card |
|---|---|
| **550 W+**, 2× PCIe 8-pin | RTX 5060 Ti 16 GB |
| **450–550 W**, 1× 8-pin | RTX 5060 (~145 W), or used RTX 3060 12 GB |
| **< 450 W** or no PCIe leads | Budget ~$120 for a 650 W unit as well |

**Do not go above ~5070 class.** A 4-core/8-thread 7700K will bottleneck anything faster, especially at 1440p. The money is better left unspent.

**Everything else is already done:** 32 GB DDR4-3000, NVMe boot drive, 1 TB secondary SSD, ATX tower. No other purchase is warranted.

---

## 7. Order of operations

1. Verify the **Bluebeam** licence type in-app; deactivate if serial-based (§3).
2. Confirm the **Entrust** credential exists elsewhere.
3. Release console-side seats: ScreenConnect ×2, Webroot, TeamViewer, Dropbox.
4. Unjoin the domain; delete the AD computer object.
5. Open the case — **read the PSU label**; buy the GPU to match (§6).
6. Flash BIOS → configure §4 → re-enable XMP.
7. Wipe both drives in Setup; clean Windows 11 install with `BypassCPUCheck`.
8. Verify UEFI + Secure Boot + TPM 2.0 (§5).

---

## Appendix — collection method

Hardware was read remotely with **WinRM and RPC dynamic ports blocked fleet-wide** (`Get-CimInstance -ComputerName` fails on every workstation). Channel used: `tools/WorkstationOps/Kor.WorkstationOps.psm1` → `Use-KorRemoteRegistry` wrapped around `reg.exe \\host`, parsing the raw SMBIOS blob at `HKLM\SYSTEM\CurrentControlSet\Services\mssmbios\Data\SMBiosData` for per-DIMM detail, chassis form factor and core/thread counts.

Two gotchas worth recording:
- **Ping is the wrong reachability gate.** KOR-EDMONTON-01 does not answer ICMP but serves SMB fine. Gate on port **445**.
- **KOR-EDMONTON-01's two DNS A records are not stale.** `192.168.1.213` and `192.168.55.213` are both statically bound to its single Intel I219-LM NIC (`...Tcpip\Parameters\Interfaces\{c022c4d4-…}\IPAddress`). Deleting the record on DC01 just lets it re-register — the fix belongs on the host NIC, pending confirmation that `192.168.55.0/24` is dead.
