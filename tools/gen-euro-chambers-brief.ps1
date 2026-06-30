$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-EuroChambers-BBQ-Brief-2026-06-19.docx"
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
Para "Euro-Chambers Meet-up & Annual Summer BBQ" 21 $true $navy 2
Para "BD event brief    |    KOR Structural    |    Friday 19 June 2026, 6:00-8:30 PM    |    Jericho Beach Park, Vancouver" 9.5 $false $gray 14

# ===== AT A GLANCE =====
Para "At a glance" 13 $true $navy 6
$t1=@(
  @("Detail","Information"),
  @("Event","Euro-Chambers Meet-up & Annual Summer BBQ (the Dutch Business Club's annual BBQ, this year expanded into the inaugural multi-chamber Euro-Chambers meet-up)"),
  @("When","Friday 19 June 2026, 6:00-8:30 PM"),
  @("Where","Jericho Pond, Jericho Beach Park - 3941 Point Grey Road, Vancouver, V6R 1B5 (outdoor / casual)"),
  @("Cost","CAD `$25 per person (general admission)"),
  @("Host","The Dutch Business Club (DBC)"),
  @("Format","Casual BBQ - grilled food, salads, drinks; each participating chamber introduces itself during the evening"),
  @("Audience","Vancouver's European business community across many industries - SME owners, professionals, chamber members & staff")
)
$tbl1=Build-Table $t1 @(80,440) @()
GoEnd; $sel.TypeParagraph()

# ===== CONFIRMED CO-HOSTS =====
Para "1.  Who is behind it (confirmed co-hosting chambers)" 13 $true $navy 6
Para "This is a six-organization coalition, not a single-chamber event - which is exactly why it is worth being in the room. The participating bodies are:" 11 $false 0 6
Bullets @(
  "Dutch Business Club (DBC) - host and organizer",
  "EU Chamber of Commerce in Canada West (EUCCAN) - the umbrella body for EU bilateral chambers; soft-landing services for EU firms entering BC under CETA",
  "Italian Chamber of Commerce in Canada - West (ICCC)",
  "French Chamber of Commerce and Industry in Canada (CCIFC)",
  "Ireland-Canada Chamber of Commerce Vancouver (ICCVan)",
  "German-Canadian Business Association (GCBA)"
)
Para "Because it is the inaugural combined Euro-Chambers meet-up, the network is forming now - early relationships with these six organizers carry more weight than they would at an established event." 11 $false 0 8
GoEnd

# ===== KOR ANGLE =====
Para "2.  KOR's angle - why this room matters" 13 $true $navy 6
Para "This is a relationship and referral channel, not a live RFP. The goal is to be the structural engineer the European business network already knows - so KOR is top of mind when a European-linked developer, contractor or design firm starts a BC project or lands here under CETA. Two of the six chambers connect straight into Vancouver's construction and development world, and the others map cleanly onto KOR's technical strengths:" 11 $false 0 8
NumberedList @(
  @("Italian & Irish chambers = the construction/development diaspora","Vancouver's Italian-Canadian community runs deep in concrete, formwork, trades and development, and the Irish community is heavily represented among the city's contractors, developers and project managers. These two are the most direct prospect pools in the room - plus Italian heritage/stone work aligns with KOR's heritage lane."),
  @("German (GCBA) = mass timber & Passive House","Mass timber/CLT is German/Austrian/Nordic in origin and BC is its North-American proving ground; Passive House is a German standard City of Vancouver actively pushes. Structural engineering is central to both - KOR's strongest technical opener with the German contingent."),
  @("Dutch (host) = sustainable & prefab building","The Netherlands is strong in engineering, prefab/modular and sustainable construction; Dutch developers and architects work internationally. Thank the host and build the relationship - they convene the whole group."),
  @("French (CCIFC) = infrastructure & institutional","French firms skew toward infrastructure, institutional and hospitality work and include large contractors; a useful longer-horizon relationship."),
  @("EUCCAN = the soft-landing gatekeeper","Any EU firm touching real estate, construction or a manufacturing facility in BC needs a local structural engineer who knows the BC code and seismic requirements. Ask EUCCAN to make KOR its structural referral - that is the single highest-leverage ask of the night.")
)
GoEnd; $sel.TypeParagraph()

# ===== WHO TO TARGET =====
Para "3.  Who to target tonight" 13 $true $navy 6
$t2=@(
  @("Target","Why / opening angle"),
  @("EUCCAN staff","Gatekeeper to the EU soft-landing pipeline. Ask to be the structural-engineering referral for EU firms entering BC."),
  @("Italian Chamber (ICCC) members","Construction, concrete/formwork, development, heritage & stone - direct prospects and KOR's heritage lane."),
  @("Irish Chamber (ICCVan) members","Contractors, developers, project managers - one of the strongest construction-prospect pools in the room."),
  @("German (GCBA) members","Mass timber, Passive House, prefab, engineering precision - KOR's strongest technical common ground."),
  @("Dutch Business Club host & board","Thank them; they convene the network. Sustainable/prefab building angle."),
  @("Any developer / contractor / real-estate owner","The direct prospects - European-linked developers and investors active in Metro Vancouver residential & mixed-use.")
)
$tbl2=Build-Table $t2 @(180,340) @()
GoEnd; $sel.TypeParagraph()

