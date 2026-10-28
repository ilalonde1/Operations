# oversight.Commitment 44: DoneWhen named the header span as 43173-44142; the inch-unit classify (14:07, classify_probe.ps1) puts
# the opening at 42997-44142 - piece 2's end is carried to the 12 in cross wall's line. Matched by Id 44 read back + Owner.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$c = $cn.CreateCommand()
$c.CommandText = "UPDATE oversight.Commitment SET DoneWhen = REPLACE(DoneWhen, N'over 43173-44142', N'over 42997-44142 (the inch-unit classify''s opening)'), UpdatedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id, inserted.DoneWhen WHERE Id = 44 AND Owner = N'PDF Ingestor'"
$r = $c.ExecuteReader(); while ($r.Read()) { "{0} | {1}" -f $r[0], $r[1] }; $r.Close(); $cn.Close(); Get-Date -Format HH:mm:ss
