# Writes the AShapeTypedOpeningCutsAHoleTests drawing through the REAL app writers (built test output),
# so the files can be drawn and looked at. Args: <bin folder> <out folder>
param([string]$Bin, [string]$Out)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $Bin 'Kor.Operations.EngineeringTools.Core.dll')
$app = [System.Reflection.Assembly]::LoadFrom((Join-Path $Bin 'Kor.Operations.App.dll'))
$ns = 'Kor.Operations.EngineeringTools.PdfToSafe'
$geoT = $app.GetType("$ns.PdfGeometryExtractor", $true)
$f2kT = $app.GetType("$ns.SafeF2kExporter", $true)
$e2kT = $app.GetType("$ns.EtabsE2kExporter", $true)
$setT = $app.GetType("$ns.ExportSettings", $true)
$csT  = [Kor.Operations.EngineeringTools.PdfToSafe.SlabColorSettings]

function Box($x0, $y0, $x1, $y1) {
    $l = [System.Collections.Generic.List[System.ValueTuple[double,double]]]::new()
    foreach ($p in @(@($x0,$y0), @($x1,$y0), @($x1,$y1), @($x0,$y1))) { $l.Add([System.ValueTuple[double,double]]::new($p[0], $p[1])) }
    ,$l
}
function Rgb($r, $g, $b) { [System.ValueTuple[byte,byte,byte]]::new($r, $g, $b) }
$slab = Rgb 0 0 255; $hole = Rgb 255 0 255

$g = [Kor.Operations.EngineeringTools.PdfToSafe.ExtractedGeometry]::new()
$g.IsVectorPdf = $true
$g.Slabs.Add((Box 0 0 10000 10000)); $g.SlabColors.Add($slab)
$g.Slabs.Add((Box 1000 1000 3000 3000)); $g.SlabColors.Add($hole)
$g.Slabs.Add((Box 20000 0 22000 2000)); $g.SlabColors.Add($hole)
foreach ($s in @(@(6000,1000,8000,1000), @(8000,1000,8000,3000), @(8000,3000,6000,3000), @(6000,3000,6000,1000))) {
    $l = [System.Collections.Generic.List[System.ValueTuple[double,double]]]::new()
    $l.Add([System.ValueTuple[double,double]]::new($s[0], $s[1])); $l.Add([System.ValueTuple[double,double]]::new($s[2], $s[3]))
    $g.Lines.Add($l); $g.LineColors.Add($hole); $g.LineSectionHints.Add($null)
}
$g.Columns.Add([System.ValueTuple[double,double]]::new(7000, 7000)); $g.ColumnColors.Add($hole)
$g.ColumnSizes.Add([System.ValueTuple[double,double]]::new(600, 600))

$types = [System.Collections.Generic.Dictionary[System.ValueTuple[byte,byte,byte], Kor.Operations.EngineeringTools.PdfToSafe.SlabColorSettings]]::new()
$a = $csT::new(); $a.ElementType = 'Slab'; $a.ThicknessMm = 250; $a.GradeCode = 'C30'; $types[$slab] = $a
$b = $csT::new(); $b.ElementType = 'Opening'; $b.ThicknessMm = 250; $b.GradeCode = 'C30'; $types[$hole] = $b

$reclass = $geoT.GetMethod('ReclassifyByColor').Invoke($null, @($g, $types, $null, $null, $null, 2000000.0))
"reclassified: slabs=$($reclass.Slabs.Count) lines=$($reclass.Lines.Count) columns=$($reclass.Columns.Count) openings=$($reclass.CarriedOpenings.Count)"

$settings = [Activator]::CreateInstance($setT); $settings.AutoGenerateOpeningsFromWalls = $false
$single = $f2kT.GetMethods() | Where-Object { $_.Name -eq 'Export' -and $_.GetParameters()[0].ParameterType.Name -eq 'ExtractedGeometry' }
[string]$f2kPath = [System.IO.Path]::Combine($Out, 'opening.f2k')
[string]$e2kPath = [System.IO.Path]::Combine($Out, 'opening.e2k')
[void]$single.Invoke($null, [object[]]@($reclass, $f2kPath, $null, $null, $null, $null, $types, $settings))
$e2k = $e2kT.GetMethods() | Where-Object { $_.Name -eq 'Export' -and $_.GetParameters()[1].ParameterType.Name -eq 'ExtractedGeometry' }
[void]$e2k.Invoke($null, [object[]]@($e2kPath, $reclass, $types, $settings))
"wrote opening.f2k and opening.e2k"
