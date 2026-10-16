#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes the Kor.Opportunities.Worker Windows service.

.DESCRIPTION
    Idempotent: succeeds even if the service is already gone. Does NOT delete
    the binaries under C:\Program Files\KorOperations\Opportunities\, the log
    folder under ProgramData, or the KOR_OPPORTUNITIES_OPPORTUNITIESDB env var
    - those are deliberately left in place so a re-install can pick them up.

    Mirrors uninstall-service.ps1 (FileSync) - same conventions on purpose.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ServiceName = 'Kor.Opportunities.Worker'

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Host "Service $ServiceName not found. Nothing to do." -ForegroundColor DarkGray
    return
}

if ($svc.Status -ne 'Stopped') {
    Write-Host "Stopping $ServiceName..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

Write-Host "Deleting $ServiceName..." -ForegroundColor Cyan
& sc.exe delete $ServiceName | Out-Null
Start-Sleep -Seconds 1

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Service still present after delete - reboot may be required." -ForegroundColor Yellow
} else {
    Write-Host "Service removed." -ForegroundColor Green
}
