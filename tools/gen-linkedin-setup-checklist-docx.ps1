$ErrorActionPreference = "Stop"
$out = "C:\VIsual Studio Projects\Operations\docs\KOR-LinkedIn-App-Setup-Checklist-2026-06-20.docx"
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
function Steps($items,[int]$start){
    $n=$start
    foreach($m in $items){
        $sel.Font.Name="Calibri"; $sel.Font.Size=11; $sel.ParagraphFormat.SpaceAfter=8
        $sel.ParagraphFormat.LeftIndent=22; $sel.ParagraphFormat.FirstLineIndent=-22
        $sel.Font.Bold=$true; $sel.Font.Color=$navy; $sel.TypeText(([string]$n)+".  ")
        $sel.Font.Bold=$false; $sel.Font.Color=0; $sel.TypeText($m)
        $sel.TypeParagraph(); $n++
    }
    $sel.ParagraphFormat.LeftIndent=0; $sel.ParagraphFormat.FirstLineIndent=0
}
function Quote([string]$text){
    $sel.Font.Name="Calibri"; $sel.Font.Size=10.5; $sel.Font.Italic=$true; $sel.Font.Color=$gray
    $sel.ParagraphFormat.LeftIndent=22; $sel.ParagraphFormat.SpaceAfter=8
    $sel.TypeText([char]0x201C+$text+[char]0x201D); $sel.TypeParagraph()
    $sel.Font.Italic=$false; $sel.Font.Color=0; $sel.ParagraphFormat.LeftIndent=0
}

# ===== TITLE =====
Para "KOR Structural - LinkedIn App Setup Checklist" 21 $true $navy 2
Para "One-time setup to enable automated company-page posting    |    20 Jun 2026    |    Owner: KOR page admin" 9.5 $false $gray 12

# ===== INTRO =====
Para "What this is for" 13 $true $navy 4
Bullets @(
  "Goal: let the BD platform compose KOR posts and (after your approval) publish them to the KOR Structural LinkedIn company page via the official API.",
  "This document covers the NON-CODE setup only - registering a LinkedIn Developer app and getting API access. This is the long pole; everything technical is quick once the token works.",
  "Run all steps in a browser at developer.linkedin.com, logged in as an admin of the KOR Structural company page.",
  "Note: the compose-and-approve workflow is being built in parallel and does NOT depend on this - until the token lands, approved drafts are paste-to-post; one-click after."
)
GoEnd; $sel.TypeParagraph()

# ===== A =====
Para "A.  Prerequisites" 13 $true $navy 6
Steps @(
  "Confirm you have ADMIN access to KOR Structural's LinkedIn company page (Page -> Admin tools -> Manage admins). If not, have a Super Admin add you - required to associate + verify the app.",
  "Have ready: a company privacy policy URL (korstructural.com privacy page or site root) and the company logo."
) 1
GoEnd; $sel.TypeParagraph()

# ===== B =====
Para "B.  Create & verify the app" 13 $true $navy 6
Steps @(
  "Go to developer.linkedin.com -> My apps -> Create app.",
  "Fill in: App name (e.g., 'KOR Structural Social Publisher'), LinkedIn Page = KOR Structural (search + select), privacy policy URL, upload logo, accept legal terms -> Create app.",
  "Open the app -> Settings tab -> click 'Verify' next to the associated page. It generates a verification URL -> open that URL as a page admin -> confirm. This links the app to the page (required before any org product works)."
) 3
GoEnd; $sel.TypeParagraph()

# ===== C =====
Para "C.  Request the API products" 13 $true $navy 6
Steps @(
  "App -> Products tab. Request 'Sign In with LinkedIn using OpenID Connect' (usually instant; gives openid, profile, email).",
  "Request 'Community Management API' - THIS is the one for posting to the company page. It's a review form. Grants w_organization_social (post), r_organization_social (read engagement), rw_organization_admin (see which pages you admin)."
) 6
Para "Paste this use-case on the Community Management API form (keep it first-party - that is what gets approved):" 11 $false 0 4
Quote "We publish and schedule our own organic content (company news, project milestones, industry commentary) to our own LinkedIn company page, and read engagement metrics on our own posts. First-party use only - we do not manage other organizations' pages."
GoEnd; $sel.TypeParagraph()

