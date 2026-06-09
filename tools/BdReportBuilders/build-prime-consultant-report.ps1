$ErrorActionPreference = 'Stop'
$primes = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.primes-final.json' -Raw | ConvertFrom-Json
if (-not $primes -or $primes.Count -eq 0) { throw "No prime briefs in .primes-final.json" }

$withPrime = $primes | Where-Object { $_.PrimeFirm }
$withoutPrime = $primes | Where-Object { -not $_.PrimeFirm }
$korAligned = $primes | Where-Object { $_.KnownToKor }

$word = New-Object -ComObject Word.Application
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
function Safe { param($v, $max) if (-not $v) { return '' }; $s = [string]$v; if ($s.Length -le $max) { return $s }; return $s.Substring(0, $max) }
function MakeTable { param($headers, $rows)
    $c = $headers.Count; $r = $rows.Count + 1
    $rng = $sel.Range; $tbl = $doc.Tables.Add($rng, $r, $c); $tbl.Style = "Table Grid"; $tbl.Range.Font.Size = 9
    for ($i = 0; $i -lt $c; $i++) { $tbl.Cell(1, $i+1).Range.Text = $headers[$i]; $tbl.Cell(1, $i+1).Range.Bold = $true }
    for ($r2 = 0; $r2 -lt $rows.Count; $r2++) {
        for ($c2 = 0; $c2 -lt $c; $c2++) { $tbl.Cell($r2+2, $c2+1).Range.Text = [string]$rows[$r2][$c2] }
    }
    $word.Selection.EndKey(6) | Out-Null; $sel.TypeParagraph()
}

H1 'KOR Structural — Prime Consultant Identification Report'
Italic ('Prime consultant intelligence for KOR''s BD pipeline. In public AEC work, the Prime Consultant (typically the architect) submits ONE team proposal to the owner — including KOR as structural sub. Knowing the prime = knowing who to approach BEFORE the RFP issues. Sourced from 2 focused Sonnet research drains (bc-ab-primes batch-001 + batch-002, ' + $primes.Count.ToString() + ' projects researched).')

H2 'Executive Summary'
B 'Projects researched: ' ($primes.Count.ToString())
B 'Prime consultant identified: ' "$($withPrime.Count) ($([Math]::Round(100.0*$withPrime.Count/$primes.Count, 0))%)"
B 'Prime unknown after research: ' "$($withoutPrime.Count) ($([Math]::Round(100.0*$withoutPrime.Count/$primes.Count, 0))%)"
B 'KOR-aligned primes (warm-intro available): ' ($korAligned.Count.ToString())

P 'Prime identification success rate varies by project type: high for public flagships (press coverage, permit applications), lower for early-stage private developments. Unknown primes typically require FOIP request or direct owner outreach.'

H2 'KOR-ALIGNED PRIMES — warm-intro available NOW'

if ($korAligned.Count -gt 0) {
    P 'These projects have a Prime Consultant where KOR already has a relationship (per memory). Direct outreach to existing KOR contact at the firm. Highest-leverage immediate action.'
    P ''
    $rows = @()
    foreach ($p in ($korAligned | Sort-Object -Property Cost -Descending)) {
        $rows += ,@(
            "$($p.Id)",
            (Safe $p.Name 50),
            (Safe $p.PrimeFirm 40),
            "$($p.Cost)",
            $p.Province
        )
    }
    MakeTable @('Id','Project','Prime Firm','Cost','Province') $rows
} else {
    P 'No KOR-aligned primes identified in this research batch. (KOR-aligned status depends on Sonnet''s read of KOR''s memory client list — see honing brief details for nuance.)'
}

H2 'Top 30 identified primes — by project cost'

$top30 = $withPrime | Sort-Object -Property Cost -Descending | Select-Object -First 30
$rows = @()
foreach ($p in $top30) {
    $rows += ,@(
        "$($p.Id)",
        (Safe $p.Name 45),
        (Safe $p.PrimeFirm 40),
        "$($p.Cost)"
    )
}
MakeTable @('Id','Project','Prime Consultant','Cost') $rows

H2 'Prime Consultant Heatmap — most-frequent firms across pipeline'

$primeFrequency = $withPrime | Group-Object PrimeFirm | Sort-Object Count -Descending | Select-Object -First 25
$rows = @()
foreach ($pf in $primeFrequency) {
    $rows += ,@((Safe $pf.Name 55), "$($pf.Count)", "$([string]::Join(', ', ($pf.Group.Id | Select-Object -First 4)))")
}
MakeTable @('Prime Consultant Firm','Projects','Sample MPI Ids') $rows

H2 'Top 10 — drill-down with project details'

