# KOR NetworkOps -- read-only health of a Windows SERVER (not a workstation: no GPU, no Office, no user apps).
# Runs ON the server through the one-shot SCM channel. Returns PLAIN values only (strings, numbers, arrays of
# pscustomobjects of those): a rich object serialises its whole PS graph (the 105 MB lesson, 2026-09-29).
# Built after 2026-09-30, when stuck VSS writers on FS01 failed three Veeam runs and nothing said so directly.
$ErrorActionPreference = 'SilentlyContinue'
$os = Get-CimInstance Win32_OperatingSystem
$disks = @(Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" | ForEach-Object {
    [pscustomobject]@{ Drive = "$($_.DeviceID)"; SizeGB = [math]::Round($_.Size / 1GB, 1); FreeGB = [math]::Round($_.FreeSpace / 1GB, 1) } })

$writersText = (vssadmin list writers) -join "`n"
$writers = @([regex]::Matches($writersText, "Writer name: '([^']+)'[\s\S]*?State: \[\d+\] ([^\r\n]+)[\s\S]*?Last error: ([^\r\n]+)") | ForEach-Object {
    [pscustomobject]@{ Name = "$($_.Groups[1].Value)"; State = "$($_.Groups[2].Value.Trim())"; LastError = "$($_.Groups[3].Value.Trim())" } })

# Automatic services that are not running. Ones Windows starts on demand and stops again (trigger-start, or
# delayed-auto that exit when idle) are the usual noise; they are excluded by name.
# AppXSvc / InventorySvc are demand-start on Server 2025; ncstreamer is T-Net's NinjaRemote (being retired).
$quiet = 'gupdate|edgeupdate|MapsBroker|sppsvc|RemoteRegistry|tiledatamodelsvc|WbioSrvc|CDPSvc|dmwappushservice|OneSyncSvc|CDPUserSvc|MSDTC|clr_optimization|TrustedInstaller|BITS|wuauserv|UsoSvc|WaaSMedicSvc|Sense|DoSvc|ShellHWDetection|GoogleUpdater|InstallService|StateRepository|camsvc|VSS|swprv|defragsvc|AppXSvc|InventorySvc|ncstreamer'
$stopped = @(Get-CimInstance Win32_Service -Filter "StartMode='Auto' AND State<>'Running'" | Where-Object { $_.Name -notmatch $quiet } |
    ForEach-Object { [pscustomobject]@{ Name = "$($_.Name)"; Display = "$($_.DisplayName)" } })

$pending = [bool](Get-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending' -ErrorAction SilentlyContinue) -or
           [bool](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations -or
           [bool](Get-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired' -ErrorAction SilentlyContinue)
$lastHotfix = (Get-HotFix | Where-Object InstalledOn | Sort-Object InstalledOn -Descending | Select-Object -First 1)
$storageErrors = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = (Get-Date).AddHours(-24); Level = 1, 2 } -MaxEvents 400 |
    Where-Object { $_.ProviderName -match '^(disk|Ntfs|volsnap|Microsoft-Windows-Ntfs|storahci|stornvme|iScsiPrt|mpio)$' } |
    Group-Object { "$($_.ProviderName) $($_.Id)" } | ForEach-Object { [pscustomobject]@{ Event = "$($_.Name)"; Count = $_.Count } })

# Class 2 -- "configured right, not just up": antivirus posture and NIC firewall profile. Each read is wrapped in try/catch,
# NOT left to $ErrorActionPreference: removing the Defender feature takes its WMI provider with it, so Get-MpComputerStatus
# throws a TERMINATING "Invalid class" CimException that SilentlyContinue does not swallow -- which blanked the whole server
# probe the moment Defender was uninstalled (RDS01 went rack.unreachable, 2026-10-06). A missing engine must read as absent,
# never as a dead probe. Windows Server never auto-passives Defender without MDE, so Defender in Normal mode plus a running
# third-party AV means two active engines fighting.
$mp = try { Get-MpComputerStatus } catch { $null }
$winDefend = try { Get-Service WinDefend -ErrorAction Stop } catch { $null }
$wrsvc = try { Get-Service WRSVC -ErrorAction Stop } catch { $null }
$nicCats = @(try { Get-NetConnectionProfile | ForEach-Object { "$($_.NetworkCategory)" } } catch { })

[pscustomobject]@{
    ProbeVersion = 2
    Computer = "$env:COMPUTERNAME"
    OsCaption = "$($os.Caption)"
    OsBuild = "$($os.Version)"
    UptimeHours = [math]::Round(((Get-Date) - $os.LastBootUpTime).TotalHours, 1)
    Disks = $disks
    VssWriters = $writers
    StoppedAutoServices = $stopped
    PendingReboot = $pending
    LastUpdateDays = if ($lastHotfix) { [math]::Round(((Get-Date) - $lastHotfix.InstalledOn).TotalDays) } else { -1 }
    StorageErrors24h = $storageErrors
    DefenderInstalled = [bool]$winDefend
    DefenderMode = "$($mp.AMRunningMode)"
    DefenderRealtime = [bool]$mp.RealTimeProtectionEnabled
    WebrootStatus = "$($wrsvc.Status)"
    NicCategories = $nicCats
}
