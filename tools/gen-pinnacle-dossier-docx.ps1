$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-Pinnacle-Dossier-2026-06-19.docx"
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
Para "Pinnacle International - BD Dossier" 21 $true $navy 2
Para "Warm KOR client (Deltek CL00333)    |    KOR Structural    |    19 Jun 2026    |    The BC-client-into-San-Diego wedge" 9.5 $false $gray 12

# ===== BOTTOM LINE =====
Para "Bottom line" 13 $true $navy 4
Bullets @(
  "Pinnacle is a warm KOR client, not a prospect - KOR has billed them directly under Deltek CL00333, including Mike De Cotiis's personal residence. The standout angle: KOR has already done Pinnacle work in San Diego (the Museum Site project), and Pinnacle now has two live SD projects - Pacific Heights (492 units / 31 storeys, permits under review, SE seat likely open) and an 11th & E hotel. Of KOR's BC developer client-book, Pinnacle is the single best follow-a-trusted-BC-client-into-California story. The licensing gate (a CA-stamped SE) still applies, but the relationship and intent are already there."
)
GoEnd; $sel.TypeParagraph()

# ===== WHO =====
Para "Who they are" 13 $true $navy 6
Para "Vancouver-based luxury condo / mixed-use developer led by founder Michael (Mike) De Cotiis (President & CEO). High-rise residential across BC (Vancouver, Burnaby, Richmond, North Vancouver), Toronto, and now Southern California. One of KOR's core BC developer relationships - alongside Bosa, Onni, Concord, Wesgroup, Holland and Greystar." 11 $false 0 10

# ===== TRACK RECORD =====
Para "1.  KOR <-> Pinnacle track record (Deltek ground truth - CL00333)" 13 $true $navy 6
$t1=@(
  @("Project","Where","Note"),
  @("Sorrento @ Capstan Village - Phase 1B","Richmond, BC","`$220K billed - KOR's largest Pinnacle engagement; master-planned Richmond community."),
  @("Museum Site Residential Project","San Diego, CA","KOR has already done Pinnacle structural work in San Diego - the CA precedent."),
  @("The Pier / Cascade at the Pier (amenities + rooftop pool)","North Vancouver, BC","Lower Lonsdale waterfront."),
  @("2080 West Broadway (at Maple)","Vancouver, BC","Dormant."),
  @("Mike DeCotiis Residence","-","KOR trusted with the principal's personal home - strongest relationship signal.")
)
$tbl1=Build-Table $t1 @(190,90,260)
$tbl1.Rows.Item(3).Range.Font.Bold=$true
$tbl1.Rows.Item(3).Shading.BackgroundPatternColor=$accentfill
GoEnd; $sel.TypeParagraph()

# ===== PIPELINE =====
Para "2.  Live pipeline (Pinnacle as proponent)" 13 $true $navy 6
$t2=@(
  @("Project","Where","Scope","Status"),
  @("Pinnacle Pacific Heights","San Diego, CA","492 units, 31 storeys (60 affordable)","Permits under review - SE seat likely open"),
  @("11th & E (hotel)","San Diego, CA","Future hotel","Jim-tracked, 2026-06"),
  @("Pinnacle Lougheed","Burnaby, BC","Four towers; 80-storey highrise (JYOM)","Planned"),
  @("601 Beach Crescent Condominium","Vancouver, BC","~`$60M (JYOM)","Planned")
)
$tbl2=Build-Table $t2 @(160,90,180,130)
$tbl2.Rows.Item(2).Range.Font.Bold=$true
$tbl2.Rows.Item(2).Shading.BackgroundPatternColor=$accentfill
GoEnd; $sel.TypeParagraph()
Para "Highlighted: Pinnacle Pacific Heights - a live San Diego tower by a trusted BC client, with no SE publicly named. The cleanest CA beachhead opportunity in the book." 10 $false $gray 12

