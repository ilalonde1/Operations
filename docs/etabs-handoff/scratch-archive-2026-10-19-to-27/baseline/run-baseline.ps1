# Step 0 baseline, run in a clean checkout (no uncommitted work). Builds once, then the fast and full Core suites
# and the App engineering suite filtered to PdfToSafe with a hang timeout (the unfiltered App suite hangs headless).
param([string]$Repo)
$ErrorActionPreference = 'Continue'
$out  = 'C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\baseline'
$core = Join-Path $Repo 'Kor.Operations.EngineeringTools.Core.Tests\Kor.Operations.EngineeringTools.Core.Tests.csproj'
$app  = Join-Path $Repo 'Kor.Operations.App\EngineeringTools.Tests\Kor.Operations.EngineeringTools.Tests.csproj'
$log  = Join-Path $out 'baseline.log'
"repo $Repo  commit $(git -C $Repo rev-parse --short HEAD)  dirty $((git -C $Repo status --porcelain | Measure-Object).Count)  started $(Get-Date -Format s)" | Set-Content $log

function Step([string]$name, [scriptblock]$cmd) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $cmd *> (Join-Path $out "$name.txt")
    "$name exit $LASTEXITCODE  $([int]$sw.Elapsed.TotalSeconds)s" | Add-Content $log
}

Step 'build-core' { dotnet build $core -c Debug }
Step 'build-app'  { dotnet build $app  -c Debug }
Step 'test-core-fast' { dotnet test $core --no-build -c Debug --filter 'Speed!=Slow' --logger "trx;LogFileName=$out\core-fast.trx" }
Step 'test-app-pdftosafe' { dotnet test $app --no-build -c Debug --filter 'FullyQualifiedName~PdfToSafe' --blame-hang-timeout 120s --logger "trx;LogFileName=$out\app-pdftosafe.trx" }
Step 'test-core-full' { dotnet test $core --no-build -c Debug --blame-hang-timeout 600s --logger "trx;LogFileName=$out\core-full.trx" }
"finished $(Get-Date -Format s)" | Add-Content $log
