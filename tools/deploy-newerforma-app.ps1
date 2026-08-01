#Requires -Version 7.0
<#
.SYNOPSIS
    Deploy the Kor.Operations.App (NewerForma WPF desktop app) to the firm share
    as a versioned V<N>.zip.

.DESCRIPTION
    There is no MSBuild/ClickOnce pipeline for this app — distribution is a manual
    versioned-zip drop. This script encapsulates the verified flow (first run for
    V13, 2026-06-20):

      1. Publish Release, self-contained, win-x64 -> _Publish\_Ops\V<N>\
      2. Cull the .playwright folder (~445 MB). The WPF app pulls Microsoft.Playwright
         only transitively via Kor.Opportunities.Data and never drives a browser at
         runtime (scrapers run in the Worker + tools/). The .playwright\node\{darwin,
         linux}-* binaries are foreign-OS executables a Windows app cannot run.
      3. Graft the EmailFilerv2 Outlook add-in (setup.exe + EmailFilerv2.vsto +
         Application Files\EmailFilerv2_*\) forward UNCHANGED from the previous V<N>.zip.
         The add-in is a separate product bundled in the same package; do not rebuild it.
      4. Zip the V<N> folder with the V<N>/ wrapper (CreateFromDirectory includeBase=$true).
      5. Copy to the share, leaving the prior V<N>.zip as rollback, and verify SHA256.

    VERIFY BY RUNNING before trusting a deploy: launch the culled V<N> exe and drive
    BD -> Make a Brief -> single-click an org -> Generate. A build that compiles can
    still fail to launch.

.PARAMETER Version
    Integer package version, e.g. 13 -> V13.zip. History is non-sequential and the
    number is an operator decision, so this is mandatory (no auto-increment).

.PARAMETER ShareNewDir
    The \New\ folder on the firm share that holds the V<N>.zip packages.

.PARAMETER PublishRoot
    Local staging root for the V<N> publish folder.

.PARAMETER SkipPublish
    Reuse an existing _Publish\_Ops\V<N> folder instead of re-publishing.

.EXAMPLE
    .\tools\deploy-newerforma-app.ps1 -Version 14
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int]$Version,
    [string]$ShareNewDir = '\\KOR-FS01\Library\11 IT\_Applications\Newerforma\New',
    [string]$PublishRoot = 'C:\VIsual Studio Projects\_Publish\_Ops',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot   = Split-Path $PSScriptRoot -Parent
$projectCsproj = Join-Path $repoRoot 'Kor.Operations.App\Kor.Operations.App.csproj'
$tag        = "V$Version"
$stageDir   = Join-Path $PublishRoot $tag
$localZip   = Join-Path $PublishRoot "$tag.zip"
$destZip    = Join-Path $ShareNewDir "$tag.zip"

function Folder-SizeMB($p) { [math]::Round((Get-ChildItem $p -Recurse -File -EA SilentlyContinue | Measure-Object Length -Sum).Sum/1MB,1) }

# --- Guard: never clobber an existing package on the share ---
if (Test-Path $destZip) { throw "$tag.zip already exists on the share. Pick a new version or remove it deliberately first." }

# --- Locate the previous package on the share: graft source + rollback reference ---
$prevZip = Get-ChildItem $ShareNewDir -Filter 'V*.zip' |
    Where-Object { $_.BaseName -match '^V(\d+)$' -and [int]$Matches[1] -lt $Version } |
    Sort-Object { [int]($_.BaseName.Substring(1)) } -Descending | Select-Object -First 1
if (-not $prevZip) { throw "No previous V<N>.zip found on the share to graft the EmailFilerv2 add-in from." }
Write-Host "Previous package (graft source + rollback): $($prevZip.Name)" -ForegroundColor DarkCyan

