# Read-only. Finds ETABS-written .e2k files by declared length unit and records their UNITS and MATERIAL lines.
$out = 'C:\Temp\kor-e2k-units-probe.txt'
$want = @{ 'MM' = 3; 'IN' = 1; 'FT' = 1 }
$got  = @{ 'MM' = 0; 'IN' = 0; 'FT' = 0 }
"started $(Get-Date -Format s)" | Set-Content $out
foreach ($f in [System.IO.Directory]::EnumerateFiles('E:\Projects\Projects', '*.e2k', [System.IO.SearchOption]::AllDirectories)) {
    if ($f -match 'FROM-DRAWINGS|pdf-only|KOR-') { continue }
    try { $head = Get-Content -LiteralPath $f -TotalCount 400 -ErrorAction Stop } catch { continue }
    $u = $head | Where-Object { $_ -match '^\s*UNITS\s' } | Select-Object -First 1
    if (-not $u) { continue }
    if ($u -notmatch '"[^"]+"\s+"([^"]+)"') { continue }
    $len = $Matches[1].ToUpper()
    if (-not $want.ContainsKey($len) -or $got[$len] -ge $want[$len]) { continue }
    if (-not ($head -match 'PROGRAM\s+"ETABS"')) { continue }
    $got[$len]++
    Add-Content $out "=== $f"
    Add-Content $out ($head | Where-Object { $_ -match '^\s*(PROGRAM|UNITS|MATERIAL\s.*(FC|WEIGHTPERVOLUME|\sE\s))' })
    if (@($got.Keys | Where-Object { $got[$_] -lt $want[$_] }).Count -eq 0) { break }
}
"done $(Get-Date -Format s)" | Add-Content $out
