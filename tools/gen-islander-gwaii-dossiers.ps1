$ErrorActionPreference = "Stop"
# Generates 3 branded DOCX: Gwaii dossier, Islander dossier, and a 1-page exec teaming brief.
$navy=6299648; $gray=7697781; $altfill=16382457; $white=16777215; $insideclr=13684944; $accent=2970272

$word=New-Object -ComObject Word.Application; $word.Visible=$false

function New-Doc {
    $doc=$word.Documents.Add()
    $doc.PageSetup.TopMargin=54; $doc.PageSetup.BottomMargin=54
    $doc.PageSetup.LeftMargin=54; $doc.PageSetup.RightMargin=54
    return $doc
}
function Para($sel,[string]$text,[single]$size,[bool]$bold,[int]$color,[single]$after){
    $sel.Font.Name="Calibri"; $sel.Font.Size=$size; $sel.Font.Bold=$bold; $sel.Font.Color=$color
    $sel.ParagraphFormat.SpaceAfter=$after; $sel.ParagraphFormat.SpaceBefore=0
    $sel.TypeText($text); $sel.TypeParagraph()
    $sel.Font.Bold=$false; $sel.Font.Color=0
}
function Eyebrow($sel,$text){ Para $sel $text 8 $true $gray 2 }
function H1($sel,$text){ Para $sel $text 20 $true $navy 2 }
function H2($sel,$text){
    $sel.ParagraphFormat.SpaceBefore=5
    Para $sel $text 12 $true $navy 3
}
function Body($sel,$text){ Para $sel $text 10.5 $false 0 5 }
function Bullets($sel,$items){
    foreach($b in $items){
        $sel.Font.Name="Calibri"; $sel.Font.Size=10; $sel.ParagraphFormat.SpaceAfter=4
        $sel.ParagraphFormat.LeftIndent=14; $sel.ParagraphFormat.FirstLineIndent=-14
        $sel.Font.Bold=$true; $sel.Font.Color=$navy; $sel.TypeText([char]0x2022+"  ")
        $sel.Font.Bold=$false; $sel.Font.Color=0; $sel.TypeText($b)
        $sel.TypeParagraph()
    }
    $sel.ParagraphFormat.LeftIndent=0; $sel.ParagraphFormat.FirstLineIndent=0
}
function ContactTable($doc,$sel,$rows){
    $r=$rows.Count; $c=3
    $tbl=$doc.Tables.Add($sel.Range,$r,$c)
    $tbl.Borders.Enable=$true; $tbl.Borders.OutsideColor=$navy; $tbl.Borders.InsideColor=$insideclr
    $tbl.Range.Font.Name="Calibri"; $tbl.Range.Font.Size=10
    $tbl.Rows.Item(1).Range.Font.Bold=$true; $tbl.Rows.Item(1).Range.Font.Color=$white
    $tbl.Rows.Item(1).Shading.BackgroundPatternColor=$navy
    for($i=0;$i -lt $r;$i++){ for($j=0;$j -lt $c;$j++){
        $cell=$tbl.Cell($i+1,$j+1); $cell.Range.Text=[string]$rows[$i][$j]; $cell.VerticalAlignment=1
        if($i -gt 0 -and ($i % 2 -eq 0)){ $cell.Shading.BackgroundPatternColor=$altfill }
    }}
    $tbl.Columns.Item(1).Width=120; $tbl.Columns.Item(2).Width=210; $tbl.Columns.Item(3).Width=160
    $sel.EndKey(6) | Out-Null; $sel.TypeParagraph()
}
function Footer($doc){
    $f=$doc.Sections.Item(1).Footers.Item(1).Range
    $f.Text="KOR Structural  |  Confidential / Internal  |  Prepared 2026-06-23"
    $f.Font.Name="Calibri"; $f.Font.Size=8; $f.Font.Italic=$true; $f.Font.Color=$gray
}

