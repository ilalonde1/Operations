<#
    Publishes one project's ETABS model and everything that ships beside it, in a single run.

    Staleness is not a discipline problem, it is a ritual problem. Producing the model, the report,
    the questionnaire and the dossier as four separate hand-run steps guarantees that one of them
    lags the others the moment the code changes again — which is exactly what kept happening: the
    model regenerated, the dossier left behind, quoting counts that were two rounds old.

    One command, or it drifts.

    Example:
      .\tools\Publish-EtabsModel.ps1 -Project 31168
#>
[CmdletBinding()]
param(
    # Any job number. Nothing about this command knows which jobs exist.
    [Parameter(Mandatory = $true)][string]$Project,

    # Given only when the job does not follow the usual folder convention.
    [string]$ModelFolder,
    [string]$DxfFolder,
    [string]$Reference,
    [string]$StickFile,

    # A second export of the same drawings that carries its TEXT, from our own Revit bridge.
    # The tags are lifted onto the geometry by solving the transform between the two exports.
    [string]$AnnotatedDxf,
    [string]$RulesDb = $env:KOR_ENGINEERINGTOOLS_STANDARDSDB,

    # Keep this storey and everything below it. For a site model where the engineer wants one
    # building: 31168's YMCA and its parkade is -TopStorey "C-ROOF", which keeps Level 1 entire --
    # both towers' ground floors included, because they sit at grade inside the podium.
    #
    # It does NOT drop the towers. Their floors below the split carry no prefix and stand below the
    # mid-rise's own roof, so an elevation cut keeps every one of them: 31168 went to an engineer
    # with eight storeys of a building she had said was out of scope. Name them in -DropStoreys.
    [string]$TopStorey,

    # Storeys to leave out by name, whatever their height. The blunt instrument, and the only one
    # that reaches a tower whose levels look like the podium's and sit at the mid-rise's elevations.
    # 31168's YMCA is -DropStoreys 'LEVEL 3','LEVEL 4','LEVEL 5','LEVEL 6','LEVEL 7','LEVEL 8','LEVEL 9','LEVEL 10'.
    [string[]]$DropStoreys,

    # One model per building: keep this building's storeys and drop every storey that names a
    # DIFFERENT one. "A-LEVEL 34" names tower A; "LEVEL P1" names nobody and is shared, so it
    # stays. -Tower C is 31168's YMCA.
    #
    # The engineer, unprompted: "it's best if a file only has the elevations relevant to the
    # building modelled", and before that "let's do one model per building". The model published
    # on 24 August carried A-LEVEL 1 and B-LEVEL 1 -- two towers' ground floors -- inside a file
    # she was reviewing as the YMCA. Storeys from another building corrupt every storey-to-storey
    # check she makes, because the stack she is checking is not the stack that exists.
    #
    # This cuts by NAME and cannot reach a storey the drafting left untagged: 31168's LEVEL 3
    # through LEVEL 10 are tower floors called nothing in particular, and they still need
    # -DropStoreys. The two work together; neither replaces the other.
    [string]$Tower,

    # Opt-in only. Give a storey that has members but no floor a plate copied from another storey
    # -- the one whose own plate stands under those members AND is closest to them in shape.
    # Nearest-below was the first rule and it handed 31168's mid-rise the ground floor's site-wide
    # slab, because a slab that spans the whole site covers everything above it. Andrea rejected
    # borrowed slabs on 25 August, so the default publish leaves the missing diaphragm visible
    # instead. If this is used, the plates are reported as INFERRED, because a plate she cannot tell
    # from a measured one is worse than the hole it fills.
    [switch]$InferFloors,

    # A second model from the same job, beside the first rather than over it.
    #
    # 31168 ships the YMCA, its podium and the parkade; the two towers were cut out at the
    # engineer's request and are a separate deliverable she asked for later. Every output name is
    # built from the job number, so a second run would have overwritten the first one silently --
    # the model, the report, the workbook and the summary, all four.
    #
    # A variant publish also leaves the general explainers completely alone. They describe the main
    # model, and a document about a different building sitting next to this one is the exact fault
    # this script spent a day learning to refuse.
    [string]$Variant,

    # One model per building, worked out from the drawings rather than passed in.
    #
    # A site model carries several buildings in one storey list, and the operator had to know the
    # shape of the job to cut it: -Tower C -TopStorey C-ROOF -DropStoreys LEVEL 3..LEVEL 10. That
    # is the tool asking the engineer to know what the tool is looking at, and getting it wrong is
    # silent -- a model went to her carrying eight storeys of a building she had said was out of
    # scope.
    #
    # takeoff dxf-buildings reads which storeys belong to which building: a storey NAMED for one
    # belongs to it, and a storey named for nobody belongs to whichever building's footprint its
    # structure stands inside -- or to all of them, which is what a shared podium or parkade is.
    # This then publishes one model per building, each carrying only its own elevations.
    #
    # The engineer, unprompted: "let's do one model per building", and "it's best if a file only
    # has the elevations relevant to the building modelled".
    [switch]$PerBuilding,

    [switch]$SkipDossier
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

# ---------------------------------------------------------------------------------------------
# Find the job rather than being told about it. This script used to accept exactly two job
# numbers with their folders and reference filenames written out, which made the whole tool a
# script for 31168 and 31138 however general the engine underneath was.
# ---------------------------------------------------------------------------------------------
$projectsRoot = '\\Kor-fs01\Projects\Projects'

if (-not $ModelFolder) {
    $jobFolder = Get-ChildItem $projectsRoot -Directory -ErrorAction Stop |
        ForEach-Object { Get-ChildItem $_.FullName -Directory -Filter "$Project*" -ErrorAction SilentlyContinue } |
        Select-Object -First 1
    if (-not $jobFolder) { throw "No job folder starting with '$Project' under $projectsRoot." }

    $ModelFolder = Get-ChildItem $jobFolder.FullName -Directory -Recurse -Depth 3 -Filter '*ETABS Models*' -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $ModelFolder) { throw "Found $($jobFolder.Name) but no '01 ETABS Models' folder inside it." }
}

