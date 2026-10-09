# One read-only command for SQL coverage/parity, predicate preservation, UI selection
# reachability, and the running App's actual reveal interaction and screenshots.
# Requires the current Debug App build running with its BD Opportunities screen open.
# Does not validate business scope or deployment on other PCs. A mislabeled source's
# metadata can make view/hub agree on the wrong population; this is not an ingestion gate.
param([Parameter(Mandatory=$true)][int]$AppProcessId)
$ErrorActionPreference = 'Stop'
& pwsh -NoProfile -File "$PSScriptRoot/Measure.ps1" -Verify
if ($LASTEXITCODE) { throw 'SQL coverage/parity gate failed.' }
& dotnet test Kor.Opportunities.Data.Tests/Kor.Opportunities.Data.Tests.csproj -c Debug --no-build --filter 'Speed!=Slow' -nologo
if ($LASTEXITCODE) { throw 'Data tests failed.' }
$reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine','KOR-APP01')
$key = $reg.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Environment')
$env:KOR_HUB_VERIFY_DB = $key.GetValue('KOR_OPPORTUNITIES_OPPORTUNITIESDB')
try {
    & dotnet test Kor.Operations.App/Kor.Transmittals.App.Tests/Kor.Operations.App.Tests.csproj -c Debug --no-build --filter 'FullyQualifiedName~OpportunityHubTests|FullyQualifiedName~EveryApplicationUsesTheViewAndOneRevealReachesEverySignal' -nologo --logger 'console;verbosity=normal'
    if ($LASTEXITCODE) { throw 'Hub tests failed.' }
} finally { Remove-Item Env:KOR_HUB_VERIFY_DB; $key.Dispose(); $reg.Dispose() }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot/Verify-App.ps1" -AppProcessId $AppProcessId
if ($LASTEXITCODE) { throw 'Running App reveal gate failed.' }
