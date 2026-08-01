$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-BD-Pursuit-Attribution-2026-06-18.docx"
if (Test-Path $out) { Remove-Item $out -Force }

$navy=6299648; $gray=7697781; $altfill=16382457; $totalfill=14606044
$accentfill=15921906; $white=16777215; $insideclr=13684944

$word=New-Object -ComObject Word.Application; $word.Visible=$false
$doc=$word.Documents.Add(); $sel=$word.Selection
$doc.PageSetup.TopMargin=54; $doc.PageSetup.BottomMargin=54
$doc.PageSetup.LeftMargin=54; $doc.PageSetup.RightMargin=54

function Para([string]$text,[single]$size,[bool]$bold,[int]$color,[single]$after){
    $sel.Font.Name="Calibri"; $sel.Font.Size=$size; $sel.Font.Bold=$bold; $sel.Font.Color=$color
    $sel.ParagraphFormat.SpaceAfter=$after
    $sel.TypeText($text); $sel.TypeParagraph()
    $sel.Font.Bold=$false; $sel.Font.Color=0
}
function GoEnd(){ $sel.EndKey(6) | Out-Null }
function Build-Table($rows,$widths,$amountCols){
    $r=$rows.Count; $c=$rows[0].Count
    $tbl=$doc.Tables.Add($sel.Range,$r,$c)
    $tbl.Borders.Enable=$true; $tbl.Borders.OutsideColor=$navy; $tbl.Borders.InsideColor=$insideclr
    $tbl.Range.Font.Name="Calibri"; $tbl.Range.Font.Size=10.5
    $tbl.Rows.Item(1).Range.Font.Bold=$true
    $tbl.Rows.Item(1).Range.Font.Color=$white
    $tbl.Rows.Item(1).Shading.BackgroundPatternColor=$navy
    for($i=0;$i -lt $r;$i++){
        for($j=0;$j -lt $c;$j++){
            $cell=$tbl.Cell($i+1,$j+1)
            $cell.Range.Text=[string]$rows[$i][$j]
            $cell.VerticalAlignment=1
            if($i -gt 0 -and ($i % 2 -eq 0)){ $cell.Shading.BackgroundPatternColor=$altfill }
        }
    }
    for($j=0;$j -lt $c;$j++){ $tbl.Columns.Item($j+1).Width=$widths[$j] }
    $tbl.AllowAutoFit=$true; $tbl.AutoFitBehavior(2)
    foreach($ac in $amountCols){ for($i=1;$i -le $r;$i++){ $tbl.Cell($i,$ac).Range.ParagraphFormat.Alignment=2 } }
    return $tbl
}
function NumberedList($items){
    $n=1
    foreach($m in $items){
        $sel.Font.Name="Calibri"; $sel.Font.Size=11; $sel.ParagraphFormat.SpaceAfter=8
        $sel.ParagraphFormat.LeftIndent=18; $sel.ParagraphFormat.FirstLineIndent=-18
        $sel.Font.Bold=$true; $sel.TypeText(([string]$n)+".  "+$m[0]+". ")
        $sel.Font.Bold=$false; $sel.TypeText($m[1])
        $sel.TypeParagraph(); $n++
    }
    $sel.ParagraphFormat.LeftIndent=0; $sel.ParagraphFormat.FirstLineIndent=0
}
function Bullets($items){
    foreach($b in $items){
        $sel.Font.Name="Calibri"; $sel.Font.Size=11; $sel.ParagraphFormat.SpaceAfter=6
        $sel.ParagraphFormat.LeftIndent=14; $sel.ParagraphFormat.FirstLineIndent=-14
        $sel.Font.Bold=$true; $sel.Font.Color=$navy; $sel.TypeText([char]0x2022+"  ")
        $sel.Font.Bold=$false; $sel.Font.Color=0; $sel.TypeText($b)
        $sel.TypeParagraph()
    }
    $sel.ParagraphFormat.LeftIndent=0; $sel.ParagraphFormat.FirstLineIndent=0
}

# ===== TITLE =====
Para "Business Development - Pursuit Attribution" 22 $true $navy 2
Para "Can we link BD effort to the money it made?    |    KOR Structural    |    Deltek (live), lifetime through 18 Jun 2026    |    CAD (USA @1.36)" 9.5 $false $gray 12
Para "Figures below are accuracy-audited: the pursuit-to-job link was verified 1:1 and bidirectional, with no duplicate links and no revenue billed under alternate WBS codes. The limits described are real data-coverage gaps, not query artifacts." 9.5 $false $gray 14

