param([switch]$Rollback)
$ErrorActionPreference = 'Stop'
$migration = Get-Content "$PSScriptRoot/../../Kor.Opportunities.Data/Schema/338_ApplicationBigTicketCoverage.sql" -Raw
$reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine','KOR-APP01')
$key = $reg.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Environment')
$con = [System.Data.SqlClient.SqlConnection]::new($key.GetValue('KOR_OPPORTUNITIES_OPPORTUNITIESDB'))
$key.Dispose(); $reg.Dispose()
try {
    $con.Open(); $tx = $con.BeginTransaction()
    function Execute([string]$Sql) {
        $cmd = $con.CreateCommand(); $cmd.Transaction=$tx; $cmd.CommandText=$Sql; $cmd.CommandTimeout=120
        try { [void]$cmd.ExecuteNonQuery() } finally { $cmd.Dispose() }
    }
    Execute 'SELECT OpportunityId, SourceName, IsBigTicket INTO #previous FROM opportunities.vIslandApplications;'
    foreach ($batch in [regex]::Split($migration,'(?m)^GO\s*$')) { if ($batch.Trim()) { Execute $batch } }
    Execute @'
IF EXISTS (SELECT OpportunityId,SourceName,IsBigTicket FROM #previous
           EXCEPT SELECT OpportunityId,SourceName,IsBigTicket FROM opportunities.vIslandApplications)
 THROW 51000, 'An existing classification changed. Rolling back.', 1;
IF EXISTS (SELECT 1 FROM opportunities.vIslandApplications WHERE SourceName='Calgary_LandUseRedesignation' AND IsBigTicket=0)
 THROW 51000, 'Calgary decision was not applied.', 1;
'@
    if ($Rollback) { $tx.Rollback(); 'Migration rehearsal rolled back; existing cohort unchanged.' }
    else { $tx.Commit(); 'Migration 338 committed; existing cohort unchanged.' }
} finally { if ($tx) { $tx.Dispose() }; $con.Dispose() }
