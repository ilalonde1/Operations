# Checks the running App's visible count and invokes its real reveal button.
# Does not prove business classification or pixel legibility; screenshots require review.
# Same-class miss: a wrong-but-consistent SQL classification can pass UI reachability.
param([int]$AppProcessId = 668, [string]$Evidence = 'tools/BigTicketDiff/evidence')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$process = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$AppProcessId)
$name = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Business Development Workspace')
$condition = New-Object System.Windows.Automation.AndCondition($process,$name)
$window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
function Find-Control([string]$Id) {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)
}
if (!$window) { throw 'BD workspace is not running.' }
$reveal = Find-Control 'SmallWorkReveal'
if (!$reveal) { throw 'FAIL: the running Signals screen has no small-work reveal/count control.' }
if ($reveal.Current.Name -match 'Hide smaller items') {
    $reveal.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
$before = $reveal.Current.Name
if ($before -notmatch '([\d,]+) smaller items hidden') { throw "FAIL: missing collapsed count: $before" }
$hidden = [int]($Matches[1].Replace(',',''))
$beforeCount = (Find-Control 'SmallWorkCount').Current.Name
if ($beforeCount -notmatch '^([\d,]+) matching') { throw "FAIL: no visible row count: $beforeCount" }
$visibleBefore = [int]($Matches[1].Replace(',',''))
& "$PSScriptRoot/Interact-App.ps1" -AppProcessId $AppProcessId -Screenshot "$Evidence/signals-collapsed.png"
$reveal.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$deadline = [DateTime]::UtcNow.AddSeconds(5)
do { $after = (Find-Control 'SmallWorkReveal').Current.Name } while ($after -notmatch 'Hide smaller items' -and [DateTime]::UtcNow -lt $deadline)
if ($after -notmatch 'Hide smaller items') { throw "FAIL: reveal did not change state: $after" }
& "$PSScriptRoot/Interact-App.ps1" -AppProcessId $AppProcessId -Screenshot "$Evidence/signals-revealed.png"
$count = (Find-Control 'SmallWorkCount').Current.Name
if ($count -notmatch '^([\d,]+) matching' -or [int]($Matches[1].Replace(',','')) -ne ($visibleBefore+$hidden)) {
    throw "FAIL: reveal count does not equal default plus hidden: $beforeCount -> $count"
}
"PASS: $before -> $after. $count" | Set-Content "$Evidence/app-verification.txt"
Get-Content "$Evidence/app-verification.txt"
