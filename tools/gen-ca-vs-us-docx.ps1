$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-Canada-vs-USA-Breakdown-2024-2026.docx"
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
    $tbl.AllowAutoFit=$true
    $tbl.AutoFitBehavior(2)   # 2 = wdAutoFitWindow: scale table to page width, preserving column ratios
    foreach($ac in $amountCols){ for($i=1;$i -le $r;$i++){ $tbl.Cell($i,$ac).Range.ParagraphFormat.Alignment=2 } }
    return $tbl
}

# ===== TITLE =====
Para "Canada vs USA - Business Breakdown" 22 $true $navy 2
Para "KOR Structural    |    Calendar years 2024 - 2026 YTD    |    Source: Deltek Vantagepoint (live)    |    USA figures converted to CAD at 1.36" 9.5 $false $gray 14

# ===== KEY TAKEAWAYS =====
Para "Key takeaways" 13 $true $navy 4
$bullets = @(
  "Canada is the core of the firm - roughly 85-90% of revenue and the overwhelming majority of jobs. In 2025, Canada billed `$4.58M across 439 jobs versus the USA's US`$747K (CA`$1.02M) across 25 jobs.",
  "The US business is small but growing and high-margin. USA revenue rose +39% from 2024 to 2025 (US`$539K to US`$747K) while Canadian revenue fell ~23% over the same span, lifting the USA to ~18% of firm revenue in 2025.",
  "The USA book runs a higher direct contribution margin - about 75% in 2025 versus Canada's ~46%. Caveat: the US book is small, so a few large invoices swing the margin, and revenue is matched to costs by transaction date, not by job.",
  "2026 is a partial year (1 Jan - 18 Jun), so its totals are roughly a half-year and should not be compared head-to-head with full-year 2024 / 2025."
)
foreach($b in $bullets){
    $sel.Font.Name="Calibri"; $sel.Font.Size=11; $sel.ParagraphFormat.SpaceAfter=6
    $sel.ParagraphFormat.LeftIndent=14; $sel.ParagraphFormat.FirstLineIndent=-14
    $sel.Font.Bold=$true; $sel.Font.Color=$navy; $sel.TypeText([char]0x2022 + "  ")
    $sel.Font.Bold=$false; $sel.Font.Color=0; $sel.TypeText($b)
    $sel.TypeParagraph()
}
$sel.ParagraphFormat.LeftIndent=0; $sel.ParagraphFormat.FirstLineIndent=0
GoEnd; $sel.TypeParagraph()

# ===== TABLE 1: REVENUE =====
Para "1.  Revenue (Billed Fee)" 13 $true $navy 6
$t1=@(
  @("Year","Canada (CAD)","USA (CAD)","USA native","USA % of firm"),
  @("2024","`$5,973,430","`$733,643","US`$539,444","10.9%"),
  @("2025","`$4,576,703","`$1,016,421","US`$747,369","18.2%"),
  @("2026 YTD","`$1,880,560","`$239,971","US`$176,449","11.3%")
)
$tbl1=Build-Table $t1 @(90,150,140,140,90) @(2,3,4,5)
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===== TABLE 2: JOBS =====
Para "2.  Jobs billed (distinct projects invoiced in the year)" 13 $true $navy 6
$t2=@(
  @("Year","Canada","USA"),
  @("2024","463","24"),
  @("2025","439","25"),
  @("2026 YTD","241","14")
)
$tbl2=Build-Table $t2 @(140,160,160) @(2,3)
GoEnd; $sel.TypeParagraph()
Para "Currently active jobs (live, point-in-time): Canada 649  |  USA 56  (plus 1 in KOR BC Hold Co.)." 10.5 $false $gray 10

# ===== TABLE 3: LABOUR =====
Para "3.  Direct-job labour (hours and cost of time logged to jobs)" 13 $true $navy 6
$t3=@(
  @("Year","CA hours","CA labour (CAD)","USA hours","USA labour (CAD)","USA native"),
  @("2024","50,312","`$2,744,477","4,342","`$236,893","US`$174,186"),
  @("2025","39,375","`$2,209,340","3,514","`$193,679","US`$142,411"),
  @("2026 YTD","15,789","`$903,832","1,453","`$81,181","US`$59,692")
)
$tbl3=Build-Table $t3 @(80,90,135,80,130,110) @(2,3,4,5,6)
GoEnd; $sel.TypeParagraph(); $sel.TypeParagraph()

# ===== TABLE 4: CONTRIBUTION =====
Para "4.  Direct contribution (Revenue minus job labour minus job expenses)" 13 $true $navy 6
$t4=@(
  @("Year","Canada contribution","CA margin","USA contribution (CAD)","USA margin"),
  @("2024","`$2,930,940","49.1%","`$425,902","58.1%"),
  @("2025","`$2,120,315","46.3%","`$766,654","75.4%"),
  @("2026 YTD","`$867,512","46.1%","`$121,553","50.7%")
)
$tbl4=Build-Table $t4 @(90,160,90,170,90) @(2,3,4,5)
GoEnd; $sel.TypeParagraph()
Para "Direct contribution = Billed Fee minus direct-job labour cost minus direct-job expenses (accounts 5xxx + 6xxx). It is a gross contribution by geography; it excludes firm overhead (indirect labour, rent, software, BD), so it is NOT net profit." 10.5 $false $gray 12

# ===== METHODOLOGY =====
Para "How this was calculated" 13 $true $navy 6
$method=@(
  @("Company structure","KOR runs three Deltek companies (PR.Org): CAD = KOR CAD Partnership (the core practice), USA = KOR USA Corp. (the separate US GL company), and BCC = KOR BC Hold Co. (a holding entity with no project billing or labour in this period, excluded here). 'Jobs' are bucketed by the owning company on each project."),
  @("Currency","Deltek stores each company's transactions in its own currency - USA amounts are in USD. For comparison, USA figures are converted to CAD at the firm's standard rate of 1.36 (config Financials.Billed.UsdToCadRate). Native USD is shown alongside so nothing is hidden. Margins are currency-independent."),
  @("Revenue","Billed Fee = SUM(-LedgerAR.Amount) where TransType = 'IN' and account is one of 4001 / 4003 / 4210 / 4220 / 4240 (KOR's canonical revenue accounts per Daler), bucketed by Org and calendar year. Intercompany account 4260 is excluded."),
  @("Labour","Direct-job labour = SUM(RegAmt + OvtAmt + SpecialOvtAmt) and hours = SUM(RegHrs + OvtHrs + SpecialOvtHrs) from tkDetail, for time charged to regular (ChargeType R) projects, joined to PR for the owning Org. Rejected timesheet lines are excluded. This is salary cost, not a burdened rate."),
  @("Contribution","Revenue minus direct-job labour minus direct-job expenses (ledger accounts 5xxx direct + 6xxx reimbursable). It is matched by transaction date within each calendar year, not job-by-job, so single-year margins - especially for the small US book - can be distorted by the timing of billing versus when the work was done."),
  @("Timeframe","All figures pulled live (real-time ledgers + timesheet), not the lagged GL P&L. 2026 covers 1 Jan - 18 Jun only.")
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
GoEnd

$doc.SaveAs([ref]$out,[ref]16)
$doc.Close(); $word.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($sel)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)|Out-Null
[GC]::Collect()
"WROTE: $out"
