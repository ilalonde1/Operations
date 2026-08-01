<#
.SYNOPSIS
  Render a styled dossier HTML page (the Claude-page look) to PDF via headless Edge.
  This is the preferred dossier deliverable: the PDF IS the web page.

.NOTES
  The HTML should carry its own @media print block: force light-theme tokens,
  print-color-adjust: exact, @page margins, and break-inside: avoid on cards/
  tables/pull-quotes. See scratchpad kra-brief.html for the reference pattern.

.EXAMPLE
  .\Format-BdWebPdf.ps1 -Html path\to\brief.html -Pdf docs\KOR-Foo-2026-07-03-web.pdf
#>
param(
  [Parameter(Mandatory)][string]$Html,
  [Parameter(Mandatory)][string]$Pdf
)
$ErrorActionPreference = "Stop"

$edge = @(
  'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
  'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw "Microsoft Edge not found in the standard install locations." }

$srcPath = (Resolve-Path $Html).Path
$outPath = [System.IO.Path]::GetFullPath($Pdf)
$uri = "file:///" + ($srcPath -replace '\\', '/')

& $edge --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf="$outPath" $uri 2>$null | Out-Null

# Edge returns before the file is flushed on slower runs; poll briefly.
$deadline = (Get-Date).AddSeconds(20)
while (-not (Test-Path $outPath) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (-not (Test-Path $outPath)) { throw "Edge did not produce $outPath" }

$kb = [math]::Round((Get-Item $outPath).Length / 1kb)
Write-Output ("WEB-PDF: {0}  ({1} KB)" -f (Split-Path $outPath -Leaf), $kb)
