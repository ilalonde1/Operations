# ON KOR-APP01 as SYSTEM: restart APP01 in 60 s through a one-off task, so this NetworkOps run returns its answer first
# (NetworkOps runs on APP01 and cannot watch its own host come back). Ian via operations-ee (14:5x): reboots authorized.
$at = (Get-Date).AddMinutes(1)
$a = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\shutdown.exe" -Argument '/r /t 0 /d p:2:17 /c "KOR maintenance: Windows security updates (operations-e6, Ian authorized)"'
$t = New-ScheduledTaskTrigger -Once -At $at
$p = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'KOR-NetOps-Restart' -Action $a -Trigger $t -Principal $p -Force | Out-Null
[pscustomobject]@{ Result = "restart scheduled for $($at.ToString('HH:mm:ss'))"; LastBoot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToString('yyyy-MM-dd HH:mm') }
