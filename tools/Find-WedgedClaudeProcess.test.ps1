# Known-bad test for Find-WedgedClaudeProcess.ps1 (board row 56). Run FROM a Claude Code session (the plants must be
# descendants of claude.exe): pwsh -NoProfile -File tools/Find-WedgedClaudeProcess.test.ps1
# Plants (1) a python blocked forever reading a pipe nobody writes and (2) a python holding a TCP connection to
# KOR-APP01:1433; both must be LISTED; then stops ONLY those two (its own children) and they must be GONE from the list.
$ErrorActionPreference = 'Stop'
$lister = Join-Path $PSScriptRoot 'Find-WedgedClaudeProcess.ps1'
$tag = "WEDGE-TEST-$PID"
# (1) is a pipe held open by a parent that never writes:
$psi = [Diagnostics.ProcessStartInfo]::new('python', "-I -c `"import sys; sys.stdin.read()  # $tag-pipe`"")
$psi.RedirectStandardInput = $true; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$p1 = [Diagnostics.Process]::Start($psi)          # we hold its stdin open and never write: a wedge
$p2 = Start-Process python -ArgumentList '-I', '-c', "`"import socket,time; s=socket.create_connection(('KOR-APP01',1433),timeout=10); time.sleep(600)  # $tag-tcp`"" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 3
$fail = 0
$out = & pwsh -NoProfile -File $lister -MinMinutes 0 -SampleSeconds 5 | Out-String
foreach ($want in "$tag-pipe", "$tag-tcp") { if ($out -match [regex]::Escape($want)) { "PASS listed: $want" } else { "FAIL not listed: $want"; $fail++ } }
# Format-List prints Tcp before Command within one row, so the tcp plant's row must carry KOR-APP01:1433.
$row = ($out -split '(\r?\n){2,}' | Where-Object { $_ -match [regex]::Escape("$tag-tcp") } | Select-Object -First 1)
if ($row -match 'Tcp\s*:\s*KOR-APP01:1433') { "PASS the tcp plant names KOR-APP01:1433" } else { "FAIL the tcp plant shows no KOR-APP01:1433"; $fail++ }
$p1.Kill(); $p2 | Stop-Process
Start-Sleep -Seconds 1
$out2 = & pwsh -NoProfile -File $lister -MinMinutes 0 -SampleSeconds 2 | Out-String
if ($out2 -match [regex]::Escape($tag)) { "FAIL still listed after stopping"; $fail++ } else { "PASS 0 plants listed after stopping" }
"$(4 - $fail) of 4 passed"
exit $fail
