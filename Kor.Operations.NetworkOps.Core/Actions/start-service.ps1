# FIX start-service: $Param = the service name (validated by FixCatalog: a bare identifier). Delayed-auto start and
# restart-on-failure first, so it survives the next reboot and the next crash; then start it. Plain values.
$s = Get-Service -Name $Param -ErrorAction SilentlyContinue
if (-not $s) { [pscustomobject]@{ Result = "No service named $Param on this machine." }; return }
& sc.exe config $Param start= delayed-auto | Out-Null
& sc.exe failure $Param reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
try { Start-Service -Name $Param -ErrorAction Stop } catch { }
Start-Sleep -Seconds 8
$now = (Get-Service -Name $Param).Status
[pscustomobject]@{ Result = if ("$now" -eq 'Running') { "$Param is running (delayed start, restarts itself on failure)." } else { "$Param did not start: it is $now. Read its event log." }; Status = "$now" }
