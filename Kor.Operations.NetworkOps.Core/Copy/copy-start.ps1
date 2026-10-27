# Runs ON the source-side machine as SYSTEM (NetworkOps run-command). CopyScript.cs prepends the values:
#   $CopyId, $Source, $Destination, $Files, $Overwrite, $WaitSeconds
# The copy itself runs as a ONE-OFF scheduled task (SYSTEM), not as a child of this script: the agent closes its job
# object when this script ends, which would kill a child robocopy. The task writes <CopyId>.json when robocopy ends and
# then removes itself. This script waits up to $WaitSeconds for that file and returns it, or returns State=running;
# networkops_copy_status / netops copy-status reads it later.
# Never deletes: no mirror, purge or move switch (SessionRunAndCopyTests holds it). Without $Overwrite a file already at the destination is left as it is.
$ErrorActionPreference = 'Stop'
$dir = 'C:\ProgramData\KOR\NetworkOps\copies'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$ps1 = Join-Path $dir "$CopyId.ps1"; $log = Join-Path $dir "$CopyId.log"; $out = Join-Path $dir "$CopyId.json"
$task = "KOR-NetOps-Copy-$CopyId"
if (-not (Test-Path -LiteralPath $Source)) { return [pscustomobject]@{ Result = "REFUSED: the source $Source is not reachable from $env:COMPUTERNAME"; State = 'refused'; CopyId = $CopyId } }

function Lit([string]$s) { "'" + $s.Replace("'", "''") + "'" }
$worker = @"
`$ErrorActionPreference = 'Continue'
`$src = $(Lit $Source); `$dst = $(Lit $Destination); `$files = $(Lit $Files); `$log = $(Lit $log); `$out = $(Lit $out)
`$started = Get-Date
`$opts = @('/E','/COPY:DAT','/DCOPY:T','/R:1','/W:1','/MT:16','/NP','/NFL','/NDL','/BYTES')
if (-not `$$Overwrite) { `$opts += @('/XC','/XN','/XO') }
& robocopy.exe `$src `$dst `$files @opts "/LOG:`$log" | Out-Null
`$exit = `$LASTEXITCODE
`$ended = Get-Date
function Sum(`$root) { `$f = @(Get-ChildItem -LiteralPath `$root -Recurse -File -Filter `$files -Force -ErrorAction SilentlyContinue); [pscustomobject]@{ Files = `$f.Count; Bytes = [long](`$f | Measure-Object Length -Sum).Sum } }
`$text = Get-Content -LiteralPath `$log -ErrorAction SilentlyContinue
function Row(`$name) { `$m = `$text | Select-String -Pattern ('^\s*' + `$name + '\s*:\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)') | Select-Object -Last 1
  if (`$m) { `$g = `$m.Matches[0].Groups; [pscustomobject]@{ Total = [long]`$g[1].Value; Copied = [long]`$g[2].Value; Skipped = [long]`$g[3].Value; Mismatch = [long]`$g[4].Value; Failed = [long]`$g[5].Value; Extras = [long]`$g[6].Value } } }
`$s = Sum `$src; `$d = Sum `$dst
# A sample proof: up to 10 files that exist on both sides, hashed on both.
`$checked = 0; `$bad = @()
if (`$s.Files -gt 0) {
  Get-ChildItem -LiteralPath `$src -Recurse -File -Filter `$files -Force -ErrorAction SilentlyContinue | Get-Random -Count 10 | ForEach-Object {
    `$rel = `$_.FullName.Substring((Resolve-Path -LiteralPath `$src).ProviderPath.TrimEnd('\').Length).TrimStart('\')
    `$other = Join-Path `$dst `$rel
    if (Test-Path -LiteralPath `$other) { `$checked++
      if ((Get-FileHash -LiteralPath `$_.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath `$other -Algorithm SHA256).Hash) { `$bad += `$rel } } }
}
`$meaning = if (`$exit -ge 8) { 'FAILED: some files could not be copied (see the log)' } elseif (`$exit -band 1) { 'copied' } elseif (`$exit -eq 0) { 'nothing to copy: the destination already had every file' } else { 'done, with extra or mismatched files at the destination (see the log)' }
`$ok = (`$exit -lt 8) -and (`$bad.Count -eq 0)
[pscustomobject]@{
  Result = "`$(if (`$ok) { 'OK' } else { 'FAILED' }): `$meaning; robocopy exit `$exit; source `$(`$s.Files) files / `$(`$s.Bytes) bytes, destination `$(`$d.Files) files / `$(`$d.Bytes) bytes; hash sample `$(`$checked - `$bad.Count) of `$checked match"
  State = 'done'; Ok = `$ok; CopyId = $(Lit $CopyId); Host = `$env:COMPUTERNAME
  Source = `$src; Destination = `$dst; Files = `$files; Overwrite = `$$Overwrite
  RobocopyExit = `$exit; Meaning = `$meaning
  StartedUtc = `$started.ToUniversalTime().ToString('o'); EndedUtc = `$ended.ToUniversalTime().ToString('o'); Seconds = [int](`$ended - `$started).TotalSeconds
  SourceCount = `$s; DestinationCount = `$d; FilesRow = (Row 'Files'); BytesRow = (Row 'Bytes')
  HashChecked = `$checked; HashMismatches = `$bad; Log = `$log
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath `$out -Encoding UTF8
Unregister-ScheduledTask -TaskName $(Lit $task) -Confirm:`$false -ErrorAction SilentlyContinue
"@
Set-Content -LiteralPath $ps1 -Value $worker -Encoding UTF8

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$ps1`""
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $task -Action $action -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName $task

$deadline = (Get-Date).AddSeconds($WaitSeconds)
while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $out)) { Start-Sleep -Seconds 2 }
if (Test-Path -LiteralPath $out) { return (Get-Content -LiteralPath $out -Raw | ConvertFrom-Json) }
[pscustomobject]@{ Result = "RUNNING: the copy is still going after $WaitSeconds s; read it back with copy-status, id $CopyId"; State = 'running'; CopyId = $CopyId; Host = $env:COMPUTERNAME; Log = $log }
