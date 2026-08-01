# Kor.WorkstationOps — remote workstation diagnostics and remediation for the KOR fleet.
#
# Built for the constraint that actually exists on this network: WinRM (5985/5986) is
# blocked to workstations and so are RPC dynamic ports, which kills Get-WinEvent
# -ComputerName, Get-CimInstance -ComputerName, schtasks /s and reg.exe /remote.
#
# What still works, and what every function here is built on:
#   * c$ / ADMIN$ over 445      — read + write file access
#   * sc.exe \\host             — service control via the svcctl named pipe
#   * reg.exe \\host            — only while RemoteRegistry is running (it ships Disabled)
#
# If the WinRM GPO lands (see docs/KOR-WinRM-GPO-Plan), these keep working unchanged —
# they just stop being the only option.

Set-StrictMode -Version Latest

$script:StartTypeMap = @{ '2' = 'auto'; '3' = 'demand'; '4' = 'disabled' }

function Resolve-KorAdminShare {
    param([Parameter(Mandatory)][string]$ComputerName, [string]$Path = '')
    if ($Path) { "\\$ComputerName\c`$\$($Path.TrimStart('\'))" } else { "\\$ComputerName\c`$" }
}

function Invoke-KorSc {
    <#  Thin wrapper so every sc.exe call is shaped the same way and failures are visible. #>
    param([Parameter(Mandatory)][string]$ComputerName, [Parameter(Mandatory)][string[]]$Arguments)
    $out = & sc.exe "\\$ComputerName" @Arguments 2>&1
    [PSCustomObject]@{ ExitCode = $LASTEXITCODE; Output = ($out | Out-String) }
}

function Get-KorServiceState {
    param([Parameter(Mandatory)][string]$ComputerName, [Parameter(Mandatory)][string]$Name)
    $r = Invoke-KorSc -ComputerName $ComputerName -Arguments @('query', $Name)
    if ($r.Output -match 'STATE\s+:\s+\d+\s+(\w+)') { $Matches[1] } else { 'UNKNOWN' }
}

function Wait-KorServiceState {
    <#  Poll for a target state. Never blind-sleeps past the goal — matches the deploy
        discipline of confirming Stopped before touching files. #>
    param(
        [Parameter(Mandatory)][string]$ComputerName,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet('RUNNING', 'STOPPED')][string]$Target,
        [int]$TimeoutSeconds = 60
    )
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do {
        $state = Get-KorServiceState -ComputerName $ComputerName -Name $Name
        if ($state -eq $Target) { return $true }
        Start-Sleep -Seconds 2
    } while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    return (Get-KorServiceState -ComputerName $ComputerName -Name $Name) -eq $Target
}

function Test-KorWorkstationChannel {
    <#
    .SYNOPSIS
        Establish which remote-management channels are open before assuming any of them.
    .EXAMPLE
        'KOR-206-N','KOR-207' | Test-KorWorkstationChannel
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)][string[]]$ComputerName)

    process {
        foreach ($cn in $ComputerName) {
            $online = Test-Connection -ComputerName $cn -Count 1 -Quiet -ErrorAction SilentlyContinue
            if (-not $online) {
                [PSCustomObject]@{ ComputerName = $cn; Online = $false; AdminShare = $false
                    AdminWrite = $false; ServiceControl = $false; RemoteRegistry = 'n/a'; WinRM = $false; Rdp = $false }
                continue
            }

            $share = Test-Path (Resolve-KorAdminShare $cn) -ErrorAction SilentlyContinue
            $write = $false
            if ($share) {
                $probe = Resolve-KorAdminShare $cn "Windows\Temp\_kor_channel_probe.tmp"
                try {
                    Set-Content -Path $probe -Value 'probe' -ErrorAction Stop
                    Remove-Item $probe -Force -ErrorAction SilentlyContinue
                    $write = $true
                } catch { $write = $false }
            }

            $svc = (Invoke-KorSc -ComputerName $cn -Arguments @('query', 'wsearch')).Output -match 'STATE'
            $rr = if ($svc) { Get-KorServiceState -ComputerName $cn -Name 'remoteregistry' } else { 'unknown' }
            $winrm = (Test-NetConnection -ComputerName $cn -Port 5985 -WarningAction SilentlyContinue).TcpTestSucceeded
            $rdp = (Test-NetConnection -ComputerName $cn -Port 3389 -WarningAction SilentlyContinue).TcpTestSucceeded

            [PSCustomObject]@{
                ComputerName = $cn; Online = $true; AdminShare = $share; AdminWrite = $write
                ServiceControl = $svc; RemoteRegistry = $rr; WinRM = $winrm; Rdp = $rdp
            }
        }
    }
}

