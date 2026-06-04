#Requires -Version 7.0
<#
.SYNOPSIS
    Step 2 — Deep-dive verified websites and gather raw evidence per firm.

.DESCRIPTION
    Reads discovered-websites.csv (output of Step 1 Sonnet pass). For each row:
      - If Website is empty → caller can invoke Step3-MarkDrops.ps1 separately
      - If Website is populated → fetch homepage, scrape About/Contact/Projects/Team
        pages, capture meta + emails + phones + addresses + text snippet,
        check MX records, query Wayback Machine, attempt registry lookups
        (OpenCorporates, BC Registry), check govcanadacontracts.ca presence
    Writes verbose JSON per firm to outputs/evidence/evidence-<id>.json.

.PARAMETER InputCsv
    Path to Step 1's output. Default: KOR-Data-Honing/outputs/discovered-websites.csv

.PARAMETER OutDir
    Directory for per-firm evidence JSON. Default:
    KOR-Data-Honing/outputs/gathered-evidence-<yyyy-MM-dd>/

.PARAMETER OnlyConfidence
    Optional filter: 'high' or 'high,medium' to skip uncertain Step 1 rows.

.PARAMETER ResumeFromId
    Optional Id to resume mid-batch (skip rows with Id < ResumeFromId).
#>

param(
    [string]$InputCsv = "C:\VIsual Studio Projects\KOR-Data-Honing\outputs\discovered-websites.csv",
    [string]$OutDir = ("C:\VIsual Studio Projects\KOR-Data-Honing\outputs\gathered-evidence-" + (Get-Date -Format 'yyyy-MM-dd')),
    [string]$OnlyConfidence = '',
    [long]$ResumeFromId = 0,
    [int]$MaxRows = 0
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'  # speed up Invoke-WebRequest
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$evidenceDir = Join-Path $OutDir 'evidence'
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
$logPath = Join-Path $OutDir 'gather-progress.log'
$summaryPath = Join-Path $OutDir 'gather-summary.md'

function Write-Log([string]$msg) {
    $stamp = Get-Date -Format 'HH:mm:ss'
    "$stamp $msg" | Add-Content -Path $logPath
}

function Try-Url([string]$url, [int]$timeout = 8) {
    try {
        $r = Invoke-WebRequest -Uri $url -Method Head -TimeoutSec $timeout -MaximumRedirection 5 -UserAgent 'Mozilla/5.0 KOR-BD-Intel-Bot' -ErrorAction Stop
        return [pscustomobject]@{
            Status   = [int]$r.StatusCode
            FinalUrl = $r.BaseResponse.RequestMessage.RequestUri.AbsoluteUri
            Ok       = ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400)
        }
    } catch {
        return $null
    }
}

