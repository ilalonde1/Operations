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

# ---------------------------------------------------------------------------
# Hardware profile
#
# WMI would hand all of this over in one call, but Get-CimInstance -ComputerName
# needs WinRM — the exact thing this network blocks. The mssmbios driver mirrors
# the raw SMBIOS tables into the registry, so the same firmware data is reachable
# over reg.exe. That includes per-DIMM part numbers and populated-slot counts,
# which is the difference between "32GB installed" and "32GB in 4 of 4 slots,
# nowhere left to grow" — the second one is the answer to an upgrade question.
#
# PSU wattage is NOT in SMBIOS. No remote channel exposes it. It is always eyes-on.
# ---------------------------------------------------------------------------

$script:SmbiosChassisType = @{
    1 = 'Other'; 2 = 'Unknown'; 3 = 'Desktop'; 4 = 'Low Profile Desktop'; 5 = 'Pizza Box'
    6 = 'Mini Tower'; 7 = 'Tower'; 8 = 'Portable'; 9 = 'Laptop'; 10 = 'Notebook'
    11 = 'Hand Held'; 12 = 'Docking Station'; 13 = 'All in One'; 14 = 'Sub Notebook'
    15 = 'Space-saving'; 16 = 'Lunch Box'; 17 = 'Main Server Chassis'; 18 = 'Expansion Chassis'
    23 = 'Rack Mount Chassis'; 24 = 'Sealed-case PC'; 25 = 'Multi-system'; 30 = 'Tablet'
    31 = 'Convertible'; 32 = 'Detachable'; 34 = 'Mini PC'; 35 = 'Stick PC'
}
$script:SmbiosMemoryType = @{
    1 = 'Other'; 2 = 'Unknown'; 3 = 'DRAM'; 17 = 'SDRAM'; 18 = 'SGRAM'; 20 = 'DDR'
    21 = 'DDR2'; 24 = 'DDR3'; 26 = 'DDR4'; 34 = 'DDR5'; 35 = 'LPDDR5'
}
$script:SmbiosFormFactor = @{
    1 = 'Other'; 2 = 'Unknown'; 8 = 'DIMM'; 9 = 'TSOP'; 12 = 'SODIMM'; 13 = 'RIMM'; 15 = 'FB-DIMM'
}

function Get-SmbiosString {
    <#  SMBIOS strings are 1-based indexes into the struct's own string table; 0 means absent. #>
    param([Parameter(Mandatory)]$Structure, [Parameter(Mandatory)][int]$Offset)
    if ($Offset -ge $Structure.Data.Length) { return $null }
    $i = $Structure.Data[$Offset]
    if ($i -eq 0 -or $i -gt $Structure.Strings.Count) { return $null }
    $Structure.Strings[$i - 1].Trim()
}

