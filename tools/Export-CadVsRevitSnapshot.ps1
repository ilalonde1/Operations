<#
.SYNOPSIS
  Export the banked CAD-vs-Revit snapshot from KorTransmittals into the CSV shapes the report generator reads.

.DESCRIPTION
  The measurement lives in dbo.ProjectDraftingProfitabilitySnapshot and dbo.DraftingTypeByYearSnapshot
  (db\KorTransmittals\008), filled by usp_SnapshotProjectDraftingProfitability. This writes that snapshot,
  for one AsOfDate, as the nine CSVs tools\build_cad_vs_revit_report.py consumes, with the same column names
  the first edition's ODBC pulls had, so the generator needs no change between editions.

  Two inputs are NOT in the snapshot and are left as they are in the data folder:
    screen_recon.csv   the reconciliation to the Historical screen (measured once, 2026-10-14)
    coverage_amt.csv   posted-amount coverage by year (measured once, 2026-10-14)

  Connection: KOR-APP01\SQLEXPRESS, KorTransmittals, SQL login from KOR_DB_USER / KOR_DB_PASSWORD (datareader is enough).

.EXAMPLE
  .\tools\Export-CadVsRevitSnapshot.ps1 -AsOfDate 2026-10-14 -OutDir docs\KOR-Financials-CAD-vs-Revit-2026-10-14-data
  python tools\build_cad_vs_revit_report.py --jobs <OutDir>\jobs_all.csv ... (see the generator's docstring)
#>
param(
  [Parameter(Mandatory)][string]$AsOfDate,
  [Parameter(Mandatory)][string]$OutDir,
  [string]$Server = 'KOR-APP01\SQLEXPRESS',
  [string]$Database = 'KorTransmittals'
)
$ErrorActionPreference = 'Stop'
$user = $env:KOR_DB_USER; $pwd = $env:KOR_DB_PASSWORD
if (-not $user -or -not $pwd) { throw 'KOR_DB_USER / KOR_DB_PASSWORD are not set.' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$queries = [ordered]@{
  'jobs_all.csv' = @"
SELECT Wbs1 AS WBS1, ProjectName AS Name, Status, Org, CONVERT(varchar(10), OpenDate, 120) AS OpenDate,
       Sector, DraftingType AS DraftType, Category, ConstructionType,
       FeeCad AS Fee, FeeBilledCad AS FeeBilled, EngHrs, DraftHrs,
       DraftCostCurrent AS DraftCost, EngCostCurrent AS EngCost,
       LaborCostCurrentActive AS CostActive, LaborCostCurrentAllStaff AS CostAll, BillableHrs,
       CONVERT(varchar(10), FirstHoursDate, 120) AS FirstHrs, CONVERT(varchar(10), LastHoursDate, 120) AS LastHrs
FROM dbo.ProjectDraftingProfitabilitySnapshot WHERE AsOfDate = @d ORDER BY Wbs1
"@
  'jobs_histcost.csv' = @"
SELECT Wbs1 AS WBS1, EngAmtPosted AS EngAmt, DraftAmtPosted AS DraftAmt, LaborAmtPosted AS PostedCost,
       PartnerHrs, PartnerAmtPosted AS PartnerPostedAmt, PartnerDraftHrs, PartnerDraftAmtPosted AS PartnerDraftAmt,
       PartnerEngHrs, PartnerEngAmtPosted AS PartnerEngAmt
FROM dbo.ProjectDraftingProfitabilitySnapshot WHERE AsOfDate = @d ORDER BY Wbs1
"@
  'jobs_pre2019.csv' = @"
SELECT Wbs1 AS WBS1, BilledBefore2019Cad AS BilledBefore2019, FirstBillingPeriod, NULL AS LastBillingPeriod
FROM dbo.ProjectDraftingProfitabilitySnapshot WHERE AsOfDate = @d ORDER BY Wbs1
"@
  'drafthrs_by_year.csv' = @"
SELECT [Year] AS Yr, DraftingType AS DraftType, SUM(DraftHrs) AS DraftHrs, SUM(JobsWithHours) AS Jobs
FROM dbo.DraftingTypeByYearSnapshot WHERE AsOfDate = @d GROUP BY [Year], DraftingType ORDER BY Yr, DraftType
"@
  'drafthrs_by_year_res.csv' = @"
SELECT [Year] AS Yr, DraftingType AS DraftType, SUM(DraftHrs) AS DraftHrs, SUM(JobsWithHours) AS Jobs
FROM dbo.DraftingTypeByYearSnapshot WHERE AsOfDate = @d AND Sector = N'Res' GROUP BY [Year], DraftingType ORDER BY Yr, DraftType
"@
  'billed_by_year.csv' = @"
SELECT [Year] AS Yr, Sector, DraftingType AS DraftType, BilledCad AS Billed, JobsWithHours AS Jobs
FROM dbo.DraftingTypeByYearSnapshot WHERE AsOfDate = @d ORDER BY Yr, Sector, DraftType
"@
  'cost_by_year.csv' = @"
SELECT [Year] AS Yr, Sector, DraftingType AS DraftType, EngHrs, DraftHrs,
       LaborCostCurrentAllStaff AS CostAll, NULL AS CostActive, NULL AS DraftCost, JobsWithHours AS Jobs
FROM dbo.DraftingTypeByYearSnapshot WHERE AsOfDate = @d ORDER BY Yr, Sector, DraftType
"@
  'cost_by_year_hist.csv' = @"
SELECT [Year] AS Yr, Sector, DraftingType AS DraftType, LaborAmtPosted AS PostedCost, EngAmtPosted AS EngAmt,
       DraftAmtPosted AS DraftAmt, PartnerHrs, PartnerAmtPosted AS PartnerPostedAmt
FROM dbo.DraftingTypeByYearSnapshot WHERE AsOfDate = @d ORDER BY Yr, Sector, DraftType
"@
  'pre2019_billed.csv' = @"
SELECT DraftingType AS DraftType, COUNT(*) AS Jobs, SUM(BilledBefore2019Cad) AS BilledBefore2019
FROM dbo.ProjectDraftingProfitabilitySnapshot WHERE AsOfDate = @d AND Sector = N'Res' AND BilledBefore2019Cad > 0
GROUP BY DraftingType ORDER BY DraftType
"@
}

$cn = New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=$Database;User Id=$user;Password=$pwd;Encrypt=True;TrustServerCertificate=True;")
$cn.Open()
try {
  foreach ($name in $queries.Keys) {
    $cmd = $cn.CreateCommand(); $cmd.CommandText = $queries[$name]; $cmd.CommandTimeout = 120
    [void]$cmd.Parameters.AddWithValue('@d', [datetime]::ParseExact($AsOfDate, 'yyyy-MM-dd', $null))
    $da = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
    $dt = New-Object System.Data.DataTable
    [void]$da.Fill($dt)
    $path = Join-Path $OutDir $name
    $dt | Export-Csv -NoTypeInformation -Path $path -Encoding utf8
    "{0,-26} {1,6} rows" -f $name, $dt.Rows.Count
  }
} finally { $cn.Close() }