function Use-KorRemoteRegistry {
    <#
    .SYNOPSIS
        Run a scriptblock with RemoteRegistry temporarily available, then always put it back.
    .DESCRIPTION
        RemoteRegistry ships Disabled on these machines, which is the correct posture given
        the mailbox-compromise history. This starts it, runs $Body, and restores BOTH the
        original start type and run state in a finally block so an exception mid-body cannot
        leave the service exposed.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ComputerName,
        [Parameter(Mandatory)][scriptblock]$Body
    )

    $qc = (Invoke-KorSc -ComputerName $ComputerName -Arguments @('qc', 'remoteregistry')).Output
    $origStart = if ($qc -match 'START_TYPE\s+:\s+(\d)') { $script:StartTypeMap[$Matches[1]] } else { 'disabled' }
    $origState = Get-KorServiceState -ComputerName $ComputerName -Name 'remoteregistry'

    try {
        if ($origStart -eq 'disabled') {
            Invoke-KorSc -ComputerName $ComputerName -Arguments @('config', 'remoteregistry', 'start=', 'demand') | Out-Null
        }
        if ($origState -ne 'RUNNING') {
            Invoke-KorSc -ComputerName $ComputerName -Arguments @('start', 'remoteregistry') | Out-Null
            if (-not (Wait-KorServiceState -ComputerName $ComputerName -Name 'remoteregistry' -Target RUNNING -TimeoutSeconds 30)) {
                throw "RemoteRegistry would not start on $ComputerName"
            }
        }
        & $Body
    }
    finally {
        if ($origState -ne 'RUNNING') {
            Invoke-KorSc -ComputerName $ComputerName -Arguments @('stop', 'remoteregistry') | Out-Null
        }
        if ($origStart -eq 'disabled') {
            Invoke-KorSc -ComputerName $ComputerName -Arguments @('config', 'remoteregistry', 'start=', 'disabled') | Out-Null
        }
        Write-Verbose "RemoteRegistry on ${ComputerName} restored to start=$origStart state=$origState"
    }
}

function Get-KorWorkstationEvent {
    <#
    .SYNOPSIS
        Pull an event log off a workstation and parse it locally.
    .DESCRIPTION
        The workaround for RPC being blocked: copy the .evtx over c$ and read it with
        Get-WinEvent -Path. Slower than remote querying but it is the only thing that works,
        and it gives full fidelity including provider-specific events like Outlook's.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ComputerName,
        [ValidateSet('Application', 'System', 'Security')][string]$LogName = 'Application',
        [int]$Days = 14,
        [string]$CachePath = (Join-Path $env:TEMP 'KorWorkstationOps'),
        [switch]$Refresh
    )

    if (-not (Test-Path $CachePath)) { New-Item -ItemType Directory -Path $CachePath -Force | Out-Null }
    $local = Join-Path $CachePath "$ComputerName-$LogName.evtx"

    if ($Refresh -or -not (Test-Path $local)) {
        $src = Resolve-KorAdminShare $ComputerName "Windows\System32\winevt\Logs\$LogName.evtx"
        Copy-Item -Path $src -Destination $local -Force -ErrorAction Stop
    }

    $cutoff = (Get-Date).AddDays(-$Days)
    Get-WinEvent -Path $local -ErrorAction SilentlyContinue |
        Where-Object { $_.TimeCreated -gt $cutoff }
}

