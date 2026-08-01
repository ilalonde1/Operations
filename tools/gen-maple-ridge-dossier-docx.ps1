$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-MapleRidge-Recreation-Dossier-2026-06-19.docx"
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
Para "Maple Ridge 'Recreation Ready' - BD Dossier" 21 $true $navy 2
Para "Hammond Aquatics + Albion Arena + Ballpark    |    KOR Structural - Business Development    |    19 Jun 2026    |    INTERNAL" 9.5 $false $gray 12

# ===== BOTTOM LINE =====
Para "Bottom line" 13 $true $navy 4
Bullets @(
  "A `$393M, referendum-gated municipal program - not a live RFP. HCMA is the prime architect, and their go-to aquatic-centre SE is Fast + Epp (KOR's main civic/timber rival), so the seat is contestable but defended. KOR's hand is stronger than it looks: Deltek shows a real rec/aquatic/arena resume (POCO Rec Centre, Parkinson Kelowna, Burnaby Lake Aquatic, Nelson Arena) - a genuine counter to Fast + Epp. The play is relationship-led positioning over the next 12-18 months: get on HCMA's team (we already hold 23 HCMA contacts incl. the Recreation director) and watch the October 2026 referendum that gates the whole thing. Detailed-design SE selection is ~2027 - there is time, and entry is the constraint, not capability."
)
GoEnd; $sel.TypeParagraph()

# ===== 1 PROGRAM =====
Para "1.  The program (3 projects, `$393M)" 13 $true $navy 6
$t1=@(
  @("Project","Est. cost","Construction start","Notes"),
  @("Hammond Aquatics & Recreation Centre","`$227M","2029","The prize - heavy SE scope"),
  @("Albion Arena Expansion (twin ice rinks)","`$143M","2028","Arena = timber; a parallel SE target"),
  @("Golf Course -> Ballpark (Phase 1)","`$23M","2027","Minimal structural; enables Hammond site")
)
$tbl1=Build-Table $t1 @(210,70,110,140)
$tbl1.Rows.Item(2).Range.Font.Bold=$true
$tbl1.Rows.Item(2).Shading.BackgroundPatternColor=$accentfill
GoEnd; $sel.TypeParagraph()
Para "Funding: borrow up to `$393M over 25 years - 60% taxpayer (3%/yr tax increase 2027-2030), 40% developer Amenity Cost Charges (~`$19K/unit). GATED by a referendum at the October 2026 municipal election. Council committee June 23, 2026; Council approval of designs + funding strategy June 30, 2026." 10 $false $gray 12

# ===== 2 HAMMOND =====
Para "2.  Hammond Aquatics - the target (deep detail)" 13 $true $navy 6
Bullets @(
  "122,000 sq ft over two storeys; aquatic hall ~65,000 sq ft. Site: Larry Walker / Hammond Stadium ball fields at Hammond Community Park (after the golf-course -> ballpark land swap).",
  "Aquatics: 37.5 m, 8-lane lap pool (movable bulkhead -> 25 m / 12.5 m), climbing wall + diving platforms; leisure pool w/ lazy river, play features, waterslide; 2 hot pools, cold plunge, steam, sauna.",
  "Community: full gymnasium, fitness/cardio/weights, fitness studios, multipurpose + arts/culture rooms, children's play, cafe; above- and below-ground parking (structurally significant).",
  "Structural character: long-span aquatic roof over a corrosive natatorium + below-grade parking + pool-tank structures - the specialized aquatic-SE problem set where the incumbent's typology advantage lives."
)
GoEnd; $sel.TypeParagraph()

# ===== 3 COMPETITIVE =====
Para "3.  Competitive reality - HCMA + Fast + Epp" 13 $true $navy 6
Bullets @(
  "Architect: HCMA Architecture + Design (Vancouver) - Canada's premier aquatic/rec architect (Grandview Heights, tememsew'txw New West, Rosemary Brown, Maple Ridge Leisure Centre). They own this typology.",
  "Incumbent SE: Fast + Epp. HCMA's signature aquatic centres are Fast + Epp structural with mass-timber/CLT hybrid roofs (Kalesnikoff CLT). Per our competitor map, Fast + Epp = the BC timber/civic+K12 dominator - this is their home turf.",
  "Implication: Fast + Epp is the presumptive structural partner on HCMA's schematic work now. But the formal detailed-design SE selection is post-referendum (~2027), and the City (not just HCMA) has procurement say on a `$227M public project."
)
GoEnd; $sel.TypeParagraph()

