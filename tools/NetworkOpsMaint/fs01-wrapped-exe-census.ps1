# fs01-wrapped-exe-census.ps1 -- READ-ONLY, runs ON KOR-FS01 (netops run --script <this> --hosts KOR-FS01).
# On 2026-09-27 every .exe under \\Kor-fs01\Library\03 Programs was wrapped in a one-entry STORE .zip (and the .exe
# deleted) to stop Explorer's right-click hang on network executables. Copying an installer TREE from there carries
# every nested executable as a .zip: on 28 Oct the Revit 2026 install on KOR-210 failed (ODIS DownloadManager.exe
# missing, BS_ERROR_502) after only the top-level Setup.zip was unwrapped. This counts, per product folder, the
# one-entry .exe zips at the folder's top level vs nested deeper -- the nested ones are what breaks an install.
# Opens each zip's central directory only (no extraction, no writes).
param([string]$Root = 'E:\Library\03 Programs', [int]$ShowFolders = 400)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Root)) { "ROOT NOT FOUND: $Root"; exit 2 }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$rootLen = $Root.TrimEnd('\').Length + 1
$rows = @{}; $zips = 0; $exeZips = 0; $unreadable = 0; $partial = $false
$deadline = (Get-Date).AddSeconds(75)   # the NetworkOps server route stops waiting at 90 s: stop early and SAY so
# FS01 runs Windows PowerShell 5.1 (.NET Framework): no EnumerationOptions, so AllDirectories (throws on an unreadable
# folder -- reported, not swallowed).
$all = [IO.SearchOption]::AllDirectories
$bareExe = @([IO.Directory]::EnumerateFiles($Root, '*.exe', $all)).Count
foreach ($path in [IO.Directory]::EnumerateFiles($Root, '*.zip', $all)) {
    if ((Get-Date) -gt $deadline) { $partial = $true; break }
    $f = [IO.FileInfo]$path
    $zips++
    try { $a = [IO.Compression.ZipFile]::OpenRead($f.FullName) } catch { $unreadable++; continue }
    try { $isExe = $a.Entries.Count -eq 1 -and $a.Entries[0].FullName -match '\.exe$' } finally { $a.Dispose() }
    if (-not $isExe) { continue }
    $exeZips++
    $rel = $f.FullName.Substring($rootLen).Split('\')
    # product folder = the first two levels under the root (e.g. "Autodesk\Revit 2026 - (EN)"), or the file's own folder
    $prodDepth = [Math]::Min(2, $rel.Count - 1)
    $prod = ($rel[0..($prodDepth - 1)] -join '\'); if (-not $prod) { $prod = '(root)' }
    $nested = $rel.Count - 1 -gt $prodDepth
    if (-not $rows.ContainsKey($prod)) { $rows[$prod] = [pscustomobject]@{ Product = $prod; Top = 0; Nested = 0 } }
    if ($nested) { $rows[$prod].Nested++ } else { $rows[$prod].Top++ }
}
$withNested = @($rows.Values | Where-Object Nested -gt 0)
if ($partial) { "PARTIAL (stopped at the 75 s deadline): the counts below are a SAMPLE of the zips read so far, not the whole root" }
"root $Root : $zips .zip read, $exeZips one-entry .exe zips, $unreadable unreadable zips, $bareExe bare .exe"
"product folders with wrapped exes: $($rows.Count); with NESTED wrapped exes (an installer tree that breaks if copied as is): $($withNested.Count), holding $(($withNested | Measure-Object Nested -Sum).Sum) nested"
$withNested | Sort-Object Nested -Descending | Select-Object -First $ShowFolders | Format-Table -AutoSize | Out-String -Width 220