# ===== CONTACTS =====
Para "3.  Contacts in the graph (Hunter domain-search verified, 19 Jun 2026)" 13 $true $navy 6
Para "Executives" 11 $true 0 4
$t3=@(
  @("Name","Title","Email","Conf"),
  @("Michael (Mike) De Cotiis","President & CEO / Founder / Owner","md@pinnacleinternational.ca","95"),
  @("Anson Kwok","VP, Sales & Marketing","akwok@pinnacleinternational.ca","98"),
  @("John Moy","CFO / VP Finance","jmoy@pinnacleinternational.ca","96")
)
$tbl3=Build-Table $t3 @(150,180,170,40)
$tbl3.Rows.Item(2).Range.Font.Bold=$true
GoEnd; $sel.TypeParagraph()
Para "Construction / project-management team - the people who engage the structural engineer" 11 $true 0 4
$t4=@(
  @("Name","Title","Email","Conf"),
  @("Pascal Yammine","VP Construction","(senior construction lead)","-"),
  @("Luke Griffin","Project Manager","lgriffin@pinnacleinternational.ca","99"),
  @("Chris Eyles","Project Manager","ceyles@pinnacleinternational.ca","97"),
  @("Joe Meola","Project Manager","jmeola@pinnacleinternational.ca","96"),
  @("Alireza Partovi","Project Manager","apartovi@pinnacleinternational.ca","96"),
  @("Matias Gil","Construction Coordinator","mgil@pinnacleinternational.ca","99"),
  @("Daniel Bellows","Site Superintendent","dbellows@pinnacleinternational.ca","97"),
  @("Benny Yeo","Architect (in-house)","byeo@pinnacleinternational.ca","98")
)
$tbl4=Build-Table $t4 @(130,150,200,40)
GoEnd; $sel.TypeParagraph()

# ===== ARCHITECT =====
Para "4.  Architect of record - target for prime-consultant teaming" 13 $true $navy 6
Bullets @(
  "JYOM Architecture (founders Eric Elliott Lai + Kandice Emmie Kwok; offices Vancouver / Seattle / San Francisco / Shanghai / Chengdu / Hong Kong) is Pinnacle's lead design firm on Pinnacle Lougheed (four towers, an 80-storey 'calla lily' highrise - future tallest west of Toronto) and 601 Beach Crescent. Worth being JYOM's named SE on the next Pinnacle pursuit.",
  "On the San Diego towers, probe the JWDA / Joseph Wong Design Associates connection - JWDA is already KOR's San Diego beachhead architect (The Lindley)."
)
GoEnd; $sel.TypeParagraph()

# ===== THE PLAY =====
Para "5.  The play" 13 $true $navy 6
NumberedList @(
  @("It's a relationship, not an intro.","Lead with Sorrento @ Capstan Village, The Pier, and the De Cotiis residence - KOR is already Pinnacle's structural engineer in BC."),
  @("The California wedge.","'We did your Museum Site project in San Diego - let us be your SE on Pacific Heights and 11th & E.' Cleanest BC-client-into-CA story KOR has. Gate: secure a CA-licensed SE to stamp."),
  @("Get on JYOM's team.","Be the named SE for Pinnacle Lougheed / 601 Beach Crescent before the RFP, the prime-consultant way.")
)
GoEnd; $sel.TypeParagraph()

# ===== HYGIENE =====
Para "6.  Graph hygiene done this pass (19 Jun 2026)" 13 $true $navy 6
Bullets @(
  "Merged empty stray org 'Pinnacle Development' (76651) into Pinnacle International (53665) - 0 FK repoints, alias preserved, audited clean.",
  "Corrected Michael De Cotiis email - was Anson Kwok's mis-inferred address; now md@pinnacleinternational.ca (Hunter conf 95).",
  "Nulled two wrong-domain inferred emails (a JYOM JV-string person + a placeholder).",
  "Backfilled verified emails on Anson Kwok + John Moy; added 7 construction/PM contacts with Hunter-verified emails."
)

$doc.SaveAs([ref]$out, [ref]16)
$doc.Close(); $word.Quit()
"WROTE: $out"
