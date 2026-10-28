# Rehearses migration 130's transactional body inside a transaction that is ROLLED BACK: the rows it touches, read inside
# the transaction, then the live state read again after the rollback. Never commits.
Add-Type -AssemblyName System.Data
$sql = Get-Content -Raw "C:\VIsual Studio Projects\KOR.Drafter\db\130_ALegendFrameDrawnAgainWithoutItsStrokeIsTheSameFrame.sql"
$body = $sql.Substring($sql.IndexOf("BEGIN TRANSACTION;") + "BEGIN TRANSACTION;".Length)
$body = $body.Substring(0, $body.IndexOf("COMMIT TRANSACTION;"))
$cn = New-Object System.Data.SqlClient.SqlConnection($env:KOR_ENGINEERINGTOOLS_STANDARDSDB); $cn.Open()
function Q($text, $tx) { $c = $cn.CreateCommand(); $c.Transaction = $tx; $c.CommandText = $text; $r = $c.ExecuteReader(); $out = @(); do { while ($r.Read()) { $row = @(); for ($i = 0; $i -lt $r.FieldCount; $i++) { $row += "$($r.GetName($i))=$($r.GetValue($i))" }; $out += ($row -join " | ") } } while ($r.NextResult()); $r.Close(); $out }
$state = "SELECT CreatedBy, CASE WHEN RetiredAtUtc IS NULL THEN 'live' ELSE 'retired' END AS State FROM analysis.FormatConvention WHERE SettingKey = N'dxf.legend-frame-is-not-a-floor' ORDER BY CreatedBy"
"BEFORE:"; Q $state $null
$tx = $cn.BeginTransaction()
"INSIDE (the migration's own rowcounts):"; Q $body $tx
"INSIDE, the key's rows:"; Q $state $tx
"INSIDE, vw_RuleSetting:"; Q "SELECT COUNT(*) AS n, MAX(SettingValue) AS v FROM analysis.vw_RuleSetting WHERE SettingKey = N'dxf.legend-frame-is-not-a-floor'" $tx
$tx.Rollback()
"AFTER ROLLBACK:"; Q $state $null
$cn.Close()