$sharedClients = "BC Housing (KOR tracks 50 projects as owner), Cowichan Tribes, Malahat Nation, Tsawout, Tsawwassen, Songhees, Esquimalt, Ditidaht, Ucluelet First Nation, Haida Nation, Tla'amin, Tsain-Ko, Comox Valley / Strathcona, and School Districts 50 (Haida Gwaii), 71 (Comox) and 79 (Cowichan) - all currently in KOR's pursuit graph with zero KOR projects won (open targets)."

# ===================== 1. GWAII DOSSIER =====================
$doc=New-Doc; $sel=$word.Selection
Eyebrow $sel "COMPANY DOSSIER  -  TEAMING TARGET (CIVIL)"
H1 $sel "Gwaii Engineering Ltd."
Para $sel "Indigenous-owned civil & environmental engineering  -  Victoria, BC  -  gwaiieng.com" 10 $true $gray 8
Body $sel "Founded March 2017 by Corey Brown (Sta'staas Eagle Clan, Old Masset, Haida Gwaii). Indigenous-owned and -operated; CCAB / CCIB-certified (Gold). Grown from 5 to 20+ technologists, engineers and environmental scientists. Mission centres on building capacity within First Nation communities across Vancouver Island and BC."
H2 $sel "What they do"
Bullets $sel @(
 "Civil & environmental engineering; water / wastewater; project & construction management.",
 "Planning, funding & finance advisory, and Indigenous procurement strategy.",
 "Sustainable energy (biofuel, solar, green energy), community energy retrofits, hazmat / mould surveys.")
H2 $sel "Leadership & verified contacts"
ContactTable $doc $sel @(
 @("Name","Title","Email (verified)"),
 @("Corey Brown","Managing Director & Principal Civil Engineer (Founder)","cbrown@gwaiieng.com"),
 @("Mike Achtem","Principal, Senior Engineer","machtem@gwaiieng.com"),
 @("Brandon Ducharme","Director, Project Delivery & Capital Advisory","bducharme@gwaiieng.com"),
 @("Greg Gillespie","Senior Development Manager","ggillespie@gwaiieng.com"),
 @("Jared Smylie","Engineering staff (also at Islander)","jsmylie@gwaiieng.com"))
H2 $sel "Portfolio & client base"
Body $sel "Incumbent civil engineer to 25+ First Nations across coastal BC and Vancouver Island - Tla'amin, Ucluelet, Malahat, Cowichan Tribes, Ditidaht, Haida Nation, Kitasoo, K'omoks, Tsawout, Tsawwassen, Songhees, Esquimalt - plus BC Housing. Representative projects: Yuu-thlu-ilth-aht (Ucluelet) Government Lift Station, Tsawout First Nation Big House, Malahat Nation Marina & Boat Launch, Reay Creek Remediation, and 'Our House of Clans' (BC Housing & Tsain-Ko)."
H2 $sel "KOR relationship & relationships in common"
Bullets $sel @(
 "No prior KOR contractual history - not in Deltek as a client or vendor; no Gwaii contact on file. This is a NET-NEW teaming relationship, not a renewal.",
 "Sibling firm to Islander Engineering: founder Corey Brown co-founded Islander (2016) then Gwaii (2017); the firms share senior staff (Mike Achtem, Jared Smylie appear at both).",
 "Heavy overlap with KOR's own pursuit list: KOR already tracks nearly all of Gwaii's clients as open Buyer/Developer targets - " + $sharedClients)
