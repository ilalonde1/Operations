$d = 'C:\Windows\Temp\kor-mgmt'
$exe = Join-Path $d 'ShareTextExtract.exe'
$log = Join-Path $d 'run.log'
"start $(Get-Date -Format s)" | Set-Content $log
# Depth-1 names only of the other shares, to tell Ian where HR records live. No recursion.
$l = foreach ($p in 'E:\Partners','E:\Finance','E:\Library\ADMIN','E:\Home','E:\BD Brain') {
  "== $p"
  Get-ChildItem -LiteralPath $p -Force -ErrorAction SilentlyContinue | ForEach-Object { "{0}`t{1}`t{2:yyyy-MM-dd}" -f $(if ($_.PSIsContainer) {'D'} else {'F'}), $_.Name, $_.LastWriteTime }
}
$l | Set-Content (Join-Path $d 'other-shares-depth1.txt') -Encoding UTF8
& $exe 'E:\Management' (Join-Path $d 'manifest_knowledge.txt') (Join-Path $d 'bundle_knowledge.jsonl') --max-chars 400000 *>> $log
& $exe 'E:\Management' (Join-Path $d 'manifest_proposals.txt') (Join-Path $d 'bundle_proposals.jsonl') --max-chars 60000 *>> $log
"end $(Get-Date -Format s)" | Add-Content $log
