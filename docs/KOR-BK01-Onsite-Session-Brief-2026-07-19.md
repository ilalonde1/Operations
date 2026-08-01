# BRIEFING FOR CLAUDE SESSION RUNNING ON KOR-BK01

**You are running locally on KOR-BK01, KOR Structural's Veeam Backup & Replication server.**
This is a production backup server mid-incident. Read this entire brief before running anything.
Operator: Ian Lalonde (Ops Lead, not a P.Eng, strong IT/.NET). Managed backup provider: **T-Net**.

---

## ⛔ HARD SAFETY RULES — violating any of these can destroy production data

1. **NEVER delete anything in Windows Explorer inside `E:\Backup` or `F:\Backups`.** Backup file deletion goes through the Veeam console ONLY (right-click restore point → Delete from disk). Manual file deletion desyncs Veeam's database and corrupts chains.
2. **In vSphere, if you find FS01 disks hot-attached to this VM (KOR-BK01): remove with "Remove from virtual machine" ONLY — NEVER the option that also deletes files from datastore.** The wrong click deletes the live file server's disk.
3. **Do NOT re-run the old `Kor-VMs-New` job against E: for FS01.** It is reverse-incremental; that mode + FS01's 10 TB dedup churn is exactly what filled E: and caused this incident.
4. **Do NOT reset the Windows Administrator password** — Veeam services and TeamViewer depend on it.
5. **This is read-only until Ian approves each phase.** Phase 0 is inventory only. Do not create/edit/delete/start/stop jobs, repos, or files without Ian's explicit go for that phase.
6. **Report findings to Ian and WAIT at each phase gate.** Do not chain into the next phase autonomously.

---

## CURRENT STATE (as of 2026-07-19, established over a 4-day incident)

- **Veeam services were deliberately STOPPED** and jobs **disabled** after a wedged session was force-stopped per Veeam KB1727. Starting services runs nothing while jobs stay disabled.
- The `Kor-VMs-New` backup job (6 VMs incl. Kor-FS01) had been running reverse-incremental into **E:\Backup** and ballooned a rollback (.vrb) file toward disk-full. It was force-stopped. **E: is down to ~1.8 TB free** and its FS01 chain still owes a rollback repair.
- Root causes (two, distinct): (1) vCenter TLS cert expired ~July 7 → ALL backups silently failed 8 days, no provider alert; fixed July 15. (2) Windows dedup enabled on FS01 July 13 → purged shadow copies + created 10 TB churn that couldn't fit E:.

## ENVIRONMENT (verify, don't trust)

- **This server (KOR-BK01):** VM, Windows Server 2019, workgroup, PostgreSQL config DB. Veeam B&R **12.0.0.1420**.
- **Repositories (both local to this box):**
  - `Default Backup Repository` → **E:\Backup** — 25.4 TB, ~1.8 TB free (the crammed one)
  - `Synology105` → **F:\Backups** — 26.4 TB, ~13.6 TB free (physical Synology NAS — RECOVERY TARGET)
- **Off-site copy:** `Kor-Replication` = a **Veeam Cloud Connect** repo at a DC (provider-hosted). Holds all VMs incl. **Kor-FS01, 8 restore points, newest July 7.** This is a genuine second copy — reassuring.
- **Production:** VMware — vCenter 192.168.1.9, ESXi 192.168.1.10 + .16, cluster Van-Cluster, UC3200 SAN. Kor-FS01 = 21 TB file server.
- **Provider agents on this box:** Veeam Availability Console, NinjaRMM, TeamViewer — none alerted during the 8-day outage.

---

## PHASE 0 — INVENTORY (READ-ONLY, do this first, report to Ian)

