$ErrorActionPreference = 'Stop'
$briefs = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.indigenous-final.json' -Raw | ConvertFrom-Json
if (-not $briefs -or $briefs.Count -eq 0) {
    throw "No briefs in .indigenous-final.json. Re-run the indigenous pull query."
}
# Filter to actually indigenous projects (drop defense MPI overlap)
$indigenous = $briefs | Where-Object { $_.Id -lt 7161 -or $_.Id -gt 7166 }
$pursueOnly = $indigenous | Where-Object {$_.Verdict -eq 'PURSUE'}
$monitorOnly = $indigenous | Where-Object {$_.Verdict -eq 'MONITOR'}
$deadOnly = $indigenous | Where-Object {$_.Verdict -eq 'DEAD'}
$discoverOnly = $indigenous | Where-Object {$_.Verdict -eq 'DISCOVER'}

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc = $word.Documents.Add()
$sel = $word.Selection

$styles = $doc.Styles
$styles.Item('Heading 1').Font.Size = 16
$styles.Item('Heading 1').Font.Bold = $true
$styles.Item('Heading 1').ParagraphFormat.SpaceBefore = 0
$styles.Item('Heading 1').ParagraphFormat.SpaceAfter = 6
$styles.Item('Heading 2').Font.Size = 12
$styles.Item('Heading 2').Font.Bold = $true
$styles.Item('Heading 2').ParagraphFormat.SpaceBefore = 10
$styles.Item('Heading 2').ParagraphFormat.SpaceAfter = 3
$styles.Item('Heading 3').Font.Size = 10.5
$styles.Item('Heading 3').Font.Bold = $true
$styles.Item('Heading 3').ParagraphFormat.SpaceBefore = 6
$styles.Item('Heading 3').ParagraphFormat.SpaceAfter = 2
$styles.Item('Normal').Font.Size = 10
$styles.Item('Normal').ParagraphFormat.SpaceAfter = 4

$doc.PageSetup.TopMargin = 36
$doc.PageSetup.BottomMargin = 36
$doc.PageSetup.LeftMargin = 54
$doc.PageSetup.RightMargin = 54

function H1 { param($t) $sel.Style = $doc.Styles.Item('Heading 1'); $sel.TypeText($t); $sel.TypeParagraph() }
function H2 { param($t) $sel.Style = $doc.Styles.Item('Heading 2'); $sel.TypeText($t); $sel.TypeParagraph() }
function H3 { param($t) $sel.Style = $doc.Styles.Item('Heading 3'); $sel.TypeText($t); $sel.TypeParagraph() }
function P  { param($t) $sel.Style = $doc.Styles.Item('Normal');    $sel.TypeText($t); $sel.TypeParagraph() }
function B  { param($lbl, $val)
    $sel.Style = $doc.Styles.Item('Normal')
    $sel.Font.Bold = 1; $sel.TypeText($lbl); $sel.Font.Bold = 0
    $sel.TypeText($val); $sel.TypeParagraph()
}
function Italic { param($t)
    $sel.Style = $doc.Styles.Item('Normal'); $sel.Font.Italic = 1; $sel.Font.Size = 9
    $sel.TypeText($t); $sel.Font.Italic = 0; $sel.Font.Size = 10; $sel.TypeParagraph()
}
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
    $rng = $sel.Range
    $tbl = $doc.Tables.Add($rng, $r, $c)
    $tbl.Style = "Table Grid"
    $tbl.Range.Font.Size = 9
    for ($i = 0; $i -lt $c; $i++) {
        $tbl.Cell(1, $i+1).Range.Text = $headers[$i]
        $tbl.Cell(1, $i+1).Range.Bold = $true
    }
    for ($r2 = 0; $r2 -lt $rows.Count; $r2++) {
        for ($c2 = 0; $c2 -lt $c; $c2++) {
            $tbl.Cell($r2+2, $c2+1).Range.Text = [string]$rows[$r2][$c2]
        }
    }
    $word.Selection.EndKey(6) | Out-Null
    $sel.TypeParagraph()
}

H1 'KOR Structural — Indigenous Projects BD Report'
Italic 'BC + Alberta First Nations / Métis / Indigenous construction pipeline. Compiled 2026-06-08 from Sonnet first-pass + honing verification. Verdicts: PURSUE / MONITOR / DEAD / DISCOVER. Indigenous procurement is relationship-driven, not open-bid — warm-introduction paths and 1-3 year relationship cadence acknowledged in every recommendation. Cultural-competency and protocol-respect baked into the honing PROMPT.'