if (-not $DxfFolder) {
    # Drafting's plan exports live in a folder named for them, sometimes beside the models and
    # sometimes a level up — 31168 keeps it inside, 31138 outside.
    $searchFrom = Split-Path $ModelFolder -Parent
    $DxfFolder = @(
        Get-ChildItem $ModelFolder -Directory -Filter '*DXF*' -ErrorAction SilentlyContinue
        Get-ChildItem $searchFrom -Directory -Filter '*DXF*' -ErrorAction SilentlyContinue
    ) | Select-Object -First 1 -ExpandProperty FullName
    if (-not $DxfFolder) { throw "No folder with DXF in its name under $ModelFolder or its parent." }
}

if (-not $Reference) {
    # The reference is an engineer's model, never one of ours. Tool output carries KOR-prefixed
    # object names, and a file round-tripped through ETABS keeps them — which is exactly how a
    # generated model once got mistaken for an engineer's own work.
    $candidates = @(Get-ChildItem $ModelFolder -File -ErrorAction Stop |
        Where-Object { $_.Extension -in '.e2k', '.$et' -and $_.Name -notlike '*FROM-DRAWINGS*' } |
        Where-Object {
            $head = Get-Content -LiteralPath $_.FullName -TotalCount 40000 -ErrorAction SilentlyContinue
            -not ($head -match '"K[WCPFSO]\d+"')
        })

    if ($candidates.Count -eq 0) { throw "No engineer-built .e2k or .`$et in $ModelFolder to build from." }

    # Never guess between models. Taking the largest, or the first, silently decides which building
    # gets rebuilt: 31168's folder holds a site reference and a tower-B rebuild within 66 bytes of
    # each other, and the larger one is not the one meant.
    $preferred = @($candidates | Where-Object { $_.BaseName -like '*reference*' })
    if ($preferred.Count -eq 1) { $Reference = $preferred[0].Name }
    elseif ($candidates.Count -eq 1) { $Reference = $candidates[0].Name }
    else {
        throw ("More than one model in $ModelFolder could be the reference, and choosing between them " +
               "is not this script's call: " + (($candidates | ForEach-Object { $_.Name }) -join ', ') +
               ". Re-run with -Reference '<file>'.")
    }
}

if ($StickFile) {
    if (-not (Test-Path -LiteralPath $StickFile -PathType Leaf)) {
        throw "Stick file PDF not found '$StickFile'."
    }
    # ProviderPath, not Path. On a UNC path Resolve-Path's .Path is provider-qualified --
    # "Microsoft.PowerShell.Core\FileSystem::\\Kor-fs01\..." -- which PowerShell understands and
    # .NET does not. Handed that, the CLI opened nothing, matched nothing, and every plate quietly
    # kept the assumed thickness while the run reported success.
    $StickFile = (Resolve-Path -LiteralPath $StickFile).ProviderPath
}
if ($AnnotatedDxf) {
    if (-not (Test-Path -LiteralPath $AnnotatedDxf -PathType Container)) {
        throw "Annotated DXF folder not found '$AnnotatedDxf'."
    }
    # ProviderPath, not Path -- same UNC trap as the stick file.
    $AnnotatedDxf = (Resolve-Path -LiteralPath $AnnotatedDxf).ProviderPath
}

$config = @{ Folder = $ModelFolder; Dxf = $DxfFolder; Reference = $Reference; StickFile = $StickFile; AnnotatedDxf = $AnnotatedDxf }
Write-Host "job $Project" -ForegroundColor DarkGray
Write-Host "  model folder : $ModelFolder" -ForegroundColor DarkGray
Write-Host "  drawings     : $DxfFolder" -ForegroundColor DarkGray
Write-Host "  reference    : $Reference" -ForegroundColor DarkGray
if ($StickFile) { Write-Host "  stick file   : $StickFile" -ForegroundColor DarkGray }
if (-not $RulesDb) { throw "KOR_ENGINEERINGTOOLS_STANDARDSDB is not set; refusing to publish a model from built-in rules." }

$folder = $config.Folder

# Every artefact this run owns is named from $label; the dossier gate still keys on $Project,
# because the explainers describe the job's main model whichever variant is being built.
$label = if ($Variant) { "$Project-$($Variant.Trim().ToUpperInvariant())" } else { $Project }

# NOTHING IS WRITTEN INTO THE JOB FOLDER UNTIL IT HAS PASSED.
#
# The model used to be generated straight into the engineer's folder and checked afterwards, which
# makes every check a note rather than a gate: eight tower storeys, a 132-inch wall, four carried-in
# members and a site-wide plate all reached that folder with the checks running after they landed.
# Generation goes to a staging folder now, verify-e2k runs against the finished file, and only a
# model that passes is copied across. A failure leaves the engineer's folder exactly as it was.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) "kor-publish-$label"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

$out    = Join-Path $stage "$label-FROM-DRAWINGS.e2k"
if ($Variant) { Write-Host "  variant      : $label" -ForegroundColor DarkGray }

# The CLI is not rebuilt by `dotnet test`, so a stale exe silently publishes yesterday's rules.
Write-Host 'building the CLI...' -ForegroundColor DarkGray
& dotnet build (Join-Path $repo 'Kor.Operations.EngineeringTools.TakeoffCli') --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }

$cli = Join-Path $repo 'Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe'

