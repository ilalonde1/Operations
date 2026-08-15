<#
    Measures the rules the DXF→ETABS generator applies against the ETABS models KOR engineers
    have actually built.

    Every threshold in analysis.vw_RuleSetting is currently justified by two buildings. A number
    that is right for those two and wrong for the rest of the portfolio is a one-off wearing a
    constant's clothing, and nothing in the test suite can tell the difference. This reads the
    real models and reports what engineers DO, so each rule can be confirmed, corrected, or
    turned into a question.

    RUN THIS ON THE FILE SERVER. Over SMB it is the directory walk and the per-file round trips
    that cost, not the parsing — locally it is disk. Only the summary comes back.

        .\Measure-EtabsCorpusRules.ps1 -Root D:\Projects\Projects -Out C:\Temp\corpus-rules.txt

    It reads only. It writes one text file, wherever -Out says.
#>
[CmdletBinding()]
param(
    # The local path on the file server that \\Kor-fs01\Projects\Projects points at.
    [Parameter(Mandatory = $true)][string]$Root,

    [string]$Out = (Join-Path $env:TEMP 'kor-etabs-corpus-rules.txt'),

    # 0 reads every model found. Set a number to sample instead.
    [int]$Sample = 0
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Root)) { throw "Root not found: $Root" }

Write-Host "walking $Root ..." -ForegroundColor DarkGray
$files = Get-ChildItem -LiteralPath $Root -Recurse -File -Include '*.e2k', '*.$et' -ErrorAction SilentlyContinue
Write-Host ("  {0} candidate model files" -f $files.Count) -ForegroundColor DarkGray

if ($Sample -gt 0 -and $files.Count -gt $Sample) {
    $files = $files | Get-Random -Count $Sample
    Write-Host ("  sampling {0}" -f $files.Count) -ForegroundColor DarkGray
}

# Anything carrying KOR-generated object names is this tool's own output round-tripped through
# ETABS. It is never evidence about what an engineer draws.
$ourOutput = 0
$read      = 0
$failed    = 0

$wallThickness = @{}          # inches -> count
$colShort      = New-Object System.Collections.Generic.List[double]
$colLong       = New-Object System.Collections.Generic.List[double]
$spandrel      = @{}          # inches -> count
$panelForm     = @{}          # "1 1 0 0" -> count
$openingGap    = @{}          # not derivable from the model; placeholder for symmetry

function Bump($table, $key) {
    if ($table.ContainsKey($key)) { $table[$key]++ } else { $table[$key] = 1 }
}

$i = 0
foreach ($f in $files) {
    $i++
    if ($i % 100 -eq 0) { Write-Host ("  {0}/{1}" -f $i, $files.Count) -ForegroundColor DarkGray }

    try { $text = [System.IO.File]::ReadAllText($f.FullName) }
    catch { $failed++; continue }

    if ($text -match '"K[WCSFO]\d+"') { $ourOutput++; continue }
    $read++

    foreach ($m in [regex]::Matches($text, '(?m)^\s*SHELLPROP\s+"[^"]+"\s+PROPTYPE\s+"Wall".*?THICKNESS\s+([\d.]+)')) {
        Bump $wallThickness ([math]::Round([double]$m.Groups[1].Value))
    }

    foreach ($m in [regex]::Matches($text, '(?m)^\s*FRAMESECTION\s+"[^"]+".*?SHAPE\s+"Concrete Rectangular".*?\sD\s+([\d.]+).*?\sB\s+([\d.]+)')) {
        $d = [double]$m.Groups[1].Value
        $b = [double]$m.Groups[2].Value
        if ($d -gt 0 -and $b -gt 0) {
            $colShort.Add([math]::Min($d, $b))
            $colLong.Add([math]::Max($d, $b))
        }
    }

    # A partial-height panel carries its depth as the third value on its POINT line.
    foreach ($m in [regex]::Matches($text, '(?m)^\s*POINT\s+"[^"]+"\s+\S+\s+\S+\s+([\d.]+)\s*$')) {
        $z = [double]$m.Groups[1].Value
        if ($z -gt 0 -and $z -lt 200) { Bump $spandrel ([math]::Round($z)) }
    }

    foreach ($m in [regex]::Matches($text, '(?m)PANEL\s+4\s+(?:"[^"]+"\s+){4}([\d\s]+?)\s*$')) {
        Bump $panelForm (($m.Groups[1].Value -split '\s+' | Where-Object { $_ }) -join ' ')
    }
}

