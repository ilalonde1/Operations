# Generates and runs, ON KOR-APP01 (netops run, SYSTEM), the deployment of KOR-UNIFI01 on VMHost02.
# The generated script carries the ESXi root password and is deleted here whatever happens.
$ErrorActionPreference = 'Stop'
$sp = Split-Path $PSScriptRoot
$userDataB64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'user-data.yaml')))
$onTarget = @'
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$log = New-Object System.Collections.Generic.List[string]
function L($m) { $log.Add("$((Get-Date).ToString('HH:mm:ss')) $m") }
$dir = 'C:\ProgramData\KorOperations\staging\unifi'
New-Item -ItemType Directory -Force $dir | Out-Null
try {
    # 1. govc (VMware's own CLI)
    if (-not (Test-Path "$dir\govc.exe")) {
        Invoke-WebRequest 'https://github.com/vmware/govmomi/releases/latest/download/govc_Windows_x86_64.zip' -OutFile "$dir\govc.zip" -UseBasicParsing
        Expand-Archive "$dir\govc.zip" $dir -Force
    }
    $g = "$dir\govc.exe"
    L "govc $((& $g version) -join ' ')"

    # 2. Ubuntu 24.04 cloud image, checked against Ubuntu's published SHA-256
    $ova = "$dir\noble-server-cloudimg-amd64.ova"
    if (-not (Test-Path $ova)) { Invoke-WebRequest 'https://cloud-images.ubuntu.com/noble/current/noble-server-cloudimg-amd64.ova' -OutFile $ova -UseBasicParsing }
    # DownloadString, not Invoke-WebRequest: PS 5.1 hands an octet-stream back as bytes, not text.
    $sums = (New-Object Net.WebClient).DownloadString('https://cloud-images.ubuntu.com/noble/current/SHA256SUMS')
    $line = ($sums -split "`n") | Where-Object { $_.Trim() -match 'noble-server-cloudimg-amd64\.ova$' } | Select-Object -First 1
    if (-not $line) { throw 'no SHA-256 line for the OVA in SHA256SUMS' }
    $want = $line.Trim().Split(' ')[0]
    $have = (Get-FileHash $ova -Algorithm SHA256).Hash
    if (-not $want -or $want -ne $have.ToLowerInvariant()) { throw "OVA checksum mismatch: want $want have $have" }
    L "OVA $([math]::Round((Get-Item $ova).Length / 1MB)) MB, SHA-256 verified"

    # 3. deploy on VMHost02, datastore UC3200, VM Network
    $env:GOVC_URL = 'https://192.168.1.16/sdk'; $env:GOVC_USERNAME = 'root'; $env:GOVC_PASSWORD = '__ROOTPW__'
    $env:GOVC_INSECURE = '1'; $env:GOVC_DATASTORE = 'UC3200'; $env:GOVC_NETWORK = 'VM Network'
    $existing = (& $g find / -type m -name 'KOR-UNIFI01') -join ''
    if ($existing) {
        # Only ever a VM this build created (from this image, UUID 564da410-...): never another.
        # 14:25 rebuild: the current one held the full Tenacious restore (other clients' sites) -- destroyed
        # whole, so none of that data survives anywhere on the disk.
        $uuid = ((& $g vm.info KOR-UNIFI01) | Where-Object { $_ -match 'UUID:' }) -replace '.*UUID:\s*', ''
        if ($uuid.Trim() -ne '564da410-bd03-dc80-9641-ef5968a01789') { throw "a VM named KOR-UNIFI01 exists with UUID $uuid, not the one this build made: not touching it" }
        & $g vm.destroy KOR-UNIFI01
        L "destroyed the existing KOR-UNIFI01 (UUID $($uuid.Trim()))"
    }
    if (& $g find / -type m -name 'KOR-UNIFI01') { throw 'KOR-UNIFI01 still exists after destroy' }

    # 0. the address must be free -- after the destroy, which is what frees it. Ping only: ARP entries
    # outlive the machine that owned them.
    $free = $false
    for ($i = 0; $i -lt 12 -and -not $free; $i++) { if (-not (Test-Connection 192.168.1.26 -Count 1 -Quiet)) { $free = $true } else { Start-Sleep -Seconds 5 } }
    if (-not $free) { throw '192.168.1.26 still answers after the destroy: something else has it; not deploying' }
    L '192.168.1.26 free (no ping)'
    $spec = (& $g import.spec $ova) -join "`n" | ConvertFrom-Json
    $spec.Name = 'KOR-UNIFI01'; $spec.DiskProvisioning = 'thin'; $spec.PowerOn = $false; $spec.MarkAsTemplate = $false
    foreach ($n in $spec.NetworkMapping) { $n.Network = 'VM Network' }
    foreach ($p in $spec.PropertyMapping) {
        switch ($p.Key) { 'instance-id' { $p.Value = 'kor-unifi01-1' } 'hostname' { $p.Value = 'kor-unifi01' } 'user-data' { $p.Value = '__USERDATA__' } }
    }
    $spec | ConvertTo-Json -Depth 6 | Set-Content "$dir\spec.json" -Encoding ASCII
    $imp = (& $g import.ova -options "$dir\spec.json" $ova 2>&1) -join ' '
    L "import: $($imp.Substring(0, [Math]::Min(200, $imp.Length)))"
    & $g vm.change -vm KOR-UNIFI01 -c 2 -m 4096 -annotation 'UniFi OS Server: KOR self-hosted UniFi controller (APs + switches). NetworkOps. 192.168.1.26. Built 2026-09-29.'
    # cloud-init's VMware datasource: guestinfo works on a standalone host, where vApp/OVF properties do not.
    & $g vm.change -vm KOR-UNIFI01 -e 'guestinfo.metadata=__METADATA__' -e 'guestinfo.metadata.encoding=base64' -e 'guestinfo.userdata=__USERDATA__' -e 'guestinfo.userdata.encoding=base64'
    L 'guestinfo metadata + userdata set'
    & $g vm.disk.change -vm KOR-UNIFI01 -size 64G
    # A direct-to-host import leaves a vCenter-type ("vpx") MAC that the vSwitch (MAC change / forged
    # transmits = reject) blocks outright. A STATIC MAC from VMware's manual range fixes it (14:55 lesson).
    & $g vm.network.change -vm KOR-UNIFI01 -net 'VM Network' '-net.address' '00:50:56:1a:01:26' ethernet-0
    & $g vm.power -on KOR-UNIFI01 | Out-Null
    L 'powered on'
    $info = (& $g vm.info -r KOR-UNIFI01) -join ' | '
    L "info: $($info.Substring(0, [Math]::Min(600, $info.Length)))"
}
catch { L "FAILED: $($_.Exception.Message)" }
finally {
    Remove-Item "$dir\spec.json" -ErrorAction SilentlyContinue
    Remove-Item Env:\GOVC_PASSWORD -ErrorAction SilentlyContinue
}
[pscustomobject]@{ Log = ($log -join ' || ') }
'@
$metaDataB64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'meta-data.yaml')))
$onTarget = $onTarget.Replace('__ROOTPW__', ([Net.NetworkCredential]::new('', (Get-Content (Join-Path $PSScriptRoot 'device-pw.dpapi') | ConvertTo-SecureString)).Password)).Replace('__USERDATA__', $userDataB64).Replace('__METADATA__', $metaDataB64)
$gen = Join-Path $PSScriptRoot 'deploy-vm.ONTARGET.ps1'
try {
    [IO.File]::WriteAllText($gen, $onTarget)
    $exe = Get-ChildItem 'C:\VIsual Studio Projects\Operations\Kor.Operations.NetworkOps.Cli\bin\Debug' -Recurse -Filter netops.exe | Select-Object -First 1 -ExpandProperty FullName
    & $exe run --script $gen --hosts KOR-APP01 --timeout 1800
}
finally {
    if (Test-Path $gen) { [IO.File]::Delete($gen) }
    "generated script deleted: $(-not (Test-Path $gen))"
}
