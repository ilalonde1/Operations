<#
.SYNOPSIS
    One-shot recovery for BD-Audit-2026-06-09 C1: the bc-ab-primes drain was
    ingested under ProviderName='ProjectBrief' (unrecognized marker defaulted
    to first-pass) and its upsert overwrote 181 first-pass briefs on
    2026-06-09 14:42.

.DESCRIPTION
    For every opportunities.MajorProjectEnrichment row where
    ProviderName='ProjectBrief' AND ResultJson contains the primes marker:

      1. Locate the newest FIRST-PASS archive file for that MPI under
         C:\ProgramData\KorOperations\QueueDrain\*\outputs\processed\
         (excluding bc-ab-primes). First-pass = items[0] has no _providerName
         root field and no [providerName: X] marker in description.
      2. Restore the ProjectBrief row's ResultJson / LastRefreshAtUtc from it.
      3. Re-file the primes payload (from the bc-ab-primes processed file,
         falling back to the overwritten DB value) as a new
         ProviderName='PrimeConsultantResearch' enrichment row.
      4. If no first-pass archive exists, RELABEL the row to
         PrimeConsultantResearch instead — the MPI then correctly reads as
         "needs first-pass" again.

    Dry-run by default; pass -Commit to write. Per-MPI transaction.

.NOTES
    Companion fix: BdQueueDrainIngest now whitelists providers and refuses
    unknown markers, so this class of overwrite cannot recur.
#>
[CmdletBinding()]
param(
    [switch]$Commit
)

$ErrorActionPreference = 'Stop'

$cs = $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB
if (-not $cs) {
    $cs = [Environment]::GetEnvironmentVariable('KOR_OPPORTUNITIES_OPPORTUNITIESDB', 'User') ??
          [Environment]::GetEnvironmentVariable('KOR_OPPORTUNITIES_OPPORTUNITIESDB', 'Machine')
}
if (-not $cs) { throw 'KOR_OPPORTUNITIES_OPPORTUNITIESDB is not set.' }

$queueRoot = 'C:\ProgramData\KorOperations\QueueDrain'
$markerRegex = [regex]'\[\s*providerName\s*:\s*([A-Za-z0-9._-]+)\s*\]'

function Get-BriefItem {
    param([string]$Path)
    # Returns @{ Raw = items[0] raw JSON text; GeneratedAtUtc = [datetime] or $null;
    #            IsFirstPass = bool } or $null when unusable.
    try {
        $doc = Get-Content $Path -Raw | ConvertFrom-Json -AsHashtable
    } catch { return $null }

    $item = $null
    $generated = $null
    if ($doc -is [hashtable] -and $doc.ContainsKey('items') -and $doc['items'] -is [object[]] -and $doc['items'].Count -eq 1) {
        $item = $doc['items'][0]
        if ($doc.ContainsKey('generatedAtUtc')) {
            try { $generated = [datetime]::Parse($doc['generatedAtUtc'], $null, [System.Globalization.DateTimeStyles]::AdjustToUniversal) } catch {}
        }
    } elseif ($doc -is [hashtable] -and -not $doc.ContainsKey('schemaVersion')) {
        $item = $doc   # legacy un-enveloped shape
    } else {
        return $null
    }

    if ($item -isnot [hashtable]) { return $null }

    # First-pass = no _providerName root field AND no [providerName: X]
    # marker ANYWHERE in the payload. Honing outputs in the legacy nested
    # honingPass shape carry the marker inside honingPass, not in a root
    # description, so the whole serialized item must be scanned.
    $raw = ($item | ConvertTo-Json -Depth 64)
    $isFirstPass = (-not $item.ContainsKey('_providerName')) -and (-not $markerRegex.IsMatch($raw))

    return @{
        Raw            = $raw
        GeneratedAtUtc = $generated
        IsFirstPass    = $isFirstPass
    }
}

$con = New-Object System.Data.SqlClient.SqlConnection $cs
$con.Open()

# 1. Affected rows.
$affected = New-Object System.Collections.Generic.List[object]
$cmd = $con.CreateCommand()
$cmd.CommandText = @"
SELECT Id, MajorProjectsInventoryId, ResultJson, CONVERT(varchar(33), LastRefreshAtUtc, 127) AS LastRefresh
FROM opportunities.MajorProjectEnrichment
WHERE ProviderName = N'ProjectBrief'
  AND ResultJson LIKE N'%PrimeConsultantResearch%';
"@
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    $affected.Add(@{ EnrichmentId = $r.GetInt64(0); MpiId = $r.GetInt64(1); DbJson = $r.GetString(2) })
}
$r.Close()
Write-Host "Affected ProjectBrief rows: $($affected.Count)  (mode: $(if ($Commit) {'COMMIT'} else {'dry-run'}))"

$restored = 0; $relabeled = 0; $primesFiled = 0; $errors = 0

