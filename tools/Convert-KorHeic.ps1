<#
.SYNOPSIS
  Convert HEIC/HEIF photos (iPhone) to JPG, preserving orientation and file timestamps.

.DESCRIPTION
  Wraps ffmpeg, which is already present on KOR machines via the yt-dlp WinGet package -
  nothing needs installing.

  A HEIC stores the image as 512x512 tiles plus a rotation flag. ffmpeg reassembles the
  tiles and applies the rotation, so portrait shots come out portrait. Verified on
  01809-01 (130 E 11 St Deck Repair): 12 files, 9 portrait + 3 landscape, all correct.

  KNOWN LIMITATION - EXIF is NOT carried into the JPG; ffmpeg's JPEG encoder does not
  write it, so camera model, GPS and date-taken are lost from the file metadata. In
  practice iPhone filenames already encode capture time
  (20260820_192417118_iOS = 2026-08-20 19:24:17), and this script copies the source
  file's CreationTime/LastWriteTime onto the output so sorting and dating survive.
  If EXIF itself is required for a job, use a tool that preserves it.

.PARAMETER Path
  Folder containing .heic/.heif files. UNC paths and spaces are fine.

.PARAMETER OutputFolder
  Subfolder to write JPGs into, created if missing. Default 'JPG'.
  Use '.' to write alongside the originals.

.PARAMETER Quality
  ffmpeg -q:v. 2 (best) to 31 (worst). Default 2 is visually lossless and lands at
  roughly the same size as the HEIC. Use 5 for about half the size - good for email.

.PARAMETER Recurse
  Also convert HEIC files in subfolders. Each folder gets its own output subfolder.

.PARAMETER Force
  Overwrite JPGs that already exist. Without this, existing outputs are skipped.

.EXAMPLE
  .\Convert-KorHeic.ps1 -Path "P:\Projects\01 Small Jobs\01809-01 (...)\Photos"

.EXAMPLE
  .\Convert-KorHeic.ps1 -Path "\KOR-FS01\Projects\...\Photos" -Quality 5 -Recurse

.NOTES
  Originals are never modified or deleted.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory, Position = 0)][string]$Path,
    [string]$OutputFolder = 'JPG',
    [ValidateRange(2, 31)][int]$Quality = 2,
    [switch]$Recurse,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Find-Ffmpeg {
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    # ffmpeg ships with the yt-dlp WinGet package on KOR machines
    $roots = @(
        "$env:LOCALAPPDATA\Microsoft\WinGet\Packages",
        "$env:ProgramData\chocolatey\bin",
        "$env:ProgramFiles"
    )
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $hit = Get-ChildItem $r -Recurse -Depth 4 -Filter 'ffmpeg.exe' -ErrorAction SilentlyContinue |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

$ffmpeg = Find-Ffmpeg
if (-not $ffmpeg) {
    throw "ffmpeg not found. Install with:  winget install yt-dlp.FFmpeg"
}
Write-Verbose "ffmpeg: $ffmpeg"

if (-not (Test-Path -LiteralPath $Path)) { throw "Path not found: $Path" }

$src = Get-ChildItem -LiteralPath $Path -File -Recurse:$Recurse -ErrorAction SilentlyContinue |
       Where-Object { $_.Extension -in '.heic', '.heif', '.HEIC', '.HEIF' }

if (-not $src) { Write-Host "No HEIC/HEIF files found in $Path"; return }

Write-Host ("Found {0} HEIC/HEIF file(s), quality -q:v {1}" -f $src.Count, $Quality)

$converted = 0; $skipped = 0; $failed = 0; $bytesIn = 0; $bytesOut = 0
$seenDirs = [System.Collections.Generic.HashSet[string]]::new()

foreach ($f in $src) {
    $destDir = if ($OutputFolder -eq '.') { $f.DirectoryName }
               else { Join-Path $f.DirectoryName $OutputFolder }
    # HashSet.Add returns true only the first time, so the folder is created once
    # per run rather than once per file (keeps -WhatIf output readable)
    if ($seenDirs.Add($destDir) -and -not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    $dest = Join-Path $destDir ($f.BaseName + '.jpg')

    if ((Test-Path -LiteralPath $dest) -and -not $Force) {
        Write-Verbose "skip (exists): $($f.Name)"
        $skipped++
        continue
    }

    if (-not $PSCmdlet.ShouldProcess($f.FullName, "convert to JPG")) { continue }

    # -update 1 -frames:v 1 : single still image, not an image sequence
    $null = & $ffmpeg -hide_banner -loglevel error -y -i $f.FullName `
                      -update 1 -frames:v 1 -q:v $Quality $dest 2>&1

    if ((Test-Path -LiteralPath $dest) -and (Get-Item -LiteralPath $dest).Length -gt 0) {
        # carry the source timestamps across so JPGs sort with the originals
        $o = Get-Item -LiteralPath $dest
        $o.CreationTime  = $f.CreationTime
        $o.LastWriteTime = $f.LastWriteTime
        $bytesIn  += $f.Length
        $bytesOut += $o.Length
        $converted++
        Write-Host ("  OK   {0,-38} {1,6:N1} MB -> {2,6:N1} MB" -f $f.Name, ($f.Length/1MB), ($o.Length/1MB))
    }
    else {
        $failed++
        Write-Warning "FAILED: $($f.Name)"
        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue }
    }
}

Write-Host ""
Write-Host ("converted={0}  skipped={1}  failed={2}" -f $converted, $skipped, $failed)
if ($converted) {
    Write-Host ("{0:N1} MB in -> {1:N1} MB out" -f ($bytesIn/1MB), ($bytesOut/1MB))
}
if ($failed) { Write-Warning "$failed file(s) failed - rerun with -Verbose for detail" }
