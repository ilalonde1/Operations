# Generates and runs, ON KOR-APP01 (netops run, SYSTEM), the deployment of KOR-MESH01 on VMHost02 (.16).
# Same proven route as KOR-UNIFI01 (scratchpad/unifi/deploy-vm.ps1), with its two lessons built in: cloud-init via
# guestinfo, and a static MAC. Unlike that one it NEVER destroys anything: an existing KOR-MESH01 stops it.
# The generated script carries the ESXi root password and is deleted here whatever happens.
$ErrorActionPreference = 'Stop'
$sp = Split-Path $PSScriptRoot
$onTarget = @'
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$log = New-Object System.Collections.Generic.List[string]
function L($m) { $log.Add("$((Get-Date).ToString('HH:mm:ss')) $m") }
$dir = 'C:\ProgramData\KorOperations\staging\unifi'   # where govc and the Ubuntu image already are
try {
    $g = "$dir\govc.exe"
    if (-not (Test-Path $g)) { throw "govc not staged at $g" }
    L "govc $((& $g version) -join ' ')"

    # Ubuntu 24.04 cloud image, checked against Ubuntu's CURRENT published SHA-256; re-fetched if Ubuntu has moved on.
    $ova = "$dir\noble-server-cloudimg-amd64.ova"
    $sums = (New-Object Net.WebClient).DownloadString('https://cloud-images.ubuntu.com/noble/current/SHA256SUMS')
    $line = ($sums -split "`n") | Where-Object { $_.Trim() -match 'noble-server-cloudimg-amd64\.ova$' } | Select-Object -First 1
    if (-not $line) { throw 'no SHA-256 line for the OVA in SHA256SUMS' }
    $want = $line.Trim().Split(' ')[0]
    if (-not (Test-Path $ova) -or (Get-FileHash $ova -Algorithm SHA256).Hash.ToLowerInvariant() -ne $want) {
        Invoke-WebRequest 'https://cloud-images.ubuntu.com/noble/current/noble-server-cloudimg-amd64.ova' -OutFile $ova -UseBasicParsing
        L 'downloaded the current Ubuntu cloud image'
    }
    $have = (Get-FileHash $ova -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($want -ne $have) { throw "OVA checksum mismatch: want $want have $have" }
    L "OVA $([math]::Round((Get-Item $ova).Length / 1MB)) MB, SHA-256 verified"

    $env:GOVC_URL = 'https://192.168.1.16/sdk'; $env:GOVC_USERNAME = 'root'; $env:GOVC_PASSWORD = '__ROOTPW__'
    $env:GOVC_INSECURE = '1'; $env:GOVC_DATASTORE = 'UC3200'; $env:GOVC_NETWORK = 'VM Network'
    if (& $g find / -type m -name 'KOR-MESH01') { throw 'a VM named KOR-MESH01 already exists: not touching it' }
    if (Test-Connection 192.168.1.27 -Count 2 -Quiet) { throw '192.168.1.27 answers ping: something already has it; not deploying' }
    L '192.168.1.27 free (no ping); no VM named KOR-MESH01'

    $spec = (& $g import.spec $ova) -join "`n" | ConvertFrom-Json
    $spec.Name = 'KOR-MESH01'; $spec.DiskProvisioning = 'thin'; $spec.PowerOn = $false; $spec.MarkAsTemplate = $false
    foreach ($n in $spec.NetworkMapping) { $n.Network = 'VM Network' }
    $spec | ConvertTo-Json -Depth 6 | Set-Content "$dir\mesh-spec.json" -Encoding ASCII
    $imp = (& $g import.ova -options "$dir\mesh-spec.json" $ova 2>&1) -join ' '
    L "import: $($imp.Substring(0, [Math]::Min(200, $imp.Length)))"
    & $g vm.change -vm KOR-MESH01 -c 2 -m 4096 -annotation 'MeshCentral: KOR self-hosted remote screen for the PCs. NetworkOps. 192.168.1.27. Built 2026-09-30.'
    & $g vm.change -vm KOR-MESH01 -e 'guestinfo.metadata=__METADATA__' -e 'guestinfo.metadata.encoding=base64' -e 'guestinfo.userdata=__USERDATA__' -e 'guestinfo.userdata.encoding=base64'
    L 'guestinfo metadata + userdata set'
    & $g vm.disk.change -vm KOR-MESH01 -size 40G
    # A direct-to-host import leaves a vCenter-type ("vpx") MAC the vSwitch blocks outright: use a static one (UniFi lesson).
    & $g vm.network.change -vm KOR-MESH01 -net 'VM Network' '-net.address' '00:50:56:1a:01:27' ethernet-0
    # READ IT BACK (2026-09-30: this change silently did not stick, the VM booted with a vpx MAC and was cut off).
    $mac = ((& $g device.info -vm KOR-MESH01 ethernet-0) | Select-String 'MAC Address').ToString()
    if ($mac -notmatch '00:50:56:1a:01:27') { throw "static MAC not applied ($mac): run fix-mac.ps1 with the VM off" }
    & $g vm.power -on KOR-MESH01 | Out-Null
    L 'powered on'
    $info = (& $g vm.info -r KOR-MESH01) -join ' | '
    L "info: $($info.Substring(0, [Math]::Min(600, $info.Length)))"
}
catch { L "FAILED: $($_.Exception.Message)" }
finally {
    Remove-Item "$dir\mesh-spec.json" -ErrorAction SilentlyContinue
    Remove-Item Env:\GOVC_PASSWORD -ErrorAction SilentlyContinue
}
[pscustomobject]@{ Log = ($log -join ' || ') }
'@
$userDataB64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'user-data.yaml')))
$metaDataB64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'meta-data.yaml')))
$rootPw = [Net.NetworkCredential]::new('', (Get-Content (Join-Path $sp 'device-pw.dpapi') | ConvertTo-SecureString)).Password
$onTarget = $onTarget.Replace('__ROOTPW__', $rootPw).Replace('__USERDATA__', $userDataB64).Replace('__METADATA__', $metaDataB64)
$gen = Join-Path $PSScriptRoot 'deploy-vm.ONTARGET.ps1'
try {
    [IO.File]::WriteAllText($gen, $onTarget)
    $exe = Get-ChildItem 'C:\VIsual Studio Projects\Operations\Kor.Operations.NetworkOps.Cli\bin' -Recurse -Filter netops.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    & $exe run --script $gen --hosts KOR-APP01 --timeout 1800
}
finally {
    if (Test-Path $gen) { [IO.File]::Delete($gen) }
    "generated script deleted: $(-not (Test-Path $gen))"
}
