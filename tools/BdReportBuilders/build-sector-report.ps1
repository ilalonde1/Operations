param(
    [Parameter(Mandatory)][string] $Key,        # sector key: hospitals|schools|defense|...
    [string] $Title                              # display title; defaults from key
)
$ErrorActionPreference = 'Stop'
# THE generic sector dossier builder (2026-07-10). Replaces the per-sector
# static-JSON + Word-COM builders. For ANY sector key it:
#   1. runs the app's SqlBdReportService via `emit` - which ENSURES on-demand
#      freshness (stale+changed verdicts re-synthesized) and writes fresh rows;
#   2. renders them on the canonical KOR template (tools/BdDocTemplate) with
#      verdict-age chips, via Format-BdWebPdf (headless Edge).
# Always live, always fresh, always the canonical look.

$repo = Split-Path (Split-Path $PSScriptRoot)
if (-not $Title) {
    $Title = (Get-Culture).TextInfo.ToTitleCase(($Key -replace '-', ' '))
}

$emit = Join-Path $env:TEMP "kor-sector-$Key.json"
& dotnet run --project (Join-Path $repo 'tools\BdSynthesisSmoke') -- emit $Key $emit 2>&1 |
    Where-Object { $_ -match '^emit:' } | ForEach-Object { Write-Host "  $_" }
if (-not (Test-Path $emit)) { throw "emit produced no data for sector '$Key'." }
$rows = Get-Content $emit -Raw | ConvertFrom-Json
if (-not $rows) { $rows = @() }

# verdict age (freshness-on-build)
$ttlDays = @{ 'PURSUE_URGENT' = 7; 'PURSUE' = 30; 'MONITOR' = 90; 'DISCOVER' = 90; 'DEAD' = 365 }
$nowUtc = (Get-Date).ToUniversalTime()
foreach ($b in $rows) {
    $age = $null; $stale = $false
    if ($b.honedAtUtc) {
        $age = [int][math]::Floor(($nowUtc - ([datetime]::Parse($b.honedAtUtc).ToUniversalTime())).TotalDays)
        $ttl = if ($b.verdict -and $ttlDays.ContainsKey([string]$b.verdict)) { $ttlDays[[string]$b.verdict] } else { 30 }
        $stale = $age -gt $ttl
    }
    $b | Add-Member -NotePropertyName AgeDays -NotePropertyValue $age -Force
    $b | Add-Member -NotePropertyName IsStale -NotePropertyValue $stale -Force
}
$urgent  = @($rows | Where-Object { $_.verdict -eq 'PURSUE_URGENT' })
$pursue  = @($rows | Where-Object { $_.verdict -eq 'PURSUE' })
$monitor = @($rows | Where-Object { $_.verdict -eq 'MONITOR' })
$dead    = @($rows | Where-Object { $_.verdict -eq 'DEAD' })
$fresh   = @($rows | Where-Object { $null -ne $_.AgeDays -and -not $_.IsStale })

function E { param($s) if ($null -eq $s) { return '' }; [System.Net.WebUtility]::HtmlEncode([string]$s) }
function AgeChip { param($b)
    if ($null -eq $b.AgeDays) { return '<span class="pill pill--cold">UNDATED</span>' }
    $d = [datetime]::Parse($b.honedAtUtc).ToUniversalTime().ToString('yyyy-MM-dd')
    if ($b.IsStale) { return "<span class=""pill pill--cold"">STALE $d ($($b.AgeDays)d)</span>" }
    return "<span class=""pill pill--fresh"">fresh $d ($($b.AgeDays)d)</span>"
}

