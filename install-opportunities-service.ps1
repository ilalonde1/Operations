#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs Kor.Opportunities.Worker as a Windows service on this machine.

.DESCRIPTION
    Run this from C:\Program Files\KorOperations\Opportunities\ on the target server
    after copying the publish output there. Idempotent: if the service already
    exists, it is stopped and removed before a fresh install. Prompts once for
    the run-as account credential (app-admin) and once for the SQL connection
    string (stored as a machine-level env var KOR_OPPORTUNITIES_OPPORTUNITIESDB).

    Configures:
      - Automatic (Delayed Start) so it comes up after the OS settles on boot.
      - Crash recovery: restart after 5s, then 30s, then 60s; reset counter daily.
      - Per-service SID (unrestricted) so we can ACL the ProgramData folders
        to just this service if we ever want to.
      - Creates C:\ProgramData\KorOperations\Opportunities\logs with the
        service SID granted full control.
      - Sets KOR_OPPORTUNITIES_OPPORTUNITIESDB machine env var (read at
        Worker startup; reloadOnChange:false so a service restart is required
        if this changes).

    Mirrors install-service.ps1 (FileSync) - same conventions on purpose.

.PARAMETER ServiceAccount
    Domain\user (or .\user) to run the service as. Default: KOR\app-admin.

.PARAMETER ConnectionString
    Full SQL Server connection string for KorOpportunitiesDb. If omitted, the
    script prompts for it (and accepts $env:KOR_OPPORTUNITIES_OPPORTUNITIESDB
    as a default if already set).

.PARAMETER ExePath
    Path to the published service exe. Default: same folder as this script.
#>
[CmdletBinding()]
param(
    [string]$ServiceAccount = 'KOR\app-admin',
    [string]$ConnectionString,
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

$ServiceName = 'Kor.Opportunities.Worker'
$DisplayName = 'KOR Opportunities Worker'
$Description = 'KOR Opportunities: BD opportunity ingestion + heartbeat. Backed by KorOpportunitiesDb on KOR-APP01\SQLEXPRESS.'
$EnvVarName  = 'KOR_OPPORTUNITIES_OPPORTUNITIESDB'

if (-not $ExePath) {
    $ExePath = Join-Path $PSScriptRoot 'Kor.Opportunities.Worker.exe'
}

if (-not (Test-Path $ExePath)) {
    throw "Service exe not found at $ExePath. Did you copy the publish folder here?"
}

$ExePath = (Resolve-Path $ExePath).Path
Write-Host "Service exe: $ExePath" -ForegroundColor Cyan

# 1) If the service already exists, stop + delete so we can recreate cleanly.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Existing service found. Stopping and removing." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# 2) Prompt for the service account credential. We never store this anywhere.
Write-Host "Enter the password for $ServiceAccount" -ForegroundColor Cyan
$cred = Get-Credential -UserName $ServiceAccount -Message "Password for $ServiceAccount (service run-as)"
if (-not $cred) { throw "Credential prompt cancelled. Aborting." }

# 3) Resolve the connection string. Order: explicit param -> existing machine env var -> prompt.
if (-not $ConnectionString) {
    $existingEnv = [Environment]::GetEnvironmentVariable($EnvVarName, 'Machine')
    if ($existingEnv) {
        Write-Host "Using existing machine env var $EnvVarName." -ForegroundColor DarkGray
        $ConnectionString = $existingEnv
    } else {
        $ConnectionString = Read-Host "KorOpportunitiesDb connection string (stored as machine env var $EnvVarName)"
    }
}
if (-not $ConnectionString) { throw "Connection string is required." }

# Sanity check before we set the env var: trim, must contain Database=KorOpportunitiesDb.
$ConnectionString = $ConnectionString.Trim()
if ($ConnectionString -notmatch '(?i)Database\s*=\s*KorOpportunitiesDb') {
    Write-Host "WARNING: connection string does not appear to target KorOpportunitiesDb. Continuing anyway." -ForegroundColor Yellow
}

[Environment]::SetEnvironmentVariable($EnvVarName, $ConnectionString, 'Machine')
Write-Host "Set machine env var $EnvVarName." -ForegroundColor Cyan

# 4) Register the service. Automatic Delayed Start keeps it from racing the
#    network/AD/SQL stack on boot.
Write-Host "Registering service $ServiceName..." -ForegroundColor Cyan
New-Service `
    -Name $ServiceName `
    -BinaryPathName "`"$ExePath`"" `
    -DisplayName $DisplayName `
    -Description $Description `
    -StartupType Automatic `
    -Credential $cred | Out-Null

# Flip to Automatic (Delayed Start). New-Service can't set delayed-auto directly.
& sc.exe config $ServiceName start= delayed-auto | Out-Null

# 5) Crash recovery: restart with backoff. Reset the failure counter every 24h
#    so transient blips don't permanently disable auto-restart.
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null

# 6) Per-service SID. Lets us ACL the log dir to just this service.
& sc.exe sidtype $ServiceName unrestricted | Out-Null

# 7) Create the runtime data dirs and grant the service SID full control.
$progData = Join-Path $env:ProgramData 'KorOperations\Opportunities'
$logs = Join-Path $progData 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null

$serviceSid = "NT SERVICE\$ServiceName"
foreach ($dir in @($progData, $logs)) {
    $acl = Get-Acl $dir
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $serviceSid,
        'FullControl',
        'ContainerInherit,ObjectInherit',
        'None',
        'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -Path $dir -AclObject $acl
}

# Soft-check 'Log on as a service'. New-Service -Credential usually grants it
# implicitly; this just surfaces a clear hint if AD denies.
$tmp = New-TemporaryFile
& secedit /export /cfg $tmp.FullName /areas USER_RIGHTS | Out-Null
$content = Get-Content $tmp.FullName
if ($content -notmatch 'SeServiceLogonRight.*' + [regex]::Escape($cred.UserName)) {
    Write-Host "NOTE: '$($cred.UserName)' may not have 'Log on as a service'. If startup fails, grant via:" -ForegroundColor Yellow
    Write-Host "  secpol.msc -> Local Policies -> User Rights Assignment -> Log on as a service" -ForegroundColor Yellow
}
Remove-Item $tmp.FullName -ErrorAction SilentlyContinue

# 8) Start it.
Write-Host "Starting service..." -ForegroundColor Cyan
Start-Service -Name $ServiceName

Start-Sleep -Seconds 3
$svc = Get-Service -Name $ServiceName
$statusColor = if ($svc.Status -eq 'Running') { 'Green' } else { 'Red' }
Write-Host ""
Write-Host "Service: $($svc.Name)"
Write-Host "Status:  $($svc.Status)" -ForegroundColor $statusColor
Write-Host "Logs:    $logs"
Write-Host ""
Write-Host "Verify next:"
Write-Host "  1. Tail today's log: Get-Content '$logs\opportunities-$(Get-Date -Format yyyyMMdd).log' -Tail 30 -Wait"
Write-Host "  2. SELECT * FROM opportunities.ServiceHeartbeat; -- one row, LastBeatUtc updating ~60s."
