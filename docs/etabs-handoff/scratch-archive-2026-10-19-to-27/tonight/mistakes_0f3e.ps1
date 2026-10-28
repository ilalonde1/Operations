# oversight.Mistake x3 (ee 10-27: both 0f3e4ee4 regressions under ONE class with the invariant check as catch-all; plus the
# unread '17:5x' in board 52's append, which the write's own clock put at 17:43:52) and the correction line on board 52.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$q = $cn.CreateCommand()
$q.CommandText = "SELECT c.name, c.max_length FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'oversight.Mistake') ORDER BY c.column_id"
$r = $q.ExecuteReader(); $len = @{}; while ($r.Read()) { $len[$r[0]] = $r[1] }; $r.Close()
"CaughtBy {0} / MitigationRef {1} / Cost {2} bytes" -f $len['CaughtBy'], $len['MitigationRef'], $len['Cost']
$ins = "INSERT INTO oversight.Mistake (OccurredLocal, Session, What, Cost, ClassCode, SurfaceCode, CaughtBy, Mitigation, MitigationKind, MitigationRef, MitigationOwner, State, RecordedBy) OUTPUT inserted.Id VALUES (@o, N'PDF Ingestor', @w, @c, @k, NULL, @by, @mit, @mk, @ref, @mo, N'owed', N'PDF Ingestor')"
$inv = "The network invariant check (owed, ee 10-27): over every gate set's classified walls plus Halifax, before/after Connect and FindOpenings - no panel's bearing changes by more than 0.5 deg (walls drawn skew judged by their own axes), every header lies on its jambs' axis, no wall is joined only to a near-copy of itself. It must FAIL on 0f3e4ee4 first; it is the catch-all for both."
$rows = @(
  @{ o = '2026-10-27 17:14'; k = 'shared-surface-unchecked'; by = 'check'; mk = 'check'; ref = 'network invariant check (owed); board 54'; mo = 'PDF Ingestor'; mit = $inv
     c = '31087 L6-L9: a column of hers (13 mm) shipped as a 762 mm wall in metric builds from 0f3e4ee4 until fixed'
     w = "0f3e4ee4 (2 mm -> 50.8 mm network tolerance in metric) on a one-building harness: an outline drawn twice ~25 mm apart now joins itself, so a standalone column (31087 L6-L9, hers 13 mm away) is never a stub and stays a wall. Found by the before/after gate pair, after the commit." },
  @{ o = '2026-10-27 17:14'; k = 'shared-surface-unchecked'; by = 'check'; mk = 'check'; ref = 'network invariant check (owed)'; mo = 'PDF Ingestor'; mit = $inv
     c = 'off-axis panels 216 -> 434 in 27 of 56 metric sets (139 crooked headers, +79 crooked walls) until fixed'
     w = "0f3e4ee4 on a one-building harness: SnapNearbyEnds now averages ends up to 50.8 mm apart and FindOpenings finds openings between jambs ~30 mm out of line, so walls with offset centrelines are pulled crooked and headers run crooked between them - the Halifax tilt class at 2 in. Found by the gate pair, after the commit." }
)
foreach ($x in $rows) {
  $m = $cn.CreateCommand(); $m.CommandText = $ins
  foreach ($p in 'o', 'w', 'c', 'k', 'by', 'mit', 'mk', 'ref', 'mo') { [void]$m.Parameters.AddWithValue("@$p", $x[$p]) }
  "mistake: " + $m.ExecuteScalar()
}
$cn.Close(); Get-Date -Format HH:mm:ss
