# Runs ON the workstation (Windows PowerShell 5.1, as SYSTEM) under the NetworkOps payload.
# Returns raw facts only; the parsing and judgement happen in C# on the server side, where
# they are tested (Kor.Operations.NetworkOps.Core). Everything here is a local read.
$smb = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\mssmbios\Data' -Name SMBiosData -ErrorAction SilentlyContinue).SMBiosData
$os  = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$cpu = (Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0').ProcessorNameString
$sb  = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' -ErrorAction SilentlyContinue
$gpus = @(Get-CimInstance Win32_VideoController | Where-Object { $_.Name -notmatch 'Remote Display|Basic Display' } |
    ForEach-Object { [pscustomobject]@{ Name = $_.Name; Driver = $_.DriverVersion } })
$disks = @(Get-PhysicalDisk -ErrorAction SilentlyContinue | ForEach-Object {
    [pscustomobject]@{ Name = $_.FriendlyName; Media = [string]$_.MediaType; Bus = [string]$_.BusType
                       GB = [math]::Round($_.Size / 1GB); Health = [string]$_.HealthStatus } })
$c = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
# ProductName still says "Windows 10" on Windows 11 -- Microsoft never updated it. Build 22000+
# is Windows 11, so the build decides.
$product = $os.ProductName
if ([int]$os.CurrentBuild -ge 22000) { $product = $product -replace 'Windows 10', 'Windows 11' }
[pscustomobject]@{
    SmbiosBase64 = if ($smb) { [Convert]::ToBase64String($smb) } else { $null }
    Os           = "$product $($os.DisplayVersion) build $($os.CurrentBuild).$($os.UBR)"
    Cpu          = $cpu.Trim()
    SecureBoot   = if ($sb) { [bool]$sb.UEFISecureBootEnabled } else { $null }
    Gpus         = $gpus
    Disks        = $disks
    SystemDriveGB     = [math]::Round($c.Size / 1GB)
    SystemDriveFreeGB = [math]::Round($c.FreeSpace / 1GB)
}
