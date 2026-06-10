$ErrorActionPreference = 'Stop'
$archs = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.architect-freq.json' -Raw | ConvertFrom-Json
if (-not $archs -or $archs.Count -eq 0) { throw "No architect data in .architect-freq.json" }

$cs = $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB
Add-Type -AssemblyName System.Data
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()

# T-SQL LIKE treats % _ [ as metacharacters — escape them so an architect name
# binds as a literal (BD-Audit-2026-06-09 Minor 10).
function EscapeSqlLike { param($s) (([string]$s) -replace '\[','[[]') -replace '%','[%]' -replace '_','[_]' }

# Per-architect, pull top projects they're linked to (via Intel signals or affiliations)
function GetArchProjects { param($archName)
    $cmd = $conn.CreateCommand(); $cmd.CommandTimeout = 30
    # Look up the first word of arch name to search briefs (avoid full-firm-name precision issues).
    # BD-Audit-2026-06-09 Minor 10: $searchTerm was computed then unused (the full
    # firm name was bound instead) — the first-word search is what's intended here.
    $searchTerm = ($archName -split '[\s+&]+')[0]
    if ($searchTerm.Length -lt 3) { return @() }
    $cmd.CommandText = @"
SELECT TOP 8 m.Id, m.ProjectName, m.Province,
    COALESCE(m.EstimatedCostText, CAST(m.EstimatedCostCad AS NVARCHAR(64))) AS Cost
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id
    AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND e.ResultJson LIKE @pattern
ORDER BY CASE WHEN m.EstimatedCostCad IS NULL THEN 1 ELSE 0 END, m.EstimatedCostCad DESC;
"@
    $cmd.Parameters.Add('@pattern', [System.Data.SqlDbType]::NVarChar, 256).Value = "%$(EscapeSqlLike $searchTerm)%"
    $r = $cmd.ExecuteReader()
    $projs = @()
    while ($r.Read()) {
        $cost = if ($r.IsDBNull(3)) { 'TBD' } else { $r.GetValue(3) }
        $projs += "$($r.GetValue(0)): $($r.GetValue(1)) ($($r.GetValue(2))) — $cost"
    }
    $r.Close()
    return $projs
}

# Prefetch the top-10 drill-down project lists and release the SQL connection
# BEFORE any Word COM work starts (BD-Audit-2026-06-09 M12).
$archProjectsByName = @{}
foreach ($a in $archs[0..9]) {
    if ($a) { $archProjectsByName[$a.Architect] = @(GetArchProjects $a.Architect) }
}
$conn.Close()

