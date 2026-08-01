# KOR Structural — Managed-Service Findings & Redesign Record

**Prepared for the T-Net (Tenacious Networks) review.** Living document — maintained through the remediation program; present when complete.
**Period covered:** 2026-07-07 → 2026-07-25 (ongoing) · **Owner:** Ian Lalonde, KOR Structural

---

## 1. Executive summary

Between 2026-07-07 and 2026-07-25, KOR discovered and remediated a series of backup, virtualization, and data-integrity problems in the T-Net-managed environment — several of which sat unflagged for days to weeks, and one of which was a latent catastrophic data-loss condition. KOR self-drove the recovery and a full redesign. This record documents **(A)** what was found, **(B)** what KOR changed, and **(C)** the open items and questions for T-Net.

Tone is deliberately factual and evidence-based. Where T-Net's position was sound, that is noted.

---

## 2. Findings — issues discovered

| # | Finding | Evidence / date | Impact |
|---|---|---|---|
| F1 | **8-day silent backup outage.** vCenter (192.168.1.9) management certificate expired ~07-07; **all** Veeam jobs failed for ~8 days until KOR restarted them manually on 07-15. **No alert was raised by the managed monitoring** the entire time. | Repo files show last good restore points 07-07 ~17:00; discovered manually 07-15 12:43 | **Permanent data loss exposure:** FS01 files created 07-08→07-13 and deleted before 07-15 are unrecoverable (backup gap × shadow-copy purge). |
| F2 | **No effective monitoring / alerting.** The outage, the later backup failures, and the snapshot condition (F7) all went undetected by the managed service. | Throughout | A managed backup service should detect a total backup failure same-day. |
| F3 | **Backups never restore-tested.** No SureBackup, no documented restore test had ever been performed. Restorability was unproven. | First restore test performed by KOR 07-21 | Backups of unknown validity = false assurance. |
| F4 | **No immutability, no GFS, no ransomware protection** in the design. | Design review 07-16→07-23 | No ransomware-proof copy; no deep point-in-time history. |
| F5 | **Reverse-incremental job configuration** (3× write amplification) contributed to a disk-full grind during recovery. | Kor-VMs-New job; incident 07-15→ | Slow, space-inefficient; part of the near-full repo condition. |
| F6 | **Data scrubbing never run** on either Synology (Kor-NAS01 / Synology02). Backup-storage integrity had never been verified. | Storage Manager: "Never performed yet" (07-24) | Silent bit-rot in backups could surface only at restore time. |
| F7 | **FS01 orphaned snapshot + stranded backup disks.** FS01 was left running on an un-consolidated snapshot delta, and **both** of FS01's virtual disks (OS + 21 TB data) were left hot-added to the backup server (BK01) from an interrupted backup. vCenter surfaces a "consolidation needed" condition for exactly this — it was not caught. | Discovered/fixed 07-24→25 | **Latent catastrophic trap:** a "Delete from disk" on BK01 would have destroyed FS01's live 21 TB. Also caused ongoing FS01 backup failures and a growing snapshot. |
| F8 | **~22.5 TB of dead/superseded backups** left on the primary repo, running it to ~99% full and causing backup failures. | Reclaimed by KOR 07-24 | Near-full repo breaks merges/synthetic fulls; part of the failure cascade. |
| F9 | **vCenter certificate lifecycle unmanaged** — its expiry caused the entire incident. New cert now valid to **2028-07-14**; renewal must be owned. | 07-15 | Same outage will recur in 2028 without an owner + reminder. |
| F10 | **Cost vs. service delivered.** ~$1,000+/month billed for managing ~3–5 VMs while F1–F9 sat unaddressed. | Ongoing | Value question for the relationship. |

### Where T-Net's position was sound (for balance)
- **NTFS over ReFS:** T-Net correctly advised keeping local repository storage NTFS rather than ReFS. Given BK01's 16 GB RAM, ReFS block-clone on a 25 TB+ repo would have been unsafe. Sound call.
- **Host layout:** production consolidated on the faster host (.10) with the older host (.16) held as a manual-failover reserve is a reasonable design under VMware Essentials (no vSphere HA).

---

## 3. Remediation & redesign delivered by KOR (07-15 → 07-25)

**Recovery & platform**
- Recovered the outage: re-accepted the new vCenter cert, restarted all jobs.
- Upgraded Veeam B&R **12.0.0.1420 → 12.3.2.4854**.
- Stood up **APP01 (Server 2025) as the mount server** — deduplicated file-level restore now works.

