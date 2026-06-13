# Generate-FamilyPlan.ps1
# Firm-family consolidation planner (Dossier Completeness Engine, step 1).
# Detects office-variant fragments (name-prefix families) and '/'-joined JV
# rows across the six BD kinds and emits a REVIEWABLE plan CSV. Read-only:
# nothing is merged or retired here — execution goes through
# BdCanonicalDedup --pairs after human review.
#
# Family key rules (trap-aware):
#   - parentheticals removed, '&'/'+' -> space, punctuation stripped
#   - trailing legal/descriptor/geo tokens stripped ONLY while >= 2 tokens
#     would remain (keeps 'Fox Architecture' != 'Fox Corporation' and
#     'Continuum Architecture' != 'Continuum Partners')
#   - families seeded by six-kind orgs with the same key; Vendor/Unknown
#     rows join by exact key only (flagged); legacy names join by contiguous
#     phrase containment (flagged Low)
param(
    [string] $ConnectionString = [Environment]::GetEnvironmentVariable('KOR_OPPORTUNITIES_OPPORTUNITIESDB', 'User'),
    [string] $OutDir = (Join-Path $PSScriptRoot 'output')
)

$ErrorActionPreference = 'Stop'
if (-not $ConnectionString) { throw 'KOR_OPPORTUNITIES_OPPORTUNITIESDB not set.' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force $OutDir | Out-Null }

$SixKinds = @('Architect', 'GC', 'Developer', 'Buyer', 'Competitor', 'KorClient')
$KorCanonicalId = 38918L   # KOR Structural — never merge, never a loser/survivor.

# Documented false-collapse / evidence-blocked pairs (memory: maintenance state).
# Stored unordered — blocked in either direction.
$BlockedPairs = @(
    @(29098, 74300), # Continuum Architecture != Continuum Partners LLC
    @(33395, 74272), # Fox Architecture != Fox Corporation
    @(10868, 10869), # Lemay / Lemco — no proof same firm
    @(2751,  0)      # ATB Architecture — identity unproven (0 = block any pair containing 2751)
)

$LegalTokens = @('inc','incorporated','ltd','limited','llp','llc','lp','ulc','corp','corporation',
    'co','company','plc','pllc','pc','gmbh','sencrl','canada','usa','america','international')
$DescriptorTokens = @('architects','architecture','architect','engineers','engineering','engineer',
    'consultants','consulting','consultancy','builders','construction','constructors','contracting',
    'contractors','developments','development','developers','properties','realty','associates',
    'partners','partnership','studio','studios','group','office','offices','urban','planning',
    'interiors','designers','landscape')
$GeoTokens = @('vancouver','burnaby','richmond','surrey','langley','coquitlam','abbotsford','victoria',
    'nanaimo','kelowna','kamloops','calgary','edmonton','toronto','ottawa','montreal','winnipeg',
    'saskatoon','regina','seattle','portland','tacoma','spokane','los','angeles','san','francisco',
    'diego','jose','sacramento','denver','phoenix','dallas','houston','austin','chicago','boston',
    'york','atlanta','miami','bc','alberta','ontario','washington','oregon','california')
$Strippable = [System.Collections.Generic.HashSet[string]]::new([string[]]($LegalTokens + $DescriptorTokens + $GeoTokens))
$CivicStrippable = [System.Collections.Generic.HashSet[string]]::new([string[]]($LegalTokens + $DescriptorTokens | Where-Object { $_ -ne 'canada' }))

# Public-sector naming: legitimate exact-key dedup is fine, but containment
# joins across civic names ('City of New Westminster' joining a 'new
# westminster' family) are categorically wrong.
$CivicPattern = '^(the\s+)?(city|town|township|district|village|county|municipality|corporation|regional|ministry|government|province|provincial|university|college|school|health|housing|first\s+nation|nation)\b'

