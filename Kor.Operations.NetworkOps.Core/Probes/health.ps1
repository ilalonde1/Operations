# health.ps1 -- runs ON the workstation (Windows PowerShell 5.1, as SYSTEM) under the NetworkOps
# payload. Returns raw facts; the judgement is the rules in Kor.Operations.NetworkOps.Core, where
# it is tested against fixtures captured from real machines. Every read here is local, so the
# VPN only carries the result.
#
# Each block is the signal for a fault class found by hand in 2026 (design doc section 2). A block
# that fails records its error instead of taking the whole probe down: a machine with a broken
# WMI still reports its disks.

$now = Get-Date
$since14 = $now.AddDays(-14)
$errors = New-Object System.Collections.Generic.List[string]
# Milliseconds per block: "check this PC now" has to answer in seconds, so what is slow must be visible.
$timings = [ordered]@{}
$probeClock = [Diagnostics.Stopwatch]::StartNew()
function Try-Block([string]$name, [scriptblock]$body) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try { & $body } catch { $errors.Add("${name}: $($_.Exception.Message)"); $null }
    finally { $timings[$name] = [int]$sw.ElapsedMilliseconds }
}
function Count-Events($log, [hashtable]$filter) {
    $filter['LogName'] = $log
    $filter['StartTime'] = $since14
    @(Get-WinEvent -FilterHashtable $filter -ErrorAction SilentlyContinue)
}
# A failed block returns $null, and @($null) is a one-element array holding null. Every list in
# the result goes through this so a failed read is an empty list, never a list of one null.
# The leading comma matters: a function's output is unrolled, so without it a one-item list
# comes back as the bare item and serialises as an object, not an array (KOR fleet, 2026-09-28).
function Arr($x) { , @($x | Where-Object { $null -ne $_ }) }
function Summ($events) {
    # @($null) has Count 1 in PowerShell -- an empty query must count as zero, not as one null event.
    $e = @($events | Where-Object { $_ })
    [pscustomobject]@{ Count = $e.Count; Last = if ($e.Count) { ($e | Sort-Object TimeCreated -Descending)[0].TimeCreated.ToString('s') } else { $null } }
}

# --- operating system, uptime, pending reboot
$osInfo = Try-Block 'os' {
    $os = Get-CimInstance Win32_OperatingSystem
    $cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $product = $cv.ProductName
    if ([int]$cv.CurrentBuild -ge 22000) { $product = $product -replace 'Windows 10', 'Windows 11' }   # ProductName was never updated for 11
    [pscustomobject]@{
        Product = $product; DisplayVersion = $cv.DisplayVersion; Build = [int]$cv.CurrentBuild; Ubr = [int]$cv.UBR
        LastBoot = $os.LastBootUpTime.ToString('s'); UptimeHours = [int]($now - $os.LastBootUpTime).TotalHours
    }
}
$pending = Try-Block 'pendingReboot' {
    [pscustomobject]@{
        ComponentServicing = Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'
        WindowsUpdate      = Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
        FileRename         = [bool](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue)
    }
}

# --- storage: volumes, physical disks, drives that vanished (206-N's D: on 2026-09-28)
$volumes = Try-Block 'volumes' {
    @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object {
        [pscustomobject]@{ Letter = $_.DeviceID.TrimEnd(':'); Label = $_.VolumeName
            SizeGB = [math]::Round($_.Size / 1GB, 1); FreeGB = [math]::Round($_.FreeSpace / 1GB, 1) } })
}
$physical = Try-Block 'physicalDisks' {
    @(Get-PhysicalDisk -ErrorAction Stop | ForEach-Object {
        [pscustomobject]@{ Name = $_.FriendlyName; Media = [string]$_.MediaType; Bus = [string]$_.BusType
            SizeGB = [math]::Round($_.Size / 1GB); Health = [string]$_.HealthStatus; Operational = [string]$_.OperationalStatus } })
}
$missingDisks = Try-Block 'missingDisks' {
    # A disk Windows has seen before but which is no longer present. Nameless entries are the
    # SATA port's placeholder, not a drive. The instance id says which bus it was on, so the
    # rule can tell a vanished internal drive from a USB stick someone unplugged.
    @(Get-PnpDevice -Class DiskDrive -ErrorAction Stop | Where-Object { -not $_.Present -and $_.FriendlyName } |
        ForEach-Object { [pscustomobject]@{ Name = $_.FriendlyName; InstanceId = $_.InstanceId } })
}
$orphanLetters = Try-Block 'orphanLetters' {
    # Drive letters Windows still has assigned (MountedDevices) with no volume behind them.
    $assigned = @((Get-Item 'HKLM:\SYSTEM\MountedDevices').Property | Where-Object { $_ -match '^\\DosDevices\\([A-Z]):$' } | ForEach-Object { $Matches[1] })
    $present = @(Get-CimInstance Win32_LogicalDisk | ForEach-Object { $_.DeviceID.TrimEnd(':') })
    @($assigned | Where-Object { $_ -notin $present })
}

