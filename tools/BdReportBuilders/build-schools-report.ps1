$ErrorActionPreference = 'Stop'
$briefs = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.schools-final.json' -Raw | ConvertFrom-Json
if (-not $briefs -or $briefs.Count -eq 0) {
    throw "No briefs in .schools-final.json. Re-run the schools pull query."
}
$pursue = $briefs | Where-Object {$_.Verdict -eq 'PURSUE'}
$monitor = $briefs | Where-Object {$_.Verdict -eq 'MONITOR'}
$dead = $briefs | Where-Object {$_.Verdict -eq 'DEAD'}
$discover = $briefs | Where-Object {$_.Verdict -eq 'DISCOVER'}

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
    # Robustness: if $rows got pipeline-flattened to a 1D array of strings
    # (very easy to do with PowerShell @() + ForEach-Object), re-chunk by column count.
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
function ExtractSD { param($p)
    if ($p.Proponent -match '(SD\s*\d+|School District\s*(No\.?\s*)?\d+|Conseil scolaire|CSF|EPSB|CBE|ECSD|EICCSD|ATSC)') { return $matches[1] }
    if ($p.Name -match '(SD\s*\d+|CSF|EPSB|CBE)') { return $matches[1] }
    return 'Unknown SD'
}

H1 'KOR Structural — BC + AB Schools BD Report'
Italic 'K-12 school construction pipeline in BC and Alberta. Compiled 2026-06-08 from Sonnet first-pass + honing verification on 290 MPIs. Verdicts: PURSUE / MONITOR / DEAD / DISCOVER. Schools are KOR''s bread-and-butter recurring market — landing one SD opens the whole district pipeline. The BMZ legacy (KOR was Bryson Markulin Zickmantel before 2021 rebrand) is explicitly leveraged where prior SD relationships exist.'

H2 'Executive Summary'
B ('Projects honed: ') ($briefs.Count.ToString() + ' (290 BC + AB K-12 school MPIs)')
B ('PURSUE — open opportunities: ') ($pursue.Count.ToString())
B ('MONITOR — locked but SD has more pipeline: ') ($monitor.Count.ToString())
B ('DEAD — fully awarded or unviable: ') ($dead.Count.ToString())
B ('DISCOVER — pre-procurement build-SD-relationship-now: ') ($discover.Count.ToString())

P 'Schools market is rolling procurement — SDs typically procure 1-5 projects per year via pre-qualified consultant lists. Landing one SD opens the whole pipeline.'

H2 'URGENT — 3 named time-sensitive actions'
P 'From Sonnet honing status notes:'
P '1. CALL Brian Jonker SD62 (Sooke) at 250-474-9800 re: North Langford Secondary $220M — design procurement ACTIVE NOW.'
P '2. CHECK BidCentral for École Beausoleil $73M RFP — design procurement active.'
P '3. CHECK BidCentral for Matthew McNair Secondary SD38 (Richmond) RFP — design RFP imminent or recently opened, SE not yet selected.'

H2 '1. PURSUE — Open opportunities (50 projects)'

P 'Each project flagged as PURSUE has an active or pending design RFP, OR an unawarded structural-engineer slot KOR can position for. The BC seismic upgrade pipeline (Province of BC SMP — Seismic Mitigation Program) dominates the PURSUE list — direct KOR specialty.'

# BC seismic upgrades — KOR direct specialty
H3 '1.1 BC Seismic Mitigation Program upgrades (KOR direct specialty)'
$seismic = $pursue | Where-Object { $_.Name -match 'Seismic' }
foreach ($p in $seismic) {
    B ("Id $($p.Id): ") "$($p.Name)"
    P "Province: $($p.Province) | Proponent: $($p.Proponent) | Cost: $($p.Cost)"
    P ($p.korAngle.Substring(0, [Math]::Min(450, $p.korAngle.Length)))
    Italic ("Status: " + $p.status)
    P ''
}

# Non-seismic PURSUE — BC
H3 '1.2 BC new builds + expansions'
$bcNew = $pursue | Where-Object { $_.Province -eq 'BC' -and $_.Name -notmatch 'Seismic' }
foreach ($p in $bcNew) {
    B ("Id $($p.Id): ") "$($p.Name)"
    P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
    P ($p.korAngle.Substring(0, [Math]::Min(400, $p.korAngle.Length)))
    Italic ("Status: " + $p.status)
    P ''
}

# AB PURSUE
H3 '1.3 Alberta schools'
$abPursue = $pursue | Where-Object { $_.Province -eq 'AB' }
foreach ($p in $abPursue) {
    B ("Id $($p.Id): ") "$($p.Name)"
    P "Proponent: $($p.Proponent) | Cost: $($p.Cost)"
    P ($p.korAngle.Substring(0, [Math]::Min(400, $p.korAngle.Length)))
    Italic ("Status: " + $p.status)
    P ''
}

H2 '2. MONITOR — SD relationships worth building (45 projects)'

P 'Current project locked but the School District has additional projects in their capital plan. Sustained SD relationship positions KOR for future pursuits. Note any SDs appearing multiple times in this list — those are the highest-leverage relationship targets.'

MakeTable @('Id','Project','Proponent','Cost','Province') @(
    @(($monitor | ForEach-Object {
        @($_.Id,
          $_.Name.Substring(0, [Math]::Min(60, $_.Name.Length)),
          ($_.Proponent.Substring(0, [Math]::Min(35, $_.Proponent.Length))),
          $_.Cost,
          $_.Province)
    }))
)

