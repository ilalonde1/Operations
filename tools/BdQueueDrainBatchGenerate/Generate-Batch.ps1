#requires -Version 7

<#
.SYNOPSIS
    Generate a Sonnet drain batch JSON for the named kind. Replaces the
    ad-hoc PowerShell that was used before the 2026-06-07 audit caught the
    silent-failure incident.

.DESCRIPTION
    Pulls candidates from KorOpportunitiesDb with INPUT-QUALITY filters that
    keep garbled/all-caps/multi-entity rows OUT of the batch (those caused
    Sonnet to burn 5 hours of subscription time on unresearchable names).

    Filters built in:
    - Case-sensitive collate (rejects all-caps Deltek legacy names)
    - Trailing stopword rejection (AND/OR/INT/ET C/comma-terminated)
    - Slash + semicolon rejection (multi-entity garbage)
    - Parenthetical scope descriptor rejection
    - Placeholder single-word kind labels rejection
    - TBD/TBA word-boundary rejection
    - Minimum length + must-contain-space

.PARAMETER Kind
    One of: orgs | projects | people | proponents

.PARAMETER BatchNumber
    The batch number to write (e.g., 5 produces batch-005.json).

.PARAMETER Take
    Max rows in the batch. Default 200.

.PARAMETER OutDir
    Override the output directory. Defaults to
    C:\ProgramData\KorOperations\QueueDrain\<Kind>\inputs\

.PARAMETER ConnectionString
    Override the DB connection. Defaults to env var
    KOR_OPPORTUNITIES_OPPORTUNITIESDB.

.EXAMPLE
    .\Generate-Batch.ps1 -Kind orgs -BatchNumber 6

.NOTES
    After generating, launch Sonnet at the queue directory and it will
    auto-discover the new batch via the PROMPT.md auto-discovery rule.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('orgs', 'projects', 'people', 'proponents')]
    [string] $Kind,

    [Parameter(Mandatory)][int] $BatchNumber,

    [int] $Take = 200,

    [string] $OutDir,

    [string] $ConnectionString = $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB
)

if (-not $ConnectionString) {
    throw "ConnectionString not set. Pass -ConnectionString or set env var KOR_OPPORTUNITIES_OPPORTUNITIESDB."
}
if (-not $OutDir) {
    $OutDir = "C:\ProgramData\KorOperations\QueueDrain\$Kind\inputs"
}
if (-not (Test-Path $OutDir)) {
    throw "Output dir not found: $OutDir"
}

$batchPadded = "{0:D3}" -f $BatchNumber
$outFile = Join-Path $OutDir "batch-$batchPadded.json"

if (Test-Path $outFile) {
    throw "Refusing to overwrite existing batch: $outFile (delete or rename first)"
}

