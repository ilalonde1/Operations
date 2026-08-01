<#
.SYNOPSIS
  Push the slim signatures to every user's CLOUD (legacy OWA) signature store
  so OWA / new Outlook / (possibly) Outlook Mobile show them.

.DESCRIPTION
  Complements the desktop c$ deploy (Deploy-RemoteSignatures.ps1) — desktop
  classic Outlook keeps its local files with the EMBEDDED base64 logo; this
  sets the per-mailbox cloud signature used by cloud-composing clients.

  PREREQUISITE (done 2026-07-10): roaming signatures disabled tenant-wide —
    Set-OrganizationConfig -PostponeRoamingSignaturesUntilLater $true
  — otherwise cloud clients ignore this legacy store (no admin API exists for
  the roaming store; that's a Microsoft gap).

  Image handling: Exchange STRIPS base64 data URIs from SignatureHtml
  (verified — src becomes ""), so the cloud copy uses the HOSTED logo URL
  instead. Only mail composed in OWA/mobile references it; desktop mail still
  embeds. Recipients with image-blocking click "download images" — standard.

  Requires an Exchange admin session (browser prompt on connect).

.EXAMPLE
  .\Push-CloudSignatures.ps1
#>

$ErrorActionPreference = 'Stop'

$genDir = Join-Path $PSScriptRoot 'generated'
$roster = Import-Csv (Join-Path $PSScriptRoot 'roster.csv')

Import-Module ExchangeOnlineManagement
Connect-ExchangeOnline -ShowBanner:$false

foreach ($p in $roster) {
    $file = Join-Path $genDir "$($p.Alias).htm"
    if (-not (Test-Path $file)) { Write-Warning "$($p.Alias): no generated file, skipped"; continue }

    $html = Get-Content $file -Raw
    # Cloud store rejects data URIs — swap to the hosted logo
    $html = $html -replace 'src="data:image/png;base64,[^"]+"', 'src="https://www.korstructural.com/wp-content/uploads/2023/06/KOR-logo.png"'
    $text = ((($html -replace '<[^>]+>', ' ') -replace '&nbsp;', ' ' -replace '&ndash;', '-' -replace '&middot;', '·') -replace '\s+', ' ').Trim()

    Set-MailboxMessageConfiguration -Identity $p.Email `
        -SignatureHtml $html `
        -SignatureText $text `
        -SignatureTextOnMobile $text `
        -AutoAddSignature $true `
        -AutoAddSignatureOnMobile $true `
        -AutoAddSignatureOnReply $true
    Write-Host ("  {0,-12} cloud signature set" -f $p.Alias)
}

Disconnect-ExchangeOnline -Confirm:$false
Write-Host ""
Write-Host "Done. Cloud clients pick this up after roaming-disable propagates (minutes to ~24h)."
