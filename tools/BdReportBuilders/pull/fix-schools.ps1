$f = 'C:\Users\ilalonde\Desktop\Polish\build-schools-report.ps1'
$content = Get-Content $f -Raw
# Wrap any access $a.b.Substring(0, [Math]::Min(N, $a.b.Length)) → (Safe $a.b N)
$pattern = '(\$[\w.]+)\.Substring\(0,\s*\[Math\]::Min\((\d+),\s*\$[\w.]+\.Length\)\)'
$replaced = [regex]::Replace($content, $pattern, '(Safe $1 $2)')
Set-Content $f -Value $replaced -Encoding UTF8 -NoNewline
$after = (Select-String -Path $f -Pattern '\.Substring\(' -AllMatches | Measure-Object).Count
"Remaining Substring calls: $after"