# --- hardware and stability events, last 14 days
$events = Try-Block 'events' {
    $wer = Count-Events 'Application' @{ ProviderName = 'Windows Error Reporting'; Id = 1001 }
$gpuWer = @($wer | Where-Object { $_.Message -match 'LiveKernelEvent' -and $_.Message -match 'P1: 141\b' })
    [pscustomobject]@{
        DiskBadBlock       = Summ (Count-Events 'System' @{ ProviderName = 'disk'; Id = 7 })
        DiskResets         = Summ (@(Count-Events 'System' @{ ProviderName = 'storahci'; Id = 129 }) + @(Count-Events 'System' @{ ProviderName = 'stornvme'; Id = 129 }))
        NtfsCorruption     = Summ (Count-Events 'System' @{ ProviderName = 'Ntfs'; Id = 55 })
        # v3: warnings and errors only (corrected 17/19/47 are Warning, fatal are Error). WHEA also logs
        # Information-level notices (event 3, "an informational record", vendor data) -- all five of
        # 206-N's "hardware errors" on 2026-09-29 were those, i.e. not errors at all.
        Whea               = Summ (Count-Events 'System' @{ ProviderName = 'Microsoft-Windows-WHEA-Logger'; Level = 1, 2, 3 })
        UnexpectedShutdown = Summ (Count-Events 'System' @{ ProviderName = 'Microsoft-Windows-Kernel-Power'; Id = 41 })
        # v3: one GPU reset = one DISTINCT WER report. Windows re-logs event 1001 for the same report on
        # every retry to send it -- ~100 entries per reset measured 2026-09-29 (KOR-104N 6,339 entries =
        # 52 resets) -- so counting entries overstated every PC's GPU problem by two orders of magnitude.
        GpuHang            = Summ ($gpuWer | Group-Object { if ($_.Message -match 'Report Id:\s*([0-9a-fA-F-]{36})') { $Matches[1] } else { "$($_.RecordId)" } } |
                                   ForEach-Object { $_.Group | Sort-Object TimeCreated | Select-Object -First 1 })
        GpuHangLogEntries  = Summ $gpuWer
        # v2 -- early-warning signals, each rising before the failure it predicts:
        ResourceExhaustion = Summ (Count-Events 'System' @{ ProviderName = 'Microsoft-Windows-Resource-Exhaustion-Detector'; Id = 2004 })
        UpdateFailures     = Summ (Count-Events 'System' @{ ProviderName = 'Microsoft-Windows-WindowsUpdateClient'; Id = 20 })
        AppHangs           = Summ (Count-Events 'Application' @{ ProviderName = 'Application Hang'; Id = 1002 })
    }
}

# --- v2: drive health counters -- wear and errors climb BEFORE a drive dies (206-N's SSD threw
# bad blocks three weeks before it vanished). Needs SYSTEM, which the probe runs as.
$reliability = Try-Block 'reliability' {
    @(Get-PhysicalDisk -ErrorAction Stop | ForEach-Object {
        $d = $_
        $r = $d | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue
        [pscustomobject]@{
            Name = $d.FriendlyName; Serial = ([string]$d.SerialNumber).Trim()
            WearPct = if ($r -and $null -ne $r.Wear) { [int]$r.Wear } else { $null }
            TemperatureC = if ($r -and $r.Temperature) { [int]$r.Temperature } else { $null }
            TemperatureMaxC = if ($r -and $r.TemperatureMax) { [int]$r.TemperatureMax } else { $null }
            ReadErrors = if ($r -and $null -ne $r.ReadErrorsTotal) { [long]$r.ReadErrorsTotal } else { $null }
            ReadErrorsUncorrected = if ($r -and $null -ne $r.ReadErrorsUncorrected) { [long]$r.ReadErrorsUncorrected } else { $null }
            WriteErrors = if ($r -and $null -ne $r.WriteErrorsTotal) { [long]$r.WriteErrorsTotal } else { $null }
            PowerOnHours = if ($r -and $null -ne $r.PowerOnHours) { [long]$r.PowerOnHours } else { $null }
        } })
}

