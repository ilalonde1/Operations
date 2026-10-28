# As sa (Ian's DPAPI cred, in memory only): ONE shrink of KorEmailIndex's log file to 1024 MB (SIMPLE recovery; 5 MB of
# 10,184 MB used at 15:0x). Reads size and use before and after. Rollback: none needed - the file regrows on its own.
# operations-ee (Ian's proxy) 27 Oct: "item 4, the log shrink, once the PDF Ingestor reports its suite end time".
$ErrorActionPreference = 'Stop'
$ss = Get-Content (Join-Path $env:USERPROFILE '.kor\korstandards-migrate.cred') | ConvertTo-SecureString; $ss.MakeReadOnly()
$cn = [System.Data.SqlClient.SqlConnection]::new('Server=KOR-APP01\SQLEXPRESS;Database=KorEmailIndex;Encrypt=True;TrustServerCertificate=True;Application Name=netops-e6-shrink', [System.Data.SqlClient.SqlCredential]::new('sa', $ss))
$cn.Open()
try {
  function Size { $c = $cn.CreateCommand(); $c.CommandText = "SELECT CAST(total_log_size_in_bytes/1048576.0 AS int), CAST(used_log_space_in_bytes/1048576.0 AS int), DB_NAME() FROM sys.dm_db_log_space_usage"; $r = $c.ExecuteReader(); [void]$r.Read(); $s = "log $($r.GetInt32(0)) MB, used $($r.GetInt32(1)) MB in $($r.GetString(2))"; $r.Close(); $s }
  "before: $(Size)"
  $c = $cn.CreateCommand(); $c.CommandTimeout = 600; $c.CommandText = "DBCC SHRINKFILE (N'KorEmailIndex_log', 1024) WITH NO_INFOMSGS;"; [void]$c.ExecuteNonQuery()
  "after:  $(Size)"
} finally { $cn.Close() }
