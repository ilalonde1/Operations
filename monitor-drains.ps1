Set-Location "C:\VIsual Studio Projects\Operations"

$drains = @(
    [pscustomobject]@{ Name="vip-van-deep";    Dir="\\KOR-APP01\QueueDrain\vip-van-deep\outputs";    Done=$false },
    [pscustomobject]@{ Name="vip-ab-deep";     Dir="\\KOR-APP01\QueueDrain\vip-ab-deep\outputs";     Done=$false },
    [pscustomobject]@{ Name="vip-target-firms";Dir="\\KOR-APP01\QueueDrain\vip-target-firms\outputs";Done=$false }
)

function Check-Done($dir) {
    $f = Join-Path $dir "_status.json"
    if (-not (Test-Path $f)) { return $false }
    try { $s = Get-Content $f -Raw | ConvertFrom-Json; return $s.state -in @("done", "complete") } catch { return $false }
}

$ticks = 0
while ($true) {
    foreach ($d in $drains | Where-Object { -not $_.Done }) {
        if (Check-Done $d.Dir) {
            Write-Host "$(Get-Date -f 'HH:mm') [$($d.Name)] done — draining..."
            $result = dotnet run --project tools/BdQueueDrainIngest -- --kind honing-orgs --dir $d.Dir 2>&1
            $result | Select-Object -Last 3 | ForEach-Object { Write-Host "  $_" }
            $d.Done = $true
        }
    }
    $pending = @($drains | Where-Object { -not $_.Done })
    if ($pending.Count -eq 0) { break }
    Write-Host "$(Get-Date -f 'HH:mm') waiting: $($pending.Name -join ', ')"
    $ticks++
    if ($ticks -gt 120) { Write-Host "10h timeout"; break }  # 120 x 5min = 10h
    Start-Sleep -Seconds 300
}

$failed = @($drains | Where-Object { -not $_.Done })
if ($failed.Count -gt 0) {
    Write-Host "INCOMPLETE: $($failed.Name -join ', ')"
} else {
    Write-Host "$(Get-Date -f 'HH:mm') All drained — running BdIntelExtract..."
    dotnet run --project tools/BdIntelExtract -- --commit 2>&1 | Select-Object -Last 15
    Write-Host "$(Get-Date -f 'HH:mm') BdIntelExtract complete."
}
