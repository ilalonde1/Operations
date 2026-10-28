# Expand-WrappedExecutables.ps1 -- restores the executables of an installer tree that was copied from
# \\Kor-fs01\Library\03 Programs, where on 2026-09-27 EVERY .exe, at every depth, was wrapped in a one-entry STORE .zip
# (and the .exe deleted) to stop Explorer's right-click hang on network executables. Unwrapping only the top-level
# Setup.zip leaves every nested one missing: 28 Oct, Revit 2026 on KOR-210 failed with BS_ERROR_502 because
# ODIS\DownloadManager.exe was still DownloadManager.zip (38 nested, NetworkOps action 11527; share census 11530: 1,304
# nested wrapped executables in 37 product folders).
#
# The unwrap matches the wrap: every *.zip under -Root at ANY depth whose ONLY entry is an .exe is written next to the
# zip under the entry's name, then VERIFIED byte for byte against the zip entry (length + every byte), else deleted and
# reported. A zip is never deleted. An .exe that already exists is compared, never overwritten (a mismatch is reported).
# Windows PowerShell 5.1 compatible (FS01 and the PCs run it), so it can run ON the target via netops run.
#
#   -Check (default): lists every wrapped .exe whose .exe is missing or different; writes nothing. Exit = that count.
#   -Apply          : unwraps; prints one line per zip and a total. Exit = number of failures.
#
# COVERS: one-entry .exe zips at any depth under -Root; byte equality of each restored or existing .exe with its zip.
# DOES NOT COVER: zips holding several files or a non-.exe (left alone and counted as "other"); executables wrapped
# under another extension (.msi, .dll were never wrapped on 09-27, per the memory note -- not re-verified here); an
# installer that also needs its zips GONE (none known; the zips stay); a tree copied before 09-27 (nothing to do).
# SAME-CLASS FAULT IT WOULD MISS: a different "make the share friendlier" rewrite (e.g. a renamed .ex_ or a .bin) that
# a copy carries the same way -- this only knows the one-entry .zip form.
#
#   pwsh tools\Expand-WrappedExecutables.ps1 -Root "C:\Users\administrator\Desktop\Revit 2026 - (EN)"           # check
#   pwsh tools\Expand-WrappedExecutables.ps1 -Root "C:\Users\administrator\Desktop\Revit 2026 - (EN)" -Apply
#   -Quick          : with -Check, compare an existing .exe by LENGTH only (seconds instead of minutes on a 2 GB tree, so
#                     it fits NetworkOps' 90 s road); the output says "length-checked", never "identical".
#   -NestedOnly     : only the executables BELOW each installer root (the shallowest folder on a path that holds a
#                     wrapped exe, e.g. "Revit 2026 - (EN)" with its Setup.zip); the root's own zips stay wrapped, so the
#                     right-click fix holds for what people open. For the share itself (Ian, 28 Oct 12:0x).
#   -Summary        : one row per installer root instead of one line per file (for a whole share).
#   -SkipRoot       : installer roots to leave entirely alone, as wildcards on the path below -Root (e.g. '*Crack*').
param([Parameter(Mandatory)][string]$Root, [switch]$Apply, [switch]$Quick, [switch]$NestedOnly, [switch]$Summary, [string[]]$SkipRoot = @())
$ErrorActionPreference = 'Stop'
if ($Apply -and $Quick) { 'REFUSED: -Quick is for -Check only; -Apply always verifies every byte'; exit 2 }
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not (Test-Path -LiteralPath $Root -PathType Container)) { "ROOT NOT FOUND: $Root"; exit 2 }

# Same bytes as the zip entry: length, then SHA-256 of both streams (native speed; a PowerShell byte loop ran ~3 MB/s,
# hours on the 40 GB share).
function Same-AsEntry($entry, [string]$path) {
    if (([IO.FileInfo]$path).Length -ne $entry.Length) { return $false }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $a = $entry.Open(); try { $ha = [BitConverter]::ToString($sha.ComputeHash($a)) } finally { $a.Dispose() }
        $b = [IO.File]::OpenRead($path); try { $hb = [BitConverter]::ToString($sha.ComputeHash($b)) } finally { $b.Dispose() }
        return $ha -eq $hb
    } finally { $sha.Dispose() }
}

# Pass 1: every wrapped executable (a .zip whose ONLY entry is an .exe), at any depth.
$failed = 0; $other = 0
$items = foreach ($zip in [IO.Directory]::EnumerateFiles($Root, '*.zip', [IO.SearchOption]::AllDirectories)) {
    $archive = $null
    try { $archive = [IO.Compression.ZipFile]::OpenRead($zip) } catch { Write-Output "UNREADABLE: $zip"; $failed++; continue }
    try {
        if ($archive.Entries.Count -ne 1 -or $archive.Entries[0].FullName -notmatch '\.exe$') { $other++; continue }
        [pscustomobject]@{ Zip = $zip; Dir = [IO.Path]::GetDirectoryName($zip); Name = $archive.Entries[0].Name; Root = $null }
    } finally { if ($archive) { $archive.Dispose() } }
}
$items = @($items | Where-Object { $_ -is [pscustomobject] })
$unreadableLines = @($items | Where-Object { $_ -is [string] })

