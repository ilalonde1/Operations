param([int]$AppProcessId = 668, [string]$WindowTitle = 'Business Development Workspace',
      [string]$InvokeId, [string]$InvokeName, [string]$ClickName, [string]$Screenshot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
$root = [System.Windows.Automation.AutomationElement]::RootElement
$process = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$AppProcessId)
$name = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$WindowTitle)
$condition = New-Object System.Windows.Automation.AndCondition($process,$name)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
if (!$window) { throw "Window not found: $WindowTitle" }
if ($ClickName) {
    $targetCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$ClickName)
    $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$targetCondition)
    if (!$element) { throw "Control not found: $ClickName" }
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HubClick {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
}
'@
    [void][HubClick]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)
    $rect = $element.Current.BoundingRectangle
    [void][HubClick]::SetCursorPos([int]($rect.X+$rect.Width/2),[int]($rect.Y+$rect.Height/2))
    [HubClick]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [HubClick]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Write-Output "Clicked $ClickName"
}
if ($InvokeId -or $InvokeName) {
    $property = [System.Windows.Automation.AutomationElement]::AutomationIdProperty
    $value = $InvokeId
    if ($InvokeName) { $property = [System.Windows.Automation.AutomationElement]::NameProperty; $value = $InvokeName }
    $targetCondition = New-Object System.Windows.Automation.PropertyCondition($property,$value)
    $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$targetCondition)
    if (!$element) { throw "Control not found: $value" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "Invoked $value"
}
if ($Screenshot) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HubCapture {
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
'@
    $rect = $window.Current.BoundingRectangle
    $bitmap = New-Object System.Drawing.Bitmap([int]$rect.Width,[int]$rect.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap); $hdc = $graphics.GetHdc()
    try { if (![HubCapture]::PrintWindow([IntPtr]$window.Current.NativeWindowHandle,$hdc,2)) { throw 'PrintWindow failed.' } }
    finally { $graphics.ReleaseHdc($hdc); $graphics.Dispose() }
    $bitmap.Save([System.IO.Path]::GetFullPath($Screenshot),[System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
    Write-Output "Screenshot: $Screenshot"
}
