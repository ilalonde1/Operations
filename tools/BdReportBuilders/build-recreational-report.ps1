$ErrorActionPreference = 'Stop'
$briefs = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.recreational-final.json' -Raw | ConvertFrom-Json
if (-not $briefs -or $briefs.Count -eq 0) {
    throw "No briefs in .recreational-final.json. Re-run the recreational pull query."
}
$urgent = $briefs | Where-Object {$_.Verdict -eq 'PURSUE_URGENT'}
$pursue = $briefs | Where-Object {$_.Verdict -eq 'PURSUE'}
$monitor = $briefs | Where-Object {$_.Verdict -eq 'MONITOR'}
$dead = $briefs | Where-Object {$_.Verdict -eq 'DEAD'}
$discover = $briefs | Where-Object {$_.Verdict -eq 'DISCOVER'}
$duplicate = $briefs | Where-Object {$_.Verdict -eq 'DUPLICATE'}

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

H1 'KOR Structural — BC + AB Recreational BD Report'
Italic 'Recreational facility construction pipeline (aquatic centres, ice arenas, field houses, community centres, multipurpose recreation) across BC and Alberta. Compiled 2026-06-09 from Sonnet first-pass + honing verification on 168 MPIs. Long-span specialty (aquatic + arena + field house) is direct KOR structural sweet spot. HCMA Architecture + Design is the dominant BC rec architect — warm-intro channel.'

H2 'Executive Summary'
B 'Recreational projects honed: ' ($briefs.Count.ToString())
B 'PURSUE_URGENT — IMMEDIATE: ' ($urgent.Count.ToString())
B 'PURSUE — open opportunities: ' ($pursue.Count.ToString())
B 'MONITOR — pipeline open: ' ($monitor.Count.ToString())
B 'DISCOVER — pre-procurement: ' ($discover.Count.ToString())
B 'DEAD — fully awarded: ' ($dead.Count.ToString())
B 'DUPLICATE — flagged for MPI consolidation: ' ($duplicate.Count.ToString())

P 'Recreational facilities are KOR''s structural sweet spot. Long-span roof structures (aquatic + arena + field house) + complex MEP coordination + corrosive environments = direct KOR specialty. HCMA Architecture + Design dominates BC rec specialist market — warm-intro path.'

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

if ($discover.Count -gt 0) {
    H2 'DISCOVER — Pre-procurement relationship-build'
    foreach ($p in $discover) {
        B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
        P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
        P (Safe $p.korAngle 400)
        P ''
    }
}

H2 'MONITOR — Municipality pipeline open'
MakeTable @('Id','Project','Proponent','Province','Cost','Why MONITOR') @(
    @(($monitor | ForEach-Object {
        @($_.Id, (Safe $_.Name 50), (Safe $_.Proponent 30), $_.Province, $_.Cost, (Safe $_.korAngle 100))
    }))
)

if ($duplicate.Count -gt 0) {
    H2 'DUPLICATE — Flagged for MPI consolidation'
    P 'Sonnet identified these as same-project duplicates. Worth m114 consolidation migration.'
    MakeTable @('Id','Project','Proponent','Why DUPLICATE') @(
        @(($duplicate | ForEach-Object {
            @($_.Id, (Safe $_.Name 60), (Safe $_.Proponent 30), (Safe $_.korAngle 120))
        }))
    )
}

H2 'DEAD — Fully awarded or delivered'
MakeTable @('Id','Project','Province') @(
    @(($dead | ForEach-Object {
        @($_.Id, (Safe $_.Name 70), $_.Province)
    }))
)

H2 'Strategic Synthesis'

H3 'Long-span specialty = KOR direct fit'
P 'Aquatic centres (long-span roof + MEP + corrosive environment), ice arenas (long-span + low-temp condensation), and field houses (clear-span) are all direct KOR structural specialty. Any of the PURSUE/MONITOR projects in these typologies should rank top of pursuit list.'

H3 'HCMA dominates BC rec — single relationship = compounding'
P 'HCMA Architecture + Design is BC''s dominant recreational specialist architect. One sustained HCMA relationship = recurring KOR positioning across most BC rec pipeline. Newton Community Centre, Britannia, Cameron — HCMA is on most major BC rec projects.'

H3 'Newton Community Centre quadruplets — m114 candidate'
P 'Newton Community Centre $310.6M (largest civic project in City of Surrey history) appears 4 times across MPIs 4221, 4589, 5288, 6782. Britannia Community Centre $300M appears 2 times (4196, 6483). Worth m114 consolidation similar to UHNBC + Vernon Jubilee. Sonnet may have flagged additional duplicates in the DUPLICATE bucket above.'

H3 'Municipality relationships compound'
P 'Recreational procurement is municipality-driven. Operators with multiple in-pipeline rec projects (Surrey, Vancouver, Burnaby, Coquitlam, Calgary, Edmonton, Saskatoon) are KOR''s highest-leverage relationship targets. Same pattern as BC Housing operators — operator-level relationship rather than project-level chase.'

H3 'Referendum-funded BC projects need political timing'
P 'Many BC recreational projects are referendum-funded. Approval triggers RFP. Watch BC municipal election cycles (Oct 2025) for new project pipelines triggered by approved referenda.'

H2 'Recommended next actions'
P '1. **HCMA Vancouver office** — single warm-intro to HCMA Principal positions KOR for recurring BC rec pursuits. Identify Principal-in-Charge on Newton + Britannia and start there.'
P '2. **Newton Community Centre Surrey $310.6M** — largest civic project in Surrey history. If KOR has Surrey municipal relationship, this is the showcase pursuit.'
P '3. **PURSUE list (14 projects)** — review each for procurement model + incumbent before outreach.'
P '4. **m114 migration** — consolidate Newton 4x + Britannia 2x duplicates before next drain cycle (same UHNBC + Vernon Jubilee pattern).'

$outPath = 'C:\Users\ilalonde\Desktop\KOR-Recreational-BD-Report.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close(); $word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
