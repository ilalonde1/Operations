$ErrorActionPreference = 'Stop'
$briefs = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.postsec-final.json' -Raw | ConvertFrom-Json
if (-not $briefs -or $briefs.Count -eq 0) { throw "No post-sec briefs in .postsec-final.json" }
# BD-Audit-2026-06-09 C8/Minor-1: $urgent was counted in the summary but its
# items rendered nowhere (the "Top 5" section below is hardcoded prose).
# Urgent items also stay in $pursue so the open-opportunities section keeps
# showing them.
$urgent = @($briefs | Where-Object {$_.Verdict -eq 'PURSUE_URGENT'})
$pursue = @($briefs | Where-Object {$_.Verdict -in @('PURSUE','PURSUE_URGENT')})
$monitor = $briefs | Where-Object {$_.Verdict -eq 'MONITOR'}
$dead = $briefs | Where-Object {$_.Verdict -eq 'DEAD'}
$discover = $briefs | Where-Object {$_.Verdict -eq 'DISCOVER'}

$word = New-Object -ComObject Word.Application
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

H1 'KOR Structural — BC + AB Post-Secondary BD Report'
Italic 'Post-secondary capital pipeline (universities, colleges, polytechnics) in BC and Alberta. Compiled 2026-06-09 from Sonnet first-pass + honing verification on 182 MPIs. Mass timber academic specialist match (UBC Brock Commons / Earth Sciences model) flagged as KOR sweet spot. BMZ legacy reference baked into every brief.'

H2 'Executive Summary'
B 'Post-secondary projects honed: ' ($briefs.Count.ToString())
B 'PURSUE_URGENT — IMMEDIATE: ' ($urgent.Count.ToString())
B 'PURSUE — open opportunities: ' ($pursue.Count.ToString())
B 'MONITOR — pipeline open: ' ($monitor.Count.ToString())
B 'DISCOVER — pre-procurement: ' ($discover.Count.ToString())
B 'DEAD — fully awarded or out-of-scope: ' ($dead.Count.ToString())

P 'Post-secondary is RELATIONSHIP-driven — universities + colleges procure on 5-year capital plans with pre-qualified consultant lists. Sonnet flagged 5 immediate actions including direct architect contact paths.'

H2 'Top 5 named urgent actions from Sonnet'
P '1. **UBC Lower Mall Student Housing (MPI 6880)** — contact Ryder Architecture immediately re: structural engineer selection.'
P '2. **KPU Surrey Student Housing (MPI 6897)** — $144M, SE RFP may be live NOW.'
P '3. **U of A LIFT Centre (MPI 6979)** — $218.7M, SE RFP expected 2026.'
P '4. **NorQuest College CSC (MPI 7062)** — $240-250M, no incumbent, open competition.'
P '5. **UCalgary MDSH (MPI 3838)** — $450M, initiate relationship 18 months before RFP.'

function Safe { param($v, $max) if (-not $v) { return '' }; $s = [string]$v; if ($s.Length -le $max) { return $s }; return $s.Substring(0, $max) }

if ($urgent.Count -gt 0) {
    H2 ("URGENT — PURSUE_URGENT verdicts from honing ($($urgent.Count))")
    foreach ($p in $urgent) {
        B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
        P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
        P (Safe $p.korAngle 500)
        if ($p.status) { Italic ("Status: " + (Safe $p.status 400)) }
        P ''
    }
}

H2 '1. PURSUE — Open opportunities'
foreach ($p in $pursue) {
    B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
    P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
    P (Safe $p.korAngle 500)
    if ($p.status) { Italic ("Status: " + (Safe $p.status 400)) }
    P ''
}

H2 '2. MONITOR — Future opportunities'
foreach ($p in $monitor) {
    B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
    P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
    P (Safe $p.korAngle 400)
    if ($p.status) { Italic ("Status: " + (Safe $p.status 400)) }
    P ''
}

if ($discover.Count -gt 0) {
    H2 '3. DISCOVER — Pre-procurement relationship-build'
    foreach ($p in $discover) {
        B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
        P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
        P (Safe $p.korAngle 400)
        P ''
    }
}

H2 '4. DEAD — Awarded / delivered / out-of-scope'
P "$($dead.Count) projects honed as DEAD. Below for reference only."
MakeTable @('Id','Project','Proponent','Province') @(
    @(($dead | ForEach-Object {
        @($_.Id, $_.Name.Substring(0, [Math]::Min(60, $_.Name.Length)),
          $_.Proponent.Substring(0, [Math]::Min(35, $_.Proponent.Length)), $_.Province)
    }))
)

H2 '5. Strategic Synthesis'

H3 'Post-secondary market is procurement-mature'
P 'Of 182 post-secondary projects honed, 169 are DEAD or DUPLICATE. The post-secondary market in BC + AB is procurement-mature; pursuits are concentrated in 10-13 active or upcoming RFPs.'

H3 'Student housing is the dominant sub-segment'
P 'UBC Lower Mall, KPU Surrey, UVic 510-bed (delayed 2034 per m113), UBC Brock Commons follow-ons — student housing P3 / DBFM is the dominant procurement sub-pattern. Mass timber + concrete hybrid (Brock Commons model) = direct KOR specialty.'

H3 'Research facility specialty is structural-eng intensive'
P 'U of A LIFT Centre, NorQuest CSC, UCalgary MDSH — research facilities have vibration-sensitive structural requirements. KOR competitive zone if mass timber academic precedent (UBC Earth Sciences) translates.'

H3 'BMZ legacy + institution relationships compound'
P 'KOR was Bryson Markulin Zickmantel (BMZ) before 2021. Audit KOR portfolio for prior BMZ-era institution work — UBC, SFU, UVic, BCIT all likely have BMZ-era references that pre-date the rebrand. Surface those for warm-intro paths.'

H2 '6. Recommended next actions'
P '1. **CALL Ryder Architecture re: UBC Lower Mall Student Housing 6880** — Sonnet flagged this as priority 1.'
P '2. **MONITOR BC Bid for KPU Surrey 6897 SE RFP** — may be live now.'
P '3. **Build U of A relationship for LIFT Centre 6979** — SE RFP expected 2026.'
P '4. **NorQuest College CSC 7062** — open competition, no incumbent, KOR has fair chance.'
P '5. **UCalgary MDSH 3838 long-game** — $450M project, relationship-build 18 months out.'

$outPath = 'C:\Users\ilalonde\Desktop\KOR-PostSecondary-BD-Report.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close(); $word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
