# SUPERSEDED 2026-07-10 by build-sector-report.ps1 (generic, live-pull + on-demand freshness + canonical template).
# Run: .uild-sector-report.ps1 -Key hospitals
& (Join-Path $PSScriptRoot "build-sector-report.ps1") -Key "hospitals"
