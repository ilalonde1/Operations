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

H1 'KOR Structural — Defense / Military BD Report'
Italic 'BC + Alberta defense and military construction pipeline, with named contacts and KOR action items. Compiled 2026-06-08 from Sonnet drain (first-pass + honing verification). Verdicts: PURSUE / MONITOR / DEAD. PROMPT-driven Yurkovich-class error catch confirmed the original DB filter pulled significant false positives — 3 of 10 verified MPIs are misclassified non-defense, and DCC procurement gates / security clearances filtered 2 more. The two genuine PURSUEs are flagged in section 1.'

H2 'Executive Summary'
P 'Honing pass reviewed 10 BC + Alberta MPIs flagged as defense/military. Verdicts: 2 PURSUE, 5 MONITOR, 3 DEAD. The honing also caught significant database hygiene issues — Esquimalt Secondary (a school, not DND), Comox Valley arenas (civic, not DND), Comox RCMP detachment (RCMP, not DND), and Esquimalt Triangle Site (First Nations commercial, not DND) were filter false positives that the honing flagged for re-categorization.'

P 'Plus a separate first-pass discovery phase identified 6 NEW defense projects not yet in KOR''s MPI database: STTCP MOB West Edmonton, DRDC Suffield Lab (AB), CFB Edmonton CFHA Housing Phase 2, 19 Wing Comox CMMA Hangar, CFB Esquimalt CFHA 120 Units, 19 Wing Comox CFHA Housing Phase 2. These need to be inserted as new MPI rows before the next drain cycle.'

H3 'Verdict roll-up'
MakeTable @('Verdict','Count','Projects') @(
    @('PURSUE','2','Esquimalt JNCM ($165M) + Comox RPAS Facility (~$53M)'),
    @('MONITOR','5','CFB Cold Lake FFCP, Esquimalt Triangle (re-cat), CV Arena 3 (re-cat), Comox RCMP (re-cat), CV Third Ice (re-cat)'),
    @('DEAD','3','CFB Edmonton Health Sciences (delivered 2019), Esquimalt Secondary (misclassified school), Comox Island Anthem Drywall (unverifiable)')
)

H3 'Critical observation'
P 'KOR''s defense pipeline as represented in the DB is currently NOISY. 7 of the 10 MPIs reviewed were either DEAD on the surface (delivered or misclassified) or MONITOR-but-not-defense (civic, RCMP, First Nations). The honing pass essentially functioned as a data-hygiene audit. Going forward, defense MPI ingestion needs tighter filters — projects with "RCMP", "school", "CVRD arena", "First Nations" should NOT auto-classify as defense based on geographic proximity to a CFB.'

H2 '1. PURSUE — Active opportunities'

H3 '1.1 Esquimalt DoD JNCM Accommodations Facility — MPI 6442 [PURSUE]'
B 'Location: ' 'Esquimalt, BC (CFB Esquimalt)'
B 'Value: ' '$165M'
B 'Status: ' 'ACTIVE — JNCM design contract awarded to EllisDon August 2024 ($10.1M design phase). Structural sub-consultant NOT publicly named. Construction start 2026.'
B 'Procurement gate: ' 'DCC (Defense Construction Canada). EllisDon holds the design prime as of Aug 2024 ($10.1M design phase).'
B 'KOR play: ' 'The design phase window for the $165M JNCM Accommodations Facility is open NOW (Aug 2024 to 2026). EllisDon holds the design prime; the structural sub-consultant position has not been publicly disclosed and may still be open or in early sub-procurement. Approach EllisDon Victoria/Vancouver office. KOR''s BC institutional structural depth + proximity is the pitch.'
B 'Security clearance: ' 'Reliability or Secret-level may be required — confirm with EllisDon. KOR will need to evaluate clearance capability.'
P 'Action: Direct outreach to EllisDon''s Vancouver office (the JNCM design prime). Reference KOR''s BC institutional + Esquimalt proximity. Sub-consultant slot may still be open — verify directly. Time-sensitive: design phase is finite, structural sub-list will close before construction RFP issues.'
P 'Engagement timeline (12-month): M1-2 EllisDon outreach + DCC pre-qualification verification. M2-4 In-person meeting at EllisDon Vancouver or Victoria. M4-8 Structural sub-list positioning. M8-12 Construction-phase RFP response if EllisDon engages KOR.'

