# ON DC01: A kor-mesh01 -> 192.168.1.27 (static) and CNAME mesh -> kor-mesh01. Idempotent; reads back.
$zone = 'int.korstructural.com'
if (-not (Get-DnsServerResourceRecord -ZoneName $zone -Name 'kor-mesh01' -RRType A -ErrorAction SilentlyContinue)) {
    Add-DnsServerResourceRecordA -ZoneName $zone -Name 'kor-mesh01' -IPv4Address '192.168.1.27'
}
if (-not (Get-DnsServerResourceRecord -ZoneName $zone -Name 'mesh' -RRType CName -ErrorAction SilentlyContinue)) {
    Add-DnsServerResourceRecordCName -ZoneName $zone -Name 'mesh' -HostNameAlias 'kor-mesh01.int.korstructural.com'
}
$a = Get-DnsServerResourceRecord -ZoneName $zone -Name 'kor-mesh01' -RRType A
$c = Get-DnsServerResourceRecord -ZoneName $zone -Name 'mesh' -RRType CName
[pscustomobject]@{
    A     = "$($a.HostName) -> $($a.RecordData.IPv4Address) $(if ($a.Timestamp) { 'dynamic' } else { 'static' })"
    CNAME = "$($c.HostName) -> $($c.RecordData.HostNameAlias)"
}
