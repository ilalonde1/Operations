# oversight.Commitment 15 (the WRONG TWIN trace): done, with its evidence. Matched by Id read back 14:08 + Owner + What prefix.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$c = $cn.CreateCommand()
$c.CommandText = "UPDATE oversight.Commitment SET State = N'done', Evidence = @e, UpdatedAtUtc = SYSDATETIMEOFFSET(), ClosedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id, inserted.State WHERE Id = 15 AND Owner = N'PDF Ingestor' AND What LIKE N'Trace 10-26%' AND State = N'doing'"
[void]$c.Parameters.AddWithValue("@e", "350f8761 pushed (ls-remote 14:57:35): tools/CorpusGateBisect/twin_trace.py + twin_look.py on corpus-gate run 5c60023a. 7 of 7 traced and rendered: 31170 L7 x4 drawn only by S2.07.1 LEVEL 6 PLAN (3 the same 300x760 column turned 90 deg about a shared corner, 1 shifted ~210 mm), 31065 L2 x3 by S2.04.1.1 LEVEL 1 PLAN (2 size changes, 1 a box on another layer). The build reproduces the lowest sheet's drawing; the engineer's model keeps the column straight. Not a twin chosen wrongly; no code change. Renders: scratchpad gate-twin2/<job>/look-*.png.")
$r = $c.ExecuteReader(); while ($r.Read()) { "{0} {1}" -f $r[0], $r[1] }; $r.Close(); $cn.Close(); Get-Date -Format HH:mm:ss