function ConvertFrom-KorSmbios {
    <#
    .SYNOPSIS
        Parse a raw SMBIOS blob into system, board, chassis, CPU and per-DIMM detail.
    .DESCRIPTION
        Input is the bytes of HKLM\SYSTEM\CurrentControlSet\Services\mssmbios\Data\SMBiosData:
        an 8-byte registry header (major/minor at [1]/[2], table length DWORD at [4]) followed
        by the SMBIOS table proper. Each structure is type/length/handle + a formatted area +
        a double-null-terminated string table.

        Deliberately a pure function over a byte array — no network, no registry — so it can be
        regression-tested against a captured fixture without a machine to point at.
    .EXAMPLE
        $bytes = [byte[]]::new(...) ; ConvertFrom-KorSmbios -Bytes $bytes
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][byte[]]$Bytes)

    if ($Bytes.Length -lt 12) { throw 'SMBIOS blob is too short to contain a header and one structure.' }

    $len = [BitConverter]::ToUInt32($Bytes, 4)
    $end = [Math]::Min(8 + $len, $Bytes.Length)
    $p = 8
    $structs = [Collections.Generic.List[object]]::new()

    while ($p + 4 -le $end) {
        $type = $Bytes[$p]
        $flen = $Bytes[$p + 1]
        # A formatted area shorter than the 4-byte header means the table is corrupt; stop
        # rather than walk off into arbitrary bytes and report confident nonsense.
        if ($flen -lt 4 -or $p + $flen -gt $end) { break }

        $data = New-Object byte[] $flen
        [Array]::Copy($Bytes, $p, $data, 0, $flen)

        $sp = $p + $flen
        $strings = [Collections.Generic.List[string]]::new()
        if ($sp + 1 -lt $end -and $Bytes[$sp] -eq 0 -and $Bytes[$sp + 1] -eq 0) {
            $sp += 2
        } else {
            while ($sp -lt $end) {
                $start = $sp
                while ($sp -lt $end -and $Bytes[$sp] -ne 0) { $sp++ }
                $strings.Add([Text.Encoding]::ASCII.GetString($Bytes, $start, $sp - $start))
                $sp++
                if ($sp -lt $end -and $Bytes[$sp] -eq 0) { $sp++; break }
            }
        }

        $structs.Add([PSCustomObject]@{ Type = $type; Data = $data; Strings = $strings })
        $p = $sp
        if ($type -eq 127) { break }   # end-of-table
    }

    $pick = { param($t) $structs | Where-Object { $_.Type -eq $t } | Select-Object -First 1 }

    $sys = & $pick 1
    $board = & $pick 2
    $chassis = & $pick 3
    $cpu = & $pick 4
    $array = & $pick 16

    $dimms = foreach ($d in ($structs | Where-Object { $_.Type -eq 17 })) {
        $raw = [BitConverter]::ToUInt16($d.Data, 0x0C)
        # 0 = slot empty. 0x7FFF = "see the extended DWORD". Bit 15 set = the value is KB, not MB.
        $sizeMb = if ($raw -eq 0) { 0 }
                  elseif ($raw -eq 0x7FFF -and $d.Data.Length -ge 0x20) { [BitConverter]::ToUInt32($d.Data, 0x1C) }
                  elseif ($raw -band 0x8000) { ($raw -band 0x7FFF) / 1024 }
                  else { [int]$raw }
        [PSCustomObject]@{
            Locator       = Get-SmbiosString $d 0x10
            Bank          = Get-SmbiosString $d 0x11
            SizeMB        = $sizeMb
            Populated     = ($sizeMb -gt 0)
            Type          = if ($d.Data.Length -gt 0x12 -and $script:SmbiosMemoryType.ContainsKey([int]$d.Data[0x12])) { $script:SmbiosMemoryType[[int]$d.Data[0x12]] } else { 'Unknown' }
            FormFactor    = if ($d.Data.Length -gt 0x0E -and $script:SmbiosFormFactor.ContainsKey([int]$d.Data[0x0E])) { $script:SmbiosFormFactor[[int]$d.Data[0x0E]] } else { 'Unknown' }
            RatedMTs      = if ($d.Data.Length -ge 0x17) { [BitConverter]::ToUInt16($d.Data, 0x15) } else { $null }
            ConfiguredMTs = if ($d.Data.Length -ge 0x22) { [BitConverter]::ToUInt16($d.Data, 0x20) } else { $null }
            Manufacturer  = Get-SmbiosString $d 0x17
            PartNumber    = Get-SmbiosString $d 0x1A
        }
    }
    $dimms = @($dimms)

    [PSCustomObject]@{
        Version   = "$($Bytes[1]).$($Bytes[2])"
        System    = if ($sys) { [PSCustomObject]@{
                        Manufacturer = Get-SmbiosString $sys 0x04
                        Product      = Get-SmbiosString $sys 0x05
                        Family       = if ($sys.Data.Length -gt 0x1A) { Get-SmbiosString $sys 0x1A } else { $null }
                    } } else { $null }
        Board     = if ($board) { [PSCustomObject]@{
                        Manufacturer = Get-SmbiosString $board 0x04
                        Product      = Get-SmbiosString $board 0x05
                    } } else { $null }
        Chassis   = if ($chassis) {
                        $ct = [int]($chassis.Data[0x05] -band 0x7F)   # bit 7 is "lock present", not part of the type
                        [PSCustomObject]@{
                            Manufacturer = Get-SmbiosString $chassis 0x04
                            TypeCode     = $ct
                            Type         = if ($script:SmbiosChassisType.ContainsKey($ct)) { $script:SmbiosChassisType[$ct] } else { "Unknown($ct)" }
                        } } else { $null }
        Processor = if ($cpu) { [PSCustomObject]@{
                        Version     = Get-SmbiosString $cpu 0x10
                        Socket      = Get-SmbiosString $cpu 0x04
                        MaxSpeedMHz = if ($cpu.Data.Length -ge 0x16) { [BitConverter]::ToUInt16($cpu.Data, 0x14) } else { $null }
                        Cores       = if ($cpu.Data.Length -gt 0x23) { [int]$cpu.Data[0x23] } else { $null }
                        Threads     = if ($cpu.Data.Length -gt 0x25) { [int]$cpu.Data[0x25] } else { $null }
                    } } else { $null }
        Memory    = [PSCustomObject]@{
            # Slots comes from the Type 16 array when present, but the count of Type 17
            # structures is the real physical slot count and is what an upgrade hangs on.
            Slots          = if ($array -and $array.Data.Length -ge 0x0F) { [int][BitConverter]::ToUInt16($array.Data, 0x0D) } else { $dimms.Count }
            SlotsPopulated = @($dimms | Where-Object Populated).Count
            SlotsFree      = $dimms.Count - @($dimms | Where-Object Populated).Count
            # Measure-Object returns NO object for an empty pipeline, so .Sum would blow up
            # under StrictMode. A machine with a mangled SMBIOS table must degrade to zero,
            # not take the whole profile call down with it.
            InstalledGB    = if ($dimms.Count) { [math]::Round((($dimms | Measure-Object SizeMB -Sum).Sum) / 1024, 1) } else { 0 }
            MaxCapacityGB  = if ($array -and $array.Data.Length -ge 0x0B) {
                                 $kb = [BitConverter]::ToUInt32($array.Data, 0x07)
                                 if ($kb -eq 0x80000000 -and $array.Data.Length -ge 0x17) { [math]::Round([BitConverter]::ToUInt64($array.Data, 0x0F) / 1GB, 0) }
                                 else { [math]::Round($kb / 1MB, 0) }
                             } else { $null }
            Dimms          = $dimms
        }
    }
}