function Get-KorOutlookAddin {
    <#
    .SYNOPSIS
        Reconstruct the Outlook add-in inventory, with per-add-in load times, from event 45.
    .DESCRIPTION
        Outlook logs every add-in it loads along with Boot Time in milliseconds. That makes
        slow add-ins self-evident without touching the machine — a 500ms add-in next to a
        set of 0-16ms ones is the one costing the user their startup.
        Also surfaces event 59, where Outlook's resiliency engine disabled an add-in for
        causing a crash. That event is the single highest-signal Outlook diagnostic there is.

        CAVEAT: this reports what Outlook logged at its LAST LAUNCH, not the current registry.
        After Set-KorOutlookAddinState the LoadBehavior here stays stale until the user
        restarts Outlook. To confirm a change actually landed, read the registry:
            Use-KorRemoteRegistry -ComputerName X -Body {
                reg.exe query "\\X\HKLM\SOFTWARE\Microsoft\Office\Outlook\Addins\<ProgID>" /v LoadBehavior }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ComputerName,
        [int]$Days = 14,
        [switch]$Refresh
    )

    $events = Get-KorWorkstationEvent -ComputerName $ComputerName -Days $Days -Refresh:$Refresh |
        Where-Object { $_.ProviderName -eq 'Outlook' }

    $latest = $events | Where-Object Id -eq 45 | Sort-Object TimeCreated -Descending | Select-Object -First 1
    if ($latest) {
        $flat = $latest.Message -replace '\s+', ' '
        # Split on 'Name: ' boundaries, then pull the fields out of each add-in block.
        $blocks = [regex]::Split($flat, '(?=Name: )') | Where-Object { $_ -match 'ProgID:' }
        foreach ($b in $blocks) {
            [PSCustomObject]@{
                ComputerName = $ComputerName
                Observed     = $latest.TimeCreated
                Name         = if ($b -match 'Name: (.+?) Description:') { $Matches[1].Trim() } else { '?' }
                ProgID       = if ($b -match 'ProgID: (\S+)') { $Matches[1] } else { '?' }
                LoadBehavior = if ($b -match 'Load Behavior: (\d+)') { [int]$Matches[1] } else { $null }
                Scope        = if ($b -match 'HKLM: (\d)') { if ($Matches[1] -eq '1') { 'HKLM' } else { 'HKCU' } } else { '?' }
                BootMs       = if ($b -match 'Boot Time \(Milliseconds\): (\d+)') { [int]$Matches[1] } else { $null }
                Location     = if ($b -match 'Location: (.+?) Boot Time') { $Matches[1].Trim() } else { '?' }
            }
        }
    }

    foreach ($d in ($events | Where-Object Id -in 57, 58, 59)) {
        $flat = $d.Message -replace '\s+', ' '
        [PSCustomObject]@{
            ComputerName = $ComputerName
            Observed     = $d.TimeCreated
            Name         = if ($flat -match 'Name: (.+?) Description:') { $Matches[1].Trim() } else { '?' }
            ProgID       = if ($flat -match 'ProgID: (\S+)') { $Matches[1] } else { '?' }
            LoadBehavior = $null
            Scope        = 'DISABLED-BY-OUTLOOK'
            BootMs       = if ($flat -match 'Time Taken \(Milliseconds\): (\d+)') { [int]$Matches[1] } else { $null }
            Location     = if ($flat -match 'Disable Reason: (.+?) Policy') { $Matches[1].Trim() } else { '?' }
        }
    }
}

