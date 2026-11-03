#Requires -Version 7.0
<#
.SYNOPSIS
    Install the `kor` CLI (Kor.Operations.Framework.Cli - the Knowledge IDE's one command, Framework
    docs/KNOWLEDGE-IDE-PLAN.md K2) to C:\KOR-Operations\cli, beside netops, so Claude Code hooks can call it. A copy of
    tools/install-netops.ps1 with the names changed: publish to a stage, then swap file by file (a loaded file is renamed
    to <name>.old-<time>, never deleted), every file hash-verified. Framework-dependent; not single-file.
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $repo 'Kor.Operations.Framework.Cli\Kor.Operations.Framework.Cli.csproj'
# Its OWN folder, never netops' C:\KOR-Operations\cli: on 2 Nov the first install into the shared folder replaced 9 of
# netops' newer dependency DLLs (Microsoft.Identity.Client 4.83.1 and others) with older ones and netops stopped
# starting ("Could not load ... Microsoft.Identity.Client, Version=4.83.1.0"). Two apps, two folders.
$dest = 'C:\KOR-Operations\kor'

$stage = Join-Path $env:TEMP 'kor-cli-publish'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force $dest | Out-Null
Write-Host "Publishing kor -> $stage" -ForegroundColor Cyan
& dotnet publish $proj -c Release -r win-x64 --self-contained false -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $stage 'kor.exe'))) { throw "no kor.exe in $stage after publish." }
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
Write-Host "kor files: $swapped replaced, $same unchanged, every file hash-verified." -ForegroundColor Green
Write-Host "kor installed: $(Join-Path $dest 'kor.exe'). Try: kor --version ; kor ping" -ForegroundColor Green
exit 0
