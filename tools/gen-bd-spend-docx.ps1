$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-BD-Spend-2026-YTD.docx"
if (Test-Path $out) { Remove-Item $out -Force }

$navy      = 6299648    # BGR for RGB(0,51,96)
$gray      = 7697781
$altfill   = 16382457
$totalfill = 14606044
$accentfill= 15921906   # light gold-ish for the headline total row
$white     = 16777215
$insideclr = 13684944

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc  = $word.Documents.Add()
$sel  = $word.Selection
$doc.PageSetup.TopMargin = 54; $doc.PageSetup.BottomMargin = 54
$doc.PageSetup.LeftMargin = 60; $doc.PageSetup.RightMargin = 60

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
    foreach($ac in $amountCols){ for($i=1;$i -le $r;$i++){ $tbl.Cell($i,$ac).Range.ParagraphFormat.Alignment=2 } }
    return $tbl
}

# ===================== TITLE =====================
Para "Business Development Spend - 2026 YTD" 22 $true $navy 2
Para "KOR Structural    |    Source: Deltek Vantagepoint (live ledgers + timesheet detail)    |    Period: 1 Jan - 18 Jun 2026    |    Currency: CAD" 9.5 $false $gray 14

# ===================== BOTTOM LINE =====================
Para "Bottom line" 13 $true $navy 4
$sel.Font.Name="Calibri"; $sel.Font.Size=11; $sel.ParagraphFormat.SpaceAfter=12
$sel.TypeText("Fully loaded, KOR has invested approximately ")
$sel.Font.Bold=$true; $sel.TypeText("`$26,500 (strict) to `$28,200 (fully swept)"); $sel.Font.Bold=$false
$sel.TypeText(" in Business Development so far in 2026. That is the sum of ")
$sel.Font.Bold=$true; $sel.TypeText("`$6,886 cash out-of-pocket"); $sel.Font.Bold=$false
$sel.TypeText(" (airfare, hotels, meals, car/taxi, entertainment, marketing) and ")
$sel.Font.Bold=$true; $sel.TypeText("`$19,587 of staff time"); $sel.Font.Bold=$false
$sel.TypeText(" (about 302 hours logged to the BD and Marketing projects by ~10 people).")
$sel.TypeParagraph(); $sel.TypeParagraph()

