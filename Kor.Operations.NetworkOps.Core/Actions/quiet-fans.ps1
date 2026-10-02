# FIX quiet-fans: sets a Lenovo workstation's BIOS cooling profile to its quiet setting, which takes effect at the next
# restart (nothing restarts now). The quiet value differs by BIOS family, so it is read from the PC's own list of
# allowed values, never assumed (measured 2026-10-01 on 13 PCs):
#   ThinkStation P340 (30DH / 30DK)      Best Performance | Best Experience | Full Speed   -> Best Experience
#   ThinkStation P350 / P360 (30FM/30GS) Balance mode | Performance mode | Full Speed      -> Balance mode
# Fails, changing nothing, where the PC is not a Lenovo, has no such setting, or a BIOS password is set.
$ErrorActionPreference = 'Stop'
if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notmatch 'LENOVO') {
    return [pscustomobject]@{ Result = 'Not a Lenovo: its BIOS cannot be changed from Windows. Change the fan profile at the console.' }
}
$line = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_BiosSetting |
    Where-Object { $_.CurrentSetting -match '^IntelligentCoolingPerformanceMode,' } | Select-Object -First 1 -ExpandProperty CurrentSetting
if (-not $line) { return [pscustomobject]@{ Result = 'This BIOS has no IntelligentCoolingPerformanceMode setting: change the fan profile at the console.' } }

$current = (($line -split ';')[0] -split ',', 2)[1].Trim()
$allowed = if ($line -match '\[Optional:([^\]]+)\]') { $Matches[1] -split ',' | ForEach-Object { $_.Trim() } } else { @() }
$quiet = @('Balance mode', 'Best Experience') | Where-Object { $allowed -contains $_ } | Select-Object -First 1
if (-not $quiet) { return [pscustomobject]@{ Result = "No quiet profile among this BIOS's choices ($($allowed -join ', ')): nothing changed." } }
if ($current -eq $quiet) { return [pscustomobject]@{ Result = "Already '$quiet': nothing to change." } }

$set = (Get-WmiObject -Namespace root\wmi -Class Lenovo_SetBiosSetting).SetBiosSetting("IntelligentCoolingPerformanceMode,$quiet").return
if ($set -ne 'Success') { throw "the BIOS refused the setting ($set)" }
$save = (Get-WmiObject -Namespace root\wmi -Class Lenovo_SaveBiosSettings).SaveBiosSettings('').return
if ($save -ne 'Success') { throw "the BIOS did not save it ($save): a BIOS password is set, so change it at the console" }

# Read it back: what the BIOS now holds for the next boot.
$after = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_BiosSetting |
    Where-Object { $_.CurrentSetting -match '^IntelligentCoolingPerformanceMode,' } | Select-Object -First 1 -ExpandProperty CurrentSetting
[pscustomobject]@{
    Result = "Fan profile '$current' -> '$quiet'. It takes effect at the next restart; nothing restarted now."
    Before = $current; After = (($after -split ';')[0] -split ',', 2)[1].Trim()
}
