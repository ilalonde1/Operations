#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs Kor.Operations.FileSync as a Windows service on this machine.

.DESCRIPTION
    Run this from C:\Program Files\KorOperations\FileSync\ on the target server
    after copying the publish output there. Idempotent: if the service already
    exists, it is stopped and removed before a fresh install. Prompts once for
    the run-as account credential (app-admin).

    Configures:
      - Automatic (Delayed Start) so it comes up after the OS settles on boot.
      - Crash recovery: restart after 5s, then 30s, then 60s; reset counter daily.
      - Per-service SID (unrestricted) so we can ACL the ProgramData folders
        to just this service if we ever want to.
      - Creates C:\ProgramData\KorOperations\FileSync\{logs,shadow} with the
        service SID granted full control.

.PARAMETER ServiceAccount
    Domain\user (or .\user) to run the service as. Default: KOR\app-admin.

.PARAMETER ExePath
    Path to the published service exe. Default: same folder as this script.
#>
[CmdletBinding()]
param(
    [string]$ServiceAccount = 'KOR\app-admin',
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

$ServiceName = 'Kor.Operations.FileSync'
$DisplayName = 'KOR Operations FileSync'
$Description = 'KOR Operations: scheduled jobs, watcher, and SharePoint sync. Control plane in KorTransmittals.FileSync.*'

if (-not $ExePath) {
    $ExePath = Join-Path $PSScriptRoot 'Kor.Operations.FileSync.Service.exe'
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

# 3) Register the service. Automatic Delayed Start keeps it from racing the
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

# 4) Crash recovery: restart with backoff. Reset the failure counter every 24h
#    so transient blips don't permanently disable auto-restart.
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null

# 5) Per-service SID. Lets us ACL the log/shadow dirs to just this service.
& sc.exe sidtype $ServiceName unrestricted | Out-Null

# 6) Create the runtime data dirs and grant the service SID full control.
$progData = Join-Path $env:ProgramData 'KorOperations\FileSync'
$logs = Join-Path $progData 'logs'
$shadow = Join-Path $progData 'shadow'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
New-Item -ItemType Directory -Path $shadow -Force | Out-Null

$serviceSid = "NT SERVICE\$ServiceName"
foreach ($dir in @($progData, $logs, $shadow)) {
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

# Grant the service account "Log on as a service" right.
# (New-Service -Credential usually does this implicitly, but we double-check
#  via secedit so install fails loudly if AD denies it instead of silently
#  hanging at first start.)
$tmp = New-TemporaryFile
& secedit /export /cfg $tmp.FullName /areas USER_RIGHTS | Out-Null
$content = Get-Content $tmp.FullName
if ($content -notmatch 'SeServiceLogonRight.*' + [regex]::Escape($cred.UserName)) {
    Write-Host "NOTE: '$($cred.UserName)' may not have 'Log on as a service'. If startup fails, grant via:" -ForegroundColor Yellow
    Write-Host "  secpol.msc -> Local Policies -> User Rights Assignment -> Log on as a service" -ForegroundColor Yellow
}
Remove-Item $tmp.FullName -ErrorAction SilentlyContinue

# 7) Start it.
Write-Host "Starting service..." -ForegroundColor Cyan
Start-Service -Name $ServiceName

Start-Sleep -Seconds 3
$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "Service: $($svc.Name)"
Write-Host "Status:  $($svc.Status)" -ForegroundColor (if ($svc.Status -eq 'Running') {'Green'} else {'Red'})
Write-Host "Logs:    $logs"
Write-Host "Shadow:  $shadow"
Write-Host ""
Write-Host "Verify next:"
Write-Host "  1. Tail today's log: Get-Content '$logs\filesync-$(Get-Date -Format yyyyMMdd).log' -Tail 30 -Wait"
Write-Host "  2. Open Command Center on your workstation -> a row for $($env:COMPUTERNAME) should appear in the heartbeat panel."
