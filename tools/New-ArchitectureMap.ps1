<#
.SYNOPSIS
    Builds and runs the architecture mapper. A LAUNCHER — the tool itself is a project in the solution.

.DESCRIPTION
    This was 776 lines of PowerShell that drew the whole map over Visio COM while the extractor sat
    in a proper project. Half a job, and the half left behind was the one that produces the actual
    deliverable — sitting in tools/, which is exactly where this tool's own findings page says
    prototypes go to die.

    All of it now lives in Kor.Operations.Architecture:

        Program.cs         command line, and the one command that measures every deliverable
        Extractor          Roslyn syntax trees -> the model
        GraphBuilder       force-directed and layered layout
        ScriptInventory    the parts that are not C#
        VisioRenderer      every page, over COM

    This file stays because the command is worth keeping in the fingers, and because a launcher is
    the right amount of PowerShell: build, then run.

.EXAMPLE
    ./tools/New-ArchitectureMap.ps1
    ./tools/New-ArchitectureMap.ps1 -Verify
    ./tools/New-ArchitectureMap.ps1 -ModelOnly      # skip Visio entirely
#>
[CmdletBinding()]
param(
    [string] $Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch] $Verify,
    [switch] $ModelOnly,
    [switch] $KeepVisioOpen
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $Root 'Kor.Operations.Architecture/Kor.Operations.Architecture.csproj'
$exe     = Join-Path $Root 'Kor.Operations.Architecture/bin/Debug/net8.0-windows/archmap.exe'

& dotnet build $project -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'archmap failed to build' }

$archArgs = @('--root', $Root)
if ($Verify)        { $archArgs += '--verify' }
if ($ModelOnly)     { $archArgs += '--model-only' }
if ($KeepVisioOpen) { $archArgs += '--keep-open' }

& $exe @archArgs
exit $LASTEXITCODE