function Get-PageEvidence([string]$url, [int]$timeout = 15) {
    try {
        $r = Invoke-WebRequest -Uri $url -Method Get -TimeoutSec $timeout -MaximumRedirection 5 -UserAgent 'Mozilla/5.0 KOR-BD-Intel-Bot' -ErrorAction Stop
        $html = $r.Content
        $clean = $html -replace '(?is)<(script|style)\b[^>]*>.*?</\1>',''
        $title = if ($html -match '(?is)<title[^>]*>(.*?)</title>') { ($matches[1] -replace '\s+',' ').Trim() } else { $null }
        $desc = if ($html -match '(?is)<meta\s+[^>]*name=["'']description["''][^>]*content=["'']([^"'']+)') { $matches[1].Trim() } else { $null }
        $ogTitle = if ($html -match '(?is)<meta\s+[^>]*property=["'']og:title["''][^>]*content=["'']([^"'']+)') { $matches[1].Trim() } else { $null }
        $ogDesc  = if ($html -match '(?is)<meta\s+[^>]*property=["'']og:description["''][^>]*content=["'']([^"'']+)') { $matches[1].Trim() } else { $null }
        $text = ($clean -replace '(?s)<[^>]+>',' ') -replace '\s+',' '
        $textSnippet = if ($text.Length -gt 6000) { $text.Substring(0, 6000) } else { $text }

        $links = New-Object System.Collections.Generic.List[object]
        foreach ($m in [regex]::Matches($html, '(?is)<a\s+[^>]*href=["'']([^"'']+)["''][^>]*>(.*?)</a>')) {
            $href = $m.Groups[1].Value
            $lbl  = ($m.Groups[2].Value -replace '(?s)<[^>]+>',' ').Trim().ToLowerInvariant()
            if ($lbl -match '\b(about|contact|team|projects|portfolio|services|capabilities|leadership|people|sectors|practice areas|markets|clients|news|press|insights|careers)\b') {
                $links.Add([pscustomobject]@{ Text = $lbl; Href = $href })
                if ($links.Count -ge 30) { break }
            }
        }

        $emails = ([regex]::Matches($text, '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}')) | ForEach-Object { $_.Value } | Sort-Object -Unique | Select-Object -First 15
        $phones = ([regex]::Matches($text, '(?:\+?1[-.\s]?)?\(?(?:[2-9][0-8][0-9])\)?[-.\s]?(?:[2-9][0-9]{2})[-.\s]?(?:[0-9]{4})')) | ForEach-Object { $_.Value.Trim() } | Sort-Object -Unique | Select-Object -First 10
        $addresses = ([regex]::Matches($text, '\d+[^,]{2,80},\s*[A-Z][A-Za-z\s]{2,30},?\s*[A-Z]{2}\s*[A-Z0-9 ]{6,8}')) | ForEach-Object { $_.Value.Trim() } | Sort-Object -Unique | Select-Object -First 5

        return [pscustomobject]@{
            Url           = $r.BaseResponse.RequestMessage.RequestUri.AbsoluteUri
            FetchedAt     = (Get-Date).ToUniversalTime().ToString('o')
            Title         = $title
            OgTitle       = $ogTitle
            Description   = $desc
            OgDescription = $ogDesc
            TextSnippet   = $textSnippet
            UsefulLinks   = $links
            Emails        = @($emails)
            Phones        = @($phones)
            Addresses     = @($addresses)
            HtmlLength    = $html.Length
        }
    } catch {
        return [pscustomobject]@{ Url = $url; Error = $_.Exception.Message; FetchedAt = (Get-Date).ToUniversalTime().ToString('o') }
    }
}

function Get-FollowonPages($homepage, [string]$apex) {
    # Follow up to 5 useful links from the homepage (about/contact/projects/team)
    $empty = [object[]]@()
    if (-not $homepage) { return ,$empty }
    if (-not $homepage.PSObject.Properties['UsefulLinks']) { return ,$empty }
    $links = $homepage.UsefulLinks
    if (-not $links) { return ,$empty }
    $followups = New-Object System.Collections.ArrayList
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($link in $links) {
        if ($followups.Count -ge 5) { break }
        $href = $link.Href
        if ([string]::IsNullOrWhiteSpace($href)) { continue }
        if ($href.StartsWith('mailto:') -or $href.StartsWith('tel:') -or $href.StartsWith('javascript:')) { continue }
        $absolute = $null
        try {
            if ($href -match '^https?://') { $absolute = $href }
            elseif ($href.StartsWith('/')) { $absolute = "https://$apex$href" }
            else { $absolute = "https://$apex/$href" }
        } catch { continue }
        if (-not $absolute) { continue }
        if (-not $seen.Add($absolute)) { continue }
        $page = Get-PageEvidence $absolute 12
        if ($page -and -not $page.PSObject.Properties['Error']) {
            [void]$followups.Add([pscustomobject]@{
                Label = $link.Text
                Page  = $page
            })
        }
    }
    return ,([object[]]$followups.ToArray())
}