# ===== 4 CREDENTIALS =====
Para "4.  KOR's credential case (the counter - from Deltek)" 13 $true $navy 6
Bullets @(
  "POCO Recreation Centre (Port Coquitlam) - Phases A/B/C + pool/fitness. A Lower-Mainland municipal rec-centre-with-aquatics - the single most on-point reference.",
  "Parkinson Recreation Centre, Kelowna (active) - major municipal rec centre.",
  "Burnaby Lake aquatic pursuit + the delivered Burnaby Lake Recreation Centre (public/institutional).",
  "Arena work: Nelson Arena, Rogers Arena South Tower, preliminary arena structural - relevant to the Albion twin-rink parallel.",
  "The pitch: 'We're the available, principal-led SE that has actually delivered Lower-Mainland municipal recreation + aquatics (POCO, Burnaby Lake, Parkinson) and arenas - bring us onto the Hammond / Albion team.'"
)
GoEnd; $sel.TypeParagraph()

# ===== 5 GAPS =====
Para "5.  Honest gaps & risks" 13 $true $navy 6
Bullets @(
  "No existing City of Maple Ridge or HCMA Deltek relationship - KOR is cold to both (mitigated: 23 HCMA contacts already in our graph).",
  "Fast + Epp's HCMA lock + timber-aquatic typology ownership - the hardest barrier. KOR must match the mass-timber aquatic story or win on availability/responsiveness + municipal-rec record + cost.",
  "Referendum risk (Oct 2026): the entire `$393M program is contingent on the public vote. Tax-increase optics (`$90 -> `$385/yr) are a real failure risk. Don't over-invest before the vote signals.",
  "Long horizon: Hammond construction 2029 -> detailed-design SE selection ~2027. A positioning pursuit, not near-term revenue."
)
GoEnd; $sel.TypeParagraph()

# ===== 6 CONTACTS =====
Para "6.  Contacts" 13 $true $navy 6
Para "HCMA (prime architect - already in our graph, verified emails)" 11 $true 0 4
$t6=@(
  @("Name","Role","Email"),
  @("Tracy Liu","Director, Community + Recreation - THE target","t.liu@hcma.ca"),
  @("Darryl Condon","Managing Principal","d.condon@hcma.ca"),
  @("Joshua Potvin","Project Director","j.potvin@hcma.ca"),
  @("Maeve Counihan","Project Director","m.counihan@hcma.ca")
)
$tbl6=Build-Table $t6 @(110,250,160)
$tbl6.Rows.Item(2).Range.Font.Bold=$true
GoEnd; $sel.TypeParagraph()
Para "City of Maple Ridge (owner): Mayor Dan Ruimy (program champion - political will = referendum) | Stephane Labonne, GM Parks, Recreation & Culture (per 2022 hire - verify current) | RecFacilityStudy@MapleRidge.ca, 604-467-7310." 10 $false 0 10

# ===== 7 PLAY =====
Para "7.  The play (sequenced to the gates)" 13 $true $navy 6
NumberedList @(
  @("Now -> mid-2026: warm HCMA.","Open with Tracy Liu - lead with POCO Rec Centre + Burnaby Lake + Parkinson. Be HCMA's known, credible rec/aquatic SE in time for the post-referendum team."),
  @("Watch the gates.","June 23 / June 30, 2026 Council decisions and the Oct 2026 referendum gate everything. Don't commit pursuit cost before the vote signals favorably."),
  @("Run Albion Arena (2028) as a parallel.","Also timber, slightly earlier, arguably more contestable than the flagship aquatic; KOR's arena credentials apply."),
  @("Build the City relationship independently.","Maple Ridge has a `$115.6M/yr capital plan, 99 projects (Fire Hall No.3 `$35M, Protective Services Building) - a City relationship compounds beyond this program."),
  @("Decide the mass-timber position.","To contest Fast + Epp's aquatic typology, KOR needs a credible CLT/hybrid-aquatic story (ties to the open BC/CA mass-timber question).")
)

$doc.SaveAs([ref]$out, [ref]16)
$doc.Close(); $word.Quit()
"WROTE: $out"
