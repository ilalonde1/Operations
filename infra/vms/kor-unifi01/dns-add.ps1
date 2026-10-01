# ON DC01: A kor-unifi01 -> 192.168.1.26 (static) and CNAME unifi01 -> kor-unifi01. Idempotent; reads back.
$zone = 'int.korstructural.com'
if (-not (Get-DnsServerResourceRecord -ZoneName $zone -Name 'kor-unifi01' -RRType A -ErrorAction SilentlyContinue)) {
    Add-DnsServerResourceRecordA -ZoneName $zone -Name 'kor-unifi01' -IPv4Address '192.168.1.26'
}
if (-not (Get-DnsServerResourceRecord -ZoneName $zone -Name 'unifi01' -RRType CName -ErrorAction SilentlyContinue)) {
    Add-DnsServerResourceRecordCName -ZoneName $zone -Name 'unifi01' -HostNameAlias 'kor-unifi01.int.korstructural.com'
}
$a = Get-DnsServerResourceRecord -ZoneName $zone -Name 'kor-unifi01' -RRType A
$c = Get-DnsServerResourceRecord -ZoneName $zone -Name 'unifi01' -RRType CName
[pscustomobject]@{
    A     = "$($a.HostName) -> $($a.RecordData.IPv4Address) $(if ($a.Timestamp) { 'dynamic' } else { 'static' })"
    CNAME = "$($c.HostName) -> $($c.RecordData.HostNameAlias)"
}
