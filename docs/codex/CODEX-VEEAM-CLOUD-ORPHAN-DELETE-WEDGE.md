# CODEX — Veeam Cloud Connect orphan: perpetual Checkpoint Removal + unreclaimable 13.3 TB

## Objective (one incident, two coupled outcomes)
A single Veeam B&R backup got into a corrupt, half-deleted state. Resolve it:
1. **Stop** the "Checkpoint Removal" job that respawns perpetually and wedges (survives process-kill, service restart, and full OS reboot) — even though the backup it references no longer exists in the configuration.
2. **Reclaim** the ~13.3 TB that backup still occupies on a Cloud Connect (provider) repository, **without** provider involvement if at all possible.

Codex cannot reach the server. Produce the **exact, ordered, Veeam-supported procedure** (cmdlets and/or precise SQL with before/after snapshots + rollback) for an operator to execute. Identify the specific persisted record that drives the respawn.

## Environment (verified)
- **Veeam Backup & Replication 12.3.2** (build strings seen: 12.3.2.4854; cloud session reported server version 12.3.2.3617) on **KOR-BK01**, Windows Server 2019, **workgroup-isolated**. Only reachable via a MeshCentral agent running as `NT AUTHORITY\SYSTEM`; WinRM/RDP not available to the operator.
- **Config DB: PostgreSQL 15** (`postgresql-x64-15`, service runs as `NT AUTHORITY\NetworkService`), database **`VeeamBackup`**, `localhost:5432`, listening on loopback only. Local **trust** auth works: `"C:\Program Files\PostgreSQL\15\bin\psql.exe" -w -h 127.0.0.1 -U postgres -d VeeamBackup` connects with no password. Table names are dotted and live in schema `public`, e.g. `public."backup.model.backups"`.
- **Repository `Kor-Replication`** is a **Veeam Cloud Connect** repository hosted by provider **T-Net** (gateway `remote.t-net.ca:6180` / `155.138.155.62`). Cap ~25,600 GB; **2,136.6 GB free**. KOR is the *tenant*; T-Net is the *provider* (the physical storage is on their side). Repo access impl in logs: `CCloudRepositoryAccessorImpl`.

## The corrupt backup (the orphan)
- Was `Kor-Replication\Kor-VMs-New - Kor-FS01`, **backup Id `83615926-5858-4170-a1ef-e2273a8caeea`**.
- **0 restore points but 13,344.8 GB (~13.3 TB)** of storage on the cloud repo — i.e. the restore points are gone from the DB while the storage/blocks remain on the provider. This is the root inconsistency.

## What was tried and observed (chronological, factual)
1. `Remove-VBRBackup -Backup $b -FromDisk`: connected to the T-Net cloud fine, then **hung at the guest-file-index catalog unpublish** — last log line `Making catalog manager notification` in `C:\ProgramData\Veeam\Backup\Utils\DeleteBackup\Task.DeleteBackup_*.log`; sat 2 h+ at 0%.
2. Parked the catalog folder: renamed `E:\VBRCatalog\Index\Machines\Kor-FS01` → `Kor-FS01.OLD-20261007` (still parked — **reversible**; rename back to restore). `VeeamCatalogSvc` is healthy; its `Publications` queue is empty.
3. On every `VeeamBackupSvc` start, startup reconciliation spawns **`Veeam.Backup.Manager.exe STARTCHECKPOINTREMOVAL <sessionId>`** ("Checkpoint Removal Job", a Generic-Independent system job, job UID `bb70be84-d59c-419d-878c-e5d04d97405c`) to finalize the interrupted delete. It **wedges immediately**: 0% progress, **flat CPU** (measured ~0.02 s over 10 s), **no cloud TCP connection**, writes no deletion log. It holds a lock: every `Remove-VBRBackup` (both `-FromDisk` and config-only "forget") fails with `Failed to delete specified backup. It is used by Backup Deletion Job`.
4. The respawn is confirmed to survive: killing the Manager PID (respawns in <5 s), `Stop/Start VeeamBackupSvc`, **and a full OS reboot**.
5. **PostgreSQL config DB, read-only, all empty/absent** — so the lock/respawn is NOT a simple row in the obvious tables:
   - `public."backup.trackedactions.locks"`, `"backup.trackedactions.lockitems"`, `"backup.trackedactions.lockstoleases"` → **0 rows**.
   - `public."backup.model.backups.directbackupcheckpoints"`, `"backup.model.backups.externalcheckpoints"` → **0 rows** (columns: id, backup_id, checkpoint_id, managed_by_vbr, is_synchronized, owner_info xml, usn).
   - `public.bsessions` with `end_time IS NULL` → **0 rows**; no session with job_name like `%checkpoint%`/`%deletion%`.
   - `public."backup.model.backups"` has **no row** for id `83615926…`.
