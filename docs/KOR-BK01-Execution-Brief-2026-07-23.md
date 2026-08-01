# EXECUTION BRIEF — Claude Code session on KOR-BK01

You are running locally on **KOR-BK01**, KOR Structural's Veeam Backup & Replication server (now v12.3.2).
The backup incident of July 2026 is **fully recovered and restore-tested**. You are now executing the
**optimization roadmap** (target design: `docs/KOR-Backup-Target-Design-2026-07-23.pdf`). Operator: Ian Lalonde.

Read this whole brief before running anything.

---

## ⛔ HARD RULES — do not violate

1. **STOP before anything irreversible. Do NOT, under any circumstance in this session:**
   - Delete or unregister any VM (especially BMZ-HO-FS007 — 10.5 TB old file server, possibly the only copy of old data).
   - Delete/retire the old FS01 reverse-incremental chain on E: (~23 TB).
   - Modify, disable, or delete the `Kor-Replication` Cloud Connect copy job or anything touching `remote.t-net.ca`.
   - Change any job's backup mode (reverse↔forward) or retention.
   These are gated on Ian's explicit approval and will be done later, carefully. **Investigate and report only.**
2. **The ONLY deletion permitted this session** is the verified-orphaned Dec-2025 `.vrb` files in Action 1 — and only after the DB check confirms they're orphaned.
3. **Never delete backup files via the filesystem** except that one verified case. Everything else goes through the Veeam console.
4. **Report findings + recommendations to Ian and WAIT.** Do not chain into destructive steps.

## Context you need
- Repos: `Default`→E:\Backup (25.4 TB, ~1.9 TB free, ~23 TB is FS01's old chain), `Synology105`→F:\Backups (26.4 TB, ~5.7 TB free), `Kor-Replication`→Cloud Connect at remote.t-net.ca.
- vSphere: vCenter 192.168.1.9; hosts 192.168.1.10 and 192.168.1.16; the dead BMZ VMs live on **.16**.
- **PowerCLI is NOT installed here.** Use Veeam's own vCenter connection (`Get-VBRServer`, `Find-VBRViEntity`) or read Veeam logs for vSphere facts; do not install PowerCLI.
- Load Veeam PowerShell: `Import-Module Veeam.Backup.PowerShell -EA SilentlyContinue`

---

## ACTION 1 — Delete orphaned Dec-2025 .vrb files (SAFE, ~44 GB)
```powershell
# 1. Preview the candidates
Get-ChildItem 'E:\Backup\Kor-VMs-New' -File -Filter 'Kor-FS01*2025-12*' |
  Select-Object @{n='GB';e={[math]::Round($_.Length/1GB,1)}}, Name

# 2. Prove Veeam's DB has NO 2025 restore points for FS01 (MUST return nothing)
Import-Module Veeam.Backup.PowerShell -EA SilentlyContinue
Get-VBRBackup -Name 'Kor-VMs-New' | Get-VBRRestorePoint |
  Where-Object { $_.VMName -eq 'Kor-FS01' -and $_.CreationTime.Year -eq 2025 }
```
- If step 2 returns **nothing** and step 1 lists only 2025-12 Kor-FS01 files → delete:
```powershell
Get-ChildItem 'E:\Backup\Kor-VMs-New' -File -Filter 'Kor-FS01*2025-12*' | Remove-Item -Force -Verbose
```
- If step 2 returns ANY row → **do not delete**; report to Ian.

## ACTION 2 — Investigate the dead BMZ VMs (READ-ONLY — do NOT delete)
Goal: give Ian the facts to safely approve retiring ~20 TB. For each of BMZ-HO-FS007, DC005, APP002, RDS001:
- Power state (should be OFF).
- Disk file paths + sizes (which datastore, how big).
- Last power-on / last-modified evidence you can find (Veeam inventory, vCenter events via Veeam, or vmdk file dates via datastore browser if reachable).
- Whether anything still references them (in any Veeam job, or current DNS/AD if checkable).
Use Veeam's vCenter connection:
```powershell
Get-VBRServer
Find-VBRViEntity -Server (Get-VBRServer -Type VC) -Name 'BMZ-HO-*'
```
**Special attention: BMZ-HO-FS007** — the old file server. Report its size, last-used evidence, and disk paths, but the delete decision is Ian's (he must confirm its data was migrated to the current Kor-FS01). Do NOT delete it.

## ACTION 3 — Report
Summarise to Ian:
- Action 1 result (files deleted / space freed, or why not).
- Action 2 findings per VM, with a clear recommendation on which are safe to retire and which need Ian's data-check (FS007).
- Then STOP and wait for Ian's go on the actual VM retirements.

Reference: full roadmap in `docs/KOR-Backup-Target-Design-2026-07-23.pdf`. Steps beyond these (job-mode conversion, off-site re-seed, E: chain retirement) are later and several need T-Net coordination — not this session.
