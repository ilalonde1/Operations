# Invoke-ModelCoverage210.ps1 - run ModelCoverageTests on KOR-210 reading KorStandards as rules_reader, the read-only login
# KOR.Drafter db/146 creates (2026-10-28, Ian's yes via the Virtual Drafter; PDF Ingestor).
#
#   Invoke-ModelCoverage210.ps1 -Archive C:\KOR-Test\operations-<sha8>.zip -Sha <40 hex>
#
# The source is a `git archive --format=zip <sha>` of Operations (210 has git and the .NET SDK but no route to the private
# repo). The commit is read from the ZIP'S OWN COMMENT, which git archive writes as the commit id, and must equal -Sha: a
# file name is not a commit.
#
# THE PASSWORD: read from C:\ProgramData\KOR\korstandards-reader.bin (New-ReaderSecret.ps1, DPAPI LocalMachine, readable by
# SYSTEM and Administrators only) into THIS process's environment as KOR_ENGINEERINGTOOLS_STANDARDSDB, which dotnet test
# inherits. Never printed, never written to a file, gone when the process ends. Nothing is set machine- or user-wide.
#
# IN ORDER, stopping at the first failure:
#   1. the login reads: SUSER_SNAME() and the row count of analysis.vw_RuleSetting, as rules_reader
#   2. the login cannot write: HAS_PERMS_BY_NAME on the view must read SELECT=1 and INSERT/UPDATE/DELETE/ALTER/CONTROL=0, and
#      a DELETE TOP (0) on -WriteProbe (analysis.Ruling), through dynamic SQL in a rolled-back transaction, must be refused
#      with 229 - zero rows either way, so the proof cannot change anything even if the grant were wrong
#   3. dotnet test --filter FullyQualifiedName~ModelCoverageTests, its summary line shown with passed / failed / total
# Writes C:\KOR-Test\modelcoverage-<sha8>-<utc>.log (the test output; no secret is in it - checked, see the end).
#
# COVERS: the login's read and refused write from 210, the one test class, the commit it ran. DOES NOT: the rest of the
# slow suite, or the drawings cache (the tests read drawings themselves). SAME-CLASS FAULT IT MISSES: a test that skips
# instead of failing when the DB is unreachable reads as passed here - so step 1 runs first and the skipped count is shown.
param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Sha,
    [string]$Work = 'C:\KOR-Test',
    [string]$Server = 'KOR-APP01\SQLEXPRESS',
    [ValidatePattern('^[A-Za-z_]+\.[A-Za-z_]+$')][string]$WriteProbe = 'analysis.Ruling'   # a table the rules live in (RuleSettings.cs writes it)
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Store = 'C:\ProgramData\KOR\korstandards-reader.bin'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$short = $Sha.Substring(0, 8)

# the commit, from the archive itself
$zip = [System.IO.Compression.ZipFile]::OpenRead($Archive)
try {
    $fs = [IO.File]::OpenRead($Archive)
    try {
        # the zip comment is the last bytes of the end-of-central-directory record: 22 bytes + comment
        $tailLen = [Math]::Min(65557, $fs.Length); $fs.Seek(-$tailLen, 'End') | Out-Null
        $tail = New-Object byte[] $tailLen; [void]$fs.Read($tail, 0, $tailLen)
        $at = -1; for ($i = $tailLen - 22; $i -ge 0; $i--) { if ($tail[$i] -eq 0x50 -and $tail[$i+1] -eq 0x4b -and $tail[$i+2] -eq 5 -and $tail[$i+3] -eq 6) { $at = $i; break } }
        if ($at -lt 0) { throw "$Archive has no end-of-central-directory record" }
        $clen = $tail[$at + 20] + 256 * $tail[$at + 21]
        $comment = [Text.Encoding]::ASCII.GetString($tail, $at + 22, $clen).Trim()
    } finally { $fs.Dispose() }
} finally { $zip.Dispose() }
if ($comment -ne $Sha) { throw "the archive's own commit is '$comment', not $Sha - refusing to run something other than what was asked" }
"commit (from the archive's comment): $comment"

$src = Join-Path $Work "Operations-$short"
if (-not (Test-Path $src)) { [System.IO.Compression.ZipFile]::ExtractToDirectory($Archive, $src) }

# the connection, in this process only
$pw = [Text.Encoding]::UTF8.GetString([System.Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes($Store), $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine))
$conn = "Server=$Server;Database=KorStandards;User ID=rules_reader;Password=$pw;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15"
$pw = $null

