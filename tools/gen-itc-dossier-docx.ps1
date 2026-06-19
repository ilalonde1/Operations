$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-ITC-Collaboration-Dossier-2026-06-19.docx"
if (Test-Path $out) { Remove-Item $out -Force }

$navy=6299648; $gray=7697781; $altfill=16382457; $accentfill=15921906; $white=16777215; $insideclr=13684944

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
function NumberedList($items){
    $n=1
    foreach($m in $items){
        $sel.Font.Name="Calibri"; $sel.Font.Size=11; $sel.ParagraphFormat.SpaceAfter=8
        $sel.ParagraphFormat.LeftIndent=18; $sel.ParagraphFormat.FirstLineIndent=-18
        $sel.Font.Bold=$true; $sel.TypeText(([string]$n)+".  "+$m[0]+"  ")
        $sel.Font.Bold=$false; $sel.TypeText($m[1])
        $sel.TypeParagraph(); $n++
    }
    $sel.ParagraphFormat.LeftIndent=0; $sel.ParagraphFormat.FirstLineIndent=0
}
function Build-Table($rows,$widths){
    $r=$rows.Count; $c=$rows[0].Count
    $tbl=$doc.Tables.Add($sel.Range,$r,$c)
    $tbl.Borders.Enable=$true; $tbl.Borders.OutsideColor=$navy; $tbl.Borders.InsideColor=$insideclr
    $tbl.Range.Font.Name="Calibri"; $tbl.Range.Font.Size=10
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
    return $tbl
}

# ===== TITLE =====
Para "KOR / ITC Construction Group - Collaboration Dossier" 21 $true $navy 2
Para "BC Housing teaming pursuit    |    KOR Structural    |    19 Jun 2026    |    Prepared for the John Markulin to Brad Burnett outreach" 9.5 $false $gray 12

# ===== BOTTOM LINE =====
Para "Bottom line" 13 $true $navy 4
Bullets @(
  "KOR is not cold with ITC - it is a warm, multi-project relationship with two ready inroads. Lead the outreach with the shared Arbutus Village / Burrard Gateway history and KOR's deep Larco book, then ask to be ITC's structural partner on BC Housing bids."
)
GoEnd; $sel.TypeParagraph()

# ===== 1. INROADS =====
Para "1.  Two warm inroads (now in the graph)" 13 $true $navy 6
$t1=@(
  @("KOR contact","ITC contact (email)","Why it matters"),
  @("John Markulin","Brad Burnett - President & CEO  (bburnett@itc-group.com)","The relationship to activate - JM knows him; ITC's top decision-maker."),
  @("Omar Alcazar (Sr SE)","Manit Saiyadh - Construction Manager  (msaiyadh@itc-group.com)","Live thread - took Omar's call re: BC Housing teaming, Jun 2026."),
  @("(engage for bid mechanics)","David Carlton - SVP Preconstruction  (dcarlton@itc-group.com)","Preconstruction assembles the consultant team incl. the structural engineer on bids.")
)
$tbl1=Build-Table $t1 @(120,210,210)
GoEnd; $sel.TypeParagraph()

# ===== 2. SHARED HISTORY =====
Para "2.  Shared track record (Deltek-verified)" 13 $true $navy 6
Bullets @(
  "Billed directly to ITC (Deltek client CL00209): Arbutus Block C & D (basement wall + public-art pedestal) and Burrard Gateway Tower B - KOR was ITC's structural sub.",
  "With Larco Investments (client CL00236): KOR's deepest BC relationship - 186 projects - including the Arbutus Village development (Buildings A, C & D) that ITC is building now. Arbutus Village is the shared ground: Larco (owner) + ITC (GC) + KOR (SE).",
  "Collaboration context already in the graph on Little Mountain (Holborn / BC Housing) and the Burnaby Lake Recreation Centre."
)
GoEnd; $sel.TypeParagraph()

# ===== 3. PROFILE =====
Para "3.  ITC company profile" 13 $true $navy 6
Para "Founded 1983; HQ 564 Beatty St, Vancouver; offices in Calgary, Edmonton, Toronto and Victoria; ~250+ staff. Now owned by Pomerleau (Canada's largest private GC), giving national scale and bonding. 250+ completed projects, `$1M-`$600M. Delivery modes: General Contracting, Construction Management, Design-Build / Design-Assist, Tenant Improvement. In Aug 2025 ITC acquired Farmer Construction (Victoria), expanding onto Vancouver Island (Harris Green Village, 526 units)." 11 $false 0 8
Bullets @(
  "Leadership: Brad Burnett (President & CEO, since 2025) | Doug MacFarlane (elevated to CEO / Vice-Chair, Pomerleau West) | Mathias Graf (SVP Operations) | David Carlton (SVP Preconstruction - the SE-team decision-maker) | Tom Gevatkoff (VP General Superintendent)."
)
GoEnd; $sel.TypeParagraph()

