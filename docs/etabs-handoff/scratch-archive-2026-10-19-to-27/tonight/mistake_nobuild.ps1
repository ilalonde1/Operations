# oversight.Mistake owed since 18:31 (window): tests run with --no-build right after a stash pop showed the stashed code's
# failures. Constrained values read first (Mistake 66's mitigation).
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$q = $cn.CreateCommand()
$q.CommandText = "SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'oversight.Mistake') ORDER BY c.column_id"
$r = $q.ExecuteReader(); $cols = @(); while ($r.Read()) { $cols += $r[0] }; $r.Close(); "columns: " + ($cols -join ",")
foreach ($col in 'CaughtBy', 'MitigationKind', 'ClassCode', 'State') {
  $q.CommandText = "SELECT DISTINCT $col FROM oversight.Mistake"
  $r = $q.ExecuteReader(); $v = @(); while ($r.Read()) { $v += [string]$r[0] }; $r.Close(); "${col}: " + ($v -join ", ")
}
$m = $cn.CreateCommand()
$m.CommandText = "INSERT INTO oversight.Mistake (OccurredLocal, Session, What, Cost, ClassCode, SurfaceCode, CaughtBy, Mitigation, MitigationKind, MitigationRef, MitigationOwner, State, RecordedBy) OUTPUT inserted.Id VALUES (N'2026-10-27 18:31', N'PDF Ingestor', @w, @c, N'precondition-not-checked', NULL, N'self', @mit, N'rule', N'every dotnet test after a stash push/pop', N'PDF Ingestor', N'done', N'PDF Ingestor')"
[void]$m.Parameters.AddWithValue("@w", "Ran the network tests with --no-build straight after `git stash pop`; the binaries were the ones built with the fix stashed, so the run showed the old code's 3 failures as if they were the fix's. Caught from the counts (the same 3 that had just failed on purpose) before anything was reported; rebuilt and reran: 22 of 22.")
[void]$m.Parameters.AddWithValue("@c", "one wasted run; nothing reported or committed from it")
[void]$m.Parameters.AddWithValue("@mit", "No --no-build after any stash push or pop, ever; and every test claim names the Core.dll write time read off disk after the run (done since 18:31 - 18:31:41, 18:43:22).")
"mistake: " + $m.ExecuteScalar(); $cn.Close(); Get-Date -Format HH:mm:ss
