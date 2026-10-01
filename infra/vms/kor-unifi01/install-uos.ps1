# Waits for KOR-UNIFI01's first boot, then installs UniFi OS Server 5.1.42 (SHA-256 checked) non-interactively.
$k = "$env:USERPROFILE\.ssh\kor-unifi01"
$ssh = @('-i', $k, '-o', 'StrictHostKeyChecking=accept-new', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10', '-o', 'ServerAliveInterval=30')
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalMinutes -lt 8) {
    $t = New-Object Net.Sockets.TcpClient
    $up = $t.ConnectAsync('192.168.1.26', 22).Wait(3000) -and $t.Connected; $t.Close()
    if ($up) { break }
    Start-Sleep -Seconds 5
}
"ssh up after $([int]$sw.Elapsed.TotalSeconds)s"
$remote = @'
set -e
timeout 900 cloud-init status --wait >/dev/null || true
cloud-init status --long | grep -E '^status|^errors'
echo "host=$(hostname -f) ip=$(ip -4 -br addr show ens192 | awk '{print $3}')"
mkdir -p ~/unifi && cd ~/unifi
f=unifi-os-server-5.1.42-linux-x64
[ -f $f ] || curl -fsSL -o $f "https://fw-download.ubnt.com/data/unifi-os-server/5172-linux-x64-5.1.42-12e9e3cf-8f8b-4e54-928c-76b80a10c8a4.42-x64"
echo "f6111e9396a42c74016f5dde9b01fbef486a6fe69efe137935e6e38b7c22f94d  $f" | sha256sum -c -
chmod +x $f
sudo ./$f --non-interactive > install.log 2>&1 && echo install=ok || { echo install=FAILED; tail -20 install.log; exit 1; }
grep -E "INSTALLATION COMPLETE|running at" install.log
systemctl is-active uosserver
curl -sk -o /dev/null -w "web=%{http_code}\n" https://127.0.0.1:11443/
'@
$remote -replace "`r", '' | ssh @ssh koradmin@192.168.1.26 'bash -s' 2>&1
