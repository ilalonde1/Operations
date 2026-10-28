# ON KOR-DC01, read-only: after the update restart. Boot time, pending-restart flags, today's update results, AD/DNS/DHCP up.
$os = Get-CimInstance Win32_OperatingSystem
$pending = @()
if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $pending += 'WindowsUpdate' }
if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $pending += 'CBS' }
$h = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher(); $n = $h.GetTotalHistoryCount()
$today = $h.QueryHistory(0, [Math]::Min($n, 15)) | Where-Object { $_.Date -gt (Get-Date).Date.ToUniversalTime() } |
  ForEach-Object { "$($_.Date.ToLocalTime().ToString('HH:mm')) rc=$($_.ResultCode) hr=0x$('{0:X8}' -f $_.HResult) $($_.Title)" }
$svc = 'NTDS','DNS','DHCPServer','Netlogon','KDC','W32Time' | ForEach-Object { $s = Get-Service $_ -ErrorAction SilentlyContinue; "$_=$(if ($s) { $s.Status } else { 'absent' })" }
$dns = try { (Resolve-DnsName 'kor-app01.int.korstructural.com' -Server 127.0.0.1 -Type A -ErrorAction Stop | Select-Object -First 1).IPAddress } catch { "FAILED: $($_.Exception.Message)" }
$sysvol = Test-Path "\\$env:COMPUTERNAME\SYSVOL"
[pscustomobject]@{
  LastBoot = $os.LastBootUpTime.ToString('yyyy-MM-dd HH:mm:ss'); Pending = ($(if ($pending) { $pending -join ',' } else { 'none' }))
  TodayHistory = @($today); Services = $svc -join '; '; DnsSelfLookupApp01 = $dns; SysvolShared = $sysvol
}
