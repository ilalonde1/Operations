# ==== watcher_lock_registry.ps1 ====

$ErrorActionPreference = "Continue"

# ---- CONFIG ----
$script:MaxSyncMinutes = 15   # adjust as needed; 30 is a good starting point
$script:watchPath         = "\\KOR-FS01\Projects\Projects"

$script:ssiPattern        = "04 Construction Admin\02 SSI (Structural Site Instructions)"
$script:rfiPattern        = "04 Construction Admin\03 RFI (Request for Info)\Sent to Inspectors"
$script:photosPattern     = "04 Construction Admin\07 Photos"
$script:stickfilePattern  = "05 Stickfile"

$script:ssiScript         = "C:\_APPS\FileSync\Production\SINGLE_SYNC_SSI.ps1"
$script:rfiScript         = "C:\_APPS\FileSync\Production\SINGLE_SYNC_RFI.ps1"
$script:photosScript      = "C:\_APPS\FileSync\Production\SINGLE_SYNC_Photos.ps1"
$script:stickfileScript   = "C:\_APPS\FileSync\Production\SINGLE_SYNC_Stickfile.ps1"

# Cleaner (control-file adds/changes)
$script:cleanScript       = "C:\_APPS\FileSync\Production\SINGLE_CLEAN_Projects.ps1"

$script:logPath           = "C:\_APPS\FileSync\Production\Logs\Watcher_Log.txt"

# Minimal folder-level debounce (seconds)
$script:DebounceSeconds   = 5

# Lock poller cadence / retention
$global:LockPollSeconds     = 10       # poll every 10s
$global:MaxLockTrackHours   = 12       # drop any lock entries older than this
$global:PostRunRetrySeconds = 120      # post-run verification window

# ---- Ensure log folder exists ----
try {
  $logDir = Split-Path -Path $script:logPath -Parent
  if ($logDir -and -not (Test-Path -LiteralPath $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
  }
} catch {}

# ---- Logging ----
function Log([string]$msg) {
  try {
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    Add-Content -Path $script:logPath -Value "$ts $msg" -Encoding utf8
  } catch {}
}

# ---- Control-file skip ----
$script:controlFileName = 'NOT SYNCED TO ACTIVE PROJECTS.txt'
function ShouldIgnoreFolder([string]$directory) {
  try { $dirItem = Get-Item -LiteralPath $directory -ErrorAction Stop } catch { return $false }
  $rootLower = $script:watchPath.ToLower()
  $curr = $dirItem
  while ($curr) {
    if ($curr.FullName.ToLower() -eq $rootLower) { break }
    $controlFile = Join-Path $curr.FullName $script:controlFileName
    if (Test-Path -LiteralPath $controlFile) { Log "SKIP control file at $($curr.FullName)"; return $true }
    $curr = $curr.Parent
  }
  return $false
}

# ---- Resolve which helper to run for a path ----
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

        # Ensure the local target directory exists so the helper can run
        if (-not (Test-Path -LiteralPath $root)) {
            try {
                New-Item -ItemType Directory -Path $root -Force | Out-Null
                Log "CONTROL-FILE REMOVED -> created missing target: $root"
            } catch {
                Log "CONTROL-FILE REMOVED -> target create FAILED (skip): $root : $($_.Exception.Message)"
                continue
            }
        }

        Log "CONTROL-FILE REMOVED -> forcing sync for: $root"
        Try-Run -root $root -scriptPath $t.Script -Force -IgnoreControl
    }
}

# ---- Inline helper run with simple folder-level debounce ----
$script:recent   = @{}  # key = "<root>|<script>" -> [datetime]
$script:inflight = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

