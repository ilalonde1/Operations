$ErrorActionPreference = 'Stop'
$bcRivals = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.bc-rivals.json' -Raw | ConvertFrom-Json
if (-not $bcRivals -or @($bcRivals).Count -eq 0) {
    throw "No BC/AB rival rows in .bc-rivals.json. Re-run the bc-rivals pull query before regenerating this report."
}
$compList = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.competitor-freq.json' -Raw | ConvertFrom-Json
if (-not $compList -or @($compList).Count -eq 0) {
    throw "No competitor rows in .competitor-freq.json. Re-run the competitor-freq pull query before regenerating this report."
}

$cs = $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB
Add-Type -AssemblyName System.Data
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()

# T-SQL LIKE treats % _ [ as metacharacters — escape them so a competitor name
# like "100% Structural [BC]" binds as a literal (BD-Audit-2026-06-09 Minor 10).
function EscapeSqlLike { param($s) (([string]$s) -replace '\[','[[]') -replace '%','[%]' -replace '_','[_]' }

function GetCompProjects { param($compName)
    $cmd = $conn.CreateCommand(); $cmd.CommandTimeout = 30
    $cmd.CommandText = @"
SELECT TOP 6 m.Id, m.ProjectName, m.Province,
    COALESCE(m.EstimatedCostText, CAST(m.EstimatedCostCad AS NVARCHAR(64))) AS Cost
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id
    AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND e.ResultJson LIKE @pat
ORDER BY CASE WHEN m.EstimatedCostCad IS NULL THEN 1 ELSE 0 END, m.EstimatedCostCad DESC;
"@
    $cmd.Parameters.Add('@pat', [System.Data.SqlDbType]::NVarChar, 256).Value = "%$(EscapeSqlLike $compName)%"
    $r = $cmd.ExecuteReader()
    $projs = @()
    while ($r.Read()) {
        $cost = if ($r.IsDBNull(3)) { 'TBD' } else { $r.GetValue(3) }
        $projs += "$($r.GetValue(0)): $($r.GetValue(1)) ($($r.GetValue(2))) — $cost"
    }
    $r.Close()
    return $projs
}

