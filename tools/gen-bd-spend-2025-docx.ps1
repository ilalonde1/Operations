$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-BD-Spend-2025.docx"
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

# ===== TITLE =====
Para "Business Development Spend - 2025" 22 $true $navy 2
Para "KOR Structural    |    Calendar year 2025 (full year)    |    Source: Deltek Vantagepoint (live)    |    Currency: CAD" 9.5 $false $gray 14

# ===== BOTTOM LINE =====
Para "Bottom line" 13 $true $navy 4
$sel.Font.Name="Calibri"; $sel.Font.Size=11; $sel.ParagraphFormat.SpaceAfter=12
$sel.TypeText("Fully loaded, KOR invested approximately ")
$sel.Font.Bold=$true; $sel.TypeText("`$44,400 (strict) to `$52,800 (fully swept)"); $sel.Font.Bold=$false
$sel.TypeText(" in Business Development in 2025 - the sum of ")
$sel.Font.Bold=$true; $sel.TypeText("`$18,151 cash out-of-pocket"); $sel.Font.Bold=$false
$sel.TypeText(" (travel, meals, entertainment, marketing material, BD fees) and ")
$sel.Font.Bold=$true; $sel.TypeText("`$26,238 of staff time"); $sel.Font.Bold=$false
$sel.TypeText(" (403 hours logged to the BD and Marketing projects by 13 people).")
$sel.TypeParagraph(); $sel.TypeParagraph()

