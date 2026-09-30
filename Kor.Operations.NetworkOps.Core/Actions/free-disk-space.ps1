# FIX free-disk-space: temporary files and caches only -- never user documents. Returns plain values.
$ErrorActionPreference = 'SilentlyContinue'
$before = [math]::Round((Get-PSDrive C).Free / 1GB, 2)
$cut = (Get-Date).AddDays(-2)
$steps = New-Object System.Collections.Generic.List[string]
function Clear-Old($path) {
    $n = 0
    Get-ChildItem -LiteralPath $path -Force -Recurse -File -ErrorAction SilentlyContinue | Where-Object LastWriteTime -lt $cut | ForEach-Object {
        try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop; $n++ } catch { }
    }
    $n
}
$steps.Add("Windows temp: $(Clear-Old "$env:windir\Temp") files")
$userTemp = 0
Get-ChildItem 'C:\Users' -Directory -Force | ForEach-Object { $t = Join-Path $_.FullName 'AppData\Local\Temp'; if (Test-Path -LiteralPath $t) { $userTemp += Clear-Old $t } }
$steps.Add("users' temp: $userTemp files")
Stop-Service wuauserv, bits -Force
$steps.Add("update downloads: $(Clear-Old "$env:windir\SoftwareDistribution\Download") files")
Start-Service wuauserv, bits
try { Delete-DeliveryOptimizationCache -Force -ErrorAction Stop; $steps.Add('delivery-optimization cache cleared') } catch { $steps.Add('delivery-optimization cache: not available') }
$dism = & dism.exe /Online /Cleanup-Image /StartComponentCleanup /Quiet
$steps.Add("component store cleanup: exit $LASTEXITCODE")
$after = [math]::Round((Get-PSDrive C).Free / 1GB, 2)
[pscustomobject]@{ Result = "Freed $([math]::Round($after - $before, 2)) GB on C: ($before -> $after GB free)"; Steps = @($steps) }
