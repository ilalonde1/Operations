# oversight.Commitment 15: the 15:09:46 append said the engineer's model was "built on an earlier issue" - not checked: the gate
# ledger's yardstick_written for 31130 is blank. Struck by a following line (no edits to a written row's text beyond appends).
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$c = $cn.CreateCommand()
$c.CommandText = "UPDATE oversight.Commitment SET Evidence = Evidence + @e, UpdatedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id, LEN(inserted.Evidence) WHERE Id = 15 AND Owner = N'PDF Ingestor' AND State = N'done'"
[void]$c.Parameters.AddWithValue("@e", " || 15:11 CORRECTION: '(built on an earlier issue)' above is unverified - gate-sets.csv yardstick_written for 31130-01 is blank, so which issue the engineer's model follows is not known. What stands: the 2026-09-18 drawing draws the column at (114773, 46673); the engineer's model has it 285 mm away.")
$r = $c.ExecuteReader(); while ($r.Read()) { "{0} evidence {1} chars" -f $r[0], $r[1] }; $r.Close(); $cn.Close(); Get-Date -Format HH:mm:ss
