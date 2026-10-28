# 31065: which other step moves it. Three arms IN PARALLEL, each with 154 off plus one suspect off; local mirror only.
$T      = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe'
$base   = 'C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.Core.Tests\Baselines\pdf-only-31065-01.e2k'
$root   = 'C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\attr'
$pdf    = (Get-ChildItem (Join-Path $env:TEMP 'kor-drawings\B651AF964254AC54') -Filter *.pdf | Select-Object -First 1).FullName
$log    = Join-Path $root 'attr31065.log'
"started $(Get-Date -Format s)" | Set-Content $log
$arms = @{ '149' = @('KOR_STEP149_OFF'); '150' = @('KOR_STEP150_OFF'); '152-153' = @('KOR_STEP152_OFF', 'KOR_STEP153_OFF') }
$jobs = foreach ($name in $arms.Keys) {
    $w = Join-Path $root "31065-154+$name-off"
    New-Item -ItemType Directory -Force $w | Out-Null
    Start-Job -ArgumentList $T, $pdf, $w, $base, $env:KOR_ENGINEERINGTOOLS_STANDARDSDB, $log, $name, $arms[$name] -ScriptBlock {
        param($T, $pdf, $w, $base, $conn, $log, $name, $offs)
        $env:KOR_STEP154_OFF = '1'
        foreach ($o in $offs) { Set-Item "Env:$o" '1' }
        & $T stickfile $pdf $w --scale 100 --rules-db $conn --no-render --no-questions *> "$w-run.txt"
        $diff = (& $T model-diff $base (Join-Path $w 'out.e2k') 2>&1 | Select-Object -Last 1)
        "31065  154 + $name off  exit $LASTEXITCODE  -> $diff" | Add-Content $log
    }
}
$jobs | Wait-Job | Out-Null
"finished $(Get-Date -Format s)" | Add-Content $log