# ===== D =====
Para "D.  Auth configuration" 13 $true $navy 6
Steps @(
  "App -> Auth tab -> copy the Client ID and Client Secret.",
  "Under OAuth 2.0 settings -> Authorized redirect URLs, add our callback: http://kor-app01:5600/linkedin/callback  (placeholder LAN URL - confirm the port with the build team; flag if you prefer a different one)."
) 8
GoEnd; $sel.TypeParagraph()

# ===== E =====
Para "E.  Capture these 5 things for the build" 13 $true $navy 6
$t=@(
  @("Item","Notes"),
  @("Client ID","From the Auth tab."),
  @("Client Secret","Store as a MACHINE ENV VAR on KOR-APP01: KOR_LINKEDIN_SECRET (same pattern as ODBC/Hunter secrets). Do NOT paste it in chat/email."),
  @("Redirect URI","The exact callback URL you registered."),
  @("Granted scopes","After the product shows 'Approved' - confirm w_organization_social is granted."),
  @("Organization URN","The page ID, format urn:li:organization:XXXXXXX. From the page admin URL, or the build can fetch it once the token works.")
)
$tbl=$doc.Tables.Add($sel.Range,$t.Count,2)
$tbl.Borders.Enable=$true; $tbl.Borders.OutsideColor=$navy; $tbl.Borders.InsideColor=$insideclr
$tbl.Range.Font.Name="Calibri"; $tbl.Range.Font.Size=10
$tbl.Rows.Item(1).Range.Font.Bold=$true; $tbl.Rows.Item(1).Range.Font.Color=$white; $tbl.Rows.Item(1).Shading.BackgroundPatternColor=$navy
for($i=0;$i -lt $t.Count;$i++){ for($j=0;$j -lt 2;$j++){ $c=$tbl.Cell($i+1,$j+1); $c.Range.Text=[string]$t[$i][$j]; $c.VerticalAlignment=1; if($i -gt 0 -and ($i % 2 -eq 0)){ $c.Shading.BackgroundPatternColor=$altfill } } }
$tbl.Columns.Item(1).Width=120; $tbl.Columns.Item(2).Width=350; $tbl.AllowAutoFit=$true; $tbl.AutoFitBehavior(2)
GoEnd; $sel.TypeParagraph()

# ===== F =====
Para "F.  Gotchas to expect" 13 $true $navy 6
Bullets @(
  "Community Management API may be gated - if you hit a 'not eligible / apply to Marketing Developer Platform' wall instead of a self-serve form, screenshot it and send it over; there's a partner-application fallback.",
  "Approval timeline: Sign In is instant; Community Management is often quick but can take a few days or a clarifying email. This is the long pole - kick it off now.",
  "Tokens: access token ~60 days, refresh ~1 year - the app auto-refreshes; you authorize once.",
  "Security: never paste the Client Secret in chat or email - machine env var on KOR-APP01 only.",
  "Official API only - automating the LinkedIn website is against their Terms and risks the account; we use the approved API."
)
GoEnd; $sel.TypeParagraph()

# ===== NEXT =====
Para "Next steps" 13 $true $navy 6
Bullets @(
  "You: kick off A-D now so the approval clock starts; set KOR_LINKEDIN_SECRET on KOR-APP01; send the 5 items from section E.",
  "Build team (in parallel, no API dependency): compose -> approve workflow - draft schema, Claude composer with brand-voice prompts for the 4 content streams (project wins, thought leadership, market/BD intel, recruiting/culture), and the WPF approval surface.",
  "Confirm who the KOR page admin is (determines who does the one-time OAuth authorize)."
)

$doc.SaveAs([ref]$out, [ref]16)
$doc.Close(); $word.Quit()
"WROTE: $out"