H2 $sel "Talking points for Rory"
Bullets $sel @(
 "Lead with complement, not competition: KOR is structural, Gwaii is civil/environmental - the disciplines pair on the same teams and never bid against each other.",
 "Indigenous-participation angle: a KOR structural + Gwaii civil team earns CCAB / Indigenous-participation scoring on public RFQs, where Gwaii is already the incumbent civil engineer and KOR supplies structural depth Gwaii does not hold in-house.",
 "Concrete opener to Corey Brown (cbrown@gwaiieng.com) or Brandon Ducharme (Capital Advisory): 'KOR has a coastal-BC / Vancouver Island structural portfolio and wants to support Indigenous-led infrastructure - which of your upcoming Nation or BC Housing projects need a structural partner?'",
 "LIVE TRIGGERS - lead with a named project KOR is already pursuing on Gwaii-client land: Duncan 'River's Edge' (Cowichan Tribes + BC Housing), Saanichton (Tsawout First Nation, 7593 Tetayut Rd), the Vancouver Island BC Housing pursuits (Nanaimo, Saanich, Campbell River), or Penticton Skaha Assembly Redevelopment (PURSUE_URGENT). Ask whether Gwaii is already engaged and wants a structural partner.")
Footer $doc
$gwaiiPath="C:\VIsual Studio Projects\Operations\docs\KOR-Gwaii-Engineering-Dossier-2026-06-23.docx"
if(Test-Path $gwaiiPath){Remove-Item $gwaiiPath -Force}
$doc.SaveAs([ref]$gwaiiPath,[ref]16); $doc.Close()

# ===================== 2. ISLANDER DOSSIER =====================
$doc=New-Doc; $sel=$word.Selection
Eyebrow $sel "COMPANY DOSSIER  -  TEAMING TARGET (CIVIL)"
H1 $sel "Islander Engineering Ltd."
Para $sel "Civil engineering, clean energy & land development  -  Victoria, BC  -  islanderengineering.com" 10 $true $gray 8
Body $sel "Founded 2016; approximately 11-50 staff at 2031 Store St, Victoria. CEO & co-founder Josh Bartley, P.Eng.; co-founder Corey Brown, P.Eng. (now Managing Director of sibling firm Gwaii Engineering). Turnkey civil practice from feasibility and zoning through detailed design and construction, with a sustainability focus."
H2 $sel "What they do"
Bullets $sel @(
 "Municipal infrastructure, land development, missing-middle housing.",
 "Clean energy & carbon solutions, blue / waste-to-energy initiatives.",
 "Engineering surveys; feasibility through construction project management.")
H2 $sel "Leadership & verified contacts"
ContactTable $doc $sel @(
 @("Name","Title","Email (verified)"),
 @("Josh Bartley","CEO & Co-Founder, P.Eng.","jbartley@islanderengineering.com"),
 @("Corey Brown","Co-Founder, P.Eng. (now MD at Gwaii)","cbrown@islanderengineering.com"),
 @("Davide Cuzner","Engineer","dcuzner@islanderengineering.com"),
 @("Mike Achtem","Principal (also at Gwaii)","machtem@islanderengineering.com"),
 @("Jared Smylie","Engineering staff (also at Gwaii)","jsmylie@islanderengineering.com"))
H2 $sel "KOR relationship & relationships in common"
Bullets $sel @(
 "No prior KOR contractual history (not in Deltek; no contact on file) - net-new teaming relationship.",
 "Sibling firm to Gwaii Engineering - shared founder Corey Brown plus shared staff (Achtem, Smylie). Treat Islander + Gwaii as one relationship cluster centred on Corey Brown.",
 "DATA NOTE: Islander was mis-classified as a 'Competitor' in KOR's graph. It is a CIVIL firm and a teaming partner, not a structural rival - re-kind to Vendor/partner recommended (it was distorting the competitor-footprint report).")
H2 $sel "Talking points for Rory"
Bullets $sel @(
 "Complementary disciplines: KOR structural + Islander civil on Vancouver Island land development, missing-middle and municipal work where Islander leads the civil scope.",
 "Opener to Josh Bartley (jbartley@islanderengineering.com): complementary structural capacity for Islander's land-development pipeline.",
 "One conversation covers two firms - reference the Gwaii relationship via Corey Brown.")
