# KOR-BK01 — Phase 1 Runbook: Protect FS01 (2026-07-19)

Goal: get a fresh FS01 backup after a 12-day gap. Target = Synology105 (F:), forward incremental.
Do NOT touch E: (crammed) or reboot/upgrade until the full completes.

## PART 1 — Free C: (need ≥40 GB before a 2-day run)
```powershell
Get-ChildItem C:\ -Directory | ForEach-Object { [PSCustomObject]@{F=$_.Name; GB=[math]::Round((Get-ChildItem $_.FullName -Recurse -File -EA SilentlyContinue|Measure Length -Sum).Sum/1GB,1)} } | Sort GB -Desc | Select -First 12
Remove-Item C:\Windows\Temp\* -Recurse -Force -EA SilentlyContinue
Remove-Item $env:TEMP\* -Recurse -Force -EA SilentlyContinue
Get-ChildItem 'C:\ProgramData\Veeam\Backup' -Recurse -Filter *.log -EA SilentlyContinue | Where-Object LastWriteTime -lt (Get-Date).AddDays(-14) | Remove-Item -Force -EA SilentlyContinue
Dism.exe /online /Cleanup-Image /StartComponentCleanup
Clear-RecycleBin -Force -EA SilentlyContinue
Get-Volume C | Select @{n='FreeGB';e={[math]::Round($_.SizeRemaining/1GB,1)}}
```

## PART 2 — Windows updates + reboot BK01 (clean state before a 2-day run)
(Do PART 1 C: cleanup FIRST — updates need space; 24.7 GB is too tight for a weeks-old cumulative.)
Settings → Update & Security → Windows Update → install ALL pending → reboot.
Rationale: BK01 = 95-day uptime, pending updates, just force-killed Veeam. Reboot applies updates
(so no auto-reboot kills the run), clears force-stop residue, and proves Veeam returns healthy while watching.
NOTE: this reboots BK01 (the Veeam server), NOT FS01. FS01 backup consistency comes from VSS/snapshot, not an FS01 reboot.

## PART 3 — Post-reboot verification (do NOT skip) + confirm freeze
- Get-Service Veeam* | ft Name,Status  → VeeamBackupSvc must be Running.
- Veeam console → Backup Infrastructure → Backup Repositories → right-click each → Rescan (clear "Unavailable").
- Home → Jobs: all four still DISABLED (Kor-VMs-New, Vcenter, ManualNew, Kor-Replication).
- If anything is off after reboot, STOP and report before running the full.

## PART 3b — (freeze already covered above)

## PART 4 — Build + run the job
1. Home → Backup Job → Virtual machine → VMware vSphere.
2. Name: Kor-FS01. Next.
3. Virtual Machines → Add → Kor-FS01 → Add. Next.
4. Storage:
   - Backup repository: **Synology105** (NOT Default/E:).
   - Retention: 14 restore points.
   - Advanced → Backup tab → **Incremental** (forward). Confirm "Reverse incremental" is OFF. Tick "Create synthetic full periodically" (Saturday).
   - OK → Next.
5. Guest Processing: enable application-aware + guest creds if handy; else leave off (crash-consistent OK for first full). Next.
6. Schedule: daily evening; leave "Run the job when I click Finish" ticked.
7. Finish. First run of a forward-incremental job IS a full — it starts writing ~10 TB to F: automatically.

## While running (1-2 days)
- Watch F: free (13.6 TB → ~3-4 TB expected). Flag if < 2 TB.
- No reboot, no upgrade, don't touch E:.

## After completion (Phase 5, later)
Veeam 12.0→12.3 upgrade + Windows updates + E: cleanup (console-delete the failed ~15 TB 2026-07-15 FS01 full). Separate session.
Ref: docs/KOR-Backup-Infrastructure-Findings-2026-07-16-web.pdf, docs/KOR-BK01-Onsite-Session-Brief-2026-07-19.md
