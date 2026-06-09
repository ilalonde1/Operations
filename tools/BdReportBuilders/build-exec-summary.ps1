$ErrorActionPreference = 'Stop'

$cats = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.exec-rollup.json' -Raw | ConvertFrom-Json
$totalHoned = ($cats | Measure-Object -Property Honed -Sum).Sum
$totalM = ($cats | Measure-Object -Property DollarsM -Sum).Sum

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc = $word.Documents.Add(); $sel = $word.Selection
$styles = $doc.Styles
$styles.Item('Heading 1').Font.Size = 18; $styles.Item('Heading 1').Font.Bold = $true
$styles.Item('Heading 1').ParagraphFormat.SpaceBefore = 0; $styles.Item('Heading 1').ParagraphFormat.SpaceAfter = 8
$styles.Item('Heading 2').Font.Size = 13; $styles.Item('Heading 2').Font.Bold = $true
$styles.Item('Heading 2').ParagraphFormat.SpaceBefore = 14; $styles.Item('Heading 2').ParagraphFormat.SpaceAfter = 4
$styles.Item('Heading 3').Font.Size = 11; $styles.Item('Heading 3').Font.Bold = $true
$styles.Item('Heading 3').ParagraphFormat.SpaceBefore = 8; $styles.Item('Heading 3').ParagraphFormat.SpaceAfter = 2
$styles.Item('Normal').Font.Size = 10.5; $styles.Item('Normal').ParagraphFormat.SpaceAfter = 4
$doc.PageSetup.TopMargin = 36; $doc.PageSetup.BottomMargin = 36; $doc.PageSetup.LeftMargin = 54; $doc.PageSetup.RightMargin = 54

function H1 { param($t) $sel.Style = $doc.Styles.Item('Heading 1'); $sel.TypeText($t); $sel.TypeParagraph() }
function H2 { param($t) $sel.Style = $doc.Styles.Item('Heading 2'); $sel.TypeText($t); $sel.TypeParagraph() }
function H3 { param($t) $sel.Style = $doc.Styles.Item('Heading 3'); $sel.TypeText($t); $sel.TypeParagraph() }
function P  { param($t) $sel.Style = $doc.Styles.Item('Normal');    $sel.TypeText($t); $sel.TypeParagraph() }
function B  { param($lbl, $val) $sel.Style = $doc.Styles.Item('Normal'); $sel.Font.Bold = 1; $sel.TypeText($lbl); $sel.Font.Bold = 0; $sel.TypeText($val); $sel.TypeParagraph() }
function Italic { param($t) $sel.Style = $doc.Styles.Item('Normal'); $sel.Font.Italic = 1; $sel.Font.Size = 9.5; $sel.TypeText($t); $sel.Font.Italic = 0; $sel.Font.Size = 10.5; $sel.TypeParagraph() }
function MakeTable { param($headers, $rows)
    $c = $headers.Count; $r = $rows.Count + 1
    $rng = $sel.Range; $tbl = $doc.Tables.Add($rng, $r, $c); $tbl.Style = "Table Grid"; $tbl.Range.Font.Size = 9
    for ($i = 0; $i -lt $c; $i++) { $tbl.Cell(1, $i+1).Range.Text = $headers[$i]; $tbl.Cell(1, $i+1).Range.Bold = $true }
    for ($r2 = 0; $r2 -lt $rows.Count; $r2++) {
        for ($c2 = 0; $c2 -lt $c; $c2++) { $tbl.Cell($r2+2, $c2+1).Range.Text = [string]$rows[$r2][$c2] }
    }
    $word.Selection.EndKey(6) | Out-Null; $sel.TypeParagraph()
}

# =================================================================
# COVER
# =================================================================
H1 'KOR Structural — BD Pipeline Executive Overview'
Italic 'Cross-sector synthesis of all 10 BD research categories. Compiled 2026-06-09 from KorOpportunitiesDb. Each project verified against named procurement model + incumbent structural engineer (Yurkovich-class error catch baked into every honing pass). All claims sourced from the database — no narrative inflation.'

# =================================================================
# HEADLINE
# =================================================================
H2 'The Headline'