# --- v2: what the PC is -- the facts the fleet comparison and change tracking work from
$inventory = Try-Block 'inventory' {
    $csp = Get-CimInstance Win32_ComputerSystemProduct
    $cs = Get-CimInstance Win32_ComputerSystem
    $bios = Get-CimInstance Win32_BIOS
    $cpu = @(Get-CimInstance Win32_Processor)[0]
    # Self-built PCs (the ASUS boards) report "System manufacturer / System Product Name"; the
    # motherboard is the identity there.
    $board = Get-CimInstance Win32_BaseBoard
    $c2r = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Office\ClickToRun\Configuration' -ErrorAction SilentlyContinue
    $appPattern = '^(Autodesk Revit 20\d\d|Revit 20\d\d|AutoCAD 20\d\d|Bluebeam Revu|ETABS|SAFE |CSI |Tekla|SketchUp|Microsoft Access database engine|Webroot|NVIDIA Graphics Driver|Adobe Acrobat|Microsoft Teams)'
    $apps = @(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -and $_.DisplayName -match $appPattern -and -not $_.SystemComponent } |
        Sort-Object DisplayName -Unique | ForEach-Object { [pscustomobject]@{ Name = $_.DisplayName.Trim(); Version = [string]$_.DisplayVersion } })
    [pscustomobject]@{
        Manufacturer = $csp.Vendor; Model = if ($csp.Version -and $csp.Version -notmatch '^(None|To be filled|System Version)') { "$($csp.Version)" } else { "$($csp.Name)" }
        MachineType = $csp.Name; Serial = ([string]$bios.SerialNumber).Trim()
        BoardMaker = ([string]$board.Manufacturer).Trim(); BoardProduct = ([string]$board.Product).Trim()
        BiosVersion = $bios.SMBIOSBIOSVersion; BiosDate = if ($bios.ReleaseDate) { $bios.ReleaseDate.ToString('yyyy-MM-dd') } else { $null }
        Cpu = ($cpu.Name -replace '\s+', ' ').Trim(); Cores = [int]$cpu.NumberOfCores
        RamGB = [math]::Round($cs.TotalPhysicalMemory / 1GB)
        LoggedOnUser = $cs.UserName
        OfficeBuild = if ($c2r) { $c2r.VersionToReport } else { $null }
        OfficeChannel = if ($c2r) { $c2r.CDNBaseUrl -replace '.*/', '' } else { $null }
        Apps = Arr $apps
    }
}

# --- v2: Outlook data files -- an OST heads for its 50 GB ceiling months before it breaks mail
$mailStores = Try-Block 'mailStores' {
    # -Include does not resolve against a path with a mid-path wildcard (the WorkstationOps note,
    # re-learned here): glob each extension directly.
    @(foreach ($ext in '*.ost', '*.pst') {
        Get-ChildItem "C:\Users\*\AppData\Local\Microsoft\Outlook\$ext" -File -ErrorAction SilentlyContinue |
            ForEach-Object { [pscustomobject]@{ Profile = ($_.FullName -split '\\')[2]; Name = $_.Name; GB = [math]::Round($_.Length / 1GB, 2); LastWrite = $_.LastWriteTime.ToString('s') } }
    })
}

# --- v2: boot time -- a machine getting slower to start is the first sign of disk or startup trouble
$boot = Try-Block 'boot' {
    $b = @(Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-Diagnostics-Performance/Operational'; Id = 100 } -MaxEvents 10 -ErrorAction SilentlyContinue |
        ForEach-Object { $x = [xml]$_.ToXml(); [int](($x.Event.EventData.Data | Where-Object Name -eq 'BootTime').'#text') } | Where-Object { $_ -gt 0 })
    if ($b.Count) { [pscustomobject]@{ LastBootMs = $b[0]; MedianBootMs = ($b | Sort-Object)[[int][math]::Floor($b.Count / 2)]; Samples = $b.Count } } else { $null }
}

