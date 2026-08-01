<#
.SYNOPSIS
  Proactively harvest users' local classic-Outlook signatures over the network
  (admin shares) — no waiting for the logon-script collection.

.DESCRIPTION
  For each enabled user in the CSV, checks every reachable domain computer for
  \\<PC>\c$\Users\<sam>\AppData\Roaming\Microsoft\Signatures and copies .htm /
  .txt files to <OutDir>\<sam>\. When a user has signatures on multiple PCs,
  the newest copy of each filename wins. Provenance is logged per user in
  _remote_info.txt. Read-only on every remote machine.

  Requires admin rights on the workstations (c$). Cannot read which signature
  is the Outlook DEFAULT (that lives in each user's registry hive) — the
  logon-script collector records that; Build-RosterDraft.ps1 falls back to the
  largest .htm otherwise.

.EXAMPLE
  .\Collect-RemoteSignatures.ps1
  .\Collect-RemoteSignatures.ps1 -Computers KOR-206-N, KOR-101 -OutDir C:\temp\collected
#>
[CmdletBinding()]
param(
    [string]$UsersCsv = "$env:USERPROFILE\Desktop\ADUsers.csv",
    [string]$OutDir   = '\\KOR-FS01\BD Brain\email-signatures\collected',
    [string[]]$Computers
)

$ErrorActionPreference = 'Stop'

$users = Import-Csv $UsersCsv | Where-Object { $_.Enabled -eq 'TRUE' }
Write-Host "Users to harvest: $($users.Count)"

if (-not $Computers) {
    # ADSI directly — no RSAT/ActiveDirectory module needed on a domain-joined PC
    $searcher = [adsisearcher]'(&(objectCategory=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))'
    $searcher.PageSize = 500
    [void]$searcher.PropertiesToLoad.AddRange(@('name', 'operatingsystem'))
    $Computers = $searcher.FindAll() | ForEach-Object {
        if ("$($_.Properties['operatingsystem'])" -notmatch 'Server') { "$($_.Properties['name'])" }
    } | Sort-Object
}
Write-Host "Candidate computers: $($Computers.Count)"

Write-Host "Pinging..."
$alive = $Computers | ForEach-Object -Parallel {
    if (Test-Connection $_ -Count 1 -TimeoutSeconds 1 -Quiet) { $_ }
} -ThrottleLimit 16 | Sort-Object
Write-Host "Reachable: $($alive.Count) ($($alive -join ', '))"
if (-not $alive) { throw "No computers reachable — check network/AD filter or pass -Computers explicitly." }

New-Item -ItemType Directory -Force $OutDir | Out-Null

$summary = foreach ($u in $users) {
    $sam = $u.SamAccountName
    $found = @()
    foreach ($pc in $alive) {
        $src = "\\$pc\c`$\Users\$sam\AppData\Roaming\Microsoft\Signatures"
        try {
            if (-not (Test-Path $src)) { continue }
            $files = Get-ChildItem $src -File -ErrorAction Stop | Where-Object Extension -in '.htm', '.html', '.txt'
            if (-not $files) { continue }

            $dest = Join-Path $OutDir $sam
            New-Item -ItemType Directory -Force $dest | Out-Null
            foreach ($f in $files) {
                $target = Join-Path $dest $f.Name
                if (-not (Test-Path $target) -or $f.LastWriteTime -gt (Get-Item $target).LastWriteTime) {
                    Copy-Item $f.FullName $target -Force
                }
                $found += "$pc  $($f.Name)  $($f.LastWriteTime.ToString('s'))"
            }
        } catch {
            Write-Warning "  $sam @ $pc : $($_.Exception.Message)"
        }
    }

    if ($found) {
        @("User: $sam", "Harvested: $(Get-Date -Format s)", "Sources:") + ($found | ForEach-Object { "  $_" }) |
            Out-File (Join-Path $OutDir $sam '_remote_info.txt') -Encoding utf8
    }
    $status = if ($found) { "OK ($((($found | ForEach-Object { ($_ -split '\s+')[0] }) | Select-Object -Unique).Count) PC(s))" } else { 'NOT FOUND' }
    Write-Host ("  {0,-14} {1}" -f $sam, $status)
    [pscustomobject]@{ User = $sam; Status = $status }
}

$missing = ($summary | Where-Object Status -eq 'NOT FOUND').User
Write-Host ""
Write-Host "Harvested $((($summary | Where-Object Status -ne 'NOT FOUND')).Count)/$($users.Count) users -> $OutDir"
if ($missing) {
    Write-Host "Not found (PC off / no local sig / different machine): $($missing -join ', ')"
    Write-Host "The logon-script collector (Collect-LocalSignatures.ps1) will pick up stragglers."
}
