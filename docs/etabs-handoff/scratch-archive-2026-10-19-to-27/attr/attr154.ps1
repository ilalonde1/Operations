# Step 154 off, the three remaining moved sets IN PARALLEL, each from its LOCAL mirror; diff against its baseline.
$T      = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe'
$base   = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core.Tests\Baselines'
$root   = 'C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\attr'
$mirror = Join-Path $env:TEMP 'kor-drawings'
$log    = Join-Path $root 'attr154.log'
"started $(Get-Date -Format s)" | Set-Content $log
$sets = @(
    @{ Job = '31202-01';      Key = '7F7A2A7B7340E651'; Scale = 96 },
    @{ Job = '31170-01-arch'; Key = 'E63C9B7119BB828B'; Scale = 96 },
    @{ Job = '31065-01';      Key = 'B651AF964254AC54'; Scale = 100 }
)
$jobs = foreach ($s in $sets) {
    $pdf = (Get-ChildItem (Join-Path $mirror $s.Key) -Filter *.pdf | Select-Object -First 1).FullName
    $w = Join-Path $root "$($s.Job)-KOR_STEP154_OFF"
    New-Item -ItemType Directory -Force $w | Out-Null
    Start-Job -ArgumentList $T, $pdf, $w, $s.Scale, $s.Job, $base, $env:KOR_ENGINEERINGTOOLS_STANDARDSDB, $log -ScriptBlock {
        param($T, $pdf, $w, $scale, $job, $base, $conn, $log)
        $env:KOR_STEP154_OFF = '1'
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & $T stickfile $pdf $w --scale $scale --rules-db $conn --no-render --no-questions *> "$w-run.txt"
        $code = $LASTEXITCODE
        $diff = (& $T model-diff (Join-Path $base "pdf-only-$job.e2k") (Join-Path $w 'out.e2k') 2>&1 | Select-Object -Last 1)
        "$job  KOR_STEP154_OFF  exit $code  $([int]$sw.Elapsed.TotalSeconds)s  -> $diff" | Add-Content $log
    }
}
$jobs | Wait-Job | Out-Null
"finished $(Get-Date -Format s)" | Add-Content $log
