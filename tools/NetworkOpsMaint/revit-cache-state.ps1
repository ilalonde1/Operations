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
# [string]: Get-Content lines carry PS note properties that serialise to megabytes (16 MB on KOR-305, 27 Oct).
$last = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 4000 -ErrorAction SilentlyContinue | Where-Object { $_ -match 'copy skipped|NOT stamped' } | Select-Object -Last 4 | ForEach-Object { [string]$_ } } else { @('no loader.log') }
$logLines = if (Test-Path -LiteralPath $log) { @(Get-Content -LiteralPath $log -Tail 4000 -ErrorAction SilentlyContinue | Where-Object { $_ -match 'copy skipped|NOT stamped' }).Count } else { $null }
[pscustomobject]@{
  Revit = @(Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object { "pid $($_.Id) $($_.Path)" })
  Rows = @($rows); SharePathFile = $share; LoaderSkipLinesInLast4000 = $logLines; LoaderLast = @($last)
}
