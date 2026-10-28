# Verify brief A3: build both, Core fast + the new Core test, App PdfToSafe filtered, then the six-set gate.
$ErrorActionPreference = 'Continue'
$out  = 'C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\verifyA3'
$core = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core.Tests\Kor.Operations.EngineeringTools.Core.Tests.csproj'
$app  = 'C:\VIsual Studio Projects\Operations\Kor.Operations.App\EngineeringTools.Tests\Kor.Operations.EngineeringTools.Tests.csproj'
$log  = Join-Path $out 'verify.log'
"started $(Get-Date -Format s)" | Set-Content $log
function Step([string]$name, [scriptblock]$cmd) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $cmd *> (Join-Path $out "$name.txt")
    "$name exit $LASTEXITCODE  $([int]$sw.Elapsed.TotalSeconds)s" | Add-Content $log
    return $LASTEXITCODE
}
if ((Step 'build-core' { dotnet build $core -c Debug }) -ne 0) { 'STOPPED: Core build failed' | Add-Content $log; exit 1 }
if ((Step 'build-app'  { dotnet build $app  -c Debug }) -ne 0) { 'STOPPED: App build failed' | Add-Content $log; exit 1 }
Step 'core-fast' { dotnet test $core --no-build -c Debug --filter 'Speed!=Slow' --logger "trx;LogFileName=$out\core-fast.trx" } | Out-Null
Step 'app-pdftosafe' { dotnet test $app --no-build -c Debug --filter 'FullyQualifiedName~PdfToSafe' --blame-hang-timeout 120s --logger "trx;LogFileName=$out\app-pdftosafe.trx" } | Out-Null
Step 'sixsets' { dotnet test $core --no-build -c Debug --filter 'FullyQualifiedName~SixSetsBuildAsBanked' --logger "trx;LogFileName=$out\sixsets.trx" } | Out-Null
"finished $(Get-Date -Format s)" | Add-Content $log
