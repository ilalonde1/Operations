# One-shot backfill for opportunities.OpportunityBids.BidderCanonicalOrgId.
# Run after migration 61 has been applied. Does not create CanonicalOrg rows;
# future scraper runs do that with CanonicalOrgResolver allowCreate=true.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$cs = $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB
if ([string]::IsNullOrWhiteSpace($cs)) {
    throw 'KOR_OPPORTUNITIES_OPPORTUNITIESDB env var not set.'
}

function Normalize-StrictName {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    return $Value.Trim().ToLowerInvariant().
        Replace(' ', '').
        Replace('.', '').
        Replace(',', '').
        Replace("'", '').
        Replace('-', '').
        Replace('&', '').
        Replace('/', '').
        Replace('(', '').
        Replace(')', '').
        Replace('+', '')
}

function Normalize-ForFuzzyMatch {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    $s = $Value.Trim().ToLowerInvariant()
    $s = $s.Replace(' & ', ' and ')

    $cityPrefix = [regex]::Match($s, '^city of\s+(.+)$', 'IgnoreCase')
    if ($cityPrefix.Success) {
        $s = 'city of ' + $cityPrefix.Groups[1].Value
    }
    else {
        $citySuffix = [regex]::Match($s, '^(.+?)\s*\(city of\)$', 'IgnoreCase')
        if ($citySuffix.Success) {
            $s = 'city of ' + $citySuffix.Groups[1].Value
        }
    }

    $s = [regex]::Replace(
        $s,
        '\b(?:school\s+district|sd)\s*(?:no\.?\s*)?#?\s*(\d+)\b',
        'school district $1',
        'IgnoreCase')

    $suffixes = @(
        ' incorporated', ' corporation', ' limited',
        ' inc', ' inc.', ' ltd', ' ltd.', ' llp', ' llp.',
        ' corp', ' corp.', ' co', ' co.'
    )

    do {
        $changed = $false
        foreach ($suffix in $suffixes) {
            if ($s.EndsWith($suffix, [StringComparison]::Ordinal)) {
                $s = $s.Substring(0, $s.Length - $suffix.Length).TrimEnd(' ', ',', '.')
                $changed = $true
                break
            }
        }
    } while ($changed)

    return Normalize-StrictName $s
}

function New-Command {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$Sql,
        [System.Data.SqlClient.SqlTransaction]$Transaction = $null
    )

    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = $Sql
    $cmd.CommandTimeout = 120
    if ($null -ne $Transaction) {
        $cmd.Transaction = $Transaction
    }
    return $cmd
}

$con = New-Object System.Data.SqlClient.SqlConnection $cs
$con.Open()

try {
    $canonicalByName = @{}
    $loadCanonical = New-Command $con @'
SELECT Id, DisplayName
FROM opportunities.CanonicalOrg
WHERE DisplayName IS NOT NULL;
'@

    $r = $loadCanonical.ExecuteReader()
    try {
        while ($r.Read()) {
            $id = [int64]$r.GetInt64(0)
            $name = $r.GetString(1)
            $key = Normalize-ForFuzzyMatch $name
            if (-not [string]::IsNullOrWhiteSpace($key) -and -not $canonicalByName.ContainsKey($key)) {
                $canonicalByName[$key] = $id
            }
        }
    }
    finally {
        $r.Dispose()
        $loadCanonical.Dispose()
    }

    Write-Host ("Loaded {0} canonical-name keys." -f $canonicalByName.Count)

    $rows = New-Object System.Collections.Generic.List[object]
    $loadBids = New-Command $con @'
SELECT Id, BidderName
FROM opportunities.OpportunityBids
WHERE BidderCanonicalOrgId IS NULL
  AND BidderName IS NOT NULL;
'@

    $r = $loadBids.ExecuteReader()
    try {
        while ($r.Read()) {
            $rows.Add([pscustomobject]@{
                Id = [int64]$r.GetInt64(0)
                BidderName = $r.GetString(1)
            })
        }
    }
    finally {
        $r.Dispose()
        $loadBids.Dispose()
    }

    $scanned = 0
    $matched = 0
    $unmatched = 0

    $tran = $con.BeginTransaction()
    try {
        $update = New-Command $con @'
UPDATE opportunities.OpportunityBids
SET BidderCanonicalOrgId = @canonicalOrgId,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id
  AND BidderCanonicalOrgId IS NULL;
'@ $tran
        $null = $update.Parameters.Add('@canonicalOrgId', [System.Data.SqlDbType]::BigInt)
        $null = $update.Parameters.Add('@id', [System.Data.SqlDbType]::BigInt)

        foreach ($row in $rows) {
            $scanned++
            $key = Normalize-ForFuzzyMatch $row.BidderName
            if ([string]::IsNullOrWhiteSpace($key) -or -not $canonicalByName.ContainsKey($key)) {
                $unmatched++
                continue
            }

            $update.Parameters['@canonicalOrgId'].Value = [int64]$canonicalByName[$key]
            $update.Parameters['@id'].Value = [int64]$row.Id
            $affected = $update.ExecuteNonQuery()
            if ($affected -gt 0) {
                $matched++
            }
            else {
                $unmatched++
            }
        }

        $update.Dispose()
        $tran.Commit()
    }
    catch {
        $tran.Rollback()
        throw
    }
    finally {
        $tran.Dispose()
    }

    Write-Host ("OpportunityBids bidder canonical backfill complete. scanned={0}; matched={1}; unmatched={2}" -f $scanned, $matched, $unmatched)
}
finally {
    $con.Dispose()
}
