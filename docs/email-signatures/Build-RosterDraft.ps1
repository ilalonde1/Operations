<#
.SYNOPSIS
  Analyze the collected local signatures and draft the signature roster.

.DESCRIPTION
  Run once (from KOR-1001 or KOR-APP01) after Collect-LocalSignatures.ps1 has
  gathered signatures on the share for a day or two. For each collected user:

    - Picks their default signature (per _info.txt) or the largest .htm
    - Strips it to text and best-effort extracts: display name, credentials
      line, role, orange title, D/M phone numbers, office extension, email
    - Writes roster.draft.csv next to this script

  Review roster.draft.csv (the ExistingSig column holds the flattened original
  for cross-checking), fix anything the parse missed, save as roster.csv, then
  run Generate-Signatures.ps1. Parsing is a head start, not a source of truth.
#>

$ErrorActionPreference = 'Stop'

$CollectedDir = '\\KOR-FS01\BD Brain\email-signatures\collected'   # ADJUST to match Collect-LocalSignatures.ps1

function ConvertTo-SigText([string]$html) {
    $t = $html -replace '(?s)<style.*?</style>', ' ' -replace '(?s)<!--.*?-->', ' '
    $t = $t -replace '(?i)<(br|/p|/div|/tr)[^>]*>', "`n" -replace '<[^>]+>', ' '
    $t = $t -replace '&nbsp;', ' ' -replace '&ndash;', '-' -replace '&amp;', '&' -replace '&middot;', '·'
    ($t -split "`n" | ForEach-Object { ($_ -replace '\s+', ' ').Trim() } | Where-Object { $_ }) -join "`n"
}

$rows = foreach ($userDir in Get-ChildItem $CollectedDir -Directory | Sort-Object Name) {
    $htmFiles = Get-ChildItem $userDir.FullName -Filter *.htm -ErrorAction SilentlyContinue
    if (-not $htmFiles) { continue }

    # Prefer the signature Outlook reports as default; else the largest file
    $sigFile = $null
    $info = Join-Path $userDir.FullName '_info.txt'
    if (Test-Path $info) {
        $defaults = (Get-Content $info) -match 'Signature:' | ForEach-Object { ($_ -split ':', 2)[1].Trim() } | Where-Object { $_ }
        foreach ($d in $defaults) {
            $sigFile = $htmFiles | Where-Object { $_.BaseName -eq $d } | Select-Object -First 1
            if ($sigFile) { break }
        }
    }
    if (-not $sigFile) { $sigFile = $htmFiles | Sort-Object Length -Descending | Select-Object -First 1 }

    $text  = ConvertTo-SigText (Get-Content $sigFile.FullName -Raw)
    $lines = $text -split "`n"

    # Best-effort field extraction from the existing signature
    $name  = $lines | Select-Object -First 1
    $creds = ($lines | Where-Object { $_ -match '(P\.?\s?Eng|M\.?\s?Eng|Struct\.?\s?Eng|\bPE\b|\bSE\b|\bEIT\b|\bAScT\b|\bCTech\b)' } | Select-Object -First 1)
    if ($creds -eq $name) { $creds = '' }
    $role  = ($lines | Where-Object { $_ -cmatch '^[A-Z][A-Z .&/-]{3,}$' } | Select-Object -First 1)
    $title = ($lines | Where-Object { $_ -ne $role -and $_ -match '(?i)(engineer|technologist|drafter|designer|administrator|manager|coordinator|principal|associate)' -and $_ -notmatch '@' } | Select-Object -First 1)
    if ($title -eq $creds -or $title -eq $name) { $title = '' }

    $direct = if ($text -match 'D\s*:?\s*(\d{3}[.\-\s]\d{3}[.\-\s]\d{4})') { $Matches[1] } else { '' }
    $mobile = if ($text -match 'M\s*:?\s*(\d{3}[.\-\s]\d{3}[.\-\s]\d{4})') { $Matches[1] } else { '' }
    $ext    = if ($text -match '\((\d{3})\)')                              { $Matches[1] } else { '' }
    $email  = if ($text -match '([\w.\-]+@korstructural\.com)')            { $Matches[1] } else { "$($userDir.Name)@korstructural.com" }

    $snippet = ($text -replace "`n", ' | ')
    if ($snippet.Length -gt 250) { $snippet = $snippet.Substring(0, 250) }

    [pscustomobject]@{
        Alias       = $userDir.Name
        Email       = $email
        Name        = $name
        Credentials = $creds
        Role        = $role
        Title       = $title
        Direct      = $direct
        Mobile      = $mobile
        Ext         = $ext
        SourceFile  = $sigFile.Name
        ExistingSig = $snippet
    }
}

$out = Join-Path $PSScriptRoot 'roster.draft.csv'
$rows | Export-Csv $out -NoTypeInformation -Encoding utf8

Write-Host "Drafted $($rows.Count) users -> $out"
Write-Host "Review/correct, save as roster.csv, then run Generate-Signatures.ps1."

$missing = Get-ChildItem $CollectedDir -Directory | Where-Object { -not (Get-ChildItem $_.FullName -Filter *.htm -ErrorAction SilentlyContinue) }
if ($missing) { Write-Warning "Collected but no .htm found for: $($missing.Name -join ', ')" }
