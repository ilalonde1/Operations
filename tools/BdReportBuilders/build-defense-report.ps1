$ErrorActionPreference = 'Stop'
# BD-Audit-2026-06-09 M12: this builder was 100% hardcoded prose — regenerating
# it reproduced the June-8 snapshot forever while .defense-final.json sat
# unread. It now reads the JSON like the other sector builders. Defense-specific
# framing (DCC procurement gates, security clearances, EllisDon strategic
# observation) is kept as section intros; all project data comes from the JSON.
# JSON shape: top-level Id/Name/Stage/Province/City/Proponent/Cost/Verdict,
# honing intel nested under .Item (korAngle/status/schedule/description/...).
$briefs = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.defense-final.json' -Raw | ConvertFrom-Json
if (-not $briefs -or $briefs.Count -eq 0) {
    throw "No briefs in .defense-final.json. Re-run the defense pull query."
}
# Verdict-age wiring (freshness-on-build, 2026-07-10): every verdict carries
# HonedAtUtc; TTL per verdict class decides staleness. A report can no longer
# silently reprint dead urgency - stale verdicts are annotated on their face.
$ttlDays = @{ 'PURSUE_URGENT' = 7; 'PURSUE' = 30; 'MONITOR' = 90; 'DISCOVER' = 90; 'DEAD' = 365 }
$nowUtc = (Get-Date).ToUniversalTime()
foreach ($b in $briefs) {
    $age = $null; $stale = $false
    if ($b.HonedAtUtc) {
        $age = [int][math]::Floor(($nowUtc - ([datetime]::Parse($b.HonedAtUtc).ToUniversalTime())).TotalDays)
        $ttl = if ($b.Verdict -and $ttlDays.ContainsKey([string]$b.Verdict)) { $ttlDays[[string]$b.Verdict] } else { 30 }
        $stale = $age -gt $ttl
    }
    $b | Add-Member -NotePropertyName AgeDays -NotePropertyValue $age -Force
    $b | Add-Member -NotePropertyName IsStale -NotePropertyValue $stale -Force
}
function AgeLine { param($b)
    if ($null -eq $b.AgeDays) { return 'Verdict honed: (undated - treat as stale)' }
    $d = [datetime]::Parse($b.HonedAtUtc).ToUniversalTime().ToString('yyyy-MM-dd')
    $t = "Verdict honed: $d ($($b.AgeDays)d ago)"
    if ($b.IsStale) { $t += ' - STALE, exceeds TTL for this verdict class; refresh before acting' }
    return $t
}
$urgent = @($briefs | Where-Object {$_.Verdict -eq 'PURSUE_URGENT'})
$pursue = @($briefs | Where-Object {$_.Verdict -in @('PURSUE','PURSUE_URGENT')})
$monitor = @($briefs | Where-Object {$_.Verdict -eq 'MONITOR'})
$dead = @($briefs | Where-Object {$_.Verdict -eq 'DEAD'})
$discover = @($briefs | Where-Object {$_.Verdict -eq 'DISCOVER'})