foreach ($row in $affected) {
    $id = $row.MpiId

    # 2. Newest first-pass archive (exclude the primes queue).
    $candidates = Get-ChildItem "$queueRoot\*\outputs\processed\refresh-project-$id.json" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\bc-ab-primes\\' }
    $best = $null
    foreach ($f in $candidates) {
        $parsed = Get-BriefItem $f.FullName
        if ($null -eq $parsed -or -not $parsed.IsFirstPass) { continue }
        $ts = $parsed.GeneratedAtUtc ?? $f.LastWriteTimeUtc
        if ($null -eq $best -or $ts -gt $best.Ts) {
            $best = @{ File = $f.FullName; Raw = $parsed.Raw; Ts = $ts }
        }
    }

    # 3. Primes payload: prefer the clean archive file; fall back to the DB value.
    $primesFile = "$queueRoot\bc-ab-primes\outputs\processed\refresh-project-$id.json"
    $primes = $null
    if (Test-Path $primesFile) {
        $p = Get-BriefItem $primesFile
        if ($p) { $primes = @{ Raw = $p.Raw; Ts = ($p.GeneratedAtUtc ?? (Get-Item $primesFile).LastWriteTimeUtc); Src = $primesFile } }
    }
    if ($null -eq $primes) {
        $primes = @{ Raw = $row.DbJson; Ts = [datetime]::UtcNow; Src = '(overwritten DB value)' }
    }

    $action = if ($best) { "RESTORE from $($best.File | Split-Path -Leaf) [$(($best.File -split '\\QueueDrain\\')[1] -replace '\\outputs\\processed\\.*','')] ts=$($best.Ts.ToString('u'))" }
              else { 'RELABEL row -> PrimeConsultantResearch (no first-pass archive found)' }
    Write-Host ("MPI {0,-6} {1}" -f $id, $action)

    if (-not $Commit) { continue }

    $tx = $con.BeginTransaction()
    try {
        if ($best) {
            # Restore first-pass content.
            $u = $con.CreateCommand(); $u.Transaction = $tx
            $u.CommandText = @"
UPDATE opportunities.MajorProjectEnrichment
SET ResultJson = @json,
    LastRefreshAtUtc = @ts,
    UpdatedAtUtc = sysdatetimeoffset(),
    Notes = N'Restored from drain archive after primes-ingest overwrite (BD-Audit-2026-06-09 C1).'
WHERE Id = @id AND ProviderName = N'ProjectBrief';
"@
            [void]$u.Parameters.AddWithValue('@json', $best.Raw)
            [void]$u.Parameters.AddWithValue('@ts', [datetimeoffset]$best.Ts)
            [void]$u.Parameters.AddWithValue('@id', $row.EnrichmentId)
            if ($u.ExecuteNonQuery() -ne 1) { throw "restore UPDATE affected != 1 row for enrichment $($row.EnrichmentId)" }

            # File primes under its own provider (skip if somehow present).
            $i = $con.CreateCommand(); $i.Transaction = $tx
            $i.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment
               WHERE MajorProjectsInventoryId = @mpi AND ProviderName = N'PrimeConsultantResearch')
INSERT INTO opportunities.MajorProjectEnrichment
    (MajorProjectsInventoryId, ProviderName, Status, LastRefreshAtUtc, LastAttemptAtUtc,
     NextRefreshAtUtc, Attempts, ResultJson, Notes, CreatedAtUtc, UpdatedAtUtc)
VALUES
    (@mpi, N'PrimeConsultantResearch', N'ok', @ts, @ts,
     DATEADD(DAY, 90, @ts), 1, @json,
     N'Re-filed from bc-ab-primes drain after mis-ingest as ProjectBrief (BD-Audit-2026-06-09 C1).',
     sysdatetimeoffset(), sysdatetimeoffset());
"@
            [void]$i.Parameters.AddWithValue('@mpi', $id)
            [void]$i.Parameters.AddWithValue('@ts', [datetimeoffset]$primes.Ts)
            [void]$i.Parameters.AddWithValue('@json', $primes.Raw)
            [void]$i.ExecuteNonQuery()
            $script:primesFiled++
            $script:restored++
        }
        else {
            # No first-pass to restore: the row IS primes content — relabel it.
            $u = $con.CreateCommand(); $u.Transaction = $tx
            $u.CommandText = @"
UPDATE opportunities.MajorProjectEnrichment
SET ProviderName = N'PrimeConsultantResearch',
    UpdatedAtUtc = sysdatetimeoffset(),
    Notes = N'Relabeled from ProjectBrief: primes drain mis-ingest, no first-pass archive to restore (BD-Audit-2026-06-09 C1).'
WHERE Id = @id AND ProviderName = N'ProjectBrief'
  AND NOT EXISTS (SELECT 1 FROM opportunities.MajorProjectEnrichment
                  WHERE MajorProjectsInventoryId = @mpi AND ProviderName = N'PrimeConsultantResearch');
"@
            [void]$u.Parameters.AddWithValue('@id', $row.EnrichmentId)
            [void]$u.Parameters.AddWithValue('@mpi', $id)
            if ($u.ExecuteNonQuery() -ne 1) { throw "relabel UPDATE affected != 1 row for enrichment $($row.EnrichmentId) (PrimeConsultantResearch row already exists?)" }
            $script:relabeled++
        }

        $tx.Commit()
    }
    catch {
        $tx.Rollback()
        Write-Warning "MPI $($id): FAILED — $_"
        $script:errors++
    }
}

$con.Close()
Write-Host ''
Write-Host "Done. restored=$restored relabeled=$relabeled primesRowsFiled=$primesFiled errors=$errors"
if (-not $Commit) { Write-Host 'Dry-run only. Re-run with -Commit to apply.' }
