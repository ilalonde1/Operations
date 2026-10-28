# classify_probe.ps1 <dxf> : StructuralPlanClassifier.Classify on one sheet (mm drawing scaled to inches, as DxfToEtabsService
# does), with ConnectWalls off and on, printing every wall axis in mm - from the copied takeoff bin, no compile needed.
# COVERS: the classifier with DEFAULT PlanClassificationOptions (no KorStandards rules, no sheet info, no tags). DOES NOT: the
# rules-backed options a real run passes, the composer's joint merge, storey stacking.
# -Unit in (default) or ft: the model unit the service would classify in (an FT e2k scales segments to feet and calls
# options.InUnitOf(12), DxfToEtabsService.cs:976-980).
param([string]$Dxf, [string]$Unit = "in")
$bin = Join-Path $PSScriptRoot "takeoff-bin"
Add-Type -Path (Join-Path $bin "Kor.Operations.EngineeringTools.Core.dll")
$mm = if ($Unit -eq "ft") { 304.8 } else { 25.4 }
$segs = [Kor.Operations.EngineeringTools.Dxf.DxfPlanReader]::ReadSegments($Dxf)
$scaled = [System.Collections.Generic.List[Kor.Operations.EngineeringTools.Dxf.DxfSegment]]::new()
foreach ($g in $segs) {
  $s = [Kor.Operations.EngineeringTools.Dxf.DxfSegment]::new($g.Layer,
        [Kor.Operations.EngineeringTools.Dxf.DxfPoint]::new($g.Start.X / $mm, $g.Start.Y / $mm),
        [Kor.Operations.EngineeringTools.Dxf.DxfPoint]::new($g.End.X / $mm, $g.End.Y / $mm))
  $scaled.Add($s)
}
"segments read: $($segs.Count)"
foreach ($connect in @($false, $true)) {
  $opt = [Kor.Operations.EngineeringTools.Dxf.PlanClassificationOptions]::new()
  if ($Unit -eq "ft") { $opt = $opt.InUnitOf(12.0) }
  $prop = $opt.GetType().GetProperty("ConnectWalls"); $prop.SetValue($opt, $connect)
  $r = [Kor.Operations.EngineeringTools.Dxf.StructuralPlanClassifier]::Classify($scaled, $opt, $null, $null)
  "== ConnectWalls $connect : $($r.Walls.Count) walls, $($r.WallOpenings.Count) openings"
  foreach ($w in $r.Walls) {
    "  t{0,3:0} ({1,6:0},{2,6:0})-({3,6:0},{4,6:0})" -f $w.Thickness, ($w.Start.X * $mm), ($w.Start.Y * $mm), ($w.End.X * $mm), ($w.End.Y * $mm)
  }
  foreach ($o in $r.WallOpenings) { "  opening ({0,6:0},{1,6:0})-({2,6:0},{3,6:0})" -f ($o.Start.X * $mm), ($o.Start.Y * $mm), ($o.End.X * $mm), ($o.End.Y * $mm) }
}
