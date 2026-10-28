# oversight.Commitment 15: the 15:09 append said whether the new drawing moved the L14 column was unread; it is read now.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$c = $cn.CreateCommand()
$c.CommandText = "UPDATE oversight.Commitment SET Evidence = Evidence + @e, UpdatedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id, LEN(inserted.Evidence) WHERE Id = 15 AND Owner = N'PDF Ingestor' AND State = N'done'"
[void]$c.Parameters.AddWithValue("@e", " || 15:10 (replaces 'unread' above): twin_trace.py at today's L14 column (114773, 46673): drawn there at 0 mm by S2.15.1 LEVEL 3 - 13 CONCRETE OUTLINE PLANS (the typical sheet, L3-L13) and S2.14.1 LEVEL 2 PLAN - EAST TOWER. So the 2026-09-18 issue draws that column 285 mm from where the engineer's model (built on an earlier issue) has it, on every storey the typical sheet covers; ours follows the drawing. Present, not lost; not a reading fault.")
$r = $c.ExecuteReader(); while ($r.Read()) { "{0} evidence {1} chars" -f $r[0], $r[1] }; $r.Close(); $cn.Close(); Get-Date -Format HH:mm:ss
