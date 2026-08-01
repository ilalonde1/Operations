# Extracts SQL command-text strings + surrounding context from the codebase
# so they can be curated into the AI's vocabulary file.
# Output: candidates.md in this folder.
param(
  [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path,
  [string]$Out      = "$PSScriptRoot\candidates.md"
)
$ErrorActionPreference = 'Stop'

$targets = @(
  'Kor.Operations.App\Financials',
  'Kor.Operations.App\Crm',
  'Kor.Operations.App\Compensation',
  'Kor.Operations.App\PMTools',
  'Kor.Operations.App\Services',
  'Kor.Operations.Business',
  'Kor.Operations.Data'
) | ForEach-Object { Join-Path $RepoRoot $_ } | Where-Object { Test-Path $_ }

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# Candidate SQL queries from the existing app")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("Auto-extracted by Vocabulary/extract_candidates.ps1.")
[void]$sb.AppendLine("Each entry shows the file, line, the C# method/comment context just above it, and the SQL string itself.")
[void]$sb.AppendLine("Mark items KEEP / DROP / REWRITE as you review; the keepers become the AI's vocabulary.")
[void]$sb.AppendLine("")

$files = $targets | ForEach-Object { Get-ChildItem -Path $_ -Recurse -Filter *.cs -ErrorAction SilentlyContinue } | Sort-Object FullName -Unique
foreach ($f in $files) {
  $lines = Get-Content -LiteralPath $f.FullName
  $hits = @()
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'CommandText\s*=') { $hits += $i }
  }
  if ($hits.Count -eq 0) { continue }

  $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\','/')
  [void]$sb.AppendLine("## $rel")
  [void]$sb.AppendLine("")
  foreach ($lineIdx in $hits) {
    $start = [Math]::Max(0, $lineIdx - 6)
    $end   = [Math]::Min($lines.Count - 1, $lineIdx + 30)
    [void]$sb.AppendLine("### Line $($lineIdx + 1)")
    [void]$sb.AppendLine('```csharp')
    for ($j = $start; $j -le $end; $j++) {
      [void]$sb.AppendLine($lines[$j])
    }
    [void]$sb.AppendLine('```')
    [void]$sb.AppendLine("")
  }
}

[System.IO.File]::WriteAllText($Out, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $Out"
Write-Host "Files scanned: $($files.Count)"
$totalHits = ($files | ForEach-Object { (Get-Content -LiteralPath $_.FullName | Select-String -Pattern 'CommandText\s*=').Count } | Measure-Object -Sum).Sum
Write-Host "Total CommandText sites: $totalHits"