# One model per building: ask the drawings which storeys belong to which, then publish each as its
# own variant. Everything below runs unchanged for each one.
if ($PerBuilding -and -not $Variant) {
    $refPath = Join-Path $folder $config.Reference
    $everyStorey = @(Select-String -Path $refPath -Pattern '^\s*STORY\s+"([^"]+)"' |
        ForEach-Object { $_.Matches[0].Groups[1].Value } |
        Where-Object { $_ -ne 'Base' })

    $split = & $cli dxf-buildings $config.Dxf $refPath
    if ($LASTEXITCODE -ne 0) { throw 'dxf-buildings failed.' }

    $any = $false
    foreach ($line in $split) {
        $parts = $line -split "`t", 2
        if ($parts.Count -lt 2) { continue }
        $tag = $parts[0]
        $mine = $parts[1] -split ',' | Where-Object { $_ }
        $drop = @($everyStorey | Where-Object { $mine -notcontains $_ })

        Write-Host ''
        Write-Host "building $tag : $($mine.Count) storey(s), dropping $($drop.Count)" -ForegroundColor Cyan

        # -Tower AND -DropStoreys, because they answer different halves of this.
        #
        # -Tower cuts by NAME: it drops the storeys belonging to other buildings, and keeps the
        # shared base BELOW this one -- 31168's ground floor is drafted twice, as A-LEVEL 1 and
        # B-LEVEL 1 1.7 in apart, and building C stands on it. Without that, C comes out with no
        # ground floor at all.
        #
        # -DropStoreys reaches what a name cannot: LEVEL 3 to LEVEL 26 are tower floors called
        # nothing in particular, and only the footprint knows they are not the YMCA's.
        $forward = @{ Project = $Project; Reference = $config.Reference; ModelFolder = $ModelFolder
                      DxfFolder = $DxfFolder; RulesDb = $RulesDb; Variant = $tag; Tower = $tag }
        $dropUntagged = @($drop | Where-Object { $_ -notmatch '^[A-Za-z]-' })
        if ($dropUntagged.Count -gt 0) { $forward.DropStoreys = $dropUntagged }
        if ($InferFloors) { $forward.InferFloors = $true }
        if ($config.StickFile) { $forward.StickFile = $config.StickFile }
        if ($config.AnnotatedDxf) { $forward.AnnotatedDxf = $config.AnnotatedDxf }

        & $PSCommandPath @forward
        if ($LASTEXITCODE -ne 0) { throw "publishing building $tag failed." }
        $any = $true
    }

    if (-not $any) { throw 'dxf-buildings named no buildings.' }
    return
}

Write-Host "generating $Project..." -ForegroundColor DarkGray
$cutArgs = @()
if ($TopStorey) { $cutArgs += @('--top-storey', $TopStorey) }
if ($DropStoreys) { $cutArgs += @('--drop-storeys', ($DropStoreys -join ',')) }
if ($Tower) { $cutArgs += @('--tower', $Tower) }
if ($InferFloors) { $cutArgs += '--infer-floors' }
if ($config.StickFile) { $cutArgs += @('--stick-file', $config.StickFile) }
if ($config.AnnotatedDxf) { $cutArgs += @('--annotated-dxf', $config.AnnotatedDxf) }

& $cli dxf-to-etabs $config.Dxf (Join-Path $folder $config.Reference) $out `
    @cutArgs `
    --rules-db $RulesDb `
    --report (Join-Path $stage "$label-FROM-DRAWINGS-report.txt") `
    --questions (Join-Path $stage "$label-QUESTIONS.xlsx") |
    Select-String -Pattern 'Storeys built|^Walls|^Columns|^Floors'
if ($LASTEXITCODE -ne 0) { throw 'generation failed.' }

# ---------------------------------------------------------------------------------------------
# The gate. Structural invariants, checked against the finished file, before it can reach anyone.
# ---------------------------------------------------------------------------------------------
$verifyArgs = @()
if ($DropStoreys) { $verifyArgs += @('--dropped', ($DropStoreys -join ',')) }

# The reference goes in so the invariants judge what THIS TOOL built. On a gap-fill job the
# engineer's own model is carried through into the output, and hers is not ours to refuse:
# 31138 failed 514 checks, every one of them her work.
$verifyArgs += @('--reference', (Join-Path $folder $config.Reference))
& $cli verify-e2k $out @verifyArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host "REFUSED — the model did not pass, and nothing was written to the job folder." -ForegroundColor Red
    Write-Host "  the file that failed is at $out" -ForegroundColor DarkGray
    exit 1
}

# ---------------------------------------------------------------------------------------------
# A summary of THIS job, written from this job's own model and report.
#
# The dossier is a deep explainer for the two jobs it names and does not generalise. Without
# something in its place a third job arrives as a bare .e2k and a text report, and the engineer
# has to read the whole report to learn whether anything is missing. This is the page that says
# what was built and what was not, in the job's own numbers, so it cannot be wrong about a
# building it is not describing.
# ---------------------------------------------------------------------------------------------
$reportPath = Join-Path $stage "$label-FROM-DRAWINGS-report.txt"
$model      = Get-Content -LiteralPath $out
$counts = [ordered]@{
    # Storeys the building has, not rows in the list: the base carries an elevation, not a height.
    'Storeys populated' = @($model | Select-String '^\s+STORY\s+"[^"]+"\s+HEIGHT').Count
    'Wall panels'       = @($model | Select-String '^\s+AREA\s+"KW\d+"\s+PANEL').Count
    'Columns'           = @($model | Select-String '^\s+LINE\s+"KC\d+"\s+COLUMN').Count
    # Plate OBJECTS, and a storey that borrows one is the donor's object assigned a second time.
    # So this is legitimately lower than the storey count, and reads exactly like a storey that
    # lost its floor -- it cost me ten minutes on 24 Aug and would cost an engineer more. The
    # floored-storey count goes beside it so nobody has to work that out.
    'Floor plates'      = @($model | Select-String '^\s+AREA\s+"KF\d+"\s+FLOOR').Count
}

# How many storeys actually carry a floor, which is the number a reader means when they compare
# "floor plates" against "storeys populated" and find one missing.
$floorNames = @($model | Select-String '^\s+AREA\s+"(KF\d+)"\s+FLOOR' |
    ForEach-Object { $_.Matches[0].Groups[1].Value })
$flooredStoreys = @($model | Select-String '^\s+AREAASSIGN\s+"(KF\d+)"\s+"([^"]+)"' |
    Where-Object { $floorNames -contains $_.Matches[0].Groups[1].Value } |
    ForEach-Object { $_.Matches[0].Groups[2].Value } |
    Sort-Object -Unique).Count

# Shown only when it differs, and it differs exactly when a storey borrowed a plate. Printing
# "14 plates, 15 storeys floored" unasked answers the question before it is asked; printing
# "14 plates" alone leaves a reader to conclude a storey lost its floor, which is what I did.
if ($flooredStoreys -gt $counts['Floor plates']) { $counts['Storeys with a floor'] = $flooredStoreys }

