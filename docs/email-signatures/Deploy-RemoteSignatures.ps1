<#
.SYNOPSIS
  Push the generated signatures directly to every user's PC over c$ —
  no GPO, no logon script, no registry changes.

.DESCRIPTION
  Key idea: Outlook's default-signature setting (registry, per user) points at
  a signature NAME. By overwriting the user's EXISTING .htm signature files in
  place, their default keeps working and the content becomes ours — no
  registry write needed, and users who had no auto-signature behave exactly as
  before. Originals are already backed up on the share by the harvest
  (collected\<user>\).

  For each roster user, on every reachable PC where their profile has a
  Signatures folder:
    - installs korlogo.png to Signatures\Kor Structural_files\ (the generated
      HTML references it relatively, so it resolves from any .htm in that dir)
    - overwrites an existing .htm signature ONLY if its content contains the
      user's own email address (i.e. it is their personal firm signature) —
      this protects personal short sigs ("Thx MM"), shared-mailbox sigs
      (reviews@), and anything else deliberate. Name-based exclusions on top:
      vacation/holiday/away/out-of-office and cmurtagh's Okanagan role variant
      (different role line — left intact, still carries old boilerplate).
    - refreshes the matching .txt plain-text fallback for each overwritten name
    - if the user has no qualifying .htm at all, creates "Kor Structural.htm"
      (they would need to select it in Outlook once)

  DRY-RUN by default — prints exactly what would be overwritten where.
  Rerun with -Commit to apply. Rerun anytime for stragglers (e.g. a PC that
  was off); it is idempotent.

  Caveat: stale .rtf signature variants are left alone — only mail composed in
  Rich Text format (rare) would show the old signature.

.EXAMPLE
  .\Deploy-RemoteSignatures.ps1            # dry-run
  .\Deploy-RemoteSignatures.ps1 -Commit    # do it
#>
[CmdletBinding()]
param(
    [switch]$Commit
)

$ErrorActionPreference = 'Stop'

$kitDir  = $PSScriptRoot
$genDir  = Join-Path $kitDir 'generated'
$logoSrc = Join-Path $kitDir 'korlogo.png'
$Exclude = 'vacation|holiday|away|out.?of.?office|okanagan'

$roster = Import-Csv (Join-Path $kitDir 'roster.csv')

# Reachable workstations via ADSI (no RSAT needed)
$searcher = [adsisearcher]'(&(objectCategory=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))'
$searcher.PageSize = 500
[void]$searcher.PropertiesToLoad.AddRange(@('name', 'operatingsystem'))
$computers = $searcher.FindAll() | ForEach-Object {
    if ("$($_.Properties['operatingsystem'])" -notmatch 'Server') { "$($_.Properties['name'])" }
} | Sort-Object
$alive = $computers | ForEach-Object -Parallel {
    # Ping first (fast); fall back to SMB — remote/VPN machines (e.g.
    # KOR-EDMONTON-01) block ICMP but still expose c$
    if ((Test-Connection $_ -Count 1 -TimeoutSeconds 1 -Quiet) -or (Test-Path "\\$_\c`$\Users")) { $_ }
} -ThrottleLimit 16 | Sort-Object
Write-Host "Reachable PCs: $($alive.Count)"
Write-Host ("Mode: " + $(if ($Commit) { 'COMMIT' } else { 'DRY-RUN (nothing written)' }))
Write-Host ""

function ConvertTo-PlainText([string]$html) {
    ((($html -replace '<[^>]+>', ' ') -replace '&nbsp;', ' ' -replace '&ndash;', '-' -replace '&middot;', '·' -replace '&amp;', '&') -replace '\s+', ' ').Trim()
}

$summary = foreach ($p in $roster) {
    $alias = $p.Alias
    $genFile = Join-Path $genDir "$alias.htm"
    if (-not (Test-Path $genFile)) { Write-Warning "$alias : no generated file, skipped"; continue }
    $html = Get-Content $genFile -Raw
    $text = ConvertTo-PlainText $html

    $deployedTo = @()
    foreach ($pc in $alive) {
        $sigDir = "\\$pc\c`$\Users\$alias\AppData\Roaming\Microsoft\Signatures"
        try {
            if (-not (Test-Path $sigDir)) { continue }

            $targets = @(); $skipped = @()
            foreach ($f in Get-ChildItem $sigDir -Filter *.htm -File) {
                # Match on VISIBLE text only — Word-HTML metadata can contain the
                # user's email even in unrelated sigs (e.g. a bare "Thx MM")
                $visible = (Get-Content $f.FullName -Raw) -replace '(?s)<style.*?</style>', ' ' -replace '(?s)<head.*?</head>', ' ' -replace '(?s)<xml.*?</xml>', ' ' -replace '(?s)<!--.*?-->', ' ' -replace '<[^>]+>', ' '
                # Email OR display name = their personal sig (some old sigs have
                # no E line, e.g. kevinw); vacation variants are name-excluded.
                # Name tokens joined by \s+ — Word-HTML wraps names across lines.
                $nameRx = ($p.Name.Trim() -split '\s+' | ForEach-Object { [regex]::Escape($_) }) -join '\s+'
                $isPersonal = $visible -match [regex]::Escape($p.Email) -or $visible -match $nameRx
                if ($f.BaseName -notmatch $Exclude -and $isPersonal) { $targets += $f }
                else { $skipped += $f.BaseName }
            }
            $names = if ($targets) { $targets.BaseName } else { @('Kor Structural') }
            if ($skipped) { Write-Host ("    {0,-12} skipping on {1}: {2}" -f $alias, $pc, ($skipped -join '; ')) -ForegroundColor DarkGray }

            if ($Commit) {
                # Generated HTML is fully self-contained (logo = base64 data
                # URI) — no _files folder or path rewriting needed. Word
                # converts the data URI to a cid: attachment at send.
                foreach ($n in $names) {
                    $html | Out-File (Join-Path $sigDir "$n.htm") -Encoding utf8BOM
                    $text | Out-File (Join-Path $sigDir "$n.txt") -Encoding utf8BOM
                }
            }
            $deployedTo += "$pc [$($names -join '; ')]"
        } catch {
            Write-Warning "  $alias @ $pc : $($_.Exception.Message)"
        }
    }

    $status = if ($deployedTo) { $deployedTo -join ' + ' } else { 'NOT FOUND — PC off? rerun later' }
    Write-Host ("  {0,-12} {1}" -f $alias, $status)
    [pscustomobject]@{ User = $alias; Found = [bool]$deployedTo }
}

$missing = ($summary | Where-Object { -not $_.Found }).User
Write-Host ""
Write-Host "$(($summary | Where-Object Found).Count)/$($summary.Count) users reachable$(if ($Commit) { ' and deployed' })."
if ($missing) { Write-Host "Rerun for: $($missing -join ', ')" }
if (-not $Commit) { Write-Host "Dry-run only. Rerun with -Commit to apply." }
