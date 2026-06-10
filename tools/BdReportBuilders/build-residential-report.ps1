$ErrorActionPreference = 'Stop'
$briefs = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.residential-final.json' -Raw | ConvertFrom-Json
if (-not $briefs -or $briefs.Count -eq 0) {
    throw "No briefs in .residential-final.json. Re-run the residential pull query."
}
$urgent = $briefs | Where-Object {$_.Verdict -eq 'PURSUE_URGENT'}
$pursue = $briefs | Where-Object {$_.Verdict -eq 'PURSUE'}
$monitor = $briefs | Where-Object {$_.Verdict -eq 'MONITOR'}
$dead = $briefs | Where-Object {$_.Verdict -eq 'DEAD'}
$discover = $briefs | Where-Object {$_.Verdict -eq 'DISCOVER'}
$duplicate = $briefs | Where-Object {$_.Verdict -eq 'DUPLICATE'}

$word = New-Object -ComObject Word.Application
# BD-Audit-2026-06-09 M12: any throw after Word.Application creation must quit
# Word and release COM, or an orphaned WINWORD.EXE is left behind. Cleanup is
# idempotent (catch-wrapped) so the successful SaveAs path is not double-quit.
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

    H1 'KOR Structural — BC + AB Residential / Condo BD Report'
    Italic 'Residential / condo tower construction pipeline in BC and Alberta — KOR''s largest BD market. Compiled 2026-06-09 from Sonnet first-pass + honing batch-001 verification (pt1 250 items, pt2 233 pending). High-rise concrete + mid-rise concrete-wood hybrid = direct KOR core. Memory-confirmed KOR client list (Wesgroup, Bosa, Reliance, Westland, Belford, Anthem, Cressey, Peterson, Strand, Beedie, Capital Region Housing, Onni, Concord, Polygon) checked against every project.'

    H2 'Executive Summary'
    B 'Residential projects honed (batch-001 only): ' ($briefs.Count.ToString())
    B 'PURSUE_URGENT: ' ($urgent.Count.ToString())
    B 'PURSUE — open opportunities: ' ($pursue.Count.ToString())
    B 'MONITOR — developer pipeline open: ' ($monitor.Count.ToString())
    B 'DISCOVER — pre-launch: ' ($discover.Count.ToString())
    B 'DEAD — locked or sold-out: ' ($dead.Count.ToString())
    B 'DUPLICATE — flagged for MPI consolidation: ' ($duplicate.Count.ToString())

    P 'Residential is RELATIONSHIP-driven — most BC residential is developer-led with in-house GC (Wesgroup, Bosa, Polygon, Onni) or developer + selected GC + pre-qualified subs. Architect drives structural-engineer selection. Memory-confirmed KOR client developers checked against every project for prior-relationship advantage.'

    P 'NOTE: This report covers batch-001 (250 of 483 residential MPIs). Pt2 (233 items) honing not yet launched. Final residential coverage at ~100% pending pt2 completion.'

    if ($urgent.Count -gt 0) {
        H2 'URGENT — IMMEDIATE action required'
        foreach ($p in $urgent) {
            H3 ($p.Name)
            B 'Id: ' $p.Id.ToString()
            B 'Province: ' $p.Province
            B 'Proponent: ' $p.Proponent
            B 'Cost: ' $p.Cost
            B 'Status: ' (Safe $p.status 200)
            P (Safe $p.korAngle 500)
            P ''
        }
    }

    H2 'PURSUE — Open opportunities'
    foreach ($p in $pursue) {
        B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
        P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
        P (Safe $p.korAngle 500)
        if ($p.status) { Italic ("Status: " + (Safe $p.status 300)) }
        P ''
    }

    H2 'MONITOR — Developer pipeline open'
    MakeTable @('Id','Project','Developer','Province','Cost','Why MONITOR') @(
        @(($monitor | ForEach-Object {
            @($_.Id, (Safe $_.Name 45), (Safe $_.Proponent 28), $_.Province, $_.Cost, (Safe $_.korAngle 90))
        }))
    )

    if ($discover.Count -gt 0) {
        H2 'DISCOVER — Pre-launch relationship-build'
        foreach ($p in $discover) {
            B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
            P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
            P (Safe $p.korAngle 400)
            P ''
        }
    }

    H2 'DEAD — Locked or sold-out'
    MakeTable @('Id','Project','Developer','Province') @(
        @(($dead | ForEach-Object {
            @($_.Id, (Safe $_.Name 55), (Safe $_.Proponent 30), $_.Province)
        }))
    )

    # BD-Audit-2026-06-09 Minor 10: $duplicate was computed but never rendered —
    # DUPLICATE-verdict briefs were silently dropped from the report.
    if ($duplicate.Count -gt 0) {
        H2 'DUPLICATE — Flagged for MPI consolidation'
        P 'Sonnet identified these as same-project duplicates. Worth a consolidation migration before the next drain cycle.'
        MakeTable @('Id','Project','Developer','Why DUPLICATE') @(
            @(($duplicate | ForEach-Object {
                @($_.Id, (Safe $_.Name 60), (Safe $_.Proponent 30), (Safe $_.korAngle 120))
            }))
        )
    }

    H2 'Strategic Synthesis'

    H3 'Developer-relationship play dominates residential'
    P 'Residential market is developer-led. Most BC residential structural is awarded via developer-PQ-list rather than open RFP. The compounding move is operator-level relationship rather than project-by-project chase. Memory-confirmed KOR clients (Wesgroup, Bosa, Reliance, Anthem, Cressey, Beedie, Onni, Polygon) each have multi-decade pipelines.'

    H3 'BMZ legacy = warm-intro lever'
    P 'KOR was Bryson Markulin Zickmantel pre-2021. Many BC residential developers have BMZ-era references that pre-date the rebrand. Audit KOR portfolio for prior BMZ work on each developer in MONITOR list — that''s the warm-intro path. Memory feedback explicitly noted BMZ legacy as KOR''s 30+ year BC residential differentiator.'

    H3 'In-house GC developers = direct entry'
    P 'Wesgroup, Bosa, Polygon, Onni all have in-house construction divisions. For these developers, structural selection is centralized — one VP Development/VP Construction relationship covers the entire pipeline. Identify the structural-engineer decision-maker at each.'

    H3 'Mass timber hybrid = emerging differentiator'
    P 'TELUS Ocean (EllisDon GC, mass timber + concrete hybrid) is the Vancouver model. UBC Brock Commons set the residential mass timber precedent. KOR''s ability to deliver mass timber + concrete hybrid is a competitive advantage on mid-rise (8-12 storey) residential.'

    H2 'Recommended next actions'
    P '1. **Audit MONITOR list for repeat developers** — developers appearing 3+ times = strategic-relationship targets. Memory-confirmed KOR clients appearing in MONITOR are warm-leads.'
    P '2. **Launch residential-honing pt2** (233 items remaining) for complete coverage. After ingest, this report regenerates with full pipeline.'
    P '3. **BMZ legacy audit** — surface pre-2021 KOR portfolio references per developer to establish warm-intro lead-ins.'
    P '4. **Wesgroup / Bosa / Onni in-house construction relationships** — single VP Development relationship at each unlocks entire pipeline.'

    $outPath = 'C:\Users\ilalonde\Desktop\KOR-Residential-BD-Report.docx'
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
