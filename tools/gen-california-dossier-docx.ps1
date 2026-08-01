$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-California-Dossier-2026-06-19.docx"
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
Para "KOR Structural - California Dossier" 21 $true $navy 2
Para "Executive top-sheet over the full CA library    |    KOR Structural - Business Development    |    19 Jun 2026    |    INTERNAL" 9.5 $false $gray 12

# ===== BOTTOM LINE =====
Para "Bottom line" 13 $true $navy 4
Bullets @(
  "KOR is a San Diego structural incumbent with a thin-but-real foothold across the rest of California, and a defensible identity no other firm in the state combines: a Vancouver-rooted, seismic-led, woodframe-fluent SE that follows Vancouver developers into CA and out-competes capacity-stretched incumbents on responsiveness. Growth is developer/architect-relationship-led - ride warm relationships into open seats, not cold pursuit - anchored by 9 tracked SE seats + 8 verified decision-makers already in hand. Near-term revenue: SoCal (SD incumbency + OC's open-seat glut + the LA retrofit lane). NorCal is a patient relationship build led by Holland."
)
GoEnd; $sel.TypeParagraph()

# ===== 1 FOOTPRINT =====
Para "1.  The verified footprint (Deltek spine)" 13 $true $navy 6
Bullets @(
  "San Diego is the franchise - ~117 of 127 CA projects; deep downtown high-rise incumbency (Bosa, JWDA, Holland, Murfey, Cast).",
  "LA is near-greenfield (~10 projects, recently active: 495 Hartford / Intergulf, 674 Catalina).",
  "Orange County ~ 0; NorCal ~ 2 (Bosa SF dormant + DBRDS Sacramento) - relationship-genealogy markets, not project bases.",
  "Licensure is NOT the constraint - KOR holds CA SE licensure (John Markulin, PE/SE) and is SEoR on completed CA towers (The Lindley, 6th & Palm). The constraint is depth + DSA/HCAI track record, not entry."
)
GoEnd; $sel.TypeParagraph()

# ===== 2 COMPETITIVE STORY =====
Para "2.  The competitive story" 13 $true $navy 6
Para "A fellow Vancouver SE - Glotman Simpson - is riding KOR's own Vancouver-developer channel into CA (holds Bosa Pacific Gate / Andia SD, MVE Ritz-Carlton Newport, Westbank San Jose). But KOR is not displaced - it's a 20-year Bosa co-incumbent (Evolution / Calgary, Halifax & Willingdon / Burnaby, The Grande SD). The two incumbents KOR contests are both capacity-stretched: Glotman (90 staff spread across Senakw 6,000u + Brentwood 4,200u + CA) and Nelson Structural (11-person SoCal shop running a `$500M+ NorCal masterplan on a co-SE crutch). KOR's wedge everywhere = the dedicated, principal-led, available alternative." 11 $false 0 10

# ===== 3 METRO BOARD =====
Para "3.  The five-metro board" 13 $true $navy 6
$t3=@(
  @("Metro","KOR position","The #1 play"),
  @("San Diego","Incumbent","Bosa as 20-yr co-incumbent (1st & Island restart 2027-28) + convert Kennedy Wilson on the Lindley credential"),
  @("Orange County","Greenfield, biggest open inventory","MVE flagship displacement (Ritz-Carlton, via McLarand) + the open-seat glut (OC Vibe, Bolsa Pacific, Related Bristol)"),
  @("Los Angeles","Cold, patient","Onni re-engagement + the owner-direct NDC seismic-retrofit lane (near-term revenue)"),
  @("Bay Area","Relationship build","Holland / 3896 Stevens Creek via John Wayland (warm off SD; beat stretched Nelson)"),
  @("Sacramento","Single live thread","DBRDS / Craig Howard - verify-first (thin pipeline; he's architect, not SE-picker)")
)
$tbl3=Build-Table $t3 @(95,150,295)
GoEnd; $sel.TypeParagraph()

# ===== 4 LANES =====
Para "4.  Cross-cutting opportunity lanes" 13 $true $navy 6
Bullets @(
  "NDC seismic-retrofit mid-market - best owner-direct lane. LA + West Hollywood + Santa Monica active mandates where the SE is prime-of-record; target the 4-12 story concrete mid-market the big incumbents skip. `$75-500K/engagement; Year-10 deadlines 2026-2028.",
  "SB 79 (eff. Jul 2026) + mass-timber Type IV - the growth pipeline. SB 79's 85-ft by-right transit band across all 8 counties is KOR's woodframe/hybrid sweet spot. (Reconcile the mass-timber positioning vs. Glotman's claimed LA CLT first-mover before committing.)",
  "San Jose soft-story (3,500 bldgs, Oct 2026 screening) - time-sensitive, unsaturated, co-located with the Holland/Westbank Bay action.",
  "Rule-outs (don't chase as primary): HCAI 2030 hospital seismic (incumbent-gated - Degenkolb / RJC / Saiful / Miyamoto moat); SB 721/326 balcony inspection (past the surge; bundled add-on only)."
)
GoEnd; $sel.TypeParagraph()