H3 '1.2 Comox DoD Yellow Ridge — RPAS Facility + CMMA Hangar — MPI 6443 [PURSUE / MONITOR]'
B 'Location: ' 'CFB Comox, 19 Wing Comox, BC'
B 'Value: ' 'RPAS facility ~$53M (PURSUE) + CMMA Maintenance Hangar $279M (MONITOR)'
B 'Status: ' 'RFP PENDING / ACTIVE PROCUREMENT — RPAS facility IAAC complete, design-build RFP imminent or recently issued. CMMA hangar anticipated Spring 2025 posting; award not confirmed in public record as of June 2026.'
B 'Procurement gate: ' 'DCC. Advance Procurement Notice 81311 issued. IAAC environmental review complete for RPAS.'
B 'KOR play (RPAS facility): ' 'PURSUE — ~$53M, 6,000 m² hangar. Structural scope accessible at Reliability clearance level if not SECRET-gated. KOR''s mid-rise + long-span structural depth is the pitch. Sub-consultant on prime D-B winner''s team is the entry.'
B 'KOR play (CMMA hangar): ' 'MONITOR — $279M is more SECRET FSC-restricted likely. Worth tracking but lower KOR competitive position without clearance.'
P 'Action: Subscribe to buyandsell.gc.ca for DCC notices on 19 Wing Comox. Identify likely D-B prime contenders (PCL, Graham, EllisDon, Bird) and pre-position with them as structural sub. Reach out to DCC Pacific Region Office to verify clearance requirements.'

H2 '2. MONITOR — Watching but not actively pursuing'

H3 '2.1 CFB Cold Lake Improvements — MPI 717 [MONITOR]'
B 'Location: ' 'Cold Lake, AB'
B 'Status: ' 'Original MPI scope DEAD (civil/utilities completed 2020). FFCP (Future Fighter Capability Project) is ACTIVE but structural locked with Stantec under EllisDon design-build. Hangerette subpackage and Hangar 6+7 replacement TBD.'
B 'KOR play: ' 'No direct entry on FFCP FSF structural — Stantec captive. Monitor Hangar 6+7 replacement package (anticipated separate procurement) and any hangerette sub-package. EllisDon-Stantec teaming pattern means Stantec captive, but Stantec may sub out specialty structural.'
P 'Action: Track Hangar 6+7 RFP via DCC + EllisDon. Build EllisDon relationship for non-Stantec hangar work.'

H3 '2.2-2.5 Re-categorization candidates — not defense'
P 'The honing pass identified four MPIs that should be RE-CATEGORIZED out of the defense pipeline:'
MakeTable @('MPI','Project','Actual category','Action') @(
    @('3255','Esquimalt Triangle Site','First Nations commercial (Esquimalt Nation, Kosapsum Development Corp)','Re-categorize Commercial/First Nations. Passive monitor.'),
    @('5180','Comox Valley Third Ice Surface','Civic / Recreation (CVRD)','Re-categorize Civic. Same project as 5287 — consolidate. MONITOR for design RFP if project advances.'),
    @('5184','Comox Valley RCMP Detachment Replacement','Civic / Police (Courtenay + CVRD)','Re-categorize Civic. MONITOR — $47M institutional scope if advances to design.'),
    @('5287','Comox Valley Arena 3','Civic / Recreation (CVRD)','Re-categorize Civic. Duplicate of 5180 — merge.')
)

H2 '3. DEAD — Removed from pipeline'

