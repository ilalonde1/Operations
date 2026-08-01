# KOR Backup & Virtualization Infrastructure

## Findings & Remediation Punch List — 2026-07-16

**Prepared for:** Ian Lalonde, Operations Lead — review with managed-backup provider
**Scope:** Veeam Backup & Replication on KOR-BK01, VMware cluster (vCenter 192.168.1.9, hosts 192.168.1.10 / .16, UC3200 SAN), KOR-FS01 file server
**Trigger:** Investigation of 2026-07-13 Data Deduplication rollout on KOR-FS01 surfaced a chain of unrelated, unmonitored backup-infrastructure failures.

---

## A0. RESOLUTION STATUS (2026-07-20) — read first

- **FS01 is protected again.** A new job `Kor-FS01` → Synology repository, **forward incremental**, completed 2026-07-20: **5.79 TB written at ~126 MB/s in ~14 hours, 0 errors.** First FS01 restore point since 2026-07-07 (13-day gap closed).
- **The measurement that matters:** the same data via the old **reverse-incremental** job to E: ran at **~29 MB/s** and had consumed **~23 TB** of repository before failing. Forward incremental on a right-sized target: **~4× faster, ~⅓ the disk.** This is the single clearest quantification of what the existing job design costs.
- vCenter certificate re-accepted; snapshot on FS01 confirmed removed and consolidated (vCenter's own confirmation); no stray hot-add disks; BK01 patched and rebooted; C: reclaimed (was 20 GB free — cause: files in the Administrator profile's Downloads).
- **Still open:** off-site copy of FS01 (needs new copy job + physical seed); Veeam upgrade; mount server for file-level restore; E: repository structurally over-committed (see A10); no restore has ever been tested (A11).

## A. Findings

