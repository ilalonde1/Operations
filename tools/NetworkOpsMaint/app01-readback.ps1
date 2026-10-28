# ON KOR-APP01 as SYSTEM, read-only: after the restart (runbook section 5). Boot time, pending-restart flags, services,
# the restart task (removed here: it is one-off and has fired), SQL build.
$os = Get-CimInstance Win32_OperatingSystem
$pending = @()
if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $pending += 'WindowsUpdate' }
if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $pending += 'CBS' }
$pfro = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations
if ($pfro) { $pending += "PendingFileRename($(@($pfro).Count))" }
$svc = Get-Service | Where-Object { $_.Name -match 'SQL|Kor|Opportunit|MSSQLLaunchpad|FDLauncher' } | ForEach-Object { "$($_.Name)=$($_.Status)/$($_.StartType)" }
$task = Get-ScheduledTask -TaskName 'KOR-NetOps-Restart' -ErrorAction SilentlyContinue
$taskInfo = if ($task) { $i = $task | Get-ScheduledTaskInfo; "last run $($i.LastRunTime.ToString('HH:mm:ss')) result $($i.LastTaskResult)"; Unregister-ScheduledTask -TaskName 'KOR-NetOps-Restart' -Confirm:$false } else { 'absent' }
$taskAfter = if (Get-ScheduledTask -TaskName 'KOR-NetOps-Restart' -ErrorAction SilentlyContinue) { 'STILL THERE' } else { 'removed' }
$ver = & sqlcmd -S '.\SQLEXPRESS' -E -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(20)) + ' ' + CAST(SERVERPROPERTY('ProductUpdateLevel') AS varchar(10)) + ' start ' + CONVERT(varchar(16), sqlserver_start_time, 120) + ' online ' + (SELECT CAST(COUNT(*) AS varchar) FROM sys.databases WHERE state_desc='ONLINE') + '/' + (SELECT CAST(COUNT(*) AS varchar) FROM sys.databases) FROM sys.dm_os_sys_info" 2>&1
[pscustomobject]@{
  LastBoot = $os.LastBootUpTime.ToString('yyyy-MM-dd HH:mm:ss'); Pending = ($(if ($pending) { $pending -join ',' } else { 'none' }))
  RestartTask = "$taskInfo; $taskAfter"; Sql = "$ver"; Services = $svc -join '; '
}
