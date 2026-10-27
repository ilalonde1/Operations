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

# Publish to a stage, then swap file by file. Every Claude session runs `netops mcp` from $dest, so its exe and DLLs are
# loaded and cannot be deleted or overwritten -- but Windows does let a loaded file be RENAMED. A changed file is renamed
# to <name>.old-<time> and the new one copied in: running bridges keep the old code until they restart, new calls get the
# new code. (Until 2026-10-27 this deleted $dest\* first, which fails while any session is open.) Old copies that are no
# longer loaded are removed on the next install.
$stage = Join-Path $env:TEMP 'netops-cli-publish'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force $dest | Out-Null
Write-Host "Publishing netops -> $stage" -ForegroundColor Cyan
& dotnet publish $proj -c Release -r win-x64 --self-contained false -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $stage 'netops.exe'))) { throw "no netops.exe in $stage after publish." }
Get-ChildItem $dest -Recurse -File -Filter '*.old-*' -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
$stamp = Get-Date -Format 'yyyyMMddHHmmss'; $swapped = 0; $same = 0
foreach ($f in Get-ChildItem $stage -Recurse -File) {
    $rel = $f.FullName.Substring($stage.Length).TrimStart('\')
    $target = Join-Path $dest $rel
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $f.FullName).Hash) { $same++; continue }
        Rename-Item -LiteralPath $target -NewName "$([IO.Path]::GetFileName($target)).old-$stamp"
    }
    Copy-Item -LiteralPath $f.FullName -Destination $target
    $swapped++
}
$bad = @(Get-ChildItem $stage -Recurse -File | Where-Object { (Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $dest $_.FullName.Substring($stage.Length).TrimStart('\'))).Hash })
if ($bad.Count) { throw "$($bad.Count) file(s) in $dest do not match the build: $($bad.Name -join ', ')" }
Write-Host "netops files: $swapped replaced, $same unchanged, every file hash-verified." -ForegroundColor Green

# Put $dest on the USER PATH (persistent) if it is not already there.
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $dest) {
    [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ';' + $dest), 'User')
    Write-Host "Added $dest to the user PATH (new shells will see it)." -ForegroundColor Green
}
$env:Path = $env:Path + ';' + $dest
Write-Host "netops installed: $(Join-Path $dest 'netops.exe'). Try: netops findings --hosts KOR-206-N" -ForegroundColor Green
exit 0
