# oversight.Commitment: the one OFF HER STACK set left untraced (31138), because corpus-gate cannot be pointed at its six-set PDF.
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
$c = $cn.CreateCommand()
$c.CommandText = "INSERT INTO oversight.Commitment (Owner, What, DoneWhen, DueLocal, State, RecordedBy) OUTPUT inserted.Id VALUES (N'PDF Ingestor', @w, @d, @due, N'open', N'PDF Ingestor')"
[void]$c.Parameters.AddWithValue("@w", "Trace 31138 L2's OFF HER STACK columns (x3 on 10-26) to their sheets. Blocked on a tool: 31138 is in no corpus-gate run (0 rows in gate57-1027's sets) - its six-set PDF is under 05 Stickfile/06 Old Structural Stickfiles and the census does not pick it; corpus-gate has no option naming a PDF; only CorpusAnalyzer writes the sheets.csv placement column (CorpusAnalyzer.cs:451). Needs a way to run the analyzer on a named PDF (a --pdf job=path on corpus-gate, or a probe), compiled after the running suite.")
[void]$c.Parameters.AddWithValue("@d", "the 31138 L2 columns run through twin_trace.py on a build carrying placement, each rendered with twin_look.py and called a drawn transition or a reading fault; board 15's evidence then covers 11 of 11")
[void]$c.Parameters.AddWithValue("@due", "2026-10-27 21:30")
"inserted Id " + $c.ExecuteScalar(); $cn.Close(); Get-Date -Format HH:mm:ss
