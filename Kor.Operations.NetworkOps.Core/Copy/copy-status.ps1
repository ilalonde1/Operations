# Runs ON the machine a copy was started on (NetworkOps run-command). CopyScript.cs prepends $CopyId.
# Reads what copy-start.ps1's task wrote: the finished result, or that it is still running (and the log's size so far).
$dir = 'C:\ProgramData\KOR\NetworkOps\copies'
$out = Join-Path $dir "$CopyId.json"; $log = Join-Path $dir "$CopyId.log"
if (Test-Path -LiteralPath $out) { return (Get-Content -LiteralPath $out -Raw | ConvertFrom-Json) }
$task = Get-ScheduledTask -TaskName "KOR-NetOps-Copy-$CopyId" -ErrorAction SilentlyContinue
if ($task) {
  $logBytes = if (Test-Path -LiteralPath $log) { (Get-Item -LiteralPath $log).Length } else { 0 }
  return [pscustomobject]@{ Result = "RUNNING: task $($task.State); log $logBytes bytes so far"; State = 'running'; CopyId = $CopyId; Host = $env:COMPUTERNAME; Log = $log }
}
[pscustomobject]@{ Result = "UNKNOWN: no result and no task for copy $CopyId on $env:COMPUTERNAME"; State = 'unknown'; CopyId = $CopyId; Host = $env:COMPUTERNAME }
