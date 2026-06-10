$f = 'C:\Users\ilalonde\Desktop\Polish\build-hospitals-report.ps1'
$content = Get-Content $f -Raw
$pattern = '(\$[\w.]+)\.Substring\(0,\s*\[Math\]::Min\((\d+),\s*\$[\w.]+\.Length\)\)'
$replaced = [regex]::Replace($content, $pattern, '(Safe $1 $2)')
Set-Content $f -Value $replaced -Encoding UTF8 -NoNewline
$after = (Select-String -Path $f -Pattern '\.Substring\(' -AllMatches | Measure-Object).Count
"Remaining Substring calls: $after"
