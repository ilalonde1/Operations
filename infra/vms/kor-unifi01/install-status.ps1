#Requires -Version 7
<#
.SYNOPSIS
    Installs kor-unifi-status (beside this file) on KOR-UNIFI01 as /usr/local/bin/kor-unifi-status -- the one command the
    NetworkOps `netops` login may run -- then runs it and proves the output is the JSON NetworkOps reads.

.DESCRIPTION
    Runs from a PC holding the VM's admin key (%USERPROFILE%\.ssh\kor-unifi01, koradmin). The old script is kept as
    /usr/local/bin/kor-unifi-status.previous and put back if the new one does not produce valid JSON with devices, clients
    and openAlarms. Line endings are forced to LF on the VM (.gitattributes keeps them LF in the repo too).
#>
param(
    [string]$Address = '192.168.1.26',
    [string]$Key = (Join-Path $env:USERPROFILE '.ssh\kor-unifi01')
)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'kor-unifi-status'
if (-not (Test-Path $src)) { throw "not found: $src" }
if (-not (Test-Path $Key)) { throw "the VM admin key is not on this PC: $Key" }
$ssh = @('-i', $Key, '-o', 'BatchMode=yes', "koradmin@$Address")

& scp -q -i $Key -o BatchMode=yes $src "koradmin@${Address}:/tmp/kor-unifi-status.new"
if ($LASTEXITCODE -ne 0) { throw 'copy to the VM failed' }

$install = 'set -e; f=/tmp/kor-unifi-status.new; sudo sed -i "s/\r$//" $f; bash -n $f; ' +
           'sudo cp -p /usr/local/bin/kor-unifi-status /usr/local/bin/kor-unifi-status.previous; ' +
           'sudo install -m 755 -o root -g root $f /usr/local/bin/kor-unifi-status; rm -f $f; sha256sum /usr/local/bin/kor-unifi-status'
& ssh @ssh $install
if ($LASTEXITCODE -ne 0) { throw 'install failed (the previous script is untouched)' }

$json = & ssh @ssh 'sudo -n /usr/local/bin/kor-unifi-status'
try
{
    $o = $json | ConvertFrom-Json -Depth 20
    if ($null -eq $o.devices -or $null -eq $o.clients -or $null -eq $o.openAlarms) { throw 'devices, clients or openAlarms missing' }
    $placed = @($o.clients | Where-Object { $_.uplinkMac -and $_.port }).Count
    "Installed. $($o.devices.Count) devices, $($o.clients.Count) clients seen in 30 days ($placed with a switch port), $($o.openAlarms.Count) open alarms."
}
catch
{
    & ssh @ssh 'sudo cp -p /usr/local/bin/kor-unifi-status.previous /usr/local/bin/kor-unifi-status'
    throw "the new script's output is not what NetworkOps reads ($($_.Exception.Message)) -- the previous script is back"
}
