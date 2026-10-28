# Live security probe of Griffin's KorConcrete engine, run against a scratch instance on this PC.
# Each line is one test: what was sent, what came back.
param([int]$Port = 8150)
$base = "http://127.0.0.1:$Port"
Add-Type -AssemblyName System.Net.Http
$h = New-Object System.Net.Http.HttpClientHandler
$h.AllowAutoRedirect = $false
$c = New-Object System.Net.Http.HttpClient($h)

function Send($method, $path, $headers = @{}, $body = $null) {
    $m = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($method), "$base$path")
    foreach ($k in $headers.Keys) { if ($k -eq 'Host') { $m.Headers.Host = $headers[$k] } else { [void]$m.Headers.TryAddWithoutValidation($k, $headers[$k]) } }
    if ($body) { $m.Content = New-Object System.Net.Http.StringContent($body, [Text.Encoding]::UTF8, 'application/json') }
    $r = $c.SendAsync($m).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $acao = ($r.Headers | Where-Object Key -eq 'Access-Control-Allow-Origin' | ForEach-Object { $_.Value -join ',' })
    [pscustomobject]@{ Status = [int]$r.StatusCode; ACAO = $acao; Body = $text }
}

"1 listening sockets on port $Port :"
Get-NetTCPConnection -LocalPort $Port -State Listen | ForEach-Object { "   $($_.LocalAddress):$($_.LocalPort)" }

$page = Send GET '/'
$token = [regex]::Match($page.Body, 'name="korconcrete-token"\s+content="([0-9a-f]+)"').Groups[1].Value
"2 GET / with no token: $($page.Status), page carries the token: $([bool]$token) ($($token.Length) hex chars)"

$r = Send POST '/section/etabs/parse' @{} '{"filename":"a.e2k","contentB64":""}'
"3 POST /section/etabs/parse, no token: $($r.Status) $($r.Body)"
$r = Send POST '/section/etabs/parse' @{ 'x-korconcrete-token' = ('0' * 64) } '{"filename":"a.e2k","contentB64":""}'
"4 same, wrong token: $($r.Status) $($r.Body)"
$r = Send GET '/' @{ Host = "evil.example:$Port" }
"5 GET / with Host evil.example (DNS rebinding): $($r.Status) $($r.Body)"
$r = Send GET '/' @{ Host = "localhost:9999" }
"6 GET / with Host localhost on the wrong port: $($r.Status)"
$r = Send OPTIONS '/section/etabs/parse' @{ Origin = 'http://evil.example'; 'Access-Control-Request-Method' = 'POST'; 'Access-Control-Request-Headers' = 'x-korconcrete-token,content-type' }
"7 CORS preflight from http://evil.example: $($r.Status), allow-origin '$($r.ACAO)'"
$r = Send OPTIONS '/section/etabs/parse' @{ Origin = 'http://localhost:5173'; 'Access-Control-Request-Method' = 'POST'; 'Access-Control-Request-Headers' = 'x-korconcrete-token,content-type' }
"8 CORS preflight from http://localhost:5173 (his dev server): $($r.Status), allow-origin '$($r.ACAO)'"
$r = Send GET '/' @{ Origin = 'http://localhost:5173' }
"9 GET / from origin localhost:5173: $($r.Status), allow-origin '$($r.ACAO)' - a page there could read the token: $($r.ACAO -eq 'http://localhost:5173')"
foreach ($p in '/../../../../Windows/win.ini', '/..%2f..%2f..%2f..%2fWindows%2fwin.ini', '/%5c..%5c..%5c..%5cWindows%5cwin.ini', '/assets/..%2f..%2f..%2fbackend%2fversion.json') {
    $r = Send GET $p
    "10 static traversal $p : $($r.Status) $(if ($r.Body -match 'fonts|extensions') {'LEAKED win.ini'} else {'no leak'})"
}
$t = @{ 'x-korconcrete-token' = $token }
$r = Send POST '/section/import-path' $t '{"path":"C:\\Windows\\win.ini"}'
"11 import-path C:\Windows\win.ini, with token: $($r.Status) $($r.Body.Substring(0, [Math]::Min(300, $r.Body.Length)))"
$r = Send POST '/section/import-path' $t '{"path":"\\\\127.0.0.1\\C$\\Windows\\win.ini"}'
"12 import-path over UNC, with token: $($r.Status) $($r.Body.Substring(0, [Math]::Min(200, $r.Body.Length)))"
$r = Send POST '/loads/etabs/upload' $t '{"slot":"pier","filename":"x.xlsx","contentB64":"","designId":"..\\..\\evil"}'
"13 loads upload with design id ..\..\evil: $($r.Status) $($r.Body.Substring(0, [Math]::Min(200, $r.Body.Length)))"
$r = Send GET '/health'
"14 /health with no token: $($r.Status) $($r.Body.Substring(0, [Math]::Min(300, $r.Body.Length)))"
