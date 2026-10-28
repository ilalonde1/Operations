# oversight.Mistake: twinbuild.py built 31170-arch and 31065 through stickfile for the placement column, which stickfile never
# writes (it is in the corpus-gate ledger). Parameterised.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$c = $cn.CreateCommand(); $c.CommandText = "INSERT INTO oversight.Mistake (OccurredLocal, Session, What, Cost, ClassCode, SurfaceCode, CaughtBy, Mitigation, MitigationKind, MitigationRef, MitigationOwner, State, RecordedBy) OUTPUT inserted.Id VALUES (@o, N'PDF Ingestor', @w, @c, N'interface-assumed-not-read', NULL, N'self', @mit, N'code', @ref, N'PDF Ingestor', N'done', N'PDF Ingestor')"
[void]$c.Parameters.AddWithValue("@o", "2026-10-26 to 2026-10-27 14:29")
[void]$c.Parameters.AddWithValue("@w", "The WRONG TWIN trace (board 15) was run through twinbuild.py, which builds via takeoff stickfile, on the assumption that its sheets.csv carries d92e50b1's placement column. It does not: stickfile's sheets.csv is the page list (14 columns, last two dxf_files, failure); placement is in the corpus-gate ledger. Found on reading the header after the 13:57-14:29 builds.")
[void]$c.Parameters.AddWithValue("@c", "three build attempts (10-26 night, 13:50, 13:57-14:29, about 33 min of the last), and the trace still not started")
[void]$c.Parameters.AddWithValue("@mit", "twinbuild.py now exits at its first line naming corpus-gate --jobs as the tool; the trace runs from corpus-gate's ledger (gate-twin2), whose sheets.csv header is read before any trace code")
[void]$c.Parameters.AddWithValue("@ref", "scratchpad tonight/twinbuild.py (sys.exit at import, 14:30)")
"inserted Id " + $c.ExecuteScalar(); $cn.Close(); Get-Date -Format HH:mm:ss
