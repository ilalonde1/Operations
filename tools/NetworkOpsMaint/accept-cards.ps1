# Accept proposed knowledge cards by their prompt run ids (Ian 27 Oct: "yes to all", incl. accepting the cards). pwsh 7.
param([Parameter(Mandatory)][int[]]$RunIds)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System; using System.Net.Http; using System.Security.Cryptography;
public static class KorAcceptPin { public static HttpClient C(string pin) { var h = new HttpClientHandler(); h.ServerCertificateCustomValidationCallback = (m, c, ch, e) => c != null && Convert.ToHexString(SHA256.HashData(c.RawData)).Equals(pin, StringComparison.OrdinalIgnoreCase); return new HttpClient(h) { BaseAddress = new Uri("https://KOR-APP01.int.korstructural.com:8445/"), Timeout = TimeSpan.FromSeconds(120) }; } }
"@
$http = [KorAcceptPin]::C('EBAE9FE81D92623E1EED3C1F9C8F4ED7D5750A8BAE27522CE9F2CE1A3738EA6F')
$http.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', (& 'C:\KOR-Operations\cli\netops.exe' token 2>$null | Select-Object -Last 1).Trim())
foreach ($id in $RunIds) {
  $body = [System.Net.Http.StringContent]::new('{"decision":"accept"}', [Text.Encoding]::UTF8, 'application/json')
  $res = $http.PostAsync("api/prompt-runs/$id/learned", $body).GetAwaiter().GetResult()
  "run ${id}: $([int]$res.StatusCode) $($res.Content.ReadAsStringAsync().GetAwaiter().GetResult())"
}
