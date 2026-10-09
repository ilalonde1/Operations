# Compares every application/source membership's view coverage and classification,
# score/classification disagreements, and preservation of the old classified cohort.
# Does not judge business scope, missing descriptions, UI interaction or scorer quality.
# Same-class miss: a scope mentioning a former single-family building can be classified
# small even when the proposal is a tower. The UI reveal must preserve recall.
param([switch]$Capture, [string]$Out = '.codex_bigticket', [switch]$Verify)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine','KOR-APP01')
$key = $reg.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Environment')
$con = [System.Data.SqlClient.SqlConnection]::new($key.GetValue('KOR_OPPORTUNITIES_OPPORTUNITIESDB'))
$key.Dispose(); $reg.Dispose()
function Read-Json([string]$Sql) {
    $cmd = $con.CreateCommand(); $cmd.CommandText = $Sql; $cmd.CommandTimeout = 120
    $reader = $cmd.ExecuteReader(); $buffer = [System.Text.StringBuilder]::new()
    try { while ($reader.Read()) { [void]$buffer.Append($reader.GetString(0)) } }
    finally { $reader.Dispose(); $cmd.Dispose() }
    return $buffer.ToString()
}
try {
    $con.Open()
    $json = Read-Json @'
SELECT DISTINCT o.Id, s.Name SourceName, o.Name Title, o.RelevanceScore Score,
       v.IsBigTicket, d.Description
FROM opportunities.Opportunities o
JOIN opportunities.OpportunityObservations ob ON ob.OpportunityId=o.Id
JOIN opportunities.OpportunitySources s ON s.Id=ob.OpportunitySourceId
LEFT JOIN opportunities.OpportunitySourceMappings m ON m.OpportunitySourceId=s.Id AND m.[Key]='bd.kind'
LEFT JOIN opportunities.vIslandApplications v ON v.OpportunityId=o.Id AND v.SourceName=s.Name
OUTER APPLY (SELECT TOP (1) z.Description FROM opportunities.OpportunityObservations z WHERE z.OpportunityId=o.Id ORDER BY z.Id DESC) d
WHERE o.DismissedAtUtc IS NULL AND (s.SourceType IN (20,21,22)
 OR (s.SourceType IN (1,2,3,5,99) AND LOWER(REPLACE(LTRIM(RTRIM(m.ValueJson)),'"',''))='application'))
ORDER BY o.Id,s.Name FOR JSON PATH, INCLUDE_NULL_VALUES;
'@
    $rows = @($json | ConvertFrom-Json)
    $baselinePath = Join-Path $Out 'before.json'
    if ($Capture) {
        if (Test-Path $baselinePath) { throw 'Refusing to overwrite the baseline.' }
        $json | Set-Content $baselinePath
        $cmd = $con.CreateCommand(); $cmd.CommandText = "SELECT OBJECT_DEFINITION(OBJECT_ID('opportunities.vIslandApplications'));"
        $cmd.ExecuteScalar() | Set-Content (Join-Path $Out 'view-before.sql'); $cmd.Dispose()
    }
    if (Test-Path $baselinePath) { $before = @(Get-Content $baselinePath -Raw | ConvertFrom-Json) }
    else {
        # The small committed ID/source/bit ledger makes the original coverage
        # comparison reproducible without committing raw publisher scope text.
        $before = @(Import-Csv "$PSScriptRoot/evidence/before-classification.csv" | ForEach-Object {
            [pscustomobject]@{ Id=[long]$_.Id; SourceName=$_.SourceName;
                IsBigTicket=if ([string]::IsNullOrEmpty($_.IsBigTicket)) { $null } else { [bool]::Parse($_.IsBigTicket) } }
        })
    }
    $json | Set-Content (Join-Path $Out 'current.json')
    $counts = foreach ($name in @($before.SourceName + $rows.SourceName | Sort-Object -Unique)) {
        $b = @($before | Where-Object SourceName -eq $name); $a = @($rows | Where-Object SourceName -eq $name)
        [pscustomobject]@{ Source=$name; BeforeBig=@($b | Where-Object IsBigTicket -eq $true).Count;
            BeforeSmall=@($b | Where-Object IsBigTicket -eq $false).Count; BeforeUnreached=@($b | Where-Object { $null -eq $_.IsBigTicket }).Count;
            AfterBig=@($a | Where-Object IsBigTicket -eq $true).Count; AfterSmall=@($a | Where-Object IsBigTicket -eq $false).Count;
            AfterUnreached=@($a | Where-Object { $null -eq $_.IsBigTicket }).Count }
    }
    $counts | Export-Csv (Join-Path $Out 'counts.csv') -NoTypeInformation
    $index = @{}; foreach ($row in $rows) { $index["$($row.Id)|$($row.SourceName)"] = $row }
    $changes = @($before | Where-Object { $null -ne $_.IsBigTicket -and
        (!$index.ContainsKey("$($_.Id)|$($_.SourceName)") -or $index["$($_.Id)|$($_.SourceName)"].IsBigTicket -ne $_.IsBigTicket) })
    $changes | Export-Csv (Join-Path $Out 'old-cohort-changes.csv') -NoTypeInformation
    # Compare scores against both cohort-wide and source-local means; retain every disagreement.
    $islandNames = @($before | Where-Object { $null -ne $_.IsBigTicket } | Select-Object -ExpandProperty SourceName -Unique)
    $means = [System.Collections.Generic.List[object]]::new()
    $disagreements = foreach ($group in @(@{Name='All applications'; Group=$rows},
        @{Name='Original Island cohort'; Group=@($rows | Where-Object { $_.SourceName -in $islandNames })}) + @($rows | Group-Object SourceName)) {
        $big = @($group.Group | Where-Object { $_.IsBigTicket -eq $true -and $null -ne $_.Score })
        $small = @($group.Group | Where-Object { $_.IsBigTicket -eq $false -and $null -ne $_.Score })
        if (!$big.Count -or !$small.Count) { continue }
        $bigMean = ($big | Measure-Object Score -Average).Average; $smallMean = ($small | Measure-Object Score -Average).Average
        $means.Add([pscustomobject]@{ Comparison=$group.Name; BigCount=$big.Count; SmallCount=$small.Count;
            BigMean=$bigMean; SmallMean=$smallMean; SmallAboveBigMean=@($small | Where-Object Score -gt $bigMean).Count;
            BigBelowSmallMean=@($big | Where-Object Score -lt $smallMean).Count })
        foreach ($row in $group.Group) {
            if (($row.IsBigTicket -eq $false -and $row.Score -gt $bigMean) -or
                ($row.IsBigTicket -eq $true -and $row.Score -lt $smallMean)) {
                [pscustomobject]@{ Comparison=$group.Name; Id=$row.Id; Source=$row.SourceName; IsBigTicket=$row.IsBigTicket;
                    Score=$row.Score; BigMean=$bigMean; SmallMean=$smallMean; Title=$row.Title; Description=$row.Description }
            }
        }
    }
    $disagreements | Export-Csv (Join-Path $Out 'disagreements.csv') -NoTypeInformation
    $means | Export-Csv (Join-Path $Out 'score-means.csv') -NoTypeInformation
    $unique = @($rows | Sort-Object Id -Unique)
    $summary = "Applications=$($unique.Count); big=$(@($unique | Where-Object IsBigTicket -eq $true).Count); small=$(@($unique | Where-Object IsBigTicket -eq $false).Count); unreached=$(@($unique | Where-Object { $null -eq $_.IsBigTicket }).Count); old-cohort changes=$($changes.Count)."
    $summary | Set-Content (Join-Path $Out 'summary.txt'); Write-Output $summary
    $counts | Format-Table -AutoSize
    if ($Verify -and ($changes.Count -gt 0 -or @($unique | Where-Object { $null -eq $_.IsBigTicket }).Count -gt 0)) { exit 1 }
} finally { $con.Dispose() }
