# FIX enable-wake-on-lan: makes a magic packet able to wake this PC from shutdown. Changes, only where needed:
#   - Fast Startup off (HiberbootEnabled=0): "Shut down" becomes a real shutdown the network card can wake from;
#   - every wired network card: Wake on Magic Packet on, allowed to wake the computer, PME (its wake signal) on, and the
#     vendor "wake from power off" switch on (Realtek S5WakeOnLan, Marvell WakeFromPowerOff);
#   - Lenovo only, when the BIOS says Disabled: WakeOnLAN -> Automatic (fails harmlessly if a BIOS password is set).
# Card settings are written with -NoRestart: resetting the card now would drop this very connection; they apply as
# the card next initialises (the next restart or shutdown -- which is exactly when wake matters). Returns plain values.
$ErrorActionPreference = 'Stop'
$done = New-Object System.Collections.Generic.List[string]
$failed = New-Object System.Collections.Generic.List[string]
function Step([string]$what, [scriptblock]$body) { try { & $body; $done.Add($what) } catch { $failed.Add("${what}: $($_.Exception.Message)") } }

$power = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power'
if ((Get-ItemProperty $power -Name HiberbootEnabled -ErrorAction SilentlyContinue).HiberbootEnabled -ne 0) {
    Step 'Fast Startup off' { Set-ItemProperty $power -Name HiberbootEnabled -Value 0 -Type DWord }
}

$armed = @(powercfg /devicequery wake_armed)
foreach ($a in @(Get-NetAdapter -Physical | Where-Object { $_.MediaType -eq '802.3' -and $_.InterfaceDescription -notmatch 'Wi-?Fi|Wireless|Bluetooth|Virtual' })) {
    $n = $a.InterfaceDescription
    $pm = Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction SilentlyContinue
    if ($pm -and [string]$pm.WakeOnMagicPacket -ne 'Enabled') { Step "$n wake on magic packet" { Enable-NetAdapterPowerManagement -Name $a.Name -WakeOnMagicPacket -NoRestart } }
    foreach ($kw in 'EnablePME', 'S5WakeOnLan', 'WakeFromPowerOff', '*WakeOnMagicPacket') {
        $p = Get-NetAdapterAdvancedProperty -Name $a.Name -RegistryKeyword $kw -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        $on = @($p.ValidDisplayValues) | Where-Object { $_ -match '^(Enabled|On)$' } | Select-Object -First 1
        if ($on -and $p.DisplayValue -ne $on) { Step "$n $kw" { Set-NetAdapterAdvancedProperty -Name $a.Name -RegistryKeyword $kw -DisplayValue $on -NoRestart } }
    }
    if ($armed -notcontains $n) {
        Step "$n allowed to wake the computer" { & powercfg.exe /deviceenablewake $n | Out-Null; if ($LASTEXITCODE -ne 0) { throw "powercfg exit $LASTEXITCODE" } }
    }
}

if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -match 'LENOVO') {
    $s = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_BiosSetting -ErrorAction SilentlyContinue | Where-Object { $_.CurrentSetting -match '^WakeOnLAN,' } | Select-Object -First 1
    if ($s -and ($s.CurrentSetting -split ';')[0] -match ',Disabled$') {
        Step 'BIOS WakeOnLAN -> Automatic' {
            $set = (Get-WmiObject -Namespace root\wmi -Class Lenovo_SetBiosSetting).SetBiosSetting('WakeOnLAN,Automatic').return
            if ($set -ne 'Success') { throw "set returned $set" }
            $save = (Get-WmiObject -Namespace root\wmi -Class Lenovo_SaveBiosSettings).SaveBiosSettings('').return
            if ($save -ne 'Success') { throw "save returned $save (a BIOS password is set: change it at the console)" }
        }
    }
}

[pscustomobject]@{
    Result = if ($failed.Count) { "Wake-on-LAN: $($done.Count) changed, $($failed.Count) FAILED: $($failed -join ' | ')" }
             elseif ($done.Count) { "Wake-on-LAN ready: $($done -join '; '). Card settings apply at the next restart or shutdown." }
             else { 'Wake-on-LAN was already set up; nothing to change.' }
    Changed = @($done); Failed = @($failed)
}