Footer $doc
$islPath="C:\VIsual Studio Projects\Operations\docs\KOR-Islander-Engineering-Dossier-2026-06-23.docx"
if(Test-Path $islPath){Remove-Item $islPath -Force}
$doc.SaveAs([ref]$islPath,[ref]16); $doc.Close()

# ===================== 3. EXEC ONE-PAGER =====================
$doc=New-Doc; $sel=$word.Selection
Eyebrow $sel "EXECUTIVE TEAMING BRIEF  -  ONE PAGE"
H1 $sel "Islander + Gwaii Engineering"
Para $sel "Two linked Victoria civil firms - and a clean lane for KOR on Vancouver Island & Indigenous work" 10.5 $true $gray 8
H2 $sel "The relationship cluster"
Body $sel "Islander Engineering (civil / land-dev, est. 2016) and Gwaii Engineering (Indigenous-owned civil & environmental, est. 2017) are sibling firms: Corey Brown co-founded both, and senior staff (Mike Achtem, Jared Smylie) appear at each. One relationship - Corey Brown - opens both doors. Neither firm has any prior KOR contractual history (confirmed absent from Deltek): this is net-new white space."
H2 $sel "Why it matters to KOR"
Bullets $sel @(
 "Complementary, non-competing: KOR is structural; both firms are civil/environmental. They pair on the same prime-consultant teams and never bid against KOR.",
 "Indigenous participation: Gwaii is CCAB-certified and Indigenous-owned - a KOR+Gwaii team scores Indigenous-participation credit on the public RFQs that increasingly require it.",
 "Incumbency KOR lacks: Gwaii is the embedded civil engineer for 25+ First Nations + BC Housing on the coast - exactly the owners KOR is chasing but has not yet won.")
H2 $sel "Shared client overlap (KOR is already pursuing these)"
Body $sel $sharedClients
H2 $sel "Plays for KOR"
Bullets $sel @(
 "PLAY 1 - Indigenous teaming: propose KOR structural + Gwaii civil on First Nations / BC Housing pursuits where Gwaii is incumbent. KOR supplies structural depth; team gains CCAB scoring + Gwaii's warm client access.",
 "PLAY 2 - Vancouver Island land-dev: KOR structural capacity for Islander's land-development & missing-middle pipeline.",
 "PLAY 3 - One entry, two firms: build the relationship through Corey Brown; let it branch to Gwaii (Nation/BC-Housing) and Islander (land-dev).",
 "PLAY 4 - Strike on the live triggers: KOR is ALREADY pursuing projects on Gwaii-client land - Duncan 'River's Edge' (Cowichan Tribes + BC Housing), Saanichton Tsawout First Nation, Vancouver Island BC Housing (Nanaimo/Saanich/Campbell River), Penticton Skaha (PURSUE_URGENT). Bring Gwaii into these specific pursuits now.")
H2 $sel "Priority contacts"
ContactTable $doc $sel @(
 @("Who","Why","Email"),
 @("Corey Brown","Bridges both firms; MD at Gwaii (Indigenous lead)","cbrown@gwaiieng.com"),
 @("Brandon Ducharme","Gwaii Capital Advisory - knows the project pipeline","bducharme@gwaiieng.com"),
 @("Josh Bartley","Islander CEO - land-dev pipeline","jbartley@islanderengineering.com"))
Footer $doc
$execPath="C:\VIsual Studio Projects\Operations\docs\KOR-Islander-Gwaii-Exec-Teaming-Brief-2026-06-23.docx"
if(Test-Path $execPath){Remove-Item $execPath -Force}
$doc.SaveAs([ref]$execPath,[ref]16); $doc.Close()

$word.Quit()
Write-Host "Generated:"
Write-Host "  $gwaiiPath"
Write-Host "  $islPath"
Write-Host "  $execPath"
foreach($p in @($gwaiiPath,$islPath,$execPath)){ Write-Host ("  {0:N0} bytes  {1}" -f (Get-Item $p).Length, (Split-Path $p -Leaf)) }
