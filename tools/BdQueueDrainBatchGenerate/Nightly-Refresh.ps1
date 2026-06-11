#requires -Version 7
<#
.SYNOPSIS
    The BD refresh trigger: housekeeps the drain queues, generates whatever
    work is DUE per kind, and prints the run cards for the evening's sessions.

.DESCRIPTION
    Encodes the manual loop from the 2026-06-09/10 sessions:

      1. Per kind: move fully-summarized input batches to inputs\_done so
         the sibling-exclusion scan and batch auto-discovery only see live
         work.
      2. Run Generate-Batch with the next free batch number. The selectors
         are the freshness policy — they pick rows whose enrichment is due
         (age filters + recently-honed exclusion) and write NOTHING when a
         kind is fully fresh.
      3. Print which queues have pending batches (pre-existing + new) as
         ready-to-paste run cards, and append a line to refresh-log.txt.

    Sessions still launch manually (claude CLI per queue). The full
    automation path — scheduling the Worker's BdResearchExecutorService /
    BdPersonResearchExecutorService against the NextRefreshAtUtc due-dates —
    is the planned platform end-state; this script is the deterministic
    trigger until then.

.EXAMPLE
    .\Nightly-Refresh.ps1
    # Typically via the scheduled task 'KOR BD Nightly Refresh' at 21:30.
#>
[CmdletBinding()]
param(
    [string]$QueueRoot = 'C:\ProgramData\KorOperations\QueueDrain',
    [string]$ConnectionString = ($env:KOR_OPPORTUNITIES_OPPORTUNITIESDB ??
        [Environment]::GetEnvironmentVariable('KOR_OPPORTUNITIES_OPPORTUNITIESDB', 'User'))
)

$ErrorActionPreference = 'Stop'
if (-not $ConnectionString) { throw 'KOR_OPPORTUNITIES_OPPORTUNITIESDB not set.' }

# kind -> (queue dir name, batch size). Kinds whose selectors self-limit
# stay listed even when usually empty (proponents) — empty generation is
# the freshness signal, not an error.
$kinds = [ordered]@{
    'projects'        = @{ Take = 150 }
    'honing-projects' = @{ Take = 150 }
    'orgs'            = @{ Take = 150 }
    'honing-orgs'     = @{ Take = 120 }
    'people'          = @{ Take = 150 }
    'honing-people'   = @{ Take = 120 }
    'proponents'      = @{ Take = 150 }
}

$generateBatch = Join-Path $PSScriptRoot 'Generate-Batch.ps1'
$runCards = @()
$report = @()

# m127 auto-revival: orgs culled for having no BD surface get their research
# slot back the moment something live links them (new MPI role, affiliation,
# or intel). m126 junk-name suppressions stay — those need a name fix first.
Add-Type -AssemblyName System.Data
$revCon = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$revCon.Open()
$revCmd = $revCon.CreateCommand(); $revCmd.CommandTimeout = 300
$revCmd.CommandText = @"
UPDATE co SET EnrichmentSuppressedAtUtc = NULL, EnrichmentSuppressedReason = NULL
FROM opportunities.CanonicalOrg co
WHERE co.EnrichmentSuppressedAtUtc IS NOT NULL
  AND co.EnrichmentSuppressedReason LIKE N'm127:%'
  AND (
       EXISTS (SELECT 1 FROM opportunities.MajorProjectsInventory m WHERE m.RetiredAtUtc IS NULL
               AND (m.ArchitectCanonicalOrgId = co.Id OR m.GeneralContractorCanonicalOrgId = co.Id
                    OR m.StructuralEngineerCanonicalOrgId = co.Id OR m.ProponentCanonicalOrgId = co.Id))
    OR EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL)
    OR EXISTS (SELECT 1 FROM opportunities.IntelSignal s WHERE s.CanonicalOrgId = co.Id AND s.RetiredAtUtc IS NULL)
    OR EXISTS (SELECT 1 FROM opportunities.IntelAction x WHERE x.CanonicalOrgId = co.Id AND x.RetiredAtUtc IS NULL)
  );
"@
$revived = $revCmd.ExecuteNonQuery()
$revCon.Close()
if ($revived -gt 0) { $report += "m127 auto-revival: $revived org(s) regained research slots" }

