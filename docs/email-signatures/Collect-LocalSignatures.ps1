<#
.SYNOPSIS
  Logon script: copies the user's local classic-Outlook signatures to the LAN
  share so they can be analyzed centrally. Read-only on the user's machine.

.DESCRIPTION
  Deploy via GPO (User Configuration > Scripts > Logon) alongside
  Set-LocalOutlookSignature.ps1 — both are safe to enable at the same time:
  this one collects, the other exits silently until a generated signature
  exists for the user on the share.

  Per user it copies %APPDATA%\Microsoft\Signatures\* to
  <share>\collected\<username>\ and records which signature Outlook has set
  as default (registry) in _info.txt. Re-collects at most once per day so
  repeated logons are cheap.

  ADJUST $ShareDir before deploying. The share folder needs domain users
  write access.
#>

$ShareDir = '\\KOR-FS01\BD Brain\email-signatures\collected'   # ADJUST if hosted elsewhere

$sigDir = Join-Path $env:APPDATA 'Microsoft\Signatures'
if (-not (Test-Path $sigDir)) { return }

$dest = Join-Path $ShareDir $env:USERNAME
$marker = Join-Path $dest '_info.txt'
if ((Test-Path $marker) -and ((Get-Item $marker).LastWriteTime -gt (Get-Date).AddDays(-1))) { return }

try {
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $sigDir '*.htm') $dest -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $sigDir '*.txt') $dest -Force -ErrorAction SilentlyContinue

    # Which signature is the default? Check global MailSettings, then any
    # per-profile signature values under the Outlook profiles key.
    $lines = @("User: $env:USERNAME", "Machine: $env:COMPUTERNAME", "Collected: $(Get-Date -Format s)")
    $ms = Get-ItemProperty 'HKCU:\Software\Microsoft\Office\16.0\Common\MailSettings' -ErrorAction SilentlyContinue
    $lines += "MailSettings NewSignature: $($ms.NewSignature)"
    $lines += "MailSettings ReplySignature: $($ms.ReplySignature)"
    Get-ChildItem 'HKCU:\Software\Microsoft\Office\16.0\Outlook\Profiles' -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object {
            $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            foreach ($name in 'New Signature', 'Reply-Forward Signature') {
                if ($p.$name) { $lines += "Profile $($_.PSChildName) ${name}: $($p.$name)" }
            }
        }
    $lines | Out-File $marker -Encoding utf8
} catch {
    # Never block or noisy-fail a user logon over signature collection
}