# ===== BOTTOM LINE =====
Para "Bottom line" 13 $true $navy 4
Bullets @(
  "Only partly, and only for a thin slice. Just ~7.7% of the firm's lifetime revenue (116 of 2,515 money-making jobs, ~5%) is traceable to a tracked pursuit. The other ~92% came from work never recorded as a pursuit, so Deltek cannot tell you what BD produced firm-wide.",
  "Within that tracked slice the link is rock-solid. 116 pursuits that converted into billing jobs account for `$5.54M of lifetime revenue, and every pursuit-to-job link was verified one-to-one.",
  "But true ROI still cannot be computed. Across those 116 wins, only 177 hours of BD time were ever logged (median half an hour; 52 wins had none at all). The real chasing time lives in the catch-all BD project (99999-10) or is never logged, with no per-deal tag."
)
GoEnd; $sel.TypeParagraph()

# ===== TABLE 1: COVERAGE =====
Para "1.  Coverage reality - how much of the business this even sees" 13 $true $navy 6
$t1=@(
  @("Measure","Value","Pursuit-tracked","Coverage"),
  @("Firm lifetime billed revenue","`$72.4M","`$5.54M","7.7%"),
  @("Jobs that ever billed revenue","2,515","116","4.6%")
)
$tbl1=Build-Table $t1 @(230,110,110,90) @(2,3,4)
GoEnd; $sel.TypeParagraph()
Para "Read this first: roughly 92% of revenue and 95% of money-making jobs never went through a tracked pursuit. Any 'BD won X dollars' figure only describes the small tracked slice, not the firm." 10.5 $false $gray 12

# ===== TABLE 2: the tracked slice =====
Para "2.  Inside the tracked slice - what converted" 13 $true $navy 6
$t2=@(
  @("Measure","Value"),
  @("Pursuit projects linked to a job (verified 1:1)","345"),
  @("...that actually billed revenue","116"),
  @("...linked but never billed (143 active, 67 inactive, 19 dead)","229"),
  @("Lifetime revenue on the 116 billing wins","`$5,537,378")
)
$tbl2=Build-Table $t2 @(360,160) @(2)
$tbl2.Rows.Item(5).Range.Font.Bold=$true
$tbl2.Rows.Item(5).Shading.BackgroundPatternColor=$accentfill
GoEnd; $sel.TypeParagraph()
Para "Note: 'linked to a job' is not the same as 'made money'. Two-thirds of linked pursuits (229 of 345) have billed nothing - some are live jobs not yet invoiced, many are dead or dormant. Counting wins by links alone overstates success." 10.5 $false $gray 12

# ===== TABLE 3: top wins =====
Para "3.  Top revenue-bearing pursuits" 13 $true $navy 6
$t3=@(
  @("Pursuit","Project","Logged hrs","Revenue (CAD)"),
  @("10017-01","Burnaby Lake Aquatic","1.5","`$622,828"),
  @("10063-01","13425-13455 107A Ave Surrey","1.0","`$478,524"),
  @("10272-01","Mirka Tower San Diego","34.5","`$198,000"),
  @("10101-01","Uplands Area 6 Lot 12","0.5","`$187,050"),
  @("10123-01","130 W Broadway Vancouver","3.0","`$127,000"),
  @("10072-01","1310 Monashee Dr","0.5","`$116,000"),
  @("10164-01","3066-3086 Gladwin Abbotsford","1.0","`$95,550"),
  @("10114-01","Bridge & Elliot Delta BC","0.5","`$93,750"),
  @("10030-01","Timmins St Penticton","1.0","`$90,302"),
  @("10113-01","2170 W 1st Ave Vancouver","0.5","`$72,250"),
  @("10051-01","230 Sadler Rd Kelowna","0.5","`$72,050"),
  @("10064-01","Hilton Rd & Bentley Rd Surrey","0.5","`$65,044")
)
$tbl3=Build-Table $t3 @(80,260,90,110) @(3,4)
GoEnd; $sel.TypeParagraph()
Para "The Logged hrs column tells the story: the `$623K and `$479K wins each show ~1 hour. That is the under-logging problem, not the real effort." 10.5 $false $gray 12

