# ON KOR-BK01: add KOR-MESH01 to the job Kor-VMs-New with the SAME per-object guest options as KOR-UNIFI01 (the other
# Linux VM: no application processing, no indexing -- the object-level LinGuestFSIndexingType is what matters).
# Idempotent; reads both back.
$ErrorActionPreference = 'Stop'
Import-Module Veeam.Backup.PowerShell -WarningAction SilentlyContinue
$job = Get-VBRJob -Name 'Kor-VMs-New'
if (-not (Get-VBRJobObject -Job $job | Where-Object Name -eq 'KOR-MESH01')) {
    $entity = Find-VBRViEntity -Name 'KOR-MESH01' | Select-Object -First 1
    if (-not $entity) { throw 'Veeam cannot see a VM named KOR-MESH01 (rescan the host?)' }
    Add-VBRViJobObject -Job $job -Entities $entity | Out-Null
}
$job = Get-VBRJob -Name 'Kor-VMs-New'
$model = (Get-VBRJobObject -Job $job | Where-Object Name -eq 'KOR-UNIFI01').VssOptions
$mesh = Get-VBRJobObject -Job $job | Where-Object Name -eq 'KOR-MESH01'
$o = $mesh.VssOptions
$o.Enabled = $model.Enabled
$o.GuestFSIndexingType = $model.GuestFSIndexingType
$o.LinGuestFSIndexingType = $model.LinGuestFSIndexingType
Set-VBRJobObjectVssOptions -Object $mesh -Options $o | Out-Null
$job = Get-VBRJob -Name 'Kor-VMs-New'
Get-VBRJobObject -Job $job | ForEach-Object {
    [pscustomobject]@{ Name = [string]$_.Name; AppProcessing = [string]$_.VssOptions.Enabled; WinIndexing = [string]$_.VssOptions.GuestFSIndexingType; LinIndexing = [string]$_.VssOptions.LinGuestFSIndexingType }
}
