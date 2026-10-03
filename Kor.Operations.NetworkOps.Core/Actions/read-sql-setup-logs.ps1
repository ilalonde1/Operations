# FIX read-sql-setup-logs (read-only): why a SQL Server update failed, in SQL Setup's own words. Windows Update reports
# only a generic code (0x80070643); SQL Setup writes the reason to Summary*.txt under "Setup Bootstrap\Log\<date_time>".
# KOR-223N, 2026-10-01: KB5122773 (the GDR-branch package) failed because the same batch had just moved the instance
# onto the CU branch -- Setup said so plainly ("part of a general distribution release (GDR) ... cannot be applied").
# Reads every installed SQL version's logs from the last 14 days, and each instance's patch level. Changes nothing.
$ErrorActionPreference = 'Stop'
$since = (Get-Date).AddDays(-14)
$roots = @(Get-ChildItem 'C:\Program Files\Microsoft SQL Server' -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d{3}$' } | ForEach-Object { Join-Path $_.FullName 'Setup Bootstrap\Log' } | Where-Object { Test-Path $_ })
$runs = @(foreach ($root in $roots) {
    foreach ($d in @(Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $since } | Sort-Object Name)) {
        $summary = Get-ChildItem $d.FullName -Filter 'Summary*.txt' -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $summary) { continue }
        $lines = @(Get-Content $summary.FullName -TotalCount 120 | ForEach-Object { $_.Trim() })
        function Field($label) { ($lines | Where-Object { $_ -like "$label*" } | Select-Object -First 1) -replace "^$([regex]::Escape($label))\s*", '' }
        [pscustomobject]@{
            When = $d.Name; Version = Split-Path (Split-Path (Split-Path $root -Parent) -Parent) -Leaf
            Kb = Field 'KBArticle:'; Action = Field 'Requested action:'
            Result = Field 'Final result:'; ExitCode = Field 'Exit code (Decimal):'
            Message = (Field 'Exit message:')
        }
    }
})
$instances = @()
$names = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -ErrorAction SilentlyContinue
if ($names) {
    $instances = @($names.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object {
        $setup = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$($_.Value)\Setup" -ErrorAction SilentlyContinue
        [pscustomobject]@{ Instance = [string]$_.Name; PatchLevel = [string]$setup.PatchLevel; Edition = [string]$setup.Edition } })
}
$failed = @($runs | Where-Object { $_.Result -and $_.Result -notmatch '^Passed' })
[pscustomobject]@{
    Result = if ($roots.Count -eq 0) { 'No SQL Server installed: nothing to read.' }
             elseif ($runs.Count -eq 0) { 'No SQL Setup run in the last 14 days.' }
             elseif ($failed.Count -eq 0) { "$($runs.Count) SQL Setup run(s) in 14 days, all passed." }
             else { "$($failed.Count) of $($runs.Count) SQL Setup run(s) failed; newest: KB$($failed[-1].Kb -replace '^KB','') -- $($failed[-1].Message)" }
    Instances = $instances
    Runs = $runs
}
