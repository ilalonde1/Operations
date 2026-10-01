# FIX display-never-off: for a PC that runs with no keyboard or mouse. Sets "Turn off the display" to Never (plugged in
# and on battery) in the active power plan, so a monitor connected later shows the screen at once. Sleep, hibernate
# and the screen lock are untouched. Returns plain values.
$ErrorActionPreference = 'Stop'
& powercfg.exe /change monitor-timeout-ac 0
$ac = $LASTEXITCODE
& powercfg.exe /change monitor-timeout-dc 0
$after = @(powercfg /q SCHEME_CURRENT SUB_VIDEO VIDEOIDLE | Select-String 'Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)' | ForEach-Object { [Convert]::ToInt32($_.Matches[0].Groups[1].Value, 16) })
[pscustomobject]@{
    Result = if ($ac -eq 0 -and $after.Count -and $after[0] -eq 0) { 'The display now stays on (never turns off); a monitor plugged in shows the screen at once.' }
             else { "powercfg did not take (exit $ac, display off after now $($after -join ',') s)" }
    OffAfterSeconds = if ($after.Count) { $after[0] } else { $null }
}
