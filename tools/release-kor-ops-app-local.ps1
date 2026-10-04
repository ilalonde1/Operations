#Requires -Version 7.0
<#
.SYNOPSIS
    Release the KOR Operations desktop app LOCALLY, to this machine only (for Ian).

.DESCRIPTION
    The fleet flow (tools/deploy-newerforma-app.ps1) drops a versioned zip on the firm share for every
    workstation. This is the opposite: a personal install on the machine it runs on, into C:\KOR-Operations,
    with Start Menu + Desktop shortcuts. Nothing touches the share. Renamed off the old "Newerforma" name.

      1. Publish Release, self-contained, win-x64 -> a temp staging folder.
      2. Cull .playwright (the WPF app never drives a browser at runtime; ~445 MB of foreign-OS binaries).
      3. Stop any instance running FROM the target (ours only), mirror the staging folder into C:\KOR-Operations.
      4. Create "KOR Operations" shortcuts in the Start Menu and on the Desktop.

    The Outlook add-in (EmailFilerv2) is a separate product installed into Outlook, not part of this WPF app,
    so it is not grafted here.

.PARAMETER Target
    Where the app installs. Default C:\KOR-Operations.

.PARAMETER SkipPublish
    Reuse the existing staging publish instead of re-publishing.
#>
param(
    [string]$Target = 'C:\KOR-Operations',
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$repo   = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $repo 'Kor.Operations.App\Kor.Operations.App.csproj'
$stage  = Join-Path $env:TEMP 'kor-ops-app-publish'
$exe    = 'Kor.Operations.App.exe'

if (-not $SkipPublish) {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Write-Host "Publishing Release self-contained win-x64 -> $stage" -ForegroundColor Cyan
    & dotnet publish $csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:PublishTrimmed=false `
        -o $stage --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
    $pw = Join-Path $stage '.playwright'
    if (Test-Path $pw) { Remove-Item $pw -Recurse -Force; Write-Host "Culled .playwright." -ForegroundColor Green }
}
if (-not (Test-Path (Join-Path $stage $exe))) { throw "No $exe in $stage -- publish did not produce the app." }

# Stop only an instance running FROM the target, then mirror the fresh publish in.
Get-Process 'Kor.Operations.App' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path -like "$Target\*" } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
New-Item -ItemType Directory -Force $Target | Out-Null
Write-Host "Installing into $Target ..." -ForegroundColor Cyan
$rc = robocopy $stage $Target /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:1
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)." }
$exePath = Join-Path $Target $exe
if (-not (Test-Path $exePath)) { throw "Install failed: no $exePath." }

# Shortcuts: Start Menu + Desktop.
$ws = New-Object -ComObject WScript.Shell
foreach ($dir in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
    $lnk = $ws.CreateShortcut((Join-Path $dir 'KOR Operations.lnk'))
    $lnk.TargetPath       = $exePath
    $lnk.WorkingDirectory = $Target
    $lnk.IconLocation     = $exePath
    $lnk.Description       = 'KOR Operations'
    $lnk.Save()
}
$sizeMB = [math]::Round((Get-ChildItem $Target -Recurse -File | Measure-Object Length -Sum).Sum/1MB, 0)
Write-Host "RELEASED KOR Operations ($sizeMB MB) to $Target. Shortcuts: Start Menu + Desktop ('KOR Operations')." -ForegroundColor Green