$rank = 1
foreach ($p in ($withPrime | Sort-Object -Property Cost -Descending | Select-Object -First 10)) {
    H3 ("$rank. $($p.Name)")
    B 'Project Id: ' "$($p.Id)"
    B 'Province: ' $p.Province
    B 'Proponent: ' $p.Proponent
    B 'Cost: ' "$($p.Cost)"
    B 'Prime Consultant: ' $p.PrimeFirm
    if ($p.PrincipalInCharge) { B 'Principal-in-Charge: ' $p.PrincipalInCharge }
    if ($p.ContactEmail) { B 'Contact Email: ' $p.ContactEmail }
    if ($p.Confidence) { B 'Confidence: ' "$($p.Confidence)" }
    if ($p.KnownToKor) { B 'KOR Relationship: ' 'KOR-aligned per memory — warm-intro available' }
    if ($p.KorAngle) { P (Safe $p.KorAngle 500) }
    P ''
    $rank++
}

H2 'Unknown primes — recommended next moves'

if ($withoutPrime.Count -gt 0) {
    P 'For these projects, Sonnet research could not identify a publicly-disclosed prime consultant. Typically requires FOIP request (BC) / FOIP request (AB) / direct owner contact / building permit application search.'
    P ''
    $top20unknown = $withoutPrime | Sort-Object -Property Cost -Descending | Select-Object -First 20
    $rows = @()
    foreach ($p in $top20unknown) {
        $rows += ,@(
            "$($p.Id)",
            (Safe $p.Name 50),
            "$($p.Cost)",
            $p.Province,
            'FOIP request / owner direct outreach'
        )
    }
    MakeTable @('Id','Project','Cost','Province','Recommended Action') $rows
}

H2 'Strategic Synthesis'

H3 'Most-leveraged prime relationships'
P 'Per the heatmap above, firms appearing 3+ times across KOR''s pipeline = highest-leverage relationships to build/deepen. One sustained Principal-in-Charge relationship at these firms unlocks recurring KOR structural sub positioning across multiple projects.'

H3 'EllisDon-as-prime pattern'
P 'EllisDon Design Build appears as Prime on multiple major BC healthcare projects (Royal Columbian Phases 2 & 3, RIH Phase 2 Completion). This validates the Strategic Relationships report finding that EllisDon BC office (Murphy / Husband / Enns / MacDonald / McFarlane) is a cross-sector compounding target — already known healthcare engagement now confirmed at prime level.'

H3 'Public-PD architects as scoreable team leaders'
P 'Hariri Pontarini, Diamond Schmitt, KPMB, Bing Thom/Revery, Acton Ostry — these are the public-facing scoreable architect names KOR''s team-sub pitch must align with. Position KOR''s P.Eng count + similar-project experience as the structural complement these firms need to win publicly.'

H3 'Unknown primes are not necessarily dead'
P 'Unknown-prime projects often represent EARLIEST-stage opportunities — the prime hasn''t been selected yet because the owner is still in master-planning. These are DISCOVER candidates — relationship-build with the OWNER (not the prime, who doesn''t exist yet).'

H2 'Recommended next actions'

P '1. **KOR-aligned primes table** — immediate outreach via existing KOR relationship at named firms.'
P '2. **EllisDon BC office** — re-engage on Royal Columbian, RIH, future BC healthcare. 5 named contacts already in pipeline.'
P '3. **Hariri Pontarini + Adamson + Stantec on SFU Medicine** — initiate structural-sub positioning via Stantec (prime team includes Stantec captive structural; opportunity may be on Phase 2 or future SFU pursuits).'
P '4. **Iredale Architecture (Build Canada Homes consortium)** — DASH stream BD opportunity. KOR-aligned per memory.'
P '5. **Top 20 unknown-prime projects** — FOIP request to BC/AB municipalities + Health Authorities + School Districts for development permit / RFI documentation.'
P '6. **Audit the prime heatmap quarterly** — firms appearing on 5+ KOR pipeline projects are top-tier relationship priorities.'

H2 'Methodology'

P ('Sourced from ' + $primes.Count + ' Sonnet-researched briefs via the bc-ab-primes drain queue (PROMPT.md at tools/BdQueueDrainPrompts/bc-ab-primes/PROMPT.md). Each brief required: named prime consultant firm with sourceUrl, named Principal-in-Charge where discoverable, KOR relationship status (BMZ legacy check included), KOR pursuit verdict (HIGH-LEVERAGE / WARM-INTRO / COLD / LOCKED), 12-month engagement timeline. Briefs without prime identification document the search trail.')

Italic ('Compiled 2026-06-10 from MajorProjectEnrichment.ResultJson where ProviderName = ''PrimeConsultantResearch'' or description carries the prime-research marker. Drill-down per project via Project Brief in WPF app.')

$outPath = 'C:\Users\ilalonde\Desktop\KOR-Prime-Consultant-Report.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close(); $word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