### A1. vCenter TLS certificate expired — all backups silently dead for ~8 DAYS ⛔ *(fixed 2026-07-15)*
- **Corrected 2026-07-17 from repository evidence:** the last successful restore points are dated **2026-07-07 ~17:00**; the next are 2026-07-15 12:43 (manual restart by KOR). **The company ran with zero backups for ~8 days.** (Initial "2-day" estimate came from the console's Last-24-Hours view, which cannot show older failures.)
- Cause: vCenter's machine certificate expired (likely 07-07/07-08 — a July-2024 two-year VMCA cert); a replacement was auto-issued 2026-07-15 11:09 (valid to 2028-07-14, thumbprint `CD3550B8…4EA76E21`). Veeam pins the trusted thumbprint → every nightly job attempt and 4-hourly discovery failed until manual re-accept on 07-15.
- **No alert reached KOR from the managing provider at any point in the 8 days.** The failure was found by KOR reviewing the console manually.
- **Permanent exposure window (compounded by A7):** with backups dead from 07-07 and pre-existing shadow copies purged 07-13, any FS01 data **created 07-08 → 07-13 and deleted/overwritten before 07-15** has no recoverable copy anywhere.
- Fix applied: certificate re-accepted via Managed Servers → vCenter properties; discovery and jobs confirmed working.

### A2. Veeam B&R three years unpatched ⛔
- Running **12.0.0.1420** (v12 GA, early 2023). Predates multiple critical, actively-exploited Veeam CVEs. The backup server is the highest-value ransomware target in the environment.
- Also functionally outgrown: cannot manage Windows Server 2025 machines (KOR-FS01, KOR-APP01 are 2025).
- Remediation: in-place upgrade to **12.3.x** (direct path from 12.0 supported). License is a rental via provider (aaronp@t-net.ca) — includes upgrades.

### A3. File-level restores from FS01's current backups are impossible ⛔
- FS01 (Server 2025) enabled Data Deduplication on E: on 2026-07-13. All FS01 restore points **after** that date contain a 2025-format deduplicated volume.
- The Veeam mount server (KOR-BK01, **Server 2019**) cannot mount that format — single-file restore from any post-2026-07-13 FS01 backup fails. Full-VM and volume restores are unaffected. Interim workaround: Instant VM Recovery, copy files from the booted VM.
- Remediation: after A2's upgrade, add KOR-APP01 (Server 2025; dedup feature already installed 2026-07-15) as a managed server and set it as the repository mount server. Restore points **before** 2026-07-13 are plain NTFS and restore normally today.

### A4. Retired servers still in the nightly backup job ⚠️
- **BMZ-HO-APP003 / BMZ-HO-APP004** are retired (powered off, decommissioned), yet remain objects in `Kor-VMs-New` — consuming backup window, repository space, and copy-job bandwidth nightly.

### A5. Orphaned VM folders squatting on the SAN ⚠️
- UC3200 datastore (50.78 TB, 7.51 TB free) holds **unregistered** leftover folders: `BMZ-HO-APP002`, `BMZ-HO-DC005`, `BMZ-HO-FS007`, `BMZ-HO-RDS001` — invisible in inventory, unquantified TBs reclaimable.
- ⚠️ `BMZ-HO-FS007` is the old BMZ file server — verify no unmigrated data before deletion (likely the only copy in existence; these folders predate current backup jobs).

### A6. Duplicate RDS01 folders — live-disk verification required ⚠️
- Both `Kor-RDS01` and `Kor-RDS01_1` exist on UC3200. The `_1` suffix indicates a past re-registration/migration name collision — **one of these contains the live server's disks.** Do not touch either until vSphere → Kor-RDS01 → Edit Settings confirms which paths are in use.
- Likely related: Veeam session note "*Kor-RDS01 is no longer processed by this job*" (VM identity changed at some point; worth confirming RDS01's restore-point continuity).

### A7. FS01 shadow copies purged by dedup rollout — uncommunicated side effect ⚠️
- The 2026-07-13 initial dedup optimization rewrote ~10 TB and **flushed all pre-existing VSS snapshots on E:** — "Previous Versions" history restarted from zero on 2026-07-13.
- Older file versions remain recoverable via pre-2026-07-13 Veeam restore points (plain NTFS, normal file-level restore on current setup).
- Snapshots have been re-accumulating normally since (Dedup Writer stable; 1.5 TB shadowstorage cap, ample headroom).

### A8. One-time post-dedup backup hump in progress ℹ️
- The dedup rewrite makes ~10 TB of FS01 blocks read as "changed." The first backup after it (started 2026-07-15 12:45) must move all of it — a **multi-day run** at current transport speeds.
- Datastore snapshot-growth risk assessed: ~7.5 TB free vs. expected snapshot growth under 2 TB — **ride it out**; daily free-space check, abort threshold 3 TB.
- Follow-on: the `Kor-Replication` copy job repeats the hump (no VM snapshot held — stop/resume safe). Afterward, incrementals return to normal permanently.
- **Root cause of slowness identified 2026-07-17:** session shows `Bottleneck: Target` at ~29 MB/s with transport already hot-add. The job runs in **Reverse Incremental** mode — ~3× write amplification on the BK01 repository for every changed block. The repository disk + mode combination, not the SAN or network, is the limiter.
- 07-17 status: 5 of 6 VMs complete; FS01 at 26% (4.3 TB of ~10 TB transferred); ~2 days remaining. UC3200 free space stable at 7.51 TB — snapshot growth negligible, datastore risk retired.

### A9. Monitoring & governance gaps ⛔
- Jobs red for two days with no provider alert (A1). Veeam email notifications: recipient/config unknown — KOR receives nothing.
- No evidence of periodic test restores (industry baseline: at least quarterly file-level + annual full-VM test).
- `ManualNew` job (4 VMs → Synology105, unscheduled): purpose undocumented.
- BK01 pending Windows updates; Veeam console reports "Missing Updates (1)" for a managed server.

### A10. The E: repository is structurally over-committed — and cannot be cleaned up ⛔ *(new 2026-07-20)*
Verified against Veeam's own restore-point database and the files on disk:

| File | Size | Status |
|---|---|---|
| `Kor-FS01…2026-07-15…vbk` | **14.7 TB** | **Chain head — load-bearing** |
| `Kor-FS01…2026-07-07…vrb` | **8.3 TB** | 07-07 restore point, inflated by dedup churn — load-bearing |
| 17 × `.vrb` (Jun 20 → Jul 7) | ~87 GB | Daily history points — load-bearing |
| 9 × `.vrb` dated **Dec 2025** | ~44 GB | True orphans (no restore points reference them) |

- FS01's chain alone occupies **~23 TB of a 25.4 TB repository.** In reverse-incremental the `.vbk` holds the *current* state and all 18 older points are reconstructed by rolling backward through it — **so nothing meaningful can be deleted without destroying the entire FS01 local history, including the pre-dedup Jul 1–7 points.** Only ~44 GB is safely reclaimable.
- **This is a design problem, not a housekeeping problem:** a 21 TB VM was placed in a reverse-incremental chain on a repository that cannot hold one full plus its rollback growth. The July 13 dedup rewrite merely exposed it.
- The 2026-07-15 "Full" restore point originates from the **failed** session; its integrity is **unverified** and should be validated (Veeam Backup Validator) before any decision about the chain.

### A11. No restore has ever been tested ⛔ *(new 2026-07-20)*
- There is no evidence any restore — file-level, volume, or full-VM — has ever been performed or tested from any of the three copies (E:, Synology, Cloud Connect).
- Consequence: the decision about reclaiming E:'s 23 TB **cannot be made safely**, because deleting local history would leave KOR relying on two copies that have never been proven restorable.
- This is the highest-value outstanding item and should precede any repository cleanup.

---

## B. Questions for the managing provider

1. Who monitors job results? Why did an **8-day total backup outage (2026-07-07 → 07-15)** — zero restore points for every production server — produce no alert to KOR? It was discovered only because KOR opened the console.
2. Who owns vCenter certificate lifecycle? The expiry was foreseeable to the day, two years in advance.
3. What is the patch policy for backup infrastructure? (B&R was 3 years / several exploited CVEs behind.)
4. Why are retired servers (BMZ-HO-*) still in active jobs and on the SAN in 2026?
5. What is `ManualNew` for, and what is the history of `Kor-RDS01_1`?
6. What restore-testing cadence is included in the engagement? When was the last successful test restore?
7. Request: add KOR (ilalonde@) to Veeam email notifications regardless of provider monitoring.
8. **Design:** why is a 21 TB file server backed up in **reverse-incremental** mode onto a **25.4 TB** repository — a target that cannot hold one full plus rollback growth? Forward incremental to the Synology completed the same workload ~4× faster in ~⅓ the space. What job design do you recommend going forward?
9. **The E: chain (~23 TB):** validate and keep, or retire once the Synology and Cloud copies are proven restorable? What migration path preserves the pre-dedup Jul 1–7 history?
10. **Off-site for FS01:** now on its own Synology chain, FS01 no longer flows to the Cloud Connect copy. What is your plan and timeline for the physical seed to the DC, and who performs it?
11. **`ManualNew` job:** unscheduled, undocumented, targets the Synology (competing with FS01's new chain), holds a March 2026 FS01 full. Keep, repurpose, or retire?
12. **Repository sizing:** what capacity do you recommend for E: (or its replacement) given a 21 TB and growing file server, and what is the refresh plan?

---

## C. Remediation punch list

| # | Action | Owner | When | Verification |
|---|--------|-------|------|--------------|
| 1 | ~~Re-accept vCenter cert; restart job chain~~ | KOR | **Done 07-15** | Apply step green; jobs running |
| 2 | Daily UC3200 free-space check during current run (abort < 3 TB) | KOR | Daily until run ends | Free ≥ 3 TB |
| 3 | Verify orphaned folders (dates/sizes; RDS01 live-disk paths via Edit Settings) | KOR | Anytime (read-only) | Each folder classified keep/archive/delete |
| 4 | Delete verified-dead unregistered folders (APP002, DC005, RDS001; FS007 only after data review/archive) | KOR | After verification | UC3200 free space increases |
| 5 | Remove BMZ-HO-APP003/004 from `Kor-VMs-New` (decide: export final restore point first). Check Storage→Advanced→Maintenance "remove deleted items" retention | KOR | After current run completes | Job runs without them; exports on Synology if kept |
| 6 | Delete APP003/004 from vCenter inventory + disk | KOR | After #5 | Folders gone; inventory clean |
| 7 | Expect FS01 snapshot consolidation at run end (possible slowdown — do not interrupt) | KOR | At run end | Snapshot manager shows none |
| 8 | Windows updates + reboot KOR-BK01 | KOR | Quiet window after run | No pending updates |
| 9 | Notify provider; upgrade Veeam 12.0 → 12.3.x (config backup first — runbook exists) | KOR / provider | This week, after #8 | Build ≥ 12.3; jobs green next cycle |
| 10 | Add KOR-APP01 as managed server; set as repository mount server | KOR | After #9 | Repository properties show APP01 |
| 11 | Acceptance tests: file-level restore from a post-07-13 FS01 point **and** a pre-07-13 point | KOR | After #10 | Both files restore and open |
| 12 | Let `Kor-Replication` copy job absorb its one-time hump | KOR | After main run | Copy job green |
| 13 | Configure Veeam email notifications to KOR; agree monitoring/alerting SLA with provider | KOR / provider | With #9 | Test email received |
| 14 | Evaluate hot-add proxy (APP01) for transport speed | KOR / provider | After #10 | Large-run throughput materially improved |
| 15 | Clarify `ManualNew` job purpose; document or remove | Provider | Next review | Documented decision |
| 16 | Resolve `Kor-RDS01` vs `Kor-RDS01_1`; clean stale folder; confirm RDS01 restore-point continuity | KOR / provider | After #3 | Single folder; restore points verified |
| 17 | Establish restore-test cadence (quarterly FLR, annual full-VM) | Provider | Ongoing | Test log |
| 18 | Switch `Kor-VMs-New` from **Reverse Incremental** to **Forward Incremental** + periodic synthetic fulls (~3× less target I/O; ends multi-day runs) | KOR / provider | After current run (with #9) | Next large run completes overnight |
| 19 | Audit Default Backup Repository placement/disk speed on BK01 (Target-bottlenecked at ~29 MB/s) | Provider | With #18 | Documented layout; throughput ≥ 100 MB/s |
| 20 | ~~New `Kor-FS01` job → Synology, forward incremental, active full~~ | KOR | **Done 07-20** | 5.79 TB, ~14 h, 126 MB/s, 0 errors |
| 21 | Remove Kor-FS01 + BMZ-HO-APP003/004 from `Kor-VMs-New`; re-enable it, `Vcenter`, `Kor-Replication` | KOR | Now | 3 small VMs back on nightly; off-site copy resumes |
| 22 | **Test restores from all three copies** (E: pre-dedup point, Synology, Cloud Connect) — never done | KOR / provider | **Priority** | Documented successful restores |
| 23 | Validate the unverified 2026-07-15 "Full" (Veeam Backup Validator) | KOR / provider | Before any E: decision | Pass/fail recorded |
| 24 | Decide the fate of the ~23 TB E: chain (see A10) — only after #22 | Provider / KOR | After #22 | Documented decision |
| 25 | New backup-copy job for the `Kor-FS01` chain + physical seed to DC | Provider / KOR | Scheduled with provider | FS01 current in off-site copy |
| 26 | Configure Veeam email notifications to KOR; agree alerting SLA | Provider | **Immediate** | Test alert received |
| 27 | Delete ~44 GB of orphaned Dec-2025 `.vrb` files (only safe filesystem reclaim) | KOR | Anytime | Files gone |

---

*Compiled 2026-07-16 from live console evidence (Veeam B&R on KOR-BK01, ESXi host client, direct TLS inspection of vCenter). All findings verified at time of writing; no configuration changes were made beyond items marked Done.*
