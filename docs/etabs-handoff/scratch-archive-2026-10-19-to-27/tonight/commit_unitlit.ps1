# oversight.Commitment rows for VD's two Halifax LEVEL 1 findings (14:0x 10-27), one cause: WallNetwork's inch literals run in
# model units. Plus row 29's due time, moved for the 13:56:50 restart. Parameterised; matched by Id read back 14:08 + Owner.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
function Exec($sql, $params) { $c = $cn.CreateCommand(); $c.CommandText = $sql; foreach ($k in $params.Keys) { [void]$c.Parameters.AddWithValue($k, $params[$k]) }; $c.ExecuteScalar() }
$ins = "INSERT INTO oversight.Commitment (Owner, What, DoneWhen, DueLocal, State, RecordedBy) OUTPUT inserted.Id VALUES (N'PDF Ingestor', @w, @d, @due, N'doing', N'PDF Ingestor')"
"check: " + (Exec $ins @{
  "@w" = "The check before the fix (rule 11): AModelIsTheSameInInchesAndMillimetresTests gains Halifax LEVEL 1's core - an L corner whose ends are 329 mm apart, a T at a 12 in cross wall, a 969 mm and a 1195 mm door in a 24 in wall - composed in inches, millimetres and feet, every joint and member compared. Must FAIL on aa73cd4a in feet (VD finding 1: the 969 door closed; finding 2: four corner pieces tilted). Cause measured 14:07: WallNetwork.SnapTolerance 2.0 and the reach's + 2.0 are inch literals applied in feet (2 ft = 610 mm), reproduced exactly by classify_probe.ps1 -Unit ft.";
  "@d" = "the extended differential fails on aa73cd4a with the feet model's closed door and averaged corner named in its message; its header states what it compares, what it does not, and a same-class fault it would miss";
  "@due" = "2026-10-27 17:50" })
"fix: " + (Exec $ins @{
  "@w" = "Fix WallNetwork's unit-blind literals (SnapTolerance 2.0, reach + 2.0, FindOpenings 3.0 / 4.0 / 6.0, and any other length literal on the wall path found by the check): take the drawing unit from PlanClassificationOptions.UnitInInches. Halifax LEVEL 1 rebuilt with VD's call keeps the 969 mm door and its header N and S, and the four corner pieces axis-aligned.";
  "@d" = "the differential passes in in/mm/ft; Halifax 1.e2k rebuilt shows KS headers over 43173-44142 (N and S) and 0 of the LEVEL 1 panels off 0/90 degrees (e2k_walls.py); filtered suite green; full suite after the 19:10-19:55 reboot window (est end 23:30), reported via suite_diff.py. Compile waits on the running suite (est end 17:25).";
  "@due" = "2026-10-27 18:40" })
"29: " + (Exec "UPDATE oversight.Commitment SET DueLocal = @due, UpdatedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id WHERE Id = 29 AND Owner = N'PDF Ingestor' AND What LIKE N'Full suite at aa73cd4a%'" @{ "@due" = "2026-10-27 17:30 est" })
$cn.Close(); Get-Date -Format HH:mm:ss