function Try-Run([string]$root, [string]$scriptPath, [switch]$Force, [switch]$IgnoreControl) {
  if ([string]::IsNullOrWhiteSpace($root)) { 
    Log "ABORT: empty root passed to $scriptPath"; 
    return 
  }
  if (-not (Test-Path -LiteralPath $root)) { 
    Log "ABORT: root does not exist for $scriptPath : $root"; 
    return 
  }
  if (-not (Test-Path -LiteralPath $scriptPath)) { 
    Log "ABORT: script not found: $scriptPath"; 
    return 
  }

  $key = "$root|$scriptPath"
  $now = Get-Date

  if ($script:inflight.Contains($key)) {
    Log "COALESCE skip (in-flight) for $key"
    return
  }

  if (-not $Force) {
    if ($script:recent.ContainsKey($key)) {
      $age = ($now - $script:recent[$key]).TotalSeconds
      if ($age -lt $script:DebounceSeconds) {
        Log "DEBOUNCE skip ($([math]::Round($age,1))s) for $key"
        return
      }
    }
  }

  # Mark as recently scheduled so we do not spam runs
  $script:recent[$key] = $now

  if (-not $IgnoreControl) {
    if (ShouldIgnoreFolder $root) { 
      return 
    }
  }

  $timeoutMinutes = $script:MaxSyncMinutes
  if (-not $timeoutMinutes -or $timeoutMinutes -le 0) { 
    $timeoutMinutes = 30 
  }
  $timeoutSeconds = [int]($timeoutMinutes * 60)

  try {
    $null = $script:inflight.Add($key)

    Log "RUN $scriptPath -GivenPath `"$root`" (timeout ${timeoutMinutes}m)"

    # Launch the real sync in a separate PowerShell process
    $psExe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"

    $arguments = @(
      "-NoProfile",
      "-ExecutionPolicy", "Bypass",
      "-File", "`"$scriptPath`"",
      "-GivenPath", "`"$root`""
    )

    $proc = Start-Process -FilePath $psExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if (-not $proc) {
      throw "Failed to start child process for $scriptPath"
    }

    # Wait with a hard timeout
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not $proc.HasExited -and $sw.Elapsed.TotalSeconds -lt $timeoutSeconds) {
      Start-Sleep -Seconds 5
    }

    if (-not $proc.HasExited) {
      Log "ERROR: sync for $root exceeded ${timeoutMinutes} minutes. Killing process Id $($proc.Id)."
      try { 
        $proc.Kill() 
      } catch {
        Log "WARN: failed to kill process Id $($proc.Id): $($_.Exception.Message)"
      }
    } else {
      if ($proc.ExitCode -eq 0) {
        Log "DONE $scriptPath -GivenPath `"$root`" (exit 0)"
      } else {
        Log "ERROR: $scriptPath -GivenPath `"$root`" exited with code $($proc.ExitCode)"
      }
    }
  } catch {
    Log "ERROR running $scriptPath for $root : $($_.Exception.Message)"
  } finally {
    # Always clear in-flight so a stuck run cannot block all future ones
    $script:inflight.Remove($key) | Out-Null
  }
}


# ---- Filters ----
$script:imageExts         = @('.jpg','.jpeg','.png','.heic','.bmp','.tif','.tiff')
$script:ignoredExts       = @('.tmp', '.bak', '.log', '.rws', '.dat', '.dwgtmp')
$script:ignoredNameStarts = @('~$', 'pulse-', 'n4newforma-')
$script:ignoredDirRegex   = '\\Newforma\\email($|\\)'

# ---- Lock registry + poller ----
$global:LockedFiles = @{}  # key = fullpath (lower); value = @{ Root=..., Script=..., FirstSeen=[datetime]; [PostRun=$true; ExpireAt=[datetime]] }

function Test-FileUnlockedStable {
  param([Parameter(Mandatory)] [string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) { return $false }

  # Try read+write, no sharing
  try {
    $fs = [System.IO.File]::Open($Path, 'Open', 'ReadWrite', 'None')
    $fs.Close()
  } catch {
    return $false
  }

  # Size must be stable for ~1s
  $len1 = (Get-Item -LiteralPath $Path).Length
  Start-Sleep -Milliseconds 1000
  if (-not (Test-Path -LiteralPath $Path)) { return $false }
  $len2 = (Get-Item -LiteralPath $Path).Length

  return ($len1 -eq $len2)
}

try { Unregister-Event -SourceIdentifier 'LockPoll' -ErrorAction SilentlyContinue } catch {}
if ($global:LockTimer) { try { $global:LockTimer.Stop(); $global:LockTimer.Dispose() } catch {} }

$global:LockTimer = New-Object System.Timers.Timer
$global:LockTimer.Interval = [double]($global:LockPollSeconds * 1000)
$global:LockTimer.AutoReset = $true

Register-ObjectEvent -InputObject $global:LockTimer -EventName Elapsed -SourceIdentifier 'LockPoll' -Action {
  try {
    if ($global:LockedFiles.Count -eq 0) { return }
    $now = Get-Date
    foreach ($k in @($global:LockedFiles.Keys)) {
      $entry      = $global:LockedFiles[$k]
      $p          = $k
      $root       = $entry.Root
      $scriptPath = $entry.Script

      # Post-run verification entries (cover Save/close race)
      if ($entry.ContainsKey('PostRun') -and $entry.PostRun) {
        if ($entry.ContainsKey('ExpireAt') -and $now -gt $entry.ExpireAt) {
          $global:LockedFiles.Remove($k) | Out-Null
          Log "POSTRUN drop (expired): $p"
          continue
        }
        if (Test-Path -LiteralPath $p -ErrorAction SilentlyContinue) {
          if (Test-FileUnlockedStable -Path $p) {
            Log "POSTRUN verify -> invoking sync for root: $root (file: $p)"
            Try-Run -root $root -scriptPath $scriptPath -Force
            $global:LockedFiles.Remove($k) | Out-Null
          }
          continue
        } else {
          Log "POSTRUN verify (file gone) -> invoking sync for root: $root (file: $p)"
          Try-Run -root $root -scriptPath $scriptPath -Force
          $global:LockedFiles.Remove($k) | Out-Null
          continue
        }
      }

      # Normal locked-file tracking
      if ( ($now - $entry.FirstSeen).TotalHours -ge $global:MaxLockTrackHours ) {
        $global:LockedFiles.Remove($k) | Out-Null
        Log "LOCK drop (stale > $global:MaxLockTrackHours h): $p"
        continue
      }

      if (-not (Test-Path -LiteralPath $p)) {
        Log "LOCK cleared (file gone) -> invoking sync for root: $root (file: $p)"
        Try-Run -root $root -scriptPath $scriptPath -Force
        $global:LockedFiles.Remove($k) | Out-Null
        continue
      }

      if (Test-FileUnlockedStable -Path $p) {
        Log "LOCK cleared -> invoking sync for root: $root (file: $p)"
        Try-Run -root $root -scriptPath $scriptPath -Force
        $global:LockedFiles.Remove($k) | Out-Null
      }
    }
  } catch {
    Log "ERROR lock poller: $($_.Exception.Message)"
  }
} | Out-Null

$global:LockTimer.Start()
Log "LOCK poller started (interval ${global:LockPollSeconds}s, post-run ${global:PostRunRetrySeconds}s)"

# ---- Process a single event ----
function Process-Event([System.IO.FileSystemEventArgs]$e, [string]$eventName) {
  try {
    $path = $e.FullPath
    $dir  = Split-Path -Path $path -Parent
    if (-not $dir) { $dir = [System.IO.Path]::GetDirectoryName($path) }
    if ([string]::IsNullOrWhiteSpace($dir)) { return }

    if ($dir -match $script:ignoredDirRegex) { return }

    $fname = [IO.Path]::GetFileName($path)

    # Control-file REMOVED -> initial syncs
    if ( ($eventName -eq 'Deleted' -or $eventName -eq 'Renamed') -and ($fname -ieq $script:controlFileName) ) {
      $projectDir = $dir
      if ($eventName -eq 'Renamed' -and $e -is [System.IO.RenamedEventArgs]) {
        $projectDir = Split-Path -Path ([System.IO.RenamedEventArgs]$e).OldFullPath -Parent
      }
      if ($projectDir -and ($projectDir.ToLower().StartsWith($script:watchPath.ToLower()))) {
        Log "Detected control-file REMOVAL at: $projectDir -> launching initial syncs"
        Trigger-ProjectSync -projectDir $projectDir
      }
      return
    }

    # Control-file ADDED/CHANGED/RENAMED-TO -> CLEAN
    if ( ($eventName -in @('Created','Changed','Renamed')) -and ($fname -ieq $script:controlFileName) ) {
      $projectDir = $dir
      if ($eventName -eq 'Renamed' -and $e -is [System.IO.RenamedEventArgs]) {
        $projectDir = Split-Path -Path $e.FullPath -Parent
      }
      if (-not (Test-Path -LiteralPath $projectDir)) { Log "CLEAN abort: projectDir not found for $($e.FullPath)"; return }
      if (-not (Test-Path -LiteralPath $script:cleanScript)) { Log "CLEAN abort: cleaner script missing: $script:cleanScript"; return }

      Log "Detected control-file ADD/CHANGE at: $projectDir -> launching project CLEAN"
      $psExe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
      $args  = @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$script:cleanScript`"",'-GivenPath',"`"$projectDir`"")
      $proc = Start-Process -FilePath $psExe -ArgumentList $args -WindowStyle Hidden -PassThru
      Log "RUN CLEAN $script:cleanScript -GivenPath `"$projectDir`" (pid=$($proc.Id))"
      return
    }

    # Only consider our target trees (normal file events)
    $t = Resolve-Target $dir
    if (-not $t -or -not $t.Root) { return }

    # Only act when the event is in the exact target root (not subfolders)
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

    # Log event
    if ($eventName -eq 'Renamed' -and $e -is [System.IO.RenamedEventArgs]) {
      $old = ([System.IO.RenamedEventArgs]$e).OldFullPath
      Log "EVT Renamed   : $old -> $($e.FullPath)"
    } else {
      Log "EVT $eventName   : $($e.FullPath)"
    }

    # Deleted -> immediate run
    if ($eventName -eq 'Deleted') {
      Log "DELETE immediate sync for root: $($t.Root)"
      Try-Run -root $t.Root -scriptPath $t.Script -Force
      return
    }

    # Created/Changed/Renamed -> if unlocked+stable, run now (and register post-run); else register locked file
    if (Test-Path -LiteralPath $path -ErrorAction SilentlyContinue) {
      if (Test-FileUnlockedStable -Path $path) {
        $k = $path.ToLower()
        if ($global:LockedFiles.ContainsKey($k)) { $global:LockedFiles.Remove($k) | Out-Null }

        Log "UNLOCKED now -> invoking sync for root: $($t.Root) (file: $path)"
        Try-Run -root $t.Root -scriptPath $t.Script -Force

        # Post-run verification entry to catch late lock release
        $global:LockedFiles[$k] = @{
          Root      = $t.Root
          Script    = $t.Script
          FirstSeen = (Get-Date)
          ExpireAt  = (Get-Date).AddSeconds($global:PostRunRetrySeconds)
          PostRun   = $true
        }
      } else {
        $k = $path.ToLower()
        if (-not $global:LockedFiles.ContainsKey($k)) {
          $global:LockedFiles[$k] = @{ Root = $t.Root; Script = $t.Script; FirstSeen = (Get-Date) }
          Log "LOCKED registered: $path (root: $($t.Root))"
        }
      }
    }

  } catch {
    Log "ERROR Process-Event: $($_.Exception.Message)"
  }
}

