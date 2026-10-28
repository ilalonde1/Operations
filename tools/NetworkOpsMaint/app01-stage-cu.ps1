# ON KOR-APP01 as SYSTEM: STAGE (download only, never run) SQL Server 2022 CU27 + October 2026 security fix, KB5126934,
# build 16.0.4305.5, from Microsoft's Update Catalog into D:\Installers\SQL2022-CU27-KB5126934\. Then verify: size against
# the catalog (491,045,896 bytes), SHA-256 against Microsoft's KB page, Authenticode signature. Runs as a one-off task so
# a slow download outlives the NetworkOps run; result in D:\Installers\SQL2022-CU27-KB5126934\stage.json.
# Ian, 15:1x via operations-ee: "You can download and stage the installers and I can execute".
$dir = 'D:\Installers\SQL2022-CU27-KB5126934'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$worker = @'
$ErrorActionPreference = 'Stop'
$dir = 'D:\Installers\SQL2022-CU27-KB5126934'; $out = Join-Path $dir 'stage.json'
$want = '8F1A7F0AC5F47FF98F5825687C8CCD897563AA835915F2271F908273E962E7E2'; $wantBytes = 491045896
$res = [ordered]@{ Started = (Get-Date).ToString('o') }
try {
  [Net.ServicePointManager]::SecurityProtocol = 'Tls12'
  $search = Invoke-WebRequest -UseBasicParsing 'https://www.catalog.update.microsoft.com/Search.aspx?q=KB5126934'
  # The page carries the result row's update id once (27 Oct: no "_link" attribute in what APP01 receives).
  $ids = @([regex]::Matches([string]$search.Content, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}') | ForEach-Object { $_.Value } | Select-Object -Unique)
  $res.CatalogIds = $ids
  if ($ids.Count -ne 1) { throw "expected one catalog entry for KB5126934, found $($ids.Count)" }
  $body = 'updateIDs=' + [uri]::EscapeDataString("[{`"size`":0,`"languages`":`"`",`"uidInfo`":`"$($ids[0])`",`"updateID`":`"$($ids[0])`"}]")
  $dlg = Invoke-WebRequest -UseBasicParsing -Method Post -Uri 'https://www.catalog.update.microsoft.com/DownloadDialog.aspx' -Body $body -ContentType 'application/x-www-form-urlencoded'
  $urls = @([regex]::Matches($dlg.Content, "url\s*=\s*'(https?://[^']+\.exe)'") | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
  $res.Urls = $urls
  # The catalog serves it lower-case with a SHA-1 suffix: sqlserver2022-kb5126934-x64_<sha1>.exe (27 Oct).
  $url = @($urls | Where-Object { $_ -match '(?i)/sqlserver2022-kb5126934-x64(_[0-9a-f]{40})?\.exe$' })[0]
  if (-not $url) { throw 'the catalog gave no SQLServer2022-KB5126934-x64.exe link' }
  if (([uri]$url).Host -notmatch '(^|\.)(windowsupdate\.com|microsoft\.com)$') { throw "refused: link not on a Microsoft host: $url" }
  $file = Join-Path $dir 'SQLServer2022-KB5126934-x64.exe'
  (New-Object Net.WebClient).DownloadFile($url, $file)
  $len = (Get-Item -LiteralPath $file).Length; $sha = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
  $sig = Get-AuthenticodeSignature -LiteralPath $file
  $res.File = $file; $res.Url = $url; $res.Bytes = $len; $res.SizeMatchesCatalog = ($len -eq $wantBytes)
  $res.Sha256 = $sha; $res.HashMatchesMicrosoft = ($sha -eq $want)
  $res.Signature = [string]$sig.Status; $res.Signer = [string]$sig.SignerCertificate.Subject; $res.SignerIssuer = [string]$sig.SignerCertificate.Issuer
  $res.Ok = ($res.SizeMatchesCatalog -and $res.HashMatchesMicrosoft -and $sig.Status -eq 'Valid' -and $res.Signer -match 'O=Microsoft Corporation')
} catch { $res.Error = $_.Exception.Message; $res.Ok = $false }
$res.Ended = (Get-Date).ToString('o')
$res | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $out -Encoding UTF8
Unregister-ScheduledTask -TaskName 'KOR-NetOps-StageCU' -Confirm:$false -ErrorAction SilentlyContinue
'@
$ps1 = Join-Path $dir 'stage.ps1'; Set-Content -LiteralPath $ps1 -Value $worker -Encoding UTF8
Remove-Item -LiteralPath (Join-Path $dir 'stage.json') -ErrorAction SilentlyContinue
$a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$ps1`""
$p = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName 'KOR-NetOps-StageCU' -Action $a -Principal $p -Force | Out-Null
Start-ScheduledTask -TaskName 'KOR-NetOps-StageCU'
[pscustomobject]@{ Result = 'staging task started; read D:\Installers\SQL2022-CU27-KB5126934\stage.json in a few minutes'; D = "$([math]::Round((Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='D:'").FreeSpace/1GB,1)) GB free on D:" }
