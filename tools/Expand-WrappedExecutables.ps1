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
param([Parameter(Mandatory)][string]$Root, [switch]$Apply, [switch]$Quick)
$ErrorActionPreference = 'Stop'
if ($Apply -and $Quick) { 'REFUSED: -Quick is for -Check only; -Apply always verifies every byte'; exit 2 }
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not (Test-Path -LiteralPath $Root -PathType Container)) { "ROOT NOT FOUND: $Root"; exit 2 }

function Same-AsEntry($entry, [string]$path) {
    $fi = [IO.FileInfo]$path
    if ($fi.Length -ne $entry.Length) { return $false }
    $a = $entry.Open(); $b = [IO.File]::OpenRead($path)
    try {
        $ba = New-Object byte[] 1048576; $bb = New-Object byte[] 1048576
        while ($true) {
            $na = 0; while ($na -lt $ba.Length) { $r = $a.Read($ba, $na, $ba.Length - $na); if ($r -le 0) { break }; $na += $r }
            $nb = 0; while ($nb -lt $bb.Length) { $r = $b.Read($bb, $nb, $bb.Length - $nb); if ($r -le 0) { break }; $nb += $r }
            if ($na -ne $nb) { return $false }
            if ($na -eq 0) { return $true }
            for ($i = 0; $i -lt $na; $i++) { if ($ba[$i] -ne $bb[$i]) { return $false } }
        }
    } finally { $a.Dispose(); $b.Dispose() }
}

$wrapped = 0; $restored = 0; $present = 0; $missing = 0; $failed = 0; $other = 0
foreach ($zip in [IO.Directory]::EnumerateFiles($Root, '*.zip', [IO.SearchOption]::AllDirectories)) {
    $archive = $null
    try { $archive = [IO.Compression.ZipFile]::OpenRead($zip) } catch { "UNREADABLE: $zip"; $failed++; continue }
    try {
        if ($archive.Entries.Count -ne 1 -or $archive.Entries[0].FullName -notmatch '\.exe$') { $other++; continue }
        $wrapped++
        $entry = $archive.Entries[0]
        $exe = Join-Path ([IO.Path]::GetDirectoryName($zip)) $entry.Name
        if (Test-Path -LiteralPath $exe) {
            $same = if ($Quick) { ([IO.FileInfo]$exe).Length -eq $entry.Length } else { Same-AsEntry $entry $exe }
            if ($same) { $present++ } else { "DIFFERENT (left as is): $exe"; $failed++ }
            continue
        }
        if (-not $Apply) { "MISSING: $exe"; $missing++; continue }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $exe)
        if (Same-AsEntry $entry $exe) { "restored: $exe ($($entry.Length) B, verified)"; $restored++ }
        else { Remove-Item -LiteralPath $exe -Force; "FAILED verify (removed): $exe"; $failed++ }
    } finally { if ($archive) { $archive.Dispose() } }
}
if ($Apply) {
    "TOTAL ${wrapped} wrapped executables under ${Root}: restored $restored, already there and identical $present, failed $failed; other zips left alone $other"
    exit $failed
}
$how = if ($Quick) { 'present, same length (length-checked only)' } else { 'present and identical' }
"CHECK ${wrapped} wrapped executables under ${Root}: missing $missing, $how $present, different/unreadable $failed; other zips $other"
exit ($missing + $failed)
