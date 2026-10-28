# Runs ON KOR-FS01. Lists candidate client-contract files under the Projects tree and the
# Management share, one CSV, nothing crosses the wire but this file.
$ErrorActionPreference = 'Continue'
$out = 'C:\Windows\Temp\kor-contracts-enum.csv'
$log = 'C:\Windows\Temp\kor-contracts-enum.log'
"start $(Get-Date -Format s)" | Out-File $log
net share | Out-File $log -Append
$roots = @('E:\Projects\Projects', 'E:\Management\Project Opportunities')
$rx = 'agreement|contract|terms|psa|csa|proposal|engagement|letter of intent|loi|purchase order|\bpo\b|markup|redline|executed|signed'
$rows = foreach ($r in $roots) {
  if (-not (Test-Path $r)) { "missing root $r" | Out-File $log -Append; continue }
  Get-ChildItem -LiteralPath $r -Recurse -File -Include *.pdf,*.docx,*.doc,*.msg -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match $rx -or $_.DirectoryName -match 'Proposals? (&|and) Contracts|Contracts?|Agreements?' } |
    Select-Object FullName, Length, LastWriteTime, CreationTime
}
$rows | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding UTF8
"rows $($rows.Count)" | Out-File $log -Append
"done $(Get-Date -Format s)" | Out-File $log -Append
