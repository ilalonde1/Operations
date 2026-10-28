# Runs the PdfToSafe window's own read + DXF export code (PdfGeometryExtractor in Kor.Operations.App.dll)
# on one page, headlessly, with no colour assignments - exactly what the window does before anyone
# touches the Color Properties grid, except the grid's seeded types (reported separately).
param([string]$Pdf, [int]$Page, [string]$OutDxf, [int]$Scale = 0, [switch]$ByColour)
$bin = 'C:\VIsual Studio Projects\Operations\Kor.Operations.App\bin\Debug\net8.0-windows10.0.19041.0'
[AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
    $n = (New-Object System.Reflection.AssemblyName($e.Name)).Name
    $p = Join-Path $bin "$n.dll"; if (Test-Path $p) { [Reflection.Assembly]::LoadFrom($p) } else { $null } })
$app = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'Kor.Operations.App.dll'))
$x = $app.GetType('Kor.Operations.EngineeringTools.PdfToSafe.PdfGeometryExtractor', $true)
$detected = $x.GetMethod('DetectScaleForLoad').Invoke($null, @($Pdf, $Page))
"scale detected: 1:$($detected.Denominator) from $($detected.Source)"
$extract = $x.GetMethods() | Where-Object { $_.Name -eq 'Extract' -and $_.GetParameters()[0].ParameterType -eq [string] }
$den = if ($Scale -gt 0) { $Scale } else { [int]$detected.Denominator }
"reading at 1:$den"
$args7 = @($Pdf, $den, $Page) + ($extract.GetParameters() | Select-Object -Skip 3 | ForEach-Object { $_.DefaultValue })
$geo = $extract.Invoke($null, $args7)
"app read: slabs $($geo.Slabs.Count)  columns $($geo.Columns.Count)  walls $($geo.Walls.Count)  lines $($geo.Lines.Count)  text $($geo.TextAnnotations.Count)"
$exp = $x.GetMethod('ExportDxf')
$pars = $exp.GetParameters() | ForEach-Object { if ($_.Position -eq 0) { $geo } elseif ($_.Position -eq 1) { $OutDxf } elseif ($_.Name -eq 'layerByColour') { [bool]$ByColour } else { $_.DefaultValue } }
$exp.Invoke($null, [object[]]$pars) | Out-Null
"written: $OutDxf"