function Try-MxLookup([string]$apex) {
    try {
        $mx = Resolve-DnsName -Type MX -Name $apex -DnsOnly -QuickTimeout -ErrorAction Stop 2>$null
        return [pscustomobject]@{ HasMx = $true; Records = @($mx | ForEach-Object { $_.NameExchange } | Select-Object -First 5) }
    } catch {
        return [pscustomobject]@{ HasMx = $false; Records = @() }
    }
}

function Try-Wayback([string]$apex) {
    try {
        $u = "https://archive.org/wayback/available?url=$apex"
        $r = Invoke-RestMethod -Uri $u -TimeoutSec 8 -UserAgent 'Mozilla/5.0 KOR-BD-Intel-Bot' -ErrorAction Stop
        if ($r.archived_snapshots.closest) {
            $ts = $r.archived_snapshots.closest.timestamp
            $parsed = $null
            try { $parsed = [datetime]::ParseExact($ts, 'yyyyMMddHHmmss', $null).ToString('yyyy-MM-dd HH:mm:ss') } catch {}
            return [pscustomobject]@{
                Available     = $true
                Timestamp     = $ts
                TimestampIso  = $parsed
                Url           = $r.archived_snapshots.closest.url
                StatusCode    = $r.archived_snapshots.closest.status
            }
        }
    } catch { }
    return [pscustomobject]@{ Available = $false }
}

function Try-OpenCorporates([string]$name) {
    try {
        $q = [uri]::EscapeDataString($name)
        $u = "https://api.opencorporates.com/v0.4/companies/search?q=$q&jurisdiction_code=ca&order=score"
        $r = Invoke-RestMethod -Uri $u -TimeoutSec 10 -UserAgent 'Mozilla/5.0 KOR-BD-Intel-Bot' -ErrorAction Stop
        $matches = $r.results.companies | Select-Object -First 3 | ForEach-Object {
            [pscustomobject]@{
                Name        = $_.company.name
                Jurisdiction = $_.company.jurisdiction_code
                Status      = $_.company.current_status
                Incorporated = $_.company.incorporation_date
                Url         = $_.company.opencorporates_url
            }
        }
        return [pscustomobject]@{ Found = ($r.results.companies.Count -gt 0); Matches = @($matches) }
    } catch {
        return [pscustomobject]@{ Found = $false; Error = $_.Exception.Message }
    }
}

function Try-FedContracts([string]$name) {
    # Normalize name to govcanadacontracts.ca's URL slug style
    $slug = $name.ToLowerInvariant() -replace "[^a-z0-9]+",'_' -replace '_+','_' -replace '^_|_$',''
    if ([string]::IsNullOrWhiteSpace($slug)) { return [pscustomobject]@{ Found = $false } }
    $u = "https://govcanadacontracts.ca/vendors/$slug/"
    try {
        $r = Invoke-WebRequest -Uri $u -Method Head -TimeoutSec 6 -MaximumRedirection 0 -UserAgent 'Mozilla/5.0 KOR-BD-Intel-Bot' -ErrorAction Stop
        if ([int]$r.StatusCode -eq 200) { return [pscustomobject]@{ Found = $true; Url = $u } }
    } catch { }
    return [pscustomobject]@{ Found = $false }
}

# ----- main -----

if (-not (Test-Path $InputCsv)) {
    Write-Host "Input CSV not found: $InputCsv" -ForegroundColor Red
    Write-Host "Run Step 1 (Sonnet website discovery) first; see tools\BdGatherIntel\Step1-DiscoverWebsites-Prompt.md"
    exit 2
}

$all = Import-Csv $InputCsv
if ($ResumeFromId -gt 0) {
    $all = $all | Where-Object { [long]$_.Id -ge $ResumeFromId }
}
if ($OnlyConfidence) {
    $allowed = ($OnlyConfidence -split ',') | ForEach-Object { $_.Trim().ToLowerInvariant() }
    $all = $all | Where-Object { $allowed -contains ($_.Confidence.ToString().ToLowerInvariant()) }
}
# Only deep-dive rows with a website
$rows = $all | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Website) }
if ($MaxRows -gt 0) { $rows = $rows | Select-Object -First $MaxRows }