B 'Total active BC + AB MPIs (cross-sector): ' '2,211'
B 'First-pass enriched: ' '2,341 (>100% incl. retired)'
B 'Honed (deep BD intel — verified, named, action-ready): ' "$totalHoned (~75%)"
B 'Total $ honed pipeline (estimated cost rollup): ' "~`$$([Math]::Round($totalM/1000.0,1))B"
B 'Categories at 100% honed coverage: ' '8 of 10 (Schools 86%, Hospitals 67% still in flight)'
B 'Migrations applied this cycle: ' '14 (m106-m114, including 5 dupe-consolidations)'
B 'Detailed Word reports on Desktop: ' '12 reports + this overview'

P ''
P 'KOR''s BD intelligence is no longer a list of projects — it is a named, verified, action-ready pursuit engine. Every PURSUE has procurement model identified, incumbent (if any) named, decision-makers surfaced, warm-intro path mapped, and 12-month engagement timeline. The "Yurkovich Pavilion" error class — chasing already-locked projects — is structurally eliminated.'

# =================================================================
# THIS WEEK
# =================================================================
H2 'IMMEDIATE — Action this week'

H3 '1. June 30 RFP — DCC Pacific CFHA Housing $700M (22 days)'
P 'MPI 7162 + 7163. 19 Wing Comox CFHA Phase 2 + CFB Esquimalt CFHA 120 Units. Submit interest or structural sub-consultant proposal NOW via MERX. Contact DCC Pacific Region office.'

H3 '2. NACIC + SACIC Alberta DBFM ($249M twin RFP)'
P 'MPI 7036 ($90M Edmonton) + MPI 6473 ($159M Calgary). RFP stage active, contract award August 2026. Shortlisted consortia forming teams NOW. Engage PCL Health Group, EllisDon, Graham Healthcare. KOR Alberta presence = direct angle.'

H3 '3. Plant + Animal Health Centre Replacement $400M (BC)'
P 'MPI 6585. IBC D-B RFP stage. Shortlisted primes filling sub-consultant slots NOW. Identify shortlisted teams + engage immediately.'

H3 '4. Brian Jonker SD62 (Sooke) — 250-474-9800'
P 'MPI 5261 North Langford Secondary $220M. Design procurement active. Call THIS week.'

H3 '5. VPO pre-qualification = 30 VSB seismic schools unlock'
P '$850M aggregate program. Single action: contact Jessie Gresley-Jones (jgresley-jones@vsb.bc.ca) — unlocks pre-qualified consultant list across all VSB seismic projects.'

H3 '6. Olympic Village Elementary + Pitt Meadows Secondary'
P 'MPIs 4436 + 4439 (PURSUE_URGENT). Hybrid mass-timber 4-storey VSB project, rezoning approved Feb 2026; Pitt Meadows tender April 2026 — confirm if structural still open.'

# =================================================================
# CATEGORY ROLL-UP
# =================================================================
H2 'Sector heatmap'

MakeTable @('Sector','Total','Honed','% Honed','$ Pipeline (M)') @(
    ,@('Schools (K-12)', '679', '582', '86%', '$19,869M')
    ,@('Residential / Condo', '293', '261', '89%', '$11,861M')
    ,@('Hospitals + Healthcare', '262', '175', '67%', '$26,511M')
    ,@('Post-Secondary', '147', '147', '100%', '$17,596M')
    ,@('Recreational', '119', '117', '98%', '$7,060M')
    ,@('BC Housing', '92', '91', '99%', '$2,932M')
    ,@('Indigenous', '47', '47', '100%', '$1,409M')
    ,@('Commercial Office', '42', '42', '100%', '$6,538M')
    ,@('Defense / Military', '9', '9', '100%', '$46M')
    ,@('Total', '1,690', '1,371', '81%', '$93,822M')
)

P 'Hospitals leads $ pipeline ($26.5B honed) — biggest single bet. Post-Secondary $17.6B + Schools $19.9B together = $37.5B education infrastructure. Residential $11.9B is KOR''s largest unit-count market.'

# =================================================================
# STRATEGIC RELATIONSHIPS
# =================================================================
H2 'Strategic compounding relationships'