# ===== 4. BC HOUSING PIPELINE =====
Para "4.  ITC's active BC Housing pipeline - the teaming bullseye" 13 $true $navy 6
$t4=@(
  @("Project","Location","Units","Owner / Developer","Status"),
  @("ALT Housing & Healing Centre (Lu'ma)","52-92 E Hastings, Vancouver","111","BC Housing / Terra / ALT","U/C 2026"),
  @("Little Mountain EA","Vancouver","70 non-market","Holborn / BC Housing","U/C 2026"),
  @("Little Mountain AB (Neighbour House)","Vancouver","48 + daycare","Holborn / City of Vancouver","U/C 2026"),
  @("ALT Jackson","401 Jackson Ave, Vancouver","172 social (`$60M)","Lu'ma Native Housing","U/C 2026"),
  @("First United Church Redevelopment","320 E Hastings, Vancouver","100+ below-market","First United / Lu'ma","U/C 2026"),
  @("Arbutus Village Blocks C & D","4255 Arbutus St, Vancouver","266 (KOR = SE)","Larco Developments","U/C 2026"),
  @("Landmark on Robson","740 Nicola St, Vancouver","319 (83 social)","Asia Standard","Done 2024")
)
$tbl4=Build-Table $t4 @(150,120,80,110,55)
$tbl4.Rows.Item(7).Range.Font.Bold=$true
$tbl4.Rows.Item(7).Shading.BackgroundPatternColor=$accentfill
GoEnd; $sel.TypeParagraph()
Para "Highlighted: KOR is already the structural engineer on Arbutus Village Blocks C & D - a live ITC project - the strongest possible opener." 10 $false $gray 12

# ===== 5. SE PROCUREMENT =====
Para "5.  How ITC picks the structural engineer (KOR's entry point)" 13 $true $navy 6
Bullets @(
  "Design-Build / Design-Assist (private developer work - Larco, Tien Sher, Chard): ITC picks the SE as part of its team. Primary entry - be ITC's named SE on DB/DA pursuits before RFQ.",
  "BC Housing / stipulated-sum GC: owner and architect appoint the SE, but ITC's bid team lobbies for familiar subs and informally recommends the SE during pursuit.",
  "No public preferred-SE partner is credited for ITC (no Fast+Epp / Glotman / RJC / Bush Bohlman) - open field. The person to land is David Carlton, SVP Preconstruction."
)
GoEnd; $sel.TypeParagraph()

# ===== 6. THE PITCH =====
Para "6.  The pitch KOR leads with" 13 $true $navy 6
NumberedList @(
  @("Proven team.","'We're your structural engineer on Arbutus Village right now, did Burrard Gateway with you, and we're Larco's SE - let's run that team on BC Housing.'"),
  @("BC Housing fit.","KOR just delivered the Burnaby Lake Recreation Centre (public / institutional) - directly relevant to BC Housing and public-sector bids."),
  @("Value engineering.","Structural cost-optimization to help ITC's bid budgets pencil (Omar's offer)."),
  @("The ask.","Get KOR onto ITC's BC Housing bid teams as the structural partner: JM to Brad Burnett, Omar to Manit Saiyadh, and into David Carlton (SVP Preconstruction) for the bid-team mechanics.")
)
GoEnd; $sel.TypeParagraph()

# ===== 7. DO WE HAVE IT =====
Para "7.  Do we already have this information?" 13 $true $navy 6
Bullets @(
  "Yes - more than expected. KOR's graph already held ITC's senior leadership (Brad Burnett, David Carlton, Mathias Graf, with emails) and already mapped ITC to its parent Pomerleau, from a prior research pass. This dossier added the other seven contacts (Manit Saiyadh + the preconstruction and project directors) and the connective tissue.",
  "The shared-project history was verifiable in Deltek (ITC = client CL00209; Larco = CL00236, 186 projects). The only real gap was pulling it together plus the live ITC pipeline - now closed."
)

$doc.SaveAs([ref]$out, [ref]16)
$doc.Close(); $word.Quit()
"WROTE: $out"