# ==========================
# Self-healing FileSystemWatcher
# ==========================
# State for watcher lifecycle
$script:watcher         = $null
$script:subs            = @()
$script:watcherGen      = 0
$script:lastRealEventAt = Get-Date

function Remove-Watcher {
  try {
    if ($script:subs) {
      foreach ($s in ($script:subs | Where-Object { $_ })) {
        try { Unregister-Event -SourceIdentifier $s.SourceIdentifier -ErrorAction SilentlyContinue } catch {}
      }
    }
    $script:subs = @()
    try { Get-Event | Where-Object { $_.SourceIdentifier -like 'FSW.*' } | Remove-Event -ErrorAction SilentlyContinue } catch {}
    if ($script:watcher) {
      try { $script:watcher.EnableRaisingEvents = $false } catch {}
      try { $script:watcher.Dispose() } catch {}
    }
  } catch {
    Log "WARN Remove-Watcher: $($_.Exception.Message)"
  }
}

function New-Watcher {
  if (-not (Test-Path -LiteralPath $script:watchPath)) {
    Log "FATAL: watchPath not found: $script:watchPath"; throw "watchPath missing"
  }

  Remove-Watcher
  $w = New-Object System.IO.FileSystemWatcher
  $w.Path = $script:watchPath
  $w.Filter = "*.*"
  $w.IncludeSubdirectories = $true
  $w.NotifyFilter = [IO.NotifyFilters]'FileName, DirectoryName, LastWrite'
  $w.InternalBufferSize = 65536
  $script:watcher = $w
  $script:watcherGen++
  Log "Watcher (gen $script:watcherGen) created for: $script:watchPath"
  Log "DEBUG: SSI [$script:ssiPattern]"
  Log "DEBUG: RFI [$script:rfiPattern]"
  Log "DEBUG: Stickfile [$script:stickfilePattern]"
  Log "DEBUG: Photos [$script:photosPattern]"
}

