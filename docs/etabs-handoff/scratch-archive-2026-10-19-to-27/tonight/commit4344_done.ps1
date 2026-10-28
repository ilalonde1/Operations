# oversight.Commitment 43 (the check) and 44 (the fix) for Halifax LEVEL 1: done, with the commit and the measurements as Evidence.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$q = $cn.CreateCommand()
$q.CommandText = "SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'oversight.Commitment') ORDER BY c.column_id"
$r = $q.ExecuteReader(); $cols = @(); while ($r.Read()) { $cols += $r[0] }; $r.Close(); "columns: " + ($cols -join ",")
$q.CommandText = "SELECT Id, State, LEFT(What, 90) FROM oversight.Commitment WHERE Id IN (43, 44)"
$r = $q.ExecuteReader(); while ($r.Read()) { "{0} {1} {2}" -f $r[0], $r[1], $r[2] }; $r.Close()
$ev = @{
  43 = "Operations 0f3e4ee4 (pushed, ls-remote read back): HalifaxsCoreIsTheSameWallNetworkInInchesMillimetresAndFeet. On the old code it FAILED (2 columns, the t-band fault); with the t-band fix and no unit passed it FAILED (walls 24 vs 22, the unit fault); with both it passes. Covers/does-not/same-class header in the test."
  44 = "Operations 0f3e4ee4 (pushed, read back): WallNetwork literals scale by inch from the classifier; T needs the end beyond half the other wall. Speed!=Slow 1706 of 1706. Halifax rebuilt with ee's call: off-axis panels 101 -> 9 (P1's drawn skew walls, same in both), columns 432 -> 432 per storey, LEVEL 1 headers KS 42997-44142 on N and S, corners square at (38422,27901)/(38422,36053); rendered l1-old/l1-new and looked. OWED: 57-set corpus gate + full suite (every MM model's snap moves 2 mm -> 50.8 mm)."
}
foreach ($id in 43, 44) {
  $c = $cn.CreateCommand()
  $c.CommandText = "UPDATE oversight.Commitment SET State = N'done', Evidence = COALESCE(Evidence + N' || ', N'') + @e, UpdatedAtUtc = SYSDATETIMEOFFSET(), ClosedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id, inserted.State WHERE Id = @id AND Owner = N'PDF Ingestor'"
  [void]$c.Parameters.AddWithValue("@e", $ev[$id]); [void]$c.Parameters.AddWithValue("@id", $id)
  $r = $c.ExecuteReader(); while ($r.Read()) { "updated {0} -> {1}" -f $r[0], $r[1] }; $r.Close()
}
$cn.Close(); Get-Date -Format HH:mm:ss
