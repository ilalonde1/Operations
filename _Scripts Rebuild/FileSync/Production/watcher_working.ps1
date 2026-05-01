# ==== watcher_stable_service_wait.ps1 ====

$ErrorActionPreference = "Continue"

# ---- CONFIG ----
$script:watchPath         = "\\KOR-FS01\Projects\Projects"

$script:ssiPattern        = "04 Construction Admin\02 SSI (Structural Site Instructions)"
$script:rfiPattern        = "04 Construction Admin\03 RFI (Request for Info)\Sent to Inspectors"
$script:photosPattern     = "04 Construction Admin\07 Photos"
$script:stickfilePattern  = "05 Stickfile"

$script:ssiScript         = "C:\_APPS\FileSync\Production\SINGLE_SYNC_SSI.ps1"
$script:rfiScript         = "C:\_APPS\FileSync\Production\SINGLE_SYNC_RFI.ps1"
$script:photosScript      = "C:\_APPS\FileSync\Production\SINGLE_SYNC_Photos.ps1"
$script:stickfileScript   = "C:\_APPS\FileSync\Production\SINGLE_SYNC_Stickfile.ps1"

# Cleaner deletes the SharePoint project when control file (re)appears
$script:cleanScript       = "C:\_APPS\FileSync\Production\SINGLE_CLEAN_Projects.ps1"

$script:logPath           = "C:\_APPS\FileSync\Production\Logs\Watcher_Log.txt"
$script:DebounceSeconds   = 5

# ---- Logging ----
function Log([string]$msg) {
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    Add-Content -Path $script:logPath -Value "$ts $msg" -Encoding utf8
}

# ---- Control-file skip ----
function ShouldIgnoreFolder([string]$directory) {
    try { $dirItem = Get-Item -LiteralPath $directory -ErrorAction Stop } catch { return $false }
    $rootLower = $script:watchPath.ToLower()
    $curr = $dirItem
    while ($curr) {
        if ($curr.FullName.ToLower() -eq $rootLower) { break }
        $controlFile = Join-Path $curr.FullName "NOT SYNCED TO ACTIVE PROJECTS.txt"
        if (Test-Path -LiteralPath $controlFile) { Log "SKIP control file at $($curr.FullName)"; return $true }
        $curr = $curr.Parent
    }
    return $false
}

