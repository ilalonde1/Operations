# ON a Revit PC as SYSTEM, READ-ONLY: per user profile and Revit year, is the KOR RevitTools cache stamped current, and is
# the year opted in to the folded bridge (KOR.Drafter.addin pointing into the loader's own cache)? For Revit Fleet's
# bridge-lock finding (oversight commitment 65, NetworkOps card 35). Reads files only: no writes, no process touched.
$share = $null
$rows = foreach ($p in Get-ChildItem 'C:\Users' -Directory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin 'Public', 'Default', 'Default User', 'All Users' }) {
  $cacheRoot = Join-Path $p.FullName 'AppData\Local\KOR\RevitTools'
  $addinRoot = Join-Path $p.FullName 'AppData\Roaming\Autodesk\Revit\Addins'
  $years = @()
  if (Test-Path -LiteralPath $cacheRoot) { $years += Get-ChildItem -LiteralPath $cacheRoot -Directory -ErrorAction SilentlyContinue | Where-Object Name -match '^20\d\d$' | ForEach-Object Name }
  if (Test-Path -LiteralPath $addinRoot) { $years += Get-ChildItem -LiteralPath $addinRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^20\d\d$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'KOR.Drafter.addin')) } | ForEach-Object Name }
  foreach ($y in $years | Sort-Object -Unique) {
    $v = Join-Path $cacheRoot "$y\version.txt"
    $stamp = if (Test-Path -LiteralPath $v) { (Get-Content -LiteralPath $v -Raw -ErrorAction SilentlyContinue).Trim() } else { $null }
    $addin = Join-Path $addinRoot "$y\KOR.Drafter.addin"
    $assembly = if (Test-Path -LiteralPath $addin) { ([regex]::Match((Get-Content -LiteralPath $addin -Raw -ErrorAction SilentlyContinue), '<Assembly>\s*([^<]+?)\s*</Assembly>')).Groups[1].Value } else { $null }
    if (-not $share) { $kp = Join-Path $cacheRoot "$y\kor-deploy.path"; if (Test-Path -LiteralPath $kp) { $share = (Get-Content -LiteralPath $kp -TotalCount 1).Trim() } }
    [pscustomobject]@{ User = $p.Name; Year = $y; CacheStamp = $stamp; DrafterAddin = [bool]$assembly; DrafterAssembly = $assembly
      OptedIn = [bool]($assembly -and $assembly -match '\\AppData\\Local\\KOR\\RevitTools\\') }
  }
}
$log = 'C:\ProgramData\KOR\logs\loader.log'
# Per row: current / behind / stuck (Revit Fleet's ask, 27 Oct). The share's own stamp per year, read once:
#   current  = the cache's stamp equals <share>\current\<year>\version.txt
#   behind   = it differs and loader.log has no "copy skipped"/"NOT stamped" line since that share stamp was written --
#              benign: Revit has not launched since the publish, the next launch syncs
#   stuck    = it differs AND loader.log has such a line since the publish -- a launch tried and failed to update
#   no cache = no version.txt for that year
# A "copy skipped" line names its year in the file's path (...\RevitTools\2026\...) and counts for that year only; a
# "NOT stamped" line names none and counts for every year. Misses: a failure logged in another wording.
$shareStamp = @{}
# the loader's own share pointer (C:\ProgramData\Autodesk\Revit\Addins\<year>\KOR.Loader\kor-deploy.path, read 30 Oct on
# KOR-306); the per-user cache does not carry one
if (-not $share) {
  $kp = Get-ChildItem 'C:\ProgramData\Autodesk\Revit\Addins\*\KOR.Loader\kor-deploy.path' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($kp) { $share = (Get-Content -LiteralPath $kp.FullName -TotalCount 1).Trim() }
}
$skipTimes = @(if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 4000 -ErrorAction SilentlyContinue | Where-Object { $_ -match 'copy skipped|NOT stamped' } |
  ForEach-Object { $l = [string]$_; if ($l -match '^(\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d)') {
    [pscustomobject]@{ At = [datetime]$Matches[1]; Year = if ($l -match '\\RevitTools\\(20\d\d)\\') { $Matches[1] } else { $null } } } } })
foreach ($r in @($rows)) {
  if ($share -and -not $shareStamp.ContainsKey($r.Year)) {
    $sv = Join-Path $share "current\$($r.Year)\version.txt"
    $shareStamp[$r.Year] = if (Test-Path -LiteralPath $sv) { [pscustomobject]@{ Stamp = (Get-Content -LiteralPath $sv -Raw).Trim(); At = (Get-Item -LiteralPath $sv).LastWriteTime } } else { $null }
  }
  $s = $shareStamp[$r.Year]
  $state = if (-not $r.CacheStamp) { 'no cache' } elseif (-not $s) { 'share unknown' } elseif ($r.CacheStamp -eq $s.Stamp) { 'current' }
           elseif (@($skipTimes | Where-Object { $_.At -ge $s.At -and (-not $_.Year -or $_.Year -eq $r.Year) }).Count) { 'stuck' } else { 'behind' }
  $r | Add-Member -NotePropertyName ShareStamp -NotePropertyValue $(if ($s) { [string]$s.Stamp } else { $null })
  $r | Add-Member -NotePropertyName State -NotePropertyValue $state
}
# [string]: Get-Content lines carry PS note properties that serialise to megabytes (16 MB on KOR-305, 27 Oct).
$last = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 4000 -ErrorAction SilentlyContinue | Where-Object { $_ -match 'copy skipped|NOT stamped' } | Select-Object -Last 4 | ForEach-Object { [string]$_ } } else { @('no loader.log') }
$logLines = if (Test-Path -LiteralPath $log) { @(Get-Content -LiteralPath $log -Tail 4000 -ErrorAction SilentlyContinue | Where-Object { $_ -match 'copy skipped|NOT stamped' }).Count } else { $null }
[pscustomobject]@{
  Revit = @(Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object { "pid $($_.Id) $($_.Path)" })
  Rows = @($rows); SharePathFile = $share; LoaderSkipLinesInLast4000 = $logLines; LoaderLast = @($last)
}
