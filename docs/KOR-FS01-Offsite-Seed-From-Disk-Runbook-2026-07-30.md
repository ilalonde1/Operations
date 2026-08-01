# KOR Structural — FS01 Off-Site Copy: Seed-from-Disk Runbook

**Purpose:** Get a current, recoverable off-site copy of KOR-FS01 (the 21 TB file server) into the T-Net Cloud Connect repository by shipping the first full on a drive, so that only small nightly deltas ever have to cross the WAN afterward.
**Owner:** Ian Lalonde, KOR Structural · **Date:** 2026-07-30 · **Off-site target:** T-Net Cloud Connect (`remote.t-net.ca\kor`, Toronto)

---

## 1. Why we're doing this (the problem, in three lines)

- The `Kor-FS01 Offsite Copy` job has **0 sealed restore points.** Every night it runs ~11 hours, moves ~260 GB at **7 MB/s (network-bottlenecked)**, then hits **"Job was stopped due to backup window setting"** and finishes with an **error** before it can seal a point.
- At that rate the initial full seed (**~6–10 TB** on the wire) is **weeks-to-months** away and never completes, because FS01 changes daily and the finish line keeps receding.
- **Fix:** carry the first full to Toronto on a drive. T-Net imports it into your cloud repo. From then on the copy job only sends the nightly change (small, fits the window). This is the textbook Veeam procedure for a large seed over a thin link.

**What is NOT broken:** FS01's **local** backups are healthy and SureBackup-proven (your primary recovery path). The four small VMs (APP01/DC01/RDS01/vcenter) are **current off-site** via the separate `Kor-Replication` job (11 points each, newest today). The gap is *only* the fresh off-site copy of FS01.

---

## 2. Hardware required

| Item | Spec | Notes |
|---|---|---|
| **Seed drive ×1** | 1× 16 TB SATA CMR — **Seagate Exos X18 16 TB (ST16000NM001J)** | Covers the seed at either end of the ~6–10 TB range with headroom. Same model as the hardened-repo build → **reusable** (see §3). |
| **USB-to-SATA dock** | USB 3.1 Gen2 (10 Gbps), 3.5" bay, own power brick | The Exos is a bare internal drive — you need a dock to write it and **T-Net needs one to read it** (ship the dock with the drive, or confirm T-Net has one). |
| **Encryption** | BitLocker-To-Go **or** Veeam backup-file encryption | **Mandatory** — this drive carries your entire file server out the door via courier (see §6). |
| **Physical staging PC** | Any on-LAN Windows box with USB 3 | To receive the .vbk before it ships. BK01/APP01 are VMs — see §5 Step 2 for the attach decision. |

