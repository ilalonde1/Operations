# Find-WedgedClaudeProcess.ps1 -- READ-ONLY. Lists processes on THIS machine that a Claude Code session started (any
# descendant of claude.exe) and that look wedged: older than -MinMinutes AND either idle (no CPU and no file I/O over
# a -SampleSeconds window) or still holding a TCP connection to the SQL server (APP01:1433) or the file server
# (FS01:445). It names the owning session's claude.exe (pid, start time, working folder) and the command line.
# It NEVER stops anything: killing a process is the owning session's call (MEMORY: never kill other sessions' processes).
#
# Board: KorStandards oversight.Commitment 56, the catch-all for class silent-hang (rule 11): three instances so far
# -- the Virtual Drafter's background oq.py tasks wedged 10-25/26 (stopped 10-27 18:21), and the netops CLI waiting
# forever on an MFA prompt nobody could answer (Mistake 20).
#
# COVERS: every descendant of every claude.exe on this machine, at any depth (python, dotnet, pwsh, git, sqlcmd,
# testhost...), its age, CPU and I/O deltas over the sample, and its TCP connections to APP01:1433 / FS01:445.
# DOES NOT COVER:
#   - a process whose parent exited (Windows does not re-parent; the chain to claude.exe is broken) -- reported
#     separately as an ORPHAN only when its name is in -OrphanNames and it matches the wedge test;
#   - work started on ANOTHER machine through NetworkOps or a remote shell (look there, or at networkops_runs);
#   - a process that is busy-looping (CPU > 0) while making no progress: it is not idle, so only the age and TCP
#     tests can name it;
#   - which Claude session NAME owns it: claude.exe carries no session name, only its pid, start time and folder;
#   - a process matching -LongLived (MCP bridges), which is counted but never judged -- a wedged MCP bridge is missed.
# SAME-CLASS FAULT IT WOULD MISS: a hang inside a long-lived service (the NetworkOps service on APP01 itself).
#
# Output: one row per suspect, then "N suspect(s) of M Claude descendant(s)". Exit code = number of suspects (0 = clean).
param(
    [int]$MinMinutes = 120,
    [int]$SampleSeconds = 10,
    [string[]]$TcpTargets = @('KOR-APP01:1433', 'KOR-FS01:445'),
    [string[]]$OrphanNames = @('python.exe', 'sqlcmd.exe', 'testhost.exe', 'dotnet.exe'),
    # Servers a session keeps for its whole life and that sit idle by design (an MCP bridge waits on stdio): counted,
    # never listed. 10-27 first run: 5 of 5 30-min suspects were `netops.exe mcp`, one per session.
    [string]$LongLived = '(?i)\bnetops(\.exe)?"?\s+mcp\b|\bmcp-server\b|\s--mcp\b'
)
$ErrorActionPreference = 'Stop'

function Snapshot { Get-CimInstance Win32_Process | ForEach-Object { [pscustomobject]@{
    Pid = [int]$_.ProcessId; Ppid = [int]$_.ParentProcessId; Name = $_.Name; Cmd = $_.CommandLine; Start = $_.CreationDate
    Cpu = [long]$_.KernelModeTime + [long]$_.UserModeTime; Io = [long]$_.ReadTransferCount + [long]$_.WriteTransferCount + [long]$_.OtherTransferCount } } }

$a = Snapshot
$byPid = @{}; foreach ($p in $a) { $byPid[$p.Pid] = $p }
$claudes = @($a | Where-Object Name -eq 'claude.exe')

# Walk up to the nearest claude.exe ancestor (guarding against pid reuse: a parent must be older than its child).
function ClaudeOf($p) {
    $cur = $p; $seen = @{}
    while ($cur -and -not $seen.ContainsKey($cur.Pid)) {
        $seen[$cur.Pid] = $true
        $par = $byPid[$cur.Ppid]
        if (-not $par -or ($par.Start -and $cur.Start -and $par.Start -gt $cur.Start)) { return $null }
        if ($par.Name -eq 'claude.exe') { return $par }
        $cur = $par
    }
    return $null
}

$desc = foreach ($p in $a) { if ($p.Name -ne 'claude.exe' -and $p.Name -ne 'conhost.exe') { $c = ClaudeOf $p; if ($c) { [pscustomobject]@{ P = $p; Claude = $c } } } }
$orph = $a | Where-Object { $_.Name -in $OrphanNames -and -not $byPid.ContainsKey($_.Ppid) }

Start-Sleep -Seconds $SampleSeconds
$b = @{}; foreach ($p in Snapshot) { $b[$p.Pid] = $p }

# Resolve the TCP targets once; a connection counts when its remote address is one of theirs on that port.
$targets = foreach ($t in $TcpTargets) { $h, $port = $t -split ':'; try { foreach ($ip in [Net.Dns]::GetHostAddresses($h)) { [pscustomobject]@{ Ip = $ip.ToString(); Port = [int]$port; Label = $t } } } catch { } }
$tcp = @{}; foreach ($c in Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue) {
    $hit = $targets | Where-Object { $_.Ip -eq $c.RemoteAddress -and $_.Port -eq $c.RemotePort } | Select-Object -First 1
    if ($hit) { $tcp[[int]$c.OwningProcess] = @($tcp[[int]$c.OwningProcess]) + $hit.Label | Where-Object { $_ } } }

$now = Get-Date
function Row($p, $claude, $kind) {
    $after = $b[$p.Pid]; if (-not $after -or $after.Start -ne $p.Start) { return }   # gone, or the pid was reused
    $age = if ($p.Start) { [int]($now - $p.Start).TotalMinutes } else { 0 }
    if ($age -lt $MinMinutes) { return }
    $idle = ($after.Cpu -eq $p.Cpu) -and ($after.Io -eq $p.Io)
    $conn = @($tcp[$p.Pid]) | Where-Object { $_ }
    if (-not $idle -and -not $conn) { return }
    $cwd = if ($claude) { ([regex]::Match([string]$claude.Cmd, '(?i)--cwd\s+"?([^"]+)"?')).Groups[1].Value } else { '' }
    [pscustomobject]@{
        Kind = $kind; Pid = $p.Pid; Name = $p.Name; AgeMin = $age; Idle = $idle; Tcp = ($conn -join ',')
        Claude = if ($claude) { "claude.exe pid $($claude.Pid) started $($claude.Start.ToString('MM-dd HH:mm'))$(if ($cwd) { " in $cwd" })" } else { '(parent gone)' }
        Command = ([string]$p.Cmd).Substring(0, [Math]::Min(200, ([string]$p.Cmd).Length))
    }
}

$servers = @($desc | Where-Object { [string]$_.P.Cmd -match $LongLived })
$rows = @(foreach ($d in $desc) { if ([string]$d.P.Cmd -notmatch $LongLived) { Row $d.P $d.Claude 'descendant' } }) + @(foreach ($o in $orph) { Row $o $null 'orphan' }) | Where-Object { $_ }
$rows | Sort-Object AgeMin -Descending | Format-List | Out-String -Width 240
"$(@($rows).Count) suspect(s) of $(@($desc).Count) Claude descendant(s) on $env:COMPUTERNAME (claude.exe sessions: $($claudes.Count); older than $MinMinutes min; idle = no CPU and no I/O over $SampleSeconds s, or TCP to $($TcpTargets -join ' / ')); $($servers.Count) long-lived server(s) such as MCP bridges counted, not judged"
exit @($rows).Count