function Get-FamilyKey([string] $name) {
    if (-not $name) { return '' }
    $n = $name.ToLowerInvariant()
    $n = [regex]::Replace($n, '\([^)]*\)', ' ')          # parentheticals = office/location qualifiers
    $n = $n -replace '[&+]', ' '                          # Perkins&Will / Perkins + Will -> perkins will
    $n = [regex]::Replace($n, "[^a-z0-9 ]", ' ')          # punctuation
    $n = [regex]::Replace($n, '^\s*the\s+', '')           # 'The City of Red Deer' = 'City of Red Deer'
    # Public bodies: the geographic token IS the identity ('City of Vancouver'
    # must never reduce to 'city of'), so geo tokens are not strippable there.
    $strippable = if ($n -match $CivicPattern) { $CivicStrippable } else { $Strippable }
    $tokens = [System.Collections.Generic.List[string]]($n -split '\s+' | Where-Object { $_ })
    while ($tokens.Count -ge 3 -and $strippable.Contains($tokens[$tokens.Count - 1])) {
        # strip trailing strippable tokens only while >= 2 tokens remain AFTER the strip
        $tokens.RemoveAt($tokens.Count - 1)
    }
    return ($tokens -join ' ')
}

# Award-echo junk rows (m126 tier) — never family material.
$JunkPattern = '(?i)(thank you|awarded|based on the|rfp was|proposal in response|submissions)'
# Generic tokens that cannot anchor a family identity on their own.
$GenericExtra = @('general','contractor','builder','services','service','management','projects','project','enterprises','holdings','the','of','and','interior','design')

# A '+'/'/' segment is a FIRM only if it carries an identity token — something
# beyond descriptors/legal suffixes. Keeps 'Tony Osborn Architecture + Design
# Inc.' (one firm, '+ Design' is brand styling) out of the JV bucket while
# 'MST Development Corporation + Canada Lands Company' stays in.
function Test-FirmSegment([string] $seg) {
    $tokens = (Get-FamilyKey $seg) -split '\s+' | Where-Object { $_ }
    $identity = @($tokens | Where-Object { -not ($Strippable.Contains($_) -or $GenericExtra -contains $_) })
    return ($identity.Count -ge 1)
}

function Test-JvName([string] $name) {
    $n = [regex]::Replace($name, '\([^)]*\)', ' ')    # parenthetical echoes would double-count segments
    $n = $n -replace '(?i)\bc/o\b', ' '               # 'DIMEX PROPERTIES INC C/O Sunny Dhillon' is care-of, not a JV
    $n = $n -replace '(?<=\d)/(?=\d)', '-'            # 'Magellen Developments 20/20' is a brand, not a JV
    if ($n -match '/') { return $true }
    $plusSegs = [regex]::Split($n, '\s\+\s') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    if ($plusSegs.Count -lt 2) { return $false }
    $firmSegs = @($plusSegs | Where-Object { Test-FirmSegment $_ })
    if ($plusSegs.Count -ge 3 -and $firmSegs.Count -ge 2) { return $true }        # Saucier + Perrotte + Stantec
    if ($plusSegs.Count -eq 2 -and $firmSegs.Count -eq 2 -and
        -not ($plusSegs | Where-Object { ($_ -split '\s+').Count -lt 2 })) {
        return $true                                  # 'MST Development Corp + Canada Lands' yes; 'Perkins + Will' no
    }
    return $false
}

function Test-Blocked([long] $a, [long] $b) {
    foreach ($p in $BlockedPairs) {
        if ($p[1] -eq 0) { if ($a -eq $p[0] -or $b -eq $p[0]) { return $true } }
        elseif (($a -eq $p[0] -and $b -eq $p[1]) -or ($a -eq $p[1] -and $b -eq $p[0])) { return $true }
    }
    return $false
}

$cn = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$cn.Open()
try {
    # 1) Enumerate every FK column referencing CanonicalOrg so link-weight can
    #    never drift from schema (same guard philosophy as BdCanonicalDedup).
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = @"
SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables t ON t.object_id = fk.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
WHERE fk.referenced_object_id = OBJECT_ID('opportunities.CanonicalOrg')
"@
    $r = $cmd.ExecuteReader()
    $fkCols = New-Object System.Data.DataTable
    $fkCols.Load($r)
    if ($fkCols.Rows.Count -eq 0) { throw 'No FKs referencing CanonicalOrg found — wrong DB?' }

    $unions = foreach ($row in $fkCols.Rows) {
        "SELECT [$($row.ColumnName)] AS OrgId, 1 AS W FROM [$($row.SchemaName)].[$($row.TableName)] WHERE [$($row.ColumnName)] IS NOT NULL"
    }
    $weightSql = ($unions -join "`nUNION ALL`n")

    # 2) Active orgs (six kinds as family seeds + Vendor/Unknown as exact-key joiners)
    #    with their aggregated FK link weight.
    $cmd = $cn.CreateCommand()
    $cmd.CommandTimeout = 300
    $cmd.CommandText = @"
WITH W AS (SELECT OrgId, SUM(W) AS LinkWeight FROM (
$weightSql
) u GROUP BY OrgId)
SELECT o.Id, o.DisplayName, o.Kind, o.ClendorClientId, o.BcRegistryBusinessNumber,
       o.EnrichmentSuppressedReason,
       LinkWeight = ISNULL(w.LinkWeight, 0),
       EnrichCount = (SELECT COUNT(*) FROM opportunities.CanonicalOrgEnrichment e
                      WHERE e.CanonicalOrgId = o.Id AND e.Status = 'ok')
FROM opportunities.CanonicalOrg o
LEFT JOIN W w ON w.OrgId = o.Id
WHERE o.RetiredAtUtc IS NULL
  AND o.Kind IN ('Architect','GC','Developer','Buyer','Competitor','KorClient','Vendor','Unknown')
"@
    $r = $cmd.ExecuteReader()
    $orgs = New-Object System.Data.DataTable
    $orgs.Load($r)
} finally { $cn.Close() }