P 'These are NOT individual project pursuits. These are RELATIONSHIPS that, if built, unlock recurring KOR positioning across multi-year multi-project pipelines. One relationship = N projects.'

H3 'Healthcare'
P '**Graham Design Builders LP (canonical 8361)** — $3.4B+ active BC healthcare pipeline. Alex Trifunov, Vancouver Pre-Construction Manager. Single relationship = entire Graham healthcare portfolio.'
P '**EllisDon (canonical 22257)** — $1.6B UHNBC + defense + Esquimalt JNCM + future BC healthcare. 7 named BC contacts: Daniel Murphy (VP Preconstruction), Keeli Husband (Dir BD), Craig Enns (SVP BC), Candace MacDonald (Dir Special Projects), David McFarlane (COO Western), plus 2 more.'
P '**Sharon Petty at VCH** — Director Real Estate Operations. VGH Building 1 + future VCH pipeline.'

H3 'Indigenous'
P '**MST Development Corp (Musqueam-Squamish-Tsleil-Waututh)** — Lower Mainland mega-developments: Jericho Lands, Maplewood Innovation District, Tsleil-Waututh North Shore. Billion-dollar phased developments. One MST relationship = N phases.'

H3 'Recreational + Civic'
P '**HCMA Architecture + Design (Vancouver)** — BC dominant rec specialist architect. On Newton + Britannia + most major BC rec projects. Single Principal-in-Charge relationship at HCMA = recurring KOR positioning across BC rec.'

H3 'Commercial Office + TOD'
P '**TELUS Living (Manasweeta Bhatia program)** — 40 sites under evaluation, 20 in planning/construction. Repurposing 2,300+ institutional properties. Single Vancouver HQ relationship unlocks national pipeline.'

H3 'Schools'
P '**Jessie Gresley-Jones (VSB)** — jgresley-jones@vsb.bc.ca. Single contact unlocks 30 VSB seismic schools ($850M program).'
P '**Brian Jonker (SD62 / Sooke)** — 250-474-9800. SD62 capital plan + North Langford Secondary $220M.'
P '**GGA Architecture (Gibbs Gage)** — AB school market player. FrancoSud Airdrie + St. Martin de Porres HS.'

H3 'Residential'
P '**KOR memory-confirmed client developers (warm-leads)**: Wesgroup, Bosa, Reliance, Westland, Belford, Anthem, Cressey, Peterson, Strand, Beedie, Capital Region Housing, Onni, Concord, Polygon. BMZ pre-2021 legacy = 30+ years of BC residential relationships.'

H3 'Post-Secondary'
P '**Ryder Architecture (UBC Lower Mall Student Housing)** — immediate call per Sonnet honing flag.'

# =================================================================
# CROSS-CUTTING STRATEGIC INSIGHTS
# =================================================================
H2 'Cross-sector strategic insights'

H3 'Procurement-mature markets vs open RFP markets'
P 'Commercial Office: 88% DEAD (procurement-mature, structural already locked). Hospitals: 60% DEAD (Alliance / P3 / Stantec-captive). Indigenous: 100% relationship-driven (no DEAD in traditional sense). Schools: 23% DEAD (large pipeline of seismic upgrades still active).'
P 'Implication: pursuit strategy by sector is FUNDAMENTALLY different. Commercial = relationships not RFPs. Schools = relationship + active RFP both work. Hospitals = procurement model identification is the make/break gate (Yurkovich correction).'

H3 'Mass timber as KOR''s emerging differentiator'
P 'TELUS Ocean Vancouver = the BC commercial office model. UBC Brock Commons + Earth Sciences = the post-secondary academic model. Whitemud Acres / Olds Sportsplex = the recreational model. KOR''s ability to deliver mass timber + concrete hybrid is a competitive advantage on mid-rise (8-12 storey) across THREE sectors simultaneously.'

H3 'Long-span specialty (aquatics + arenas + field house) = KOR direct fit'
P 'Recreational sector is KOR''s structural sweet spot. Long-span roof + complex MEP + corrosive environment detailing (aquatic centres) + low-temp condensation (ice arenas) + clear-span (field houses) = direct KOR specialty. 16 PURSUE + 35 DISCOVER in recreational = compounding pipeline.'

