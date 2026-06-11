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
    One of: orgs | projects | people | proponents | honing-orgs | honing-people | honing-projects

    Honing kinds package each target's existing first-pass enrichment payload
    alongside its identifiers, so Sonnet's honing PROMPT can read the prior
    research and dig deeper on the remaining BD gaps without duplicating work.

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
    [Parameter(Mandatory)][ValidateSet('orgs', 'projects', 'people', 'proponents',
                                        'honing-orgs', 'honing-people', 'honing-projects')]
    [string] $Kind,

    [Parameter(Mandatory)][int] $BatchNumber,

    [int] $Take = 200,

    [string] $OutDir,

    # BD-Audit-2026-06-09 M11: the honing kinds used to require the first-pass
    # enrichment to be <= 14 days old — anything first-passed 15+ days ago
    # silently left the honing pool until its 90-day refresh. 90 days matches
    # the first-pass refresh cycle, so nothing falls in the gap.
    [int] $FirstPassMaxAgeDays = 90,

    # BD-Audit-2026-06-09 M11: sector honing queues (bc-ab-hospitals-honing
    # etc.) need a category-filtered candidate set; without this the only
    # option was the banned ad-hoc SQL. Matches MPI.ProjectCategoryName
    # (honing-projects kind only).
    [string] $Category,

    # US-drain support (2026-06-10): geographic scoping for the projects and
    # honing-projects kinds. Matches MPI.Province ('CA' / 'OR' / 'WA' / 'BC' /
    # 'AB'). NULL = no filter (existing behavior).
    [string] $Province,

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
    $cmd.Parameters.AddWithValue("@firstPassMaxAgeDays", $FirstPassMaxAgeDays) | Out-Null
    $cmd.Parameters.AddWithValue("@category", $(if ([string]::IsNullOrWhiteSpace($Category)) { [DBNull]::Value } else { $Category })) | Out-Null
    $cmd.Parameters.AddWithValue("@province", $(if ([string]::IsNullOrWhiteSpace($Province)) { [DBNull]::Value } else { $Province })) | Out-Null

    switch ($Kind) {
        'orgs' {
            $cmd.CommandText = @"
SELECT TOP (@take) co.Id, co.DisplayName, co.Kind
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND co.EnrichmentSuppressedAtUtc IS NULL -- m126 do-not-enrich flag
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
  -- Multi-entity garbage (slash / semicolon / plus / pipe joined)
  AND co.DisplayName NOT LIKE N'%/%'
  AND co.DisplayName NOT LIKE N'%;%'
  AND co.DisplayName NOT LIKE N'% + %'
  AND co.DisplayName NOT LIKE N'% | %'
  AND co.DisplayName NOT LIKE N'% in association with %'
  AND co.DisplayName NOT LIKE N'% In Association With %'
  -- m98-found bid-status pollution variants
  AND co.DisplayName NOT LIKE N'% Award'
  AND co.DisplayName NOT LIKE N'% Award %'
  AND co.DisplayName NOT LIKE N'% Proposal'
  AND co.DisplayName NOT LIKE N'% Submission'
  AND co.DisplayName NOT LIKE N'% Submissions'
  AND co.DisplayName NOT LIKE N'% Bidder'
  AND co.DisplayName NOT LIKE N'% Tender'
  AND co.DisplayName NOT LIKE N'% Tender %'
  -- m101-found pollution variants
  AND co.DisplayName NOT LIKE N'%(in-house%' AND co.DisplayName NOT LIKE N'%(consultant%'
  AND co.DisplayName NOT LIKE N'%(structural%' AND co.DisplayName NOT LIKE N'%(architect of record%'
  AND co.DisplayName NOT LIKE N'%(prime%' AND co.DisplayName NOT LIKE N'%(design lead%'
  AND co.DisplayName NOT LIKE N'% Ltd. and %' AND co.DisplayName NOT LIKE N'% Inc. and %'
  AND co.DisplayName NOT LIKE N'% Ltd and %' AND co.DisplayName NOT LIKE N'% Inc and %'
  AND co.DisplayName NOT LIKE N'% in Joint Venture%' AND co.DisplayName NOT LIKE N'% IN JOINT VENTURE%'
  -- Architecture: prefix (parser artifact)
  AND co.DisplayName NOT LIKE N'Architecture: %'
  -- Person-name concatenated (" - FirstName LastName" suffix)
  AND co.DisplayName NOT LIKE N'% - [A-Z]% [A-Z]%'
  -- Multi-entity " and " patterns (two architect/architecture mentions = JV)
  AND NOT (co.DisplayName LIKE N'%Architect% and %Architect%')
  AND NOT (co.DisplayName LIKE N'%Architecture and %Architecture%')
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
  AND (@province IS NULL OR mpi.Province = @province)
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
        'honing-orgs' {
            # Pulls already-enriched orgs and packages each with its first-pass FirmNarrative JSON.
            # Priority: KorClient > Architect > Competitor > Buyer > Developer.
            $cmd.CommandText = @"
WITH TopTargets AS (
    SELECT TOP (@take) co.Id, co.DisplayName, co.Kind, e.ResultJson AS FirstPassNarrative,
        (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory m WHERE m.RetiredAtUtc IS NULL AND (m.ArchitectCanonicalOrgId=co.Id OR m.GeneralContractorCanonicalOrgId=co.Id OR m.StructuralEngineerCanonicalOrgId=co.Id OR m.ProponentCanonicalOrgId=co.Id)) AS BdValue
    FROM opportunities.CanonicalOrg co
    INNER JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId = co.Id AND e.ProviderName = N'FirmNarrative'
        AND e.LastRefreshAtUtc >= DATEADD(DAY, -@firstPassMaxAgeDays, sysdatetimeoffset())
        AND e.Status = N'Ok'
    WHERE co.RetiredAtUtc IS NULL
      AND co.EnrichmentSuppressedAtUtc IS NULL -- m126 do-not-enrich flag
      AND co.Kind IN (N'Architect', N'Competitor', N'KorClient', N'Buyer', N'Developer')
      AND e.ResultJson IS NOT NULL AND LEN(e.ResultJson) > 200
      AND NOT EXISTS (SELECT 1 FROM opportunities.CanonicalOrgEnrichment eh WHERE eh.CanonicalOrgId = co.Id AND eh.ProviderName = N'FirmNarrativeHoning' AND eh.LastRefreshAtUtc >= DATEADD(DAY, -30, sysdatetimeoffset()))
    ORDER BY
        CASE co.Kind WHEN N'KorClient' THEN 0 WHEN N'Architect' THEN 1 WHEN N'Competitor' THEN 2 WHEN N'Buyer' THEN 3 WHEN N'Developer' THEN 4 END,
        BdValue DESC
)
SELECT * FROM TopTargets;
"@
            $r = $cmd.ExecuteReader()
            $rows = @()
            while ($r.Read()) {
                try {
                    $payload = $r.GetValue(3)
                    $rows += [ordered]@{
                        id = [int]$r.GetValue(0)
                        displayName = $r.GetValue(1)
                        kind = $r.GetValue(2)
                        firstPassNarrative = $payload | ConvertFrom-Json
                        bdValue = [int]$r.GetValue(4)
                    }
                } catch {}
            }
            $r.Close()
        }
        'honing-people' {
            # Already-enriched people, packaged with first-pass PersonBrief.
            # PersonBrief enrichments live in CanonicalOrgEnrichment with ProviderName='PersonBrief-{personId}'
            # CanonicalOrgId on the enrichment = the person's current-affiliation org.
            $cmd.CommandText = @"
WITH TopTargets AS (
    SELECT TOP (@take) p.Id, p.DisplayName, pa.Title AS CurrentTitle, co.DisplayName AS CurrentEmployer,
        e.ResultJson AS FirstPassBrief, e.LastRefreshAtUtc AS EnrichedAt
    FROM opportunities.IntelPerson p
    INNER JOIN opportunities.IntelPersonAffiliation pa ON pa.IntelPersonId = p.Id AND pa.IsCurrent = 1 AND pa.RetiredAtUtc IS NULL
    INNER JOIN opportunities.CanonicalOrg co ON co.Id = pa.CanonicalOrgId AND co.RetiredAtUtc IS NULL
    INNER JOIN opportunities.CanonicalOrgEnrichment e ON e.CanonicalOrgId = pa.CanonicalOrgId
        AND e.ProviderName = N'PersonBrief-' + CAST(p.Id AS NVARCHAR(20))
        AND e.LastRefreshAtUtc >= DATEADD(DAY, -@firstPassMaxAgeDays, sysdatetimeoffset())
        AND e.Status = N'Ok'
    WHERE p.RetiredAtUtc IS NULL
      AND p.DisplayName IS NOT NULL AND LEN(p.DisplayName) >= 6
      AND e.ResultJson IS NOT NULL AND LEN(e.ResultJson) > 200
      AND NOT EXISTS (
          SELECT 1 FROM opportunities.CanonicalOrgEnrichment eh
          WHERE eh.CanonicalOrgId = pa.CanonicalOrgId
            AND eh.ProviderName = N'PersonBriefHoning-' + CAST(p.Id AS NVARCHAR(20))
            AND eh.LastRefreshAtUtc >= DATEADD(DAY, -30, sysdatetimeoffset()))
    ORDER BY e.LastRefreshAtUtc DESC
)
SELECT * FROM TopTargets;
"@
            $r = $cmd.ExecuteReader()
            $rows = @()
            while ($r.Read()) {
                try {
                    $payload = $r.GetValue(4)
                    $rows += [ordered]@{
                        personId = [int]$r.GetValue(0)
                        displayName = $r.GetValue(1)
                        currentTitle = if ($r.IsDBNull(2)) { $null } else { $r.GetValue(2) }
                        currentEmployerName = if ($r.IsDBNull(3)) { $null } else { $r.GetValue(3) }
                        firstPassBrief = $payload | ConvertFrom-Json
                    }
                } catch {}
            }
            $r.Close()
        }
        'honing-projects' {
            # Already-enriched MPI rows, packaged with first-pass ProjectBrief.
            # Priority: highest BD value (cost + actions) + recent enrichment.
            $cmd.CommandText = @"
WITH TopTargets AS (
    SELECT TOP (@take) m.Id, m.ProjectName, m.ProjectStage, m.Province, m.MunicipalityName, m.ProponentName,
        e.ResultJson AS FirstPassBrief, m.EstimatedCostCad,
        (SELECT COUNT(*) FROM opportunities.IntelProjectAction a WHERE a.MajorProjectsInventoryId = m.Id) AS ActionCount
    FROM opportunities.MajorProjectsInventory m
    INNER JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBrief'
        AND e.LastRefreshAtUtc >= DATEADD(DAY, -@firstPassMaxAgeDays, sysdatetimeoffset())
    WHERE m.RetiredAtUtc IS NULL
      AND m.ProjectStage IN (N'CapitalPlan',N'Planned',N'Concept',N'Design',N'Permitting',N'Procurement',N'Approved',N'Announced')
      AND (@category IS NULL OR m.ProjectCategoryName = @category)
      AND (@province IS NULL OR m.Province = @province)
      AND e.ResultJson IS NOT NULL AND LEN(e.ResultJson) > 200
      AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment eh WHERE eh.MajorProjectsInventoryId = m.Id AND eh.ProviderName = N'ProjectBriefHoning' AND eh.LastRefreshAtUtc >= DATEADD(DAY, -30, sysdatetimeoffset()))
    ORDER BY ActionCount DESC, m.EstimatedCostCad DESC
)
SELECT * FROM TopTargets;
"@
            $r = $cmd.ExecuteReader()
            $rows = @()
            while ($r.Read()) {
                try {
                    $payload = $r.GetValue(6)
                    $rows += [ordered]@{
                        id = [int64]$r.GetValue(0)
                        projectName = $r.GetValue(1)
                        stage = if ($r.IsDBNull(2)) { $null } else { $r.GetValue(2) }
                        province = if ($r.IsDBNull(3)) { $null } else { $r.GetValue(3) }
                        city = if ($r.IsDBNull(4)) { $null } else { $r.GetValue(4) }
                        proponentName = if ($r.IsDBNull(5)) { $null } else { $r.GetValue(5) }
                        firstPassBrief = $payload | ConvertFrom-Json
                    }
                } catch {}
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

# Exclude items already queued in OTHER input batches of this queue. The SQL
# NOT-EXISTS checks only exclude items whose results have LANDED — an item
# sitting un-drained in an earlier batch file would otherwise be re-selected
# into every subsequent batch (2026-06-10: needed to top up us-projects-honing
# without duplicating the still-queued batches 001-003).
$alreadyQueued = [System.Collections.Generic.HashSet[string]]::new()
Get-ChildItem (Split-Path $outFile) -Filter 'batch-*.json' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $outFile } |
    ForEach-Object {
        try {
            $existing = Get-Content $_.FullName -Raw | ConvertFrom-Json
            foreach ($it in @($existing)) {
                foreach ($key in 'id', 'personId') {
                    if ($null -ne $it.$key) { [void]$alreadyQueued.Add([string]$it.$key) }
                }
            }
        } catch { Write-Warning "Could not read existing batch $($_.Name) for dedup: $_" }
    }

if ($alreadyQueued.Count -gt 0) {
    $before = $rows.Count
    $rows = @($rows | Where-Object {
        $k = if ($null -ne $_['id']) { [string]$_['id'] } elseif ($null -ne $_['personId']) { [string]$_['personId'] } else { $null }
        $null -eq $k -or -not $alreadyQueued.Contains($k)
    })
    if ($rows.Count -lt $before) {
        Write-Host "  Excluded $($before - $rows.Count) item(s) already queued in sibling batches." -ForegroundColor Yellow
    }
    if ($rows.Count -eq 0) {
        Write-Warning "All candidates are already queued in sibling batches. Nothing to write."
        exit 1
    }
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
