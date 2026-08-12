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
    [Parameter(Mandatory = $true)][ValidateSet('31168', '31138')][string]$Project,
    [switch]$SkipDossier
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

$config = @{
    '31168' = @{
        Folder    = '\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models'
        Dxf       = '\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\_DXF-plans-for-rebuild'
        Reference = '31168-reference.e2k'
    }
    '31138' = @{
        Folder    = '\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models'
        Dxf       = '\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\_DXF-plans-for-rebuild'
        Reference = '31138-reference-from-Andrea-gravity.e2k'
    }
}[$Project]

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
    $counts = @{}
    foreach ($p in '31168', '31138') {
        $f = @{
            '31168' = '\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models'
            '31138' = '\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models'
        }[$p]
        $mf = Join-Path $f "$p-FROM-DRAWINGS.e2k"
        if (-not (Test-Path $mf)) { continue }
        $mm = Get-Content -LiteralPath $mf
        $counts["$p.wall"]   = @($mm | Select-String '^\s+AREA\s+"KW\d+"\s+PANEL').Count
        $counts["$p.column"] = @($mm | Select-String '^\s+LINE\s+"KC\d+"\s+COLUMN').Count
        $counts["$p.plate"]  = @($mm | Select-String '^\s+AREA\s+"KF\d+"\s+FLOOR').Count
        $counts["$p.header"] = @($mm | Select-String '^\s+AREA\s+"KS\d+"\s+PANEL').Count
    }

    # Numbers in the document that are true of something other than a member count, each allowed
    # for a stated reason. Anything not here and not a model count is a stale claim.
    $allowed = @{
        wall   = @(78, 29, 45, 60)  # 78: the wafer fault, a count of what was wrong at the time.
                                    # 29: walls the DRAWINGS show at L01, corroborating the lost model.
                                    # 45: what one 31168 storey gained when the mezzanine fault was fixed.
                                    # 60: what 31138 gained when P4 and P5 were added to its storey list
        column = @(87, 67, 43)      # 87: her own hand-placed columns on 31138, validating placement.
                                    # 67: the same 31168 storey's columns, from that same fix.
                                    # 43: the columns P4 and P5 brought with them
        plate  = @(14, 7, 2)        # 14, 7: the sliver plates the 400 sq ft rule removed.
                                    # 2: the plates P4 and P5 brought with them
        header = @()
    }

    # The engineer's own members that were recognised and skipped are a real count the dossier
    # quotes, but they are hers, not ours, so they match no generated total. They are taken from
    # the report rather than allowlisted, so the dossier is checked against a generated number.
    foreach ($p in '31168', '31138') {
        $rf = @{
            '31168' = '\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models'
            '31138' = '\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models'
        }[$p]
        $rp = Join-Path $rf "$p-FROM-DRAWINGS-report.txt"
        if (-not (Test-Path $rp)) { continue }
        $rm = [regex]::Match((Get-Content -LiteralPath $rp -Raw),
                             '(?<w>\d+) wall\(s\) and (?<c>\d+) column\(s\) were already modelled')
        if ($rm.Success) {
            $allowed.wall   += [int]$rm.Groups['w'].Value
            $allowed.column += [int]$rm.Groups['c'].Value
        }
    }

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