H2 '3. DEAD — Awarded or unviable (108 projects)'

P 'Significant DB hygiene observation: 108 of 290 honed school MPIs are DEAD. This includes structural engineers already awarded, projects delivered, or speculative MPIs without procurement approval. The DEAD list is below for reference only — these are NOT active pursuit targets.'

P ('DEAD projects by province: BC ' + (($dead | Where-Object {$_.Province -eq 'BC'}).Count) + ', AB ' + (($dead | Where-Object {$_.Province -eq 'AB'}).Count))

# Just list briefly, no detail
MakeTable @('Id','Project','Province') @(
    @(($dead | ForEach-Object {
        @($_.Id, $_.Name.Substring(0, [Math]::Min(80, $_.Name.Length)), $_.Province)
    }))
)

H2 '4. DISCOVER — Pre-procurement SD relationship-build (87 projects)'

P 'Pre-procurement projects where KOR should be building the SD relationship NOW to position for the next RFP cycle. SDs procure 1-5 projects per year — relationships compound across rolling procurement. Group by SD to identify highest-leverage targets.'

MakeTable @('Id','Project','Proponent','Province','Cost') @(
    @(($discover | ForEach-Object {
        @($_.Id,
          $_.Name.Substring(0, [Math]::Min(50, $_.Name.Length)),
          ($_.Proponent.Substring(0, [Math]::Min(35, $_.Proponent.Length))),
          $_.Province,
          $_.Cost)
    }))
)

H2 '5. Strategic Synthesis'

H3 '5.1 BC Seismic Mitigation Program — KOR direct specialty wave'
P 'The Province of BC SMP (Seismic Mitigation Program) drives a multi-decade rolling pipeline of school seismic upgrades — every BC school in the 1970s-1990s era is on the list. The PURSUE seismic projects (Sutherland, R.C. Palmer, Hugh McRoberts, Carson Graham, Churchill, Lynnmour, Matthew McNair) are KOR''s direct competitive sweet spot. Land structural-engineer slots on these by positioning with the architect engaged on each project + the SD facilities team.'

H3 '5.2 SD relationships compound — pick the high-velocity SDs'
P 'School District procurement cadence varies — some SDs procure 5+ projects per year (Surrey SD36, Burnaby SD41, Vancouver SD39, Richmond SD38, Sooke SD62), others 1-2 per year. Cross-reference the PURSUE + MONITOR + DISCOVER lists to identify SDs appearing 3+ times — those are KOR''s highest-leverage relationship targets. Single SD pre-qualified list slot = 5+ years of recurring pursuits.'

H3 '5.3 BMZ legacy — search KOR''s prior SD relationships'
P 'KOR was Bryson Markulin Zickmantel (BMZ) before the 2021 rebrand. Many BC SD relationships pre-date the rename. Before contacting any SD, search KOR''s portfolio for prior BMZ-era work in that SD. A 1990s-2010s BMZ project reference is the warm-introduction path that compounds — SD Facilities Directors typically remember prior consultants by name.'

H3 '5.4 CSF (Conseil scolaire francophone) projects'
P 'École Beausoleil, École Élémentaire Beausoleil, École Beausoleil K-12 — multiple CSF MPIs appear PURSUE. CSF is a SINGLE province-wide francophone school district with consolidated capital procurement. One CSF relationship = access to their entire BC pipeline.'

H2 '6. Recommended next actions'

P '1. **CALL Brian Jonker SD62 (Sooke) at 250-474-9800** re: North Langford Secondary $220M structural-engineer slot. Sonnet flagged this as the highest-priority single action.'

P '2. **Verify BidCentral for École Beausoleil $73M RFP** + Matthew McNair Secondary SD38 RFP. Both have unawarded SE slots per honing pass.'

P '3. **Audit KOR portfolio for prior BMZ-era SD relationships** — identify SDs where KOR has a warm-intro path that pre-dates the rebrand. Likely candidates: Vancouver SD39, Burnaby SD41, Coquitlam SD43, Surrey SD36, Richmond SD38.'

P '4. **Target BC seismic upgrade specialty** — bid on the SMP pipeline. KOR''s seismic structural expertise + BC market depth = direct competitive position. Architects to engage as warm-intro: PUBLIC Architecture, Acton Ostry, KMBR, MMA, Stantec.'

P '5. **Build CSF relationship** — single province-wide francophone SD with multiple PURSUE projects. One relationship = recurring access.'

H2 '7. Notes on K-12 schools BD methodology'
P 'Schools procurement is rolling — SDs maintain pre-qualified consultant lists and rotate engagements across multiple projects per year. The pursuit play is NOT one-project-at-a-time. It is SD-relationship building.'
P 'Architects typically drive structural-engineer selection on school projects (different from healthcare where the owner often pre-selects). KOR''s strategy is to pair with the 5-10 BC school-market architects (PUBLIC, Acton Ostry, KMBR, MMA, Iredale, Tarpley-collaborative architects, NSDA) AS WELL AS the SD facilities teams directly. Two-channel relationship building.'
P 'BMZ legacy is real and underleveraged — every BC SD relationship from 1990s-2020 likely has a current KOR connection if the prior project structural credit can be surfaced from KOR''s portfolio archive.'

# === SAVE ===
$outPath = 'C:\Users\ilalonde\Desktop\KOR-Schools-BD-Report.docx'
$doc.SaveAs([ref]$outPath, [ref]16)
$doc.Close()
$word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($sel) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
[gc]::Collect(); [gc]::WaitForPendingFinalizers()
"Wrote: $outPath"