# Prefetch the top-5 drill-down project lists and release the SQL connection
# BEFORE any Word COM work starts (BD-Audit-2026-06-09 M12).
$top5 = @($bcRivals | Select-Object -First 5)
$top5Projects = @{}
foreach ($c in $top5) { $top5Projects[$c.Name] = @(GetCompProjects $c.Name) }
$conn.Close()

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
    $styles.Item('Heading 2').ParagraphFormat.SpaceBefore = 12; $styles.Item('Heading 2').ParagraphFormat.SpaceAfter = 3
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

    H1 'KOR Structural — Competitor Intelligence Report'
    Italic 'Structural engineering competitors mapped across KOR''s BD pipeline. Where they''re entrenched + which projects they hold = where to focus KOR''s differentiation. Sources: IntelSignal references + IntelPersonAffiliation + IntelAction across honed briefs.'

    H2 'BC + AB market — named rivals'
    P 'These are KOR''s known direct rivals in the BC + AB structural-engineering market. Tracking their footprint via Intel references gives KOR a flip-opportunity heatmap.'

    $rows = @()
    foreach ($r in $bcRivals) {
        $rows += ,@($r.Name, "$($r.Signals)", "$($r.People)", "$($r.Actions)", "$($r.Total)")
    }
    MakeTable @('BC/AB Rival','Signals','People','Actions','Total') $rows

    H2 'Top 5 BC/AB rivals — drill-down'

    foreach ($c in $top5) {
        H3 ($c.Name)
        B 'Footprint: ' "$($c.Total) Intel refs (Signals: $($c.Signals) | People: $($c.People) | Actions: $($c.Actions))"
        P 'Projects observed in:'
        $projs = $top5Projects[$c.Name]
        if ($projs.Count -gt 0) {
            foreach ($p in $projs) { P "  - $p" }
        } else { P "  (No keyword-match projects in honed briefs)" }
        P ''
    }

    H2 'US West Coast (LA / SD + emerging WA/OR) — Top 15 by Intel refs'
    P 'KOR''s US market footprint (LA + San Diego today, growth into WA + OR). These US competitors appear in KOR''s pipeline — relevant for US West Coast strategy.'

    $rows = @()
    foreach ($c in ($compList | Select-Object -First 15)) {
        $rows += ,@($c.Name, "$($c.Signals)", "$($c.People)", "$($c.Actions)", "$($c.Total)")
    }
    MakeTable @('US West Coast Rival','Signals','People','Actions','Total') $rows

    H2 'Strategic synthesis'

    H3 'Where rivals are entrenched (DEAD signals)'
    P 'Per Yurkovich-class error catch: projects where a competitor is named as incumbent are flagged DEAD in KOR''s honed briefs. Examples:'
    P '  - Richmond Hospital Yurkovich Pavilion = Entuitive incumbent'
    P '  - Cowichan Hospital = Bush, Bohlman & Partners (Alliance)'
    P '  - Cariboo Memorial Hospital + several others = Stantec (captive in-house)'
    P '  - CFB Cold Lake FFCP = Stantec under EllisDon design-build'

    H3 'BC concrete tower competitive zone'
    P 'Glotman Simpson + Fast + Epp + RJC + AME + ASPECT + Tarpley dominate BC high-rise concrete + mid-rise residential market. KOR competes directly here. Mass timber + concrete hybrid (TELUS Ocean model) is KOR''s emerging differentiator — Fast + Epp + ASPECT + Equilibrium + StructureCraft are the mass-timber specialists.'

    H3 'Healthcare market = Stantec + DIALOG captive'
    P 'BC + AB healthcare is heavily Stantec (captive in-house structural) + DIALOG (captive in-house structural). KOR flip-opportunities are limited to Alliance-mode projects where structural is procured separately, or D-B teams where Stantec/DIALOG aren''t the prime architect.'

    H3 'Long-span specialty — Fast + Epp dominant'
    P 'Fast + Epp is the dominant mass-timber long-span specialist in BC (Brock Commons, Earth Sciences, multiple aquatic + arena projects). StructureCraft also significant for engineered timber. KOR competes in this zone via mass-timber capability buildout.'

    H3 'US West Coast pattern recognition'
    P 'The US rival list reflects KOR''s LA + SD market presence. Top rivals (JMS Engineering, R+A Engineering, WHM Structural, Skyline Engineering, Degenkolb, AHBL, Miyamoto, Holmes Structures) indicate KOR''s competitive set in California + emerging WA/OR. Degenkolb + Miyamoto are seismic specialists (relevant for KOR''s seismic schools + California portfolio).'

    H2 'Recommended next actions'

    P '1. **Audit DEAD-marked projects** for competitor names — these are the lock signals to track.'
    P '2. **Monitor Glotman Simpson + Fast + Epp + RJC pipelines** — biggest BC direct rivals. Any pipeline news = competitive intelligence.'
    P '3. **Differentiation strategy** — Mass timber + concrete hybrid + long-span specialty = KOR''s positioning vs Fast + Epp / ASPECT / Equilibrium.'
    P '4. **Healthcare entry strategy** — given Stantec + DIALOG captive lock, focus on Alliance/non-Stantec D-B teams + project-specific Alliance reformations.'
    P '5. **US West Coast** — track Degenkolb (seismic specialty), Miyamoto (international + seismic), AHBL (multi-disciplinary). Identify their entrenched accounts vs flip targets.'

    Italic 'Compiled 2026-06-09 from IntelSignal + IntelPersonAffiliation + IntelAction tables on CanonicalOrg.Kind = Competitor. Plus BC/AB named-rival lookup. Drill-down per competitor via Org Dossier in WPF app.'

    $outPath = 'C:\Users\ilalonde\Desktop\KOR-Competitor-Intelligence-Report.docx'
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
