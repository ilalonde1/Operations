$ErrorActionPreference = 'Stop'
$all = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.all-honed.json' -Raw | ConvertFrom-Json
$briefs = $all | Where-Object { $_.Category -eq 'BC Housing' }
if (-not $briefs -or $briefs.Count -eq 0) {
    throw "No 'BC Housing' briefs in .all-honed.json (only categories present: $(($all | Group-Object Category | ForEach-Object { $_.Name }) -join ', ')). Re-run the cross-category pull query to refresh .all-honed.json before regenerating this report."
}
$urgent = $briefs | Where-Object {$_.Verdict -eq 'PURSUE_URGENT' -or ($_.Verdict -eq 'PURSUE' -and $_.korAngle -match 'URGENT')}
$pursue = $briefs | Where-Object {$_.Verdict -eq 'PURSUE' -and $_.korAngle -notmatch 'URGENT'}
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

H1 'KOR Structural — BC Housing BD Report'
Italic ('BC Housing-funded projects: supportive housing, affordable housing, complex care, shelters, transitional housing. Compiled 2026-06-08 from Sonnet first-pass + honing verification. The honing PROMPT identified BC Housing partnership model (non-profit operator / municipality / for-profit developer / Indigenous operator / hybrid) for every project — that''s where the structural-eng decision actually happens.')

H2 'Executive Summary'
B ('BC Housing projects honed: ') ($briefs.Count.ToString())
B ('PURSUE_URGENT — IMMEDIATE action: ') ($urgent.Count.ToString())
B ('PURSUE — open opportunities: ') ($pursue.Count.ToString())
B ('MONITOR — operator has more pipeline: ') ($monitor.Count.ToString())
B ('DEAD — fully awarded or modular-bundled: ') ($dead.Count.ToString())
B ('DISCOVER — pre-procurement operator-relationship: ') ($discover.Count.ToString())

P 'BC Housing rarely builds directly — they partner with non-profit operators (Lookout, Atira, RainCity, Coast Mental Health, PHS, AHMA), municipalities, or for-profit developers. The structural-engineer decision usually happens at the operator level, not BC Housing itself. Pursuit play is operator-relationship building.'

if ($urgent.Count -gt 0) {
    H2 'URGENT — IMMEDIATE action required'
    foreach ($p in $urgent) {
        H3 ($p.Name)
        B ('Id: ') $p.Id.ToString()
        B ('Proponent: ') $p.Proponent
        B ('Cost: ') $p.Cost
        B ('Status: ') $p.status
        P ($p.korAngle.Substring(0, [Math]::Min(500, $p.korAngle.Length)))
        P ''
    }
}

H2 'PURSUE — Open opportunities'
foreach ($p in $pursue) {
    B ("Id $($p.Id): ") "$($p.Name)"
    P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
    P ($p.korAngle.Substring(0, [Math]::Min(400, $p.korAngle.Length)))
    Italic ('Status: ' + $p.status)
    P ''
}

H2 'MONITOR — Operator pipeline worth building (29 projects)'
P 'BC Housing operators procure projects in clusters. MONITOR projects represent operators with multiple in-pipeline opportunities. Identify operators appearing multiple times — those are KOR''s highest-leverage relationship targets.'
MakeTable @('Id','Project','Proponent','Cost','Why MONITOR') @(
    @(($monitor | ForEach-Object {
        @($_.Id, $_.Name.Substring(0, [Math]::Min(50, $_.Name.Length)),
          $_.Proponent.Substring(0, [Math]::Min(35, $_.Proponent.Length)),
          $_.Cost,
          $_.korAngle.Substring(0, [Math]::Min(120, $_.korAngle.Length)))
    }))
)

H2 'DEAD — Already awarded or modular-bundled (55 projects)'
P 'Most BC Housing projects have structural engineers locked. Modular procurements bundle structural with the modular supplier (Horizon North, Britco, NRB).'
MakeTable @('Id','Project','Proponent','Why DEAD') @(
    @(($dead | ForEach-Object {
        @($_.Id, $_.Name.Substring(0, [Math]::Min(50, $_.Name.Length)),
          $_.Proponent.Substring(0, [Math]::Min(30, $_.Proponent.Length)),
          $_.korAngle.Substring(0, [Math]::Min(130, $_.korAngle.Length)))
    }))
)

H2 'Strategic Synthesis'

H3 'BC Housing operator landscape — relationship targets'
P 'Non-profit operators driving BC Housing pipeline: Lookout Housing & Health Society, Atira Women''s Resource Society, RainCity Housing, Coast Mental Health, PHS Community Services Society, Pacific Coast Affordable Housing, BCNPHA members, AHMA (Aboriginal Housing Management Association), Lu''ma Native Housing, Vancouver Native Housing Society. Each operator has 5-15+ projects in 5-year pipeline. KOR''s play is operator-level relationship rather than project-level chase.'

H3 'Funding stream signals'
P 'BC Housing funding streams differ in cadence and partnership model:'
P 'BC Builds (new 2024 program) — fastest cadence, prefers established operators. Building BC umbrella. Supportive Housing Fund. Homes for BC. Community Housing Fund. Indigenous Housing Fund (AHMA-channel). Complex Care Housing initiative. CMHC RCFI co-funded (federal). Identifying funding stream per project tells KOR the operator + cadence.'

H3 'Modular bundle risk'
P 'BC Housing has a Modular Housing Initiative that bundles structural-engineer with modular supplier (Horizon North, Britco, NRB). Projects flagged as modular procurement are typically DEAD for KOR direct entry — but the modular suppliers themselves are KOR relationship targets if KOR wants to pursue modular structural.'

H3 'Mid-rise concrete = KOR sweet spot'
P 'BC Housing''s preferred typology for supportive + transitional housing is mid-rise concrete. KOR''s mid-rise concrete depth is the direct competitive fit. Stick-frame wood low-rises are less differentiated; mass timber hybrid is emerging (Equilibrium, Fast + Epp, ASPECT compete).'

H2 'Recommended next actions'
P '1. **Audit MONITOR list for repeat operators** — operators appearing 3+ times are KOR''s highest-leverage relationships. Likely candidates: Lookout, Atira, RainCity, Coast Mental Health.'
P '2. **PURSUE projects** — direct outreach to named operator + BC Housing project director per brief.'
P '3. **Build BCNPHA Annual Conference relationship attendance** — operators network here, BC Housing executives attend.'
P '4. **Indigenous Housing Fund channel via AHMA** — separate but adjacent to Indigenous Projects BD pipeline. Use the indigenous-projects honing learnings.'

# === SAVE ===
$outPath = 'C:\Users\ilalonde\Desktop\KOR-BC-Housing-BD-Report.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close()
$word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