**Storage & hygiene**
- **Reclaimed ~22.5 TB** by retiring the dead FS01 reverse-incremental chain; verified live backups intact.
- Converted the main job path to **forward-incremental + weekly synthetic full**.
- **Data scrubbing** scheduled monthly + running on both Synologys (first-ever integrity pass).
- Right-sized **DC01 16→8 GB** (freed failover margin); bumped **BK01 to 6 vCPU / 32 GB**.

**Data protection & integrity**
- **Health checks** enabled on all 5 jobs (3 backups + 2 copy jobs).
- **Ransomware detection:** inline entropy analysis (live), Veeam **Threat Hunter** signature engine, **File Detection + IoC**, alerts routed to KOR.
- **Guest indexing + application-aware processing** (application-consistent backups, incl. the domain controller).
- First-ever **restore test** and off-site copy build/seed.

**The FS01 snapshot / stranded-disk repair (07-24→25)**
- Consolidated FS01's orphaned snapshot back to a clean single disk.
- Removed **both** stranded FS01 disks from BK01 — **disarmed the 21 TB data-loss trap.** No data lost.

**Target architecture (v5, 3-2-1-1-0)**
- Local immutable **Veeam Hardened Repository** (Linux/XFS) for fast ransomware recovery.
- Off-site immutable **Object-Lock** tier + deep **GFS** retention.
- **SureBackup** automated recovery verification.

### Delivered since 2026-07-25 (update 2026-07-27)
- **SureBackup built and PROVEN:** Virtual Lab (Kor-Lab01, isolated network on the reserve host) + Application Group + job. First run **verified all four core VMs — DC01, FS01, APP01, RDS01 — boot and restore** (heartbeat + ping + role/SMB/RDP tests all pass). This is the first time KOR's backups have ever been *proven* restorable. Nightly auto-verification being scheduled.
- **Forward-forever incremental** locked in (weekly synthetic-full disabled — it was stacking with a CBT-reset full and overloading the Synology).
- **APP01 right-sized** (32→64 GB RAM, 4→8 vCPU, SQL max-memory capped) and its **SQL databases moved off the OS drive to a dedicated data disk** — with **zero downtime for the disk add** and no connection-string/code changes. Flagged a separate compliance item: APP01 runs **SQL Server Developer Edition in production** (dev/test-licensed only) — proper Standard licensing to be arranged (KOR-side, not a T-Net item).
- **DC01 right-sized** (16→8 GB), freeing manual-failover headroom on the busy host.
- **Immutability build sheet produced** — hardened-repo box speced (repurposed PC + 4×16 TB Exos SATA, Ubuntu/XFS); KOR to build.

### Design decisions (recorded for the review)
- **Local VM replication to the reserve host: deferred.** For KOR's size, manual failover + the now-*verified* Instant VM Recovery gives an acceptable RTO, and the reserve host's **1 GbE / off-storage-fabric** path (a network-doc finding) would make on-host replicas degraded anyway.
- **Second domain controller: deferred.** SureBackup having proven DC01 restorable reduces the urgency of the DC01 single-point-of-failure; a 2nd DC remains the recommended long-term fix.

---

## 4. Open items & dependencies on T-Net

| Item | Ask |
|---|---|
| **Cloud Connect immutability** | Enable immutability (or Insider Protection) on the KOR tenant repo — requested 07-24. |
| **WAN accelerator** | Provider-side accelerator for the off-site copy — requested. |
| **License coverage** | Confirm the rental Enterprise Plus licence covers the **Hardened Repository** role at no extra cost. |
| **vCenter cert renewal** | Establish ownership + reminder for the **2028-07-14** expiry (root cause of F1). |
| **Monitoring / alerting SLA** | Establish real alerting that would catch a total backup failure, a "consolidation needed" condition, and repo-full — none of which were caught (F1, F2, F7, F8). |

### Questions for the review
1. Why was the **8-day total backup outage** (F1) never alerted?
2. Why was the **FS01 "consolidation needed"** condition (F7) — visible in vCenter — never flagged?
3. Why were the **repeated FS01 backup failures** (F7/F8) not caught?
4. What monitoring is actually in place, and what is the alerting SLA?
5. Who owns the vCenter certificate lifecycle going forward?

---

*Maintained by KOR Structural Operations. Last updated 2026-07-25.*