$word = New-Object -ComObject Word.Application
try {
    $word.Visible = $false
    $doc = $word.Documents.Add(); $sel = $word.Selection
    $styles = $doc.Styles
    $styles.Item('Heading 1').Font.Size = 16; $styles.Item('Heading 1').Font.Bold = $true
    $styles.Item('Heading 1').ParagraphFormat.SpaceBefore = 0; $styles.Item('Heading 1').ParagraphFormat.SpaceAfter = 6
    $styles.Item('Heading 2').Font.Size = 12; $styles.Item('Heading 2').Font.Bold = $true
    $styles.Item('Heading 2').ParagraphFormat.SpaceBefore = 10; $styles.Item('Heading 2').ParagraphFormat.SpaceAfter = 3
    $styles.Item('Heading 3').Font.Size = 10.5; $styles.Item('Heading 3').Font.Bold = $true
    $styles.Item('Heading 3').ParagraphFormat.SpaceBefore = 6; $styles.Item('Heading 3').ParagraphFormat.SpaceAfter = 2
    $styles.Item('Normal').Font.Size = 10; $styles.Item('Normal').ParagraphFormat.SpaceAfter = 4
    $doc.PageSetup.TopMargin = 36; $doc.PageSetup.BottomMargin = 36; $doc.PageSetup.LeftMargin = 54; $doc.PageSetup.RightMargin = 54

    function H1 { param($t) $sel.Style = $doc.Styles.Item('Heading 1'); $sel.TypeText($t); $sel.TypeParagraph() }
    function H2 { param($t) $sel.Style = $doc.Styles.Item('Heading 2'); $sel.TypeText($t); $sel.TypeParagraph() }
    function H3 { param($t) $sel.Style = $doc.Styles.Item('Heading 3'); $sel.TypeText($t); $sel.TypeParagraph() }
    function P  { param($t) $sel.Style = $doc.Styles.Item('Normal');    $sel.TypeText($t); $sel.TypeParagraph() }
    function B  { param($lbl, $val) $sel.Style = $doc.Styles.Item('Normal'); $sel.Font.Bold = 1; $sel.TypeText($lbl); $sel.Font.Bold = 0; $sel.TypeText($val); $sel.TypeParagraph() }
    function Italic { param($t) $sel.Style = $doc.Styles.Item('Normal'); $sel.Font.Italic = 1; $sel.Font.Size = 9; $sel.TypeText($t); $sel.Font.Italic = 0; $sel.Font.Size = 10; $sel.TypeParagraph() }
    function Safe { param($v, $max) if (-not $v) { return '' }; $s = [string]$v; if ($s.Length -le $max) { return $s }; return $s.Substring(0, $max) }
    function MakeTable { param($headers, $rows)
        $c = $headers.Count
        if ($rows.Count -gt 0 -and -not ($rows[0] -is [System.Collections.IList] -or $rows[0] -is [array])) {
            $chunked = New-Object System.Collections.ArrayList
            for ($i = 0; $i -lt $rows.Count; $i += $c) {
                $row = @()
                for ($j = 0; $j -lt $c; $j++) {
                    if ($i + $j -lt $rows.Count) { $row += [string]$rows[$i + $j] } else { $row += '' }
                }
                [void]$chunked.Add($row)
            }
            $rows = $chunked
        }
        $r = $rows.Count + 1
        $rng = $sel.Range; $tbl = $doc.Tables.Add($rng, $r, $c); $tbl.Style = "Table Grid"; $tbl.Range.Font.Size = 9
        for ($i = 0; $i -lt $c; $i++) { $tbl.Cell(1, $i+1).Range.Text = $headers[$i]; $tbl.Cell(1, $i+1).Range.Bold = $true }
        for ($r2 = 0; $r2 -lt $rows.Count; $r2++) {
            for ($c2 = 0; $c2 -lt $c; $c2++) { $tbl.Cell($r2+2, $c2+1).Range.Text = [string]$rows[$r2][$c2] }
        }
        $word.Selection.EndKey(6) | Out-Null; $sel.TypeParagraph()
    }

    H1 'KOR Structural — Defense / Military BD Report'
    Italic 'BC + Alberta defense and military construction pipeline, with named contacts and KOR action items. Generated from .defense-final.json (Sonnet first-pass + honing verification). Verdicts: PURSUE_URGENT / PURSUE / MONITOR / DEAD / DISCOVER. PROMPT-driven Yurkovich-class error catch confirmed the original DB filter pulled false positives (schools, civic arenas, RCMP, First Nations commercial misclassified as defense) — re-categorization candidates are flagged inside the MONITOR/DEAD briefs.'

    H2 'Executive Summary'
    B 'Defense projects honed: ' ($briefs.Count.ToString())
    B 'PURSUE_URGENT — IMMEDIATE action: ' ($urgent.Count.ToString())
    B 'PURSUE — open opportunities (incl. urgent): ' ($pursue.Count.ToString())
    B 'MONITOR — watching, incl. re-categorization candidates: ' ($monitor.Count.ToString())
    B 'DISCOVER — pre-procurement: ' ($discover.Count.ToString())
    B 'DEAD — delivered, misclassified, or unverifiable: ' ($dead.Count.ToString())

    $fresh = @($briefs | Where-Object { $null -ne $_.AgeDays -and -not $_.IsStale })
    $staleC = @($briefs | Where-Object { $_.IsStale -or $null -eq $_.AgeDays })
    B 'Verdict freshness: ' ("$($fresh.Count) of $($briefs.Count) verdicts within their freshness window; $($staleC.Count) stale or undated (annotated in place below).")
    Italic ("Report built " + $nowUtc.ToString('yyyy-MM-dd HH:mm') + " UTC. Verdict ages are computed at build time - a regenerated report is always honest about how old its intelligence is.")

    P 'Defense procurement reality: nearly everything flows through DCC (Defence Construction Canada) procurement gates, with CFHA (Canadian Forces Housing Agency) running the residential programs. Security clearance (Reliability or SECRET-level FSC) gates many structural sub slots — every PURSUE brief should be checked for clearance requirements before outreach. KOR''s entry is almost always as structural sub on a design-build prime''s team (EllisDon, PCL, Graham, Bird), not direct to DND.'

    if ($urgent.Count -gt 0) {
        H2 ("URGENT — IMMEDIATE action required ($($urgent.Count))")
        foreach ($p in $urgent) {
            H3 ("$($p.Id): $($p.Name)")
            B 'Location: ' "$($p.City), $($p.Province)"
            B 'Proponent: ' (Safe $p.Proponent 100)
            if ($p.Cost) { B 'Value: ' $p.Cost }
            if ($p.Stage) { B 'Stage: ' $p.Stage }
            B 'Status: ' (Safe $p.Item.status 300)
        Italic (AgeLine $p)
            P (Safe $p.Item.korAngle 600)
            if ($p.Item.schedule) { Italic ('Schedule: ' + (Safe $p.Item.schedule 300)) }
            P ''
        }
    }

    H2 ("1. PURSUE — Active opportunities ($($pursue.Count) projects)")
    P 'Each PURSUE has an open structural sub-consultant window verified by the honing pass. The pursuit play is the design prime''s team, gated by DCC procurement and clearance requirements.'
    foreach ($p in $pursue) {
        H3 ("$($p.Id): $($p.Name)")
        B 'Location: ' "$($p.City), $($p.Province)"
        B 'Proponent: ' (Safe $p.Proponent 100)
        if ($p.Cost) { B 'Value: ' $p.Cost }
        if ($p.Stage) { B 'Stage: ' $p.Stage }
        B 'Status: ' (Safe $p.Item.status 300)
        Italic (AgeLine $p)
        P (Safe $p.Item.korAngle 600)
        if ($p.Item.schedule) { Italic ('Schedule: ' + (Safe $p.Item.schedule 300)) }
        P ''
    }

    H2 ("2. MONITOR — Watching but not actively pursuing ($($monitor.Count) projects)")
    P 'Locked current phases, clearance-gated mega-packages, and filter false positives flagged by the honing pass for re-categorization out of the defense pipeline (civic, RCMP, First Nations commercial). The "Why MONITOR" column carries the verdict reasoning.'
    MakeTable @('Id','Project','Proponent','Province','Age','Why MONITOR') @(
        @(($monitor | ForEach-Object {
            $ageCell = if ($null -eq $_.AgeDays) { '?' } elseif ($_.IsStale) { "$($_.AgeDays)d STALE" } else { "$($_.AgeDays)d" }
            @($_.Id, (Safe $_.Name 50),
              (Safe $_.Proponent 35),
              $_.Province,
              $ageCell,
              (Safe $_.Item.korAngle 130))
        }))
    )

    if ($discover.Count -gt 0) {
        H2 ("3. DISCOVER — Pre-procurement relationship-build ($($discover.Count) projects)")
        foreach ($p in $discover) {
            B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
            P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
            P (Safe $p.Item.korAngle 400)
            P ''
        }
    }

    H2 ("4. DEAD — Removed from pipeline ($($dead.Count) projects)")
    P 'Delivered projects, misclassified non-defense MPIs, and unverifiable data-entry artifacts. Reference only — the "Why DEAD" column carries the honing reasoning (retire or re-categorize per brief).'
    MakeTable @('Id','Project','Province','Why DEAD') @(
        @(($dead | ForEach-Object {
            @($_.Id, (Safe $_.Name 55),
              $_.Province,
              (Safe $_.Item.korAngle 140))
        }))
    )

    H2 '5. Strategic observations'

    H3 'DCC is the gate, primes are the door'
    P 'Defense structural work is procured via DCC, but KOR''s entry point is the design-build prime''s sub-consultant list, not DCC directly. Identify the likely D-B contenders per pursuit (EllisDon, PCL, Graham, Bird) and pre-position before the RFP closes the team. Subscribe to MERX / buyandsell.gc.ca DCC notices for Pacific and Western regions.'

    H3 'EllisDon as strategic defense relationship'
    P 'EllisDon recurs across the defense briefs as design prime (see PURSUE/MONITOR entries above) — the same firm KOR is building toward in BC healthcare. EllisDon is to defense what Graham Design Builders is to BC healthcare: a strategic relationship to build deliberately. Contacts via their Victoria, Vancouver, or Calgary offices (named contacts in the EllisDon deep-dive and IntelPersonAffiliation).'

    H3 'Security clearance capability'
    P 'Several defense packages require Reliability or SECRET-level facility clearance for the structural sub. KOR should evaluate clearance capability once a concrete pursuit firms up — it is the recurring competitive gate across this sector.'

    H3 'Defense MPI ingestion hygiene'
    P 'The honing pass functioned partly as a data-hygiene audit: geographic proximity to a CFB caused schools, arenas, RCMP detachments, and First Nations commercial projects to auto-classify as defense. Tighter ingestion filters needed; re-categorization candidates are flagged per-brief in the MONITOR and DEAD sections above.'

    # === SAVE ===
    $outPath = 'C:\Users\ilalonde\Desktop\KOR-Defense-Military-BD-Report.docx'
    $doc.SaveAs([ref]$outPath, [ref]16)
}
finally {
    if ($doc) { try { $doc.Close(0) } catch {} }
    if ($word) { try { $word.Quit() } catch {} }
    if ($sel) { try { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($sel) } catch {} }
    if ($doc) { try { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc) } catch {} }
    if ($word) { try { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) } catch {} }
    [gc]::Collect(); [gc]::WaitForPendingFinalizers()
}
"Wrote: $outPath"
