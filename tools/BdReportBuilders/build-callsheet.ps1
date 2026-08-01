$ErrorActionPreference = 'Stop'
$cs = $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB
Add-Type -AssemblyName System.Data
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()
$cmd = $conn.CreateCommand(); $cmd.CommandTimeout = 120

# Pull all PURSUE_URGENT + PURSUE briefs across all categories (any honed today)
$cmd.CommandText = @"
SELECT m.Id, m.ProjectName, m.Province, m.MunicipalityName, m.ProponentName,
       COALESCE(m.EstimatedCostText, CAST(m.EstimatedCostCad AS NVARCHAR(64))) AS Cost,
       e.ResultJson, e.LastRefreshAtUtc
FROM opportunities.MajorProjectsInventory m
INNER JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id
    AND e.ProviderName = N'ProjectBriefHoning'
    AND e.LastRefreshAtUtc >= '2026-06-08'
WHERE m.RetiredAtUtc IS NULL
  AND (e.ResultJson LIKE N'%PURSUE_URGENT%' OR e.ResultJson LIKE N'%URGENT%'
       OR (e.ResultJson LIKE N'%PURSUE%' AND e.ResultJson NOT LIKE N'%MONITOR%PURSUE%'))
ORDER BY e.LastRefreshAtUtc DESC;
"@
$r = $cmd.ExecuteReader()
$briefs = @()
while ($r.Read()) {
    $item = $r.GetValue(6) | ConvertFrom-Json
    # BD-Audit-2026-06-09 C8: read the structured verdict first
    # (honingPass.verdict, then top-level verdict); korAngle text is the
    # last resort. NOTE: '_' is a word character, so '\bPURSUE\b' can NOT
    # match inside 'PURSUE_URGENT' — the longer token must be tried first
    # and without trailing \b.
    $verdict = $item.honingPass.verdict
    if (-not $verdict) { $verdict = $item.verdict }
    if (-not $verdict) {
        $verdict = if ($item.korAngle -match '(PURSUE_URGENT|PURSUE|MONITOR|DEAD|DISCOVER|DUPLICATE)') { $matches[1] } else { '?' }
    }
    $isUrgent = ($verdict -eq 'PURSUE_URGENT') -or ($item.korAngle -match 'URGENT')
    if ($isUrgent -and $verdict -eq 'PURSUE') { $verdict = 'PURSUE_URGENT' }
    if ($verdict -eq 'PURSUE_URGENT' -or $verdict -eq 'PURSUE') {
        # Nested honing shape keeps these under honingPass — coalesce.
        $korAngle = $item.honingPass.korAngle; if (-not $korAngle) { $korAngle = $item.korAngle }
        $status = $item.honingPass.status; if (-not $status) { $status = $item.status }
        $keyPeople = $item.honingPass.keyPeople; if (-not $keyPeople) { $keyPeople = $item.keyPeople }
        $actions = $item.honingPass.actions; if (-not $actions) { $actions = $item.actions }
        $briefs += [PSCustomObject]@{
            Id=$r.GetValue(0); Name=$r.GetValue(1); Province=$r.GetValue(2)
            City=if($r.IsDBNull(3)){''}else{$r.GetValue(3)}
            Proponent=if($r.IsDBNull(4)){''}else{$r.GetValue(4)}
            Cost=if($r.IsDBNull(5)){''}else{$r.GetValue(5)}
            Verdict=$verdict; korAngle=$korAngle; status=$status
            keyPeople=$keyPeople; actions=$actions
        }
    }
}
$r.Close()
$conn.Close()
"PURSUE_URGENT: $((($briefs | Where-Object {$_.Verdict -eq 'PURSUE_URGENT'}).Count))"
"PURSUE: $((($briefs | Where-Object {$_.Verdict -eq 'PURSUE'}).Count))"

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
        $r = $rows.Count + 1; $c = $headers.Count
        $rng = $sel.Range; $tbl = $doc.Tables.Add($rng, $r, $c); $tbl.Style = "Table Grid"; $tbl.Range.Font.Size = 9
        for ($i = 0; $i -lt $c; $i++) { $tbl.Cell(1, $i+1).Range.Text = $headers[$i]; $tbl.Cell(1, $i+1).Range.Bold = $true }
        for ($r2 = 0; $r2 -lt $rows.Count; $r2++) {
            for ($c2 = 0; $c2 -lt $c; $c2++) { $tbl.Cell($r2+2, $c2+1).Range.Text = [string]$rows[$r2][$c2] }
        }
        $word.Selection.EndKey(6) | Out-Null; $sel.TypeParagraph()
    }

    H1 'KOR — This Week''s BD Call Sheet'
    Italic 'Cross-cutting action sheet pulled across all 6 BD categories (Hospitals, Schools, Indigenous, BC Housing, Defense, EllisDon). PURSUE_URGENT + PURSUE items ranked by time-sensitivity. Generated 2026-06-08 from KorOpportunitiesDb after the full sweep. This is what to act on THIS WEEK.'

    H2 'THE 3 most time-sensitive — call/email THIS WEEK'

    H3 '1. JUNE 30 2026 BID — PR26RCIP_Pacific CFHA Housing ($700M)'
    P 'MERX solicitation covering 19 Wing Comox CFHA Phase 2 (MPI 7163) + CFB Esquimalt CFHA 120 Units (MPI 7162). 22 DAYS REMAINING from today. KOR action: submit interest or structural sub-consultant proposal NOW. Requires immediate review of bid docs on MERX and contact with DCC Pacific Region office.'

    H3 '2. Plant and Animal Health Centre $400M — RFP shortlisted teams forming structural sub slots'
    P 'MPI 6585. IBC D-B RFP stage. Shortlisted primes are now filling sub-consultant slots. Identify shortlisted teams and engage immediately. KOR''s BC institutional structural depth is the pitch.'

    H3 '3. Brian Jonker SD62 (Sooke) — 250-474-9800'
    P 'MPI 5261 (North Langford Secondary $220M). Design procurement ACTIVE NOW. Call this week. Reference KOR''s BC school structural portfolio + any BMZ-legacy SD62 prior work.'

    H2 'PURSUE_URGENT — additional 6 items'

    H3 'NACIC + SACIC Alberta DBFM (twin $249M)'
    P 'MPI 7036 (NACIC Edmonton $90M) + MPI 6473 (SACIC Calgary $159M). Same procurement. RFP stage NOW. Contract August 2026. Shortlisted consortia forming teams NOW. KOR''s Alberta presence is direct angle. Engage PCL Health Group, EllisDon, Graham Healthcare.'

    H3 'Vernon Jubilee Hospital Psychiatric — Interior Health D-B'
    P 'MPI 7031 (consolidated from 5 dupes — m111 just applied). IBC RFP active. Award likely H1 2026. Structural sub within D-B team. Sylvia Weir (Interior Health CEO LinkedIn) + Lorne Sisley (VP Infrastructure) are the relationship.'

    H3 'Quesnel Long-Term Care Home'
    P 'MPI 5253. Operator: Providence Living. Construction start fall 2026. Structural unconfirmed. Providence Living + Northern Health channel.'

    H3 'VGH Building 1 Redevelopment'
    P 'MPI 6494. VCH Campus Building 1 — interim rezoning Summer 2026. Pre-procurement but high-priority. Sharon Petty at VCH Real Estate Operations — LinkedIn: linkedin.com/in/sharon-petty-6491a911b/.'

    H3 'CFB Edmonton CFHA Phase 2 $459M RFP imminent'
    P 'MPI 7166. 1,010 units, $459M. RFP expected on MERX based on DCC Spring 2026 timeline from the April 13 info session. Monitor MERX DCC Western Region daily.'

    H3 'Esquimalt JNCM $165M — Gap 3 from EllisDon honing'
    P 'MPI 6442. EllisDon holds the $10.1M design prime since Aug 2024. Structural sub-consultant NOT publicly named. Direct outreach to Keeli Husband (Dir BD EllisDon khusband@ellisdon.com) or Daniel Murphy (VP Preconstruction dmurphy@ellisdon.com).'

    H2 'Top 5 EllisDon BC contacts (most leverage)'

    MakeTable @('Name','Role','Email','Why call them') @(
        @('Daniel Murphy','VP Preconstruction Services','dmurphy@ellisdon.com','Decision-maker for structural sub on BC pursuits. Esquimalt JNCM + Vernon Jubilee + UHNBC future pursuits all flow through Murphy.'),
        @('Keeli Husband','Director Business Development','khusband@ellisdon.com','Front-door for pursuit conversations. Esquimalt JNCM gap-closure first call.'),
        @('Craig Enns','SVP and Area Manager British Columbia','cenns@ellisdon.com','Senior sponsorship. Long-term BC relationship build.'),
        @('Candace MacDonald','Director Special Projects BC','cmacdonald@ellisdon.com','UHNBC Acute Care Tower $1.579B — Special Projects channel.'),
        @('David McFarlane','COO and EVP Construction Western Canada','dmcfarlane@ellisdon.com','Western Canada gate. Includes Alberta NACIC/SACIC if EllisDon wins.')
    )

    H2 'Strategic Relationship Targets (compounding leverage)'

    H3 'MST Development Corp (Musqueam-Squamish-Tsleil-Waututh)'
    P 'Single highest-leverage Indigenous BD relationship. Controls Jericho Lands (Id 6911), Maplewood Innovation District (Id 4882), Tsleil-Waututh North Shore Innovation District (Id 6913). Billion-dollar multi-tower phased developments. One MST relationship = recurring across Phase 1-N.'

    H3 'TELUS Living — Manasweeta Bhatia'
    P '40 sites under evaluation, 20 in planning/construction. Repurposing 2,300+ institutional properties. Single TELUS Vancouver HQ relationship unlocks national pipeline. KOR mid-rise concrete is direct fit.'

    H3 'Graham Design Builders LP — Alex Trifunov'
    P '$3.4B+ active BC healthcare pipeline. Pre-construction Manager Vancouver office is the entry. Dawson Creek Hospital structural NOT publicly named — live KOR intel gap.'

    H3 'CSF (Conseil scolaire francophone)'
    P 'Single province-wide francophone SD with multiple PURSUE projects (Beausoleil + École Élémentaire Beausoleil + École Beausoleil K-12). One relationship = entire CSF BC pipeline.'

    H3 'CSU CPDC — California Seismic Pipeline'
    P 'Lisa Salgado at CSULB (Peterson Hall $259M) is the entry. CSU Chancellor''s Office Capital Planning + Design + Construction controls seismic replacement across 23 California campuses. Per-campus PM entry → CPDC master gate.'

    H2 'Status of all 6 BD categories'

    MakeTable @('Category','Honed','URGENT items','Top action') @(
        @('Defense','16/16','3','June 30 RFP — PR26RCIP_Pacific'),
        @('EllisDon','1/1','—','Email Keeli Husband / Daniel Murphy'),
        @('Schools','200/200','3','Call Brian Jonker 250-474-9800'),
        @('Indigenous','60/60','—','Build MST Development Corp relationship'),
        @('BC Housing','89/92','1','Victoria Evergreen Terrace'),
        @('Hospitals','161/273','9','NACIC+SACIC + Plant&Animal + Vernon Jubilee + VGH')
    )

    H2 'Where to find more detail'

    P 'For each category there is a full Word report on Desktop:'
    P '- KOR-Defense-Military-BD-Report.docx + KOR-Defense-EllisDon-UPDATE.docx'
    P '- KOR-Schools-BD-Report.docx'
    P '- KOR-Indigenous-BD-Report.docx'
    P '- KOR-BC-Housing-BD-Report.docx'
    P '- KOR-Hospitals-BD-Report.docx'
    P '- Graham-Design-Builders-Brief.docx'

    P 'Per-project intel is also visible in the WPF app:'
    P '- Org Dossier for Graham (Id 8361), EllisDon (Id 22257), and all enriched canonical orgs'
    P '- Project Brief for any MPI with honing-pass data'
    P '- Region Brief for BC + AB rolled-up category views'

    $outPath = 'C:\Users\ilalonde\Desktop\KOR-This-Week-Call-Sheet.docx'
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