function Get-KorHardwareProfile {
    <#
    .SYNOPSIS
        Full hardware profile for a workstation over c$ and RemoteRegistry — no WinRM needed.
    .DESCRIPTION
        Answers the questions a refresh or repurpose decision actually turns on: what CPU and
        socket, how many DIMM slots are free and at what speed, which discrete GPU is fitted,
        which disks, and whether the box is a UEFI/Secure Boot install or a legacy MBR one.

        Reachability is gated on port 445, NOT on ping — machines on this fleet answer SMB
        while dropping ICMP, and gating on ping reports a running workstation as offline.

        Leaves RemoteRegistry exactly as it found it (see Use-KorRemoteRegistry).
    .EXAMPLE
        Get-KorHardwareProfile -ComputerName KOR-SPARE100 | Format-List
    .EXAMPLE
        'KOR-213','KOR-224' | Get-KorHardwareProfile | Select-Object ComputerName, Cpu, DiscreteGpu
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)][string[]]$ComputerName)

    process {
        foreach ($cn in $ComputerName) {
            if (-not (Test-NetConnection -ComputerName $cn -Port 445 -WarningAction SilentlyContinue -InformationLevel Quiet)) {
                [PSCustomObject]@{ ComputerName = $cn; Reachable = $false }
                continue
            }

            $reg = { param($key)
                $out = & reg.exe query "\\$cn\$key" 2>&1
                if ($LASTEXITCODE -ne 0) { return @{} }
                $h = @{}
                foreach ($line in $out) {
                    if ($line -match '^\s{4}(.+?)\s{4}(REG_\w+)\s{4}(.*)$') { $h[$Matches[1].Trim()] = $Matches[3].Trim() }
                }
                $h
            }

            # The -Body scriptblock executes in its own child scope, so assigning a result
            # variable inside it would never reach us. Use-KorRemoteRegistry returns whatever
            # the body emits (every other call in it is Out-Null'd), so emit the object.
            Use-KorRemoteRegistry -ComputerName $cn -Body {
                $os = & $reg 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
                $cpu0 = & $reg 'HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0'
                $secureBoot = & $reg 'HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State'

                # GPUs: one subkey per adapter. The RDP mirror driver is always present and
                # is never the answer to "what card is in it", so it is filtered out.
                $gpus = foreach ($k in (& reg.exe query "\\$cn\HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}" 2>&1 |
                                        Where-Object { $_ -match '\\\d{4}$' })) {
                    $v = & $reg ('HKLM' + (($k.Trim()) -replace '^HKEY_LOCAL_MACHINE', ''))
                    if ($v['DriverDesc']) {
                        [PSCustomObject]@{ Name = $v['DriverDesc']; DriverVersion = $v['DriverVersion']; DeviceId = $v['MatchingDeviceId'] }
                    }
                }
                $gpus = @($gpus | Where-Object { $_.Name -notmatch 'Remote Display Adapter|Basic Display' })

                $disks = foreach ($root in 'HKLM\SYSTEM\CurrentControlSet\Enum\SCSI', 'HKLM\SYSTEM\CurrentControlSet\Enum\IDE') {
                    $out = & reg.exe query "\\$cn\$root" /s /v FriendlyName 2>&1
                    if ($LASTEXITCODE -eq 0) {
                        foreach ($line in $out) { if ($line -match 'FriendlyName\s+REG_SZ\s+(.+)$') { $Matches[1].Trim() } }
                    }
                }

                $smbios = $null
                $smbOut = & reg.exe query "\\$cn\HKLM\SYSTEM\CurrentControlSet\Services\mssmbios\Data" /v SMBiosData 2>&1
                $hex = ($smbOut | Where-Object { $_ -match 'SMBiosData\s+REG_BINARY\s+([0-9A-Fa-f]+)' } |
                        ForEach-Object { $Matches[1] }) | Select-Object -First 1
                if ($hex) {
                    $bytes = New-Object byte[] ($hex.Length / 2)
                    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16) }
                    $smbios = ConvertFrom-KorSmbios -Bytes $bytes
                }

                $volume = $null
                try {
                    $drv = (New-Object -ComObject Scripting.FileSystemObject).GetDrive((Resolve-KorAdminShare $cn))
                    $volume = [PSCustomObject]@{ TotalGB = [math]::Round($drv.TotalSize / 1GB, 1); FreeGB = [math]::Round($drv.FreeSpace / 1GB, 1) }
                } catch { Write-Verbose "Volume size unavailable on ${cn}: $($_.Exception.Message)" }

                [PSCustomObject]@{
                    ComputerName = $cn
                    Reachable    = $true
                    Os           = "$($os['ProductName']) $($os['DisplayVersion']) build $($os['CurrentBuild'])"
                    Cpu          = $cpu0['ProcessorNameString']
                    Socket       = $smbios.Processor.Socket
                    Cores        = $smbios.Processor.Cores
                    Threads      = $smbios.Processor.Threads
                    Board        = "$($smbios.Board.Manufacturer) $($smbios.Board.Product)".Trim()
                    Chassis      = $smbios.Chassis.Type
                    DiscreteGpu  = @($gpus | Where-Object { $_.Name -notmatch 'Intel\(R\) (UHD|HD|Iris)' }).Name
                    AllGpus      = $gpus
                    Memory       = $smbios.Memory
                    Disks        = @($disks | Select-Object -Unique)
                    SystemVolume = $volume
                    # 0x0 here on a box with C:\boot means a legacy MBR install — it will not
                    # take Windows 11 without a rebuild, which is a planning fact, not a setting.
                    SecureBoot   = if ($secureBoot.ContainsKey('UEFISecureBootEnabled')) { $secureBoot['UEFISecureBootEnabled'] -ne '0x0' } else { $null }
                    Smbios       = $smbios
                }
            }
        }
    }
}

