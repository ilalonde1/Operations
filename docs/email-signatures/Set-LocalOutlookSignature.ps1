<#
.SYNOPSIS
  Logon script for classic desktop Outlook: installs the user's generated
  signature from the LAN share and sets it as default for new mail and replies.

.DESCRIPTION
  Deploy via GPO (User Configuration > Scripts > Logon) or a domain logon
  script. Per user it:
    1. Copies \\KOR-FS01\...\signatures\<alias>.htm to %APPDATA%\Microsoft\Signatures\Kor Structural.htm
    2. Sets "Kor Structural" as the default New/Reply signature (Office 16.0 registry)
    3. Disables Outlook roaming signatures so the deployed file is what Outlook uses

  Safe to run repeatedly (idempotent). Exits silently if no signature exists
  for the user on the share. Users can still be allowed to edit signatures;
  this just resets the default at each logon.

  ADJUST $ShareDir to the real share path before deploying — the generated
  folder produced by Push-Signatures.ps1 -Preview is the payload.
#>

$ShareDir = '\\KOR-FS01\BD Brain\email-signatures\generated'   # ADJUST if hosted elsewhere
$SigName  = 'Kor Structural'

$alias  = $env:USERNAME
$source = Join-Path $ShareDir "$alias.htm"
if (-not (Test-Path $source)) { return }

$sigDir = Join-Path $env:APPDATA 'Microsoft\Signatures'
New-Item -ItemType Directory -Force $sigDir | Out-Null
Copy-Item $source (Join-Path $sigDir "$SigName.htm") -Force

# Local logo copy — the signature references "Kor Structural_files/korlogo.png"
# so Outlook embeds it in outgoing mail (hosted images get blocked by clients)
$filesDir = Join-Path $sigDir "$SigName`_files"
New-Item -ItemType Directory -Force $filesDir | Out-Null
Copy-Item (Join-Path $ShareDir 'korlogo.png') (Join-Path $filesDir 'korlogo.png') -Force

# Plain-text fallback so replies from plain-text mail still get a signature
$text = ((Get-Content $source -Raw) -replace '<[^>]+>', ' ') -replace '&nbsp;', ' ' -replace '&ndash;', '-' -replace '&middot;', '·' -replace '\s+', ' '
$text.Trim() | Out-File (Join-Path $sigDir "$SigName.txt") -Encoding utf8

# Set as default signature for new messages and replies (classic Outlook, Office 16.0)
$mailSettings = 'HKCU:\Software\Microsoft\Office\16.0\Common\MailSettings'
New-Item -Path $mailSettings -Force | Out-Null
Set-ItemProperty -Path $mailSettings -Name 'NewSignature'   -Value $SigName -Type String
Set-ItemProperty -Path $mailSettings -Name 'ReplySignature' -Value $SigName -Type String

# Keep roaming signatures from overriding the deployed one in classic Outlook
$setupKey = 'HKCU:\Software\Microsoft\Office\16.0\Outlook\Setup'
New-Item -Path $setupKey -Force | Out-Null
Set-ItemProperty -Path $setupKey -Name 'DisableRoamingSignaturesTemporaryToggle' -Value 1 -Type DWord
Set-ItemProperty -Path $setupKey -Name 'DisableRoamingSignatures'                -Value 1 -Type DWord
