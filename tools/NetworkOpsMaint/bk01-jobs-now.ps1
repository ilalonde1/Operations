# ON KOR-BK01, read-only: is any Veeam job scheduled or running right now (before FS01/DC01 restarts)?
Import-Module Veeam.Backup.PowerShell -ErrorAction Stop -WarningAction SilentlyContinue
$jobs = Get-VBRJob -WarningAction SilentlyContinue | ForEach-Object {
  $s = $_.GetScheduleOptions()
  [pscustomobject]@{ Name = $_.Name; Type = "$($_.JobType)"; Enabled = $_.IsScheduleEnabled; Running = $_.IsRunning; Next = "$($s.NextRun)"; Last = "$($_.FindLastSession().EndTime)" }
}
$sure = Get-VBRSureBackupJob -WarningAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = 'SureBackup'; Enabled = $_.IsEnabled; Running = "$($_.LastRun)"; Next = "$($_.NextRun)"; Last = '' } }
@($jobs) + @($sure) | Sort-Object Name
