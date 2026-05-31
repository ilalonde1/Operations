<#
.SYNOPSIS
  Regenerates the 3 KOR-Data-Honing input CSVs from the current
  KorOpportunitiesDb state, so a fresh Sonnet honing pass starts with
  worklists that match what actually lives in the DB (vs stale IDs from
  prior dedup/merge rounds).

.NOTES
  Round 43 (2026-05-30). Run from anywhere; output paths are absolute.
  Requires $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB to be set.

  Filter logic mirrors what Sonnet was working from on May 29 — all canonical
  orgs in real business kinds (excludes Vendor/Unknown junk), BC + AB MPI
  projects that aren't retired or completed, and currently-open opportunities.
#>
$ErrorActionPreference = 'Stop'

$dst = 'C:\VIsual Studio Projects\KOR-Data-Honing\inputs'
$cs  = $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB
if (-not $cs) { throw 'KOR_OPPORTUNITIES_OPPORTUNITIESDB env var not set.' }
if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst | Out-Null }

function Export-Csv-FromSql {
  param([string]$Sql, [string]$Out)
  $con = New-Object System.Data.SqlClient.SqlConnection $cs
  $con.Open()
  $cmd = $con.CreateCommand()
  $cmd.CommandText = $Sql
  $cmd.CommandTimeout = 120
  $r = $cmd.ExecuteReader()
  $rows = New-Object System.Collections.Generic.List[string]
  # Header
  $hdr = @()
  for ($i = 0; $i -lt $r.FieldCount; $i++) { $hdr += '"' + $r.GetName($i) + '"' }
  $rows.Add(($hdr -join ','))
  # Data
  while ($r.Read()) {
    $vals = @()
    for ($i = 0; $i -lt $r.FieldCount; $i++) {
      $v = if ($r.IsDBNull($i)) { '' } else { $r.GetValue($i).ToString() }
      $v = $v -replace '"','""'
      $vals += ('"' + $v + '"')
    }
    $rows.Add(($vals -join ','))
  }
  $con.Close()
  [System.IO.File]::WriteAllLines($Out, $rows, (New-Object System.Text.UTF8Encoding $false))
  Write-Host ("Wrote {0} rows (incl header) -> {1}" -f $rows.Count, $Out)
}

# ---- orgs-to-hone.csv -----------------------------------------------------
$sqlOrgs = @"
SELECT  Id,
        Kind,
        DisplayName,
        ISNULL(Website, '')         AS Website,
        ISNULL(CAST(ClendorClientId AS NVARCHAR(50)), '') AS ClendorClientId,
        ISNULL(Notes, '')           AS Notes
FROM    opportunities.CanonicalOrg
WHERE   Kind IN
        ('Architect','Competitor','GC','Developer','KorClient','KorStructural',
         'Buyer','Client','Subcontractor')
ORDER BY DisplayName;
"@
Export-Csv-FromSql -Sql $sqlOrgs -Out (Join-Path $dst 'orgs-to-hone.csv')

# ---- projects-to-hone.csv -------------------------------------------------
$sqlProjects = @"
SELECT  m.Id,
        m.ProjectName,
        m.Province,
        ISNULL(m.Sector, '')              AS Sector,
        ISNULL(m.SubSector, '')           AS SubSector,
        ISNULL(m.EstimatedCostText, '')   AS EstimatedCostText,
        ISNULL(m.Stage, '')               AS Stage,
        ISNULL(m.ProjectStage, '')        AS ProjectStage,
        ISNULL(m.ProjectStatus, '')       AS ProjectStatus,
        ISNULL(m.ProponentName, '')       AS ProponentName,
        ISNULL(m.ArchitectName, '')       AS ArchitectName,
        ISNULL(m.MunicipalityName, '')    AS MunicipalityName,
        ISNULL(CAST(m.StartYear AS NVARCHAR(10)), '')      AS StartYear,
        ISNULL(CAST(m.CompletionYear AS NVARCHAR(10)), '') AS CompletionYear,
        ISNULL(m.SourceUrl, '')           AS SourceUrl
FROM    opportunities.MajorProjectsInventory m
WHERE   m.RetiredAtUtc IS NULL
  AND   m.Province IN ('BC','AB')
  AND   (m.Stage IS NULL OR m.Stage NOT IN ('Completed','Cancelled'))
ORDER BY m.Province, m.ProjectName;
"@
Export-Csv-FromSql -Sql $sqlProjects -Out (Join-Path $dst 'projects-to-hone.csv')

# ---- opps-to-validate.csv -------------------------------------------------
$sqlOpps = @"
SELECT  o.Id,
        o.Name,
        ISNULL(o.ProjectProvince, '')           AS ProjectProvince,
        ISNULL(o.BuyerName, '')                 AS BuyerName,
        ISNULL(CAST(o.IsPrimeConsultantRfp AS NVARCHAR(10)), '') AS IsPrimeConsultantRfp,
        ISNULL(o.PrimeProjectSector, '')        AS PrimeProjectSector,
        ISNULL(o.PrimeLikelyType, '')           AS PrimeLikelyType
FROM    opportunities.Opportunities o
-- Status is int-typed (OpportunityStatus enum). Filter on "has any prime-RFP signal"
-- instead of trying to map status integers; that's what Sonnet's track-4 task is
-- about anyway. Keeps the worklist focused on rows where validation matters.
WHERE   o.IsPrimeConsultantRfp = 1
   OR   o.PrimeProjectSector IS NOT NULL
   OR   o.PrimeLikelyType IS NOT NULL
ORDER BY o.Id DESC;
"@
Export-Csv-FromSql -Sql $sqlOpps -Out (Join-Path $dst 'opps-to-validate.csv')

Write-Host ""
Write-Host "=== Done. Worklist sizes (excluding header): ==="
Get-ChildItem $dst -Filter '*.csv' | ForEach-Object {
  $count = (Get-Content $_.FullName | Measure-Object -Line).Lines - 1
  Write-Host ("  {0,-30} {1,6} rows" -f $_.Name, $count)
}