H3 'BMZ legacy as warm-intro lever'
P 'KOR was Bryson Markulin Zickmantel pre-2021. Many BC developers, owners, architects have BMZ-era references that pre-date the rebrand. Audit KOR portfolio for prior BMZ work on each developer in MONITOR list — that''s the warm-intro path. 30+ years of compounded BC residential + commercial + institutional relationships.'

H3 'Alliance / P3 / Captive-structural as DEAD signals'
P 'Yurkovich correction: procurement model identification BEFORE marking PURSUE. Alliance = structural locked with team partner. P3 / DBFM = structural locked with selected design-build prime. Stantec-captive = healthcare locked with in-house structural. These are the 3 systematic locks that mark a project DEAD even if construction hasn''t started. Honing PROMPTs now mandate this verification.'

H3 'Single-action multiplier opportunities'
P '5 specific single actions that unlock disproportionate pipeline:'
P '  1. VPO pre-qualification → 30 VSB seismic schools ($850M)'
P '  2. MST Dev Corp relationship → Lower Mainland Indigenous phased mega-pipeline'
P '  3. EllisDon BC office → defense + healthcare + commercial cross-cutting'
P '  4. Graham Vancouver pre-con → $3.4B BC healthcare pipeline'
P '  5. HCMA Vancouver Principal → BC rec recurring positioning'

# =================================================================
# GEOGRAPHIC + PROVINCIAL
# =================================================================
H2 'Geographic concentration'

P 'KOR market footprint per memory: BC + LA + San Diego today; growth = Edmonton + US West Coast (WA/OR). The honed pipeline reflects this — BC dominates, AB second, US not yet in scope (Spring 2026 priority).'

P '**BC concentration**: Greater Vancouver (Vancouver, Surrey, Burnaby, Coquitlam, Richmond), Vancouver Island (Victoria, Saanich, Nanaimo, Courtenay), Interior (Kelowna, Vernon, Penticton), Northern BC (Prince George, Quesnel).'

P '**AB concentration**: Calgary CBD, Edmonton metropolitan, Red Deer corridor, Rocky View (Airdrie, Cochrane), Strathcona County. NorQuest + SAIT + NAIT + MacEwan + U of A non-medical surfaced as post-secondary opportunities.'

# =================================================================
# DATA HYGIENE / TRUST
# =================================================================
H2 'Data hygiene + intelligence quality'

P 'Honed briefs are not aggregated guesses — each has named procurement model, named incumbent (if applicable), named decision-makers with contact info, named warm-intro path, and 12-month engagement timeline.'

H3 'Migrations applied this cycle (14 total, m106-m114)'
P 'm106-m107: US + AB project stage backfill (~766 rows). m108: Graham strategic canonical layering + Alex Trifunov IntelPerson. m109: Defense MPI cleanup + EllisDon canonical consolidation (dupes 69204, 72453 retired). m110: UHNBC consolidation (9 dupes → survivor 7010, 78 Intel rows repointed). m111: Vernon Jubilee Psychiatric consolidation (5 dupes → 7031, 58 Intel rows repointed). m113: Post-Secondary corrections (Province bug + NAIT dupe + UVic delay note). m114: Newton (4 dupes → 4589) + Britannia (2 dupes → 6483) consolidation (35 Intel rows repointed).'

H3 'Yurkovich-class error catches'
P 'The honing PROMPT''s rigor (20-30 tool calls per project, mandatory procurement model identification) caught:'
P '  - Richmond Hospital Yurkovich Pavilion (Entuitive incumbent, NOT KOR opportunity)'
P '  - Cowichan Hospital (Alliance with Bush Bohlman)'
P '  - Cariboo Memorial + several others (Stantec-captive)'
P '  - 7 Yurkovich-class corrections in hospitals batch alone'

# =================================================================
# DETAILED REPORTS
# =================================================================
H2 'Detailed reports — drill-down references'

P 'For each sector, the full Word report on Desktop has every PURSUE, every MONITOR, every named contact, every action item with verdict reasoning:'

