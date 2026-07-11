$ErrorActionPreference = 'Stop'
# Defense sector report - canonical-template edition (2026-07-10).
# The legacy Word-COM builder produced the old boxy look; this builder emits
# the KOR document design system (tools/BdDocTemplate, single source of truth
# for the <style> block) and renders the PDF via Format-BdWebPdf.ps1
# (headless Edge). Data: Desktop\Polish\.defense-final.json (re-pull from
# opportunities.MajorProjectEnrichment; entries carry HonedAtUtc).
# Verdict-age is computed at build time - stale verdicts annotate themselves.

$repo = Split-Path (Split-Path $PSScriptRoot)
$briefs = Get-Content 'C:\Users\ilalonde\Desktop\Polish\.defense-final.json' -Raw | ConvertFrom-Json
if (-not $briefs -or $briefs.Count -eq 0) { throw "No briefs in .defense-final.json." }

# --- verdict age (freshness-on-build) ---
$ttlDays = @{ 'PURSUE_URGENT' = 7; 'PURSUE' = 30; 'MONITOR' = 90; 'DISCOVER' = 90; 'DEAD' = 365 }
$nowUtc = (Get-Date).ToUniversalTime()
foreach ($b in $briefs) {
    $age = $null; $stale = $false
    if ($b.HonedAtUtc) {
        $age = [int][math]::Floor(($nowUtc - ([datetime]::Parse($b.HonedAtUtc).ToUniversalTime())).TotalDays)
        $ttl = if ($b.Verdict -and $ttlDays.ContainsKey([string]$b.Verdict)) { $ttlDays[[string]$b.Verdict] } else { 30 }
        $stale = $age -gt $ttl
    }
    $b | Add-Member -NotePropertyName AgeDays -NotePropertyValue $age -Force
    $b | Add-Member -NotePropertyName IsStale -NotePropertyValue $stale -Force
}
$urgent  = @($briefs | Where-Object {$_.Verdict -eq 'PURSUE_URGENT'})
$pursue  = @($briefs | Where-Object {$_.Verdict -eq 'PURSUE'})
$monitor = @($briefs | Where-Object {$_.Verdict -eq 'MONITOR'})
$dead    = @($briefs | Where-Object {$_.Verdict -eq 'DEAD'})
$fresh   = @($briefs | Where-Object { $null -ne $_.AgeDays -and -not $_.IsStale })

function E { param($s) if ($null -eq $s) { return '' }; [System.Net.WebUtility]::HtmlEncode([string]$s) }
function AgeChip { param($b)
    if ($null -eq $b.AgeDays) { return '<span class="pill pill--cold">UNDATED</span>' }
    $d = [datetime]::Parse($b.HonedAtUtc).ToUniversalTime().ToString('yyyy-MM-dd')
    if ($b.IsStale) { return "<span class=""pill pill--cold"">STALE - honed $d ($($b.AgeDays)d)</span>" }
    return "<span class=""pill pill--fresh"">honed $d ($($b.AgeDays)d ago)</span>"
}

# --- canonical style from the single source of truth ---
$tpl = Get-Content (Join-Path $repo 'tools\BdDocTemplate\reference-handbook.html') -Raw
$style = $tpl.Substring($tpl.IndexOf('<style>'), $tpl.IndexOf('</style>') + 8 - $tpl.IndexOf('<style>'))
$extra = @'
<style>
  .doc { max-width: 46rem; margin: 0 auto; padding: 0 1.5rem; }
  @media print { .doc { max-width: none; padding: 0 16mm; }
    html { font-size: 12.5px !important; }
    .hero { padding: 12mm 16mm 8mm !important; }
    .hero h1 { font-size: 24pt !important; } }
</style>
'@

$sb = [System.Text.StringBuilder]::new()
[void]$sb.Append('<title>KOR Defence Sector Report</title>').Append($style).Append($extra)