# --- v2: laptop battery -- full-charge capacity against design capacity
$battery = Try-Block 'battery' {
    # The root\wmi battery classes are empty on the ThinkPads here; Windows' own battery report is
    # the reliable source. Desktops have no Win32_Battery and skip this entirely.
    if (-not (Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue)) { return $null }
    $xmlPath = Join-Path $env:WINDIR ("Temp\kor-battery-{0}.xml" -f [guid]::NewGuid().ToString('N'))
    & powercfg.exe /batteryreport /xml /output $xmlPath | Out-Null
    try {
        $x = [xml](Get-Content $xmlPath -Raw)
        $b = @($x.BatteryReport.Batteries.Battery)[0]
        if ($b -and [int]$b.DesignCapacity -gt 0) {
            [pscustomobject]@{ DesignMWh = [int]$b.DesignCapacity; FullChargeMWh = [int]$b.FullChargeCapacity
                               HealthPct = [int][math]::Round(100.0 * [int]$b.FullChargeCapacity / [int]$b.DesignCapacity); CycleCount = [int]$b.CycleCount }
        } else { $null }
    } finally { Remove-Item $xmlPath -Force -ErrorAction SilentlyContinue }
}
$crashes = Try-Block 'appCrashes' {
    @(Count-Events 'Application' @{ ProviderName = 'Application Error'; Id = 1000 } |
        ForEach-Object { if ($_.Message -match 'Faulting application name: ([^,\s]+)') { [pscustomobject]@{ Process = $Matches[1]; At = $_.TimeCreated } } } |
        Group-Object Process | ForEach-Object {
            [pscustomobject]@{ Process = $_.Name; Count = $_.Count; Last = ($_.Group | Sort-Object At -Descending)[0].At.ToString('s') } })
}

# --- Outlook search indexing (event 36), last 60 days, by the store it names
$outlookIndex = Try-Block 'outlookIndex' {
    # A machine where Outlook never registered its event source rejects the filter outright
    # ("The parameter is incorrect", KOR-306) -- that means no events, not a failed probe.
    if (-not (Get-WinEvent -ListProvider Outlook -ErrorAction SilentlyContinue)) { return @() }
    @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'Outlook'; Id = 36; StartTime = $now.AddDays(-60) } -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.Message -match 'continue for (.+?\.(ost|nst|pst))') { [pscustomobject]@{ Store = $Matches[1]; At = $_.TimeCreated } } } |
        Group-Object Store | ForEach-Object { [pscustomobject]@{ Store = $_.Name; Count = $_.Count; Last = ($_.Group | Sort-Object At -Descending)[0].At.ToString('s') } })
}

# --- Office: the Access Database Engine's down-level shared DLLs vs Click-to-Run's
$office = Try-Block 'office' {
    $down = 'C:\Program Files\Common Files\microsoft shared\Office16\mso20win32client.dll'
    $c2r = 'C:\Program Files\Microsoft Office\root\vfs\ProgramFilesCommonX64\Microsoft Shared\Office16\mso20win32client.dll'
    [pscustomobject]@{
        C2rMso      = if (Test-Path $c2r) { (Get-Item $c2r).VersionInfo.FileVersion } else { $null }
        DownlevelMso = if (Test-Path $down) { (Get-Item $down).VersionInfo.FileVersion } else { $null }
        AccessEngine = Test-Path 'C:\Program Files\Common Files\microsoft shared\Office16\ACEOLEDB.DLL'
    }
}

# --- patching: who schedules it, and when anything last installed
$update = Try-Block 'update' {
    $sm = New-Object -ComObject Microsoft.Update.ServiceManager
    $mu = [bool]($sm.Services | Where-Object { $_.ServiceID -eq '7971f918-a847-4430-9279-4a52d1efe18d' -and $_.IsRegisteredWithAU })
    $au = Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' -ErrorAction SilentlyContinue
    $hist = @((New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher().QueryHistory(0, 50) |
        Where-Object { $_.ResultCode -eq 2 -and $_.Title -notmatch '^9N|Store|Defender|Security Intelligence' } | Sort-Object Date -Descending)
    [pscustomobject]@{
        MicrosoftUpdate = $mu
        NoAutoUpdate    = if ($au) { $au.NoAutoUpdate } else { $null }
        LastInstall     = if ($hist.Count) { $hist[0].Date.ToString('s') } else { $null }
        LastInstallTitle = if ($hist.Count) { $hist[0].Title } else { $null }
        LastInstallBy   = if ($hist.Count) { $hist[0].ClientApplicationID } else { $null }
    }
}

# --- fans: Lenovo BIOS cooling profile (305 and 206-N were loud on Performance mode)
$cooling = Try-Block 'cooling' {
    # Only Lenovo firmware exposes this class; on the ASUS/other boards it is simply absent,
    # which is "not applicable", not a probe failure.
    if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notmatch 'LENOVO') { return 'n/a' }
    $s = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_BiosSetting -ErrorAction Stop |
        Where-Object { $_.CurrentSetting -match '^(IntelligentCoolingPerformanceMode|ThermalMode|FanControl),' } | Select-Object -First 1
    if ($s -and $s.CurrentSetting -match '^[^,]+,([^;]+)') { $Matches[1].Trim() } else { $null }
}