function Get-KorOutlookStore {
    <#
    .SYNOPSIS
        Per-user Outlook data files, plus the search-indexing failures that make Outlook feel slow.
    .DESCRIPTION
        Reports OST/NST size per profile and surfaces Outlook event 36 ("Search cannot complete
        the indexing of your Outlook data"). Size alone is a poor predictor — a 45GB OST can be
        healthy while a 12GB one crawls — so the index errors and duplicate stores matter more.
        A store named like 'user(2).nst' means Outlook could not open the original: profile damage.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ComputerName, [int]$Days = 14, [switch]$Refresh)

    $root = Resolve-KorAdminShare $ComputerName 'Users'
    # -Include does not resolve reliably against a UNC path with a mid-path wildcard,
    # so glob each extension directly instead.
    $stores = @('*.ost', '*.nst') | ForEach-Object {
        Get-ChildItem "$root\*\AppData\Local\Microsoft\Outlook\$_" -ErrorAction SilentlyContinue
    }

    $indexErrors = Get-KorWorkstationEvent -ComputerName $ComputerName -Days $Days -Refresh:$Refresh |
        Where-Object { $_.ProviderName -eq 'Outlook' -and $_.Id -eq 36 }

    $sharePrefix = Resolve-KorAdminShare $ComputerName

    foreach ($s in $stores) {
        $profile = ($s.FullName -split '\\')[5]
        $dup = $s.BaseName -match '\(\d+\)$'
        # Match the event on the FULL local path, not the file name. Two Windows profiles
        # for the same person hold identically-named stores (markb + 'mark bakhtavar' both
        # have markb@korstructural.com.ost), so name matching attributes one error to both.
        $localPath = 'C:' + $s.FullName.Substring($sharePrefix.Length)
        $errs = @($indexErrors | Where-Object { $_.Message -like "*$localPath*" })
        [PSCustomObject]@{
            ComputerName  = $ComputerName
            Profile       = $profile
            Store         = $s.Name
            GB            = [math]::Round($s.Length / 1GB, 2)
            LastWrite     = $s.LastWriteTime
            DuplicateStore = $dup
            # Abandoned profiles keep their old stores forever. A duplicate last written
            # three years ago is residue to clean up, not a live fault — the distinction
            # decides whether a machine needs attention today.
            AgeDays       = [int]((Get-Date) - $s.LastWriteTime).TotalDays
            IndexErrors   = $errs.Count
            # Guard the empty case: a healthy store has no index errors and .TimeCreated
            # on $null throws under StrictMode.
            LastIndexError = if ($errs.Count) { ($errs | Sort-Object TimeCreated -Descending)[0].TimeCreated } else { $null }
        }
    }
}

function Get-KorOfficeHealth {
    <#
    .SYNOPSIS
        Office build, and the Access Database Engine / C2R shared-DLL conflict.
    .DESCRIPTION
        The Access Database Engine redistributable installs its own down-level copies of the
        MSO shared DLLs into Common Files\microsoft shared\Office16. Click-to-Run Office can
        bind to those instead of its own, and the result is a crash in mso20win32client.dll.
        This reports both versions side by side so the skew is obvious.

        Do NOT remove that folder to fix the skew — it belongs to a registered MSI product
        and carries the ACE OLEDB providers that Excel/Access data access depends on.
        Update the redistributable instead.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ComputerName)

    $sharedDll = Resolve-KorAdminShare $ComputerName 'Program Files\Common Files\microsoft shared\Office16\mso20win32client.dll'
    $c2rDll = Resolve-KorAdminShare $ComputerName 'Program Files\Microsoft Office\root\vfs\ProgramFilesCommonX64\Microsoft Shared\Office16\mso20win32client.dll'
    $outlook = Resolve-KorAdminShare $ComputerName 'Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE'
    $aceDir = Resolve-KorAdminShare $ComputerName 'Program Files\Common Files\microsoft shared\Office16'

    $shared = if (Test-Path $sharedDll) { Get-Item $sharedDll } else { $null }
    $c2r = if (Test-Path $c2rDll) { Get-Item $c2rDll } else { $null }
    $olk = if (Test-Path $outlook) { Get-Item $outlook } else { $null }
    $hasAce = Test-Path (Join-Path $aceDir 'ACEOLEDB.DLL')

    [PSCustomObject]@{
        ComputerName      = $ComputerName
        OutlookBuild      = if ($olk) { $olk.VersionInfo.FileVersion } else { $null }
        OutlookUpdated    = if ($olk) { $olk.LastWriteTime } else { $null }
        C2rSharedMso      = if ($c2r) { $c2r.VersionInfo.FileVersion } else { $null }
        DownlevelMso      = if ($shared) { $shared.VersionInfo.FileVersion } else { $null }
        DownlevelMsoDate  = if ($shared) { $shared.LastWriteTime } else { $null }
        AccessEnginePresent = $hasAce
        # The conflict only bites when both exist and disagree.
        SharedDllConflict = [bool]($shared -and $c2r -and $shared.VersionInfo.FileVersion -ne $c2r.VersionInfo.FileVersion)
    }
}