function Register-WatcherEvents {
  $script:subs = @()
  $script:subs += Register-ObjectEvent -InputObject $script:watcher -EventName Created -SourceIdentifier 'FSW.Created'
  $script:subs += Register-ObjectEvent -InputObject $script:watcher -EventName Renamed -SourceIdentifier 'FSW.Renamed'
  $script:subs += Register-ObjectEvent -InputObject $script:watcher -EventName Deleted -SourceIdentifier 'FSW.Deleted'
  $script:subs += Register-ObjectEvent -InputObject $script:watcher -EventName Changed -SourceIdentifier 'FSW.Changed'
  $script:subs += Register-ObjectEvent -InputObject $script:watcher -EventName Error   -SourceIdentifier 'FSW.Error'
  $script:watcher.EnableRaisingEvents = $true
  Log "Service started (gen $script:watcherGen) and monitoring $script:watchPath (Created/Renamed/Deleted/Changed)."
}

function Restart-WatcherWithBackoff {
  param([int]$maxWaitSeconds = 300)
  Log "Watcher restart requested; checking path availability: $script:watchPath"
  $elapsed = 0
  while (-not (Test-Path -LiteralPath $script:watchPath)) {
    if ($elapsed -ge $maxWaitSeconds) {
      Log "ERROR Restart-Watcher: path still unavailable after $maxWaitSeconds s"
      break
    }
    Start-Sleep -Seconds 5
    $elapsed += 5
  }
  try {
    New-Watcher
    Register-WatcherEvents
    Log "Watcher restarted successfully (gen $script:watcherGen)."
  } catch {
    Log "ERROR Restart-Watcher: $($_.Exception.Message)"
  }
}

