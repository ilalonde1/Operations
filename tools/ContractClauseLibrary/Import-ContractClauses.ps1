<#
.SYNOPSIS
  Load the contract clause library (build_library.py output) into dbo.ContractClauseSources and
  dbo.ContractClauses in KorTransmittals (db/KorTransmittals/011). Truncate-and-reload, so a rebuilt
  library replaces the last one.

.EXAMPLE
  .\Import-ContractClauses.ps1 -Server "KOR-APP01\SQLEXPRESS" -Database KorTransmittals `
      -LibDir "..\..\docs\contract-clause-library"
  Uses integrated security unless -ConnectionString is given.
#>
param(
  [string]$Server = "KOR-APP01\SQLEXPRESS",
  [Parameter(Mandatory)][string]$Database,
  [Parameter(Mandatory)][string]$LibDir,
  [string]$ConnectionString
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
if (-not $ConnectionString) { $ConnectionString = "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True" }

$sourcesCsv = Join-Path $LibDir 'documents_roles.csv'
$clausesCsv = Join-Path $LibDir 'library_clauses.csv'
foreach ($f in @($sourcesCsv, $clausesCsv)) { if (-not (Test-Path -LiteralPath $f)) { throw "missing $f" } }

# Python's csv module writes the Text column with embedded newlines. Import-Csv mis-read some of those rows
# on 2026-10-28 (DocId came back null), so both files go through TextFieldParser, which honours quotes.
function Read-CsvRobust([string]$path) {
  Add-Type -AssemblyName Microsoft.VisualBasic
  $p = New-Object Microsoft.VisualBasic.FileIO.TextFieldParser($path, [Text.Encoding]::UTF8)
  $p.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
  $p.SetDelimiters(',')
  $p.HasFieldsEnclosedInQuotes = $true
  $p.TrimWhiteSpace = $false
  $header = $p.ReadFields()
  $rows = New-Object System.Collections.Generic.List[object]
  while (-not $p.EndOfData) {
    $f = $p.ReadFields()
    if ($f.Count -ne $header.Count) { continue }
    $o = [ordered]@{}
    for ($i = 0; $i -lt $header.Count; $i++) { $o[$header[$i]] = $f[$i] }
    $rows.Add([pscustomobject]$o)
  }
  $p.Close()
  return $rows.ToArray()
}
$sources = @(Read-CsvRobust $sourcesCsv | Where-Object { $_.Role -in @('executed', 'markup', 'draft', 'proposal') })
$clauses = @(Read-CsvRobust $clausesCsv | Where-Object { $_.DocId -and $_.DocId.Length -ge 12 })
"sources to load: $($sources.Count); clause rows: $($clauses.Count)"

$cn = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$cn.Open()
$tx = $cn.BeginTransaction()
try {
  $cmd = $cn.CreateCommand(); $cmd.Transaction = $tx
  $cmd.CommandText = "DELETE FROM dbo.ContractClauses; DELETE FROM dbo.ContractClauseSources;"
  [void]$cmd.ExecuteNonQuery()

  $ins = $cn.CreateCommand(); $ins.Transaction = $tx
  $ins.CommandText = @"
INSERT INTO dbo.ContractClauseSources (DocId, Part, SourceKey, Job, ClientName, ProjectName, Paper, Role, VersionIndex, IsLatest, NamesKor, FileDate, Area, Kind, Words, FilePath)
OUTPUT INSERTED.SourceId
VALUES (@DocId, @Part, @SourceKey, @Job, @ClientName, @ProjectName, @Paper, @Role, @VersionIndex, @IsLatest, @NamesKor, @FileDate, @Area, @Kind, @Words, @FilePath);
"@
  foreach ($p in 'DocId','Part','SourceKey','Job','ClientName','ProjectName','Paper','Role','VersionIndex','IsLatest','NamesKor','FileDate','Area','Kind','Words','FilePath') { [void]$ins.Parameters.Add("@$p", [System.Data.SqlDbType]::NVarChar) }
  $ins.Parameters['@IsLatest'].SqlDbType = [System.Data.SqlDbType]::Bit
  $ins.Parameters['@NamesKor'].SqlDbType = [System.Data.SqlDbType]::Bit
  $ins.Parameters['@Words'].SqlDbType = [System.Data.SqlDbType]::Int
  $ins.Parameters['@FileDate'].SqlDbType = [System.Data.SqlDbType]::Date

  $idOf = @{}
  foreach ($s in $sources) {
    $ins.Parameters['@DocId'].Value = $s.DocId.Substring(0, 12)
    $ins.Parameters['@Part'].Value = [string]$s.Part
    $ins.Parameters['@SourceKey'].Value = [string]$s.Key
    $ins.Parameters['@Job'].Value = if ($s.Job) { $s.Job } else { [DBNull]::Value }
    $ins.Parameters['@ClientName'].Value = if ($s.ClientName) { $s.ClientName } else { [DBNull]::Value }
    $ins.Parameters['@ProjectName'].Value = if ($s.ProjectName) { $s.ProjectName } else { [DBNull]::Value }
    $ins.Parameters['@Paper'].Value = $s.Paper
    $ins.Parameters['@Role'].Value = $s.Role
    $ins.Parameters['@VersionIndex'].Value = if ($s.VersionIndex) { $s.VersionIndex } else { [DBNull]::Value }
    $ins.Parameters['@IsLatest'].Value = ($s.Latest -eq 'yes')
    $ins.Parameters['@NamesKor'].Value = ($s.NamesKor -eq 'yes')
    $ins.Parameters['@FileDate'].Value = if ($s.Date -match '^\d{4}-\d{2}-\d{2}') { [datetime]$s.Date.Substring(0, 10) } else { [DBNull]::Value }
    $ins.Parameters['@Area'].Value = if ($s.Area) { $s.Area } else { [DBNull]::Value }
    $ins.Parameters['@Kind'].Value = if ($s.Kind -like 'error*') { 'unreadable' } else { $s.Kind }   # extractor records the exception text as the kind
    $ins.Parameters['@Words'].Value = [int]$s.Words
    $ins.Parameters['@FilePath'].Value = $s.File
    $idOf["$($s.DocId.Substring(0,12))|$($s.Part)"] = [int]$ins.ExecuteScalar()
  }

  $table = New-Object System.Data.DataTable
  foreach ($c in 'SourceId:Int32','Section:String','Heading:String','Topics:String','IsDefinition:Boolean','Words:Int32','ClauseText:String') {
    $n, $t = $c.Split(':'); [void]$table.Columns.Add($n, [type]"System.$t")
  }
  $skipped = 0
  foreach ($c in $clauses) {
    $key = "$($c.DocId.Substring(0,12))|$($c.Part)"
    if (-not $idOf.ContainsKey($key)) { $skipped++; continue }
    [void]$table.Rows.Add($idOf[$key], $c.Section, $c.Heading, $c.Topics, ($c.IsDefinition -eq 'yes'), [int]$c.Words, $c.Text)
  }
  $bulk = New-Object System.Data.SqlClient.SqlBulkCopy($cn, [System.Data.SqlClient.SqlBulkCopyOptions]::Default, $tx)
  $bulk.DestinationTableName = 'dbo.ContractClauses'
  foreach ($col in $table.Columns) { [void]$bulk.ColumnMappings.Add($col.ColumnName, $col.ColumnName) }
  $bulk.WriteToServer($table)
  $tx.Commit()
  "loaded sources=$($idOf.Count) clauses=$($table.Rows.Count) skipped(no source)=$skipped"
} catch {
  $tx.Rollback(); throw
} finally {
  $cn.Close()
}
