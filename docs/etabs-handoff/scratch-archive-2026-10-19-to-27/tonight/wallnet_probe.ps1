# wallnet_probe.ps1 : WallNetwork.Connect on Halifax LEVEL 1's 12 wall centrelines (VD's 13:58 DXF, rectangles read by
# ezdxf, centreline = the long axis through the middle, inches), from the copied takeoff bin - so the passes can be watched
# without a compile while the suite holds the build output. Prints each axis in and out, in mm.
param([string]$Drop = "")
$bin = Join-Path $PSScriptRoot "takeoff-bin"
Add-Type -Path (Join-Path $bin "Kor.Operations.EngineeringTools.Core.dll")
$mm = 25.4
# n, x1, y1, x2, y2 (mm), thickness (mm) - from the outline extents printed at 14:03
$rows = @(
  @(1, 45830, 27981, 45830, 35905, 508), @(2, 40369, 36053, 43173, 36053, 610), @(3, 44142, 36053, 46116, 36053, 610),
  @(4, 40369, 27901, 43173, 27901, 610), @(5, 44142, 27901, 46116, 27901, 610), @(6, 38422, 30341, 38422, 33582, 508),
  @(7, 42997.5, 27849, 42997.5, 35990, 305), @(8, 44362, 29349, 44362, 34517, 152), @(9, 38421.5, 28082, 38421.5, 29383, 610),
  @(10, 38147, 36053, 39174, 36053, 610), @(11, 38422, 34612, 38422, 35912, 508), @(12, 38147, 27901, 39174, 27901, 610))
$skip = $Drop -split "," | Where-Object { $_ } | ForEach-Object { [int]$_ }
$list = [System.Collections.Generic.List[Kor.Operations.EngineeringTools.Dxf.WallAxis]]::new()
foreach ($r in $rows) {
  if ($skip -contains $r[0]) { continue }
  $a = [Kor.Operations.EngineeringTools.Dxf.DxfPoint]::new($r[1] / $mm, $r[2] / $mm)
  $b = [Kor.Operations.EngineeringTools.Dxf.DxfPoint]::new($r[3] / $mm, $r[4] / $mm)
  $list.Add([Kor.Operations.EngineeringTools.Dxf.WallAxis]::new($a, $b, $r[5] / $mm, "W$($r[0])"))
}
$out = [Kor.Operations.EngineeringTools.Dxf.WallNetwork]::Connect($list)
foreach ($w in $out) {
  "{0,-4} t{1,3:0} ({2,6:0},{3,6:0})-({4,6:0},{5,6:0})" -f $w.Layer, $w.Thickness, ($w.Start.X * $mm), ($w.Start.Y * $mm), ($w.End.X * $mm), ($w.End.Y * $mm)
}
"$($out.Count) axes out of $($list.Count) in"