$counts['Headers']      = @($model | Select-String '^\s+AREA\s+"KS\d+"\s+PANEL').Count
$counts['Openings cut'] = @($model | Select-String '^\s+AREA\s+"KO\d+"\s+AREA').Count

# Everything the run declined to do, in its own words. These lines are the honest half of the
# page and are taken verbatim rather than summarised, because summarising is where they soften.
$notModelled = @()
if (Test-Path $reportPath) {
    $flags = Select-String -Path $reportPath -Pattern '^\s+- ' |
        ForEach-Object { $_.Line.Trim().TrimStart('-').Trim() } |
        Where-Object { $_ -match 'not |no |could not|were |outside|drawn more than once|beneath' }

    # A flag naming a sheet repeats once per sheet. Twelve lines saying the same thing about
    # twelve drawings is not a summary of anything — it is the report again, and the reader
    # stops. Model-wide findings are shown as written; per-sheet ones are grouped by what they
    # SAY, with the sheet count and the total in front.
    $modelWide = @($flags | Where-Object { $_ -notmatch '\.dxf:' })
    $perSheet  = @($flags | Where-Object { $_ -match '\.dxf:' })

    $grouped = $perSheet |
        ForEach-Object {
            $text = ($_ -replace '^.*?\.dxf:\s*', '')
            [pscustomobject]@{
                Shape = ($text -replace '\d[\d,]*', '#')
                Total = [int](([regex]::Match($text, '^(\d[\d,]*)')).Groups[1].Value -replace ',', '')
                Text  = $text
            }
        } |
        Group-Object Shape |
        ForEach-Object {
            $sum = ($_.Group | Measure-Object Total -Sum).Sum
            $one = $_.Group[0].Text
            if ($_.Count -eq 1) { $one }
            else { "$sum across $($_.Count) drawings: " + ($one -replace '^\d[\d,]*\s*', '') }
        }

    $all = @($modelWide) + @($grouped)

    # First sentence each, and a line saying where the rest of the words are.
    #
    # Taken verbatim, ten of these ran to 37 lines and pushed a one-page summary onto a second
    # page -- and page two of a one-page summary is where the thing nobody reads lives. The
    # softening this comment used to warn about comes from PARAPHRASING; a first sentence is the
    # finding in the tool's own words, and the report beside it carries every one in full.
    $firstSentence = {
        param($count)
        $all | Select-Object -First $count | ForEach-Object {
            $m = [regex]::Match($_, '^(.+?[.!])(\s|$)')
            if ($m.Success -and $m.Groups[1].Value.Length -lt $_.Length) { $m.Groups[1].Value + ' …' } else { $_ }
        }
    }
    $findingsShown = 8
    $notModelled = & $firstSentence $findingsShown
    $trimmedAway = $all.Count - @($notModelled).Count
}

$esc = { param($t) [System.Net.WebUtility]::HtmlEncode([string]$t) }

# The page is built inside a scriptblock so it can be built AGAIN with fewer findings listed.
# The findings are the only part whose length varies, and the page has to come out one page.
$buildSummaryHtml = {
    $html = New-Object System.Collections.Generic.List[string]
    $html.Add('<title>' + (& $esc "$Project - model from drawings") + '</title>')
    # Sized to fit ONE page, because it is called a one-page summary and was running to two. A second
    # page is where the thing nobody read lives.
    $html.Add('<style>body{font:12.5px/1.42 "Segoe UI",system-ui,sans-serif;max-width:46rem;margin:0 auto;padding:20px 26px;color:#1a1a1a}h1{font-size:19px;margin:0 0 2px;font-weight:650}.sub{color:#5b5b5b;font-size:11.5px;margin:0 0 12px}h2{font-size:12px;text-transform:uppercase;letter-spacing:.08em;color:#7a2230;margin:14px 0 5px}table{border-collapse:collapse;width:100%;font-size:12.5px}td{padding:2px 8px 2px 0;border-bottom:1px solid #eeeae5}td.n{text-align:right;font-variant-numeric:tabular-nums;font-weight:600}li{margin:0 0 3px}ul{margin:4px 0;padding-left:18px}p{margin:5px 0}code{background:#f4f2ef;padding:1px 4px;border-radius:3px;font-size:11.5px}</style>')
    $html.Add('<h1>' + (& $esc $label) + ' &mdash; model from drawings</h1>')
    $html.Add('<p class="sub">Generated ' + (Get-Date -Format 'd MMMM yyyy') + ' from ' + (& $esc (Split-Path $DxfFolder -Leaf)) + ', on top of ' + (& $esc $Reference) + '. It removes the typing; it does none of the engineering.</p>')
    $html.Add('<h2>What was built</h2><table>')
    foreach ($k in $counts.Keys) { $html.Add('<tr><td>' + (& $esc $k) + '</td><td class="n">' + ('{0:N0}' -f $counts[$k]) + '</td></tr>') }
    $html.Add('</table>')

    if ($notModelled.Count) {
        $html.Add('<h2>What was not, and why</h2><ul>')
        foreach ($n in $notModelled) { $html.Add('<li>' + (& $esc $n) + '</li>') }
        $html.Add('</ul>')
        if ($trimmedAway -gt 0) {
            # Never a silent truncation. A page that shows eight of eleven findings without saying so
            # reads as "these are the findings".
            $html.Add('<p class="sub">Shortened to the first sentence of each, and ' + $trimmedAway + ' further finding(s) are not listed here. All of them appear in full in <code>' + (& $esc "$label-FROM-DRAWINGS-report.txt") + '</code>.</p>')
        }
    }

    $html.Add('<h2>What it did not touch</h2><p>No loads, diaphragms, stiffness modifiers, section properties, meshing or design &mdash; those are yours. Geometry already in your model was recognised and left alone rather than duplicated.</p>')
    # "Nothing there is waiting on you" was written into this page once and then left, while the
    # workbook beside it opened with three NEEDS YOU rows. The engineer reads this page first, so the
    # page told her there was nothing to do and the workbook told her there were three things. Take the
    # count from the report, which takes it from the same code that writes the workbook.
    $openQuestions = $null
    if (Test-Path $reportPath) {
        $m = Select-String -Path $reportPath -Pattern '^Questions for you:\s*(\d+)' | Select-Object -First 1
        if ($m) { $openQuestions = [int]$m.Matches[0].Groups[1].Value }
    }

    $waiting = if ($null -eq $openQuestions) {
        'See that workbook for what is still open.'
    } elseif ($openQuestions -eq 0) {
        'Nothing there is waiting on you.'
    } elseif ($openQuestions -eq 1) {
        'One row is marked NEEDS YOU &mdash; nothing in the drawings could settle it.'
    } else {
        "$openQuestions rows are marked NEEDS YOU &mdash; nothing in the drawings could settle them."
    }

    $html.Add('<h2>What it decided for you</h2><p>Every judgement it had to make is listed in <code>' + (& $esc "$label-QUESTIONS.xlsx") + '</code>, each with the measurement behind it beside it. ' + $waiting + ' Rows tied to a rule can be changed from the answer cell, and that becomes the rule for every job afterwards &mdash; you are asked once. Rows without a rule key are visible scope decisions, not yet learnable settings. A second sheet lists every rule this model was built on, read-only, including the geometry tolerances no decision asks about.</p>')
    $html.Add('<p class="sub" style="margin-top:22px">Location by location, the full account is in <code>' + (& $esc "$label-FROM-DRAWINGS-report.txt") + '</code>.</p>')
    $html
}

