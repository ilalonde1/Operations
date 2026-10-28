# Full Core suite after the corner rule (a reader change: six-set gate, coverage ratchets, fixtures).
$out  = 'C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\verifyA2A3d'
$core = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core.Tests\Kor.Operations.EngineeringTools.Core.Tests.csproj'
$log  = Join-Path $out 'full.log'
"started $(Get-Date -Format s)" | Set-Content $log
dotnet build $core -c Debug *> (Join-Path $out 'full-build.txt')
"build exit $LASTEXITCODE" | Add-Content $log
if ($LASTEXITCODE -ne 0) { 'STOPPED: build failed' | Add-Content $log; exit 1 }
dotnet test $core --no-build -c Debug --blame-hang-timeout 20m --blame-hang-dump-type none --logger "trx;LogFileName=$out\full.trx" *> (Join-Path $out 'full-test.txt')
"test exit $LASTEXITCODE" | Add-Content $log
Select-String -Path (Join-Path $out 'full-test.txt') -Pattern '^\s+Failed |Passed!|Failed!' | ForEach-Object { $_.Line.Trim() } | Add-Content $log
"finished $(Get-Date -Format s)" | Add-Content $log
