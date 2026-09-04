#Requires -Version 5.1
<#
  Auto-updating launcher for the Newerforma (Kor.Operations.App) workstation install.

  Replaces the manual "copy V<N>.zip to C:\Newerforma and unzip" step: on every launch it checks the
  share for the newest V<N>.zip and, if that version isn't already unzipped under C:\Newerforma\V<N>,
  pulls + extracts it, prunes old versions, then starts the app. Offline-tolerant (runs whatever's
  installed if the share is unreachable) and lock-tolerant (a running exe is never overwritten — a
  newer version just installs into its own V<N> folder).

  Publish a new V<N>.zip (tools\deploy-newerforma-app.ps1) and every workstation self-updates on its
  next launch. Nothing to copy per machine.
#>
param(
    [string]$Share       = '\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New',
    [string]$InstallRoot = 'C:\Newerforma',
    [int]$KeepVersions   = 2
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Windows.Forms
$exeName = 'Kor.Operations.App.exe'

function Get-NewestZip($share) {
    if (-not (Test-Path $share)) { return $null }
    Get-ChildItem "$share\V*.zip" -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -match '^V\d+$' } |
        Sort-Object { [int]$_.BaseName.Substring(1) } | Select-Object -Last 1
}
function Get-LocalExe($root) {
    if (-not (Test-Path $root)) { return $null }
    Get-ChildItem $root -Recurse -Filter $exeName -ErrorAction SilentlyContinue |
        Sort-Object { if ($_.Directory.Name -match '^V(\d+)$') { [int]$Matches[1] } else { 0 } } |
        Select-Object -Last 1
}

try {
    $zip = Get-NewestZip $Share
    if ($zip) {
        $verDir = Join-Path $InstallRoot $zip.BaseName     # e.g. C:\Newerforma\V18
        $verExe = Join-Path $verDir $exeName
        if (-not (Test-Path $verExe)) {
            $tmp = Join-Path $env:TEMP ("nf-" + [Guid]::NewGuid().ToString('N'))
            [System.IO.Compression.ZipFile]::ExtractToDirectory($zip.FullName, $tmp)
            $srcExe = Get-ChildItem $tmp -Recurse -Filter $exeName -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($srcExe) {
                New-Item -ItemType Directory -Force $InstallRoot | Out-Null
                if (Test-Path $verDir) { Remove-Item $verDir -Recurse -Force }
                Move-Item $srcExe.Directory.FullName $verDir
            }
            Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
            # prune old versions, keep the newest $KeepVersions (never delete a folder whose exe is running)
            Get-ChildItem $InstallRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^V\d+$' } |
                Sort-Object { [int]$_.Name.Substring(1) } -Descending |
                Select-Object -Skip $KeepVersions |
                ForEach-Object { try { Remove-Item $_.FullName -Recurse -Force } catch { } }
        }
    }
} catch { }   # an update hiccup must never stop the app from opening

$exe = Get-LocalExe $InstallRoot
if (-not $exe) {
    [System.Windows.Forms.MessageBox]::Show(
        "Newerforma isn't installed yet and the update share ($Share) is unreachable.",
        "Newerforma", 'OK', 'Warning') | Out-Null
    exit 1
}
Start-Process -FilePath $exe.FullName