# ===== WHY ROI NOT COMPUTABLE =====
Para "4.  Why a true ROI still cannot be computed" 13 $true $navy 6
NumberedList @(
  @("Effort is barely logged","Across the 116 revenue-bearing wins, only 177 hours total were booked to pursuits - median half an hour, and 52 wins had zero logged time. Any 'revenue per hour' or 'per dollar of effort' number is an artifact of missing data, not performance."),
  @("Linked is not the same as billed","Only 116 of 345 linked pursuits (a third) actually billed revenue. Headlines that count 'wins' by links overstate the result; the honest count of money-making tracked pursuits is 116."),
  @("Lost pursuits are not recorded","A pursuit is effectively only captured once it has been won and linked. The CRM fields that would record losses - Stage, Probability, ClosedReason, LostTo - are blank firm-wide, so there is no reliable win/loss rate."),
  @("Most revenue is untracked","About 92% of firm revenue comes from jobs that never became a pursuit record, so firm-level BD return is simply not in the data today.")
)
GoEnd; $sel.TypeParagraph()

# ===== THE FIX =====
Para "5.  How to make BD ROI properly measurable" 13 $true $navy 6
NumberedList @(
  @("Log pursuit time to the pursuit","Have principals and BD staff book chasing time to the specific pursuit project (the 10xxx code, or a task under it) instead of the catch-all 99999-10. That single change makes effort-per-deal real."),
  @("Open the pursuit at go/no-go, and record the outcome","Create the tracking project when the pursuit starts - not when it is won - and mark won or lost (populate Stage / ClosedReason / LostTo). Then win rate and cost-per-win become real numbers."),
  @("Track every real pursuit, not just the winners","The biggest gap is coverage. If a go/no-go decision is made, it should create a pursuit record - so the ~92% of work currently invisible to BD reporting starts showing up."),
  @("Tie it to the Kor.Opportunities pipeline","The BD module already models opportunities and stages. Link each opportunity to its Deltek pursuit and won job (via DeltekClientId / WBS) so the CRM pipeline and the financial outcome reconcile automatically - then cost-per-win and ROI are sliceable by region, principal, market, and Canada vs USA."),
  @("Interim workaround","Until logging improves, the only defensible BD-return statement is portfolio-level: total BD investment (cash plus the 99999-10 labour) versus total new wins in a period - a coarse ratio, clearly labelled as such, not a per-pursuit ROI.")
)
GoEnd; $sel.TypeParagraph()

# ===== METHODOLOGY =====
Para "How this was calculated (and audited)" 13 $true $navy 6
NumberedList @(
  @("Pursuit","A Deltek project with ChargeType = 'P'. Overhead items miscoded as pursuits (99999-xx, e.g. Permit to Practice, EGBC Accredited Employer) were identified and excluded."),
  @("Link integrity audit","The pursuit-to-job link is PR.SiblingWBS1. Verified: all 345 links resolve to a real ChargeType = 'R' job, no SiblingWBS1 is shared by two pursuits (no double-counting), and every job points back to its pursuit (1:1 bidirectional)."),
  @("Revenue","Lifetime Billed Fee on the won job = SUM(-LedgerAR.Amount) where TransType = 'IN' and account is 4001 / 4003 / 4210 / 4220 / 4240; intercompany 4260 excluded. Verified that no linked job bills under a different WBS code, so nothing is missed."),
  @("Effort","tkDetail SUM(RegAmt + OvtAmt + SpecialOvtAmt) and hours on the pursuit project. Salary cost, not burdened."),
  @("Coverage","Pursuit-linked revenue and job counts compared against all ChargeType = 'R' jobs that ever billed. Currency: USA converted to CAD at 1.36; most pursuits and jobs are Canadian."),
  @("Why SiblingWBS1","Deltek's CRM stage / probability / estimated-fee / lost-to fields are blank firm-wide, so SiblingWBS1 is the only reliable pursuit-to-job link in the data.")
)
GoEnd

$doc.SaveAs([ref]$out,[ref]16)
$doc.Close(); $word.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($sel)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)|Out-Null
[GC]::Collect()
"WROTE: $out"
