#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes Kor.Opportunities.Worker to a timestamped folder.

.DESCRIPTION
    Framework-dependent (win-x64). KOR-APP01 has the .NET 8 runtime installed
    so we don't need --self-contained. Output goes to a timestamped folder
    under $PublishRoot so we never clobber an artifact that's currently being
    robocopied to the server. Keeps the last 3 publishes; older folders are
    pruned automatically.

    Mirrors publish.ps1 (FileSync) - same conventions on purpose.

.PARAMETER PublishRoot
    Base folder for publish output. Default matches the existing _Publish convention.

.PARAMETER KeepLast
    How many timestamped publish folders to retain. Default 3.

.EXAMPLE
    .\publish-opportunities.ps1
    # Output: C:\VIsual Studio Projects\_Publish\_Ops\Opportunities\20260502_223400\
#>
[CmdletBinding()]
param(
    [string]$PublishRoot = 'C:\VIsual Studio Projects\_Publish\_Ops\Opportunities',
    [int]$KeepLast = 3,
    # Escape hatch for a knowingly-red emergency deploy. Using it is a decision
    # you own; the default is that a red suite produces NO deployable artifact.
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot 'Kor.Opportunities.Worker\Kor.Opportunities.Worker.csproj'

if (-not (Test-Path $projectPath)) {
    throw "Project not found at $projectPath. Run this from the repo root."
}

# ---- TEST GATE (doctrine D1-D4 + behavioral suite) --------------------------
# Commercial-grade rule: a publish artifact cannot exist if the test suite is
# red. This is what makes the doctrine tests ENFORCEMENT rather than advice —
# there is no CI server, so the deploy pipeline is the gate.
if (-not $SkipTests) {
    Write-Host 'Test gate: Kor.Opportunities.Data.Tests (doctrine + behavioral) ...' -ForegroundColor Cyan
    & dotnet test (Join-Path $repoRoot 'Kor.Opportunities.Data.Tests\Kor.Opportunities.Data.Tests.csproj') `
        --configuration Release --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'TEST GATE FAILED - the suite is red, so no publish artifact was produced. Fix the failure (or, for a knowing emergency, re-run with -SkipTests and own it).'
    }
    Write-Host 'Test gate: green.' -ForegroundColor Green
}
else {
    Write-Host 'TEST GATE SKIPPED (-SkipTests) - this deploy is unverified.' -ForegroundColor Yellow
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$out = Join-Path $PublishRoot $stamp
New-Item -ItemType Directory -Path $out -Force | Out-Null

# Stamp a real, deterministic version onto every assembly in the build graph so
# a deploy is verifiable by version, not just file write-time.
#   FileVersion  = 1.0.<days-since-2000>.<minutes-since-midnight>  (numeric, UInt16-safe)
#   ProductVersion (InformationalVersion) = <publish stamp>+<git short SHA>  (human-readable)
$now          = Get-Date
$verBuild     = [int][math]::Floor(($now.Date - [datetime]'2000-01-01').TotalDays)
$verRevision  = [int]$now.TimeOfDay.TotalMinutes
$fileVersion  = "1.0.$verBuild.$verRevision"
$gitShort     = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
$infoVersion  = if ($gitShort) { "$stamp+$gitShort" } else { $stamp }

Write-Host "Publishing to $out" -ForegroundColor Cyan
Write-Host "Version $fileVersion  ($infoVersion)" -ForegroundColor Cyan
& dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $out `
    -p:PublishSingleFile=false `
    -p:DebugType=embedded `
    -p:Version=$fileVersion `
    -p:FileVersion=$fileVersion `
    -p:InformationalVersion=$infoVersion `
    -nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Ship the install/uninstall scripts inside the publish folder so they travel
# with the binaries to the server.
foreach ($script in @('install-opportunities-service.ps1', 'uninstall-opportunities-service.ps1')) {
    $src = Join-Path $repoRoot $script
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $out $script) -Force
    }
}

# Drop a small marker so the deploy step can sanity-check it has the right artifacts.
@{
    PublishedAt    = (Get-Date).ToString('o')
    Stamp          = $stamp
    FileVersion    = $fileVersion
    InfoVersion    = $infoVersion
    GitCommit      = (& git -C $repoRoot rev-parse HEAD 2>$null)
    GitBranch      = (& git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null)
    Configuration  = 'Release'
    Runtime        = 'win-x64'
    SelfContained  = $false
} | ConvertTo-Json | Out-File (Join-Path $out 'publish-info.json') -Encoding utf8

# Prune older publishes, keep last $KeepLast.
$all = Get-ChildItem -Path $PublishRoot -Directory | Sort-Object Name -Descending
if ($all.Count -gt $KeepLast) {
    $toDelete = $all | Select-Object -Skip $KeepLast
    foreach ($d in $toDelete) {
        Write-Host "Pruning old publish: $($d.FullName)" -ForegroundColor DarkGray
        Remove-Item -Recurse -Force $d.FullName
    }
}

Write-Host ""
Write-Host "Publish complete." -ForegroundColor Green
Write-Host "Output:    $out"
Write-Host "Artifacts: $((Get-ChildItem $out -File).Count) files, $([math]::Round((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)) MB"
Write-Host ""
Write-Host "Next: copy '$out' to KOR-APP01:\Program Files\KorOperations\Opportunities\, then run install-opportunities-service.ps1 there (elevated)."