function Set-KorOutlookAddinState {
    <#
    .SYNOPSIS
        Set an Outlook add-in's LoadBehavior, returning the prior value for rollback.
    .PARAMETER LoadBehavior
        0 = disabled, 2 = load on demand, 3 = load at startup.
    .EXAMPLE
        Set-KorOutlookAddinState -ComputerName KOR-206-N -ProgID UCAddin.LyncAddin.1 -LoadBehavior 0
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory)][string]$ComputerName,
        # Accepts several at once deliberately: each RemoteRegistry start/stop cycle costs
        # real time over the VPN, so all ProgIDs share one cycle rather than one apiece.
        [Parameter(Mandatory)][string[]]$ProgID,
        [Parameter(Mandatory)][ValidateSet(0, 2, 3)][int]$LoadBehavior,
        [ValidateSet('HKLM', 'HKCU')][string]$Hive = 'HKLM'
    )

    Use-KorRemoteRegistry -ComputerName $ComputerName -Body {
        foreach ($id in $ProgID) {
            $key = "\\$ComputerName\$Hive\SOFTWARE\Microsoft\Office\Outlook\Addins\$id"
            $before = & reg.exe query $key /v LoadBehavior 2>&1 | Select-String 'LoadBehavior'
            $prior = if ($before -match '0x(\w+)') { [Convert]::ToInt32($Matches[1], 16) } else { $null }

            if ($null -eq $prior) {
                Write-Warning "$id not present on $ComputerName — skipped"
                continue
            }

            if ($PSCmdlet.ShouldProcess("$ComputerName\$id", "set LoadBehavior $prior -> $LoadBehavior")) {
                & reg.exe add $key /v LoadBehavior /t REG_DWORD /d $LoadBehavior /f 2>&1 | Out-Null
                $after = & reg.exe query $key /v LoadBehavior 2>&1 | Select-String 'LoadBehavior'
                $now = if ($after -match '0x(\w+)') { [Convert]::ToInt32($Matches[1], 16) } else { $null }
            } else { $now = $prior }

            [PSCustomObject]@{
                ComputerName = $ComputerName; ProgID = $id; Hive = $Hive
                PriorValue = $prior; NewValue = $now
                RollbackCommand = "Set-KorOutlookAddinState -ComputerName $ComputerName -ProgID $id -LoadBehavior $prior"
            }
        }
    }
}

