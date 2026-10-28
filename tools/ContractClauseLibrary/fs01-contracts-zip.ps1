# Runs ON KOR-FS01. Zips the files listed in C:\Windows\Temp\kor-contracts-pull.txt (one full path per
# line, as enumerated) into C:\Windows\Temp\kor-contracts.zip, keeping the path under E:\ as the entry
# name so job folders survive. One zip crosses the wire afterwards; nothing else.
$ErrorActionPreference = 'Continue'
$list = 'C:\Windows\Temp\kor-contracts-pull.txt'
$zip  = 'C:\Windows\Temp\kor-contracts.zip'
$log  = 'C:\Windows\Temp\kor-contracts-zip.log'
"start $(Get-Date -Format s)" | Out-File $log
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fs = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
$za = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
$n = 0; $bytes = 0; $missing = 0
foreach ($line in Get-Content $list) {
  $p = $line.Trim()
  if (-not $p) { continue }
  if (-not (Test-Path -LiteralPath $p)) { $missing++; "missing $p" | Out-File $log -Append; continue }
  $entry = $p -replace '^[A-Za-z]:\\', '' -replace '\\', '/'
  try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $p, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    $n++; $bytes += (Get-Item -LiteralPath $p).Length
  } catch { "err $p : $($_.Exception.Message)" | Out-File $log -Append }
}
$za.Dispose(); $fs.Dispose()
"added $n files, $bytes bytes, missing $missing" | Out-File $log -Append
"zip $((Get-Item $zip).Length) bytes" | Out-File $log -Append
"done $(Get-Date -Format s)" | Out-File $log -Append
