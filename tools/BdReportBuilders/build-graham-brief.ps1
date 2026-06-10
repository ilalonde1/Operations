$ErrorActionPreference = 'Stop'
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
function Italic { param($t)
    $sel.Style = $doc.Styles.Item('Normal'); $sel.Font.Italic = 1; $sel.Font.Size = 9
    $sel.TypeText($t); $sel.Font.Italic = 0; $sel.Font.Size = 10; $sel.TypeParagraph()
}
function MakeTable { param($headers, $rows)
    $r = $rows.Count + 1; $c = $headers.Count
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

H1 'Graham Design Builders LP — KOR BD Intelligence Brief'
Italic 'Strategic intel on Graham Construction & Engineering Inc. as a BC healthcare-wave general contractor. Compiled 2026-06-08 from web sources (Graham project pages, ReNew Canada, On-Site Magazine, Healthcare Facilities Today, Construction Canada, Infrastructure BC, Vancouver Coastal Health, Northern Health, Island Health press releases).'

H2 'Why this matters to KOR'
P 'Graham Design Builders LP has built a $3.4B+ active BC healthcare design-build pipeline across four major hospital projects in the last 24 months and was named on ReNew Canada Top 100 Infrastructure Projects 2026. The Yurkovich Pavilion structural scope is closed (Entuitive holds it), but Grahams pre-construction team is the high-leverage relationship for KOR to position on the next wave of BC healthcare and infrastructure pursuits. Two specific structural-engineer slots on Grahams active pipeline are NOT publicly named yet and represent live BD intel gaps for KOR.'

H2 'Company background'
P 'Calgary-headquartered, employee-owned construction solutions partner providing general contracting, design-build, construction management, and P3 services across buildings, industrial, and infrastructure sectors. BC office network covers Vancouver, Coquitlam, Abbotsford, Kamloops, and Kelowna. Healthcare is currently their dominant BC growth story.'

H2 'BC healthcare pipeline ($3.4B+ active)'

MakeTable @('Project','Value','Architect / Prime','Structural Engineer','Status') @(
    @('Richmond Hospital Yurkovich Family Pavilion','$1.96B','HDR Architecture Associates','Entuitive (CONFIRMED)','Alliance Partnership. Phase 2 construction 2026-2029. Locked.'),
    @('Cariboo Memorial Hospital','$366M','Stantec','Stantec (in-house)','Progressive design-build. Active construction. Closed shop (Stantec captive structural).'),
    @('Dawson Creek and District Hospital','$590M','HDR (prime consultant)','NOT PUBLICLY NAMED','Design-build awarded to Graham. Substantial completion Fall 2026, patients 2027. POTENTIAL KOR INTEL GAP.'),
    @('Stuart Lake Hospital Redevelopment (Fort St. James)','TBD (70,000 sq ft)','TBD','TBD','Progressive Design-Build. Design Early Works Agreement signed, then Design-Build Agreement. Envelope largely complete. Full team not disclosed. POTENTIAL KOR INTEL GAP.')
)

H3 'Adjacent BC infrastructure work'
P 'Granville Bridge South Approach Ramps Rehabilitation, Vancouver. Major components complete, finishing work progressing. Non-healthcare but signals Grahams continued BC footprint and TransLink/MoTi relationships.'

H2 'Named BC contacts'

MakeTable @('Name','Role','KOR target priority') @(
    @('Alex Trifunov','Pre-construction Manager, Graham Vancouver office','#1 — Pre-con is where structural sub-consultant conversations happen on Grahams pipeline. Direct outreach is the entry move.'),
    @('Richard [surname not yet surfaced]','Executive Vice President, 30+ years construction experience, UK origin','Senior sponsorship relationship'),
    @('Blake Christian','EVP, Buildings U.S. Division (healthcare focus)','US-side relationship; less BC-specific but valuable for KOR US West Coast pursuits')
)

H2 'Structural-engineer partnering pattern (CRITICAL)'

P 'Grahams structural-sub selection follows the prime architects vertical integration:'

MakeTable @('When Graham partners with...','Structural engineer outcome','KOR opportunity') @(
    @('HDR Architecture (Richmond, Dawson Creek)','External structural sub (not HDR-captive)','LIVE — HDR brings external structural sub. Entuitive won Richmond. Dawson Creek slot NOT yet publicly named.'),
    @('Stantec (Cariboo)','Stantec in-house structural','CLOSED — Stantec is vertically integrated, no external sub'),
    @('TBD architect (Stuart Lake)','TBD','UNKNOWN — early enough to position')
)

H2 'KOR strategic positioning'

H3 'What NOT to pursue'
P 'Yurkovich Family Pavilion structural scope. Locked with Entuitive via the Graham + HDR + Entuitive Alliance Partnership. Confirmed via Entuitives Jan 2026 15-year anniversary scholarship announcement.'

H3 'What TO pursue (in priority order)'

P '1. Dawson Creek and District Hospital ($590M) — structural-engineer not publicly disclosed. HDR is prime, Graham is GC. If HDR did not bring a captive structural sub, this slot may still be live. Worth a targeted call to Alex Trifunov to verify status and position KOR.'

P '2. Stuart Lake Hospital Redevelopment — full team composition not surfaced. Progressive Design-Build means early team formation; engaging Graham pre-con NOW could land KOR in the structural-sub position before lock-in.'

P '3. Future Graham BC healthcare and infrastructure pursuits — Graham is on the ReNew Canada Top 100 list and clearly committed to BC. Building the Alex Trifunov relationship pays compounding returns across multiple future pursuits.'

H3 'Engagement move sequence'

P 'Month 1: Direct LinkedIn or email outreach to Alex Trifunov (Pre-construction Manager, Vancouver office). Ask about Grahams next 2-3 BC healthcare pursuits and where they need structural depth they do not already have via HDR or Stantec captive teams.'

P 'Month 2-3: In-person meeting at Grahams Vancouver office. Present KORs BC healthcare structural portfolio, mid-rise concrete depth, and seismic experience. Reference specific past KOR projects of comparable size and type.'

P 'Month 3-6: Position KOR on Grahams pre-qualified structural-sub list for upcoming BC pursuits. Verify Dawson Creek structural status concurrently.'

P 'Month 6-12: Targeted pursuit of next Graham + HDR (non-Stantec) BC healthcare or infrastructure project. KOR submission via Grahams pre-con team.'

H2 'Open BD intelligence gaps'

P 'Items requiring follow-up research to nail the strategy:'

P '1. Richard [surname] — Graham EVP. Full name not surfaced via web search. Honing pass needed.'

P '2. Dawson Creek Hospital structural engineer — public records do not name a structural firm. Likely either HDR sub or undisclosed. Confirm via Alex Trifunov direct call.'

P '3. Stuart Lake Hospital full design team — Progressive Design-Build full team composition not yet public. Honing pass via Graham project page + Northern Health announcements.'

P '4. Grahams broader BC structural-sub history — what other BC structural firms has Graham used on non-healthcare pursuits in the last 5 years? Pattern intel would sharpen KORs pitch.'

H2 'Sources'

Italic 'Graham Construction & Engineering Inc. — grahambuilds.com (project profiles, leadership team, news archive). Vancouver Coastal Health press release Sept 2024 (Yurkovich proponent selection). ReNew Canada Top 100 Infrastructure Projects 2026. On-Site Magazine, Healthcare Facilities Today, Construction Canada, REMI Network, HCO News, Stantec project announcement (Cariboo, 2023). Infracon Construction Inc. (Dawson Creek civil subcontractor). Northern Health "Lets Talk" project update July 2025 (Dawson Creek). Entuitive 15-year anniversary scholarship announcement Jan 2026 (Richmond Alliance Partnership confirmation).'

$outPath = 'C:\Users\ilalonde\Desktop\Graham-Design-Builders-Brief.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close()
$word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
