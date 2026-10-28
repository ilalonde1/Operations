# Restores the FiveStickFilesTests mirror from the exact sources SixSetsBuildAsBankedTests names (lines 48-53).
$dst = Join-Path $env:LOCALAPPDATA 'Temp\kor-drawings\stickfiles'
New-Item -ItemType Directory -Force $dst | Out-Null
$src = [ordered]@{
  '31130-01' = '\\Kor-fs01\Projects\Projects\03 Residential\31130-01 (Uplands Area 6 Lot 12 West Vancouver)\05 Stickfile\06 Old Structural Stickfiles\31130-01 UPLANDS LOT 12 2026-05-20 Stick file.pdf'
  '31138-01' = '\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\05 Stickfile\31138-01 2026-09-01_2170 W 1st Ave_Str Set.pdf'
  '31065-01' = '\\Kor-fs01\Projects\Projects\03 Residential\31065-01 (5350 5430 Heather Street Vancouver)\05 Stickfile\31065-01 - 2026-07-08 - 5380-Heather Street - Stickfile (up to SSI-04)-OAP.pdf'
  '31202-01' = '\\Kor-fs01\Projects\Projects\03 Residential\31202-01 (1650 N Hotel Circle San Diego)\05 Stickfile\06 Old Structural Stickfiles\31202-01 2026-09-04 Hotel Circle North StickSet  - FULL SET.pdf'
  '31168-01' = '\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\05 Stickfile\06 Old Structural Stickfiles\31168-01 - 2026-04-21- YMCA Langara - Stickfile.pdf'
}
foreach ($job in $src.Keys) {
  $from = $src[$job]; $to = Join-Path $dst "$job.pdf"
  if (-not (Test-Path -LiteralPath $from)) { "MISSING ON SHARE: $job  $from"; continue }
  $t = Measure-Command { Copy-Item -LiteralPath $from -Destination $to -Force }
  $a = (Get-FileHash -LiteralPath $from).Hash; $b = (Get-FileHash -LiteralPath $to).Hash
  "{0}  {1:N0} bytes  {2:N1}s  hash {3}" -f $job, (Get-Item -LiteralPath $to).Length, $t.TotalSeconds, $(if ($a -eq $b) { 'MATCH' } else { 'DIFF' })
}
