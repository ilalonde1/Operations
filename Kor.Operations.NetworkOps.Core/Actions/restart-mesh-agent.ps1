# FIX restart-mesh-agent: run through the Windows channel, independent of the wedged Mesh agent.
# Kill its PID: asking the wedged service to stop can leave it stuck in Stop Pending.
$name = 'Mesh Agent'
$svc = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
if (-not $svc) {
    [pscustomobject]@{ Result = 'Mesh Agent service is not installed on this PC'; Installed = $false }
    return
}
$before = '{0}/{1} pid={2}' -f $svc.State, $svc.StartMode, $svc.ProcessId
$killed = ''
if ($svc.ProcessId) {
    try { Stop-Process -Id $svc.ProcessId -Force -ErrorAction Stop; $killed = "killed pid $($svc.ProcessId)" }
    catch { $killed = "could not kill pid $($svc.ProcessId): $($_.Exception.Message)" }
}
Start-Sleep -Seconds 5
$started = ''
try { Start-Service -Name $name -ErrorAction Stop; $started = 'started' }
catch { $started = "start failed: $($_.Exception.Message)" }
Start-Sleep -Seconds 5
$svc2 = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
$after = if ($svc2) { '{0}/{1} pid={2}' -f $svc2.State, $svc2.StartMode, $svc2.ProcessId } else { 'gone' }
$est = 0
if ($svc2 -and $svc2.ProcessId) {
    $est = @(Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue |
        Where-Object { $_.OwningProcess -eq $svc2.ProcessId -and $_.RemotePort -eq 443 }).Count
}
[pscustomobject]@{
    Result = "Mesh Agent: $before -> $after ($killed; $started; 443-established=$est)"
    Installed = $true
    Running = ($svc2 -and $svc2.State -eq 'Running')
    Before = $before
    After = $after
    Est443 = $est
}