Add-Type -AssemblyName System.Data
$conn = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$conn.Open()
try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandTimeout = 180
    $cmd.Parameters.AddWithValue("@take", $Take) | Out-Null

    switch ($Kind) {
        'orgs' {
            $cmd.CommandText = @"
SELECT TOP (@take) co.Id, co.DisplayName, co.Kind
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND co.Kind IN (N'Architect', N'GC', N'Developer', N'Buyer', N'Competitor', N'KorClient')
  AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment e
                  WHERE e.CanonicalOrgId = co.Id AND e.ProviderName = N'FirmNarrative'
                    AND e.LastRefreshAtUtc >= DATEADD(DAY, -60, sysdatetimeoffset()))
  -- Case-sensitive: rejects all-caps Deltek legacy names
  AND co.DisplayName COLLATE Latin1_General_CS_AS <> UPPER(co.DisplayName) COLLATE Latin1_General_CS_AS
  -- Trailing stopwords (truncated Deltek)
  AND co.DisplayName NOT LIKE N'% AND' AND co.DisplayName NOT LIKE N'% OR'
  AND co.DisplayName NOT LIKE N'% INT' AND co.DisplayName NOT LIKE N'% ET C%'
  AND co.DisplayName NOT LIKE N'% ET A%' AND co.DisplayName NOT LIKE N'%,'
  -- Research-artifact parentheticals
  AND co.DisplayName NOT LIKE N'%(confirmed)%'
  AND co.DisplayName NOT LIKE N'%(design-build%'
  AND co.DisplayName NOT LIKE N'%(Construction Manager)%'
  -- Multi-entity garbage (slash / semicolon-joined)
  AND co.DisplayName NOT LIKE N'%/%'
  AND co.DisplayName NOT LIKE N'%;%'
  -- Parenthetical scope descriptors
  AND co.DisplayName NOT LIKE N'%(master plan%'
  AND co.DisplayName NOT LIKE N'%(phase %' AND co.DisplayName NOT LIKE N'%(Phase %'
  AND co.DisplayName NOT LIKE N'%(P3 %'
  AND co.DisplayName NOT LIKE N'%(now Stantec)%'
  -- Placeholder single-word kind labels
  AND co.DisplayName NOT IN (N'Architect', N'Architects', N'GC', N'General Contractor',
                              N'Developer', N'Developers', N'Buyer', N'Competitor',
                              N'Owner', N'Contractor', N'Engineer', N'Engineers',
                              N'Architectural Firm', N'Construction', N'Builder', N'Consultant',
                              N'Architect & Engineer', N'Unknown', N'Other', N'Various')
  -- TBA/TBD placeholders
  AND PATINDEX(N'%[^a-z]tbd[^a-z]%', N' '+LOWER(co.DisplayName)+N' ') = 0
  AND PATINDEX(N'%[^a-z]tba[^a-z]%', N' '+LOWER(co.DisplayName)+N' ') = 0
  -- Length + word sanity
  AND LEN(co.DisplayName) >= 6
  AND co.DisplayName LIKE N'% %'
ORDER BY
    CASE co.Kind WHEN N'KorClient' THEN 0 WHEN N'Architect' THEN 1 WHEN N'GC' THEN 2
                 WHEN N'Developer' THEN 3 WHEN N'Competitor' THEN 4 WHEN N'Buyer' THEN 5 END,
    COALESCE(co.LastKorProjectAtUtc, '1900-01-01') DESC,
    co.DisplayName;
"@
            $r = $cmd.ExecuteReader()
            $rows = @()
            while ($r.Read()) {
                $rows += [ordered]@{
                    id = [int]$r.GetValue(0)
                    displayName = $r.GetValue(1)
                    orgKind = $r.GetValue(2)
                }
            }
            $r.Close()
        }
        'projects' {
            $cmd.CommandText = @"
SELECT TOP (@take) mpi.Id, mpi.ProjectName, mpi.ProjectStage, mpi.Province, mpi.MunicipalityName,
       mpi.ProponentName, mpi.ProjectCategoryName,
       COALESCE(mpi.EstimatedCostText, CAST(mpi.EstimatedCostCad AS NVARCHAR(64))) AS EstimatedCost
FROM opportunities.MajorProjectsInventory mpi
WHERE mpi.RetiredAtUtc IS NULL
  AND mpi.ProjectStage IN (N'CapitalPlan', N'Planned', N'Concept', N'Design', N'Permitting', N'Procurement', N'Approved', N'Announced')
  AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment e
                  WHERE e.MajorProjectsInventoryId = mpi.Id AND e.ProviderName = N'ProjectBrief')
  -- Reject obviously-generic projectNames (avoid burning Sonnet on "Residential Condominium" etc.)
  AND mpi.ProjectName NOT IN (
      N'Condominium Development', N'Residential Condominium', N'Highrise Condominiums',
      N'Highrise Condominium', N'Lowrise Condominium', N'Rental Towers', N'Residential Tower',
      N'Mixed-Use Development', N'Office Building', N'Office Tower',
      N'Midrise Apartment', N'Mid-Rise Apartment', N'Terraced Condominium')
  -- Reject multi-entity garbage in projectName
  AND mpi.ProjectName NOT LIKE N'%/%'
  AND LEN(mpi.ProjectName) >= 6
