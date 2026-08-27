param([Parameter(Mandatory)][string]$Path)

# Runs one KorStandards migration through the same connection string the tool and the tests use.
# sqlcmd cannot: it wants a DSN-style login and integrated auth is refused for this account.
#
# Batches are split on a line that is exactly GO, the way SSMS does it. Anything else -- splitting
# on the word GO anywhere -- cuts a string literal in half and reports a syntax error that is not
# in the file.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data

$cs = $env:KOR_ENGINEERINGTOOLS_STANDARDSDB
if (-not $cs) { throw 'KOR_ENGINEERINGTOOLS_STANDARDSDB is not set.' }

$text = Get-Content -LiteralPath $Path -Raw
$batches = [regex]::Split($text, '(?im)^\s*GO\s*$') | Where-Object { $_.Trim().Length -gt 0 }

Write-Host ("{0}: {1} batch(es)" -f (Split-Path $Path -Leaf), $batches.Count) -ForegroundColor DarkGray

$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()
try {
    $n = 0
    foreach ($b in $batches) {
        $n++
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $b
        $cmd.CommandTimeout = 120
        try {
            $reader = $cmd.ExecuteReader()
            do {
                if ($reader.FieldCount -gt 0) {
                    $cols = @(0..($reader.FieldCount - 1) | ForEach-Object { $reader.GetName($_) })
                    $any = $false
                    while ($reader.Read()) {
                        if (-not $any) { Write-Host ("  [" + ($cols -join ' | ') + "]") -ForegroundColor DarkGray; $any = $true }
                        $vals = @(0..($reader.FieldCount - 1) | ForEach-Object {
                            $v = $reader.GetValue($_); if ($null -eq $v -or $v -is [DBNull]) { '-' } else { [string]$v }
                        })
                        Write-Host ('   ' + ($vals -join ' | '))
                    }
                }
            } while ($reader.NextResult())
            $reader.Close()
        }
        catch {
            Write-Host ("  BATCH {0} FAILED: {1}" -f $n, $_.Exception.Message) -ForegroundColor Red
            throw
        }
    }
    Write-Host ("  all {0} batch(es) ran." -f $batches.Count) -ForegroundColor Green
}
finally { $conn.Close() }
