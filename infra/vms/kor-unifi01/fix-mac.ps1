# KOR-UNIFI01: give the NIC a STATIC MAC (VMware's manual range 00:50:56:00-3F), so the port stops rejecting
# the vCenter-type ("vpx") MAC the direct-to-host import left it with. vSwitch security stays at reject.
$g = Join-Path $PSScriptRoot '..\govc\govc.exe'
$env:GOVC_URL = 'https://192.168.1.16/sdk'; $env:GOVC_USERNAME = 'root'; $env:GOVC_PASSWORD = ([Net.NetworkCredential]::new('', (Get-Content (Join-Path $PSScriptRoot 'device-pw.dpapi') | ConvertTo-SecureString)).Password); $env:GOVC_INSECURE = '1'
$mac = '00:50:56:1a:01:26'
try {
    # the MAC must not already be on any VM on this host
    $taken = & $g find / -type m | ForEach-Object { & $g device.info -vm $_ 'ethernet-*' 2>$null } | Select-String -SimpleMatch $mac
    if ($taken) { throw "$mac already in use: $taken" }
    & $g vm.power -s KOR-UNIFI01 2>&1 | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Seconds 3; $state = ((& $g vm.info KOR-UNIFI01) | Select-String 'Power state:').ToString() } while ($state -notmatch 'poweredOff' -and $sw.Elapsed.TotalSeconds -lt 120)
    if ($state -notmatch 'poweredOff') { & $g vm.power -off -force KOR-UNIFI01; "guest shutdown timed out: powered off" } else { "guest shut down in $([int]$sw.Elapsed.TotalSeconds)s" }
    & $g vm.network.change -vm KOR-UNIFI01 -net 'VM Network' '-net.address' $mac ethernet-0
    & $g vm.power -on KOR-UNIFI01 | Out-Null
    ((& $g device.info -vm KOR-UNIFI01 ethernet-0) | Select-String 'MAC Address|Summary|Connected') -join ' | '
}
finally { Remove-Item Env:\GOVC_PASSWORD -ErrorAction SilentlyContinue }
