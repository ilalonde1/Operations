# New oversight.Commitment: the 57-set corpus gate and the full suite on 0f3e4ee4, owed because the WallNetwork fix moves every MM model.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$q = $cn.CreateCommand()
$q.CommandText = "SELECT c.name, c.max_length FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'oversight.Commitment') ORDER BY c.column_id"
$r = $q.ExecuteReader(); while ($r.Read()) { "{0}({1})" -f $r[0], $r[1] }; $r.Close()
$c = $cn.CreateCommand()
$c.CommandText = "INSERT INTO oversight.Commitment (Owner, What, DoneWhen, DueLocal, State, BlockedOn, RecordedBy) OUTPUT inserted.Id VALUES (N'PDF Ingestor', @w, @d, N'2026-10-27 21:30 est', N'open', @b, N'PDF Ingestor')"
[void]$c.Parameters.AddWithValue("@w", "Gate the WallNetwork unit fix (Operations 0f3e4ee4) on the 57 sets and the full Core suite: every MM model's snap moves from 2 mm to 50.8 mm, so the corpus moves.")
[void]$c.Parameters.AddWithValue("@d", "corpus-gate on 57 sets at 0f3e4ee4 read against the baseline ledger with every moved set rendered and judged (drawn vs reading fault), and full suite_diff.py NEW 0 or each new failure characterised on the board.")
[void]$c.Parameters.AddWithValue("@b", "operations-ee's no-access window (from about 18:10): no KorStandards reads; the gate starts when ee says the servers are back, or now if ee says it clears the window.")
"inserted: " + $c.ExecuteScalar(); $cn.Close(); Get-Date -Format HH:mm:ss
