# FIX restart-pc: restart in 5 minutes with an on-screen warning (so work can be saved). Returns plain values.
$msg = 'KOR IT (NetworkOps) will restart this computer in 5 minutes to fix a problem. Please save your work now.'
& shutdown.exe /r /t 300 /c $msg /d p:4:1
[pscustomobject]@{ Result = if ($LASTEXITCODE -eq 0) { 'Restart scheduled in 5 minutes; the user was warned on screen.' } else { "shutdown.exe refused (exit $LASTEXITCODE): a restart may already be pending." }; ExitCode = $LASTEXITCODE }