# KOR memory client architects
$korArchitects = @(
    'Chris Dikeakos', 'CDA Architects', 'Ciccozzi', 'Bing Thom', 'Revery',
    'IBI', 'Arcadis', 'Henriquez', 'MCMP', 'Acton Ostry', 'GBL', 'Perkins',
    'Yamamoto', 'RH Architecture', 'HCMA', 'KMBR', 'Iredale', 'NSDA',
    'GGA-Architecture', 'Gibbs Gage', 'GEC', 'S2 Architecture', 'Workun Garrick',
    'DIALOG', 'Stantec', 'Kasian'
)
function IsKorAligned { param($n)
    foreach ($k in $korArchitects) { if ($n -match $k) { return $true } }
    return $false
}

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
    function MakeTable { param($headers, $rows)
        $c = $headers.Count; $r = $rows.Count + 1
        $rng = $sel.Range; $tbl = $doc.Tables.Add($rng, $r, $c); $tbl.Style = "Table Grid"; $tbl.Range.Font.Size = 9
        for ($i = 0; $i -lt $c; $i++) { $tbl.Cell(1, $i+1).Range.Text = $headers[$i]; $tbl.Cell(1, $i+1).Range.Bold = $true }
        for ($r2 = 0; $r2 -lt $rows.Count; $r2++) {
            for ($c2 = 0; $c2 -lt $c; $c2++) { $tbl.Cell($r2+2, $c2+1).Range.Text = [string]$rows[$r2][$c2] }
        }
        $word.Selection.EndKey(6) | Out-Null; $sel.TypeParagraph()
    }

    H1 'KOR Structural — Architect Frequency Report (Warm-Intro Priority List)'
    Italic 'Architects ranked by total Intel references across KOR''s BD pipeline. The premise: in public AEC work, the prime consultant (typically architect) submits ONE team proposal with structural sub included. One architect relationship = N projects. This report identifies the highest-leverage architect relationships to build, based on actual project intelligence captured in KorOpportunitiesDb.'

    H2 'Top 20 highest-leverage architects'

    $top20 = $archs[0..19]
    $rows = @()
    foreach ($a in $top20) {
        $aligned = if (IsKorAligned $a.Architect) { 'KOR-aligned' } else { 'New target' }
        $rows += ,@($a.Architect, $a.Signals, $a.People, $a.Actions, $a.TotalRefs, $aligned)
    }
    MakeTable @('Architect','Signals','People','Actions','Total Refs','KOR Status') $rows

    P 'Higher Total Refs = more projects + more contacts + more action items in KOR''s pipeline. Architects appearing 50+ times represent significant compounding opportunities. KOR-aligned status indicates the architect appears in KOR''s established relationship list (per memory).'

    H2 'Top 10 architects — drill-down with projects'

    foreach ($a in $archs[0..9]) {
        H3 ("$($a.Architect)")
        B 'Total Intel refs: ' "$($a.TotalRefs) (Signals: $($a.Signals) | People: $($a.People) | Actions: $($a.Actions))"
        $aligned = if (IsKorAligned $a.Architect) { 'KOR-aligned (per memory client list)' } else { 'NEW target — no prior memory reference' }
        B 'KOR relationship: ' $aligned
        P 'Top projects in pipeline:'
        $projs = $archProjectsByName[$a.Architect]
        if ($projs.Count -gt 0) {
            foreach ($p in $projs) { P "  - $p" }
        } else { P "  (Projects linked via Intel signals — query for full list in app)" }
        P ''
    }

    H2 'KOR-aligned architects (warm-intro lever)'

    $korAligned = $archs | Where-Object { IsKorAligned $_.Architect } | Select-Object -First 25
    P "$($korAligned.Count) KOR-aligned architects (top 25 by Total Refs) are already in KOR's memory client list. These are immediate warm-intro candidates — KOR likely has a Principal-in-Charge relationship to leverage."

    $rows = @()
    foreach ($a in $korAligned) {
        $rows += ,@($a.Architect, $a.TotalRefs, "BMZ-legacy check + Principal-in-Charge outreach")
    }
    if ($rows.Count -gt 0) { MakeTable @('Architect','Total Refs','Approach') $rows }

    H2 'NEW high-leverage architects (cold-outreach campaign)'

    $newTargets = $archs | Where-Object { -not (IsKorAligned $_.Architect) } | Select-Object -First 15
    P "$($newTargets.Count) non-aligned architects (top 15 by Total Refs) do NOT appear in KOR's memory client list. These represent NEW relationship-building targets. Combined Total Refs across these architects = $((($newTargets | Measure-Object TotalRefs -Sum).Sum)) Intel signals/actions — compounding pursuit potential."

    $rows = @()
    foreach ($a in $newTargets) {
        $rows += ,@($a.Architect, $a.TotalRefs, 'Cold-outreach via LinkedIn / industry events / referrals')
    }
    if ($rows.Count -gt 0) { MakeTable @('Architect','Total Refs','Approach') $rows }

    H2 'Strategic Synthesis'

    H3 'Architect frequency is a KOR sub-strategy lever'
    P 'In public AEC procurement, KOR wins as the structural sub on the architect''s team. The architect submits one team proposal, the team is scored on collective expertise. Higher KOR P.Eng count + similar-project experience = higher team score = more wins.'

    H3 'Top-5 single relationships unlocking compounding pipeline'
    P 'Based on Total Refs ranking, the 5 highest-leverage architect relationships to BUILD (or DEEPEN if KOR-aligned) are:'
    for ($i = 0; $i -lt [Math]::Min(5, $archs.Count); $i++) {
        P "  $($i+1). $($archs[$i].Architect) — $($archs[$i].TotalRefs) Total Refs"
    }

    H3 'Workflow recommendation'
    P 'For each architect in top 10:'
    P '  1. Audit KOR''s portfolio for prior projects with this architect (BMZ legacy included).'
    P '  2. Identify Principal-in-Charge — search firm''s leadership team for the one most aligned with KOR''s project sectors.'
    P '  3. Direct outreach via LinkedIn + email. Reference shared past projects (if any) or specific upcoming pursuit.'
    P '  4. Industry event presence — UDI Awards, NAIOP BC, AIBC events, BC Recreation and Parks Association, BC Construction Association events. Architects are accessible at these venues.'

    H2 'Next steps'
    P '1. **Build relationships with top 5 architects** — concrete monthly outreach plan per architect.'
    P '2. **For NEW architects (non-KOR-aligned)** — pursue an introduction-via-shared-past-project angle. Most architects in BC have crossed paths with BMZ pre-2021.'
    P '3. **Wait for the bc-ab-primes drain to complete** (currently running) — surfaces architects on 150 currently-unidentified projects.'
    P '4. **Integrate prime consultant identification into the WPF in-app build** (per docs/BD-UI-Plan-2026-06-08.md) — every project should surface its architect as a primary relationship lever in the Org Dossier view.'

    $outPath = 'C:\Users\ilalonde\Desktop\KOR-Architect-Frequency-Report.docx'
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
