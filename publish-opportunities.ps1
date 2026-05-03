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
    [int]$KeepLast = 3
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot 'Kor.Opportunities.Worker\Kor.Opportunities.Worker.csproj'

if (-not (Test-Path $projectPath)) {
    throw "Project not found at $projectPath. Run this from the repo root."
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$out = Join-Path $PublishRoot $stamp
New-Item -ItemType Directory -Path $out -Force | Out-Null

Write-Host "Publishing to $out" -ForegroundColor Cyan
& dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $out `
    -p:PublishSingleFile=false `
    -p:DebugType=embedded `
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