H2 'Executive Summary'
B ('Total projects honed: ') ($indigenous.Count.ToString())
B ('PURSUE — open opportunities: ') ($pursueOnly.Count.ToString())
B ('MONITOR — locked but Nation relationship pursuit-positions future projects: ') ($monitorOnly.Count.ToString())
B ('DEAD — awarded or genuinely unviable: ') ($deadOnly.Count.ToString())
B ('DISCOVER — pre-procurement build-relationship-now: ') ($discoverOnly.Count.ToString())

P 'Indigenous BD reality: most projects are NOT competitive open-RFP. They are relationship-led procurement where Nation chief and council, development corporations, or non-profit operators select trusted partners. KOR''s pursuit play is a 1-3 year relationship cadence. The PURSUE projects below have either explicit RFP windows OR specific named warm-introduction paths surfaced by the honing pass.'

H2 'Critical context — protocol respect'
P 'EVERY Indigenous outreach in this report must go through a named warm-introduction path. Cold-calling Chief and Council or Development Corp leadership is inappropriate and burns relationship capital. The honing pass identified named warm-intro paths for most PURSUE projects (past architects, prior consultants, Nation-relationship holders, industry events).'
P 'Where the warm-intro path is not yet identified, the brief explicitly notes "GAP — recommended internal action: identify warm-intro path before outreach". Do not cold-call.'

H2 '1. PURSUE — Open opportunities (25 projects)'

P 'Each project flagged as PURSUE has either an active procurement window OR a specific named warm-intro path identified by Sonnet. Below is the curated top-25 organized by Nation/sponsor type.'

H3 '1.1 Squamish Nation projects'
$squamish = $pursueOnly | Where-Object { $_.Name -match 'Squamish|Sen.{1,3}qw|Jericho|Ji.{1,3}qw|Build 600' }
if ($squamish.Count -gt 0) {
    foreach ($p in $squamish) {
        B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
        P ($p.korAngle.Substring(0, [Math]::Min(500, $p.korAngle.Length)))
        Italic ("Status: " + $p.status)
        P ''
    }
} else { P 'None in PURSUE category — see Sen̓áḵw entries under DEAD / MONITOR.' }

H3 '1.2 Musqueam / Tsleil-Waututh / Lower Mainland Nation projects'
$lowerMainland = $pursueOnly | Where-Object { $_.Name -match 'Musqueam|Tsleil-Waututh|statl.{1,3}w|Maplewood|Jericho|Langara YMCA|leləm|le.{1,3}m|Jericho' }
foreach ($p in $lowerMainland) {
    B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
    P ($p.korAngle.Substring(0, [Math]::Min(500, $p.korAngle.Length)))
    Italic ("Status: " + $p.status)
    P ''
}

H3 '1.3 Vancouver Island Nation projects'
$island = $pursueOnly | Where-Object { $_.Name -match 'Tla.{1,3}amin|APD Lands|Port Alberni|te.{1,3}tuxwtun|Camp Nanaimo|Spintlum|Cranberry|OMAHS' }
foreach ($p in $island) {
    B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
    P ($p.korAngle.Substring(0, [Math]::Min(500, $p.korAngle.Length)))
    Italic ("Status: " + $p.status)
    P ''
}

H3 '1.4 Interior + Northern BC Nation projects'
$interior = $pursueOnly | Where-Object { $_.Name -match 'Outma|Naqsmist|Coyote Rock|Northside|Seymour Creek|Willingdon|Farwell|Uchucklesaht|Redford|Mamawatoskewin' }
foreach ($p in $interior) {
    B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
    P ($p.korAngle.Substring(0, [Math]::Min(500, $p.korAngle.Length)))
    Italic ("Status: " + $p.status)
    P ''
}

H3 '1.5 Alberta Nation projects'
$ab = $pursueOnly | Where-Object { $_.Province -eq 'AB' }
foreach ($p in $ab) {
    B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
    P ($p.korAngle.Substring(0, [Math]::Min(500, $p.korAngle.Length)))
    Italic ("Status: " + $p.status)
    P ''
}

H2 '2. MONITOR — Relationship-building targets (24 projects)'

P 'Current project scope locked or pre-RFP, but Nation relationship has additional projects in pipeline. Sustained engagement with the Nation positions KOR for future pursuits.'

MakeTable @('Id','Project','Nation / Sponsor','Province','Why MONITOR') @(
    @(($monitorOnly | ForEach-Object {
        @($_.Id, $_.Name.Substring(0, [Math]::Min(50, $_.Name.Length)),
          ($_.Proponent.Substring(0, [Math]::Min(40, $_.Proponent.Length))),
          $_.Province,
          ($_.korAngle.Substring(0, [Math]::Min(80, $_.korAngle.Length))))
    }))
)

