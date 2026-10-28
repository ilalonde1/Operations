# Bank knowledge cards in NetworkOps' own table through its own path: render an "ask" prompt (a recorded run with a
# one-time token), then post that run's outcome carrying the card. Cards arrive as 'proposed' for Ian to accept in the
# Prompt Library. pwsh 7. No Anthropic API anywhere: the prompt is rendered on APP01 and never sent to a model.
param([Parameter(Mandatory)][string]$CardsFile, [switch]$DryRun)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System; using System.Net.Http; using System.Security.Cryptography;
public static class KorCardPin { public static HttpClient C(string pin) { var h = new HttpClientHandler(); h.ServerCertificateCustomValidationCallback = (m, c, ch, e) => c != null && Convert.ToHexString(SHA256.HashData(c.RawData)).Equals(pin, StringComparison.OrdinalIgnoreCase); return new HttpClient(h) { BaseAddress = new Uri("https://KOR-APP01.int.korstructural.com:8445/"), Timeout = TimeSpan.FromSeconds(120) }; } }
"@
$http = [KorCardPin]::C('EBAE9FE81D92623E1EED3C1F9C8F4ED7D5750A8BAE27522CE9F2CE1A3738EA6F')
$http.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', (& 'C:\KOR-Operations\cli\netops.exe' token 2>$null | Select-Object -Last 1).Trim())
function Post($path, $obj, $headers) {
  $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $path)
  if ($headers) { foreach ($k in $headers.Keys) { $req.Headers.Add($k, $headers[$k]) } }
  $req.Content = [System.Net.Http.StringContent]::new(($obj | ConvertTo-Json -Depth 6 -Compress), [Text.Encoding]::UTF8, 'application/json')
  $res = $http.SendAsync($req).GetAwaiter().GetResult()
  [pscustomobject]@{ Status = [int]$res.StatusCode; Body = $res.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
}
$cards = Get-Content -LiteralPath $CardsFile -Raw | ConvertFrom-Json
$i = 0
foreach ($c in $cards) {
  $i++
  if ($DryRun) { "$i. $($c.title) | appliesTo=$($c.appliesTo)"; continue }
  $r = Post 'api/prompts/render' @{ kind = 'ask'; question = "Bank a known solution (27 Oct, operations-e6): $($c.title)" } $null
  if ($r.Status -ne 200) { "$i. RENDER FAILED $($r.Status): $($r.Body)"; continue }
  $p = $r.Body | ConvertFrom-Json
  $tok = [regex]::Match($p.markdown, "'X-Prompt-Token' = '([^']+)'").Groups[1].Value
  if (-not $p.runId -or -not $tok) { "$i. NO RUN/TOKEN in the rendered prompt"; continue }
  $card = [ordered]@{ title = $c.title; appliesTo = $c.appliesTo; symptom = $c.symptom; cause = $c.cause; check = $c.check; fix = $c.fix; tags = $c.tags; plain = $c.plain }
  $o = Post "api/prompt-runs/$($p.runId)/outcome" @{ outcome = 'solved'; summary = "Known solution banked by operations-e6 on 27 Oct 2026 at Ian's request (recorded in NetworkOps' own DB): $($c.title)"; learned = $null; card = $card } @{ 'X-Prompt-Token' = $tok }
  "$i. run $($p.runId): $($o.Status) $($o.Body)"
}
