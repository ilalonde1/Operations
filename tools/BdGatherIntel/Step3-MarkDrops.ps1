#Requires -Version 7.0
<#
.SYNOPSIS
    Step 3 — Mark canonical orgs with empty Website from Step 1 as WebSearchNotFound.

.DESCRIPTION
    Reads discovered-websites.csv. For every row where Website is empty/null,
    appends Notes='WebSearchNotFound:<today>' on the CanonicalOrg row so the
    DataHealthAuditJob's Q3BareTargets query (and our own bare-orgs export
    Step1-Discover prep) skips them next time.

    Does NOT delete canonicals. Just marks them. Reversible (manually edit
    Notes to remove the marker).

.PARAMETER InputCsv
    Path to Step 1's output. Default: KOR-Data-Honing/outputs/discovered-websites.csv
#>

param(
    [string]$InputCsv = "C:\VIsual Studio Projects\KOR-Data-Honing\outputs\discovered-websites.csv",
    [string]$Today = (Get-Date -Format 'yyyy-MM-dd')
)

$ErrorActionPreference = 'Stop'

$cs = $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB
if ([string]::IsNullOrWhiteSpace($cs)) { throw 'KOR_OPPORTUNITIES_OPPORTUNITIESDB env var not set.' }
if (-not (Test-Path $InputCsv)) { throw "Input CSV not found: $InputCsv" }

Add-Type -AssemblyName System.Data
$c = New-Object System.Data.SqlClient.SqlConnection $cs; $c.Open()
$cmd = $c.CreateCommand()
$cmd.CommandTimeout = 60

$rows = Import-Csv $InputCsv
$drops = $rows | Where-Object { [string]::IsNullOrWhiteSpace($_.Website) }
Write-Host "Step 3 — marking $($drops.Count) canonicals as WebSearchNotFound:$Today (of $($rows.Count) total)" -ForegroundColor Cyan

$marked = 0
$alreadyMarked = 0
foreach ($r in $drops) {
    $id = [long]$r.Id
    $cmd.CommandText = @"
UPDATE opportunities.CanonicalOrg
SET Notes = CONCAT(COALESCE(Notes + '; ', ''), 'WebSearchNotFound:$Today'),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @id AND (Notes IS NULL OR Notes NOT LIKE '%WebSearchNotFound:%');
"@
    [void]$cmd.Parameters.Clear()
    [void]$cmd.Parameters.AddWithValue('@id', $id)
    $n = $cmd.ExecuteNonQuery()
    if ($n -gt 0) { $marked++ } else { $alreadyMarked++ }
}

$c.Close()
Write-Host ""
Write-Host "Marked: $marked"      -ForegroundColor Green
Write-Host "Already marked: $alreadyMarked"
