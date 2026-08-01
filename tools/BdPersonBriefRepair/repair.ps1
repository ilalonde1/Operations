#requires -Version 7
<#
.SYNOPSIS
    Repair the 2026-06-10 people-brief misattribution (one-shot, historical).

.DESCRIPTION
    People drain outputs were keyed by batch ORDINAL and ingested as if the
    ordinal were an IntelPerson.Id (see memory/commit 2ed7ac2). This script
    re-attributes every SURVIVING output file in outputs\processed\ to its
    true subject:

      1. Cluster files to candidate batches by LastWriteTime windows.
      2. For each file, look up candidate batch row[ordinal].displayName and
         require the name to appear in the brief content (prose mentions its
         subject). Exactly one candidate matching -> attributed.
      3. Write a copy with "displayName" injected at the item root into
         _restage\ — the fixed BdQueueDrainIngest (name-resolving) does the
         rest. Ambiguous/unmatched files are listed, not guessed.

    DRY by default: prints the attribution table. -Commit writes _restage.
#>
[CmdletBinding()]
param(
    [string]$QueueRoot = 'C:\ProgramData\KorOperations\QueueDrain\people',
    [switch]$Commit
)

$ErrorActionPreference = 'Stop'

# Timestamp window -> candidate batch(es). Built from the SUMMARY timeline:
# 001/002 ran back-to-back overnight 06-06 (ambiguous window), 003 the same
# afternoon, 010 overnight 06-10, 011 the afternoon of 06-10. Batches
# 004-009 left no surviving files (overwritten before 06-10).
$windows = @(
    @{ From = [datetime]'2026-06-06 00:00'; To = [datetime]'2026-06-06 03:00'; Batches = @('batch-001', 'batch-002') },
    @{ From = [datetime]'2026-06-06 12:00'; To = [datetime]'2026-06-06 15:00'; Batches = @('batch-003') },
    @{ From = [datetime]'2026-06-09 22:00'; To = [datetime]'2026-06-10 03:00'; Batches = @('batch-010') },
    @{ From = [datetime]'2026-06-10 13:00'; To = [datetime]'2026-06-10 17:00'; Batches = @('batch-011') }
)

# Load batch row maps: batchName -> @{ ordinal -> displayName }
$batchRows = @{}
foreach ($b in (Get-ChildItem (Join-Path $QueueRoot 'inputs') -Filter 'batch-*.json' -File)) {
    $rows = Get-Content $b.FullName -Raw | ConvertFrom-Json
    $map = @{}
    foreach ($r in $rows) { $map[[int]$r.id] = [string]$r.displayName }
    $batchRows[$b.BaseName] = $map
}

$processed = Get-ChildItem (Join-Path $QueueRoot 'outputs\processed') -Filter 'refresh-person-*.json' -File
$restage = Join-Path $QueueRoot 'outputs\_restage'
if ($Commit) { New-Item -ItemType Directory -Path $restage -Force | Out-Null }

$attributed = 0; $ambiguous = @(); $unmatched = @(); $noWindow = @()
$perBatch = @{}

foreach ($f in $processed) {
    $ordinal = [int]([regex]::Match($f.Name, 'refresh-person-(\d+)').Groups[1].Value)
    $window = $windows | Where-Object { $f.LastWriteTime -ge $_.From -and $f.LastWriteTime -le $_.To } | Select-Object -First 1
    if (-not $window) { $noWindow += $f.Name; continue }

    $raw = Get-Content $f.FullName -Raw
    $hits = @()
    foreach ($b in $window.Batches) {
        $dn = $batchRows[$b][$ordinal]
        if ($dn -and $raw -match [regex]::Escape($dn)) { $hits += @{ Batch = $b; Name = $dn } }
    }

    if ($hits.Count -eq 1) {
        $attributed++
        $perBatch[$hits[0].Batch] = ($perBatch[$hits[0].Batch] ?? 0) + 1
        if ($Commit) {
            # Inject displayName at the item root (envelope or legacy shape).
            $json = $raw | ConvertFrom-Json
            $item = if ($json.items) { $json.items[0] } else { $json }
            $item | Add-Member -NotePropertyName 'displayName' -NotePropertyValue $hits[0].Name -Force
            # Unique stage name that still satisfies the ingest's
            # refresh-person-{digits} filename parser (the digits are ignored
            # by the name-resolving people path): batchNumber*1000 + ordinal.
            $batchNum = [int]([regex]::Match($hits[0].Batch, 'batch-(\d+)').Groups[1].Value)
            $stageName = "refresh-person-{0}.json" -f ($batchNum * 1000 + $ordinal)
            $json | ConvertTo-Json -Depth 24 | Set-Content (Join-Path $restage $stageName) -Encoding UTF8
        }
    }
    elseif ($hits.Count -gt 1) { $ambiguous += "$($f.Name): matches $($hits.Batch -join ' AND ')" }
    else { $unmatched += "$($f.Name) [$($window.Batches -join '/')]" }
}

"files scanned:      $($processed.Count)"
"attributed:         $attributed"
$perBatch.GetEnumerator() | Sort-Object Name | ForEach-Object { "  {0}: {1}" -f $_.Key, $_.Value }
"ambiguous:          $($ambiguous.Count)"
"unmatched-in-window:$($unmatched.Count)"
"no-window:          $($noWindow.Count)"
if (-not $Commit) { "`nDRY RUN — re-run with -Commit to write _restage\ for ingest." }
else { "`nStaged to $restage — ingest with: BdQueueDrainIngest --kind people --dir `"$restage`"" }
if ($ambiguous.Count -gt 0) { "`nAMBIGUOUS:"; $ambiguous | Select-Object -First 10 }
if ($unmatched.Count -gt 0) { "`nUNMATCHED (first 10):"; $unmatched | Select-Object -First 10 }
