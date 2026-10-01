#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes Kor.Operations.NetworkOps.Service (with its endpoint agent) and deploys it to KOR-APP01. Run from KOR-1001.

.DESCRIPTION
    publish -> stop the service (wait) -> robocopy /MIR -> verify EVERY file by SHA-256 -> delayed-auto start -> start.
    The agent package (the "agent" folder) travels inside the publish: deploying the service is what makes a new
    agent version available to install. Bump <Version> in the service csproj first: /api/ping reports it, so the
    deploy can be confirmed from outside.

    Service account, environment variables and the API certificate are set on APP01 once and are not touched here
    (docs/runbooks/Kor.Operations.NetworkOps.Agent.md, docs/KOR-NetworkOps-Design-2026-09-28.md).

.EXAMPLE
    .\publish-networkops.ps1
    curl.exe -sk https://KOR-APP01.int.korstructural.com:8445/api/ping
#>
[CmdletBinding()]
param(
    [string]$Server = 'KOR-APP01',
    [string]$Stage = (Join-Path $env:TEMP 'networkops-service-publish')
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$target = "\\$Server\C$\Program Files\KorOperations\NetworkOps"
$svc = 'Kor.Operations.NetworkOps'

'--- publish'
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force -Confirm:$false }
dotnet publish "$repo\Kor.Operations.NetworkOps.Service" -c Release -o $Stage -v q 2>&1 | Select-String ' error |Error\(s\)'
if (-not (Test-Path "$Stage\Kor.Operations.NetworkOps.Service.dll")) { throw 'publish produced no service dll' }
if (-not (Test-Path "$Stage\agent\Kor.Operations.NetworkOps.Agent.exe")) { throw 'publish has no agent package' }

'--- stop'
sc.exe "\\$Server" stop $svc | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
do { Start-Sleep -Seconds 2; $state = (sc.exe "\\$Server" query $svc | Select-String 'STATE').ToString() } while ($state -notmatch 'STOPPED' -and $sw.Elapsed.TotalSeconds -lt 90)
if ($state -notmatch 'STOPPED') { throw "service did not stop in 90 s: $state" }
"stopped after $([int]$sw.Elapsed.TotalSeconds) s"
# The process can outlive STOPPED by a moment and hold the dlls.
$sw.Restart()
while ((Get-CimInstance Win32_Process -ComputerName $Server -Filter "Name='Kor.Operations.NetworkOps.Service.exe'" -ErrorAction SilentlyContinue) -and $sw.Elapsed.TotalSeconds -lt 30) { Start-Sleep -Seconds 1 }

'--- copy'
robocopy $Stage $target /MIR /R:3 /W:2 /NP /NFL /NDL /NJH | Select-String 'Files :|Dirs :'
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }

'--- verify every file by hash'
$bad = @(Get-ChildItem $Stage -File -Recurse | Where-Object {
    $rel = $_.FullName.Substring($Stage.Length)
    (Get-FileHash $_.FullName).Hash -ne (Get-FileHash (Join-Path $target $rel) -ErrorAction SilentlyContinue).Hash
})
"files staged $((Get-ChildItem $Stage -File -Recurse).Count), mismatched $($bad.Count)"
if ($bad.Count) { $bad | Select-Object -First 5 FullName; throw 'hash mismatch: deploy NOT verified' }

'--- start type + start'
sc.exe "\\$Server" config $svc start= delayed-auto | Out-Null
sc.exe "\\$Server" start $svc | Out-Null
Start-Sleep -Seconds 15
sc.exe "\\$Server" query $svc | Select-String 'STATE'
'ping: ' + (curl.exe -sk "https://$Server.int.korstructural.com:8445/api/ping")
