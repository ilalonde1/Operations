# net_probe.ps1 -Dxf <path> -X <mm> -Y <mm> [-R <mm>] : one sheet classified with default options in its own unit (as
# takeoff network-check does), printing walls before the network, walls after, and openings within R of the point.
# Loads a COPY of the repo's built Core.dll so the repo bin is not locked.
param([string]$Dxf, [double]$X, [double]$Y, [double]$R = 2500)
$src = "C:\VIsual Studio Projects\Operations\Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0"
$bin = Join-Path $PSScriptRoot ("probe-bin-" + (Get-Date -Format HHmmss))
Copy-Item $src $bin -Recurse
Add-Type -Path (Join-Path $bin "Kor.Operations.EngineeringTools.Core.dll")
$unit = [Kor.Operations.EngineeringTools.Dxf.DxfPlanReader]::UnitInInches($Dxf)
$segs = [Kor.Operations.EngineeringTools.Dxf.DxfPlanReader]::ReadSegments($Dxf)
$opt = [Kor.Operations.EngineeringTools.Dxf.PlanClassificationOptions]::new()
if ($unit -and [Math]::Abs($unit - 1.0) -gt 1e-12) { $opt = $opt.InUnitOf($unit) }
$mm = $unit * 25.4
$g = [Kor.Operations.EngineeringTools.Dxf.StructuralPlanClassifier]::Classify($segs, $opt, $null, $null)
function Near($p) { [Math]::Sqrt([Math]::Pow($p.X * $mm - $X, 2) + [Math]::Pow($p.Y * $mm - $Y, 2)) -le $R }
function Show($tag, $s, $e, $t) { $b = [Math]::Atan2($e.Y - $s.Y, $e.X - $s.X) * 180 / [Math]::PI; if ($b -lt 0) { $b += 180 }; "{0,-7} ({1,7:0},{2,7:0})-({3,7:0},{4,7:0}) t{5,4:0} bearing {6,6:0.00}" -f $tag, ($s.X * $mm), ($s.Y * $mm), ($e.X * $mm), ($e.Y * $mm), ($t * $mm), $b }
"unit $unit in; segments $($segs.Count); walls before $($g.WallsBeforeNetwork.Count), after $($g.Walls.Count), openings $($g.WallOpenings.Count)"
foreach ($w in $g.WallsBeforeNetwork) { if ((Near $w.Start) -or (Near $w.End)) { Show "before" $w.Start $w.End $w.Thickness } }
foreach ($w in $g.Walls) { if ((Near $w.Start) -or (Near $w.End)) { Show "after" $w.Start $w.End $w.Thickness } }
foreach ($o in $g.WallOpenings) { if ((Near $o.Start) -or (Near $o.End)) { Show "OPENING" $o.Start $o.End $o.Thickness } }