# ===== 5 SEATS =====
Para "5.  The actionable core - 9 tracked SE seats (live in KorOpportunitiesDb, verified 19 Jun 2026)" 13 $true $navy 6
$t5=@(
  @("Seat","Metro","SE status"),
  @("Amara Bay (Chula Vista Bayfront)","San Diego","OPEN (Pacifica / AVRP)"),
  @("5757 Uplander Way (Fox Hills)","Culver City (LA)","OPEN (Link Logistics / KFA)"),
  @("3896 Stevens Creek Blvd","San Jose","OPEN (Holland - #1 NorCal)"),
  @("2200 Calle De Luna","Santa Clara","OPEN"),
  @("2518 Mission College Blvd","Santa Clara","OPEN (Irvine Co. / MVE)"),
  @("300 S First St","San Jose","OPEN"),
  @("OC Vibe Residential","Anaheim","held - John A. Martin & Assoc."),
  @("Ritz-Carlton Residences","Newport Beach","held - Glotman (displacement target)"),
  @("1st & Island","San Diego","held - Glotman (Bosa restart 2027-28)")
)
$tbl5=Build-Table $t5 @(210,120,210)
for($i=2;$i -le 7;$i++){ $tbl5.Rows.Item($i).Range.Font.Bold=$true; $tbl5.Cell($i,3).Shading.BackgroundPatternColor=$accentfill }
GoEnd; $sel.TypeParagraph()
Para "Highlighted: 6 genuinely-open seats. The three 'held' rows are displacement/relationship targets." 10 $false $gray 12

# ===== 6 CONTACTS =====
Para "6.  8 verified decision-makers (emails Hunter/Apollo-verified, all live)" 13 $true $navy 6
$t6=@(
  @("Contact","Role","Email","Unlocks"),
  @("John Wayland","Holland EMD, Dev NorCal","jwayland@hollandpartnergroup.com","Stevens Creek (#1 NorCal)"),
  @("Matthew McLarand","MVE President (Jim's contact)","mmclarand@mve-architects.com","OC Vibe, Ritz-Carlton, Mission College"),
  @("Michael Eadie","Kennedy Wilson, Development","meadie@kennedywilson.com","the Lindley -> KW conversion"),
  @("John McCullough","Kennedy Wilson, Multifamily","jmccullough@kennedywilson.com","KW conversion"),
  @("Pablo Collin","AVRP Assoc. Principal","pcollin@avrpstudios.com","Amara Bay"),
  @("Ryan Bosa","Bosa President","rbosa@bosadevelopment.com","1st & Island / the Bosa relationship"),
  @("Chase Ronge","MVE SD Director","cronge@mve-architects.com","MVE San Diego work"),
  @("Craig Howard","DBRDS Sacramento","craig@dbrds.com","the NorCal verify-first thread")
)
$tbl6=Build-Table $t6 @(95,140,175,130)
GoEnd; $sel.TypeParagraph()

# ===== 7 PLAY =====
Para "7.  The play (priority order)" 13 $true $navy 6
NumberedList @(
  @("SD - bank the incumbency.","Lead Bosa with KOR's own 20-yr history (1st & Island restart); convert Kennedy Wilson off the Lindley credential (Eadie / McCullough)."),
  @("OC - attack the open glut.","MVE flagship displacement (McLarand, Jim's contact) on Ritz-Carlton; chase OC Vibe / Bolsa Pacific."),
  @("NorCal - start the patient build.","Holland / Stevens Creek via John Wayland while Nelson is stretched."),
  @("LA - owner-direct retrofit for near-term cash,","plus Onni re-engagement.")
)
GoEnd; $sel.TypeParagraph()

# ===== 8 LIBRARY =====
Para "8.  The deep library (for detail)" 13 $true $navy 6
Bullets @(
  "Statewide synthesis: bd-ca-statewide-synthesis-2026-06-18.md",
  "Per-metro: bd-ca-{sd,la,norcal}-synthesis-2026-06-17.md",
  "Competitors: bd-ca-competitor-matrix + bd-ca-competitor-playbooks",
  "Field/pursuit: bd-ca-field-guide, bd-ca-pursuit-packs, california-call-sheet-matrix",
  "Demand maps: bd-ca-seismic-se-demand-map, bd-ca-architect-se-map",
  "Raw reports: ca-research-raw/*"
)

$doc.SaveAs([ref]$out, [ref]16)
$doc.Close(); $word.Quit()
"WROTE: $out"
