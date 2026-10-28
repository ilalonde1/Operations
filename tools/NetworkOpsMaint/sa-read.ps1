# READ-ONLY SQL as sa (Ian's DPAPI cred file, decrypted in memory, never printed or written). Runs one .sql file of
# SELECTs and prints each result set as pipe-separated text. Refuses any file containing a write verb.
param([Parameter(Mandatory)][string]$SqlFile, [string]$Database = 'master')
$ErrorActionPreference = 'Stop'
$sql = Get-Content -LiteralPath $SqlFile -Raw
if ($sql -match '(?i)\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|BACKUP|RESTORE|DBCC\s+SHRINK|sp_update_job|sp_add)\b') { throw 'refused: the SQL file contains a write verb; this runner is read-only' }
$ss = Get-Content (Join-Path $env:USERPROFILE '.kor\korstandards-migrate.cred') | ConvertTo-SecureString
$ss.MakeReadOnly()
$cred = [System.Data.SqlClient.SqlCredential]::new('sa', $ss)
$cn = [System.Data.SqlClient.SqlConnection]::new("Server=KOR-APP01\SQLEXPRESS;Database=$Database;Encrypt=True;TrustServerCertificate=True;Application Name=netops-e6-readonly", $cred)
$cn.Open()
try {
  $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 120
  $r = $cmd.ExecuteReader()
  do {
    $cols = 0..($r.FieldCount - 1) | ForEach-Object { $r.GetName($_) }
    if ($cols) { ($cols -join '|'); $n = 0
      while ($r.Read()) { $n++; (0..($r.FieldCount - 1) | ForEach-Object { $v = $r.GetValue($_); if ($v -is [datetime]) { $v.ToString('yyyy-MM-dd HH:mm') } else { "$v" } }) -join '|' }
      "($n rows)"; '' }
  } while ($r.NextResult())
  $r.Close()
} finally { $cn.Close() }
