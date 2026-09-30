# FIX repair-windows: DISM /RestoreHealth then SFC /scannow. Long (15-40 min); the PC stays usable. Plain values.
$dism = & dism.exe /Online /Cleanup-Image /RestoreHealth
$dismExit = $LASTEXITCODE
$sfc = (& sfc.exe /scannow) -join "`n"
# sfc writes UTF-16 to a pipe: collapse the NULs, then keep the verdict line.
$verdict = (($sfc -replace "`0", '') -split "`n" | Where-Object { $_ -match 'Windows Resource Protection' } | Select-Object -Last 1)
[pscustomobject]@{
    Result = "DISM exit $dismExit; SFC: $("$verdict".Trim())"
    DismTail = @($dism | Select-Object -Last 3 | ForEach-Object { "$_".Trim() })
}