Run these in an elevated PowerShell. Load the Veeam module first:
```powershell
# Veeam PowerShell (v12 uses a module; older uses a snap-in — try both)
Import-Module Veeam.Backup.PowerShell -ErrorAction SilentlyContinue
Add-PSSnapin VeeamPSSnapIn -ErrorAction SilentlyContinue

# 1. Services + version
Get-Service Veeam* | Format-Table Name, Status, StartType
(Get-ItemProperty 'HKLM:\SOFTWARE\Veeam\Veeam Backup and Replication').CorePackageVersion

# 2. Disks / free space
Get-Volume | Where-Object DriveLetter | Select DriveLetter, FileSystemLabel,
  @{n='SizeTB';e={[math]::Round($_.Size/1TB,2)}}, @{n='FreeTB';e={[math]::Round($_.SizeRemaining/1TB,2)}}

# 3. Jobs — mode, last result, schedule enabled?
Get-VBRJob | Select Name, JobType,
  @{n='Mode';e={$_.BackupTargetOptions.Algorithm}},
  @{n='Enabled';e={$_.IsScheduleEnabled}},
  @{n='LastResult';e={$_.GetLastResult()}}, @{n='LastState';e={$_.GetLastState()}}

# 4. Repositories + free space
Get-VBRBackupRepository | Select Name, Type,
  @{n='FreeGB';e={[math]::Round($_.GetContainer().CachedFreeSpace.InGigabytes)}}

# 5. Backups on disk + restore point counts (incl. the Cloud Connect copy)
Get-VBRBackup | Select Name, JobName, @{n='RPs';e={($_.GetPoints()).Count}}

# 6. Repository folder sizes (read-only)
Get-ChildItem E:\Backup -Directory | ForEach-Object {
  "{0,10:N0} GB  {1}" -f (((Get-ChildItem $_.FullName -Recurse -File -EA SilentlyContinue)|Measure Length -Sum).Sum/1GB), $_.Name }
Get-ChildItem F:\Backups -Directory | ForEach-Object {
  "{0,10:N0} GB  {1}" -f (((Get-ChildItem $_.FullName -Recurse -File -EA SilentlyContinue)|Measure Length -Sum).Sum/1GB), $_.Name }

# 7. The retention setting that guards FS01's July 1-7 local history — READ, don't change
Get-VBRJob -Name 'Kor-VMs-New' | ForEach-Object { $o=$_.GetOptions();
  "DeletedVMsRetention enabled: $($o.GenerationPolicy.EnableDeletedVmDataRetention)  days: $($o.GenerationPolicy.DeletedVmsDataRetentionPeriod)" }
```

**Also in vSphere (browser to https://192.168.1.10 or .16, or vCenter 192.168.1.9):**
- KOR-BK01 VM → Edit Settings → list hard disks. Flag any disk whose path contains `Kor-FS01` (hot-add stray). LOOK ONLY.
- Kor-FS01 VM → Snapshots → flag any `VEEAM BACKUP TEMPORARY SNAPSHOT`. LOOK ONLY.

**→ Report all of the above to Ian. STOP. Wait for approval to proceed.**

---

## RECOVERY PLAN (phases 1-5 — each needs Ian's explicit go)

**Phase 1 — Protect FS01 now.** New backup job `Kor-FS01` → target **Synology105 (F:)** → **Forward Incremental** (NOT reverse) → add only Kor-FS01 → run active full (~9-10 TB, will take ~1-2 days). Ends the 12-day FS01 gap. Independent of the E: mess.

**Phase 2 — Restore nightly rhythm.** First check Phase 0 item 7; if deleted-VMs retention is ON, turn it OFF (else FS01's July 1-7 local points get auto-purged). Then remove Kor-FS01 + BMZ-HO-APP003 + BMZ-HO-APP004 from `Kor-VMs-New`; re-enable the job (5 small VMs, tiny incrementals, fit E: easily).

**Phase 3 — Repair E: chain.** Trigger the pending reverse-incremental rollback in a quiet window (disk-grinding but safe); reclaims ~8-9 TB. Preserve FS01 July 1-7 points. Then console-delete dead weight (APP003/004 old chains, empty `Kor-VMs` folder, `Vcenter_1` if stale).

**Phase 4 — Off-site re-seed.** The 10 TB dedup delta must reach the DC Cloud Connect copy; WAN too small → physical seed drive. Coordinate with T-Net. Confirm the copy job re-maps to FS01's new chain. **Fix the alerting SLA in the same conversation.**

**Phase 5 — Standing punch list** (see docs/KOR-Backup-Infrastructure-Findings-2026-07-16): BK01 Windows updates → Veeam 12.0→12.3 upgrade (needed so a Server 2025 mount server can do file-level restores of deduped FS01) → add KOR-APP01 as mount server → two test restores (one post-July-13, one pre-July-13) → switch Kor-VMs-New off reverse-incremental → repository sizing review with provider.

---

## KEY NUMBERS TO SANITY-CHECK EVERYTHING
- E: free must not hit 0 (catalog lives there). F: is the safe target (~13.6 TB free).
- Any FS01 backup to E: in reverse-incremental = STOP, that's the mistake that caused this.
- Full findings + provider dossier: `docs/KOR-Backup-Infrastructure-Findings-2026-07-16-web.pdf`
