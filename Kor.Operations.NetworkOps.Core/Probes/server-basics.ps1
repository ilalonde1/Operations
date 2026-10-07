# KOR NetworkOps -- LIGHT Windows health for a box reached over its MeshCentral agent (BK01, a remote-only server).
# The full server.ps1 is too heavy for MeshCentral's runcommands channel: Get-WinEvent (400 events), vssadmin and
# Get-HotFix run long or hang there, and the agent runs ONE command at a time -- a slow run leaves it "already busy", so
# the next sweep cannot read it. This is the BASICS only -- disk, pending reboot, stopped automatic services, uptime,
# OS build -- from CIM and the registry, no event log: a couple of seconds. ServerRules evaluates whatever is present;
# the fields it does NOT send (VSS writers, storage errors, antivirus, NIC profiles) simply raise nothing, which also
# keeps the AV check (gated on WebrootStatus) and the NIC check from firing on a box they were never meant to judge here.
$ErrorActionPreference = 'SilentlyContinue'
$os = Get-CimInstance Win32_OperatingSystem
$disks = @(Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" | ForEach-Object {
    [pscustomobject]@{ Drive = "$($_.DeviceID)"; SizeGB = [math]::Round($_.Size / 1GB, 1); FreeGB = [math]::Round($_.FreeSpace / 1GB, 1) } })

# Automatic services that are not running. The same demand-start / trigger-start noise server.ps1 excludes by name.
$quiet = 'gupdate|edgeupdate|MapsBroker|sppsvc|RemoteRegistry|tiledatamodelsvc|WbioSrvc|CDPSvc|dmwappushservice|OneSyncSvc|CDPUserSvc|MSDTC|clr_optimization|TrustedInstaller|BITS|wuauserv|UsoSvc|WaaSMedicSvc|Sense|DoSvc|ShellHWDetection|GoogleUpdater|InstallService|StateRepository|camsvc|VSS|swprv|defragsvc|AppXSvc|InventorySvc|ncstreamer'
$stopped = @(Get-CimInstance Win32_Service -Filter "StartMode='Auto' AND State<>'Running'" | Where-Object { $_.Name -notmatch $quiet } |
    ForEach-Object { [pscustomobject]@{ Name = "$($_.Name)"; Display = "$($_.DisplayName)" } })

$pending = [bool](Get-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending' -ErrorAction SilentlyContinue) -or
           [bool](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations -or
           [bool](Get-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired' -ErrorAction SilentlyContinue)

[pscustomobject]@{
    ProbeVersion = 2
    Computer = "$env:COMPUTERNAME"
    OsCaption = "$($os.Caption)"
    OsBuild = "$($os.Version)"
    UptimeHours = [math]::Round(((Get-Date) - $os.LastBootUpTime).TotalHours, 1)
    Disks = $disks
    VssWriters = @()
    StoppedAutoServices = $stopped
    PendingReboot = $pending
    StorageErrors24h = @()
}