function Query([string]$sql) {
    $cn = New-Object System.Data.SqlClient.SqlConnection $conn
    $cn.Open()
    try { $c = $cn.CreateCommand(); $c.CommandTimeout = 60; $c.CommandText = $sql; $c.ExecuteScalar() } finally { $cn.Dispose() }
}

# 0. the drawings are reachable BY THIS ACCOUNT. ModelCoverageTests returns - PASSES - when LiveProjects.ShareReachable is false
# (ModelCoverageTests.cs: "if (built is null) return;"), so a run that cannot list the root prints an all-green count that
# measured nothing. Same root the tests use: PublishDiscovery.ProjectsRoot = KOR_PROJECTS_ROOT, else the share.
$root = if ($env:KOR_PROJECTS_ROOT) { $env:KOR_PROJECTS_ROOT } else { '\\Kor-fs01\Projects\Projects' }
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "step 0 failed: $root cannot be listed as $([Security.Principal.WindowsIdentity]::GetCurrent().Name) - every ModelCoverage test would pass without building anything"
}
"0. drawings root reachable as $([Security.Principal.WindowsIdentity]::GetCurrent().Name): $root"

# 1. reads
$who = Query "SELECT SUSER_SNAME()"
$rules = Query "SELECT COUNT(*) FROM analysis.vw_RuleSetting"
"1. reads: logged in as $who; analysis.vw_RuleSetting returns $rules row(s)"
if ($who -ne 'rules_reader' -or [int]$rules -le 0) { throw "step 1 failed: expected rules_reader reading at least one rule" }

# 2. cannot write
# ⚠ about PERMISSION, never a write against the view: vw_RuleSetting is a UNION and SQL Server will not compile one on it,
# a refusal that would read as "cannot write" even with DELETE held (db/146's first apply, 2026-10-28 14:1x). So: what the
# login holds on the view, from HAS_PERMS_BY_NAME, and a zero-row write on a table the rules live in, which must be 229.
$held = Query @"
SELECT STRING_AGG(p + '=' + CAST(HAS_PERMS_BY_NAME(N'analysis.vw_RuleSetting', N'OBJECT', p) AS varchar(2)), ' ')
FROM (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE'), ('ALTER'), ('CONTROL')) v(p)
"@
"2. holds on analysis.vw_RuleSetting: $held"
if ($held -ne 'SELECT=1 INSERT=0 UPDATE=0 DELETE=0 ALTER=0 CONTROL=0') { throw "step 2 failed: the login holds more (or less) than SELECT on the view: $held" }
$refused = $null
try { Query "BEGIN TRANSACTION; EXEC (N'DELETE TOP (0) FROM $WriteProbe;'); IF @@TRANCOUNT > 0 ROLLBACK;" | Out-Null }
catch {
    # PowerShell wraps a .NET method's exception (MethodInvocationException): find the SqlException under it
    $e = $_.Exception; while ($e -and -not ($e -is [System.Data.SqlClient.SqlException])) { $e = $e.InnerException }
    if (-not $e) { throw }
    $refused = $e
}
if (-not $refused) { throw "step 2 failed: a DELETE on $WriteProbe was NOT refused - the login can write" }
if ($refused.Number -ne 229) { throw "step 2: refused, but with $($refused.Number) ($($refused.Message)), not 229 permission denied" }
"   cannot write: DELETE on $WriteProbe refused - $($refused.Number) $($refused.Message)"

# 3. the tests, inheriting the connection through this process's environment only
$env:KOR_ENGINEERINGTOOLS_STANDARDSDB = $conn
$log = Join-Path $Work "modelcoverage-$short-$stamp.log"
$proj = Join-Path $src 'Kor.Operations.EngineeringTools.Core.Tests'
& dotnet test $proj --filter 'FullyQualifiedName~ModelCoverageTests' --logger 'console;verbosity=normal' *> $log
$exit = $LASTEXITCODE
Remove-Item Env:\KOR_ENGINEERINGTOOLS_STANDARDSDB
$conn = $null

$summary = Select-String -Path $log -Pattern '^\s*(Passed|Failed)!\s+-' | Select-Object -Last 1
"3. dotnet test exit $exit"
if ($summary) { "   $($summary.Line.Trim())" } else { "   no summary line in $log - the run did not finish; read the log" }
$skips = @(Select-String -Path $log -Pattern '^\s+Skipped\s').Count
"   skipped test lines in the log: $skips"
# the log must not hold the secret: the connection string's password key is the marker
$leak = @(Select-String -Path $log -Pattern 'Password=' -SimpleMatch).Count
"   lines in the log naming a password: $leak"
if ($leak -gt 0) { throw "the test log names a password - $log must be deleted, not read or sent" }
"log: $log"
exit $exit