# Installer root of each: the SHALLOWEST folder on its path (at or below -Root) that directly holds a wrapped exe. Its own
# zips are what a person opens (Setup.zip); everything wrapped in a folder below it is part of that installer's tree.
$holders = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($i in $items) { [void]$holders.Add($i.Dir) }
$rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
foreach ($i in $items) {
    $d = $i.Dir; $i.Root = $d
    while ($d.Length -gt $rootFull.Length) {
        $d = [IO.Path]::GetDirectoryName($d)
        if ($null -eq $d -or $d.Length -lt $rootFull.Length) { break }
        if ($holders.Contains($d)) { $i.Root = $d }
    }
}
$rel = { param($p) $p.Substring([Math]::Min($p.Length, $rootFull.Length + 1)) }
$skipped = @($items | Where-Object { $r = & $rel $_.Root; @($SkipRoot | Where-Object { $r -like $_ }).Count -gt 0 })
if ($skipped) { "SKIPPED by -SkipRoot: $($skipped.Count) wrapped executables in $(@($skipped | Select-Object -ExpandProperty Root -Unique).Count) installer root(s): $((@($skipped | ForEach-Object { & $rel $_.Root } | Select-Object -Unique)) -join '; ')" }
$items = @($items | Where-Object { $skipped -notcontains $_ })
$nestedCount = @($items | Where-Object { $_.Dir -ne $_.Root }).Count
$targets = @(if ($NestedOnly) { $items | Where-Object { $_.Dir -ne $_.Root } } else { $items })

# Pass 2: check or unwrap each target.
$restored = 0; $present = 0; $missing = 0; $missingBytes = 0L
$byRoot = @{}
foreach ($i in $targets) {
    if (-not $byRoot.ContainsKey($i.Root)) { $byRoot[$i.Root] = [pscustomobject]@{ InstallerRoot = $i.Root.Substring([Math]::Min($i.Root.Length, $rootFull.Length + 1)); Targets = 0; Missing = 0; MissingGB = 0.0; Restored = 0; Present = 0; Failed = 0 } }
    $r = $byRoot[$i.Root]; $r.Targets++
    $archive = [IO.Compression.ZipFile]::OpenRead($i.Zip)
    try {
        $entry = $archive.Entries[0]
        $exe = Join-Path $i.Dir $i.Name
        if (Test-Path -LiteralPath $exe) {
            $same = if ($Quick) { ([IO.FileInfo]$exe).Length -eq $entry.Length } else { Same-AsEntry $entry $exe }
            if ($same) { $present++; $r.Present++ } else { if (-not $Summary) { "DIFFERENT (left as is): $exe" }; $failed++; $r.Failed++ }
            continue
        }
        if (-not $Apply) { if (-not $Summary) { "MISSING: $exe" }; $missing++; $r.Missing++; $r.MissingGB = [Math]::Round($r.MissingGB + $entry.Length / 1GB, 2); $missingBytes += $entry.Length; continue }
        # written as <name>.exe.partial and renamed only once verified: a stopped run never leaves a half-written .exe
        $part = "$exe.partial"
        if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force }   # only this tool makes .partial files
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $part)
        if (Same-AsEntry $entry $part) { [IO.File]::Move($part, $exe); if (-not $Summary) { "restored: $exe ($($entry.Length) B, verified)" }; $restored++; $r.Restored++ }
        else { Remove-Item -LiteralPath $part -Force; "FAILED verify (removed): $exe"; $failed++; $r.Failed++ }
    } finally { $archive.Dispose() }
}
$unreadableLines
if ($Summary) { $byRoot.Values | Sort-Object Targets -Descending | Format-Table -AutoSize | Out-String -Width 240 }
$scope = if ($NestedOnly) { "$($targets.Count) NESTED (installer-root zips kept as they are: $($items.Count - $nestedCount))" } else { "$($targets.Count)" }
if ($Apply) {
    "TOTAL ${scope} wrapped executables under ${Root} in $($byRoot.Count) installer roots: restored $restored, already there and identical $present, failed $failed; other zips left alone $other"
    exit $failed
}
$how = if ($Quick) { 'present, same length (length-checked only)' } else { 'present and identical' }
"CHECK ${scope} wrapped executables under ${Root} in $($byRoot.Count) installer roots: missing $missing ($([Math]::Round($missingBytes / 1GB, 2)) GB to write), $how $present, different/unreadable $failed; other zips $other"
exit ($missing + $failed)