[void]$sb.Append(@"
<header class="hero"><div class="hero__wrap">
<p class="hero__eyebrow">KOR Structural &#183; Sector Report &#183; Defence / Military</p>
<h1>Defence &amp; military construction</h1>
<p class="hero__lede">BC + Alberta pipeline with live DCC deadlines, named contacts, and today-true verdicts. Every verdict shows its age.</p>
<div class="hero__meta">
<span>BUILT $($nowUtc.ToString('yyyy-MM-dd HH:mm')) UTC</span>
<span>$($briefs.Count) PROJECTS &#183; $($urgent.Count) URGENT</span>
<span>FRESHNESS: $($fresh.Count)/$($briefs.Count) WITHIN WINDOW</span>
</div></div></header><main class="doc">
"@)

# --- 01 urgent ---
[void]$sb.Append('<section><p class="kicker">01 &#183; Urgent</p><h2>Act this week</h2>')
foreach ($p in $urgent) {
    [void]$sb.Append("<h3>$(E $p.Name) <span class=""pill pill--live"">URGENT</span> $(AgeChip $p)</h3>")
    [void]$sb.Append("<dl class=""map"">")
    [void]$sb.Append("<div><dt>Where</dt><dd>$(E $p.City), $(E $p.Province)</dd></div>")
    [void]$sb.Append("<div><dt>Owner</dt><dd>$(E $p.Proponent)</dd></div>")
    if ($p.Cost)  { [void]$sb.Append("<div><dt>Value</dt><dd>$(E $p.Cost)</dd></div>") }
    if ($p.Stage) { [void]$sb.Append("<div><dt>Stage</dt><dd>$(E $p.Stage)</dd></div>") }
    [void]$sb.Append('</dl>')
    if ($p.Item.status) { [void]$sb.Append("<p>$(E $p.Item.status)</p>") }
    $acts = @($p.Item.actions | Select-Object -First 3)
    if ($acts.Count -gt 0) {
        [void]$sb.Append('<ul class="plain">')
        foreach ($a in $acts) {
            $line = "<strong>$(E $a.type):</strong> $(E $a.recommendation)"
            if ($a.targetPerson) { $line += " <span class=""path"">$(E $a.targetPerson)</span>" }
            if ($a.timingNotes)  { $line += " &#8212; <em>$(E $a.timingNotes)</em>" }
            [void]$sb.Append("<li>$line</li>")
        }
        [void]$sb.Append('</ul>')
    } elseif ($p.Item.korAngle) {
        [void]$sb.Append("<div class=""box box--auto""><p class=""box__label"">KOR angle</p><p>$(E $p.Item.korAngle)</p></div>")
    }
}
[void]$sb.Append('</section>')

# --- 02 pursue ---
if ($pursue.Count -gt 0) {
    [void]$sb.Append('<section><p class="kicker">02 &#183; Pursue</p><h2>Open windows</h2>')
    foreach ($p in $pursue) {
        [void]$sb.Append("<h3>$(E $p.Name) <span class=""pill pill--watch"">PURSUE</span> $(AgeChip $p)</h3>")
        if ($p.Item.status)   { [void]$sb.Append("<p>$(E $p.Item.status)</p>") }
        if ($p.Item.korAngle) { [void]$sb.Append("<p><strong>Play:</strong> $(E $p.Item.korAngle)</p>") }
    }
    [void]$sb.Append('</section>')
}

# --- 03 monitor ---
[void]$sb.Append('<section><p class="kicker">03 &#183; Monitor</p><h2>Watching, not chasing</h2>')
[void]$sb.Append('<div class="table-wrap"><table><thead><tr><th>Project</th><th>Owner</th><th>Prov</th><th>Age</th><th>Why monitor</th></tr></thead><tbody>')
foreach ($p in $monitor) {
    $ageCell = if ($null -eq $p.AgeDays) { '?' } elseif ($p.IsStale) { "$($p.AgeDays)d STALE" } else { "$($p.AgeDays)d" }
    $why = if ($p.Item.korAngle) { $p.Item.korAngle } else { $p.Item.status }
    if ($why -and ([string]$why).Length -gt 160) { $why = ([string]$why).Substring(0,160) + '...' }
    [void]$sb.Append("<tr><td>$(E $p.Name)</td><td>$(E $p.Proponent)</td><td>$(E $p.Province)</td><td>$ageCell</td><td>$(E $why)</td></tr>")
}
[void]$sb.Append('</tbody></table></div></section>')

# --- 04 dead ---
if ($dead.Count -gt 0) {
    [void]$sb.Append('<section><p class="kicker">04 &#183; Closed out</p><h2>Dead, with reasons on record</h2><ul class="plain">')
    foreach ($p in $dead) {
        $why = if ($p.Item.status) { $p.Item.status } else { $p.Item.korAngle }
        if ($why -and ([string]$why).Length -gt 200) { $why = ([string]$why).Substring(0,200) + '...' }
        [void]$sb.Append("<li><strong>$(E $p.Name)</strong> &#8212; $(E $why) $(AgeChip $p)</li>")
    }
    [void]$sb.Append('</ul></section>')
}

# --- 05 how this sector works (stable framing) ---
[void]$sb.Append(@'
<section><p class="kicker">05 &#183; How this sector works</p><h2>Three rules that don&#8217;t change</h2>
<ul class="plain">
<li><strong>DCC is the gate, primes are the door.</strong> KOR enters as structural sub on a design-build prime&#8217;s team (EllisDon, PCL, Graham, Bird) &#8212; pre-position before the RFP closes the team. DCC&#8217;s MERX stream is now ingested by the platform automatically.</li>
<li><strong>Clearance follows the team.</strong> Sponsorship triggers when a team names KOR (AFR rides the bid; DCC can sponsor direct). The qualification path is the companion brief.</li>
<li><strong>EllisDon is the strategic relationship</strong> &#8212; recurring design prime across this sector, same firm KOR is building toward in BC healthcare.</li>
</ul>
<footer>KOR Structural &#8212; Confidential / Internal &#183; verdicts and ages computed at build time from the BD platform &#183; companion: KOR-Defence-Qualification-Path (clearances &amp; registrations).</footer>
</section></main>
'@)

$tmpHtml = Join-Path $env:TEMP 'kor-defense-report.html'
[System.IO.File]::WriteAllText($tmpHtml, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

$pdfOut = Join-Path $repo "docs\KOR-Defence-Sector-Report-$($nowUtc.ToString('yyyy-MM-dd'))-web.pdf"
& (Join-Path $repo 'tools\Format-BdWebPdf.ps1') -Html $tmpHtml -Pdf $pdfOut
Copy-Item $pdfOut 'C:\Users\ilalonde\Desktop\KOR-Defence-Sector-Report.pdf' -Force
"Wrote: $pdfOut (+ Desktop copy)"