$html = & $buildSummaryHtml

$summaryHtml = Join-Path $env:TEMP "kor-summary-$label.html"
$summaryPdf  = Join-Path $stage "KOR-$label-SUMMARY.pdf"

$pdfinfo = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter 'pdfinfo.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

# It is called a one-page summary in every document that mentions it, and it had quietly become
# two. Page two of a one-page summary is where the thing nobody reads lives, so the claim is
# checked rather than trusted -- and now MET rather than merely checked: the findings list is the
# only part of this page whose length varies, so it is shortened until the page is one page.
#
# It used to refuse instead, which is right for a wrong number and wrong for a long one. 31168's
# two towers are 63 storeys and their findings run longer than the mid-rise's, so a publish that
# was correct in every other respect was blocked by its own covering note. Whatever is dropped is
# still counted and named as dropped, and every finding is in the report in full.
$pages = 1
foreach ($tryCount in 8, 6, 4, 3, 2) {
    if ($tryCount -ne $findingsShown) {
        $findingsShown = $tryCount
        $notModelled = & $firstSentence $findingsShown
        $trimmedAway = $all.Count - @($notModelled).Count
        $html = & $buildSummaryHtml
    }

    $html -join "`n" | Set-Content -LiteralPath $summaryHtml -Encoding UTF8
    & (Join-Path $PSScriptRoot 'Format-BdWebPdf.ps1') -Html $summaryHtml -Pdf $summaryPdf | Out-Null

    # The temp HTML is left where it is. Deleting it the instant the renderer returns is a race the
    # renderer loses on a slow run, and what lands in the job folder is a PDF of the browser's
    # "file not found" page -- which looks like a document until somebody opens it.
    if (-not (Test-Path $summaryPdf)) { throw "the per-job summary did not render: $summaryPdf" }

    if (-not $pdfinfo) { break }
    $pages = [int](& $pdfinfo $summaryPdf | Select-String '^Pages:\s*(\d+)').Matches[0].Groups[1].Value
    if ($pages -le 1) { break }
}

Write-Host "  summary      : $(Split-Path $summaryPdf -Leaf)" -ForegroundColor DarkGray
if ($pdfinfo) {
    if ($pages -gt 1) {
        Write-Host ''
        Write-Host "The one-page summary is $pages pages even with only $findingsShown finding(s) listed." -ForegroundColor Red
        Write-Host '  Either it fits on one page or it stops being called a one-page summary.' -ForegroundColor Red
        exit 1
    }
    Write-Host "  summary pages: $pages" -ForegroundColor DarkGray
    if ($findingsShown -lt 8) {
        Write-Host "  findings shown: $findingsShown (shortened to fit one page; all are in the report)" -ForegroundColor DarkGray
    }
}

if ((-not $SkipDossier) -and (-not $Variant)) {
    # The dossier and the one-pager describe particular buildings by name. Copying them beside a
    # job they do not describe hands the engineer a document about somebody else's tower, and it
    # is the kind of thing nobody notices until it is in front of a client: the counts look
    # authoritative, the prose reads well, and none of it is about the model it sits next to.
    #
    # So the documents travel only to the jobs they actually name. On a job they do not, the model
    # and the report still publish — those are generated FROM this job and are always true of it.
    $describes = @()
    $dossierSource = Join-Path $repo 'docs\KOR-DxfToEtabs-dossier.html'
    if (Test-Path $dossierSource) {
        $describes = [regex]::Matches((Get-Content -LiteralPath $dossierSource -Raw), '\b(3\d{4})\b') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    }

    if ($describes -notcontains $Project) {
        Write-Host ''
        Write-Host "The dossier and one-pager describe $($describes -join ', ') — not $Project." -ForegroundColor Yellow
        Write-Host "  Not copied. The model, report and questions are this job's own and have published." -ForegroundColor Yellow
        Write-Host "  Write a dossier for $Project, or publish with -SkipDossier to say so deliberately." -ForegroundColor Yellow
        $SkipDossier = $true
    }
}

# The copy used to happen HERE, and the count check ran afterwards against the copy. So the run
# that discovered the dossier was three model-revisions out of date had already put it in the
# engineer's folder, and exiting 1 left it there: a document claiming 1,119 walls and 63 storeys
# sat beside a 349-wall, 15-storey model for nine days, saying "the model is fine, the document
# describing it is not" to a terminal nobody was reading.
#
# A check that runs after the copy is not a gate, it is a note. Nothing is copied now until the
# counts have been checked against the SOURCE, and a source that fails takes its stale copy out of
# the folder with it.
$dossierSourcePdf = Join-Path $repo 'docs\KOR-DxfToEtabs-web.pdf'
$onePagerSourcePdf = Join-Path $repo 'docs\KOR-DxfToEtabs-onepager-web.pdf'
$dossierTarget = Join-Path $folder 'KOR-Model-From-Drawings-DOSSIER.pdf'
$onePagerTarget = Join-Path $folder 'KOR-Model-From-Drawings-READ-THIS-FIRST.pdf'

