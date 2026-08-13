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

$config = @{ Folder = $ModelFolder; Dxf = $DxfFolder; Reference = $Reference }
Write-Host "job $Project" -ForegroundColor DarkGray
Write-Host "  model folder : $ModelFolder" -ForegroundColor DarkGray
Write-Host "  drawings     : $DxfFolder" -ForegroundColor DarkGray
Write-Host "  reference    : $Reference" -ForegroundColor DarkGray

$folder = $config.Folder
$out    = Join-Path $folder "$Project-FROM-DRAWINGS.e2k"

# The CLI is not rebuilt by `dotnet test`, so a stale exe silently publishes yesterday's rules.
Write-Host 'building the CLI...' -ForegroundColor DarkGray
& dotnet build (Join-Path $repo 'Kor.Operations.EngineeringTools.TakeoffCli') --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }

$cli = Join-Path $repo 'Kor.Operations.EngineeringTools.TakeoffCli\bin\Debug\net8.0\takeoff.exe'

Write-Host "generating $Project..." -ForegroundColor DarkGray
& $cli dxf-to-etabs $config.Dxf (Join-Path $folder $config.Reference) $out `
    --report (Join-Path $folder "$Project-FROM-DRAWINGS-report.txt") `
    --questions (Join-Path $folder "$Project-QUESTIONS-for-Andrea.xlsx") |
    Select-String -Pattern 'Storeys built|^Walls|^Columns|^Floors'
if ($LASTEXITCODE -ne 0) { throw 'generation failed.' }

if (-not $SkipDossier) {
    $dossier = Join-Path $repo 'docs\KOR-DxfToEtabs-web.pdf'
    if (Test-Path $dossier) {
        Copy-Item $dossier (Join-Path $folder 'KOR-Model-From-Drawings-DOSSIER.pdf') -Force
    }

    # The page an engineer actually reads. It ships beside the dossier rather than instead of it,
    # and it is copied here for the same reason everything else is: a document produced by a
    # separate step is a document that goes stale.
    $onePager = Join-Path $repo 'docs\KOR-DxfToEtabs-onepager-web.pdf'
    if (Test-Path $onePager) {
        Copy-Item $onePager (Join-Path $folder 'KOR-Model-From-Drawings-READ-THIS-FIRST.pdf') -Force
    }
}

# The dossier quotes counts, and they are written by hand. A timestamp check cannot see a wrong
# number in a current file — 31138 shipped with the dossier claiming 162 columns against 165 in the
# model, and 5 headers against 8. Every count it states must appear in the model it describes.
$dossier = Join-Path $folder 'KOR-Model-From-Drawings-DOSSIER.pdf'
$pdftotext = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter 'pdftotext.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

if ((Test-Path $dossier) -and $pdftotext) {
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
        $mf = Get-ChildItem $jf.FullName -File -Recurse -Depth 4 -Filter "$job-FROM-DRAWINGS.e2k" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if (-not $mf) { continue }
        $mm = Get-Content -LiteralPath $mf.FullName
        $counts["$job.wall"]   = @($mm | Select-String '^\s+AREA\s+"KW\d+"\s+PANEL').Count
        $counts["$job.column"] = @($mm | Select-String '^\s+LINE\s+"KC\d+"\s+COLUMN').Count
        $counts["$job.plate"]  = @($mm | Select-String '^\s+AREA\s+"KF\d+"\s+FLOOR').Count
        $counts["$job.header"] = @($mm | Select-String '^\s+AREA\s+"KS\d+"\s+PANEL').Count

        $rp = Join-Path $mf.DirectoryName "$job-FROM-DRAWINGS-report.txt"
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

        # Prose: "<number> <member>".
        foreach ($c in [regex]::Matches($prose, '(?<n>\d[\d,]*)\s+(?<what>wall panels|walls|columns|floor plates|plates|headers)')) {
            $n = [int](($c.Groups['n'].Value) -replace ',', '')
            $what = switch -Regex ($c.Groups['what'].Value) {
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
        $plateless = Join-Path $folder "$Project-FROM-DRAWINGS-report.txt"
        if (Test-Path $plateless) {
            $pm = [regex]::Match((Get-Content -LiteralPath $plateless -Raw),
                                 'carry walls or columns but no floor plate[^:]*:\s*(?<list>[^.]+)\.')
            if ($pm.Success -and $prose -match [regex]::Escape('Storeys still carrying members with no plate')) {
                foreach ($s in ($pm.Groups['list'].Value -split ',\s*')) {
                    $storey = $s.Trim()
                    # Only the project the dossier actually enumerates; it lists 31168's by name.
                    if ($Project -eq '31168' -and $storey -and $prose -notmatch [regex]::Escape($storey)) {
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

        # The summary table, where the label comes first and both projects' numbers follow. Checked
        # by position: left column is 31168, right is 31138, and each must be that model's own count.
        $rows = @{
            'Wall panels'              = 'wall'
            'Columns, sized'           = 'column'
            'Floor plates'             = 'plate'
            'Headers over openings'    = 'header'
        }
        foreach ($label in $rows.Keys) {
            $what = $rows[$label]
            $m = [regex]::Match($prose, [regex]::Escape($label) + '[^0-9]*(?<a>[\d,]+)\s+(?<b>[\d,]+)')
            if (-not $m.Success) { $wrong += "dossier table has no '$label' row"; continue }
            foreach ($side in @(@('a', '31168'), @('b', '31138'))) {
                $stated = [int](($m.Groups[$side[0]].Value) -replace ',', '')
                $true_  = $counts["$($side[1]).$what"]
                if ($null -ne $true_ -and $stated -ne $true_) {
                    $wrong += "dossier table: $label for $($side[1]) says $stated, model has $true_"
                }
            }
        }
    }

    # The one-pager is checked by the same rule, because it is the page that actually gets read
    # first and it was the one nobody was checking. Every count in the dossier was verified while
    # the summary beside it claimed 925 wall panels against 947 in the model and 83 plates against
    # 82 — two wrong numbers in the four largest figures on the page, caught by eye rather than by
    # this script. A gate that covers the long document and not the short one covers the wrong one.
    $onePagerSource = Join-Path $repo 'docs\KOR-DxfToEtabs-onepager.html'
    if (Test-Path $onePagerSource) {
        $opHtml = Get-Content -LiteralPath $onePagerSource -Raw
        $opProse = (($opHtml -replace '(?s)<style.*?</style>', ' ' -replace '(?s)<script.*?</script>', ' ' `
                             -replace '<[^>]+>', ' ') -replace '&[a-z]+;', ' ') -replace '\s+', ' '

        foreach ($c in [regex]::Matches($opProse, '(?<n>\d[\d,]*)\s+(?<what>wall panels|walls|columns|floor plates|plates|headers)')) {
            $n = [int](($c.Groups['n'].Value) -replace ',', '')
            $what = switch -Regex ($c.Groups['what'].Value) {
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
        exit 1
    }
}

# Nothing ships that predates the code that made it.
$newestSource = (Get-ChildItem (Join-Path $repo 'Kor.Operations.EngineeringTools.Core\Dxf') -Filter '*.cs' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime

$stale = Get-ChildItem $folder -File |
    Where-Object { $_.Name -match 'FROM-DRAWINGS|QUESTIONS|DOSSIER|READ-THIS-FIRST' -and $_.LastWriteTime -lt $newestSource }

Write-Host ''
Get-ChildItem $folder -File |
    Where-Object { $_.Name -match 'FROM-DRAWINGS|QUESTIONS|DOSSIER|READ-THIS-FIRST' } |
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
