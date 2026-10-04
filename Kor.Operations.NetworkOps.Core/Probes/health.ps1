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
        # v10: the drive letters on it and whether Windows boots from it, so a finding says "the data drive D:" and not
        # only a model number (Ian, 2026-10-02: "which disk is this? His system disk? His secondary disk?").
        $n = $_.DeviceId
        $letters = @(Get-Partition -DiskNumber $n -ErrorAction SilentlyContinue | Where-Object { [int][char]$_.DriveLetter -gt 0 } |
            ForEach-Object { [string]$_.DriveLetter }) -join ','
        $disk = Get-Disk -Number $n -ErrorAction SilentlyContinue
        # v11: data partitions (GPT "Basic", MBR "IFS"/"Logical"/"FAT*") over 1 GB with no letter and no folder mount -- data
        # Windows is no longer showing. KOR-208-N, 2026-10-02 15:05: its D: hard drive reset, logged 306 bad blocks, and its
        # 1,863 GB partition lost its volume and letter while the drive itself stayed (so "a drive has disappeared" never
        # fired). Measured on 11 drives across 5 PCs before shipping: 1,863 there, 0 on every other. A blank new drive has 0.
        $parts = @(Get-Partition -DiskNumber $n -ErrorAction SilentlyContinue)
        $unmounted = @($parts | Where-Object { [string]$_.Type -match '^(Basic|IFS|Logical|FAT)' -and [int][char]$_.DriveLetter -eq 0 -and $_.Size -gt 1GB -and
            -not @($_.AccessPaths | Where-Object { $_ -and $_ -notlike '\\?\Volume*' }).Count })
        [pscustomobject]@{ Name = $_.FriendlyName; Media = [string]$_.MediaType; Bus = [string]$_.BusType
            SizeGB = [math]::Round($_.Size / 1GB); Health = [string]$_.HealthStatus; Operational = [string]$_.OperationalStatus
            Letters = $letters; System = [bool]($disk -and ($disk.IsBoot -or $disk.IsSystem))
            UnmountedGB = [math]::Round((($unmounted | Measure-Object Size -Sum).Sum) / 1GB, 1) } })
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
    # WER 1001 fields by position (read, not rendered: .Message over hundreds of entries is the slow part):
    # [2] event name, [5] P1, [16] the report's folder, [19] Report Id.
    $gpuWer = @($wer | Where-Object { $_.Properties.Count -gt 19 -and "$($_.Properties[2].Value)" -eq 'LiveKernelEvent' -and "$($_.Properties[5].Value)".Trim() -eq '141' })
    # v8: a reset is dated by when Windows CREATED its report (the report folder), not by a log entry. A report
    # that cannot be sent stays queued and is re-logged on every retry for months: on 2026-10-01, 6 of the 7 PCs
    # flagged had 0 resets in 14 d -- every "reset" was a 2024-2026 report still in ReportQueue (KOR-217's newest
    # was June). A report whose folder is gone cannot be dated and is not counted (104N: 4 such, all first
    # logged within one second at the window's start). Archived reports were moved there from the queue.
    $gpuReports = @($gpuWer | Group-Object { "$($_.Properties[19].Value)" } | ForEach-Object {
        $first = $_.Group | Sort-Object TimeCreated | Select-Object -First 1
        $store = "$($first.Properties[16].Value)"
        $folder = $null
        foreach ($p in @($store, ($store -replace '\\ReportQueue\\', '\ReportArchive\'))) {
            if ($p -and (Test-Path -LiteralPath $p)) { $folder = Get-Item -LiteralPath $p; break }
        }
        [pscustomobject]@{ ReportId = $_.Name; TimeCreated = if ($folder) { $folder.CreationTime } else { $null }
            Entries = $_.Count; Queued = [bool]($folder -and $folder.FullName -match '\\ReportQueue\\') }
    })
    $gpuNew = @($gpuReports | Where-Object { $_.TimeCreated -and $_.TimeCreated -ge $since14 })
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
        GpuHang            = Summ $gpuNew
        GpuHangLogEntries  = Summ $gpuWer
        # v8: the reports behind the count, newest first -- the evidence a person (or a Claude session) reads first.
        GpuHangStaleReports = @($gpuReports).Count - $gpuNew.Count
        GpuHangReports     = Arr ($gpuReports | Sort-Object { if ($_.TimeCreated) { $_.TimeCreated } else { [datetime]::MinValue } } -Descending | Select-Object -First 12 |
                                   ForEach-Object { [pscustomobject]@{ ReportId = $_.ReportId; Created = if ($_.TimeCreated) { $_.TimeCreated.ToString('s') } else { $null }
                                                                       Entries = $_.Entries; Queued = $_.Queued } })
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
# (KOR-104N / EDMONTON-01 / 213 showed no NVIDIA card at all on 2026-09-28).
# v12: also the resolution each card is DRIVING right now (CurrentHorizontal/VerticalResolution). 0/null = the
# card is driving no display at all -- a headless PC with no monitor and no dummy plug, so KOR Remote (which mirrors
# the physical screen) comes up blank or at a fallback size. A dummy HDMI/DP plug makes it report a real resolution.
$display = Try-Block 'display' {
    @(Get-CimInstance Win32_VideoController | ForEach-Object {
        [pscustomobject]@{ Name = $_.Name; Driver = $_.DriverVersion; ErrorCode = [int]$_.ConfigManagerErrorCode
            Width = [int]$_.CurrentHorizontalResolution; Height = [int]$_.CurrentVerticalResolution } })
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

# --- who is on the PC right now (v4): the console user, whether the console is LOCKED (the lock screen,
# LogonUI.exe, is up while a user is signed in), since when (Security 4800 -- only where lock auditing is on),
# and any remote sessions with their idle time. The console's own idle time is NOT readable from here: only
# the user's session knows it (GetLastInputInfo), and quser reports console idle unreliably -- so the agent
# reads it in the user's session and hands it over as KOR_CONSOLE_IDLE_SECONDS (v5). Run over the network
# there is no agent and no idle time. Read before a disruptive fix: never restart a PC someone is working on
# without being told.
$session = Try-Block 'session' {
    $consoleIdle = if ("$env:KOR_CONSOLE_IDLE_SECONDS" -match '^\d+$') { [int]$env:KOR_CONSOLE_IDLE_SECONDS } else { $null }
    $idleFor = { param([int]$s) if ($s -ge 86400) { '{0} d {1} h' -f [math]::Floor($s / 86400), [math]::Floor(($s % 86400) / 3600) } elseif ($s -ge 3600) { '{0} h {1} min' -f [math]::Floor($s / 3600), [math]::Floor(($s % 3600) / 60) } else { '{0} min' -f [math]::Floor($s / 60) } }
    $console = "$((Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).UserName)"
    $locked = [bool]($console -and (Get-Process -Name LogonUI -ErrorAction SilentlyContinue))
    $lockedSince = $null
    if ($locked) {
        $e = Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4800; StartTime = $now.AddDays(-3) } -MaxEvents 1 -ErrorAction SilentlyContinue
        if ($e) { $lockedSince = $e.TimeCreated.ToString('s') }
    }
    # quser: USERNAME SESSIONNAME ID STATE IDLE-TIME LOGON-TIME; a disconnected session has no SESSIONNAME.
    # v9: through cmd, which swallows its stderr. With nobody signed in quser writes "No User exists for *" to stderr, and
    # `quser 2>$null` under ErrorActionPreference Stop turns that into a terminating error -- "nobody signed in" failed
    # the whole block (probe-incomplete on 6 freshly restarted PCs, 2026-10-02; reproduced on KOR-207, fixed the same run).
    $remote = @(cmd.exe /c 'quser 2>nul' | Select-Object -Skip 1 | ForEach-Object {
        $f = ($_.Trim() -replace '^>', '') -split '\s{2,}'
        if ($f.Count -ge 6) { $u = $f[0]; $name = $f[1]; $state = $f[3]; $idle = $f[4] }
        elseif ($f.Count -eq 5) { $u = $f[0]; $name = ''; $state = $f[2]; $idle = $f[3] }
        else { return }
        if ($name -eq 'console') { return }
        [pscustomobject]@{ User = "$u"; State = "$state"; Idle = "$idle" }
    })
    $state = if ($console) { if ($locked) { 'Locked' } else { 'Active' } } elseif ($remote.Count -gt 0) { 'RemoteOnly' } else { 'Nobody' }
    $who = if ($console) { ($console -split '\\')[-1] } else { '' }
    $summary = switch ($state) {
        # Under two minutes is someone working; past that, say how long the keyboard has been untouched.
        'Active' { "$who · active" + $(if ($null -ne $consoleIdle -and $consoleIdle -ge 120) { ", idle $(& $idleFor $consoleIdle)" } else { '' }) }
        'Locked' { "$who · locked" + $(if ($lockedSince) { " since $(([datetime]$lockedSince).ToString('HH:mm'))" } else { '' }) }
        'RemoteOnly' { 'nobody at the console' }
        default { 'nobody signed in' }
    }
    # quser idle is h:mm, d+h:mm, a bare minute count, or '.'/'none' for none.
    $idleText = { param($i) if ($i -match '^\d+\+\d+:\d+$') { "idle $($i -replace '\+', ' d ') h" } elseif ($i -match '^\d+:\d+$') { "idle $i h" } elseif ($i -match '^\d+$') { "idle $i min" } else { 'not idle' } }
    if ($remote.Count -gt 0) {
        $summary += ' · ' + (($remote | ForEach-Object {
            if ($_.State -match '^Disc') { "$($_.User) signed in but disconnected, $(& $idleText $_.Idle)" } else { "$($_.User) on a remote session, $(& $idleText $_.Idle)" }
        }) -join '; ')
    }
    [pscustomobject]@{ ConsoleUser = $console; State = $state; LockedSince = $lockedSince; IdleSeconds = $(if ($console) { $consoleIdle } else { $null }); Remote = $remote; Summary = $summary }
}

# --- v6: can a magic packet wake it? Three things must all hold (measured fleet-wide 2026-10-01: Fast Startup was
# ON on 29 of 29, which is why PERFORM3 ignored two correct magic packets): the wired NIC armed to wake with its
# PME signal on, Fast Startup off (a "shutdown" with it on is a hybrid hibernation many NICs do not wake from), and
# the firmware allowing it -- readable through WMI on Lenovo only. The wired MAC is what the Wake button sends to.
$wake = Try-Block 'wake' {
    $armed = @(powercfg /devicequery wake_armed)
    $nics = @(Get-NetAdapter -Physical -ErrorAction Stop | Where-Object { $_.MediaType -eq '802.3' -and $_.InterfaceDescription -notmatch 'Wi-?Fi|Wireless|Bluetooth|Virtual' } | ForEach-Object {
        $a = $_
        $pm = Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction SilentlyContinue
        $pme = Get-NetAdapterAdvancedProperty -Name $a.Name -RegistryKeyword EnablePME -ErrorAction SilentlyContinue
        [pscustomobject]@{
            Mac = ($a.MacAddress -replace '-', ':').ToUpperInvariant(); Description = $a.InterfaceDescription; Up = [string]$a.Status -eq 'Up'
            MagicPacket = [bool]($pm -and [string]$pm.WakeOnMagicPacket -eq 'Enabled')
            Armed = [bool]($armed -contains $a.InterfaceDescription)
            Pme = if ($pme) { [string]$pme.DisplayValue } else { $null }
        } })
    $bios = $null
    if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -match 'LENOVO') {
        $s = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_BiosSetting -ErrorAction SilentlyContinue | Where-Object { $_.CurrentSetting -match '^WakeOnLAN,' } | Select-Object -First 1
        if ($s) { $bios = (($s.CurrentSetting -split ';')[0] -split ',', 2)[1] }
    }
    [pscustomobject]@{
        FastStartup = [int](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -ErrorAction SilentlyContinue).HiberbootEnabled
        Nics = Arr $nics
        LenovoWakeOnLan = $bios
    }
}

# --- v7: a PC with no keyboard and no mouse whose display still switches off when idle. Nothing can ever wake that
# display, so a monitor plugged in later stays black while remote sessions work (KOR-210, PERFORM2/3, 2026-10-01:
# "it WILL NOT display anything on a monitor"). OffAfterSeconds 0 = never.
$console = Try-Block 'console' {
    $idx = @(powercfg /q SCHEME_CURRENT SUB_VIDEO VIDEOIDLE | Select-String 'Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)' | ForEach-Object { $_.Matches[0].Groups[1].Value })
    [pscustomobject]@{
        DisplayOffAfterSeconds = if ($idx.Count) { [Convert]::ToInt32($idx[0], 16) } else { $null }
        Keyboards = @(Get-PnpDevice -Class Keyboard -PresentOnly -ErrorAction SilentlyContinue).Count
        Mice = @(Get-PnpDevice -Class Mouse -PresentOnly -ErrorAction SilentlyContinue).Count
    }
}

[pscustomobject]@{
    ProbeVersion  = 12
    Console       = $console
    Wake          = $wake
    Session       = $session
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
