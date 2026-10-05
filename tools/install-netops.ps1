#Requires -Version 7.0
<#
.SYNOPSIS
    Install the `netops` CLI to a stable location and put it on PATH, so `netops ...` works from any shell -- including
    Claude Code's .mcp.json (`netops mcp`, the NetworkOps MCP tools). Run from this repo on a machine that uses netops
    (KOR-1001). Idempotent; framework-dependent (KOR-1001 has the .NET 8 Desktop runtime). NOT single-file -- the MCP
    host/SDK (`netops mcp`) does not run reliably from a single-file bundle, so it publishes as a normal folder.
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $repo 'Kor.Operations.NetworkOps.Cli\Kor.Operations.NetworkOps.Cli.csproj'
$dest = 'C:\KOR-Operations\cli'

if (Test-Path $dest) { Remove-Item "$dest\*" -Recurse -Force -Confirm:$false }   # clean (e.g. a stale single-file exe)
New-Item -ItemType Directory -Force $dest | Out-Null
Write-Host "Publishing netops -> $dest" -ForegroundColor Cyan
& dotnet publish $proj -c Release -r win-x64 --self-contained false -o $dest --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $dest 'netops.exe'))) { throw "no netops.exe in $dest after publish." }

# Put $dest on the USER PATH (persistent) if it is not already there.
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $dest) {
    [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ';' + $dest), 'User')
    Write-Host "Added $dest to the user PATH (new shells will see it)." -ForegroundColor Green
}
$env:Path = $env:Path + ';' + $dest
Write-Host "netops installed: $(Join-Path $dest 'netops.exe'). Try: netops findings --hosts KOR-206-N" -ForegroundColor Green
exit 0