Write-Host ("Loaded {0} active orgs ({1} FK columns feed link-weight)." -f $orgs.Rows.Count, $fkCols.Rows.Count)

# 3) Classify rows.
$seedByKey = @{}     # familyKey -> List of six-kind, non-JV org rows
$extByKey  = @{}     # familyKey -> List of Vendor/Unknown exact-key rows
$jvRows    = [System.Collections.Generic.List[object]]::new()
$allSeeds  = [System.Collections.Generic.List[object]]::new()

foreach ($row in $orgs.Rows) {
    $isSix = $SixKinds -contains $row.Kind
    $name = [string]$row.DisplayName
    if ($name.Length -gt 120 -or $name -match $JunkPattern) { continue }   # award-echo junk, not family material
    $key = Get-FamilyKey $name
    if ($key.Length -lt 4 -or ($key -split ' ').Count -lt 1) { continue }
    $entry = [pscustomobject]@{
        Id = [long]$row.Id; Name = $name; Kind = [string]$row.Kind; Key = $key
        Weight = [int]$row.LinkWeight; Enrich = [int]$row.EnrichCount
        Clendor = if ($row.ClendorClientId -is [dbnull]) { '' } else { ([string]$row.ClendorClientId).Trim() }
        BizNum = if ($row.BcRegistryBusinessNumber -is [dbnull]) { '' } else { ([string]$row.BcRegistryBusinessNumber).Trim() }
        Suppressed = -not ($row.EnrichmentSuppressedReason -is [dbnull])
    }
    if ($isSix -and (Test-JvName $name)) { $jvRows.Add($entry); continue }   # JV artifact, not a family member
    if ($isSix) {
        if (-not $seedByKey.ContainsKey($key)) { $seedByKey[$key] = [System.Collections.Generic.List[object]]::new() }
        $seedByKey[$key].Add($entry)
        $allSeeds.Add($entry)
    } elseif ($name -notmatch '/') {
        if (-not $extByKey.ContainsKey($key)) { $extByKey[$key] = [System.Collections.Generic.List[object]]::new() }
        $extByKey[$key].Add($entry)
    }
}

# 4) Build families: same-key seeds (>=2) are the core; exact-key Vendor/Unknown
#    rows extend; containment (family key as contiguous phrase inside another
#    seed's key, family key >= 2 tokens) catches legacy names — Low confidence.
$families = [System.Collections.Generic.List[object]]::new()
foreach ($kv in $seedByKey.GetEnumerator()) {
    if ($kv.Value.Count -lt 2) { continue }
    $key = $kv.Key
    $keyTokens = $key -split ' '
    # A family key made only of generic/strippable tokens ('general contractor')
    # anchors nothing — drop the family.
    if (-not ($keyTokens | Where-Object { -not ($Strippable.Contains($_) -or $GenericExtra -contains $_) })) { continue }
    $members = [System.Collections.Generic.List[object]]::new()
    foreach ($m in $kv.Value) { $members.Add([pscustomobject]@{ Org = $m; Join = 'EXACT' }) }
    if ($extByKey.ContainsKey($key)) {
        foreach ($m in $extByKey[$key]) { $members.Add([pscustomobject]@{ Org = $m; Join = 'EXACT-NONBD' }) }
    }
    # Containment join = legacy-name catch (Busby Perkins + Will). Tight rules:
    # extra material is exactly ONE token, and neither side is a public body.
    if ($keyTokens.Count -ge 2 -and $key -notmatch $CivicPattern) {
        $needle = " $key "
        foreach ($s in $allSeeds) {
            if ($s.Key -eq $key) { continue }
            if (-not " $($s.Key) ".Contains($needle)) { continue }
            if ((($s.Key -split ' ').Count - $keyTokens.Count) -ne 1) { continue }
            if ($s.Key -match $CivicPattern -or $s.Name -match $CivicPattern) { continue }
            $members.Add([pscustomobject]@{ Org = $s; Join = 'CONTAINS' })
        }
    }
    if (($members | Where-Object { $_.Org.Id -eq $KorCanonicalId }).Count -gt 0) {
        Write-Warning "Family '$key' touches KOR canonical 38918 — skipped entirely."
        continue
    }
    $families.Add([pscustomobject]@{ Key = $key; Members = $members })
}

