# Builds two netops-run scripts for KOR-FS01 from an already-made remote copy of Expand-WrappedExecutables (its
# parameters fixed in, exits turned into returns):
#   fs01-unwrap-launch.ps1 : writes that copy to E:\IT\netops-unwrap\<stamp>\unwrap.ps1 and starts it as a one-off
#                            SYSTEM scheduled task (the agent's job object would kill a child); returns at once.
#   fs01-unwrap-status.ps1 : reads E:\IT\netops-unwrap\<stamp>\result.txt (+ done.txt) back.
param([Parameter(Mandatory)][string]$RemoteCopy, [Parameter(Mandatory)][string]$OutDir, [Parameter(Mandatory)][string]$Stamp)
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText($RemoteCopy)))
$dir = "E:\IT\netops-unwrap\$Stamp"
$launch = @"
# ON KOR-FS01: start the Ian-approved unwrap of nested installer executables (28 Oct, via the Virtual Drafter) as a
# one-off SYSTEM task, so it is not cut at the 90 s netops limit. Writes only under $dir and the unwrapped .exe files.
`$dir = '$dir'
if (Test-Path -LiteralPath (Join-Path `$dir 'unwrap.ps1')) { "ALREADY STARTED: `$dir"; return }
[void][IO.Directory]::CreateDirectory(`$dir)
[IO.File]::WriteAllText((Join-Path `$dir 'unwrap.ps1'), [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$b64')))
# a runner FILE, so no quoting has to survive the task's command line
[IO.File]::WriteAllText((Join-Path `$dir 'runner.ps1'), "& '`$dir\unwrap.ps1' *> '`$dir\result.txt'`r`nGet-Date -Format o | Set-Content -LiteralPath '`$dir\done.txt'`r`n")
`$name = 'KOR-NetOps-Unwrap-$Stamp'
`$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File ```"`$dir\runner.ps1```""
Register-ScheduledTask -TaskName `$name -Action `$action -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
Start-ScheduledTask -TaskName `$name
Start-Sleep -Seconds 3
"started `$name at `$(Get-Date -Format HH:mm:ss); state `$((Get-ScheduledTask -TaskName `$name).State); writing `$dir\result.txt"
"@
$status = @"
# ON KOR-FS01, READ-ONLY: the unwrap task's state and its result file.
`$dir = '$dir'; `$name = 'KOR-NetOps-Unwrap-$Stamp'
`$t = Get-ScheduledTask -TaskName `$name -ErrorAction SilentlyContinue
`$done = Join-Path `$dir 'done.txt'; `$res = Join-Path `$dir 'result.txt'
# the task holds result.txt open while it writes: read it with sharing, never ReadAllLines
`$lines = if (Test-Path -LiteralPath `$res) { `$fs = [IO.File]::Open(`$res, 'Open', 'Read', 'ReadWrite'); `$sr = New-Object IO.StreamReader(`$fs); try { [string[]](`$sr.ReadToEnd() -split "\r?\n") } finally { `$sr.Dispose() } } else { `$null }
`$info = if (`$t) { Get-ScheduledTaskInfo -TaskName `$name } else { `$null }
if (`$t -and (Test-Path -LiteralPath `$done)) { Unregister-ScheduledTask -TaskName `$name -Confirm:`$false }   # finished: leave no task behind
[pscustomobject]@{
  Task = if (`$t) { "`$(`$t.State) last run `$(`$info.LastRunTime) result 0x{0:X}" -f `$info.LastTaskResult } else { 'gone' }
  Unregistered = [bool](`$t -and (Test-Path -LiteralPath `$done))
  Done = if (Test-Path -LiteralPath `$done) { [string]([IO.File]::ReadAllText(`$done)).Trim() } else { `$null }
  ResultLines = if (`$lines) { [int]`$lines.Count } else { 0 }
  Restored = if (`$lines) { [int]@(`$lines | Where-Object { `$_ -like 'restored: *' }).Count } else { 0 }
  Failures = if (`$lines) { [string[]]@(`$lines | Where-Object { `$_ -cmatch 'FAILED|DIFFERENT|UNREADABLE' } | Select-Object -First 40) } else { [string[]]@() }
  Tail = if (`$lines) { [string[]]@(`$lines | Where-Object { `$_ } | Select-Object -Last 4) } else { [string[]]@() }
}
"@
[IO.File]::WriteAllText((Join-Path $OutDir 'fs01-unwrap-launch.ps1'), $launch)
[IO.File]::WriteAllText((Join-Path $OutDir 'fs01-unwrap-status.ps1'), $status)
"launch $((Get-Item (Join-Path $OutDir 'fs01-unwrap-launch.ps1')).Length) B, status $((Get-Item (Join-Path $OutDir 'fs01-unwrap-status.ps1')).Length) B; dir $dir"
