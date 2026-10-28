# Runs ON KOR-FS01. Second pass for the paths the first zip could not find. Their names carry a
# character the enumeration wrote as U+FFFD (an en dash in a Newforma-exported name, most likely), so
# an exact path never matches. This pass turns every non-ASCII character into a wildcard, resolves the
# path with Get-ChildItem -Path (wildcards are allowed in folder segments), and zips the first match.
# Output: kor-contracts-2.zip; the log records what each line resolved to, or that it did not.
$ErrorActionPreference = 'Continue'
$list = 'C:\Windows\Temp\kor-contracts-pull-2.txt'
$zip  = 'C:\Windows\Temp\kor-contracts-2.zip'
$log  = 'C:\Windows\Temp\kor-contracts-zip-2.log'
"start $(Get-Date -Format s)" | Out-File $log
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fs = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
$za = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
$n = 0; $bytes = 0; $missing = 0; $multi = 0
foreach ($line in Get-Content -LiteralPath $list -Encoding UTF8) {
  $p = $line.Trim()
  if (-not $p) { continue }
  $target = $null
  if (Test-Path -LiteralPath $p) { $target = $p }
  else {
    $pattern = [regex]::Replace($p, '[^\x00-\x7F]', '*')
    $hits = @(Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue)
    if ($hits.Count -ge 1) { $target = $hits[0].FullName; if ($hits.Count -gt 1) { $multi++ } }
  }
  if (-not $target) { $missing++; "missing $p" | Out-File $log -Append -Encoding UTF8; continue }
  $entry = $target -replace '^[A-Za-z]:\\', '' -replace '\\', '/'
  try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $target, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    $n++; $bytes += (Get-Item -LiteralPath $target).Length
    "ok $target" | Out-File $log -Append -Encoding UTF8
  } catch { "err $target : $($_.Exception.Message)" | Out-File $log -Append -Encoding UTF8 }
}
$za.Dispose(); $fs.Dispose()
"added $n files, $bytes bytes, missing $missing, multi $multi" | Out-File $log -Append
"zip $((Get-Item $zip).Length) bytes" | Out-File $log -Append
"done $(Get-Date -Format s)" | Out-File $log -Append
