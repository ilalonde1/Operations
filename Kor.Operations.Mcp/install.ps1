<#
.SYNOPSIS
    First-time install of Kor.Operations.Mcp as a Windows Service on KOR-APP01.

.DESCRIPTION
    Creates the Windows Service entry pointing at the published binary.
    Re-running is safe: sc.exe create fails with code 1073 if the service
    already exists, which the script tolerates and reports.

    For ongoing redeploys (after the service exists), use the runbook in
    docs/runbooks/Kor.Operations.Mcp.deploy.md. That is stop/robocopy/start,
    not this script.

.PARAMETER InstallPath
    The directory on the target host where the published binaries live.
    Default matches the FileSync convention: D:\Services\Kor.Operations.Mcp.

.PARAMETER ServiceAccount
    The Windows account the service runs as. Default 'KOR\app-admin' is the
    standard service-account for KOR's other services on KOR-APP01 (verified
    2026-05-08 against the Services console: KOR Operations FileSync and
    KOR Opportunities Worker both run as KOR\app-admin). The account must
    have db_datareader/db_datawriter on the target SQL database AND working
    Deltek ODBC access; coordinate with the DBA / Deltek admin before
    changing.

.EXAMPLE
    Run on KOR-APP01 from an admin PowerShell:
    .\install.ps1
#>
param(
    [string]$ServiceName    = 'Kor.Operations.Mcp',
    [string]$DisplayName    = 'KOR Operations MCP Server',
    [string]$Description    = 'Hosts the AI tool catalog (Model Context Protocol) and COO Card scheduler for the Kor.Operations.App WPF client.',
    [string]$InstallPath    = 'D:\Services\Kor.Operations.Mcp',
    [string]$ServiceAccount = 'KOR\app-admin'
)

$ErrorActionPreference = 'Stop'

$exePath = Join-Path $InstallPath 'Kor.Operations.Mcp.exe'
if (-not (Test-Path $exePath)) {
    Write-Error "Binary not found at $exePath. Publish + robocopy the build output before running install."
}

# Friendly check before sc.exe gives a cleaner message than the raw
# Windows error for the common "already installed" path.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists. To redeploy, use the deploy runbook (sc stop / robocopy / sc start), NOT this script." -ForegroundColor Yellow
    return
}

Write-Host "Creating service '$ServiceName'..."
& sc.exe create $ServiceName binPath= "`"$exePath`"" start= auto displayname= "$DisplayName" obj= "$ServiceAccount"
if ($LASTEXITCODE -ne 0) {
    throw "sc.exe create failed with exit code $LASTEXITCODE."
}

Write-Host "Setting description..."
& sc.exe description $ServiceName "$Description" | Out-Null

# Auto-restart on the first two failures, reset window 1 day.
# (Recovery profile chosen to match FileSync's general operational posture;
# specific failure-action numbers are this script's choice, not verified
# against FileSync's actual sc.exe failure config.)
Write-Host "Configuring failure recovery..."
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/`"`"/0 | Out-Null

Write-Host "Done. Next steps:"
Write-Host "  1. Populate appsettings.Production.json on $InstallPath (Username/Password/SqlConnectionString/Anthropic key)."
Write-Host "  2. Run Sql\CreateAuditLog.sql against the target SQL Server."
Write-Host "  3. Open the firewall rule for the chosen port (or wire up the *.korstructural.com reverse proxy)."
Write-Host "  4. sc start $ServiceName"
Write-Host "  5. curl http://kor-app01:<port>/health to verify."
