#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes the Kor.Operations.FileSync Windows service.
    Leaves the published binaries and ProgramData folders in place so logs
    and shadow CSVs aren't lost.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ServiceName = 'Kor.Operations.FileSync'

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Host "Service '$ServiceName' not installed." -ForegroundColor Yellow
    return
}

if ($svc.Status -ne 'Stopped') {
    Write-Host "Stopping $ServiceName..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
}

Write-Host "Deleting $ServiceName..." -ForegroundColor Cyan
& sc.exe delete $ServiceName | Out-Null

Write-Host "Done. Binaries and ProgramData logs/shadow folders are preserved." -ForegroundColor Green
