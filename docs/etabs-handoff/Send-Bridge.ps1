<#
  Hands one command file to the Drafter Bridge on a workstation and waits for its reply.

  The bridge is a FileSystemWatcher inside a running Revit: a JSON dropped in the inbox comes
  back by id in the outbox. Written as a script rather than typed inline because a command
  carrying Windows paths through a shell loses its backslashes -- that has cost this project an
  afternoon more than once.
#>
param(
    [Parameter(Mandatory)][string]$File,
    [string]$ComputerName = 'KOR-302N',
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'

$id    = [System.IO.Path]::GetFileNameWithoutExtension($File) + '-' + (Get-Random -Maximum 999999)
$inbox = "\\$ComputerName\C`$\KOR.Drafter\bridge\inbox\$id.json"
$out   = "\\$ComputerName\C`$\KOR.Drafter\bridge\outbox\$id.json"

Copy-Item -LiteralPath $File -Destination $inbox -Force
Write-Host "sent $id" -ForegroundColor DarkGray

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$lastBeat = Get-Date
while ((Get-Date) -lt $deadline -and -not (Test-Path $out)) {
    Start-Sleep -Milliseconds 900
    if (((Get-Date) - $lastBeat).TotalSeconds -ge 30) {
        Write-Host ("  still working, {0:0}s elapsed" -f ((Get-Date) - $lastBeat).TotalSeconds) -ForegroundColor DarkGray
        $lastBeat = Get-Date
    }
}

if (-not (Test-Path $out)) {
    Write-Host "NO REPLY in $TimeoutSeconds s." -ForegroundColor Red
    exit 1
}

Get-Content -LiteralPath $out -Raw
