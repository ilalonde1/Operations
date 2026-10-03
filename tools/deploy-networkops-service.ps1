# Deploys Kor.Operations.NetworkOps.Service to KOR-APP01, run FROM KOR-1001 -- the service runbook, as code:
# publish -> stop (wait for the process too) -> robocopy /MIR -> verify every file by SHA-256 -> delayed-auto start ->
# start -> PROVE it: /api/ping must answer with the version just built (csproj <Version>), or the deploy failed.
#
# Lived in a session scratchpad until 2026-10-02 and deployed 0.2.0 .. 0.15.1 from there; versioned here beside
# deploy-newerforma-app.ps1 (Ian: "ALL THIS MUST BE CODE AND DB BASED"). The target folder holds only the publish output:
# keys, logs and settings that are not in the build live elsewhere (C:\ProgramData\KorOperations\NetworkOps), so /MIR
# deletes nothing that matters. Nothing in flight is protected: check GET /api/actions for Running fixes first -- a restart
# marks a running fix failed (the work on the PC carries on, its result is lost).
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'Kor.Operations.NetworkOps.Service'
$stage = Join-Path $env:TEMP 'networkops-service-publish'
$target = '\\KOR-APP01\C$\Program Files\KorOperations\NetworkOps'
$svc = 'Kor.Operations.NetworkOps'
$ping = 'https://KOR-APP01.int.korstructural.com:8445/api/ping'

$version = ([xml](Get-Content (Join-Path $project 'Kor.Operations.NetworkOps.Service.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'no <Version> in the service csproj' }
"--- publish $version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
dotnet publish $project -c Release -o $stage -v q 2>&1 | Select-String ' error |Error\(s\)'
if (-not (Test-Path "$stage\Kor.Operations.NetworkOps.Service.dll")) { throw 'publish produced no service dll' }

"--- stop"
sc.exe \\KOR-APP01 stop $svc | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
do { Start-Sleep -Seconds 2; $state = (sc.exe \\KOR-APP01 query $svc | Select-String 'STATE').ToString() } while ($state -notmatch 'STOPPED' -and $sw.Elapsed.TotalSeconds -lt 90)
if ($state -notmatch 'STOPPED') { throw "service did not stop in 90 s: $state" }
"stopped after $([int]$sw.Elapsed.TotalSeconds) s"
# The process can outlive STOPPED by a moment and hold the dlls.
$sw.Restart()
while ((Get-CimInstance Win32_Process -ComputerName KOR-APP01 -Filter "Name='Kor.Operations.NetworkOps.Service.exe'" -ErrorAction SilentlyContinue) -and $sw.Elapsed.TotalSeconds -lt 30) { Start-Sleep -Seconds 1 }

"--- copy"
robocopy $stage $target /MIR /R:3 /W:2 /NP /NFL /NDL /NJH | Select-String 'Files :|Dirs :'
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }

"--- verify every file by hash"
$bad = @(Get-ChildItem $stage -File -Recurse | Where-Object {
    $rel = $_.FullName.Substring($stage.Length)
    (Get-FileHash $_.FullName).Hash -ne (Get-FileHash (Join-Path $target $rel) -ErrorAction SilentlyContinue).Hash
})
"files staged $((Get-ChildItem $stage -File -Recurse).Count), mismatched $($bad.Count)"
if ($bad.Count) { $bad | Select-Object -First 5 FullName; throw 'hash mismatch: deploy NOT verified' }

"--- start type + start"
sc.exe \\KOR-APP01 config $svc start= delayed-auto | Out-Null
sc.exe \\KOR-APP01 start $svc | Out-Null

"--- prove it is the new build"
$sw.Restart(); $answer = $null
while ($sw.Elapsed.TotalSeconds -lt 90) {
    try { $answer = (curl.exe -sk --max-time 5 $ping | ConvertFrom-Json) } catch { $answer = $null }
    if ($answer.version -eq $version) { break }
    Start-Sleep -Seconds 3
}
if ($answer.version -ne $version) { throw "the service answers $($answer.version), not ${version}: deploy NOT proven" }
"DEPLOYED ${version}: running, $((Get-ChildItem $stage -File -Recurse).Count) files hash-verified, /api/ping answers $($answer.version) after $([int]$sw.Elapsed.TotalSeconds) s"
