# Opens a transaction, retires the max-pier convention exactly as UpsertRuling does, writes a marker, then
# sleeps without committing - to be killed. Never commits under any path.
param([string]$Marker)
Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB)
$cn.Open()
$tx = $cn.BeginTransaction()
$c = $cn.CreateCommand(); $c.Transaction = $tx
$c.CommandText = "UPDATE analysis.FormatConvention SET RetiredAtUtc = SYSDATETIMEOFFSET(), RetiredReason = N'kill proof, never committed' WHERE SettingKey = 'dxf.max-pier-thickness' AND RetiredAtUtc IS NULL"
$n = $c.ExecuteNonQuery()
Set-Content -Path $Marker -Value "retired $n row(s) in an open transaction, pid $PID"
Start-Sleep -Seconds 60
$tx.Rollback()