6. **The backup's config entry was observed to be gone at ~16:14** (no longer a child of `Kor-Replication`, not in `backup.model.backups`). No command returned a success for removing it, so the **exact trigger is unconfirmed** — likely the reconciliation deletion partially completed the config/catalog removal but wedged before the storage delete. **The 13.3 TB was NOT reclaimed** (repo free stayed 2,136.6 GB) and the checkpoint-removal loop **still respawns** with the object gone.
7. `Sync-VBRBackupRepository -Repository (Get-VBRBackupRepository -Name 'Kor-Replication')` (the repo rescan) ran OK but **did not re-import** the orphan — only the two legitimate FS01 backups exist afterward. So the orphan's `.vbm` metadata is gone from the repo; re-import-then-clean-delete is **not** available as a path.

## Current verified state (as of 2026-10-07 17:06)
- `Kor-Replication`: **2,136.6 GB free / ~25,600 GB**; children = 6 legitimate replicas (`…- Kor-RDS01`, `- Kor-DC01`, `- KOR-UNIFI01`, `- Kor-APP01`, `- KOR-MESH01`, `Vcenter - vcenter`) under the `Kor-VMs-New` per-VM parent.
- **Legitimate FS01 backups intact and MUST NOT be touched:** `Kor-FS01` (Id `1417178d-7802-4389-b026-cfa76d0e6743`, 33 points, `PerVmParentBackup`) and `Kor-FS01 Offsite` (Id `f32cb7e5-c9df-4107-946c-c8f957781ae2`, 5 points, `SimpleBackupCopyPolicy`).
- Orphan FS01 object: gone from config. Its 13.3 TB: still consumed on the provider repo, now with no tenant-side metadata.
- "Checkpoint Removal" job: respawns perpetually, wedged at 0% CPU (inert, but will not clear).
- Catalog folder `Kor-FS01` remains parked as `Kor-FS01.OLD-20261007`.

## Constraints / guards
- Access to BK01 is via the Mesh agent (SYSTEM) only; psql via loopback trust as above.
- **Do not impact** the two real FS01 backups, the 6 live replicas, the per-VM parent, or any other job/repository. Any destructive step must be guarded (e.g. by exact backup Id, or by asserting the target is NOT one of the Ids above).
- Prefer **no T-Net** involvement. Veeam support is acceptable, but note the license may be a provider/rental (Cloud Connect) license, so a support path could route through T-Net.
- The PostgreSQL config DB is editable via loopback trust, but blast radius is the entire backup configuration — any SQL must be exact, snapshotted, and reversible.

## Questions to answer, with exact steps
1. **Where does VBR 12.3.2 persist the "interrupted delete / pending checkpoint-removal" work-item** that makes startup reconciliation respawn `STARTCHECKPOINTREMOVAL` for a backup already absent from `backup.model.backups`, given `directbackupcheckpoints`/`externalcheckpoints`/`bsessions`(non-terminal)/`trackedactions.*` are all empty? Name the exact PostgreSQL table(s)/row(s) (and how to identify the row for this orphan) and the correct way to clear it so the loop stops permanently.
2. **Tenant-side reclaim of orphaned Cloud Connect data** when the backup object and its `.vbm` are already gone: is there any supported tenant-side mechanism (orphaned-data import/rescan variant, a specific cmdlet, re-registering the backup, `Start-VBR*`/repository maintenance) that reclaims the 13.3 TB, or is provider-side (T-Net) cleanup the only path? If provider-only, state that definitively and specify exactly what the provider must run and against what identifier.
3. Provide the **exact ordered procedure** (psql/SQL with `SELECT` before, `UPDATE`/`DELETE` with WHERE keyed to the orphan, `SELECT` after, and rollback; and/or Veeam cmdlets) to (a) stop the respawn loop and (b) reclaim or definitively release the 13.3 TB — including the guard conditions that prove the other backups are untouched, and a `pg_dump` of `VeeamBackup` as the rollback gate before any write.

## Deliverable
An ordered runbook the operator can paste/run step by step, each step with its verification command and expected output, and an explicit "STOP — needs provider" marker if/where tenant-side action cannot finish the reclaim.
