# Calls one tool on the NetworkOps MCP endpoint (APP01 /mcp) as the signed-in person, cert pinned. pwsh 7.
# Usage: pwsh -File mcp-call.ps1 -Tool networkops_run -ArgsJson '{...}'   (or -List to list the tools)
param([string]$Tool, [string]$ArgsJson = '{}', [switch]$List)
$ErrorActionPreference = 'Stop'
$pin = 'EBAE9FE81D92623E1EED3C1F9C8F4ED7D5750A8BAE27522CE9F2CE1A3738EA6F'
Add-Type -TypeDefinition @"
using System; using System.Net.Http; using System.Security.Cryptography;
public static class KorMcpPin {
  public static HttpClient Client(string pin) {
    var h = new HttpClientHandler();
    h.ServerCertificateCustomValidationCallback = (m, c, ch, e) => c != null && Convert.ToHexString(SHA256.HashData(c.RawData)).Equals(pin, StringComparison.OrdinalIgnoreCase);
    return new HttpClient(h) { Timeout = TimeSpan.FromSeconds(330) };
  }
}
"@
$token = (& 'C:\KOR-Operations\cli\netops.exe' token 2>$null | Select-Object -Last 1).Trim()
$http = [KorMcpPin]::Client($pin)
$url = 'https://KOR-APP01.int.korstructural.com:8445/mcp'
$script:sid = $null
function Send([string]$body) {
  $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $url)
  $req.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
  $req.Headers.Accept.ParseAdd('application/json'); $req.Headers.Accept.ParseAdd('text/event-stream')
  if ($script:sid) { $req.Headers.Add('Mcp-Session-Id', $script:sid) }
  $req.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
  $res = $http.SendAsync($req).GetAwaiter().GetResult()
  if (-not $script:sid -and $res.Headers.Contains('Mcp-Session-Id')) { $script:sid = @($res.Headers.GetValues('Mcp-Session-Id'))[0] }
  $text = $res.Content.ReadAsStringAsync().GetAwaiter().GetResult()
  # Streamable HTTP may answer as SSE: keep the data lines.
  $data = ($text -split "`n" | Where-Object { $_ -like 'data:*' } | ForEach-Object { $_.Substring(5).Trim() }) -join ''
  if ($data) { $data } else { "HTTP $([int]$res.StatusCode): $text" }
}
Send '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"operations-e6-proof","version":"1"}}}' | Out-Null
Send '{"jsonrpc":"2.0","method":"notifications/initialized"}' | Out-Null
if ($List) { (Send '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' | ConvertFrom-Json).result.tools | ForEach-Object { $_.name }; return }
$call = @{ jsonrpc = '2.0'; id = 3; method = 'tools/call'; params = @{ name = $Tool; arguments = ($ArgsJson | ConvertFrom-Json -AsHashtable) } } | ConvertTo-Json -Depth 10 -Compress
$out = Send $call | ConvertFrom-Json
if ($out.error) { "ERROR: $($out.error | ConvertTo-Json -Compress)" } else { $out.result.content | ForEach-Object { $_.text } }
