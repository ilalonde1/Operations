<#
    Publishes one project's ETABS model and everything that ships beside it, in a single run.

    Staleness is not a discipline problem, it is a ritual problem. Producing the model, the report,
    the questionnaire and the dossier as four separate hand-run steps guarantees that one of them
    lags the others the moment the code changes again — which is exactly what kept happening: the
    model regenerated, the dossier left behind, quoting counts that were two rounds old.

    One command, or it drifts.

    Example:
      .\tools\Publish-EtabsModel.ps1 -Project 31168
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('31168', '31138')][string]$Project,
    [switch]$SkipDossier
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

$config = @{
    '31168' = @{
        Folder    = '\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models'
        Dxf       = '\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\_DXF-plans-for-rebuild'
        Reference = '31168-reference.e2k'
    }
    '31138' = @{
        Folder    = '\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models'
        Dxf       = '\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\_DXF-plans-for-rebuild'
        Reference = '31138-reference-from-Andrea-gravity.e2k'
    }
}[$Project]

$folder = $config.Folder
$out    = Join-Path $folder "$Project-FROM-DRAWINGS.e2k"

# The CLI is not rebuilt by `dotnet test`, so a stale exe silently publishes yesterday's rules.
Write-Host 'building the CLI...' -ForegroundColor DarkGray
& dotnet build (Join-Path $repo 'Kor.Operations.EngineeringTools.TakeoffCli') --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }

$cli = Join-Path $repo 'Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe'

Write-Host "generating $Project..." -ForegroundColor DarkGray
& $cli dxf-to-etabs $config.Dxf (Join-Path $folder $config.Reference) $out `
    --report (Join-Path $folder "$Project-FROM-DRAWINGS-report.txt") `
    --questions (Join-Path $folder "$Project-QUESTIONS-for-Andrea.xlsx") |
    Select-String -Pattern 'Storeys built|^Walls|^Columns|^Floors'
if ($LASTEXITCODE -ne 0) { throw 'generation failed.' }

if (-not $SkipDossier) {
    $dossier = Join-Path $repo 'docs\KOR-DxfToEtabs-web.pdf'
    if (Test-Path $dossier) {
        Copy-Item $dossier (Join-Path $folder 'KOR-Model-From-Drawings-DOSSIER.pdf') -Force
    }
}

# Nothing ships that predates the code that made it.
$newestSource = (Get-ChildItem (Join-Path $repo 'Kor.Operations.EngineeringTools.Core\Dxf') -Filter '*.cs' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime

$stale = Get-ChildItem $folder -File |
    Where-Object { $_.Name -match 'FROM-DRAWINGS|QUESTIONS|DOSSIER' -and $_.LastWriteTime -lt $newestSource }

Write-Host ''
Get-ChildItem $folder -File |
    Where-Object { $_.Name -match 'FROM-DRAWINGS|QUESTIONS|DOSSIER' } |
    Sort-Object Name |
    ForEach-Object { '  {0,-44} {1,7:N0} KB  {2:HH:mm}' -f $_.Name, ($_.Length / 1kb), $_.LastWriteTime }

if ($stale) {
    Write-Host ''
    Write-Host 'STALE — these predate the source that built them:' -ForegroundColor Red
    $stale | ForEach-Object { Write-Host ('  ' + $_.Name) -ForegroundColor Red }
    exit 1
}

Write-Host ''
Write-Host "$Project published, nothing stale." -ForegroundColor Green
