#Requires -Version 5.1
<#
  One-time per-workstation setup for the auto-updating Newerforma install. Run once per machine
  (or push via login script / RMM). It drops the launcher at the install root, makes Start Menu +
  Desktop shortcuts that run it, and does the first pull so the app is ready immediately. After this
  the workstation self-updates to the newest published V<N>.zip on each launch — no manual copy/unzip.
#>
param(
    [string]$Share       = '\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New',
    [string]$InstallRoot = 'C:\Newerforma',
    [switch]$NoDesktopShortcut
)
$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force $InstallRoot | Out-Null
$launcher = Join-Path $InstallRoot 'Launch-Newerforma.ps1'
Copy-Item (Join-Path $PSScriptRoot 'Launch-Newerforma.ps1') $launcher -Force
Write-Host "Launcher: $launcher" -ForegroundColor Green

Write-Host "First pull..." -ForegroundColor Cyan
& $launcher -Share $Share -InstallRoot $InstallRoot

# Stable icon (extracted once from the current exe; survives version bumps)
$iconPath = Join-Path $InstallRoot 'app.ico'
try {
    Add-Type -AssemblyName System.Drawing
    $exe = Get-ChildItem $InstallRoot -Recurse -Filter 'Kor.Operations.App.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($exe) {
        $ico = [System.Drawing.Icon]::ExtractAssociatedIcon($exe.FullName)
        $fs = [System.IO.File]::Create($iconPath); $ico.Save($fs); $fs.Close()
    }
} catch { $iconPath = $null }

$psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$launchArgs = "-WindowStyle Hidden -ExecutionPolicy Bypass -File `"$launcher`" -Share `"$Share`" -InstallRoot `"$InstallRoot`""
$ws = New-Object -ComObject WScript.Shell
$targets = @( (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Newerforma.lnk') )
if (-not $NoDesktopShortcut) { $targets += (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Newerforma.lnk') }
foreach ($lnkPath in $targets) {
    $lnk = $ws.CreateShortcut($lnkPath)
    $lnk.TargetPath       = $psExe
    $lnk.Arguments        = $launchArgs
    $lnk.WorkingDirectory = $InstallRoot
    if ($iconPath) { $lnk.IconLocation = "$iconPath,0" }
    $lnk.Description       = 'Newerforma (auto-updating)'
    $lnk.Save()
    Write-Host "Shortcut: $lnkPath" -ForegroundColor Green
}
Write-Host "Done. Launch 'Newerforma' from the Start Menu / Desktop; it self-updates from the share." -ForegroundColor Green
