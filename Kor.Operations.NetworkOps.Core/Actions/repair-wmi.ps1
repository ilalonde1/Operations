# FIX repair-wmi: verify, salvage only if inconsistent, restart, prove it answers. Returns plain values.
$verify = "$((& winmgmt.exe /verifyrepository) -join ' ')"
$salvage = ''
if ($verify -notmatch 'consistent' -or $verify -match 'inconsistent|not consistent') { $salvage = "$((& winmgmt.exe /salvagerepository) -join ' ')" }
Restart-Service Winmgmt -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 5
$ok = [bool](Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue)
[pscustomobject]@{ Result = if ($ok) { 'WMI answers again.' } else { 'WMI still does not answer: next is `winmgmt /resetrepository` and a restart.' }; Verify = $verify; Salvage = $salvage }
