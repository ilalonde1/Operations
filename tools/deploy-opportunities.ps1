#Requires -Version 7.0
<#
.SYNOPSIS
    Deploys the Kor.Opportunities.Worker publish artifact to KOR-APP01 and PROVES
    every one of our assemblies actually landed.

.DESCRIPTION
    Companion to publish-opportunities.ps1, and deliberately in the repo root beside
    it rather than in output/.

    ⛔ WHY THIS FILE EXISTS. The deploy step was the only part of the pipeline not
    under version control. `output/` is gitignored (.gitignore:93), and it held FIVE
    divergent forks named after whatever task spawned them —
    deploy-worker-{vancouver,crm,permitfix,lowvalue,258}.ps1, 54-57 lines each, all
    different. Root CLAUDE.md rule 8: one artefact, one place, one name.

    ⛔ AND EACH FORK VERIFIED A DIFFERENT SINGLE DLL, which is how a real deploy got
    a false green on 2026-10-09. deploy-worker-vancouver checks Kor.Opportunities.
    Data.dll; deploy-worker-crm checks Kor.Opportunities.Worker.dll. The R-CG scoring
    term (commit b860aea7) lives in Kor.Opportunities.CORE.dll — so the script
    reported "Data.dll replaced: True / service RUNNING: True" while saying nothing
    whatsoever about the assembly that carried the change. Had the copy of Core.dll
    failed, the deploy would have looked perfect and the fix would simply not be
    there. A deploy is verified by checking the thing that changed, and the script
    cannot know in advance which thing that is — so it checks them all.

    WHAT IT VERIFIES: every Kor.*.dll in the publish folder, by ProductVersion, read
    back off the server over SMB after the service restarts. Any mismatch is a
    terminating error that names the file.

    WHAT IT DOES NOT COVER, stated so nobody reads the name and stops looking:
      * It does not verify third-party assemblies, appsettings, or static content —
        only our own Kor.* code.
      * It proves the FILE landed, not that the service loaded it. The service is
        started and polled for RUNNING, but a binary that throws on first use still
        reports RUNNING here.
      * It does not check that ingestion still works afterwards. On 2026-10-09 a
        deploy whose assemblies all landed correctly dropped 15 opportunities for
        forty minutes, because the fault was a null column no deploy check would see.
        After deploying, look at IngestionRuns.FailedCount — see the footer it prints.
      * ⚠ A same-class fault it would NOT catch: a publish built from the wrong
        commit. The versions would match each other and the server perfectly, because
        they all came from the same wrong build. publish-opportunities.ps1 stamps the
        git SHA into ProductVersion; this script prints it so a human can check it,
        and that is the only defence.

.PARAMETER PublishPath
    The publish folder to deploy. Defaults to the path recorded by
    publish-opportunities.ps1 in output/_last-worker-publish.txt.