H2 '3. DEAD — Awarded or unviable (14 projects)'

P 'Projects with confirmed structural engineer award OR genuinely unviable pursuits (receivership, cancelled, etc.). Named for record-keeping; not for active pursuit. Some Nations on DEAD projects may have OTHER projects in MONITOR — relationship is still worth building.'

MakeTable @('Id','Project','Why DEAD','Province') @(
    @(($deadOnly | ForEach-Object {
        @($_.Id,
          $_.Name.Substring(0, [Math]::Min(55, $_.Name.Length)),
          ($_.korAngle.Substring(0, [Math]::Min(120, $_.korAngle.Length))),
          $_.Province)
    }))
)

H2 '4. DISCOVER — Pre-procurement watch (3 projects)'

P 'Pre-procurement projects where KOR should build the Nation or Development Corp relationship NOW to position for future RFP cycles. Not yet active opportunities.'

foreach ($p in $discoverOnly) {
    B ("Id $($p.Id): ") "$($p.Name) ($($p.Province))"
    P ($p.korAngle.Substring(0, [Math]::Min(400, $p.korAngle.Length)))
    Italic ("Status: " + $p.status)
    P ''
}

H2 '5. Strategic Synthesis'

P 'Three patterns emerge from the 60 honed Indigenous projects:'

H3 '5.1 Lower Mainland Nation development corp dominance'
P 'Squamish Nation, Musqueam, Tsleil-Waututh control major Lower Mainland development pipelines (Senakw, Jericho Lands, Langara YMCA, Maplewood / statləw̓ District). These are billion-dollar multi-tower phased developments where structural-eng selection happens early in each phase. KOR''s Lower Mainland concrete tower expertise is direct fit. Build relationships with the Development Corps directly: Senakw Development Corp (Squamish), MST Development Corporation (Musqueam-Squamish-Tsleil-Waututh).'

H3 '5.2 Vancouver Island Nation housing wave'
P 'Tla''amin, Snuneymuxw, Tseshaht, Uchucklesaht, K''ómoks all have multi-unit affordable housing programs with BC Housing or CMHC co-funding. Smaller per-project value but RECURRING. Nation-level relationships compound across multiple project phases.'

H3 '5.3 Indigenous-owned structural firm requirement caution'
P 'A growing number of Indigenous procurements require Indigenous-owned firms on the prime team. KOR is NOT Indigenous-owned. The play is: (1) sub-consultant to an Indigenous-led prime architect (Two Row Architect, Smoke Architecture, Brook McIlroy), (2) sub-consultant to a non-Indigenous prime with strong Indigenous track record (Stantec, DIALOG, IBI/Arcadis), or (3) joint-venture partnership with an Indigenous engineering firm for specific pursuits. Identify which projects allow which strategy.'

H2 '6. Recommended next actions'

P '1. **Squamish Nation relationship play** — Build 600 Affordable Homes Action Plan (Id 4873) + Sen̓áḵw Phases 3-4 (Id 6910 MONITOR). One named warm-intro target via Senakw Development Corp or via past architect.'

P '2. **Musqueam-Tsleil-Waututh development corp** — Jericho Lands Phase 1 (Id 6911) + Maplewood Innovation District (Id 4882) + statləw̓ District (Id 6913). MST Development Corp is the gate. KOR should target this single development corp relationship for compounding returns across Phase 1-N.'

P '3. **VI housing wave** — APD Lands Port Alberni (Id 4846), te''tuxwtun Camp Nanaimo (Id 4843), Redford Uchucklesaht (Id 5821). Smaller pursuits each but cumulative. Identify a Nation-relationship consultant or past-architect channel.'

P '4. **AB Alexander First Nation Mamawatoskewin** (Id 6999) — single PURSUE in AB. Direct opportunity if warm-intro path identified.'

H2 '7. Notes on Indigenous BD methodology'
P 'Indigenous procurement is a 1-3 year relationship play, not a 90-day pursuit. The honing PROMPT explicitly rejected cold-calling as a strategy. Every PURSUE action recommendation includes a named warm-introduction path OR an explicit "GAP — identify warm-intro path before outreach" statement.'
P 'Industry events to attend for relationship-building: National Aboriginal Day of Engineering, CCAB (Canadian Council for Aboriginal Business) conferences, AFOA Canada (Aboriginal Financial Officers Association) events. Many Nation chief executives and Development Corp leaders attend these — networking entry rather than cold outreach.'

# === SAVE ===
$outPath = 'C:\Users\ilalonde\Desktop\KOR-Indigenous-BD-Report.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close()
$word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