# ===== SPONSORSHIP / ROI =====
Para "4.  Sponsorship & ROI read" 13 $true $navy 6
Bullets @(
  "Tonight is near-zero-risk: `$25 and two and a half hours. Judge it on quality contacts (aim for 3-5) and follow-ups booked, not deals - the payoff is pipeline and referrals.",
  "Because it is the inaugural Euro-Chambers meet-up, there is a first-mover opportunity: offer to host or speak at a future Euro-Chambers session (a short technical talk on mass timber or BC seismic) - that puts KOR in front of all six chambers as the structural expert, far stronger than a logo on a BBQ.",
  "If specific chambers prove valuable, price a membership in the one or two best-fit (Italian and/or German, given construction-diaspora and timber overlap) for recurring access, directories and speaking slots - usually a few hundred dollars a year."
)
GoEnd; $sel.TypeParagraph()

# ===== TONIGHT CHECKLIST =====
Para "5.  Tonight - practical checklist" 13 $true $navy 6
Bullets @(
  "Smart-casual; outdoor BBQ by the water at Jericho. Bring business cards.",
  "Goal: 3-5 quality conversations, not a pitch. Lead with curiosity about their business, then the relevant KOR hook (construction/development, mass timber, Passive House, seismic).",
  "Catch each chamber's intro to clock who is who, then seek out the Italian, Irish and German reps and the EUCCAN staff.",
  "One-line KOR intro: 'KOR Structural - Vancouver structural engineers; we do a lot of developer residential plus heritage and seismic work across BC, and we work on mass-timber and Passive House projects.'",
  "The night's single best ask: EUCCAN referral relationship + an offer to give a future Euro-Chambers technical talk.",
  "Follow up within 48 hours: LinkedIn connection + a short note, and send the KOR capability one-pager to anyone worth pursuing."
)
GoEnd; $sel.TypeParagraph()

# ===== KEY CONTACTS (own page) =====
$sel.InsertBreak(7)   # 7 = wdPageBreak
Para "6.  Key contacts to approach" 13 $true $navy 6
Para "KOR has no prior relationship with these chambers or their leaders on file (checked against Deltek), so treat tonight as net-new. Names and titles are from the chambers' public listings - glance at name tags to confirm, as boards rotate." 11 $false 0 8
$tc=@(
  @("Contact","Role","Why approach them"),
  @("Vasileios Tsianos","EUCCAN President (Neo Performance Solutions)","THE ask of the night: get KOR named as EUCCAN's structural-engineering referral for EU firms entering BC."),
  @("Cathy Murphy","EUCCAN Vice-President & Ireland-Canada Chamber","Two-for-one connector - bridges EUCCAN and the Irish construction crowd. Easiest warm path into both."),
  @("Andrea Basche","German-Canadian Business Assoc. (GCBA) President","40-year career that began in German real estate - ideal first conversation (real estate + mass timber + Passive House)."),
  @("Jens Schuster","GCBA Director, Business Development & Sponsorship","The contact for KOR speaking at or co-hosting a future Euro-Chambers technical talk."),
  @("Ilaria Baldan","Italian Chamber (ICCC West) Executive Director","Gateway to Vancouver's Italian-Canadian construction & development community."),
  @("Celso Boscariol","Italian Chamber President (Vancouver lawyer)","Well-connected local; heritage & development ties (confirm title on the night)."),
  @("Dutch Business Club board","Siekman, Smit, van Engelen, Kalwij, Weidema, de Rover","Hosts - thank them; they convene the network and can introduce you around."),
  @("Marie-Claire Howard","CCI France Western Canada (VP Finance; Park Board Commissioner)","French infrastructure / institutional angle; prominent francophone connector.")
)
$tbc=Build-Table $tc @(135,175,210) @()
GoEnd; $sel.TypeParagraph()
$sel.Font.Name="Calibri"; $sel.Font.Size=11; $sel.ParagraphFormat.SpaceAfter=8
$sel.Font.Bold=$true; $sel.Font.Color=$navy; $sel.TypeText("If you only get to three: ")
$sel.Font.Bold=$false; $sel.Font.Color=0
$sel.TypeText("Tsianos (the referral ask) -> Andrea Basche (best sector + technical fit) -> Cathy Murphy (the connector into EUCCAN and the Irish chamber).")
$sel.TypeParagraph()
Para "Chamber contact details: EUCCAN - office@eu-canada.com / 604-559-1008 (409 Granville St #1209).  Italian Chamber - iccbc@iccbc.com / 604-682-1410 (889 W Pender St #703).  GCBA - secretary@mygcba.com.  Dutch Business Club / Ireland-Canada / French chambers - via dutchbusinessclub.ca, icccvan.ca, ccifwesterncanada.com." 10.5 $false $gray 12

Para "Sources" 10 $true $navy 4
Para "Eventbrite and allevents.in listings for 'Euro-Chambers Meet-up & Annual Summer BBQ' (Fri 19 Jun 2026, 6:00-8:30 PM, Jericho Pond / 3941 Point Grey Rd, `$25); Dutch Business Club (dutchbusinessclub.ca); EUCCAN / EU Chamber of Commerce in Canada West (eu-canada.com, euccan.com). Confirmed co-hosts per the event listing: DBC, EUCCAN, Italian Chamber (ICCC), French Chamber (CCIFC), Ireland-Canada Chamber (ICCVan), German-Canadian Business Association (GCBA). Contact names from chamber public listings: EUCCAN board (euccan.com/board-of-directors), GCBA 2025 board (mygcba.com), Italian Chamber (iccbc.com), Dutch Business Club (dutchbusinessclub.ca), CCI France Western Canada (ccifwesterncanada.com). KOR-side relationship check run against Deltek (no prior relationship found). Compiled 19 Jun 2026." 9 $false $gray 4

$doc.SaveAs([ref]$out,[ref]16)
$doc.Close(); $word.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($sel)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc)|Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)|Out-Null
[GC]::Collect()
"WROTE: $out"