function Get-KorThermalProfile {
    <#
    .SYNOPSIS
        Why a workstation is loud, hot, or throttling: cooling profile, fan speeds,
        temperatures, GPU state, and whether the CPU is genuinely being limited.
    .DESCRIPTION
        Written 2026-08-24 after KOR-305 was reported as NOISY. Every Office-focused check in
        this module came back clean; the answer was a BIOS cooling profile set to Performance
        on a machine that was idle and cold. This function exists so the next "it's noisy" is
        one command instead of four hand-written probes.

        Unlike the rest of the module this uses CIM over DCOM, which the fleet permits even
        though WinRM is blocked. Reachability is gated on port 445, never on ping.

        Two traps worth not re-learning:

        1. A COLD, IDLE MACHINE CAN STILL BE LOUD. Check the fan profile before hunting for
           runaway load. KOR-305 sat at 2.2% CPU and 27.9 C while running Lenovo
           "Performance mode", which holds an aggressive curve regardless of temperature.

        2. Kernel-Processor-Power EVENT 37 IS USUALLY MEANINGLESS on Intel hybrid CPUs. It
           says "limited by system firmware" and fires constantly for the E-cores, whose max
           turbo sits below the nominal-derived ceiling. On KOR-305 it had fired 4,928 times
           and meant nothing: every instance named a logical processor in 16-31, exactly the
           16 E-cores of an i9-13900K. Only treat it as a real throttle when it names a
           P-core. ThrottledPCores below does that filtering for you.
    .EXAMPLE
        Get-KorThermalProfile -ComputerName KOR-305 | Format-List
    .EXAMPLE
        'KOR-305','KOR-306' | Get-KorThermalProfile | Select-Object ComputerName, CoolingMode, CpuFanRpm
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][string[]]$ComputerName,
        # Event-log scan is the slow part; skip it when you only want the live readings.
        [switch]$SkipEventScan
    )

    process {
        foreach ($cn in $ComputerName) {
            if (-not (Test-NetConnection -ComputerName $cn -Port 445 -WarningAction SilentlyContinue -InformationLevel Quiet)) {
                [PSCustomObject]@{ ComputerName = $cn; Reachable = $false }
                continue
            }

            # Neither CIM transport is reliable on this fleet by itself, so try both rather
            # than assuming either. DCOM answers "RPC server is unavailable" wherever the
            # dynamic port range is blocked, which is most machines; WSMan answers only where
            # the WinRM listener happens to be up. Whichever connects first wins.
            $session = $null
            $proto = $null
            foreach ($p in 'Wsman', 'Dcom') {
                try {
                    $session = New-CimSession -ComputerName $cn -SessionOption (New-CimSessionOption -Protocol $p) `
                        -OperationTimeoutSec 20 -ErrorAction Stop
                    $proto = $p
                    break
                }
                catch { $session = $null }
            }
            if (-not $session) {
                [PSCustomObject]@{
                    ComputerName = $cn; Reachable = $true
                    Findings     = @('No CIM transport: WSMan and DCOM both refused. Thermal data needs one of them.')
                }
                continue
            }

            $cim = { param($ns, $cls)
                try { Get-CimInstance -CimSession $session -Namespace $ns -ClassName $cls -ErrorAction Stop } catch { $null }
            }
            # The Lenovo_DT_* desktop getters report through a 'return' property, not a value
            # property. A platform that does not populate one answers 0 — which is neither a
            # temperature nor an RPM, so surface it as null rather than a confident zero.
            $lenovoData = { param($cls)
                try {
                    $i = Get-CimInstance -CimSession $session -Namespace root\wmi -ClassName $cls -ErrorAction Stop | Select-Object -First 1
                    if ($null -ne $i -and [int]$i.return -gt 0) { [int]$i.return } else { $null }
                } catch { $null }
            }

            # Win32_ComputerSystem, NOT Win32_ComputerSystemProduct: over WSMan the former
            # comes back as "the XML is invalid" from these Lenovo boxes, and the latter
            # carries the same vendor and model without tripping the serialiser.
            $cs   = & $cim 'root\cimv2' 'Win32_ComputerSystemProduct'
            $bios = & $cim 'root\cimv2' 'Win32_BIOS'
            $cpu  = @(& $cim 'root\cimv2' 'Win32_Processor')

            # BIOS cooling profile. This is the setting that actually decides the fan curve.
            $coolingMode = $null
            $biosSettings = & $cim 'root\wmi' 'Lenovo_BiosSetting'
            foreach ($s in $biosSettings) {
                if ($s.CurrentSetting -match '^(IntelligentCoolingPerformanceMode|ThermalMode|FanControl)\s*,\s*([^;]+)') {
                    $coolingMode = $Matches[2].Trim()
                }
            }

            $smartFan = $null
            $smartFanLabel = $null
            try {
                $gz = Get-CimInstance -CimSession $session -Namespace root\wmi -ClassName LENOVO_GAMEZONE_DATA -ErrorAction Stop | Select-Object -First 1
                $smartFan = (Invoke-CimMethod -InputObject $gz -MethodName GetSmartFanMode -ErrorAction Stop).Data
                # Lenovo's numbering is not consistent across lines, so decode leniently and
                # let CoolingMode (read straight from BIOS) be the authoritative answer.
                $smartFanLabel = switch ($smartFan) {
                    1 { 'Quiet' } 2 { 'Balanced' } 3 { 'Performance' } 4 { 'Full Speed' } default { "mode $smartFan" }
                }
            } catch { }

            $thermalC = $null
            $tz = & $cim 'root\wmi' 'MSAcpi_ThermalZoneTemperature'
            if ($tz) { $thermalC = [math]::Round((($tz | Select-Object -First 1).CurrentTemperature / 10) - 273.15, 1) }

            # Real RPM, where the platform exposes it.
            $cpuFan = & $lenovoData 'Lenovo_DT_GetCPUFan'
            $sysFan = & $lenovoData 'Lenovo_DT_GetSYSFan'
            $cpuTemp = & $lenovoData 'Lenovo_DT_GetCPUTemp'

            # GPU fans are a common noise source and nvidia-smi is the only honest source for
            # them. It has to run ON the box: Win32_Process Create works where WinRM does not.
            $gpu = $null
            try {
                $tmp = "C:\Windows\Temp\kor-thermal-gpu.txt"
                $unc = "\\$cn\c$\Windows\Temp\kor-thermal-gpu.txt"
                if (Test-Path $unc) { Remove-Item $unc -Force -ErrorAction SilentlyContinue }
                $cmd = "cmd.exe /c nvidia-smi --query-gpu=name,temperature.gpu,fan.speed,utilization.gpu,power.draw,power.limit --format=csv,noheader > $tmp 2>&1"
                $null = Invoke-CimMethod -CimSession $session -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd } -ErrorAction Stop
                for ($i = 0; $i -lt 10 -and -not (Test-Path $unc); $i++) { Start-Sleep -Milliseconds 500 }
                if (Test-Path $unc) {
                    $line = (Get-Content $unc -ErrorAction SilentlyContinue | Where-Object { $_ -match ',' } | Select-Object -First 1)
                    if ($line) {
                        $f = $line -split '\s*,\s*'
                        $gpu = [PSCustomObject]@{
                            Name = $f[0]; TempC = $f[1]; FanPct = $f[2]; UtilPct = $f[3]; PowerW = $f[4]; PowerLimitW = $f[5]
                        }
                    }
                }
            } catch { }

            # Only P-core throttling is real; see the E-core trap in the description.
            $throttledP = $null
            if (-not $SkipEventScan) {
                try {
                    $ev = Get-KorWorkstationEvent -ComputerName $cn -LogName System -ErrorAction Stop |
                        Where-Object { $_.Id -eq 37 -and $_.ProviderName -match 'Processor-Power' }
                    $procs = foreach ($e in $ev) { if ($e.Message -match 'processor (\d+) in group') { [int]$Matches[1] } }
                    $pcoreHits = @($procs | Sort-Object -Unique | Where-Object { $_ -lt 16 })
                    $throttledP = $pcoreHits.Count
                } catch { }
            }

            [PSCustomObject]@{
                ComputerName    = $cn
                Reachable       = $true
                Transport       = $proto
                Model           = if ($cs) { "$($cs.Vendor) $($cs.Name)".Trim() } else { $null }
                Bios            = if ($bios) { "$($bios.SMBIOSBIOSVersion) ($(if ($bios.ReleaseDate) { $bios.ReleaseDate.ToString('yyyy-MM-dd') }))" } else { $null }
                Cpu             = if ($cpu) { $cpu[0].Name.Trim() } else { $null }
                CpuLoadPct      = if ($cpu) { ($cpu | Measure-Object LoadPercentage -Average).Average } else { $null }
                CoolingMode     = $coolingMode
                SmartFanMode    = $smartFanLabel
                ThermalZoneC    = $thermalC
                CpuTempC        = $cpuTemp
                CpuFanRpm       = $cpuFan
                SysFanRpm       = $sysFan
                Gpu             = $gpu
                ThrottledPCores = $throttledP
                Findings        = @(
                    if ($coolingMode -and $coolingMode -match 'Performance|Full Speed') {
                        "Cooling profile is '$coolingMode' — an aggressive fan curve that stays loud even at idle. 'Balance mode' is the quiet setting."
                    }
                    if ($thermalC -and $thermalC -gt 80) { "Thermal zone at $thermalC C — genuinely hot, not just a fan profile." }
                    if ($throttledP) { "$throttledP P-core(s) report firmware limiting — this one is real, unlike E-core reports." }
                    if ($gpu -and [int]($gpu.FanPct -replace '\D') -gt 60) { "GPU fan at $($gpu.FanPct) — check GPU load and dust." }
                )
            }
        }
    }
}

Export-ModuleMember -Function Test-KorWorkstationChannel, Use-KorRemoteRegistry, Get-KorWorkstationEvent,
    Get-KorOutlookAddin, Get-KorOutlookStore, Get-KorOfficeHealth, Set-KorOutlookAddinState,
    Invoke-KorSearchIndexRebuild, Get-KorWorkstationHealth, Get-KorServiceState, Wait-KorServiceState,
    Invoke-KorSc, Resolve-KorAdminShare, Get-KorHardwareProfile, ConvertFrom-KorSmbios,
    Get-KorThermalProfile
