# ON KOR-FS01, read-only: after the update restart. Boot time, pending-restart flags, today's update results, shares.
$os = Get-CimInstance Win32_OperatingSystem
$pending = @()
if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $pending += 'WindowsUpdate' }
if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $pending += 'CBS' }
$s = New-Object -ComObject Microsoft.Update.Session
$h = $s.CreateUpdateSearcher(); $n = $h.GetTotalHistoryCount()
$today = $h.QueryHistory(0, [Math]::Min($n, 30)) | Where-Object { $_.Date -gt (Get-Date).Date.ToUniversalTime() } |
  ForEach-Object { "$($_.Date.ToLocalTime().ToString('HH:mm')) rc=$($_.ResultCode) hr=0x$('{0:X8}' -f $_.HResult) $($_.Title)" }
$left = 'not searched (slow online search; the next health check reports it)'
[pscustomobject]@{
  LastBoot = $os.LastBootUpTime.ToString('yyyy-MM-dd HH:mm:ss'); Pending = ($(if ($pending) { $pending -join ',' } else { 'none' }))
  TodayHistory = @($today); StillOffered = @($left)
  Server = "$((Get-Service LanmanServer).Status)"; Shares = (Get-SmbShare | Where-Object { $_.Name -notmatch '\$$' } | ForEach-Object Name) -join ','
}