#>
[CmdletBinding()]
param(
    [string]$PublishPath,
    [string]$ComputerName = 'KOR-APP01',
    [string]$ServiceName  = 'Kor.Opportunities.Worker'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$handoff  = Join-Path $repoRoot 'output\_last-worker-publish.txt'

if (-not $PublishPath) {
    if (-not (Test-Path $handoff)) {
        throw "No -PublishPath given and no handoff file at $handoff. Run publish-opportunities.ps1 first."
    }
    $PublishPath = (Get-Content $handoff -Raw).Trim()
}

$workerDll = Join-Path $PublishPath 'Kor.Opportunities.Worker.dll'
if (-not (Test-Path $workerDll)) { throw "Publish source missing: $workerDll" }

$dest     = "\\$ComputerName\C`$\Program Files\KorOperations\Opportunities"
$destLocal= 'C:\Program Files\KorOperations\Opportunities'

# Every assembly of OURS in the publish. This is the set that must land.
$ours = Get-ChildItem $PublishPath -Filter 'Kor.*.dll' -File | Sort-Object Name
if ($ours.Count -eq 0) { throw "No Kor.*.dll found in $PublishPath." }

Write-Host "Deploying $PublishPath" -ForegroundColor Cyan
Write-Host ("  {0} assemblies of ours; Worker ProductVersion {1}" -f `
    $ours.Count, (Get-Item $workerDll).VersionInfo.ProductVersion) -ForegroundColor Cyan
Write-Host ''

# PRE: what is on the server now, so the diff is visible rather than asserted.
$pre = @{}
foreach ($f in $ours) {
    $onServer = Join-Path $dest $f.Name
    $pre[$f.Name] = if (Test-Path $onServer) { (Get-Item $onServer).VersionInfo.ProductVersion } else { '(absent)' }
}

Write-Host "Stopping $ServiceName on $ComputerName (poll on sc.exe text + proc-gone)..." -ForegroundColor Cyan
Invoke-Command -ComputerName $ComputerName -ArgumentList $ServiceName -ScriptBlock {
    param($svc)
    sc.exe stop $svc | Out-Null
    $tries = 0
    do {
        Start-Sleep 1; $tries++
        $line     = ((sc.exe query $svc) -join ' ')
        $stopped  = $line -match '\bSTOPPED\b'
        $procGone = -not (Get-Process -Name $svc -ErrorAction SilentlyContinue)
        if ($tries -gt 60) { throw "Timed out waiting for $svc to stop" }
    } until ($stopped -and $procGone)
    Get-Process -Name $svc -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 8   # handle release
}

Write-Host 'Robocopy /MIR (excluding logs and production config)...' -ForegroundColor Cyan
robocopy $PublishPath $dest /MIR /R:3 /W:2 /XD logs /XF appsettings.Production.json | Out-Null
$rc = $LASTEXITCODE
# ⚠ robocopy 0-7 are SUCCESS codes (1 = files copied). Letting $LASTEXITCODE stand
#    made every successful deploy exit 1 and look like a failure in the caller.
if ($rc -ge 8) { throw "robocopy failed with exit code $rc." }
Write-Host ("Robocopy exit $rc (0-7 = success)") -ForegroundColor Green

Write-Host "Starting $ServiceName..." -ForegroundColor Cyan
Invoke-Command -ComputerName $ComputerName -ArgumentList $ServiceName -ScriptBlock {
    param($svc) sc.exe start $svc | Out-Null
}
Start-Sleep 5

$running = Invoke-Command -ComputerName $ComputerName -ArgumentList $ServiceName -ScriptBlock {
    param($svc) ((sc.exe query $svc) -join ' ') -match '\bRUNNING\b'
}

# POST: read every assembly back off the server over SMB and compare ProductVersion.
# SMB rather than Invoke-Command on purpose: the verification still works when WinRM
# does not, and reading a file is not executing a process on someone else's machine.
Write-Host ''
Write-Host 'Verifying every Kor.* assembly landed:' -ForegroundColor Cyan
$mismatch = [System.Collections.Generic.List[string]]::new()
foreach ($f in $ours) {
    $onServer = Join-Path $dest $f.Name
    $want = $f.VersionInfo.ProductVersion
    $got  = if (Test-Path $onServer) { (Get-Item $onServer).VersionInfo.ProductVersion } else { '(absent)' }
    $ok   = ($got -eq $want)
    if (-not $ok) { $mismatch.Add("$($f.Name): server '$got' != publish '$want'") }
    if (-not $ok -or $pre[$f.Name] -ne $want) {
        Write-Host ("  {0} {1}  {2} -> {3}" -f ($ok ? 'OK  ' : 'FAIL'), $f.Name.PadRight(34), $pre[$f.Name], $got) `
            -ForegroundColor ($ok ? 'Green' : 'Red')
    }
}
$changed = ($ours | Where-Object { $pre[$_.Name] -ne $_.VersionInfo.ProductVersion }).Count
Write-Host ("  {0} of {1} assemblies verified; {2} changed by this deploy." -f `
    ($ours.Count - $mismatch.Count), $ours.Count, $changed)

Write-Host ("service RUNNING: {0}" -f $running) -ForegroundColor ($running ? 'Green' : 'Red')

if ($mismatch.Count -gt 0) {
    throw "DEPLOY NOT VERIFIED - $($mismatch.Count) assembly(ies) did not land:`n  " + ($mismatch -join "`n  ")
}
if (-not $running) { throw "DEPLOY NOT VERIFIED - $ServiceName is not RUNNING on $ComputerName." }

Write-Host ''
Write-Host 'Deploy verified.' -ForegroundColor Green
Write-Host 'NEXT: a green deploy is not a healthy system. Check that ingestion still works:' -ForegroundColor Yellow
Write-Host '  SELECT ProviderName, Success, InsertedCount, FailedCount FROM opportunities.IngestionRuns' -ForegroundColor DarkGray
Write-Host '  WHERE EndedAtUtc IS NOT NULL AND FailedCount > 0' -ForegroundColor DarkGray
Write-Host '    AND StartedAtUtc >= DATEADD(hour, -2, SYSDATETIMEOFFSET());' -ForegroundColor DarkGray