# ===== HEADLINE TABLE =====
Para "The big number - fully-loaded BD cost" 13 $true $navy 6
$tH=@(
  @("Component","2025","Basis"),
  @("Cash out-of-pocket (strict)","`$18,151","Vendor + expense-report spend on the BD & Marketing projects"),
  @("Staff labour","`$26,238","403 hours logged to BD & Marketing projects, at cost rate"),
  @("Fully-loaded BD cost","`$44,389","Cash + labour, strict project basis"),
  @("...with BD-nature cash booked elsewhere","`$52,795","Adds `$8,407 of BD-type charges coded to other projects")
)
$tblH=Build-Table $tH @(225,75,195) @(2)
$tblH.Rows.Item(4).Range.Font.Bold=$true
$tblH.Rows.Item(4).Shading.BackgroundPatternColor=$accentfill
$tblH.Rows.Item(5).Range.Font.Bold=$true
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===== LABOUR BY PROJECT =====
Para "Labour breakdown" 13 $true $navy 6
$tL=@(
  @("Project","Hours","People","Labour cost"),
  @("99999-10  Business Development","235.5","10","`$15,622.46"),
  @("99999-01  Promotional & Marketing","167.5","10","`$10,615.63"),
  @("Total labour","403.0","13","`$26,238.09")
)
$tblL=Build-Table $tL @(245,70,70,110) @(2,3,4)
$tblL.Rows.Item(4).Range.Font.Bold=$true
$tblL.Rows.Item(4).Shading.BackgroundPatternColor=$totalfill
GoEnd; $sel.TypeParagraph()
Para "Contributors by individual (by labour cost), BD + Marketing projects combined:" 10.5 $false $gray 6
$tT=@(
  @("Employee","Hours","Labour cost"),
  @("James DesRoches","108.0","`$6,715.44"),
  @("Conor Murtagh","77.5","`$5,532.33"),
  @("Rory Beirne","69.5","`$4,202.59"),
  @("John Markulin","42.5","`$3,015.94"),
  @("Omar Alcazar Pastrana","37.5","`$2,618.87"),
  @("Kevin Wurmlinger","27.0","`$1,675.44"),
  @("Hengchao Xu","12.0","`$753.84"),
  @("Jeremy Atkinson","11.0","`$686.73"),
  @("Jason Stuart","7.5","`$470.93"),
  @("John Bryson","5.5","`$284.52"),
  @("Andrea Neuviale","3.5","`$209.37"),
  @("Daler Singh","1.0","`$45.47"),
  @("Jing Zheng","0.5","`$26.62"),
  @("Total","403.0","`$26,238.09")
)
$tblT=Build-Table $tT @(245,80,110) @(2,3)
$tblT.Rows.Item($tT.Count).Range.Font.Bold=$true
$tblT.Rows.Item($tT.Count).Shading.BackgroundPatternColor=$totalfill
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===== CASH BY BUCKET =====
Para "Cash spend by bucket" 13 $true $navy 6
$t1=@(
  @("Cost bucket","2025","What's included"),
  @("BD project (99999-10 Business Development)","`$8,248","BD fees `$3,499; Meals `$1,951; Mileage `$1,210; Lodging `$1,116; plus airfare, trains, entertainment, supplies"),
  @("Marketing project (99999-01 Promotional & Marketing)","`$9,902","Promo material `$4,442; Education & seminars `$2,129; BD fees `$1,904; Entertainment `$1,334; courier `$92"),
  @("Subtotal - intentional BD / Marketing projects","`$18,151","The two overhead projects above"),
  @("BD-nature cash booked to other projects","`$8,407","Marketing / entertainment / BD fees coded to client jobs or General Overhead instead of the BD project")
)
$tbl1=Build-Table $t1 @(175,75,245) @(2)
$tbl1.Rows.Item(4).Range.Font.Bold=$true
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===== CASH DETAIL: BD project =====
Para "Cash detail - BD project (99999-10), by expense account" 13 $true $navy 6
$t2=@(
  @("GL account","Category","Txns","Amount"),
  @("7625.00","Business Development Fees","24","`$3,499.31"),
  @("7300.06","Travel - Meals","2","`$1,951.24"),
  @("7300.04","Travel - Mileage","2","`$1,209.50"),
  @("7300.02","Travel - Lodging & Hotels","5","`$1,116.19"),
  @("7300.08","Travel - Trains & Boats","2","`$146.00"),
  @("7300.01","Travel - Airfare","1","`$129.52"),
  @("7640.00","Business Entertainment","3","`$93.89"),
  @("7510.00","General Office Supplies","1","`$50.00"),
  @("7870.00","Parking","1","`$26.66"),
  @("7300.05","Travel - Parking","1","`$26.04"),
  @("","Total - BD project","42","`$8,248.35")
)
$tbl2=Build-Table $t2 @(80,235,55,90) @(3,4)
$tbl2.Rows.Item($t2.Count).Range.Font.Bold=$true
$tbl2.Rows.Item($t2.Count).Shading.BackgroundPatternColor=$totalfill
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===== CASH DETAIL: Marketing project =====
Para "Cash detail - Marketing project (99999-01), by expense account" 13 $true $navy 6
$t3=@(
  @("GL account","Category","Txns","Amount"),
  @("7612.00","Marketing & Promo Material","9","`$4,442.09"),
  @("7730.00","Education & Seminars","2","`$2,129.44"),
  @("7625.00","Business Development Fees","2","`$1,904.45"),
  @("7640.00","Business Entertainment","8","`$1,333.96"),
  @("7040.00","Courier, Postage, Shipping & Delivery","4","`$92.25"),
  @("","Total - Marketing project","25","`$9,902.19")
)
$tbl3=Build-Table $t3 @(80,235,55,90) @(3,4)
$tbl3.Rows.Item($t3.Count).Range.Font.Bold=$true
$tbl3.Rows.Item($t3.Count).Shading.BackgroundPatternColor=$totalfill
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===== METHODOLOGY =====
Para "How this was calculated" 13 $true $navy 6
$method=@(
  @("Source data","Pulled live from KOR's Deltek Vantagepoint database via ODBC. Cash comes from the three transaction ledgers (AP = vendor invoices, EX = employee expense reports, Misc = journals); labour comes from timesheet detail (tkDetail). Covers calendar 2025 in full."),
  @("How BD is identified","Deltek tags every transaction and timesheet line with a project code (WBS1). KOR maintains two dedicated overhead projects: 99999-10 Business Development and 99999-01 Promotional & Marketing. BD spend = everything (cash and hours) charged to those two projects. Travel and printing charged to client jobs (the 5xxx / 6xxx reimbursable accounts) are excluded - that is project delivery cost, not BD."),
  @("Cash component (`$18,151)","Vendor and expense-report lines on the two BD projects, across the indirect 7xxx accounts (travel, marketing, BD fees, entertainment, education, supplies). 'Labor Posting' allocation entries were stripped out so this is true cash only."),
  @("Labour component (`$26,238)","Direct labour cost = SUM of RegAmt + OvtAmt + SpecialOvtAmt from tkDetail for hours logged to the two BD projects - the same Direct Labour Cost basis the firm's financial dashboard uses. RegAmt is the employee cost rate times hours (a blended ~`$65/hr), stated in project currency. The BD projects are Canadian, so no FX conversion applies."),
  @("The 'swept' range","A strict project-based query captures `$18,151 of cash. Some BD-nature charges (marketing, entertainment, BD fees) get coded to client jobs or General Overhead rather than the BD project; sweeping those in adds ~`$8,407, lifting the fully-loaded figure to ~`$52,800."),
  @("What is NOT included","(a) Printing - effectively nil as a BD line; KOR books printing direct-to-jobs as reimbursable. (b) Overhead burden - the labour figure is salary cost only; it does not apply the firm's overhead multiplier (benefits, rent, software).")
)
$n=1
foreach($m in $method){
    $sel.Font.Name="Calibri"; $sel.Font.Size=11; $sel.ParagraphFormat.SpaceAfter=8
    $sel.ParagraphFormat.LeftIndent=18; $sel.ParagraphFormat.FirstLineIndent=-18
    $sel.Font.Bold=$true; $sel.TypeText(([string]$n)+".  "+$m[0]+". ")
    $sel.Font.Bold=$false; $sel.TypeText($m[1])
    $sel.TypeParagraph(); $n++
}
$sel.ParagraphFormat.LeftIndent=0; $sel.ParagraphFormat.FirstLineIndent=0
GoEnd; $sel.TypeParagraph()

# ===== NOTE / COMPARISON =====
Para "For context" 12 $true $navy 4
Para "2025 full-year BD investment was `$44,389 fully-loaded (`$18,151 cash + `$26,238 labour). For comparison, 2026 year-to-date (through 18 Jun) stood at `$26,473 fully-loaded (`$6,886 cash + `$19,587 labour) - so 2026 is tracking broadly in line with 2025 on a half-year basis. As with all BD figures here, labour is salary cost (not burdened), and most BD value is the people-time, not the cash." 11 $false 0 4

$doc.SaveAs([ref]$out,[ref]16)
$doc.Close(); $word.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($sel)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)|Out-Null
[GC]::Collect()
"WROTE: $out"
