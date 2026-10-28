# Runs the app's rebar-compare pipeline (page text -> RebarChangeService.Compare) and the CLI's
# (positioned words -> RebarChangeService.ComparePdfs) on the same two PDFs and prints where they differ.
# Args: <Core bin folder> <before.pdf> <after.pdf> <out folder>
param([string]$Bin, [string]$Before, [string]$After, [string]$Out)
$ErrorActionPreference = 'Stop'
Get-ChildItem $Bin -Filter *.dll | Where-Object { $_.Name -match '^(UglyToad|Kor\.Operations\.EngineeringTools\.Core|ClosedXML|DocumentFormat)' } | ForEach-Object { [void][Reflection.Assembly]::LoadFrom($_.FullName) }
$core = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Kor.Operations.EngineeringTools.Core' }
$svc = $core.GetType('Kor.Operations.EngineeringTools.RebarChange.RebarChangeService', $true)
$reader = $core.GetTypes() | Where-Object { $_.Name -eq 'PdfPageTextReader' } | Select-Object -First 1
$unitT = $core.GetTypes() | Where-Object { $_.Name -eq 'UnitSystem' } | Select-Object -First 1
$metric = [Enum]::Parse($unitT, 'Metric')

$bp = $reader.GetMethod('ReadPages', [type[]]@([string])).Invoke($null, @($Before))
$ap = $reader.GetMethod('ReadPages', [type[]]@([string])).Invoke($null, @($After))
"pages: before $($bp.Count)  after $($ap.Count)"

$app = $svc.GetMethod('Compare').Invoke($null, [object[]]@($bp, $ap, 'IFT', 'IFC', $metric))
$cli = $svc.GetMethod('ComparePdfs').Invoke($null, [object[]]@($Before, $After, 'IFT', 'IFC', $metric))

function Rows($r) { $r.Sheets | ForEach-Object { [pscustomobject]@{ Sheet = $_.Sheet; Status = "$($_.Status)"; B = $_.BeforeCount; A = $_.AfterCount; Added = ($_.Added -join '; '); Removed = ($_.Removed -join '; ') } } }
$ra = @(Rows $app); $rc = @(Rows $cli)
"APP pipeline : sheets compared $($app.SheetsCompared), changed $($app.SheetsChanged), rows $($ra.Count)"
"CLI pipeline : sheets compared $($cli.SheetsCompared), changed $($cli.SheetsChanged), rows $($rc.Count), callouts read $($cli.TotalCalloutsRead)"
$ra | Export-Csv (Join-Path $Out 'app.csv') -NoTypeInformation
$rc | Export-Csv (Join-Path $Out 'cli.csv') -NoTypeInformation

$keys = @($ra.Sheet) + @($rc.Sheet) | Sort-Object -Unique
$diff = 0
foreach ($k in $keys) {
  $x = $ra | Where-Object Sheet -eq $k | Select-Object -First 1
  $y = $rc | Where-Object Sheet -eq $k | Select-Object -First 1
  $sx = if ($x) { "$($x.Status) $($x.B)->$($x.A)" } else { '(absent)' }
  $sy = if ($y) { "$($y.Status) $($y.B)->$($y.A)" } else { '(absent)' }
  if ($sx -ne $sy) { $diff++; "{0,-8} app: {1,-28} cli: {2}" -f $k, $sx, $sy }
}
"sheets where the two pipelines disagree: $diff of $($keys.Count)"