# --- display adapters: after enough GPU hangs a card can drop to "Microsoft Basic Display"
# (KOR-104N / EDMONTON-01 / 213 showed no NVIDIA card at all on 2026-09-28)
$display = Try-Block 'display' {
    @(Get-CimInstance Win32_VideoController | ForEach-Object {
        [pscustomobject]@{ Name = $_.Name; Driver = $_.DriverVersion; ErrorCode = [int]$_.ConfigManagerErrorCode } })
}

# --- residue and remote-access footprint
$residue = Try-Block 'residue' {
    [pscustomobject]@{
        NewformaProfiles = @(Get-ChildItem 'C:\Users\*\AppData\Local\Newforma' -Directory -ErrorAction SilentlyContinue).Count
        NewformaInstalled = @(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -match 'Newforma' } | ForEach-Object DisplayName)
    }
}
$remoteTools = Try-Block 'remoteTools' {
    @(Get-CimInstance Win32_Service | Where-Object { $_.Name -match 'ScreenConnect|TeamViewer|AnyDesk|Splashtop|ncstreamer|Tailscale|LogMeIn|RustDesk|MeshAgent' } |
        ForEach-Object { [pscustomobject]@{ Name = $_.Name; State = $_.State; StartMode = $_.StartMode } })
}

# --- v3: memory modules -- which slot, how big, rated vs the speed it actually runs at. An odd module
# count or unequal channels runs part of the RAM single-channel; two modules on a DDR5 channel drop
# the whole set's speed (206-N, 2026-09-29: 3 x 32 GB, 32 GB on channel A and 64 GB on B, 4800 rated
# running at 3600). Plain values only.
$memory = Try-Block 'memory' {
    @(Get-CimInstance Win32_PhysicalMemory -ErrorAction Stop | ForEach-Object {
        [pscustomobject]@{
            Slot = "$($_.DeviceLocator)"; Bank = "$($_.BankLabel)"; SizeGB = [int][math]::Round($_.Capacity / 1GB)
            RatedMTs = [int]$_.Speed; ConfiguredMTs = [int]$_.ConfiguredClockSpeed
            Maker = "$("$($_.Manufacturer)".Trim())"; Part = "$("$($_.PartNumber)".Trim())" } })
}
$memorySlots = Try-Block 'memorySlots' { [int](@(Get-CimInstance Win32_PhysicalMemoryArray -ErrorAction Stop) | Measure-Object MemoryDevices -Sum).Sum }

# --- data that no backup covers: fixed volumes other than C: holding real data (206-N's D:)
$dataOutside = Try-Block 'dataOutside' {
    @($volumes | Where-Object { $_.Letter -ne 'C' -and ($_.SizeGB - $_.FreeGB) -ge 1 } |
        ForEach-Object { [pscustomobject]@{ Letter = $_.Letter; UsedGB = [math]::Round($_.SizeGB - $_.FreeGB, 1) } })
}

[pscustomobject]@{
    ProbeVersion  = 3
    CollectedAt   = $now.ToString('s')
    Computer      = $env:COMPUTERNAME
    Os            = $osInfo
    PendingReboot = $pending
    Volumes       = Arr $volumes
    PhysicalDisks = Arr $physical
    MissingDisks  = Arr $missingDisks
    OrphanDriveLetters = Arr $orphanLetters
    Events14d     = $events
    AppCrashes14d = Arr $crashes
    OutlookIndex60d = Arr $outlookIndex
    Office        = $office
    Update        = $update
    CoolingMode   = $cooling
    DisplayAdapters = Arr $display
    Residue       = $residue
    RemoteTools   = Arr $remoteTools
    DataOutsideSystemDrive = Arr $dataOutside
    DiskReliability = Arr $reliability
    Inventory     = $inventory
    MailStores    = Arr $mailStores
    Boot          = $boot
    Battery       = $battery
    Memory        = Arr $memory
    MemorySlots   = $memorySlots
    # WMI itself broken: the core CIM classes fail. KOR-213, 2026-09-28 -- every block that
    # touches CIM said "Invalid class". Reported as a fact so a rule can raise it.
    WmiHealthy    = [bool](Try-Block 'wmi' { Get-CimInstance Win32_OperatingSystem -ErrorAction Stop })
    ProbeErrors   = @($errors)
    ProbeMs       = [int]$probeClock.ElapsedMilliseconds
    BlockMs       = [pscustomobject]$timings
}
