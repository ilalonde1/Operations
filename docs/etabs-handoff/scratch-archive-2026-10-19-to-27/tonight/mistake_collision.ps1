# oversight.Mistake: the 13:56 collision between the suite's compile step and a twin run holding Core.dll. Parameterised.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$c = $cn.CreateCommand(); $c.CommandText = "INSERT INTO oversight.Mistake (OccurredLocal, Session, What, Cost, ClassCode, SurfaceCode, CaughtBy, Mitigation, MitigationKind, MitigationRef, MitigationOwner, State, RecordedBy) OUTPUT inserted.Id VALUES (@o, N'PDF Ingestor', @w, @c, N'precondition-not-checked', NULL, N'check', @mit, N'code', @ref, N'PDF Ingestor', N'done', N'PDF Ingestor')"
[void]$c.Parameters.AddWithValue("@o", "2026-10-27 13:55:53-13:56:20")
[void]$c.Parameters.AddWithValue("@w", "Started the full Core suite and the twin runs in the same second; the twin run's takeoff executed from the TakeoffCli bin and held Core.dll, so the suite's compile step failed copying it at 13:56:20 and no test ran.")
[void]$c.Parameters.AddWithValue("@c", "one wasted start, about 1 min; suite restarted 13:56:50, twin runs restarted 13:57 from a copied bin")
[void]$c.Parameters.AddWithValue("@mit", "twinbuild.py copies the TakeoffCli bin to scratchpad takeoff-bin and runs the copy unless SIXKNOB_EXE is set, so a twin run never locks the repo bin a suite compile writes")
[void]$c.Parameters.AddWithValue("@ref", "scratchpad tonight/twinbuild.py (session scratch, edited 13:57)")
"inserted Id " + $c.ExecuteScalar(); $cn.Close(); Get-Date -Format HH:mm:ss
