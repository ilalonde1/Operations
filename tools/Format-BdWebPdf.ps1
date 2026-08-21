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

# Render through a copy under a name Edge has never seen.
#
# Edge caches file:/// URLs, and it does it silently: edit the HTML, re-render to the same path,
# and you get a PDF of the version before your edit at a plausible new file size. That shipped a
# dossier still carrying a claim the source had already corrected, and it passed a check that
# read the HTML rather than the PDF. The copy sits beside the source so any relative reference
# still resolves, and is removed once the PDF is on disk.
$fresh = Join-Path (Split-Path $srcPath -Parent) ("_render-" + [guid]::NewGuid().ToString('N') + ".html")
Copy-Item -LiteralPath $srcPath -Destination $fresh -Force
$uri = "file:///" + ($fresh -replace '\\', '/')

try {
    $started = Get-Date
    & $edge --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf="$outPath" $uri 2>$null | Out-Null

    # Edge can return while an existing destination still holds the old PDF. Wait for this render
    # to touch the output before removing the temporary source file.
    $deadline = (Get-Date).AddSeconds(20)
    while (((-not (Test-Path $outPath)) -or ((Get-Item $outPath).LastWriteTime -lt $started)) -and
           (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    if (-not (Test-Path $outPath)) { throw "Edge did not produce $outPath" }
    if ((Get-Item $outPath).LastWriteTime -lt $started) { throw "Edge did not refresh $outPath" }
}
finally {
    # Only after the PDF exists. Deleting while the renderer still holds the source is the race
    # that produces a PDF of the browser's "file not found" page, which looks like a document.
    Remove-Item -LiteralPath $fresh -Force -ErrorAction SilentlyContinue
}

$kb = [math]::Round((Get-Item $outPath).Length / 1kb)
Write-Output ("WEB-PDF: {0}  ({1} KB)" -f (Split-Path $outPath -Leaf), $kb)
