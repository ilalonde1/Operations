# oversight.Commitment 52 (WallNetwork gate): append the pair's results (still open); new commitment for the duplicate-outline class.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$q = $cn.CreateCommand()
$q.CommandText = "SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'oversight.Commitment') ORDER BY c.column_id"
$r = $q.ExecuteReader(); $cols = @(); while ($r.Read()) { $cols += $r[0] }; $r.Close(); "columns: " + ($cols -join ",")
$c = $cn.CreateCommand()
$c.CommandText = "UPDATE oversight.Commitment SET Evidence = COALESCE(Evidence + N' || ', N'') + @e, UpdatedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id, inserted.State WHERE Id = 52 AND Owner = N'PDF Ingestor'"
[void]$c.Parameters.AddWithValue("@e", "Pair run 17:17-17:30 (same views, two bins, --recompose): gw-before (aa73cd4a) vs gw-after (0f3e4ee4). Gate verdicts IDENTICAL (same 7 stale-bank STOP lines). ModelCoverageTests alone 17:32-17:36 exit 0 (no ratchet down). gate_pair_moves: 48 of 56 differ; wall length +0.46%; columns 23,827 -> 23,599; re-placed 77 closer / 102 further / 890 same (the 102 NOT yet looked at). Lost columns 238: 93 became walls in place (4 near hers = 31087 x4 storeys), 145 absorbed (none within 1 m of hers). Looked: 30849 L2 better; 30933 L12 + 30990 L17 Halifax pattern (no hers there); 31087 L6-L9 WORSE (duplicate outline) -> board item opened. Suite still held for ee.")
$r = $c.ExecuteReader(); while ($r.Read()) { "updated {0} ({1})" -f $r[0], $r[1] }; $r.Close()
$n = $cn.CreateCommand()
$n.CommandText = "INSERT INTO oversight.Commitment (Owner, What, DoneWhen, DueLocal, State, BlockedOn, RecordedBy) OUTPUT inserted.Id VALUES (N'PDF Ingestor', @w, @d, N'2026-10-27 22:30 est', N'open', @b, N'PDF Ingestor')"
[void]$n.Parameters.AddWithValue("@w", "An element joined only to a near-duplicate of itself counts as joined: 31087 L6-L9 (80,103, 57,560), a column outline drawn twice ~20-30 mm apart, became a 762 mm wall once the snap is 50.8 mm (0f3e4ee4); her column is 13 mm away. Same in every inch drawing at 2 in since the network was written.")
[void]$n.Parameters.AddWithValue("@d", "a check that fails on 31087's doubled outline and counts every instance in the 57 gate sets (rule 11, covers/misses stated), the fix, the before/after gate pair again with the moves judged and rendered, and the 102 'further' re-placed members of board 52 looked at.")
[void]$n.Parameters.AddWithValue("@b", "ee's call on the order; KorStandards reads wait for the window to end")
"inserted: " + $n.ExecuteScalar(); $cn.Close(); Get-Date -Format HH:mm:ss