H3 '3.1 CFB Edmonton Health Sciences Centre — MPI 718 [DEAD]'
B 'Status: ' 'COMPLETED — delivered approximately 2019. GC: Aman Building Inc. Procurement: DCC.'
B 'Why DEAD: ' 'Fully built. MPI record is stale.'
P 'Action: Retire MPI 718.'

H3 '3.2 Esquimalt Secondary — MPI 5444 [DEAD / MISCLASSIFIED]'
B 'Status: ' 'Speculative — no MoE capital approval, no RFP, no design budget.'
B 'Why DEAD: ' 'This is a K-12 school project managed by SD61 and BC Ministry of Education, NOT defense/DCC. Filter false positive on geographic proximity to CFB Esquimalt.'
P 'Action: Retire from defense pipeline. Re-categorize as schools MPI if project advances.'

H3 '3.3 Comox Island Project (Anthem Drywall) — MPI 6437 [DEAD / UNVERIFIABLE]'
B 'Status: ' 'No public procurement record, no government project data, no structural scope identified.'
B 'Why DEAD: ' 'KOR internal data entry — investigate who entered this MPI and what underlying project it references. May be a CRM artifact, not an actual project.'
P 'Action: Internal investigation. Retire or re-categorize based on findings.'

H2 '4. Discovered defense projects — pending MPI insertion'

P 'First-pass discovery surfaced 6 NEW defense projects not yet in MPI. Insert as new rows before next drain cycle for proper tracking:'

MakeTable @('Project','Province','Notes') @(
    @('STTCP MOB West Edmonton','AB','Mobile Operations Base West for Strategic Tactical Airlift Capability Project. New CFB Edmonton area facility.'),
    @('DRDC Suffield Lab','AB','Defence Research and Development Canada — laboratory expansion at Suffield.'),
    @('CFB Edmonton CFHA Housing Phase 2','AB','Canadian Forces Housing Agency Phase 2 — multi-year rollout pattern.'),
    @('19 Wing Comox CMMA Hangar','BC','Multi-Mission Aircraft maintenance hangar — $279M. Likely SECRET FSC restricted.'),
    @('CFB Esquimalt CFHA 120 Units','BC','CFHA housing — 120 unit residential program.'),
    @('19 Wing Comox CFHA Housing Phase 2','BC','CFHA Phase 2 at 19 Wing.')
)

P 'Action: Insert all 6 as new MPI rows with ProponentName "Department of National Defence" (or specific procurement entity like DCC or CFHA) and Province set correctly. Tag for honing in next defense drain cycle to identify procurement model + structural opportunity status.'

H2 '5. Key contacts to date'
P 'The 10 honed briefs surfaced contacts across DCC, CFB / Wing commands, and design primes. Names + roles + organizations are captured in the database under IntelProjectKeyPerson for each MPI. Direct email/phone surfacing was limited — DND staff directories rarely publish individual contacts. Most pursuit moves go through DCC Pacific or Prairies Region office, or the named design prime (EllisDon for both Esquimalt JNCM and CFB Cold Lake FFCP).'

P 'Recommended next step: Identify DCC Pacific Region project lead via DCC public org chart, and EllisDon Vancouver/Victoria pre-construction manager — these are the two highest-leverage relationships for KOR''s defense BD strategy.'

H2 '6. Strategic observation'
P 'EllisDon appears in TWO of the 10 reviewed MPIs (Esquimalt JNCM design prime + CFB Cold Lake FFCP) and may appear on the upcoming RPAS facility at CFB Comox. **EllisDon is to defense what Graham Design Builders is to BC healthcare** — a strategic relationship for KOR to build deliberately. Recommend layering EllisDon into the BD database as a strategic GC alongside Graham (Id 8361). EllisDon contacts via their Victoria, Vancouver, or Calgary offices.'

# === SAVE ===
$outPath = 'C:\Users\ilalonde\Desktop\KOR-Defense-Military-BD-Report.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close()
$word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
