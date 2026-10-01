# KOR-MESH01: give the NIC a STATIC MAC (VMware's manual range 00:50:56:00-3F) so the port stops rejecting the
# vCenter-type ("vpx") MAC the direct-to-host import left it with -- the UniFi lesson; the deploy's own MAC change
# did not stick and was not checked. vSwitch security stays at reject. Reads the MAC back and fails if it is wrong.
$ErrorActionPreference = 'Stop'
$sp = Split-Path $PSScriptRoot
$g = Join-Path $sp 'govc\govc.exe'
$env:GOVC_URL = 'https://192.168.1.16/sdk'; $env:GOVC_USERNAME = 'root'; $env:GOVC_INSECURE = '1'
$env:GOVC_PASSWORD = [Net.NetworkCredential]::new('', (Get-Content (Join-Path $sp 'device-pw.dpapi') | ConvertTo-SecureString)).Password
$mac = '00:50:56:1a:01:27'
try {
    $taken = & $g find / -type m | Where-Object { $_ -notmatch 'KOR-MESH01$' } | ForEach-Object { & $g device.info -vm $_ 'ethernet-*' 2>$null } | Select-String -SimpleMatch $mac
    if ($taken) { throw "$mac already in use: $taken" }
    & $g vm.power -s KOR-MESH01 2>&1 | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { [Threading.Thread]::Sleep(3000); $state = ((& $g vm.info KOR-MESH01) | Select-String 'Power state:').ToString() } while ($state -notmatch 'poweredOff' -and $sw.Elapsed.TotalSeconds -lt 120)
    if ($state -notmatch 'poweredOff') { & $g vm.power -off -force KOR-MESH01 | Out-Null; 'guest shutdown timed out: powered off' } else { "guest shut down in $([int]$sw.Elapsed.TotalSeconds) s" }
    $out = & $g vm.network.change -vm KOR-MESH01 -net 'VM Network' '-net.address' $mac ethernet-0 2>&1
    "network.change: $out"
    $now = ((& $g device.info -vm KOR-MESH01 ethernet-0) | Select-String 'MAC Address').ToString().Trim()
    if ($now -notmatch [regex]::Escape($mac)) { throw "MAC did not change: $now" }
    "MAC now: $now"
    & $g vm.power -on KOR-MESH01 | Out-Null
    'powered on'
}
finally { Remove-Item Env:\GOVC_PASSWORD -ErrorAction SilentlyContinue }