$tpl = Get-Content (Join-Path $repo 'tools\BdDocTemplate\reference-handbook.html') -Raw
$style = $tpl.Substring($tpl.IndexOf('<style>'), $tpl.IndexOf('</style>') + 8 - $tpl.IndexOf('<style>'))
$extra = @'
<style>.doc{max-width:46rem;margin:0 auto;padding:0 1.5rem}
/* actionables - loud by design */
.acts{margin:.35rem 0 .6rem;padding:0;list-style:none}
.acts li{position:relative;padding:.42rem .6rem .42rem 2rem;margin:0 0 .3rem;
  background:var(--accent-bg,#FDEEE8);border-left:3px solid var(--accent);border-radius:4px;font-size:.96rem}
.acts li::before{content:"\2192";position:absolute;left:.7rem;top:.42rem;color:var(--accent);font-weight:700}
.acts .who{font-family:var(--mono);font-size:.82em;color:var(--accent-ink)}
.acts .when{font-weight:600}
.doactions{font-family:var(--mono);font-size:.66rem;letter-spacing:.14em;text-transform:uppercase;
  color:var(--accent-ink);font-weight:700;margin:.2rem 0 .25rem}
.rollup li{padding-left:2rem}
@media print{:root{--accent-bg:#FDEEE8}.doc{max-width:none;padding:0 16mm}html{font-size:12.5px!important}
  .hero{padding:12mm 16mm 8mm!important}.hero h1{font-size:24pt!important}.acts li{break-inside:avoid}}
</style>
'@

# Renders a project's action list loudly. Falls back to the KOR angle when the
# honing row carries no structured actions.
function ActionsHtml { param($p)
    $out = ''
    $acts = @($p.actions)
    if ($acts.Count -gt 0) {
        $out += '<p class="doactions">Do this</p><ul class="acts">'
        foreach ($a in ($acts | Select-Object -First 4)) {
            $line = ''
            if ($a.recommendation) { $line += (E $a.recommendation) }
            elseif ($a.type)       { $line += (E $a.type) }
            if ($a.targetPerson) { $line += " <span class=""who"">$(E $a.targetPerson)</span>" }
            if ($a.timingNotes)  { $line += " &#183; <span class=""when"">$(E $a.timingNotes)</span>" }
            if ($line) { $out += "<li>$line</li>" }
        }
        $out += '</ul>'
    } elseif ($p.korAngle) {
        $out += "<p class=""doactions"">Do this</p><ul class=""acts""><li>$(E $p.korAngle)</li></ul>"
    }
    return $out
}

$sb = [System.Text.StringBuilder]::new()
[void]$sb.Append("<title>KOR $Title Sector Report</title>").Append($style).Append($extra)
[void]$sb.Append(@"
<header class="hero"><div class="hero__wrap">
<p class="hero__eyebrow">KOR Structural &#183; Sector Report</p>
<h1>$([System.Net.WebUtility]::HtmlEncode($Title))</h1>
<p class="hero__lede">Live from the BD platform, verdicts refreshed on the fly. Every verdict shows its age.</p>
<div class="hero__meta"><span>BUILT $($nowUtc.ToString('yyyy-MM-dd HH:mm')) UTC</span>
<span>$($rows.Count) PROJECTS &#183; $($urgent.Count) URGENT</span>
<span>FRESHNESS $($fresh.Count)/$($rows.Count)</span></div></div></header><main class="doc">
"@)

# Top-of-report cross-cutting action rollup: every URGENT action, one list, so
# "what to do this week" is the first thing seen.
$rollup = @()
foreach ($p in $urgent) { foreach ($a in @($p.actions | Select-Object -First 2)) {
    $l = if ($a.recommendation) { $a.recommendation } elseif ($a.type) { $a.type } else { $null }
    if ($l) {
        $chip = "$(E $p.name): $(E $l)"
        if ($a.targetPerson) { $chip += " <span class=""who"">$(E $a.targetPerson)</span>" }
        if ($a.timingNotes)  { $chip += " &#183; <span class=""when"">$(E $a.timingNotes)</span>" }
        $rollup += $chip
    }
}}
if ($rollup.Count -gt 0) {
    [void]$sb.Append('<section><p class="kicker">Act this week</p><h2>Top actions across this sector</h2><ul class="acts rollup">')
    foreach ($c in ($rollup | Select-Object -First 8)) { [void]$sb.Append("<li>$c</li>") }
    [void]$sb.Append('</ul></section>')
}

if ($urgent.Count -gt 0) {
    [void]$sb.Append('<section><p class="kicker">01 &#183; Urgent</p><h2>Act now</h2>')
    foreach ($p in $urgent) {
        [void]$sb.Append("<h3>$(E $p.name) <span class=""pill pill--live"">URGENT</span> $(AgeChip $p)</h3>")
        [void]$sb.Append((ActionsHtml $p))
        [void]$sb.Append("<dl class=""map""><div><dt>Where</dt><dd>$(E $p.city), $(E $p.province)</dd></div><div><dt>Owner</dt><dd>$(E $p.proponent)</dd></div>")
        if ($p.cost)  { [void]$sb.Append("<div><dt>Value</dt><dd>$(E $p.cost)</dd></div>") }
        if ($p.stage) { [void]$sb.Append("<div><dt>Stage</dt><dd>$(E $p.stage)</dd></div>") }
        [void]$sb.Append('</dl>')
        if ($p.status)   { [void]$sb.Append("<p>$(E $p.status)</p>") }
    }
    [void]$sb.Append('</section>')
}
if ($pursue.Count -gt 0) {
    [void]$sb.Append('<section><p class="kicker">02 &#183; Pursue</p><h2>Open windows</h2>')
    foreach ($p in $pursue) {
        [void]$sb.Append("<h3>$(E $p.name) <span class=""pill pill--watch"">PURSUE</span> $(AgeChip $p)</h3>")
        [void]$sb.Append((ActionsHtml $p))
        if ($p.status)   { [void]$sb.Append("<p>$(E $p.status)</p>") }
    }
    [void]$sb.Append('</section>')
}
if ($monitor.Count -gt 0) {
    [void]$sb.Append('<section><p class="kicker">03 &#183; Monitor</p><h2>Watching</h2><div class="table-wrap"><table><thead><tr><th>Project</th><th>Owner</th><th>Prov</th><th>Age</th><th>Why</th></tr></thead><tbody>')
    foreach ($p in $monitor) {
        $ageCell = if ($null -eq $p.AgeDays) { '?' } elseif ($p.IsStale) { "$($p.AgeDays)d!" } else { "$($p.AgeDays)d" }
        $why = if ($p.korAngle) { $p.korAngle } else { $p.status }
        if ($why -and ([string]$why).Length -gt 150) { $why = ([string]$why).Substring(0,150) + '...' }
        [void]$sb.Append("<tr><td>$(E $p.name)</td><td>$(E $p.proponent)</td><td>$(E $p.province)</td><td>$ageCell</td><td>$(E $why)</td></tr>")
    }
    [void]$sb.Append('</tbody></table></div></section>')
}
if ($dead.Count -gt 0) {
    [void]$sb.Append('<section><p class="kicker">04 &#183; Closed out</p><h2>Dead, reasons on record</h2><ul class="plain">')
    foreach ($p in $dead) {
        $why = if ($p.status) { $p.status } else { $p.korAngle }
        if ($why -and ([string]$why).Length -gt 180) { $why = ([string]$why).Substring(0,180) + '...' }
        [void]$sb.Append("<li><strong>$(E $p.name)</strong> &#8212; $(E $why)</li>")
    }
    [void]$sb.Append('</ul></section>')
}
[void]$sb.Append("<footer>KOR Structural &#8212; Confidential / Internal &#183; $Title sector &#183; verdicts + ages computed live at build time from the BD platform.</footer></main>")

$tmpHtml = Join-Path $env:TEMP "kor-sector-report-$Key.html"
[System.IO.File]::WriteAllText($tmpHtml, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
$pdfOut = Join-Path $repo "docs\KOR-$Title-Sector-Report-$($nowUtc.ToString('yyyy-MM-dd'))-web.pdf".Replace(' ', '-')
& (Join-Path $repo 'tools\Format-BdWebPdf.ps1') -Html $tmpHtml -Pdf $pdfOut
Copy-Item $pdfOut ("C:\Users\ilalonde\Desktop\KOR-$Title-Sector-Report.pdf".Replace(' ', '-')) -Force
"Wrote: $pdfOut (+ Desktop copy)"
