$ErrorActionPreference = 'Stop'
$all = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.all-honed.json' -Raw | ConvertFrom-Json
# Hospitals (incl Other since those are also healthcare-adjacent)
$briefs = $all | Where-Object { $_.Category -eq 'Hospital' -or $_.Category -eq 'Other' }
if (-not $briefs -or $briefs.Count -eq 0) {
    throw "No 'Hospital' / 'Other' briefs in .all-honed.json (only categories present: $(($all | Group-Object Category | ForEach-Object { $_.Name }) -join ', ')). Re-run the cross-category pull query to refresh .all-honed.json before regenerating this report."
}
$urgent = $briefs | Where-Object {$_.Verdict -eq 'PURSUE_URGENT' -or ($_.Verdict -eq 'PURSUE' -and $_.korAngle -match 'URGENT')}
$pursue = $briefs | Where-Object {$_.Verdict -eq 'PURSUE' -and $_.korAngle -notmatch 'URGENT'}
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

    H1 'KOR Structural — BC + AB Hospitals BD Report'
    Italic ('Healthcare construction pipeline in BC and Alberta. Compiled 2026-06-08 from Sonnet first-pass + honing verification. This is the YURKOVICH CORRECTION PASS — the highest-rigor honing PROMPT in KOR''s pipeline (20-30 tool calls per project). Procurement model identification (Alliance / P3 / Progressive DB captive / Progressive DB external / Traditional DBB / Pre-procurement) is non-negotiable in every brief.')

    H2 'Executive Summary'
    B ('Hospitals + healthcare projects honed: ') ($briefs.Count.ToString())
    B ('PURSUE_URGENT — IMMEDIATE action: ') ($urgent.Count.ToString())
    B ('PURSUE — open opportunities: ') ($pursue.Count.ToString())
    B ('MONITOR — locked but future phases open: ') ($monitor.Count.ToString())
    B ('DISCOVER — pre-procurement relationship-build: ') ($discover.Count.ToString())
    B ('DEAD — Alliance/P3/Stantec-captive locked: ') ($dead.Count.ToString())
    B ('DUPLICATE — flagged for MPI consolidation: ') ($duplicate.Count.ToString())

    P 'Yurkovich correction worked. The honing PROMPT explicitly required procurement model identification via 2 independent sources before marking any project PURSUE. Alliance / P3 / Stantec-captive projects auto-marked DEAD with named incumbent. Progressive DB with external structural slot is where most PURSUE opportunities sit.'

    H2 'URGENT — IMMEDIATE action required'
    P ''
    $rank = 1
    foreach ($p in $urgent) {
        H3 ("$rank. $($p.Name)")
        B ('Id: ') $p.Id.ToString()
        B ('Province: ') $p.Province
        B ('Proponent: ') $p.Proponent
        B ('Cost: ') $p.Cost
        B ('Status: ') $p.status
        P ((Safe $p.korAngle 600))
        P ''
        $rank++
    }

    H2 'PURSUE — Open opportunities (not yet urgent)'
    foreach ($p in $pursue) {
        B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
        P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
        P ((Safe $p.korAngle 450))
        Italic ('Status: ' + $p.status)
        P ''
    }

    H2 'MONITOR — Current phase locked, future phases open'
    MakeTable @('Id','Project','Proponent','Province','Cost','Why MONITOR') @(
        @(($monitor | ForEach-Object {
            @($_.Id, (Safe $_.Name 55),
              (Safe $_.Proponent 30),
              $_.Province, $_.Cost,
              (Safe $_.korAngle 110))
        }))
    )

    H2 'DISCOVER — Pre-procurement relationship-build'
    MakeTable @('Id','Project','Proponent','Province','Cost') @(
        @(($discover | ForEach-Object {
            @($_.Id, (Safe $_.Name 60),
              (Safe $_.Proponent 30),
              $_.Province, $_.Cost)
        }))
    )

    H2 'DEAD — Locked (Alliance / P3 / Stantec-captive / Delivered)'
    MakeTable @('Id','Project','Province','Why DEAD') @(
        @(($dead | ForEach-Object {
            @($_.Id, (Safe $_.Name 60),
              $_.Province,
              (Safe $_.korAngle 140))
        }))
    )

    # BD-Audit-2026-06-09 Minor 10: $duplicate was computed but never rendered —
    # DUPLICATE-verdict briefs were silently dropped from the report.
    if ($duplicate.Count -gt 0) {
        H2 'DUPLICATE — Flagged for MPI consolidation'
        P 'Sonnet identified these as same-project duplicates. Worth a consolidation migration (UHNBC / Vernon Jubilee pattern) before the next drain cycle.'
        MakeTable @('Id','Project','Proponent','Why DUPLICATE') @(
            @(($duplicate | ForEach-Object {
                @($_.Id, (Safe $_.Name 60), (Safe $_.Proponent 30), (Safe $_.korAngle 120))
            }))
        )
    }

    H2 'Strategic Synthesis'

    H3 'Procurement model breakdown'
    P 'The Yurkovich correction worked as designed — Sonnet identified the procurement model on every project before scoring. Alliance projects (Richmond Hospital Yurkovich locked with Entuitive, Cowichan locked with Bush Bohlman) are correctly DEAD. Stantec-captive projects (Cariboo Memorial, several others) are correctly DEAD. The PURSUE list is concentrated in Progressive DB with external structural sub OR Traditional DBB pre-procurement.'

    H3 'NACIC + SACIC twin opportunity — Alberta compassionate intervention'
    P 'Northern Alberta Compassionate Intervention Centre (NACIC, MPI 7036, $90M, Edmonton) and Southern Alberta Compassionate Intervention Centre (SACIC / Spyhill, MPI 6473, $159M, Calgary) are TWIN PROJECTS under the same DBFM procurement. RFP stage NOW. Contract award August 2026. Shortlisted consortia forming teams NOW. KOR''s Alberta presence is the direct angle.'

    H3 'Plant and Animal Health Centre $400M — biggest URGENT'
    P 'MPI 6585. RFP stage with shortlisted teams. Structural sub slot OPEN in each shortlisted team and filling NOW. Identify the shortlisted prime contractors and engage their pre-construction teams immediately.'

    H3 'Vancouver General Hospital Campus Redevelopment Building 1'
    P 'MPI 6494. VCH Campus Building 1 — interim rezoning pending Summer 2026 City Council vote. Pre-procurement but high-priority. Sharon Petty (VCH Director Real Estate Operations, ex-CPO Richmond Hospital) is the relationship.'

    H3 'Vernon Jubilee Hospital Psychiatric — D-B RFP active'
    P 'MPI 3133. IBC RFP stage. Award likely H1 2026. Structural sub within D-B team. If KOR can engage shortlisted teams (likely PCL, Graham, EllisDon, Bird), structural sub slot is accessible.'

    H2 'Recommended next actions'
    P '1. **PURSUE_URGENT — IMMEDIATE outreach** on NACIC + SACIC Alberta DBFM (combined $249M, twin procurement, shortlisted teams forming now). Reach out to known D-B contenders for Alberta health: PCL Health Group, EllisDon, Graham Healthcare.'
    P '2. **Plant and Animal Health Centre $400M** — identify shortlisted teams and engage structurally.'
    P '3. **VGH Building 1** — Sharon Petty at VCH is the relationship. Sharon Petty LinkedIn: linkedin.com/in/sharon-petty-6491a911b/.'
    P '4. **Vernon Jubilee Hospital Psychiatric** — engage shortlisted D-B teams via Daniel Murphy (EllisDon VP Preconstruction dmurphy@ellisdon.com) or equivalent at Graham, PCL, Bird.'
    P '5. **Audit DEAD list** for Alliance / P3 dissolutions or future phase openings. Phase 2/3 of Richmond Hospital, Cowichan, etc. are MONITOR candidates as future Alliance partner re-procurement.'

    # === SAVE ===
    $outPath = 'C:\Users\ilalonde\Desktop\KOR-Hospitals-BD-Report.docx'
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
