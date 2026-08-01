# KOR — Immutability Server (Veeam Hardened Repository) — Build Sheet

**Purpose:** a **local, immutable** backup copy that ransomware cannot delete or encrypt — the fast on-prem recovery tier. Immutability is enforced by the Linux kernel on this box, so even a compromised domain/BK01 can't wipe it.
**Prepared:** 2026-07-27 · One-page build reference.

---

## 1. Hardware (repurpose an old PC + new drives)

| Part | Spec | Notes |
|---|---|---|
| **Chassis/PC** | 64-bit, **16 GB RAM** (32 GB better), room for **4 drives** | Tower/workstation, not an SFF/NUC. Pick the most reliable old box (PSU/board). |
| **SATA/ports** | 4–6 SATA ports | If short, add a **~$40 HBA card** (IT-mode, e.g. LSI 9211-8i). |
| **PSU** | Enough SATA power leads for 4 drives | Splitter if needed. |
| **Drives** | **4 × Seagate Exos X18 16 TB SATA** (or IronWolf Pro 16 TB) | **MUST be CMR + SATA.** Never SMR, never SAS. ~$280–300 ea. Optional 5th as cold spare. |
| **NIC** | Onboard gigabit is fine | (This box isn't on the 10G core anyway.) |

**Storage layout:** 4 drives → **software RAID (mdadm) RAID5** ≈ **48 TB usable** → **XFS** (with reflink) on the array. Redundancy matters — this is your only local immutable copy.

---

## 2. Operating system & filesystem

- **OS:** Ubuntu Server **22.04 LTS** (or newer LTS). Minimal install, no GUI.
- **NOT domain-joined.** Local accounts only.
- **Filesystem:** **XFS** on the mdadm array, mounted with reflink support (gives Veeam fast-clone — space-efficient synthetic fulls without the RAM cost of ReFS).
- **Dedicated repo mount**, e.g. `/mnt/veeamrepo`.

---

## 3. Hardening (this is what makes it "immutable")

- Create a **single-use Veeam repository account** (Veeam manages it; you never log in with it interactively).
- Set the **immutability period: 14–30 days** (recommend 30 for ransomware dwell-time).
- **SSH:** key-only or disabled after setup; no root login; firewall to Veeam/BK01 only.
- **Time sync (NTP)** must be correct — immutability is time-based.
- Keep it **physically in the server room**, powered via the UPS.

---

## 4. Network

- **Static IP in the reserved band `192.168.1.200–.254`** (outside the DHCP pool `.51–.199`) — e.g. `.241`. Document it in the network sheet.
- Gateway `192.168.1.1`, DNS `192.168.1.30`.
- Ideally on a management segment once VLANs exist; for now, LAN is fine (immutability is the protection, not network isolation).

---

## 5. Wire it into Veeam (I'll drive this part with you)

1. Veeam console → **Backup Infrastructure → Backup Repositories → Add → Linux (Hardened Repository)** → point at `/mnt/veeamrepo`, enter the single-use creds, tick **"Make backups immutable for N days."**
2. Create a **Backup Copy job** → source = your existing local backups → target = this hardened repo. (This becomes your on-prem immutable tier.)
3. Optional later: wrap Synology + hardened repo + object storage into a **Scale-Out Backup Repository** with GFS.

---

## 6. Buy / do checklist

- [ ] Pick the PC (16 GB+ RAM, 4 bays, best PSU/board)
- [ ] Order **4 × Exos X18 16 TB SATA** (+ optional 5th spare); HBA card if ports are short
- [ ] Install Ubuntu 22.04 LTS
- [ ] Build mdadm RAID5 + XFS on `/mnt/veeamrepo`
- [ ] Harden (single-use account, SSH lockdown, NTP, firewall)
- [ ] Assign static IP `.241`, document it
- [ ] **Then ping me** — I'll walk the Veeam Hardened Repository add + Backup Copy job

**Cost:** ~$1,100–1,300 (drives) + $0 (repurposed PC) + optional ~$40 HBA. That's your entire local ransomware-recovery tier.
