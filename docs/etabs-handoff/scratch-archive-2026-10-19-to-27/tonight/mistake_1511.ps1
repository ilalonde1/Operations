# The '15:11 CORRECTION' appended to Commitment 15 was written before the clock was read (the write returned 15:10:05), and the
# same '15:11' went to VD. Appended to row 15, and recorded as a Mistake of the existing clock class (mitigation already owed: Ian's hook).
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$c = $cn.CreateCommand()
$c.CommandText = "UPDATE oversight.Commitment SET Evidence = Evidence + N' || the line marked 15:11 was written at 15:10:05 by the write''s own clock read; the time in it was not read off a clock.', UpdatedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id WHERE Id = 15 AND Owner = N'PDF Ingestor'"
"15: " + $c.ExecuteScalar()
$m = $cn.CreateCommand()
$m.CommandText = "INSERT INTO oversight.Mistake (OccurredLocal, Session, What, Cost, ClassCode, SurfaceCode, CaughtBy, Mitigation, MitigationKind, MitigationRef, MitigationOwner, State, RecordedBy) OUTPUT inserted.Id VALUES (N'2026-10-27 15:10', N'PDF Ingestor', @w, N'one correction line on the board and one to VD', N'claim-without-artifact', NULL, N'self', @mit, N'hook', N'Claude Code settings (Ian''s to change)', N'Ian', N'owed', N'PDF Ingestor')"
[void]$m.Parameters.AddWithValue("@w", "Wrote '15:11' into a board Evidence line and a message to VD without reading the clock; the write's own Get-Date returned 15:10:05. The same class as Mistakes 32-34, again, inside the line that was correcting a different unverified claim.")
[void]$m.Parameters.AddWithValue("@mit", "the hook owed on Mistakes 32-34 (a SendMessage/DB write carrying HH:MM refused unless the string appears in a tool output of the last 3 steps); until it exists, write 'at the time below' and let the script's Get-Date print the time")
"mistake: " + $m.ExecuteScalar(); $cn.Close(); Get-Date -Format HH:mm:ss