# The dossier quotes counts, and they are written by hand. A timestamp check cannot see a wrong
# number in a current file — 31138 shipped with the dossier claiming 162 columns against 165 in the
# model, and 5 headers against 8. Every count it states must appear in the model it describes.
$dossier = $dossierSourcePdf
$pdftotext = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter 'pdftotext.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

if ((-not $SkipDossier) -and (-not $Variant) -and (Test-Path $dossier) -and $pdftotext) {
    $model = Get-Content -LiteralPath $out
    $actual = [ordered]@{
        walls    = @($model | Select-String '^\s+AREA\s+"KW\d+"\s+PANEL').Count
        columns  = @($model | Select-String '^\s+LINE\s+"KC\d+"\s+COLUMN').Count
        plates   = @($model | Select-String '^\s+AREA\s+"KF\d+"\s+FLOOR').Count
        headers  = @($model | Select-String '^\s+AREA\s+"KS\d+"\s+PANEL').Count
        openings = @($model | Select-String '^\s+AREA\s+"KO\d+"\s+AREA').Count
    }
    # -layout, or pdftotext interleaves the table's cells with the paragraph beside it and a
    # claim like '2,418 columns' arrives as '2,418 31138-FROM-DRAWINGS.e2k', which no pattern
    # can catch. That hole hid a stale column count from this check.
    $text = ((& $pdftotext -layout $dossier -) -join ' ') -replace '\s+', ' '

    $wrong = @()
    foreach ($k in $actual.Keys) {
        $n = $actual[$k]
        if ($n -eq 0) { continue }
        $plain = [string]$n
        $grouped = '{0:N0}' -f $n
        if ($text -notmatch ("\b" + [regex]::Escape($plain) + "\b") -and
            $text -notmatch ("\b" + [regex]::Escape($grouped) + "\b")) {
            $wrong += "$k = $n"
        }
    }

    # The check above only asks whether the true number appears SOMEWHERE. That passes a document
    # whose table is right and whose prose is two rounds old, which is exactly what shipped: the
    # table said 900 walls and 2,407 columns while the paragraph below it said 897 and 2,418.
    #
    # So every "<number> <member>" phrase has to be a number one of the two models actually
    # produces. That is read from the HTML the PDF is built from, not from the PDF: pdftotext
    # interleaves the file-name column with the paragraph beside it, so "2,418 columns" arrives as
    # "2,418 DRAWINGS.e2k columns" and no adjacency pattern can see it. The check above already
    # proves the PDF carries the true numbers, which is what catches a PDF that was never rebuilt.
    $source = Join-Path $repo 'docs\KOR-DxfToEtabs-dossier.html'
    $script:reuse = @()
    $counts = @{}
    # Only the jobs the dossier actually names. Walking the whole Projects share to find every
    # generated model takes minutes over the network and grows with the company; the document
    # states which buildings it describes, so those are the ones its numbers are checked against.
    $named = @()
    if (Test-Path $source) {
        $named = [regex]::Matches((Get-Content -LiteralPath $source -Raw), '\b(3\d{4})\b') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    }
    foreach ($job in $named) {
        $jf = Get-ChildItem $projectsRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Get-ChildItem $_.FullName -Directory -Filter "$job*" -ErrorAction SilentlyContinue } |
            Select-Object -First 1
        if (-not $jf) { continue }
        # For the job being published, check the dossier against the model STAGED by this run --
        # not the one sitting in the folder, which is whatever last succeeded.
        #
        # Reading the landed model deadlocks the moment the two differ: generation moved to a
        # staging folder so nothing lands until it passes, and this gate then compared the new
        # dossier against the OLD model and refused, so the new model could never land to make the
        # dossier true. The 7 roof columns sat in staging through two publishes for exactly this.
        $mf = $null
        if ($job -eq $Project -and (Test-Path $out)) { $mf = Get-Item $out }
        if (-not $mf) {
            $mf = Get-ChildItem $jf.FullName -File -Recurse -Depth 4 -Filter "$job-FROM-DRAWINGS.e2k" -ErrorAction SilentlyContinue |
                Select-Object -First 1
        }
        if (-not $mf) { continue }
        $mm = Get-Content -LiteralPath $mf.FullName
        $counts["$job.wall"]   = @($mm | Select-String '^\s+AREA\s+"KW\d+"\s+PANEL').Count
        $counts["$job.column"] = @($mm | Select-String '^\s+LINE\s+"KC\d+"\s+COLUMN').Count
        $counts["$job.plate"]  = @($mm | Select-String '^\s+AREA\s+"KF\d+"\s+FLOOR').Count
        $counts["$job.header"] = @($mm | Select-String '^\s+AREA\s+"KS\d+"\s+PANEL').Count

        $rp = if ($job -eq $Project) { $reportPath } else { Join-Path $mf.DirectoryName "$job-FROM-DRAWINGS-report.txt" }
        if (Test-Path $rp) {
            $rm = [regex]::Match((Get-Content -LiteralPath $rp -Raw),
                                 '(?<w>\d+) wall\(s\) and (?<c>\d+) column\(s\) were already modelled')
            if ($rm.Success) { $script:reuse += @([int]$rm.Groups['w'].Value, [int]$rm.Groups['c'].Value) }
        }
    }

    # Numbers in the document that are true of something other than a member count, each allowed
    # for a stated reason. Anything not here and not a model count is a stale claim.
    $allowed = @{
        wall   = @(78, 29, 45, 60, 22) # 78: the wafer fault, a count of what was wrong at the time.
                                    # 29: walls the DRAWINGS show at L01, corroborating the lost model.
                                    # 45: what one 31168 storey gained when the mezzanine fault was fixed.
                                    # 60: what 31138 gained when P4 and P5 were added to its storey list.
                                    # 22: what 31168 gained when outlines stopped welding across layer families
        column = @(87, 67, 43)      # 87: her own hand-placed columns on 31138, validating placement.
                                    # 67: the same 31168 storey's columns, from that same fix.
                                    # 43: the columns P4 and P5 brought with them
        plate  = @(14, 7, 2)        # 14, 7: the sliver plates the 400 sq ft rule removed.
                                    # 2: the plates P4 and P5 brought with them
        header = @(5)   # 5: the headers that came back with those same 22 walls
    }

    # The engineer's own members that were recognised and skipped are a real count the dossier
    # quotes, but they are hers, not ours, so they match no generated total. They are taken from
    # the report rather than allowlisted, so the dossier is checked against a generated number.
    foreach ($v in $script:reuse) { $allowed.wall += $v; $allowed.column += $v }

    if (Test-Path $source) {
        $html = Get-Content -LiteralPath $source -Raw
        $prose = (($html -replace '(?s)<style.*?</style>', ' ' -replace '(?s)<script.*?</script>', ' ' `
                        -replace '<[^>]+>', ' ') -replace '&[a-z]+;', ' ') -replace '\s+', ' '

        # Prose: "<number> <member>", "<number> of your <member>", and "<member>: <number>" —
        # each reads as a count to an engineer. The member-first form matters because summaries
        # often phrase counts as labels.
        $countClaimPattern = '(?:(?<n>\d[\d,]*)\s+(?:of\s+(?:your|her|its|the)\s+)?(?<what>wall panels|walls|columns|floor plates|plates|headers))|(?:(?<what2>wall panels|walls|columns|floor plates|plates|headers)\s*[:(]\s*(?<n2>\d[\d,]*))'

        # Prose: "<number> <member>", and "<number> of your <member>" — which reads as a count to
        # anyone but a pattern that demands adjacency. That hole shipped: two sentences said "315 of
        # your columns" while the table three lines up said 316, and the report says 316. The gate
        # existed precisely to catch that and could not see the sentence.
        foreach ($c in [regex]::Matches($prose, $countClaimPattern)) {
            $nText = if ($c.Groups['n'].Success) { $c.Groups['n'].Value } else { $c.Groups['n2'].Value }
            $whatText = if ($c.Groups['what'].Success) { $c.Groups['what'].Value } else { $c.Groups['what2'].Value }
            $n = [int]($nText -replace ',', '')
            $what = switch -Regex ($whatText) {
                'wall'   { 'wall' }; 'column' { 'column' }; 'plate' { 'plate' }; 'header' { 'header' }
            }
            $ok = $counts.GetEnumerator() | Where-Object { $_.Key -like "*.$what" -and $_.Value -eq $n }
            if (-not $ok -and $allowed[$what] -notcontains $n) {
                $wrong += "dossier says '$($c.Value)' — no model has that many $what`s"
            }
        }

        # The dossier names the storeys left without a floor plate. That list is generated into the
        # report, and a code change moves it: fixing the mezzanine fault gave 31168 a fourth such
        # storey, and the dossier still named three. Every storey the report lists must be named.
        #
        # The dossier says whose storeys it is listing — "on 31168 six" — so which job this check
        # applies to is read from that sentence rather than named in code. With the job hardwired,
        # rewriting the section for a different building would have silently stopped checking it.
        $plateless = Join-Path $folder "$Project-FROM-DRAWINGS-report.txt"
        $listedFor = [regex]::Match($prose,
            'Storeys still carrying members with no plate[^0-9]*\b(?<job>3\d{4})\b')
        if ((Test-Path $plateless) -and $listedFor.Success -and $listedFor.Groups['job'].Value -eq $Project) {
            $pm = [regex]::Match((Get-Content -LiteralPath $plateless -Raw),
                                 'carry walls or columns but no floor plate[^:]*:\s*(?<list>[^.]+)\.')
            if ($pm.Success) {
                foreach ($s in ($pm.Groups['list'].Value -split ',\s*')) {
                    $storey = $s.Trim()
                    if ($storey -and $prose -notmatch [regex]::Escape($storey)) {
                        $wrong += "dossier does not name '$storey' among the storeys left without a plate"
                    }
                }
            }
        }

        # One suite, one number. The document described the same test suite as "410 tests" in one
        # paragraph and "384 tests" in another; both were wrong, and neither could be right at once.
        $suite = [regex]::Matches($prose, '(?<n>\d[\d,]*)\s+tests') |
            ForEach-Object { [int](($_.Groups['n'].Value) -replace ',', '') } | Sort-Object -Unique
        if ($suite.Count -gt 1) {
            $wrong += "dossier states more than one test count: $($suite -join ', ')"
        }

        # The summary table, where the label comes first and each project's number follows. Which
        # column belongs to which building is read from the table's own header row, not assumed:
        # this used to say "left is 31168, right is 31138" in code, so reordering the columns or
        # adding a third job would have checked every number against the wrong building and passed.
        $rows = @{
            'Wall panels'              = 'wall'
            'Columns, sized'           = 'column'
            'Floor plates'             = 'plate'
            'Headers over openings'    = 'header'
        }

        $tableJobs = @()
        $hm = [regex]::Match($html, '(?s)<tr>\s*<th>What was generated</th>(?<cells>.*?)</tr>')
        if ($hm.Success) {
            $tableJobs = @([regex]::Matches($hm.Groups['cells'].Value, '<th>[^<]*?\b(3\d{4})\b') |
                ForEach-Object { $_.Groups[1].Value })
        }
        if ($tableJobs.Count -eq 0) {
            $wrong += "dossier summary table does not name the jobs its columns describe"
        }
        else {
            foreach ($label in $rows.Keys) {
                $what = $rows[$label]
                $pattern = [regex]::Escape($label) + '[^0-9]*' +
                           ((1..$tableJobs.Count | ForEach-Object { '([\d,]+)' }) -join '\s+')
                $m = [regex]::Match($prose, $pattern)
                if (-not $m.Success) { $wrong += "dossier table has no '$label' row"; continue }
                for ($i = 0; $i -lt $tableJobs.Count; $i++) {
                    $job    = $tableJobs[$i]
                    $stated = [int](($m.Groups[$i + 1].Value) -replace ',', '')
                    $true_  = $counts["$job.$what"]
                    if ($null -ne $true_ -and $stated -ne $true_) {
                        $wrong += "dossier table: $label for $job says $stated, model has $true_"
                    }
                }
            }
        }
    }

    # The one-pager is checked by the same rule, against the PDF that would ship. Reading the source
    # HTML proves only that the input was right; it says nothing about a stale or failed render.
    $onePagerPdf = $onePagerSourcePdf
    if (Test-Path $onePagerPdf) {
        $opProse = ((& $pdftotext -layout $onePagerPdf -) -join ' ') -replace '\s+', ' '
        if ($opProse -match 'ERR_FILE_NOT_FOUND|File not found|Microsoft Edge') {
            $wrong += "one-pager PDF renders as a browser error page"
        }

        $opClaims = [regex]::Matches($opProse, $countClaimPattern)
        if ($opClaims.Count -eq 0) {
            $wrong += "one-pager PDF contains no checked model count claims"
        }

        foreach ($c in $opClaims) {
            $nText = if ($c.Groups['n'].Success) { $c.Groups['n'].Value } else { $c.Groups['n2'].Value }
            $whatText = if ($c.Groups['what'].Success) { $c.Groups['what'].Value } else { $c.Groups['what2'].Value }
            $n = [int]($nText -replace ',', '')
            $what = switch -Regex ($whatText) {
                'wall'   { 'wall' }; 'column' { 'column' }; 'plate' { 'plate' }; 'header' { 'header' }
            }
            $ok = $counts.GetEnumerator() | Where-Object { $_.Key -like "*.$what" -and $_.Value -eq $n }
            if (-not $ok -and $allowed[$what] -notcontains $n) {
                $wrong += "one-pager says '$($c.Value)' — no model has that many $what`s"
            }
        }
    }

    if ($wrong) {
        Write-Host ''
        Write-Host 'DOSSIER OUT OF DATE — these counts are not in it:' -ForegroundColor Red
        $wrong | ForEach-Object { Write-Host ("  " + $_) -ForegroundColor Red }
        Write-Host '  (the model is fine; the document describing it is not)' -ForegroundColor Red

        # It did not ship, and any copy an earlier run left behind goes with it. Leaving it there
        # is how a 1,119-wall document ended up beside a 349-wall model in front of an engineer.
        foreach ($t in @($dossierTarget, $onePagerTarget)) {
            if (Test-Path $t) {
                Remove-Item -LiteralPath $t -Force
                Write-Host "  withdrawn from the job folder: $(Split-Path $t -Leaf)" -ForegroundColor Yellow
            }
        }
        exit 1
    }

    # Checked, and only now copied.
    Copy-Item $dossierSourcePdf $dossierTarget -Force
    if (Test-Path $onePagerSourcePdf) { Copy-Item $onePagerSourcePdf $onePagerTarget -Force }
}
elseif ($SkipDossier -and (-not $Variant)) {
    # -SkipDossier says "do not ship the explainer". A copy left from a run that did ship it makes
    # that a lie, so saying no removes it rather than merely declining to refresh it.
    foreach ($t in @($dossierTarget, $onePagerTarget)) {
        if (Test-Path $t) {
            Remove-Item -LiteralPath $t -Force
            Write-Host "  withdrawn from the job folder: $(Split-Path $t -Leaf)" -ForegroundColor Yellow
        }
    }
}