function Pct($table, $lo, $hi) {
    $total = ($table.Values | Measure-Object -Sum).Sum
    if (-not $total) { return 0 }
    $inside = ($table.GetEnumerator() | Where-Object { $_.Key -ge $lo -and $_.Key -le $hi } |
               ForEach-Object { $_.Value } | Measure-Object -Sum).Sum
    return [math]::Round(100.0 * $inside / $total, 1)
}

function Quantile($list, $q) {
    if ($list.Count -eq 0) { return 0 }
    $sorted = $list | Sort-Object
    return $sorted[[math]::Min($sorted.Count - 1, [int]([math]::Floor($q * $sorted.Count)))]
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("KOR ETABS corpus — what engineers actually draw")
$lines.Add("root: $Root")
$lines.Add("generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$lines.Add("")
$lines.Add("models found        : $($files.Count)")
$lines.Add("engineer models read: $read")
$lines.Add("skipped, our output : $ourOutput")
$lines.Add("unreadable          : $failed")
$lines.Add("")

$lines.Add("WALL THICKNESS   rule: dxf.min-wall-thickness 4, dxf.max-wall-thickness 36")
$wtTotal = ($wallThickness.Values | Measure-Object -Sum).Sum
$lines.Add("   $wtTotal wall sections; $(Pct $wallThickness 4 36)% inside 4-36 in")
$outside = $wallThickness.Keys | Where-Object { $_ -lt 4 -or $_ -gt 36 } | Sort-Object
$lines.Add("   outside the rule: $((($outside | Select-Object -First 20) -join ', '))")
$lines.Add("   commonest: $((($wallThickness.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 12 | ForEach-Object { '{0}x{1}' -f $_.Key, $_.Value }) -join '  '))")
$lines.Add("")

$lines.Add("CONCRETE RECTANGULAR COLUMNS   rule: dxf.min-column-size 6, dxf.max-column-size 96, dxf.max-column-aspect 3.0")
if ($colShort.Count) {
    $aspects = New-Object System.Collections.Generic.List[double]
    for ($k = 0; $k -lt $colShort.Count; $k++) { $aspects.Add($colLong[$k] / $colShort[$k]) }
    $lines.Add("   $($colShort.Count) sections")
    $lines.Add("   short side  min $(Quantile $colShort 0) / median $(Quantile $colShort 0.5) / max $(Quantile $colShort 1)")
    $lines.Add("   long side   min $(Quantile $colLong 0) / median $(Quantile $colLong 0.5) / max $(Quantile $colLong 1)")
    $lines.Add("   aspect      median $([math]::Round((Quantile $aspects 0.5),2)) / p95 $([math]::Round((Quantile $aspects 0.95),2)) / max $([math]::Round((Quantile $aspects 1),2))")
    $lines.Add("   short side under 6 : $(($colShort | Where-Object { $_ -lt 6 }).Count)")
    $lines.Add("   long side over 96  : $(($colLong  | Where-Object { $_ -gt 96 }).Count)")
    $lines.Add("   aspect over 3.0    : $(($aspects  | Where-Object { $_ -gt 3.0 }).Count) of $($aspects.Count)")
} else { $lines.Add("   none found") }
$lines.Add("")

$lines.Add("SPANDREL DEPTH   rule: dxf.spandrel-depth-floor 18, dxf.spandrel-depth-ceiling 60")
$spTotal = ($spandrel.Values | Measure-Object -Sum).Sum
$lines.Add("   $spTotal raised joints; $(Pct $spandrel 18 60)% inside 18-60 in")
$lines.Add("   commonest: $((($spandrel.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 12 | ForEach-Object { '{0}x{1}' -f $_.Key, $_.Value }) -join '  '))")
$lines.Add("")

$lines.Add("PANEL STOREY-SPAN FORMS   the tool writes 'n n 0 0'; n>1 spans past another tower's storeys")
foreach ($p in ($panelForm.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 10)) {
    $lines.Add("   {0,-14} {1}" -f $p.Key, $p.Value)
}

$lines | Set-Content -LiteralPath $Out -Encoding UTF8
Write-Host ""
Write-Host "written: $Out" -ForegroundColor Green
$lines | ForEach-Object { Write-Host $_ }