# ---- Resolve which script to run and the exact target ROOT folder ----
function Resolve-Target {
    param([string]$anyDirUnderTarget)

    $p  = ($anyDirUnderTarget -replace '/', '\')
    $pl = $p.ToLower()

    $stick = $script:stickfilePattern.ToLower()
    $ssi   = $script:ssiPattern.ToLower()
    $rfi   = $script:rfiPattern.ToLower()
    $photos= $script:photosPattern.ToLower()

    function GetRoot([string]$needleLower, [string]$hayLower, [string]$hayOrig) {
        $i = $hayLower.IndexOf("\$needleLower")
        if ($i -ge 0) { return $hayOrig.Substring(0, $i + 1 + $needleLower.Length) }
        return $null
    }

    if ($pl.Contains("\$stick"))  { return @{ Script=$script:stickfileScript; Root=(GetRoot $stick  $pl $p); Kind='pdf'   } }
    if ($pl.Contains("\$ssi"))    { return @{ Script=$script:ssiScript;       Root=(GetRoot $ssi    $pl $p); Kind='pdf'   } }
    if ($pl.Contains("\$rfi"))    { return @{ Script=$script:rfiScript;       Root=(GetRoot $rfi    $pl $p); Kind='pdf'   } }
    if ($pl.Contains("\$photos")) { return @{ Script=$script:photosScript;    Root=(GetRoot $photos $pl $p); Kind='image' } }
    return $null
}

# ---- Trigger all helpers for a project (control file removed) ----
function Trigger-ProjectSync([string]$projectDir) {
    $targets = @(
        @{ Rel = $script:stickfilePattern; Script = $script:stickfileScript; Kind='pdf'   },
        @{ Rel = $script:ssiPattern;       Script = $script:ssiScript;       Kind='pdf'   },
        @{ Rel = $script:rfiPattern;       Script = $script:rfiScript;       Kind='pdf'   },
        @{ Rel = $script:photosPattern;    Script = $script:photosScript;    Kind='image' }
    )
    foreach ($t in $targets) {
        $root = Join-Path $projectDir $t.Rel
        if (Test-Path -LiteralPath $root) {
            Log "CONTROL-FILE REMOVED → forcing sync for: $root"
            Try-Run -root $root -scriptPath $t.Script -Force
        } else {
            Log "CONTROL-FILE REMOVED → target missing (skip): $root"
        }
    }
}

# ---- Per-target debounce + in-flight lock (inline helper run) ----
$script:recent   = @{}  # key = "<root>|<script>" -> [datetime]
$script:inflight = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

function Try-Run([string]$root, [string]$scriptPath, [switch]$Force) {
    if ([string]::IsNullOrWhiteSpace($root)) {
        Log "ABORT: empty root passed to $scriptPath"; return
    }
    if (-not (Test-Path -LiteralPath $root)) {
        Log "ABORT: root does not exist for $scriptPath : $root"; return
    }
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        Log "ABORT: script not found: $scriptPath"; return
    }

    $key = "$root|$scriptPath"
    $now = Get-Date

    if ($script:inflight.Contains($key)) { Log "COALESCE skip (in-flight) for $key"; return }

    if (-not $Force) {
        if ($script:recent.ContainsKey($key)) {
            $age = ($now - $script:recent[$key]).TotalSeconds
            if ($age -lt $script:DebounceSeconds) {
                Log "DEBOUNCE skip ($([math]::Round($age,1))s) for $key"; return
            }
        }
    }
    $script:recent[$key] = $now
    if (ShouldIgnoreFolder $root) { return }

    try {
        $null = $script:inflight.Add($key)
        Log "RUN $scriptPath -GivenPath `"$root`""
        & $scriptPath -GivenPath $root
    } catch {
        Log "ERROR running $scriptPath for $root : $($_.Exception.Message)"
    } finally {
        $script:inflight.Remove($key) | Out-Null
    }
}

# ---- Filters ----
$script:imageExts         = @('.jpg','.jpeg','.png','.heic','.bmp','.tif','.tiff')
$script:ignoredExts       = @('.tmp', '.bak', '.log', '.rws', '.dat', '.dwgtmp')
$script:ignoredNameStarts = @('~$', 'pulse-', 'n4newforma-')
$script:ignoredDirRegex   = '\\Newforma\\email($|\\)'

# ---- Process a single event ----
function Process-Event([System.IO.FileSystemEventArgs]$e, [string]$eventName) {
    try {
        $path = $e.FullPath
        $dir  = Split-Path -Path $path -Parent
        if (-not $dir) { $dir = [System.IO.Path]::GetDirectoryName($path) }
        if ([string]::IsNullOrWhiteSpace($dir)) { return }

        # Fast ignore noisy subtree
        if ($dir -match $script:ignoredDirRegex) { return }

        $controlName = 'NOT SYNCED TO ACTIVE PROJECTS.txt'
        $fname       = [IO.Path]::GetFileName($path)

        # --- Control-file REMOVED → fire initial syncs
        if ( ($eventName -eq 'Deleted' -or $eventName -eq 'Renamed') -and
             ($fname -ieq $controlName) ) {

            $projectDir = $dir
            if ($eventName -eq 'Renamed' -and $e -is [System.IO.RenamedEventArgs]) {
                $projectDir = Split-Path -Path ([System.IO.RenamedEventArgs]$e).OldFullPath -Parent
            }

            if ($projectDir -and ($projectDir.ToLower().StartsWith($script:watchPath.ToLower()))) {
                Log "Detected control-file REMOVAL at: $projectDir → launching initial syncs"
                Trigger-ProjectSync -projectDir $projectDir
            }
            return
        }

        # --- Control-file ADDED/CHANGED/RENAMED-TO → CLEAN (delete SharePoint project)
        if ( ($eventName -in @('Created','Changed','Renamed')) -and ($fname -ieq $controlName) ) {
            $projectDir = $dir
            if ($eventName -eq 'Renamed' -and $e -is [System.IO.RenamedEventArgs]) {
                $projectDir = Split-Path -Path $e.FullPath -Parent
            }

            if (-not (Test-Path -LiteralPath $projectDir)) {
                Log "CLEAN abort: projectDir not found for control-file ADD/CHANGE at $($e.FullPath)"
                return
            }

            if (-not (Test-Path -LiteralPath $script:cleanScript)) {
                Log "CLEAN abort: cleaner script missing: $script:cleanScript"
                return
            }

            Log "Detected control-file ADD/CHANGE at: $projectDir → launching project CLEAN"

            # Fire cleaner in a fresh PowerShell with -GivenPath (NOT -ProjectNumber)
            $psExe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
            $args  = @(
                '-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$script:cleanScript`"",
                '-GivenPath',"`"$projectDir`""
            )
            $proc = Start-Process -FilePath $psExe -ArgumentList $args -WindowStyle Hidden -PassThru
            Log "RUN CLEAN $script:cleanScript -GivenPath `"$projectDir`" (pid=$($proc.Id))"
            return
        }

        # Only consider our target trees (normal file events)
        $t = Resolve-Target $dir
        if (-not $t -or -not $t.Root) { return }

        # Only fire when the event is in the exact target root (not subfolders)
        $dirNorm  = $dir.TrimEnd('\')
        $rootNorm = $t.Root.TrimEnd('\')
        if (-not ($dirNorm -ieq $rootNorm)) { return }

        # Extension & temp name filters
        $ext  = [IO.Path]::GetExtension($path).ToLowerInvariant()
        $name = [IO.Path]::GetFileName($path)
        if ($script:ignoredExts -contains $ext) { return }
        foreach ($pref in $script:ignoredNameStarts) {
            if ($name.StartsWith($pref, [StringComparison]::OrdinalIgnoreCase)) { return }
        }

        $isPdf   = ($ext -eq '.pdf')
        $isImage = $script:imageExts -contains $ext
        if ( ($t.Kind -eq 'pdf'   -and -not $isPdf)   -or
             ($t.Kind -eq 'image' -and -not $isImage) ) { return }

        # Relevant -> log
        if ($eventName -eq 'Renamed' -and $e -is [System.IO.RenamedEventArgs]) {
            $old = ([System.IO.RenamedEventArgs]$e).OldFullPath
            Log "EVT Renamed: $old -> $($e.FullPath)"
        } else {
            Log "EVT $eventName   : $($e.FullPath)"
        }

        # Adaptive settle
        if ($eventName -eq 'Created') { Start-Sleep -Milliseconds 150 }
        elseif ($eventName -eq 'Changed') { Start-Sleep -Milliseconds 500 }

        Try-Run -root $t.Root -scriptPath $t.Script
    } catch {
        Log "ERROR Process-Event: $($_.Exception.Message)"
    }
}

# ---- FileSystemWatcher ----
$w = New-Object System.IO.FileSystemWatcher
$w.Path = $script:watchPath
$w.Filter = "*.*"
$w.IncludeSubdirectories = $true
$w.NotifyFilter = [IO.NotifyFilters]'FileName, DirectoryName, LastWrite'
$w.InternalBufferSize = 65536

Log "Watcher started for: $script:watchPath"
Log "DEBUG: SSI [$script:ssiPattern]"
Log "DEBUG: RFI [$script:rfiPattern]"
Log "DEBUG: Stickfile [$script:stickfilePattern]"
Log "DEBUG: Photos [$script:photosPattern]"

# ---- Register events (dequeue with Wait-Event)
$subs = @()
$subs += Register-ObjectEvent -InputObject $w -EventName Created -SourceIdentifier 'FSW.Created'
$subs += Register-ObjectEvent -InputObject $w -EventName Renamed -SourceIdentifier 'FSW.Renamed'
$subs += Register-ObjectEvent -InputObject $w -EventName Deleted -SourceIdentifier 'FSW.Deleted'
$subs += Register-ObjectEvent -InputObject $w -EventName Changed -SourceIdentifier 'FSW.Changed'
$subs += Register-ObjectEvent -InputObject $w -EventName Error   -SourceIdentifier 'FSW.Error'

$w.EnableRaisingEvents = $true
Log "Service started and monitoring $script:watchPath (Created/Renamed/Deleted/Changed)."

# ---- Main loop: pull events from the queue and handle them
$lastHeartbeat = Get-Date
while ($true) {
    $evt = Wait-Event -Timeout 2
    if ($evt) {
        try {
            $name = $evt.SourceIdentifier
            if ($name -eq 'FSW.Error') {
                $ex = $evt.SourceEventArgs.GetException()
                Log "WATCHER ERROR: $($ex.GetType().FullName) - $($ex.Message)"
            } else {
                Process-Event -e $evt.SourceEventArgs -eventName ($evt.SourceEventArgs.ChangeType.ToString())
            }
        } catch {
            Log "ERROR main loop: $($_.Exception.Message)"
        } finally {
            Remove-Event -EventIdentifier $evt.EventIdentifier -ErrorAction SilentlyContinue
        }
    }

    # heartbeat
    $now = Get-Date
    if (($now - $lastHeartbeat).TotalMinutes -ge 5) {
        $subCount = (Get-EventSubscriber | Where-Object { $_.SourceIdentifier -like 'FSW.*' } | Measure-Object).Count
        Log "HEARTBEAT OK. Subs: $subCount."
        $lastHeartbeat = $now
    }
}