# ---------------------------------------------------------------------------------------------
# Every gate has passed. Only now does anything cross into the engineer's folder.
# ---------------------------------------------------------------------------------------------
$landed = @()
foreach ($f in Get-ChildItem $stage -File) {
    Copy-Item $f.FullName (Join-Path $folder $f.Name) -Force
    $landed += $f.Name
}
Write-Host ''
Write-Host "  landed: $($landed.Count) file(s)" -ForegroundColor DarkGray

# The workbook was named QUESTIONS-for-Andrea.xlsx until 24 Aug -- one engineer's first name on
# every job, and the one-pager already called it QUESTIONS.xlsx. The old copy would otherwise sit
# beside the new one, and two workbooks is worse than a badly named one.
$super = Join-Path $folder "$label-QUESTIONS-for-Andrea.xlsx"
if ((Test-Path $super) -and (Test-Path (Join-Path $folder "$label-QUESTIONS.xlsx"))) {
    Remove-Item -LiteralPath $super -Force
    Write-Host "  withdrew the superseded $label-QUESTIONS-for-Andrea.xlsx" -ForegroundColor Yellow
}

# Nothing ships that predates the code that made it.
$newestSource = (Get-ChildItem (Join-Path $repo 'Kor.Operations.EngineeringTools.Core\Dxf') -Filter '*.cs' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime

# This run owns exactly the files it just landed, plus the explainers when it shipped them.
#
# Pattern-matching on FROM-DRAWINGS|QUESTIONS instead made a YMCA publish judge the TOWERS model,
# and report a package it had not touched as stale. A false "stale" is worse than none: it teaches
# whoever reads it that the word does not mean anything.
$owned = @($landed)
if ((-not $SkipDossier) -and (-not $Variant)) {
    $owned += @('KOR-Model-From-Drawings-DOSSIER.pdf', 'KOR-Model-From-Drawings-READ-THIS-FIRST.pdf')
}

$stale = Get-ChildItem $folder -File |
    Where-Object { $owned -contains $_.Name -and $_.LastWriteTime -lt $newestSource }

Write-Host ''
Get-ChildItem $folder -File |
    Where-Object { $owned -contains $_.Name } |
    Sort-Object Name |
    ForEach-Object { '  {0,-44} {1,7:N0} KB  {2:HH:mm}' -f $_.Name, ($_.Length / 1kb), $_.LastWriteTime }

if ($stale) {
    Write-Host ''
    Write-Host 'STALE — these predate the source that built them:' -ForegroundColor Red
    $stale | ForEach-Object { Write-Host ('  ' + $_.Name) -ForegroundColor Red }
    exit 1
}

Write-Host ''
Write-Host "$Project published, nothing stale." -ForegroundColor Green
