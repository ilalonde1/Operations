$ErrorActionPreference = 'Stop'
$briefs = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.commercial-final.json' -Raw | ConvertFrom-Json
if (-not $briefs -or $briefs.Count -eq 0) {
    throw "No commercial briefs in .commercial-final.json — re-run the commercial pull query."
}
# BD-Audit-2026-06-09 C8/Minor-1: $urgent was computed but never rendered —
# any PURSUE_URGENT brief was silently dropped from the report. Urgent items
# also stay in $pursue so the open-opportunities section keeps showing them.
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

H1 'KOR Structural — BC + AB Commercial Office BD Report'
Italic 'Commercial office tower pipeline in BC and Alberta. Compiled 2026-06-09 from Sonnet first-pass + honing verification on 126 MPIs. Mass timber emergence (TELUS Ocean model) flagged as KOR competitive zone. Vancouver downtown + Burnaby Metrotown + Surrey City Centre + Calgary CBD + Edmonton CBD coverage.'

H2 'Executive Summary'
B ('Commercial office projects honed: ') ($briefs.Count.ToString())
B ('PURSUE_URGENT — act this week: ') ($urgent.Count.ToString())
B ('PURSUE — open opportunities (incl. urgent): ') ($pursue.Count.ToString())
B ('MONITOR — locked but pipeline open: ') ($monitor.Count.ToString())
B ('DISCOVER — pre-anchor-tenant: ') ($discover.Count.ToString())
B ('DEAD — fully awarded / delivered: ') ($dead.Count.ToString())

P 'Commercial office market is heavily anchor-tenant-driven. Pre-lease secures viability, then RFP timing predictable. Most BC commercial office projects are developer-led with selected GC + pre-qualified subs.'

if ($urgent.Count -gt 0) {
    H2 ("URGENT — act this week ($($urgent.Count))")
    foreach ($p in $urgent) {
        H3 ("$($p.Id): $($p.Name)")
        B 'Province: ' $p.Province
        B 'Proponent: ' $p.Proponent
        B 'Cost: ' $p.Cost
        B 'Status: ' (Safe $p.status 400)
        P (Safe $p.korAngle 600)
        P ''
    }
}

H2 'PURSUE — Open opportunities'
foreach ($p in $pursue) {
    H3 ("$($p.Id): $($p.Name)")
    B 'Province: ' $p.Province
    B 'Proponent: ' $p.Proponent
    B 'Cost: ' $p.Cost
    B 'Status: ' (Safe $p.status 400)
    P (Safe $p.korAngle 600)
    P ''
}

H2 'MONITOR — Locked but pipeline open'
MakeTable @('Id','Project','Proponent','Province','Cost','Why MONITOR') @(
    @(($monitor | ForEach-Object {
        @($_.Id, (Safe $_.Name 50),
          (Safe $_.Proponent 30),
          $_.Province, $_.Cost,
          (Safe $_.korAngle 100))
    }))
)

H2 'DISCOVER — Pre-procurement relationship-build'
foreach ($p in $discover) {
    B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
    P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
    P ($p.korAngle.Substring(0, [Math]::Min(400, $p.korAngle.Length)))
    Italic ('Status: ' + $p.status)
    P ''
}

H2 'DEAD — Awarded / delivered / locked'
P "$($dead.Count) projects honed as fully awarded or delivered. Reference only."
MakeTable @('Id','Project','Province') @(
    @(($dead | ForEach-Object {
        @($_.Id, $_.Name.Substring(0, [Math]::Min(70, $_.Name.Length)), $_.Province)
    }))
)

H2 'Strategic Synthesis'

H3 'Commercial office market reality — 88% DEAD'
P 'Of 126 commercial office projects honed, 111 (88%) are DEAD — structural engineer already assigned, project delivered, or owner pre-locked. The commercial office market in BC + AB is procurement-mature; pursuits are concentrated in a small set of active RFPs.'

H3 'The 3 PURSUE — small but high-leverage'
P 'Waterfront Square Office Tower (BC), 11-15 East 4th Avenue Life Sciences (BC), Centre Block SFU/Provincial Office Tower Surrey. All require immediate engagement — DPB hearings complete or Development Permit in process. Structural selection window OPEN now.'

H3 'TELUS Ocean was the model — find the next mass-timber office anchor'
P 'TELUS Ocean Vancouver (EllisDon GC, mass timber + concrete hybrid) set the model for the next wave of BC commercial office. The 2 DISCOVER projects are likely future mass-timber office candidates. Track anchor-tenant pre-lease announcements at Microsoft / Shopify / Amazon Canada to identify the next opportunity.'

H3 'Strategic relationships > project pursuits'
P 'Given 88% DEAD, the commercial pursuit play is RELATIONSHIPS not projects. Build sustained relationships with the BC commercial office majors: Concert Properties, Westbank, Beedie Development, Anthem, Aquilini, Reliance. Most of these are already KorClients (per memory). The relationship investment compounds across multi-decade commercial pipelines.'

H2 'Recommended next actions'
P '1. **Waterfront Square Office Tower** — DPB hearing complete May 11, 2026. Verify approval status THIS WEEK. James Cheng Architects + Cadillac Fairview VP Development BC channel.'
P '2. **Oxford 11-15 East 4th Ave Life Sciences** — Post-rezoning, Development Permit in process. Oxford Properties + Chernoff Thompson Architects channel.'
P '3. **Centre Block SFU Surrey** — Note flagged as duplicate of 6890. Verify whether it is genuinely distinct or merge with 6890. Stantec Architecture + PCL Westcoast channel.'
P '4. **Audit MONITOR list for repeat developers** — Concert / Westbank / Beedie / Anthem / Aquilini appearing multiple times = strategic-relationship targets.'

# === SAVE ===
$outPath = 'C:\Users\ilalonde\Desktop\KOR-Commercial-BD-Report.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close()
$word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
