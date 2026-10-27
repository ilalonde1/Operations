# Runs ON KOR-BK01 (through its NetworkOps agent; RackCollector, at most hourly). READ-ONLY: Get-* and FindLastSession().
# What the REST API at x-api-version 1.2-rev0 does not show (2026-10-26: "offsite not running" was its blind spot, and
# three failed SureBackup restore tests went unseen): every job's last session -- Backup Copy jobs included -- and the
# newest SureBackup sessions with each VM's result. Proven by hand as action 11278 (13 s, module load under 1 s).
$ErrorActionPreference = 'Continue'
Import-Module Veeam.Backup.PowerShell -ErrorAction SilentlyContinue -WarningAction SilentlyContinue
function Utc($t) { if ($t -is [datetime] -and $t.Year -gt 2000) { $t.ToUniversalTime().ToString('o') } else { $null } }
$jobs = @(Get-VBRJob -WarningAction SilentlyContinue | ForEach-Object {
  $last = try { $_.FindLastSession() } catch { $null }
  [pscustomobject]@{
    Name = $_.Name; Type = [string]$_.TypeToString; Enabled = [bool]$_.IsScheduleEnabled
    Result = if ($last) { [string]$last.Result } else { $null }; State = if ($last) { [string]$last.State } else { $null }
    Start = if ($last) { Utc $last.CreationTime } else { $null }; End = if ($last) { Utc $last.EndTime } else { $null }
  } })
# The newest two sessions of each SureBackup job: if the newest is still running, the one before is the verdict.
$sure = @(Get-VBRSureBackupSession -WarningAction SilentlyContinue | Sort-Object { $_.CreationTime } -Descending |
  Group-Object { [string]$_.JobId } | ForEach-Object { $_.Group | Select-Object -First 2 } | ForEach-Object {
    $s = $_
    [pscustomobject]@{
      Job = $s.Name; Result = [string]$s.Result; State = [string]$s.State; Start = Utc $s.CreationTime; End = Utc $s.EndTime
      Vms = @(Get-VBRSureBackupTaskSession -Session $s -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Result = [string]$_.Result } })
    } })
[pscustomobject]@{ Result = "read $($jobs.Count) jobs and $($sure.Count) SureBackup sessions"; Jobs = $jobs; SureBackup = $sure }
