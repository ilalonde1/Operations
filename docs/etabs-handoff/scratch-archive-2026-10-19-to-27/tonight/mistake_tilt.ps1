# oversight.Mistake: 0f3e4ee4 changed every metric model's network tolerance (2 mm -> 50.8 mm) on a check of one building's core;
# the gate pair afterwards found two regressions (duplicate joins itself; offset ends averaged into tilts). Board 52 appended.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$q = $cn.CreateCommand()
$q.CommandText = "SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'oversight.Mistake') ORDER BY c.column_id"
$r = $q.ExecuteReader(); $cols = @(); while ($r.Read()) { $cols += $r[0] }; $r.Close(); "columns: " + ($cols -join ",")
$m = $cn.CreateCommand()
$m.CommandText = "INSERT INTO oversight.Mistake (OccurredLocal, Session, What, Cost, ClassCode, SurfaceCode, CaughtBy, Mitigation, MitigationKind, MitigationRef, MitigationOwner, State, RecordedBy) OUTPUT inserted.Id VALUES (N'2026-10-27 17:14', N'PDF Ingestor', @w, @c, N'shared-surface-unchecked', NULL, N'self, gate pair', @mit, N'test', N'board 54 + the network invariant check (owed)', N'PDF Ingestor', N'owed', N'PDF Ingestor')"
[void]$m.Parameters.AddWithValue("@w", "Committed 0f3e4ee4 - a tolerance change that moves every metric model's wall network from 2 mm to 50.8 mm - on a harness of ONE building's core (Halifax L1, 12 outlines). The harness said what it covered, but its 'DOES NOT' did not name the thing the change most affects: what the larger tolerance does to walls that are NOT Halifax's. The gate pair afterwards: tilted panels 216 -> 434 in 27 of 56 sets (139 new crooked headers, +79 crooked walls) and a real column (31087 L6-L9) turned into a wall.")
[void]$m.Parameters.AddWithValue("@c", "a pushed commit that worsens 27 metric sets until fixed forward; ee's and Ian's attention; nothing banked")
[void]$m.Parameters.AddWithValue("@mit", "A network invariant test in the suite over the gate sets' classified walls: no wall's bearing changes in Connect/FindOpenings by more than 0.5 deg, a header lies on its jambs' axis, and no element joins only a near-copy of itself - so a tolerance change fails the build on every set, not just the one it was written for.")
"mistake: " + $m.ExecuteScalar()
$c = $cn.CreateCommand()
$c.CommandText = "UPDATE oversight.Commitment SET Evidence = COALESCE(Evidence + N' || ', N'') + @e, UpdatedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id WHERE Id = 52 AND Owner = N'PDF Ingestor'"
[void]$c.Parameters.AddWithValue("@e", "17:5x RULE 10 - second regression: off-axis panels 216 -> 434 (27 sets more, 0 fewer): headers 0 -> 139 (openings now found between jambs ~30 mm out of line, header crooked; 31076 L4 rendered), walls 216 -> 295 (averaged offset ends; 30864 L2 rendered). 102 'further' re-placed: all walls; 36 started on hers (12 sites), 4 sites looked at (2 no change vs hers, 30864 better topology but tilted, 31032 not judged). Code stopped; ee asked to choose fix-forward vs revert.")
"52: " + $c.ExecuteScalar(); $cn.Close(); Get-Date -Format HH:mm:ss
