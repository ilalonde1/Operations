# FIX install-updates: installs what Windows Update has waiting on this machine, as SYSTEM, now. Software only --
# drivers are never installed from here (the same filter as Probes/updates.ps1). Preview and feature upgrades are
# left out: a feature upgrade is a project, not a patch. Restarts only when $RestartIfNeeded is set (the
# "install-updates-restart" fix) AND an update needs it -- with the same 5-minute on-screen warning as "Restart the PC".
$ErrorActionPreference = 'Stop'
if (-not (Get-Variable RestartIfNeeded -ErrorAction SilentlyContinue)) { $RestartIfNeeded = $false }
$session = New-Object -ComObject Microsoft.Update.Session
$session.ClientApplicationID = 'KOR NetworkOps'
$searcher = $session.CreateUpdateSearcher()
$searcher.Online = $true
$found = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
$todo = New-Object -ComObject Microsoft.Update.UpdateColl
foreach ($u in $found.Updates) {
    $cats = @($u.Categories | ForEach-Object { [string]$_.Name })
    if ($cats -contains 'Upgrades' -or $u.Title -match '\bPreview\b') { continue }
    if (-not $u.EulaAccepted) { $u.AcceptEula() }
    [void]$todo.Add($u)
}
if ($todo.Count -eq 0) {
    return [pscustomobject]@{ Result = 'Nothing to install: Windows Update has nothing waiting.'; Installed = 0; Failed = 0; RebootRequired = $false; Updates = @() }
}

$dl = $session.CreateUpdateDownloader()
$dl.Updates = $todo
[void]$dl.Download()

$ready = New-Object -ComObject Microsoft.Update.UpdateColl
foreach ($u in $todo) { if ($u.IsDownloaded) { [void]$ready.Add($u) } }
$inst = $session.CreateUpdateInstaller()
$inst.Updates = $ready
$r = if ($ready.Count -gt 0) { $inst.Install() } else { $null }

# ResultCode: 2 succeeded, 3 succeeded with errors, 4 failed, 5 aborted.
$codes = @{ 0 = 'not started'; 1 = 'in progress'; 2 = 'installed'; 3 = 'installed with errors'; 4 = 'failed'; 5 = 'aborted' }
$rows = @(for ($i = 0; $i -lt $todo.Count; $i++) {
    $u = $todo.Item($i)
    $idx = -1
    for ($j = 0; $j -lt $ready.Count; $j++) { if ($ready.Item($j).Identity.UpdateID -eq $u.Identity.UpdateID) { $idx = $j; break } }
    $code = if ($idx -ge 0 -and $r) { [int]$r.GetUpdateResult($idx).ResultCode } else { -1 }
    [pscustomobject]@{
        Kb = (@($u.KBArticleIDs) | Select-Object -First 1) -as [string]; Title = [string]$u.Title
        Outcome = if ($code -lt 0) { 'not downloaded' } else { $codes[$code] }
        HResult = if ($idx -ge 0 -and $r) { '0x{0:X8}' -f $r.GetUpdateResult($idx).HResult } else { $null }
    }
})
$ok = @($rows | Where-Object { $_.Outcome -like 'installed*' }).Count
$failed = $rows.Count - $ok
$reboot = [bool]($r -and $r.RebootRequired)

$restart = 'no restart needed'
if ($reboot) {
    if ($RestartIfNeeded) {
        $msg = 'KOR IT (NetworkOps) installed Windows updates and will restart this computer in 5 minutes. Please save your work now.'
        & shutdown.exe /r /t 300 /c $msg /d p:2:17
        $restart = if ($LASTEXITCODE -eq 0) { 'restart scheduled in 5 minutes (the user was warned on screen)' } else { "restart needed, but shutdown.exe refused (exit $LASTEXITCODE)" }
    } else { $restart = 'RESTART NEEDED to finish (not restarted)' }
}
[pscustomobject]@{
    Result = "Installed $ok of $($rows.Count)$(if ($failed) { ", $failed failed" }); $restart."
    Installed = $ok; Failed = $failed; RebootRequired = $reboot; Updates = @($rows)
}
