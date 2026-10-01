# updates.ps1 -- runs ON the machine (Windows PowerShell 5.1, as SYSTEM). Read-only: asks Windows Update, online,
# what is waiting to be installed. Installs nothing, downloads nothing, changes no setting. The leftover policy that
# turned automatic updates off (NoAutoUpdate=1) does not stop this search, and does not stop an install NetworkOps
# starts (Actions/install-updates.ps1) -- which is why nothing has to change on a machine for patching to be ours.
#
# Drivers are left out on purpose: GPU and Revit-certified drivers stay a deliberate choice (Core/Updates/UpdateRules).
$ErrorActionPreference = 'Stop'
$sw = [Diagnostics.Stopwatch]::StartNew()
$session = New-Object -ComObject Microsoft.Update.Session
$session.ClientApplicationID = 'KOR NetworkOps'
$searcher = $session.CreateUpdateSearcher()
$searcher.Online = $true
$found = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
$updates = @(foreach ($u in $found.Updates) {
    $cats = @($u.Categories | ForEach-Object { [string]$_.Name })
    [pscustomobject]@{
        Kb          = (@($u.KBArticleIDs) | Select-Object -First 1) -as [string]
        Title       = [string]$u.Title
        Categories  = $cats -join '; '
        Security    = [bool]($cats -match '^(Security Updates|Critical Updates)$')
        Severity    = [string]$u.MsrcSeverity
        Released    = if ($u.LastDeploymentChangeTime) { $u.LastDeploymentChangeTime.ToString('s') } else { $null }
        SizeMB      = [math]::Round([double]$u.MaxDownloadSize / 1MB, 1)
        Downloaded  = [bool]$u.IsDownloaded
        NeedsReboot = [int]$u.InstallationBehavior.RebootBehavior -ne 0
    }
})
[pscustomobject]@{
    ScanVersion   = 1
    ScannedAt     = (Get-Date).ToString('s')
    Computer      = $env:COMPUTERNAME
    Updates       = @($updates)
    RebootPending = [bool]((Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or
                           (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'))
    SearchMs      = [int]$sw.ElapsedMilliseconds
}
