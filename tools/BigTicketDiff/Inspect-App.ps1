param([int]$AppProcessId = 668)
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$AppProcessId)
$windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children,$condition)
foreach ($window in $windows) {
    $elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $elements) {
        $c = $element.Current
        if ($c.Name -and $c.ControlType.ProgrammaticName -match 'Button|TabItem|Window|MenuItem|Text|ComboBox') {
            '{0} | {1} | {2}' -f $c.ControlType.ProgrammaticName,$c.AutomationId,$c.Name
        }
    }
}