function Invoke-KorSearchIndexRebuild {
    <#
    .SYNOPSIS
        Force a full Windows Search catalog rebuild — the fix for Outlook event 36.
    .DESCRIPTION
        Uses Microsoft's supported rebuild flag (SetupCompletedSuccessfully = 0) rather than
        deleting catalog files, so Windows tears down and recreates its own state cleanly.
        Windows flips the flag back to 1 itself once the rebuild initialises.

        SIDE EFFECT: this rebuilds the entire machine index, not just Outlook. File search and
        Start-menu search return incomplete results until it finishes — hours on a large
        mailbox. Run it outside the user's working day.
    #>
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param([Parameter(Mandatory)][string]$ComputerName)

    $catalog = Resolve-KorAdminShare $ComputerName 'ProgramData\Microsoft\Search\Data\Applications\Windows'
    $before = [math]::Round((Get-ChildItem $catalog -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object Length -Sum).Sum / 1MB, 1)

    if (-not $PSCmdlet.ShouldProcess($ComputerName, "rebuild Windows Search catalog (currently ${before}MB)")) { return }

    Invoke-KorSc -ComputerName $ComputerName -Arguments @('stop', 'wsearch') | Out-Null
    if (-not (Wait-KorServiceState -ComputerName $ComputerName -Name 'wsearch' -Target STOPPED -TimeoutSeconds 60)) {
        throw "wsearch would not stop on $ComputerName — aborting before touching the catalog"
    }

    Use-KorRemoteRegistry -ComputerName $ComputerName -Body {
        & reg.exe add "\\$ComputerName\HKLM\SOFTWARE\Microsoft\Windows Search" /v SetupCompletedSuccessfully /t REG_DWORD /d 0 /f 2>&1 | Out-Null
    }

    # Windows Search will silently IGNORE the rebuild flag if the service is restarted too
    # soon after the registry write — the flag stays 0, the old catalog survives, and the
    # whole call looks like it succeeded. Observed on KOR-207 2026-07-28. So: start, then
    # verify the catalog actually collapsed, and cycle the service again if it did not.
    $collapsed = $false
    $attempts = 0
    while (-not $collapsed -and $attempts -lt 3) {
        $attempts++
        Invoke-KorSc -ComputerName $ComputerName -Arguments @('start', 'wsearch') | Out-Null
        $running = Wait-KorServiceState -ComputerName $ComputerName -Name 'wsearch' -Target RUNNING -TimeoutSeconds 60

        # A real rebuild drops the catalog to near-nothing within ~30s of the service coming up.
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt 45) {
            Start-Sleep -Seconds 10
            $now = (Get-ChildItem $catalog -Recurse -File -ErrorAction SilentlyContinue |
                Measure-Object Length -Sum).Sum / 1MB
            if ($now -lt ($before * 0.5)) { $collapsed = $true; break }
        }

        if (-not $collapsed -and $attempts -lt 3) {
            Write-Verbose "Rebuild flag not honoured on attempt $attempts — cycling wsearch again"
            Invoke-KorSc -ComputerName $ComputerName -Arguments @('stop', 'wsearch') | Out-Null
            Wait-KorServiceState -ComputerName $ComputerName -Name 'wsearch' -Target STOPPED -TimeoutSeconds 60 | Out-Null
            Start-Sleep -Seconds 15   # let the flag settle before trying again
        }
    }

    $after = [math]::Round((Get-ChildItem $catalog -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object Length -Sum).Sum / 1MB, 1)

    if (-not $collapsed) {
        Write-Warning "$ComputerName : catalog did not collapse after $attempts attempts (${before}MB -> ${after}MB). The rebuild did NOT run."
    }

    [PSCustomObject]@{
        ComputerName = $ComputerName
        ServiceRunning = $running
        CatalogMbBefore = $before
        CatalogMbAfter = $after
        Attempts = $attempts
        # Only true on a real collapse — never infer success from a trivial size change.
        RebuildConfirmed = $collapsed
    }
}