Write-Log "BdGatherIntel Step 2 starting. Input rows: $($all.Count); with website: $($rows.Count); resume: $ResumeFromId; confidence: $OnlyConfidence"
Write-Host "Deep-diving $($rows.Count) URLs from $InputCsv ..." -ForegroundColor Cyan

$idx = 0
$succeeded = 0
$failed = 0
foreach ($row in $rows) {
    $idx++
    $id   = [long]$row.Id
    $name = $row.DisplayName
    $url  = $row.Website
    Write-Host ("[{0,4}/{1}] {2,6}  {3}" -f $idx, $rows.Count, $id, $name) -ForegroundColor Cyan

    $probe = Try-Url $url
    if (-not $probe) {
        Write-Log "$id  FAIL HEAD $url"
        $failed++
        continue
    }
    $apex = ([uri]$probe.FinalUrl).Host -replace '^www\.',''
    $homepage = Get-PageEvidence $probe.FinalUrl 15
    $followups = Get-FollowonPages $homepage $apex
    $mx       = Try-MxLookup $apex
    $wayback  = Try-Wayback $apex
    $oc       = Try-OpenCorporates $name
    $fed      = Try-FedContracts $name

    $evidence = [pscustomobject]@{
        id              = $id
        displayName     = $name
        kind            = $row.Kind
        sourceConfidence = $row.Confidence
        sourceNotes      = $row.Notes
        gatheredAtUtc    = (Get-Date).ToUniversalTime().ToString('o')

        website          = $probe.FinalUrl
        websiteApex      = $apex
        homepage         = $homepage
        followonPages    = $followups
        mxLookup         = $mx
        wayback          = $wayback
        openCorporates   = $oc
        federalContracts = $fed
    }

    $outPath = Join-Path $evidenceDir "evidence-$id.json"
    $evidence | ConvertTo-Json -Depth 14 | Set-Content -Path $outPath -Encoding UTF8
    $succeeded++
    Write-Log "$id  OK  $url -> $outPath"
}

# Summary
$total = $rows.Count
$withMx = (Get-ChildItem $evidenceDir -Filter 'evidence-*.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json } | Where-Object { $_.mxLookup.HasMx }).Count
$withWayback = (Get-ChildItem $evidenceDir -Filter 'evidence-*.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json } | Where-Object { $_.wayback.Available }).Count
$withOc = (Get-ChildItem $evidenceDir -Filter 'evidence-*.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json } | Where-Object { $_.openCorporates.Found }).Count
$withFed = (Get-ChildItem $evidenceDir -Filter 'evidence-*.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json } | Where-Object { $_.federalContracts.Found }).Count

@"
# Step 2 — Gather-Intel Summary
**Run:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') UTC
**Input CSV:** $InputCsv
**Output dir:** $evidenceDir

## Counts
- Rows processed: **$total**
- Succeeded: **$succeeded**
- Failed (HEAD or fetch error): **$failed**

## Evidence depth
- MX records present: $withMx
- Wayback snapshot available: $withWayback
- OpenCorporates registration found: $withOc
- Federal contracts presence (govcanadacontracts.ca): $withFed

## Next
- Step 3 (Sonnet polish): batch evidence-*.json into structured enrichment JSON ready for BdResearchImport
- Step 3 alt (drops): tools\BdGatherIntel\Step3-MarkDrops.ps1 marks Notes='WebSearchNotFound:<today>' on rows where Step 1 returned empty Website
"@ | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host ""
Write-Host "Step 2 complete." -ForegroundColor Green
Write-Host "  Succeeded: $succeeded"
Write-Host "  Failed:    $failed"
Write-Host "  Evidence:  $evidenceDir"
Write-Host "  Summary:   $summaryPath"