# ===================== HEADLINE TABLE: FULLY LOADED =====================
Para "The big number - fully-loaded BD cost" 13 $true $navy 6
$tH = @(
  @("Component","2026 YTD","Basis"),
  @("Cash out-of-pocket (strict)","`$6,886","Vendor + expense-report spend on the BD & Marketing projects"),
  @("Staff labour","`$19,587","302.5 hours logged to BD & Marketing projects, at cost rate"),
  @("Fully-loaded BD cost","`$26,473","Cash + labour, strict project basis"),
  @("...with BD-nature cash booked elsewhere","`$28,228","Adds `$1,755 of BD-type charges coded to other projects")
)
$tblH = Build-Table $tH @(225,75,195) @(2)
$tblH.Rows.Item(4).Range.Font.Bold=$true
$tblH.Rows.Item(4).Shading.BackgroundPatternColor=$accentfill
$tblH.Rows.Item(5).Range.Font.Bold=$true
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===================== TABLE: LABOUR BREAKDOWN =====================
Para "Labour breakdown" 13 $true $navy 6
$tL = @(
  @("Project","Hours","People","Labour cost"),
  @("99999-10  Business Development","197.0","8","`$13,347.44"),
  @("99999-01  Promotional & Marketing","105.5","7","`$6,239.65"),
  @("Total labour","302.5","~10","`$19,587.09")
)
$tblL = Build-Table $tL @(245,70,70,110) @(2,3,4)
$tblL.Rows.Item(4).Range.Font.Bold=$true
$tblL.Rows.Item(4).Shading.BackgroundPatternColor=$totalfill
GoEnd; $sel.TypeParagraph()
Para "Contributors by individual (by labour cost), BD + Marketing projects combined:" 10.5 $false $gray 6
$tT = @(
  @("Employee","Hours","Labour cost"),
  @("Shabana Islam","71.0","`$4,944.44"),
  @("Conor Murtagh","51.5","`$3,896.71"),
  @("James DesRoches","65.0","`$3,302.71"),
  @("Omar Alcazar Pastrana","35.0","`$2,649.50"),
  @("John Markulin","27.0","`$2,056.38"),
  @("Rory Beirne","29.5","`$1,474.75"),
  @("Jason Stuart","9.0","`$457.29"),
  @("Kevin Wurmlinger","7.0","`$355.68"),
  @("Andrea Neuviale","4.5","`$299.75"),
  @("John Bryson","3.0","`$149.88"),
  @("Total","302.5","`$19,587.09")
)
$tblT = Build-Table $tT @(245,80,110) @(2,3)
$tblT.Rows.Item($tT.Count).Range.Font.Bold=$true
$tblT.Rows.Item($tT.Count).Shading.BackgroundPatternColor=$totalfill
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===================== TABLE: CASH BY BUCKET =====================
Para "Cash spend by bucket" 13 $true $navy 6
$t1 = @(
  @("Cost bucket","2026 YTD","What's included"),
  @("BD project (99999-10 Business Development)","`$5,914","Meals `$2,111; Lodging/Hotels `$1,808; Airfare `$769; Car/Taxi `$431; BD fees `$390; Marketing `$158; Promo material `$150; Entertainment `$98"),
  @("Marketing project (99999-01 Promotional & Marketing)","`$972","Airfare `$660; Business entertainment `$312"),
  @("Subtotal - intentional BD / Marketing projects","`$6,886","The two overhead projects above"),
  @("BD-nature cash booked to other projects","`$1,755","Entertainment / marketing / BD fees coded to client jobs or General Overhead instead of the BD project")
)
$tbl1 = Build-Table $t1 @(175,75,245) @(2)
$tbl1.Rows.Item(4).Range.Font.Bold=$true
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===================== TABLE: CASH DETAIL =====================
Para "Cash detail - BD project (99999-10), by expense account" 13 $true $navy 6
$t2 = @(
  @("GL account","Category","Txns","Amount"),
  @("7300.06","Travel - Meals","22","`$2,111.47"),
  @("7300.02","Travel - Lodging & Hotels","6","`$1,808.01"),
  @("7300.01","Travel - Airfare","3","`$768.69"),
  @("7300.03","Travel - Car Rentals & Taxis","10","`$431.41"),
  @("7625.00","Business Development Fees","4","`$389.50"),
  @("7100.07","Marketing & Promotion","1","`$157.50"),
  @("7612.00","Marketing & Promo Material","1","`$150.00"),
  @("7640.00","Business Entertainment","1","`$97.86"),
  @("","Total - BD project","48","`$5,914.44")
)
$tbl2 = Build-Table $t2 @(80,235,55,90) @(3,4)
$tbl2.Rows.Item($t2.Count).Range.Font.Bold=$true
$tbl2.Rows.Item($t2.Count).Shading.BackgroundPatternColor=$totalfill
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===================== METHODOLOGY =====================
Para "How this was calculated" 13 $true $navy 6
$method = @(
  @("Source data", "Pulled live from KOR's Deltek Vantagepoint database via ODBC. Cash comes from the three transaction ledgers (AP = vendor invoices, EX = employee expense reports, Misc = journals); labour comes from timesheet detail (tkDetail). These are real-time, so the figures are current through today and are not subject to the ~3-month posting lag on the GL-based P&L tab."),
  @("How BD is identified", "Deltek tags every transaction and timesheet line with a project code (WBS1). KOR maintains two dedicated overhead projects: 99999-10 Business Development and 99999-01 Promotional & Marketing. BD spend = everything (cash and hours) charged to those two projects. Travel and printing charged to client jobs (the 5xxx / 6xxx reimbursable accounts) are excluded - that is project delivery cost, not BD."),
  @("Cash component (`$6,886)", "Vendor and expense-report lines on the two BD projects, across the indirect 7xxx accounts: travel (Airfare, Lodging & Hotels, Car Rentals & Taxis, Meals), plus Marketing & Promotion, Marketing & Promo Material, Business Development Fees, and Business Entertainment. 'Labor Posting' allocation entries were stripped out so this is true cash only."),
  @("Labour component (`$19,587)", "Direct labour cost = SUM of RegAmt + OvtAmt + SpecialOvtAmt from tkDetail for hours logged to the two BD projects - the same Direct Labour Cost basis the firm's financial dashboard uses. RegAmt is the employee cost rate times hours (a blended ~`$65/hr here), stated in project currency. The BD projects are Canadian, so no FX conversion applies."),
  @("The 'swept' range", "A strict project-based query captures `$6,886 of cash. Some BD-nature charges (entertainment, marketing, BD fees) get coded to client jobs or General Overhead rather than the BD project; sweeping those in adds ~`$1,755, lifting the fully-loaded figure to ~`$28,200."),
  @("What is NOT included", "(a) Printing - effectively zero as a BD line (~`$17 to overhead); KOR books printing direct-to-jobs as reimbursable. (b) Overhead burden - the labour figure is salary cost only; it does not apply the firm's overhead multiplier (benefits, rent, software). Applying that multiplier would increase the labour number further if a fully-burdened view is wanted.")
)
$n = 1
foreach($m in $method){
    $sel.Font.Name="Calibri"; $sel.Font.Size=11
    $sel.ParagraphFormat.SpaceAfter=8
    $sel.ParagraphFormat.LeftIndent=18
    $sel.ParagraphFormat.FirstLineIndent=-18
    $sel.Font.Bold=$true; $sel.TypeText(([string]$n) + ".  " + $m[0] + ". ")
    $sel.Font.Bold=$false; $sel.TypeText($m[1])
    $sel.TypeParagraph()
    $n++
}
$sel.ParagraphFormat.LeftIndent=0; $sel.ParagraphFormat.FirstLineIndent=0
GoEnd; $sel.TypeParagraph()

# ===================== NOTE =====================
Para "Reading the number" 12 $true $navy 4
Para "The `$6,886 cash figure is what most people mean by 'what we spent on BD' - the chequebook cost of flights, hotels, meals and marketing. The `$26,500 fully-loaded figure adds the cost of the time KOR's people put into pursuits and marketing, which is the larger and more strategic investment. Both are defensible; which one to quote depends on the audience." 11 $false 0 4

$doc.SaveAs([ref]$out,[ref]16)
$doc.Close(); $word.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($sel)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)|Out-Null
[GC]::Collect()
"WROTE: $out"