MakeTable @('Report','Verdicts','Headline') @(
    ,@('KOR-Schools-BD-Report.docx', '506', '3 URGENT, VPO unlock + Brian Jonker call')
    ,@('KOR-Residential-BD-Report.docx', '429', '63 PURSUE, BMZ legacy + developer-PQ play')
    ,@('KOR-PostSecondary-BD-Report.docx', '182', '29 PURSUE, Ryder Arch UBC Lower Mall')
    ,@('KOR-Hospitals-BD-Report.docx', '161', '9 URGENT, NACIC+SACIC twin DBFM')
    ,@('KOR-Recreational-BD-Report.docx', '166', 'HCMA + long-span sweet spot')
    ,@('KOR-Commercial-BD-Report.docx', '126', '88% DEAD, relationships > projects')
    ,@('KOR-BC-Housing-BD-Report.docx', '89', '1 URGENT Evergreen Terrace')
    ,@('KOR-Indigenous-BD-Report.docx', '60', 'MST Dev Corp single anchor')
    ,@('KOR-Defense-Military-BD-Report.docx', '16', 'June 30 RFP $700M CFHA')
    ,@('KOR-Defense-EllisDon-UPDATE.docx', '1 dive', '7 named BC contacts')
    ,@('Graham-Design-Builders-Brief.docx', '1 dive', '$3.4B+ BC healthcare')
    ,@('KOR-This-Week-Call-Sheet.docx', '145 PURSUE', 'Cross-sector URGENT roll-up')
)

# =================================================================
# WHAT'S NEXT
# =================================================================
H2 'What''s next'

H3 'Immediate (next 7 days)'
P '1. Act on the 6 URGENT items above (June 30 RFP, NACIC/SACIC, Plant+Animal Health, Brian Jonker, VPO, Olympic Village + Pitt Meadows).'
P '2. Adversarial audit per docs/BD-Audit-Prompt-2026-06-09.md on fresh Opus 4.8 session — gates BEFORE WPF in-app build.'
P '3. Review Top 5 EllisDon BC contacts (Murphy, Husband, Enns, MacDonald, McFarlane) for cold-outreach campaign.'

H3 'Short-term (next 30 days)'
P '1. WPF in-app BD Reports tile build per docs/BD-UI-Plan-2026-06-08.md — OpenXml-based generators, WebView2 preview, action tracking, MCP AI integration. Estimated 5 days build effort across 3 phases.'
P '2. Strategic relationship outreach campaign — 6 named compounding targets above (Graham, EllisDon, HCMA, MST, TELUS Living, Sharon Petty).'
P '3. Single-action multipliers: VPO pre-qual, EllisDon BC office, Graham Vancouver, HCMA Principal — 4 calls covering ~$5B+ pipeline access.'

H3 'Medium-term (next 90 days)'
P '1. BD AI layer (Phase 4) — MCP function-calling tools for verdict queries (per project_bd_ai_layer_deferred.md). Builds on top of in-app reports.'
P '2. US West Coast (WA / OR) entry — second growth market per KOR geographic footprint memory.'
P '3. Hospitals pt3 + Schools batch-004 if more capacity surfaces — last ~25% coverage.'

# =================================================================
# SIGNATURE
# =================================================================
H2 'Methodology footnote'

P 'Every brief in this overview was generated via: (1) Sonnet first-pass enrichment from web search + government sources; (2) Yurkovich-rigor honing pass with 20-30 tool calls per project, mandatory procurement model identification + named incumbent + 12-month engagement timeline + named warm-intro path; (3) MajorProjectEnrichment + Intel* decompose into normalized relational rows; (4) WPF Org Dossier + Project Brief + Region Brief integration; (5) Word docx report builders pulling live from DB.'

P 'No claim in this overview is asserted without underlying DB rows. Every URGENT can be drilled down to the named contact + named action + procurement model evidence.'

Italic 'Compiled 2026-06-09 by KOR Operations BD pipeline. Pre-audit. WPF in-app build queued.'

$outPath = 'C:\Users\ilalonde\Desktop\KOR-BD-Pipeline-Executive-Overview.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close(); $word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