# Initial bring-up
New-Watcher
Register-WatcherEvents

# ---- Main loop + heartbeat
$lastHeartbeat = Get-Date
while ($true) {
  $evt = Wait-Event -Timeout 2
  if ($evt) {
    try {
      $name = $evt.SourceIdentifier
      if ($name -eq 'FSW.Error') {
        $ex = $evt.SourceEventArgs.GetException()
        Log "WATCHER ERROR: $($ex.GetType().FullName) - $($ex.Message)"
        Restart-WatcherWithBackoff
        Remove-Event -EventIdentifier $evt.EventIdentifier -ErrorAction SilentlyContinue
        continue
      } else {
        $script:lastRealEventAt = Get-Date
        Process-Event -e $evt.SourceEventArgs -eventName ($evt.SourceEventArgs.ChangeType.ToString())
      }
    } catch {
      Log "ERROR main loop: $($_.Exception.Message)"
    } finally {
      Remove-Event -EventIdentifier $evt.EventIdentifier -ErrorAction SilentlyContinue
    }
  }

  $now = Get-Date
  if (($now - $lastHeartbeat).TotalMinutes -ge 5) {
    $subCount = (Get-EventSubscriber | Where-Object { $_.SourceIdentifier -like 'FSW.*' } | Measure-Object).Count
    Log "HEARTBEAT OK. Subs: $subCount."

    # Liveness nudge: if no real file events for 24h, cycle watcher
    try {
      if (Test-Path -LiteralPath $script:watchPath) {
        $quietHours = (New-TimeSpan -Start $script:lastRealEventAt -End (Get-Date)).TotalHours
        if ($quietHours -ge 24) {
          Log "LIVENESS: no file events for ${quietHours}h; cycling watcher."
          Restart-WatcherWithBackoff
          $script:lastRealEventAt = Get-Date
        }
      }
    } catch { Log "WARN heartbeat liveness check: $($_.Exception.Message)" }

    $lastHeartbeat = $now
  }
}
