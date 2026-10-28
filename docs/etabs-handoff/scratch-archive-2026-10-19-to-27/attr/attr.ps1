# Attribute the six-set moves: each moved set rebuilt from its LOCAL mirror copy with one step off at a time, diffed
# against its banked baseline. One line per arm to attr.log. No share access.
$T     = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe'
$base  = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core.Tests\Baselines'
$root  = 'C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\attr'
$mirror = Join-Path $env:TEMP 'kor-drawings'
$log   = Join-Path $root 'attr.log'
$sets = @(
    @{ Job = '31130-01';      Key = 'A321696C615C72F5'; Scale = 96 },
    @{ Job = '31202-01';      Key = '7F7A2A7B7340E651'; Scale = 96 },
    @{ Job = '31170-01-arch'; Key = 'E63C9B7119BB828B'; Scale = 96 },
    @{ Job = '31065-01';      Key = 'B651AF964254AC54'; Scale = 100 }
)
"started $(Get-Date -Format s)" | Set-Content $log
foreach ($s in $sets) {
    $pdf = Get-ChildItem (Join-Path $mirror $s.Key) -Filter *.pdf | Select-Object -First 1
    foreach ($off in 'KOR_STEP149_OFF', 'KOR_STEP150_OFF', 'KOR_STEP154_OFF') {
        $w = Join-Path $root "$($s.Job)-$off"
        New-Item -ItemType Directory -Force $w | Out-Null
        Set-Item "Env:$off" '1'
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & $T stickfile $pdf.FullName $w --scale $s.Scale --rules-db $env:KOR_ENGINEERINGTOOLS_STANDARDSDB --no-render --no-questions *> "$w-run.txt"
        $code = $LASTEXITCODE
        Remove-Item "Env:$off"
        $diff = (& $T model-diff (Join-Path $base "pdf-only-$($s.Job).e2k") (Join-Path $w 'out.e2k') 2>&1 | Select-Object -Last 1)
        "$($s.Job)  $off  exit $code  $([int]$sw.Elapsed.TotalSeconds)s  -> $diff" | Add-Content $log
    }
}
"finished $(Get-Date -Format s)" | Add-Content $log
