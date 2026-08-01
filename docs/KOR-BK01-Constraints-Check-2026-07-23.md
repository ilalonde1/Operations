# READ-ONLY CONSTRAINTS CHECK — Claude Code on KOR-BK01

You are on **KOR-BK01** (Veeam B&R 12.3.2). This is a **READ-ONLY data-gathering pass** to size the remaining
backup redesign (Phase B/C) against real hardware constraints. Design context: `docs/KOR-Backup-Target-Design-v4-2026-07-23.pdf`.

## ⛔ HARD RULE
**Change NOTHING.** No config edits, no job changes, no service restarts, no deletions. Read and report only.

Load Veeam PowerShell: `Import-Module Veeam.Backup.PowerShell -EA SilentlyContinue`

---

## Gather and report:

### 1. BK01 compute (the key one — RAM)
```powershell
Get-CimInstance Win32_ComputerSystem | Select-Object @{n='RAM_GB';e={[math]::Round($_.TotalPhysicalMemory/1GB)}}, NumberOfLogicalProcessors
Get-Counter '\Memory\Available MBytes','\Processor(_Total)\% Processor Time' -SampleInterval 2 -MaxSamples 3 |
  ForEach-Object { $_.CounterSamples } | Select-Object Path, CookedValue
```
Report: total RAM, logical CPUs, current available RAM, current CPU %.

### 2. Storage presentation for E: and F: (confirms the ReFS-iSCSI concern + object-storage plan)
```powershell
Get-Disk | Select-Object Number, FriendlyName, BusType, @{n='SizeTB';e={[math]::Round($_.Size/1TB,1)}}, PartitionStyle
Get-Partition | Where-Object DriveLetter -in 'E','F' | Select-Object DriveLetter, DiskNumber
# iSCSI (if any)
Get-IscsiTarget -ErrorAction SilentlyContinue | Select-Object NodeAddress, IsConnected
Get-IscsiConnection -ErrorAction SilentlyContinue | Select-Object TargetAddress
```
Report: bus type for the E: and F: disks (iSCSI / SAS / RAID / etc.), and any iSCSI targets.

### 3. Veeam licence — what's enabled
```powershell
Get-VBRInstalledLicense | Select-Object Edition, Status, LicensedTo, ExpirationDate, @{n='Type';e={$_.Type}}
```
Report edition + status (confirms object-storage capacity tier / SOBR are covered).

### 4. Mount server APP01 — cache room
```powershell
Get-VBRServer | Where-Object { $_.Name -like '*APP01*' } | Select-Object Name, Type
# C: free on APP01 if reachable (it's domain-joined):
Invoke-Command -ComputerName KOR-APP01 { Get-Volume C | Select-Object @{n='FreeGB';e={[math]::Round($_.SizeRemaining/1GB)}}, @{n='SizeGB';e={[math]::Round($_.Size/1GB)}} } -ErrorAction SilentlyContinue
# Does APP01 have any spare volume for a cache disk?
Invoke-Command -ComputerName KOR-APP01 { Get-Volume | Where-Object DriveLetter | Select-Object DriveLetter, @{n='FreeGB';e={[math]::Round($_.SizeRemaining/1GB)}} } -ErrorAction SilentlyContinue
```

### 5. ESXi host RAM headroom (to size a BK01 RAM bump + SureBackup lab placement)
Use Veeam's vCenter connection (no PowerCLI needed). Report, for hosts 192.168.1.10 and 192.168.1.16: total physical RAM and current used/available if obtainable. Try:
```powershell
Get-VBRServer -Type ESXi | Select-Object Name
# host memory may require the vSphere client; if you can pull it via Veeam inventory, report it, else note "needs vSphere client"
```

### 6. Job configs (to plan retention/GFS/verification)
```powershell
Get-VBRJob | ForEach-Object {
  $o = $_.GetOptions()
  [PSCustomObject]@{
    Name = $_.Name
    Mode = $o.BackupTargetOptions.Algorithm
    SyntheticFull = $o.BackupStorageOptions.EnableFullBackup
    RetainDays = $o.BackupStorageOptions.RetainDays
    RetainCycles = $o.BackupStorageOptions.RetainCycles
    AppAware = $o.VssOptions.Enabled
    Indexing = $o.ViSourceOptions.VMWareToolsQuiesce
  }
} | Format-Table -AutoSize
```
Report each job's mode, synthetic-full on/off, retention, app-aware/indexing state.

### 7. Malware detection / Threat Hunter state
Report whether inline malware detection / Threat Hunter is enabled (check via the console Security & Compliance / Malware Detection settings, or any `Get-VBRMalwareDetection*` cmdlet). Note if guest indexing being off limits any detection feature.

### 8. Seed progress
```powershell
Get-VBRJob -Name 'Kor-FS01 Offsite Copy' | ForEach-Object { "$($_.Name): $($_.GetLastState()) / $($_.GetLastResult())" }
```
Report the offsite copy's current state/progress %.

---

## Report format
Give the raw output for each, then a short summary flagging: (a) BK01 RAM vs. what the feature set needs, (b) whether host .16 can give BK01 more RAM, (c) storage bus type (iSCSI confirmed?), (d) APP01 cache room, (e) anything that constrains SureBackup/malware/GFS. Then STOP — no changes.