# 5) Emit plan rows.
$planRows = [System.Collections.Generic.List[string]]::new()
$planRows.Add('FamilyKey,Action,LoserId,LoserName,LoserKind,LoserWeight,LoserEnrich,SurvivorId,SurvivorName,SurvivorKind,SurvivorWeight,Confidence,Evidence')
$csv = { param($s) '"' + ([string]$s -replace '"', '""') + '"' }

$pairCount = 0
$seenLosers = [System.Collections.Generic.HashSet[long]]::new()
foreach ($fam in ($families | Sort-Object { -(($_.Members | Measure-Object { $_.Org.Weight } -Sum).Sum) })) {
    # survivor: Deltek-linked rows get a big bonus, then raw link weight, then enrichment depth
    $ranked = $fam.Members | Sort-Object @{e={ $(if ($_.Org.Clendor) { 1 } else { 0 }) }; Descending=$true},
                                          @{e={ $_.Org.Weight }; Descending=$true},
                                          @{e={ $_.Org.Enrich }; Descending=$true},
                                          @{e={ $_.Org.Id }}
    $surv = $ranked[0].Org
    foreach ($m in $ranked[1..($ranked.Count - 1)]) {
        $o = $m.Org
        if (Test-Blocked $o.Id $surv.Id) { continue }
        if (-not $seenLosers.Add($o.Id)) { continue }   # already proposed as a loser in a heavier family
        $conflicts = @()
        if ($o.Clendor -and $surv.Clendor -and $o.Clendor -ne $surv.Clendor) { $conflicts += "CLENDOR-CONFLICT($($o.Clendor) vs $($surv.Clendor))" }
        if ($o.BizNum -and $surv.BizNum -and $o.BizNum -ne $surv.BizNum) { $conflicts += "BIZNUM-CONFLICT($($o.BizNum) vs $($surv.BizNum))" }
        if ($o.Suppressed) { $conflicts += 'loser-suppressed' }
        $conf = if ($conflicts | Where-Object { $_ -like '*CONFLICT*' }) { 'Low' }
                elseif ($m.Join -eq 'CONTAINS') { 'Low' }
                elseif ($m.Join -eq 'EXACT-NONBD') { 'Medium' }
                else { 'High' }
        $evidence = (@("join=$($m.Join)") + $conflicts) -join '; '
        $planRows.Add((@(
            (& $csv $fam.Key), 'MERGE',
            $o.Id, (& $csv $o.Name), $o.Kind, $o.Weight, $o.Enrich,
            $surv.Id, (& $csv $surv.Name), $surv.Kind, $surv.Weight,
            $conf, (& $csv $evidence)
        ) -join ','))
        $pairCount++
    }
}

foreach ($jv in ($jvRows | Sort-Object Weight -Descending)) {
    $planRows.Add((@(
        (& $csv (Get-FamilyKey $jv.Name)), 'RETIRE_JV',
        $jv.Id, (& $csv $jv.Name), $jv.Kind, $jv.Weight, $jv.Enrich,
        '', '', '', '',
        'Review', (& $csv ("slash-joined JV row; linkWeight={0}; project artifact, retire not merge" -f $jv.Weight))
    ) -join ','))
}

$outFile = Join-Path $OutDir 'family-plan-2026-06-12.csv'
[System.IO.File]::WriteAllLines($outFile, $planRows)
Write-Host ("Families: {0}  merge pairs: {1}  JV retire proposals: {2}" -f $families.Count, $pairCount, $jvRows.Count)
Write-Host "Plan written: $outFile"