**Confirm the real seed size before you rely on the drive choice** (our records span ~6–10 TB because FS01's E: is Windows-deduped, ~10 TB physical behind 21 TB logical, and Veeam BitLooker/compression then vary the result):

> Veeam console → **Home → Backups → Disk → Kor-FS01** → right-click the newest restore point → **Properties** → read **Backup Size** for the full. (Or check the `.vbk` size directly in `F:\Backups\Kor-FS01\`.)

---

## 3. Can the seed drive become a hardened-repo drive afterward? — YES

Once T-Net has imported the seed and the copy job is confirmed sealing points, the data on the drive is **disposable**. Wipe it and it joins the Linux hardened-repo array with no residue.

**Recommended:** buy the seed drive as a **5th Exos X18** (~$300) on top of the four for the RAID5.
- The four hardened-repo drives stay together, so you can **build the Linux box in parallel** without waiting on the courier round-trip.
- After seeding, the 5th drive becomes your **cold spare** — exactly what you want on the shelf for a 4-drive RAID5 (single-drive fault tolerance; a spare means same-day rebuild).

**Budget alternative:** borrow **one of the four** hardened-repo drives as the shuttle. Works fine, but the RAID5 build (mdadm needs all four present) **waits ~1–2 weeks** for that drive to return from Toronto. Since the hardened-repo box is a parallel build at your pace, that may be acceptable — your call.

Either way the drive is **fully erased between roles** (NTFS/GPT for seeding → wiped → XFS-on-mdadm in the array), so there's no format or data carry-over risk.

---

## 4. Pre-flight gates (all must be true before you start)

1. **T-Net will do a tenant seed import.** Cloud Connect seeding *requires* the service provider to place the seed into your cloud repo on their end — this is a hard dependency. Confirm with T-Net (aaronp@t-net.ca) that they support tenant drive-seeding and get their intake steps. **If they won't, use the §7 fallback instead.**
2. **FS01 local backup = Success** with a good, recent full to export from (`Home → Jobs`, Kor-FS01 last run green).
3. **Exact full-backup size confirmed** (§2) and the drive comfortably exceeds it.
4. **Encryption method chosen** (§6).
5. **Staging attach-point decided** (§5 Step 2).

---

## 5. Procedure

### Step 1 — Stop and disable the failing off-site job
Veeam console → **Home → Jobs** (or the running session) → **Kor-FS01 Offsite Copy** → **Stop**, then right-click → **Disable**.
- Losing this job costs nothing — it has **0 sealed points**. You'll recreate it in Step 6 mapped to the seed.
- **Do NOT touch `Kor-Replication`** (that job keeps the four small VMs current off-site — leave it running/enabled).
- **Never "Delete from disk"** anything, on any FS01 object.

### Step 2 — Decide where the drive attaches, then prep it
BK01 and APP01 are VMs, so a bare USB drive can't just "plug into" them. Two viable paths:

- **(Recommended) Export to local repo, then copy to USB from a physical box.** Export the full to your free `E:` repo (~24 TB free on .15) as a standalone `.vbk`, then copy that file to the encrypted USB drive attached to any on-LAN physical Windows machine. Decouples the slow/fussy USB write from Veeam and lets you verify the `.vbk` before it leaves.
- **(Alternative) USB pass-through to BK01** via ESXi host .16 (Host USB device → BK01 VM). Exports write straight to the drive, but large sustained pass-through transfers can be flaky — acceptable for a one-time job, not preferred.

Prep the drive: **GPT, single NTFS volume, enable BitLocker-To-Go** (record the recovery key to your vault, *not* on the drive).

### Step 3 — Export the FS01 full for seeding
Veeam console → **Home → Backups → Disk → Kor-FS01** → select the newest restore point → ribbon **Export Backup** → produce a standalone full `.vbk` → target the export at your chosen destination (E: repo per recommended path, or the USB drive directly).
- **Export is image-level** (block-level), so it is **not** blocked by the deduped-volume FLR limitation — it does **not** need the APP01 mount server. It runs on the Veeam infrastructure.
- Budget time: a ~6–10 TB export to USB at ~150–200 MB/s is roughly **10–16 hours**; to the LAN repo it's faster, then the USB copy is the long pole. Run it off-hours; it's local I/O, no WAN.

### Step 4 — Verify, encrypt-in-transit, ship
- Confirm the `.vbk` is complete and the drive mounts/reads cleanly on a second machine.
- Include a **manifest** (VM name, backup date, .vbk filename + size, BitLocker handling instructions) and your T-Net ticket/reference.
- Ship to the T-Net Toronto DC by a **tracked, signature-required** courier. Send T-Net the BitLocker key **out-of-band** (not with the drive, not in the same email as the tracking number).

### Step 5 — T-Net imports the seed (their side)
T-Net imports the `.vbk` into **your tenant's cloud repository** so it registers as an available backup. This is their documented Cloud Connect tenant-seeding process — request the exact steps from them; the outcome you need is: **the seeded FS01 full appears as a restore point in your `Kor-Replication` cloud repo.**

### Step 6 — Map a new copy job to the seed
Once T-Net confirms the seed is imported:
- **Home → Backup Copy → Add** a new job (recreate `Kor-FS01 Offsite Copy`): source **Kor-FS01**, target the **Kor-Replication** cloud repo.
- On the target step, use **"Map backup"** → point it at the imported seed. Veeam registers the seed as the starting point and will transfer **only changes** from here on.
- Set the copy mode/window as before (the nightly delta is small and *will* fit).

### Step 7 — Confirm it's actually working now
- Let one cycle run. **Home → Backups → Cloud → Kor-FS01 Offsite Copy → Kor-FS01** should now show **≥1 restore point**, and the run should finish **Success**, not window-stopped.
- Watch two or three nights: restore points should **increment** and stay current.

### Step 8 — Clean up and repurpose
- Once the new off-site FS01 copy is confirmed healthy and current, **retire the stale 7/7 FS01 points** under the old `Kor-Replication` job (frees cloud space; the fresh copy supersedes it).
- **Wipe the seed drive** → build it into the hardened-repo RAID5, or shelve it as the cold spare (§3).

---

## 6. Security & chain-of-custody (do not skip)

- The seed is **a full copy of your file server** leaving the building. Treat it like the data itself.
- **Encrypt the drive** (BitLocker-To-Go or Veeam encryption). A lost/stolen unencrypted drive is a reportable data breach.
- **Key travels separately** from the drive and separately from the tracking info.
- **Tracked, signed** courier; log the handoff.
- **Wipe on return** before any reuse (§8).

---

## 7. Fallback if T-Net won't seed from disk — lift the window

If drive-seeding isn't available, the only other way through is to let it grind over the WAN:

- Edit `Kor-FS01 Offsite Copy` → remove/widen the **backup window** so it can run past 06:00 (ideally 24/7 until the first full seals).
- Reality check: the pipe is still **7 MB/s**, so ~6–10 TB is **~10–16 days of continuous transfer** — and running through business hours means **~56 Mbps of sustained upload** competing with staff (Teams, email, cloud apps). Acceptable only if that bandwidth hit is tolerable.
- This is a band-aid; once seeded, re-impose a sane window for steady-state deltas.

*(WAN accelerator — already on the T-Net ask list — reduces ongoing delta size but won't rescue the cold seed on its own.)*

---

## 8. Guardrails (repeat, because they matter)

- **Never** "Delete from disk" on any FS01 object — the FS01 disk-strand trap is disarmed; keep it that way.
- **Do not touch `Kor-Replication`** — it's the healthy off-site job for the four small VMs.
- The **BK01 box stays workgroup-isolated** — nothing here changes that.
- Server-side changes are **warn-first, then execute** — this runbook is the plan; each step is yours to trigger.

---

*Prepared by KOR Structural Operations, 2026-07-30. Companion to the backup redesign record (`KOR-TNet-Findings-and-Redesign-Record`) and the hardened-repo build sheet (`KOR-Hardened-Repo-Build-Sheet-2026-07-27`).*
