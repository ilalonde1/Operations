# Invoke-KorWorkstationAudit.ps1
# Runs the autonomous KOR workstation audit (KorWorkstationAudit-Prompt.md) on THIS machine, headless.
# Output lands in C:\_KORAudit\ and is copied to \\KOR-FS01\Management\IT\Workstation-Audits\<HOSTNAME>\.
#
# Usage (plain PowerShell, logged-in user, NOT elevated — bypass mode refuses elevated shells):
#   powershell -ExecutionPolicy Bypass -File Invoke-KorWorkstationAudit.ps1
#
# Requires Claude Code on PATH and a logged-in account (claude /login once per machine).
# First run fleet-wide should be KOR-1001 so Phase 2 bootstraps the draft KOR-Workstation-Standard.md.

$ErrorActionPreference = 'Stop'

$promptFile = Join-Path $PSScriptRoot 'KorWorkstationAudit-Prompt.md'
if (-not (Test-Path $promptFile)) { throw "Prompt file not found: $promptFile" }

if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {
    throw 'Claude Code not found on PATH. Install: irm https://claude.ai/install.ps1 | iex  (then: claude /login)'
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Running elevated. Bypass-permissions mode refuses elevated shells — rerun from a plain PowerShell window.'
}

Write-Host "Starting KOR workstation audit on $env:COMPUTERNAME — this can run an hour or more. Output: C:\_KORAudit\" -ForegroundColor Cyan

Get-Content -Raw $promptFile | claude -p --dangerously-skip-permissions

if (Test-Path 'C:\_KORAudit\audit-report.md') {
    Write-Host "Done. Report: C:\_KORAudit\audit-report.md" -ForegroundColor Green
} else {
    Write-Warning 'Audit finished but C:\_KORAudit\audit-report.md was not found - review the console output above.'
}