ORDER BY CASE WHEN mpi.EstimatedCostCad IS NULL THEN 1 ELSE 0 END, mpi.EstimatedCostCad DESC, mpi.Id;
"@
            $r = $cmd.ExecuteReader()
            $rows = @()
            while ($r.Read()) {
                $rows += [ordered]@{
                    id = [int64]$r.GetValue(0)
                    projectName = if ($r.IsDBNull(1)) { $null } else { $r.GetValue(1) }
                    stage = if ($r.IsDBNull(2)) { $null } else { $r.GetValue(2) }
                    province = if ($r.IsDBNull(3)) { $null } else { $r.GetValue(3) }
                    city = if ($r.IsDBNull(4)) { $null } else { $r.GetValue(4) }
                    proponentName = if ($r.IsDBNull(5)) { $null } else { $r.GetValue(5) }
                    sector = if ($r.IsDBNull(6)) { $null } else { $r.GetValue(6) }
                    estimatedCost = if ($r.IsDBNull(7)) { $null } else { $r.GetValue(7).ToString() }
                }
            }
            $r.Close()
        }
        'people' {
            $cmd.CommandText = @"
WITH cte AS (
    SELECT LTRIM(RTRIM(kp.DisplayName)) AS DisplayName, kp.Title,
        co.DisplayName AS EmployerName,
        ROW_NUMBER() OVER (PARTITION BY LTRIM(RTRIM(kp.DisplayName))
                           ORDER BY CASE WHEN co.DisplayName IS NOT NULL THEN 0 ELSE 1 END,
                                    CASE WHEN kp.Title IS NOT NULL THEN 0 ELSE 1 END,
                                    kp.CreatedAtUtc DESC) AS rn
    FROM opportunities.IntelProjectKeyPerson kp
    LEFT JOIN opportunities.CanonicalOrg co ON co.Id = kp.CanonicalOrgId AND co.RetiredAtUtc IS NULL
    WHERE kp.CreatedAtUtc >= DATEADD(HOUR, -48, sysdatetimeoffset())
      AND kp.DisplayName IS NOT NULL AND LEN(LTRIM(RTRIM(kp.DisplayName))) >= 6
      AND LTRIM(RTRIM(kp.DisplayName)) LIKE N'% %'
      AND LTRIM(RTRIM(kp.DisplayName)) NOT LIKE N'%<%'
      AND LOWER(LTRIM(RTRIM(kp.DisplayName))) NOT LIKE N'unknown%'
      AND NOT EXISTS (SELECT 1 FROM opportunities.IntelPerson p
                      WHERE p.DisplayName = LTRIM(RTRIM(kp.DisplayName))
                        AND p.LastSeenAtUtc >= DATEADD(DAY, -60, sysdatetimeoffset()))
)
SELECT TOP (@take) DisplayName, Title, EmployerName FROM cte WHERE rn = 1
ORDER BY CASE WHEN EmployerName IS NOT NULL THEN 0 ELSE 1 END,
         CASE WHEN Title IS NOT NULL THEN 0 ELSE 1 END, DisplayName;
"@
            $r = $cmd.ExecuteReader()
            $rows = @(); $id = 1
            while ($r.Read()) {
                $rows += [ordered]@{
                    id = $id
                    displayName = $r.GetValue(0)
                    currentTitle = if ($r.IsDBNull(1)) { $null } else { $r.GetValue(1) }
                    currentEmployerName = if ($r.IsDBNull(2)) { $null } else { $r.GetValue(2) }
                }
                $id++
            }
            $r.Close()
        }
        'proponents' {
            $cmd.CommandText = @"
SELECT TOP (@take) Id, ProjectName, ProjectStage, Province, MunicipalityName, Sector, ProjectCategoryName,
       COALESCE(EstimatedCostText, CAST(EstimatedCostCad AS NVARCHAR(64))) AS EstimatedCost
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL
  AND (ProponentName IS NULL OR LEN(LTRIM(RTRIM(ProponentName))) = 0)
  -- Reject generic project names (Sonnet can't research them)
  AND ProjectName NOT IN (
      N'Condominium Development', N'Residential Condominium', N'Highrise Condominiums',
      N'Highrise Condominium', N'Lowrise Condominium', N'Rental Towers', N'Residential Tower',
      N'Mixed-Use Development', N'Office Building', N'Office Tower',
      N'Midrise Apartment', N'Mid-Rise Apartment', N'Terraced Condominium')
ORDER BY CASE WHEN EstimatedCostCad IS NULL THEN 1 ELSE 0 END, EstimatedCostCad DESC;
"@
            $r = $cmd.ExecuteReader()
            $rows = @()
            while ($r.Read()) {
                $rows += [ordered]@{
                    id = [int64]$r.GetValue(0)
                    projectName = $r.GetValue(1)
                    stage = if ($r.IsDBNull(2)) { $null } else { $r.GetValue(2) }
                    province = if ($r.IsDBNull(3)) { $null } else { $r.GetValue(3) }
                    city = if ($r.IsDBNull(4)) { $null } else { $r.GetValue(4) }
                    sector = if ($r.IsDBNull(5)) { $null } else { $r.GetValue(5) }
                    category = if ($r.IsDBNull(6)) { $null } else { $r.GetValue(6) }
                    estimatedCost = if ($r.IsDBNull(7)) { $null } else { $r.GetValue(7).ToString() }
                }
            }
            $r.Close()
        }
    }
}
finally {
    $conn.Close()
}

if ($rows.Count -eq 0) {
    Write-Warning "Query returned 0 candidates. Nothing to drain."
    exit 1
}

$rows | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $outFile
Write-Host ""
Write-Host "Batch written: $outFile" -ForegroundColor Green
Write-Host "  Kind: $Kind"
Write-Host "  Rows: $($rows.Count)"
Write-Host ""
Write-Host "Next step: launch Sonnet at the queue directory. It will auto-discover this batch:"
Write-Host "  cd C:\ProgramData\KorOperations\QueueDrain\$Kind" -ForegroundColor Cyan
Write-Host "  claude --model claude-sonnet-4-6 --permission-mode bypassPermissions" -ForegroundColor Cyan
