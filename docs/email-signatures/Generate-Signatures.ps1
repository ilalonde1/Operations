<#
.SYNOPSIS
  Generate per-user signature files from signature-template.html + roster.csv
  and stage them on the LAN share for the deploy logon script.

.DESCRIPTION
  Makes no changes to any mailbox or PC. Output:
    .\generated\<alias>.htm            (local copy for inspection)
    <share>\generated\<alias>.htm      (only with -Publish; picked up by
                                        Set-LocalOutlookSignature.ps1 at each
                                        user's next logon)

  Template lines drop cleanly when a roster field is blank:
    Credentials blank -> credentials row removed
    Title blank       -> orange title row removed (admin staff)
    Mobile blank      -> "M ..." segment removed from the phone line

  Run without -Publish first, open a few files in a browser and paste one into
  an Outlook signature to eyeball it, then rerun with -Publish.

.NOTES
  Run:  .\Generate-Signatures.ps1            # generate locally only
        .\Generate-Signatures.ps1 -Publish   # also stage to the share
#>
[CmdletBinding()]
param(
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

$ShareGeneratedDir = '\\KOR-FS01\BD Brain\email-signatures\generated'   # ADJUST to match Set-LocalOutlookSignature.ps1

$rosterPath = Join-Path $PSScriptRoot 'roster.csv'
if (-not (Test-Path $rosterPath)) {
    throw "roster.csv not found. Run Build-RosterDraft.ps1, review roster.draft.csv, save as roster.csv."
}
$roster = Import-Csv $rosterPath

$template = Get-Content (Join-Path $PSScriptRoot 'signature-template.html') -Raw
$template = $template -replace '(?s)^<!--.*?-->\s*', ''   # strip the instruction comment

# Logo as base64 data URI — Word embeds it as cid: attachment at send and it
# survives roaming-signature sync (same mechanism as the fleet's old sigs)
$logoB64 = 'data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'korlogo-small.png')))

$genDir = Join-Path $PSScriptRoot 'generated'
New-Item -ItemType Directory -Force $genDir | Out-Null

Write-Host "Generating $($roster.Count) signatures into $genDir"
$count = 0
foreach ($p in $roster) {
    if (-not $p.Alias.Trim()) { continue }
    $html = $template
    if (-not $p.Credentials.Trim()) {
        $html = $html -replace '(?s)<tr>\s*<td[^>]*>\{\{CREDENTIALS\}\}</td>\s*</tr>\s*', ''
    }
    if (-not $p.Role.Trim()) {
        $html = $html -replace '(?s)<tr>\s*<td[^>]*>\{\{ROLE\}\}</td>\s*</tr>\s*', ''
    }
    if (-not $p.Title.Trim()) {
        $html = $html -replace '(?s)<tr>\s*<td[^>]*>\{\{TITLE\}\}</td>\s*</tr>\s*', ''
    }
    if (-not $p.Direct.Trim()) {
        $html = $html -replace 'D <span[^>]*>\{\{DIRECT\}\}</span>&nbsp;\|&nbsp;', ''
    }
    if (-not $p.Mobile.Trim()) {
        $html = $html -replace 'M <span[^>]*>\{\{MOBILE\}\}</span>&nbsp;\|&nbsp;', ''
    }
    if (-not $p.Ext.Trim()) {
        $html = $html -replace ' \(\{\{EXT\}\}\)', ''
    }
    $html = $html -replace '\{\{NAME\}\}',        $p.Name
    $html = $html -replace '\{\{CREDENTIALS\}\}', $p.Credentials
    $html = $html -replace '\{\{ROLE\}\}',        $p.Role
    $html = $html -replace '\{\{TITLE\}\}',       $p.Title
    $html = $html -replace '\{\{DIRECT\}\}',      $p.Direct
    $html = $html -replace '\{\{MOBILE\}\}',      $p.Mobile
    $html = $html -replace '\{\{EXT\}\}',         $p.Ext
    $html = $html -replace '\{\{EMAIL\}\}',       $p.Email
    $html = $html -replace '\{\{LOGO_B64\}\}',    $logoB64

    # Optional Greeting roster column (e.g. "Regards,") — prepended as its own
    # line in Aptos 12 so it flows with the user's typed message text
    if (('' + $p.Greeting).Trim()) {
        $gRow = "  <tr>`n    <td style=`"padding: 0 0 16px 0; font-family: Aptos, 'Segoe UI', Calibri, sans-serif; font-size: 12pt; color: #0F0F0F;`">$($p.Greeting)</td>`n  </tr>"
        $html = $html -replace '(<table[^>]*>)', "`$1`n$gRow"
    }

    $leftover = [regex]::Matches($html, '\{\{\w+\}\}') | ForEach-Object Value | Select-Object -Unique
    if ($leftover) { throw "Unfilled tokens for $($p.Alias): $($leftover -join ', ') — fix roster.csv." }

    # BOM so classic Outlook detects Unicode (accented names) in the .htm
    $html | Out-File (Join-Path $genDir "$($p.Alias).htm") -Encoding utf8BOM
    Write-Host "  $($p.Alias).htm"
    $count++
}

if ($Publish) {
    New-Item -ItemType Directory -Force $ShareGeneratedDir | Out-Null
    Copy-Item (Join-Path $genDir '*.htm') $ShareGeneratedDir -Force
    Copy-Item (Join-Path $PSScriptRoot 'korlogo.png') $ShareGeneratedDir -Force
    Write-Host ""
    Write-Host "Published $count signatures + korlogo.png to $ShareGeneratedDir — they apply at each user's next logon."
} else {
    Write-Host ""
    Write-Host "Generated locally only. Inspect .\generated\, then rerun with -Publish to stage for deployment."
}