function Get-KorWorkstationHealth {
    <#
    .SYNOPSIS
        One-shot health report for a workstation — the whole KOR-206-N investigation in one call.
    .EXAMPLE
        Get-KorWorkstationHealth -ComputerName KOR-206-N | Format-List
    .EXAMPLE
        $fleet | Get-KorWorkstationHealth | Where-Object { $_.Findings }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][string[]]$ComputerName,
        [int]$Days = 14,
        # A store not written within this many days belongs to an abandoned profile.
        [int]$StaleAfterDays = 30,
        [switch]$Refresh
    )

    process {
        foreach ($cn in $ComputerName) {
            $channel = Test-KorWorkstationChannel -ComputerName $cn
            if (-not $channel.AdminShare) {
                [PSCustomObject]@{ ComputerName = $cn; Reachable = $false; Findings = @('unreachable or no c$ access') }
                continue
            }

            $office = Get-KorOfficeHealth -ComputerName $cn
            $stores = @(Get-KorOutlookStore -ComputerName $cn -Days $Days -Refresh:$Refresh)
            $addins = @(Get-KorOutlookAddin -ComputerName $cn -Days $Days)
            $crashes = @(Get-KorWorkstationEvent -ComputerName $cn -Days $Days |
                Where-Object { $_.Id -in 1000, 1002 -and $_.ProviderName -in 'Application Error', 'Application Hang' })

            $findings = [Collections.Generic.List[string]]::new()
            if ($office.SharedDllConflict) {
                $findings.Add("Office shared-DLL skew: down-level $($office.DownlevelMso) ($($office.DownlevelMsoDate.ToString('yyyy-MM-dd'))) vs C2R $($office.C2rSharedMso)" +
                    $(if ($office.AccessEnginePresent) { ' — owned by Access Database Engine, update it, do not delete' } else { '' }))
            }
            foreach ($s in $stores | Where-Object { $_.IndexErrors -gt 0 }) {
                $findings.Add("Search indexing failing for $($s.Profile)\$($s.Store) ($($s.IndexErrors) errors, last $($s.LastIndexError))")
            }
            # Group by profile so one underlying problem yields one finding rather than
            # 31, and separate live recreation from long-dead residue. Reporting a 2023
            # duplicate cluster as active damage sends you to the wrong machine.
            foreach ($g in ($stores | Where-Object DuplicateStore | Group-Object Profile)) {
                $live = @($g.Group | Where-Object { $_.AgeDays -le $StaleAfterDays } | Sort-Object AgeDays)
                $mb = [math]::Round((($g.Group | Measure-Object GB -Sum).Sum * 1024))
                if ($live.Count) {
                    $findings.Add("LIVE: Outlook recreating stores for $($g.Name) — $($live.Count) duplicate(s) within $StaleAfterDays days, newest $($live[0].Store)")
                } else {
                    $oldest = ($g.Group | Sort-Object AgeDays -Descending)[0]
                    $newest = ($g.Group | Sort-Object AgeDays)[0]
                    $findings.Add("CLEANUP: $($g.Count) stale duplicate stores for $($g.Name), ${mb}MB, last touched $($newest.AgeDays)d ago (span $($oldest.AgeDays)-$($newest.AgeDays)d) — residue, not a live fault")
                }
            }
            foreach ($a in $addins | Where-Object { $_.Scope -eq 'DISABLED-BY-OUTLOOK' }) {
                $findings.Add("Outlook disabled add-in '$($a.Name)' on $($a.Observed.ToString('yyyy-MM-dd')): $($a.Location)")
            }
            foreach ($a in $addins | Where-Object { $_.BootMs -ge 400 -and $_.Scope -ne 'DISABLED-BY-OUTLOOK' }) {
                $findings.Add("Slow add-in '$($a.Name)' costs $($a.BootMs)ms every launch")
            }
            $repeat = $crashes | Group-Object { ($_.Properties.Value)[0] } | Where-Object Count -ge 3
            foreach ($r in $repeat) { $findings.Add("Repeat faulting process '$($r.Name)' x$($r.Count) in $Days days") }

            [PSCustomObject]@{
                ComputerName = $cn
                Reachable    = $true
                Channel      = $channel
                Office       = $office
                Stores       = $stores
                AddIns       = $addins
                CrashCount   = $crashes.Count
                Findings     = $findings.ToArray()
            }
        }
    }
}

Export-ModuleMember -Function Test-KorWorkstationChannel, Use-KorRemoteRegistry, Get-KorWorkstationEvent,
    Get-KorOutlookAddin, Get-KorOutlookStore, Get-KorOfficeHealth, Set-KorOutlookAddinState,
    Invoke-KorSearchIndexRebuild, Get-KorWorkstationHealth, Get-KorServiceState, Wait-KorServiceState,
    Invoke-KorSc, Resolve-KorAdminShare
