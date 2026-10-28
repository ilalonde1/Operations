# Writes what the app's two rebar-compare buttons write (no weights entered): the change workbook from the page-text
# pipeline (RebarChangeService.Compare -> BuildXlsx) and the highlighted PDF (RebarOverlayGenerator.Build).
# Args: <Core bin folder> <before.pdf> <after.pdf> <out folder>
param([string]$Bin, [string]$Before, [string]$After, [string]$Out)
$ErrorActionPreference = 'Stop'
Get-ChildItem $Bin -Filter *.dll | Where-Object { $_.Name -match '^(UglyToad|Kor\.Operations\.EngineeringTools\.Core|ClosedXML|DocumentFormat|ExcelNumberFormat|SixLabors|Irony|XLParser|RBush)' } | ForEach-Object { try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch {} }
$core = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Kor.Operations.EngineeringTools.Core' }
$t = { param($n) $core.GetTypes() | Where-Object { $_.Name -eq $n } | Select-Object -First 1 }
$metric = [Enum]::Parse((& $t 'UnitSystem'), 'Metric')
$reader = & $t 'PdfPageTextReader'
$bp = $reader.GetMethod('ReadPages', [type[]]@([string])).Invoke($null, @($Before))
$ap = $reader.GetMethod('ReadPages', [type[]]@([string])).Invoke($null, @($After))
$change = (& $t 'RebarChangeService').GetMethod('Compare').Invoke($null, [object[]]@($bp, $ap, 'IFT', 'IFC', $metric))
$xlsx = (& $t 'RebarChangeReportGenerator').GetMethod('BuildXlsx').Invoke($null, [object[]]@($change, '31065'))
[IO.File]::WriteAllBytes((Join-Path $Out 'app - Rebar Change Detection.xlsx'), $xlsx)
$build = (& $t 'RebarOverlayGenerator').GetMethods() | Where-Object { $_.Name -eq 'Build' -and $_.GetParameters()[0].ParameterType -eq [string] }
$pdf = $build.Invoke($null, [object[]]@($Before, $After, '31065', 'IFT', 'IFC', $metric))
[IO.File]::WriteAllBytes((Join-Path $Out 'app - Rebar Changes (highlighted).pdf'), $pdf)
"wrote workbook $($xlsx.Length) bytes, highlighted PDF $($pdf.Length) bytes"