# --- 1. Publish ---
if ($SkipPublish -and (Test-Path (Join-Path $stageDir 'Kor.Operations.App.exe'))) {
    Write-Host "SkipPublish: reusing $stageDir" -ForegroundColor Yellow
} else {
    if (Test-Path $stageDir) { [System.IO.Directory]::Delete($stageDir, $true) }
    Get-Process Kor.Operations.App -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
    Write-Host "Publishing Release self-contained win-x64 -> $stageDir" -ForegroundColor Cyan
    & dotnet publish $projectCsproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:PublishTrimmed=false `
        -o $stageDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
}
Write-Host ("  staged: " + (Folder-SizeMB $stageDir) + " MB") -ForegroundColor Gray

# --- 2. Cull .playwright (app never drives a browser at runtime) ---
$pw = Join-Path $stageDir '.playwright'
if (Test-Path $pw) {
    $b = Folder-SizeMB $stageDir
    [System.IO.Directory]::Delete($pw, $true)
    Write-Host ("Culled .playwright: " + $b + " MB -> " + (Folder-SizeMB $stageDir) + " MB") -ForegroundColor Green
}

# --- 3. Graft EmailFilerv2 add-in forward from the previous zip (unchanged, minus .playwright) ---
$existing = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
Get-ChildItem $stageDir -Recurse -File | ForEach-Object { [void]$existing.Add($_.FullName.Substring($stageDir.Length + 1).Replace('\','/')) }
$zip = [System.IO.Compression.ZipFile]::OpenRead($prevZip.FullName)
$grafted = 0
foreach ($e in $zip.Entries) {
    if ($e.FullName -match '/$') { continue }
    $rel = $e.FullName -replace '^V\d+/', ''
    if ($rel -match '^\.playwright/') { continue }
    if ($existing.Contains($rel)) { continue }
    $dest = Join-Path $stageDir ($rel -replace '/','\')
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
    $grafted++
}
$zip.Dispose()
Write-Host "Grafted $grafted carry-forward file(s) from $($prevZip.Name) (e.g. EmailFilerv2 add-in)." -ForegroundColor Cyan

# --- 4. Zip with V<N>/ wrapper ---
if ([System.IO.File]::Exists($localZip)) { [System.IO.File]::Delete($localZip) }
Write-Host "Zipping $tag ..." -ForegroundColor Cyan
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $localZip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
Write-Host ("  $tag.zip = " + [math]::Round((Get-Item $localZip).Length/1MB,1) + " MB") -ForegroundColor Green

# --- Parity check vs previous: only intended diff is the .playwright cull ---
function Get-FileEntries($path) {
    $z = [System.IO.Compression.ZipFile]::OpenRead($path)
    $s = $z.Entries | Where-Object { $_.FullName -notmatch '/$' } | ForEach-Object { ($_.FullName -replace '^V\d+/','') }
    $z.Dispose(); return $s
}
$fp = Get-FileEntries $prevZip.FullName
$fn = [System.Collections.Generic.HashSet[string]]::new([string[]](Get-FileEntries $localZip), [System.StringComparer]::OrdinalIgnoreCase)
$missingOther = @($fp | Where-Object { $_ -notmatch '^\.playwright/' -and -not $fn.Contains($_) })
if ($missingOther.Count -gt 0) {
    Write-Host "PARITY FAIL — non-playwright files present in $($prevZip.Name) but missing from ${tag}:" -ForegroundColor Red
    $missingOther | Select-Object -First 30 | ForEach-Object { Write-Host "   - $_" }
    throw "Parity check failed; not copying to share."
}
Write-Host "Parity OK: only the intended .playwright cull differs from $($prevZip.Name)." -ForegroundColor Green

# --- 5. Copy to share + verify SHA256 ---
$srcHash = (Get-FileHash $localZip -Algorithm SHA256).Hash
Write-Host "Copying to $destZip ..." -ForegroundColor Cyan
Copy-Item $localZip $destZip
$dstHash = (Get-FileHash $destZip -Algorithm SHA256).Hash
if ($srcHash -ne $dstHash) { throw "Hash mismatch after copy (src $srcHash / dst $dstHash)." }
Write-Host "HASH MATCH on share ($srcHash)." -ForegroundColor Green
Write-Host ""
Write-Host "Deployed $tag. Prior $($prevZip.Name) retained as rollback. Install on workstations manually." -ForegroundColor Green