foreach ($kind in $kinds.Keys) {
    $queueDir = Join-Path $QueueRoot $kind
    $inputs = Join-Path $queueDir 'inputs'
    $outputs = Join-Path $queueDir 'outputs'
    if (-not (Test-Path $inputs)) { $report += "{0,-16} queue dir missing — skipped" -f $kind; continue }

    # 0. Race guard (2026-06-11): generating while a session is actively
    # working this queue produced a 100%-duplicate projects batch (013 ≡ 012)
    # and 135 redundant researches. If the heartbeat says "working" and is
    # fresh (<30 min), leave this kind alone tonight.
    $statusFile = Join-Path $outputs '_status.json'
    if (Test-Path $statusFile) {
        $st = Get-Content $statusFile -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
        $stState = $st.state ?? $st.status
        $stAgeMin = ((Get-Date) - (Get-Item $statusFile).LastWriteTime).TotalMinutes
        if ($stState -eq 'working' -and $stAgeMin -lt 30) {
            $report += "{0,-16} SESSION ACTIVE ({1}/{2}, tick {3:N0}m ago) — generation skipped" -f $kind, $st.completed, $st.total, $stAgeMin
            $runCards += $kind  # still pending work; just don't add more
            continue
        }
    }

    # 1. Housekeep: summarized batches out of the live glob.
    $doneDir = Join-Path $inputs '_done'
    New-Item -ItemType Directory -Path $doneDir -Force | Out-Null
    $pending = 0
    foreach ($b in (Get-ChildItem $inputs -Filter 'batch-*.json' -File)) {
        if (Test-Path (Join-Path $outputs "SUMMARY-$($b.BaseName).txt")) {
            Move-Item $b.FullName $doneDir -Force
        }
        else { $pending++ }
    }

    # 2. Next free number across live + done.
    $nums = @(Get-ChildItem $inputs, $doneDir -Filter 'batch-*.json' -File -ErrorAction SilentlyContinue |
        ForEach-Object { [int]([regex]::Match($_.Name, 'batch-(\d+)').Groups[1].Value) })
    $next = [int]((($nums | Measure-Object -Maximum).Maximum ?? 0) + 1)

    # Generate-Batch reports via Write-Host (information stream) — capture
    # everything; whether a batch was written is verified on DISK, not by
    # parsing console text.
    & $generateBatch -Kind $kind -BatchNumber $next -Take $kinds[$kind].Take -ConnectionString $ConnectionString *>&1 | Out-Null
    $newBatch = Join-Path $inputs ("batch-{0:D3}.json" -f $next)
    $wrote = if (Test-Path $newBatch) { (Get-Content $newBatch -Raw | ConvertFrom-Json).Count } else { 0 }

    # Recount live pending from disk (pre-existing un-summarized + new).
    $pending = (Get-ChildItem $inputs -Filter 'batch-*.json' -File |
        Where-Object { -not (Test-Path (Join-Path $outputs "SUMMARY-$($_.BaseName).txt")) } |
        Measure-Object).Count

    $report += "{0,-16} new-rows={1,-5} pending-batches={2}" -f $kind, $wrote, $pending
    if ($pending -gt 0) { $runCards += $kind }
}

$lines = @('', "=== BD NIGHTLY REFRESH — {0:yyyy-MM-dd HH:mm} ===" -f (Get-Date)) + $report + @('')
if ($runCards.Count -eq 0) {
    $lines += 'All kinds fresh — nothing to run tonight.'
}
else {
    $lines += "RUN TONIGHT ($($runCards.Count) queue$(if ($runCards.Count -ne 1) { 's' })) — paste 'Read PROMPT.md in this directory and execute it.' into each:"
    foreach ($q in $runCards) {
        $lines += ''
        $lines += "  cd $QueueRoot\$q"
        $lines += '  claude --model claude-sonnet-4-6 --dangerously-skip-permissions'
    }
    $lines += ''
    $lines += 'Next morning: tell Claude which queues finished — it ingests and verifies.'
}

# Console (visible on manual runs) + REPORT FILE (the scheduled task runs
# headless — this file IS the report): latest run overwrites refresh-report.txt,
# the rolling one-liner history appends to refresh-log.txt.
$lines
Set-Content (Join-Path $QueueRoot 'refresh-report.txt') ($lines -join [Environment]::NewLine)
Add-Content (Join-Path $QueueRoot 'refresh-log.txt') ("{0:yyyy-MM-dd HH:mm}  {1}" -f (Get-Date), (($report -join ' | ') -replace '\s+', ' '))

# DB bridge (m128): the Worker's BdMorningReportJob on KOR-APP01 embeds the
# latest row in the 6am email — the file above is unreachable from there.
try {
    $repCon = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    $repCon.Open()
    $repCmd = $repCon.CreateCommand()
    $repCmd.CommandText = "INSERT INTO opportunities.QueueRefreshReport (PendingQueues, ReportText) VALUES (@q, @t);"
    [void]$repCmd.Parameters.AddWithValue('@q', ($runCards -join ','))
    [void]$repCmd.Parameters.AddWithValue('@t', ($lines -join "`n"))
    [void]$repCmd.ExecuteNonQuery()
    $repCon.Close()
} catch { Write-Warning "QueueRefreshReport insert failed (morning email will show stale trigger data): $_" }

# Generate-Batch's "nothing to drain" warning leaves a non-zero LASTEXITCODE;
# an empty kind is the freshness signal, not a failure.
exit 0
