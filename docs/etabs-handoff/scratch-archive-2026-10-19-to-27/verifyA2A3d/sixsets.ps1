# The six-set gate, local mirror only (steps 149-154 in the tree). Writes one line per result to sixsets.log.
$core = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core.Tests\Kor.Operations.EngineeringTools.Core.Tests.csproj'
$dir  = 'C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\verifyA2A3d'
$log  = Join-Path $dir 'sixsets.log'
"started $(Get-Date -Format s)" | Set-Content $log
dotnet test $core --no-build -c Debug --filter 'FullyQualifiedName~SixSetsBuildAsBanked' --blame-hang-timeout 45m --blame-hang-dump-type none *> (Join-Path $dir 'sixsets-154.txt')
"test exit $LASTEXITCODE" | Add-Content $log
Select-String -Path (Join-Path $dir 'sixsets-154.txt') -Pattern '^\s+Failed |Passed!|Failed!' | ForEach-Object { $_.Line.Trim() } | Add-Content $log
"finished $(Get-Date -Format s)" | Add-Content $log
