# fs01-sql-backups-state.ps1 -- READ-ONLY, runs ON KOR-FS01 (netops run --script <this> --hosts KOR-FS01).
# Board row 34 (14-day SQL backup retention, @CleanupTime=336, first run 28 Oct 01:33/01:47): per database folder,
# how many .bak files are left, the oldest, the total size, and how many are older than the cleanup cut-off (should
# be 0 once a night has run). Reads E: locally -- never walks the share over the VPN.
param([string]$Root = 'E:\Library\ADMIN\SQL SERVER\BACKUPS\Kor-APP01$SQLEXPRESS', [datetime]$Cutoff = '2026-10-14T01:33:00')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Root)) { "ROOT NOT FOUND: $Root"; exit 2 }
$vol = Get-PSDrive -Name ($Root.Substring(0, 1))
"root $Root ; volume free $([math]::Round($vol.Free / 1GB, 1)) GB of $([math]::Round(($vol.Free + $vol.Used) / 1GB, 1)) GB"
$all = @(); $rows = foreach ($db in Get-ChildItem -LiteralPath $Root -Directory) {
    $f = @(Get-ChildItem -LiteralPath $db.FullName -Recurse -File -Filter *.bak)
    $all += $f
    $old = @($f | Where-Object LastWriteTime -lt $Cutoff)
    [pscustomobject]@{ Db = $db.Name; Files = $f.Count; OlderThanCutoff = $old.Count
        Oldest = if ($f) { ($f | Sort-Object LastWriteTime | Select-Object -First 1).LastWriteTime.ToString('yyyy-MM-dd HH:mm') } else { '-' }
        GB = [math]::Round(($f | Measure-Object Length -Sum).Sum / 1GB, 2) }
}
$rows | Sort-Object GB -Descending | Format-Table -AutoSize | Out-String -Width 200
$other = @(Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object Extension -ne '.bak')
"TOTAL: $($all.Count) .bak in $(@($rows).Count) folders, $([math]::Round(($all | Measure-Object Length -Sum).Sum / 1GB, 1)) GB; older than cut-off $($Cutoff.ToString('yyyy-MM-dd HH:mm')): $(@($all | Where-Object LastWriteTime -lt $Cutoff).Count); non-.bak files: $($other.Count)"
