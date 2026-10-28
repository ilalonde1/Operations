# oversight.Commitment 49 (31138 L2 x3 trace): done with evidence; a new commitment for the one reading fault it found (GC12).
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$q = $cn.CreateCommand()
$q.CommandText = "SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'oversight.Commitment') ORDER BY c.column_id"
$r = $q.ExecuteReader(); $cols = @(); while ($r.Read()) { $cols += $r[0] }; $r.Close(); "columns: " + ($cols -join ",")
$q.CommandText = "SELECT Id, State, LEFT(What, 100) FROM oversight.Commitment WHERE Id = 49"
$r = $q.ExecuteReader(); while ($r.Read()) { "{0} {1} {2}" -f $r[0], $r[1], $r[2] }; $r.Close()
$c = $cn.CreateCommand()
$c.CommandText = "UPDATE oversight.Commitment SET State = N'done', Evidence = COALESCE(Evidence + N' || ', N'') + @e, UpdatedAtUtc = SYSDATETIMEOFFSET(), ClosedAtUtc = SYSDATETIMEOFFSET() OUTPUT inserted.Id, inserted.State WHERE Id = 49 AND Owner = N'PDF Ingestor'"
[void]$c.Parameters.AddWithValue("@e", "corpus-gate --pdf job=path built (StickFileCorpus.PinIssues + test APinnedIssueReplacesTheNewestForThatJobOnly; a bad pin exits 2 before anything builds). 31138 built from the 2026-09-01 issue (mirror copy, no share read), work scratchpad gate-31138-old: 28 storeys, 603 columns; near-miss = 3 OFF HER STACK, all L2, the same 3 as 10-26. twin_trace: each drawn at 0 mm by S2.12_2 LEVEL 1 AT 55'-0 only. LOOKED at the PDF page (tools/CorpusGateBisect/page_look.py, page 20, overview boxes all on columns): (76974,44066) DRAWN TRANSITION - a solid shaded 36x57 column over the dashed column below; (41898,47394) DRAWN TRANSITION - GC5 36x38 solid over a smaller dashed column below with EXTRA DOWELS FROM BELOW; (61125,35164) READING FAULT - GC12 (x48) is one tall shaded column crossed near its top by the dashed edge of the column below, and we kept only the ~460x250 mm strip above the dashed line (its whole centre is ~483 mm lower, where the 64'-1 view draws it). New commitment opened for that fault.")
$r = $c.ExecuteReader(); while ($r.Read()) { "updated {0} -> {1}" -f $r[0], $r[1] }; $r.Close()
$n = $cn.CreateCommand()
$n.CommandText = "INSERT INTO oversight.Commitment (Owner, What, DoneWhen, DueLocal, State, BlockedOn, RecordedBy) OUTPUT inserted.Id VALUES (N'PDF Ingestor', @w, @d, N'2026-10-28 12:00 est', N'open', @b, N'PDF Ingestor')"
[void]$n.Parameters.AddWithValue("@w", "31138 S2.12_2 (2026-09-01 issue) GC12 at (61125, 35164): a shaded column crossed by the dashed outline of the column below is read as only the strip on one side of the dashed line. Characterise the class (how many shaded columns in the corpus have a dashed line through them, and what each became) before any fix.")
[void]$n.Parameters.AddWithValue("@d", "a check that fails on GC12 and on every other instance it finds in the gate sets (rule 11), stated with what it covers / misses; then a fix that passes it with the corpus gate's before/after pair judged and rendered.")
[void]$n.Parameters.AddWithValue("@b", "after the WallNetwork gate (board 52) is judged; KorStandards reads wait for the window to end")
"inserted: " + $n.ExecuteScalar(); $cn.Close(); Get-Date -Format HH:mm:ss
