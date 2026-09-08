<#
.SYNOPSIS
  Push the generated signatures into the Transmittals app's user preferences.

.DESCRIPTION
  The app keeps one signature per user in `dbo.UserPreferences.EmailSignatureHtml`
  on KorTransmittalsDb, keyed on UserUpn. Until now people pasted their own in by
  hand through Preferences, so they drift from the desktop Outlook signature the
  moment either changes.

  The generated .htm files are self-contained -- the logo is a base64 data URI,
  not a relative path -- so the same file that goes to Outlook works here without
  modification.

  Roster-driven, exactly like Deploy-RemoteSignatures.ps1: both call
  Get-SignatureRoster, so a user who keeps their own signature is skipped on
  both surfaces from one file (exclude-users.csv). jbryson is opted out
  (2026-07-10) and has no roster row, so he is skipped without a special case.

  A user who has never opened Preferences has no row. One is inserted with
  AutoFileOnSend = 0 and ItemsToFileEnabled = 0, which is exactly what
  SqlUserPreferencesStore.GetAsync returns for a user with no row -- so nothing
  about their filing behaviour changes.

  DRY-RUN by default. Back up first with sig_prefs_backup, or pass -BackupPath.

.EXAMPLE
  .\Push-TransmittalSignatures.ps1              # dry-run
  .\Push-TransmittalSignatures.ps1 -Commit      # write
#>
[CmdletBinding()]
param(
    [switch]$Commit,
    [string]$BackupPath
)

$ErrorActionPreference = 'Stop'

$kitDir = $PSScriptRoot
$genDir = Join-Path $kitDir 'generated'
. (Join-Path $kitDir 'SignatureRoster.ps1')
$roster = @(Get-SignatureRoster -Surface transmittals -KitDir $kitDir)

$cs = $env:KOR_FILESYNC_KORTRANSMITTALSDB
if (-not $cs) { throw 'KOR_FILESYNC_KORTRANSMITTALSDB is not set in this session' }

$cn = New-Object System.Data.SqlClient.SqlConnection($cs)
$cn.Open()
try {
    if ($BackupPath) {
        $bcmd = $cn.CreateCommand()
        $bcmd.CommandText = 'SELECT * FROM dbo.UserPreferences'
        $ad = New-Object System.Data.SqlClient.SqlDataAdapter($bcmd)
        $dt = New-Object System.Data.DataTable
        [void]$ad.Fill($dt)
        $out = foreach ($row in $dt.Rows) {
            $h = @{}
            foreach ($col in $dt.Columns) {
                $v = $row[$col.ColumnName]
                $h[$col.ColumnName] = if ($v -is [DBNull]) { $null } else { $v }
            }
            $h
        }
        $out | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $BackupPath
        "Backed up $($dt.Rows.Count) rows -> $BackupPath"
    }

    # What is there now, so the report can say what changed rather than guessing.
    $existing = @{}
    $qcmd = $cn.CreateCommand()
    $qcmd.CommandText = 'SELECT UserUpn, CAST(ISNULL(LEN(EmailSignatureHtml),0) AS int) FROM dbo.UserPreferences'
    $rd = $qcmd.ExecuteReader()
    while ($rd.Read()) { $existing[$rd.GetString(0).ToLowerInvariant()] = $rd.GetInt32(1) }
    $rd.Close()

    "Mode: " + $(if ($Commit) { 'COMMIT' } else { 'DRY-RUN (nothing written)' })
    ""

    $merge = @'
MERGE dbo.UserPreferences AS t
USING (VALUES(@UserUpn)) v(UserUpn)
   ON t.UserUpn = v.UserUpn
WHEN MATCHED THEN
    UPDATE SET EmailSignatureHtml = @Html, ModifiedUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (UserUpn, AutoFileOnSend, ItemsToFileEnabled, EmailSignatureHtml, CreatedUtc, ModifiedUtc)
    VALUES (@UserUpn, 0, 0, @Html, SYSUTCDATETIME(), SYSUTCDATETIME());
'@

    $written = 0; $missing = 0
    foreach ($p in $roster) {
        $alias = $p.Alias
        $upn = $p.Email
        $file = Join-Path $genDir "$alias.htm"
        if (-not (Test-Path $file)) {
            "  {0,-12} NO GENERATED FILE - skipped" -f $alias
            $missing++
            continue
        }
        $html = Get-Content $file -Raw
        if ($html -notmatch 'ALBERTA&nbsp;\|&nbsp;USA') {
            "  {0,-12} generated file is STALE (no USA) - skipped" -f $alias
            $missing++
            continue
        }

        $key = $upn.ToLowerInvariant()
        $was = if ($existing.ContainsKey($key)) { "was $($existing[$key])" } else { 'NEW ROW' }
        "  {0,-12} {1,-34} {2,7} chars  ({3})" -f $alias, $upn, $html.Length, $was

        if ($Commit) {
            $cmd = $cn.CreateCommand()
            $cmd.CommandText = $merge
            [void]$cmd.Parameters.AddWithValue('@UserUpn', $upn)
            [void]$cmd.Parameters.AddWithValue('@Html', $html)
            [void]$cmd.ExecuteNonQuery()
        }
        $written++
    }

    ""
    "$written of $($roster.Count) roster users " + $(if ($Commit) { 'written' } else { 'would be written' })
    if ($missing) { "$missing skipped" }
    if (-not $Commit) { 'Dry-run only. Rerun with -Commit to apply.' }
} finally { $cn.Close() }
