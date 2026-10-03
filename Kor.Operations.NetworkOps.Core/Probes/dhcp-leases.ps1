# KOR NetworkOps -- read-only: every DHCP lease on this server (KOR-DC01), for the port map's names of devices NetworkOps
# has no agent on (printers, phones, VMs). Runs ON the server through the one-shot SCM channel. PLAIN values only.
# 2026-10-02: 107 leases in scope 192.168.1.0, 101 with a host name.
$ErrorActionPreference = 'Stop'
@(foreach ($s in Get-DhcpServerv4Scope) {
    foreach ($l in Get-DhcpServerv4Lease -ScopeId $s.ScopeId) {
        [pscustomobject]@{
            Ip       = "$($l.IPAddress.IPAddressToString)"
            Mac      = "$($l.ClientId)"
            HostName = "$($l.HostName)"
            State    = "$($l.AddressState)"
            Expires  = if ($l.LeaseExpiryTime) { $l.LeaseExpiryTime.ToUniversalTime().ToString('o') } else { $null }
        }
    }
})
