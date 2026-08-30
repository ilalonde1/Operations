<#
.SYNOPSIS
    Extracts the architecture from the source and renders it to Visio. The map is DERIVED, never drawn.

.DESCRIPTION
    Two stages, and the split is the whole point:

        source  --(archmap.exe, Roslyn)-->  docs/architecture/architecture.json  --(here)-->  .vsdx + .png

    The MODEL is committed as text, so `git diff` shows the architecture moving and a test can fail
    when the code and the map disagree. The VISIO FILE IS AN OUTPUT. Editing it is pointless — the
    next run overwrites it. Layout changes belong in this script.

    Why derived at all: in the single session that prompted this, ~460 lines moved from the App into
    VectorPageReader, PdfGeometryParser became a projection over it, SheetScaleReader grew a method,
    DxfPositionedTag grew a field, dxf-render grew a flag. A picture drawn that morning would have
    been wrong three times by the evening.

.PARAMETER Verify
    Regenerate and check EVERY page and BOTH outputs, then report. Not the page being worked on —
    all of them, because a layout fix that repairs one page and silently breaks another is exactly
    the failure this repo has paid for before.

.EXAMPLE
    ./tools/New-ArchitectureMap.ps1
    ./tools/New-ArchitectureMap.ps1 -Verify
#>
[CmdletBinding()]
param(
    [string] $Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch] $SkipExtract,
    [switch] $Verify,
    [switch] $KeepVisioOpen
)

$ErrorActionPreference = 'Stop'

$outDir   = Join-Path $Root 'docs/architecture'
$modelPath = Join-Path $outDir 'architecture.json'
$vsdxPath = Join-Path $outDir 'KOR-Application-Map.vsdx'

# ---------------------------------------------------------------------------------------------
# 1. EXTRACT
# ---------------------------------------------------------------------------------------------

if (-not $SkipExtract) {
    $proj = Join-Path $Root 'tools/ArchitectureMap/ArchitectureMap.csproj'
    Write-Host 'extracting…' -ForegroundColor Cyan
    & dotnet build $proj -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "archmap build failed" }
    $exe = Join-Path $Root 'tools/ArchitectureMap/bin/Debug/net8.0/archmap.exe'
    & $exe --root $Root --out $modelPath
    if ($LASTEXITCODE -ne 0) { throw "archmap failed" }
}

if (-not (Test-Path $modelPath)) { throw "no model at $modelPath" }
$model = Get-Content $modelPath -Raw -Encoding UTF8 | ConvertFrom-Json

# ---------------------------------------------------------------------------------------------
# 2. RENDER
# ---------------------------------------------------------------------------------------------

# Cluster order is the reading order of the business, not alphabetical: what comes in off a drawing
# first, then the app that runs it, then the commercial side, then the shared floor underneath.
$clusterOrder = @(
    'drawing intake'
    'desktop app'
    'BD platform'
    'AI / MCP'
    'email + transmittals'
    'shared'
)

$clusterFill = @{
    'drawing intake'       = 'RGB(222,235,247)'
    'desktop app'          = 'RGB(226,240,226)'
    'BD platform'          = 'RGB(252,236,219)'
    'AI / MCP'             = 'RGB(238,230,246)'
    'email + transmittals' = 'RGB(249,240,225)'
    'shared'               = 'RGB(238,238,238)'
    'one-off tools'        = 'RGB(244,244,244)'
}

$ink       = 'RGB(28,37,48)'
$hairline  = 'RGB(150,158,166)'
$accent    = 'RGB(0,90,150)'

Write-Host 'rendering…' -ForegroundColor Cyan
$visio = New-Object -ComObject Visio.Application
$visio.Visible = [bool]$KeepVisioOpen
$visio.AlertResponse = 7          # answer "No" to any prompt rather than blocking on it

try {
    $doc = $visio.Documents.Add('')

    function Set-PageSize($page, [double]$w, [double]$h, [string]$name) {
        $page.Name = $name
        $page.PageSheet.CellsU('PageWidth').FormulaU  = "$w in"
        $page.PageSheet.CellsU('PageHeight').FormulaU = "$h in"
        $page.PageSheet.CellsU('PageScale').FormulaU  = '1 in'
        $page.PageSheet.CellsU('DrawingScale').FormulaU = '1 in'
    }

    function New-Box($page, [double]$x, [double]$y, [double]$w, [double]$h,
                     [string]$text, [string]$fill, [double]$pt = 9, [string]$lineColor = $null) {
        $s = $page.DrawRectangle($x, $y, ($x + $w), ($y + $h))
        $s.Text = $text
        $s.CellsU('FillForegnd').FormulaU = $fill
        $s.CellsU('LineColor').FormulaU   = $(if ($lineColor) { $lineColor } else { $hairline })
        $s.CellsU('LineWeight').FormulaU  = '0.5 pt'
        $s.CellsU('Rounding').FormulaU    = '0.06 in'
        $s.CellsU('Char.Size').FormulaU   = "$pt pt"
        $s.CellsU('Char.Color').FormulaU  = $ink
        $s.CellsU('Para.HorzAlign').FormulaU = '1'
        $s.CellsU('VerticalAlign').FormulaU  = '1'
        return $s
    }

    function New-Label($page, [double]$x, [double]$y, [string]$text, [double]$pt, [string]$color = $ink,
                       [double]$w = 30) {
        # Wide by default: a label box narrower than its text wraps, and the subtitle came out on two
        # lines overlapping the row of headers beneath it.
        $s = $page.DrawRectangle($x, $y, ($x + $w), ($y + 0.34))
        $s.Text = $text
        $s.CellsU('LinePattern').FormulaU = '0'
        $s.CellsU('FillPattern').FormulaU = '0'
        $s.CellsU('Char.Size').FormulaU   = "$pt pt"
        $s.CellsU('Char.Color').FormulaU  = $color
        $s.CellsU('Char.Style').FormulaU  = '1'          # bold
        $s.CellsU('Para.HorzAlign').FormulaU = '0'       # left
        return $s
    }

    function Connect($page, $from, $to, [string]$color, [double]$weight = 0.5) {
        $c = $page.Drop($visio.ConnectorToolDataObject, 0, 0)
        $c.CellsU('BeginX').GlueTo($from.CellsU('PinX')) | Out-Null
        $c.CellsU('EndX').GlueTo($to.CellsU('PinX'))     | Out-Null
        $c.CellsU('LineColor').FormulaU  = $color
        $c.CellsU('LineWeight').FormulaU = "$weight pt"
        $c.CellsU('EndArrow').FormulaU   = '4'
        $c.CellsU('EndArrowSize').FormulaU = '1'
        return $c
    }

    # =========================================================================================
    # PAGE 1 — THE WHOLE APPLICATION
    # =========================================================================================
    $page1 = $doc.Pages.Item(1)
    Set-PageSize $page1 46 32 'Application'

    $shapes = @{}
    $boxW = 3.9; $boxH = 1.0; $gapX = 0.32; $gapY = 0.30
    $left = 0.9; $y = 30.4

    New-Label $page1 $left ($y + 0.7) 'KOR Operations — the whole application' 20 $accent | Out-Null
    New-Label $page1 $left ($y + 0.25) (
        "generated from source by tools/New-ArchitectureMap.ps1 — do not edit this file, edit the script   ·   " +
        "$($model.Projects.Count) projects · $('{0:N0}' -f $model.Stats.Lines) lines · $($model.Types.Count) types"
    ) 10 $hairline | Out-Null

    foreach ($cluster in $clusterOrder) {
        $inCluster = @($model.Projects | Where-Object { $_.Cluster -eq $cluster } | Sort-Object { -$_.Lines })
        if ($inCluster.Count -eq 0) { continue }

        $y -= 0.62
        $totalLines = ($inCluster | Measure-Object -Property Lines -Sum).Sum
        New-Label $page1 $left $y ("$cluster  —  $($inCluster.Count) project(s), $('{0:N0}' -f $totalLines) lines") 12 | Out-Null
        $y -= ($boxH + 0.12)

        $x = $left
        foreach ($p in $inCluster) {
            if (($x + $boxW) -gt 45.2) { $x = $left; $y -= ($boxH + $gapY) }
            $label = "$($p.Name)`n$('{0:N0}' -f $p.Lines) lines · $($p.Files) files"
            $shapes[$p.Name] = New-Box $page1 $x $y $boxW $boxH $label $clusterFill[$cluster] 9
            $x += ($boxW + $gapX)
        }
        $y -= 0.55
    }

    # The 35 one-off tools are one box, not 35. They are real, they are not architecture, and drawing
    # each of them buries the seven things on this page that matter.
    $tools = @($model.Projects | Where-Object { $_.Cluster -eq 'one-off tools' })
    if ($tools.Count -gt 0) {
        $y -= 0.5
        $toolLines = ($tools | Measure-Object -Property Lines -Sum).Sum
        New-Box $page1 $left $y ($boxW * 2 + $gapX) $boxH (
            "tools/  —  $($tools.Count) one-off tools`n$('{0:N0}' -f $toolLines) lines · see the CLI verbs page"
        ) $clusterFill['one-off tools'] 10 | Out-Null
        $y -= 0.55
    }

    # External systems, with the count of files that prove each one.
    $y -= 0.45
    New-Label $page1 $left $y 'outside this repository' 12 | Out-Null
    $y -= ($boxH + 0.12)
    $x = $left
    foreach ($e in $model.Externals) {
        if (($x + 3.0) -gt 45.2) { $x = $left; $y -= (0.8 + $gapY) }
        $b = New-Box $page1 $x $y 3.0 0.8 "$($e.Name)`n$($e.Kind) · $($e.Evidence.Count) file(s)" 'RGB(255,249,230)' 9 'RGB(190,150,60)'
        $shapes["ext:$($e.Name)"] = $b
        $x += (3.0 + $gapX)
    }

    # Project references, drawn only where both ends are on this page.
    $edges = 0
    foreach ($p in $model.Projects) {
        if (-not $shapes.ContainsKey($p.Name)) { continue }
        foreach ($r in $p.ProjectRefs) {
            if (-not $shapes.ContainsKey($r)) { continue }
            Connect $page1 $shapes[$p.Name] $shapes[$r] 'RGB(175,185,195)' 0.4 | Out-Null
            $edges++
        }
    }
    Write-Host "  page 1: $($shapes.Count) shape(s), $edges reference edge(s)"

    # =========================================================================================
    # PAGE 2 — THE CONVERGENCE. The tools converge at the READING, not at the job.
    # =========================================================================================
    $page2 = $doc.Pages.Add()
    Set-PageSize $page2 44 30 'Drawing intake'

    $spine = @($model.Types | Where-Object {
        $_.Namespace -like '*EngineeringTools*' -and $_.Role -in @('read','compose','classify','write')
    })

    New-Label $page2 0.9 28.9 'Drawing intake — where the tools converge' 20 $accent | Out-Null
    New-Label $page2 0.9 28.45 (
        'a drawing arrives in one of five formats, is READ into one geometry model, and is WRITTEN out to another — ' +
        "$($spine.Count) types"
    ) 10 $hairline | Out-Null

    $colX    = @(0.9, 9.6, 19.4, 29.6, 38.4)
    $colW    = @(7.6, 8.6, 9.0, 7.6, 4.6)
    $colHead = @('arrives as', 'read by', 'held as', 'written by', 'ships as')

    for ($i = 0; $i -lt $colHead.Count; $i++) {
        New-Label $page2 $colX[$i] 27.6 $colHead[$i] 13 | Out-Null
    }

    function Stack($page, [int]$col, [string[]]$labels, [string]$fill, [double]$h = 0.62, [double]$pt = 9) {
        $out = @{}
        $yy = 27.0
        foreach ($l in $labels) {
            $out[$l] = New-Box $page $colX[$col] ($yy - $h) $colW[$col] $h $l $fill $pt
            $yy -= ($h + 0.16)
        }
        return $out
    }

    # Column 1 / 5: the formats, taken from the format edges the extractor found on these types.
    $spineIds  = @{}; foreach ($t in $spine) { $spineIds[$t.Id] = $t }
    $readExt   = @($model.Formats | Where-Object { $spineIds.ContainsKey($_.Type) -and $spineIds[$_.Type].Role -eq 'read'  } | ForEach-Object { $_.Ext } | Sort-Object -Unique)
    $writeExt  = @($model.Formats | Where-Object { $spineIds.ContainsKey($_.Type) -and $spineIds[$_.Type].Role -eq 'write' } | ForEach-Object { $_.Ext } | Sort-Object -Unique)

    $inBoxes  = Stack $page2 0 $readExt  'RGB(255,249,230)' 0.62 11
    $readers  = Stack $page2 1 (@($spine | Where-Object Role -eq 'read'     | ForEach-Object Name | Sort-Object -Unique)) $clusterFill['drawing intake']
    $middle   = Stack $page2 2 (@($spine | Where-Object { $_.Role -in @('compose','classify') } | ForEach-Object Name | Sort-Object -Unique)) 'RGB(226,240,226)'
    $writers  = Stack $page2 3 (@($spine | Where-Object Role -eq 'write'    | ForEach-Object Name | Sort-Object -Unique)) 'RGB(252,236,219)'
    $outBoxes = Stack $page2 4 $writeExt 'RGB(255,249,230)' 0.62 11

    # Format → reader, and writer → format, from the extracted edges.
    $spineEdges = 0
    foreach ($f in $model.Formats) {
        if (-not $spineIds.ContainsKey($f.Type)) { continue }
        $t = $spineIds[$f.Type]
        if ($t.Role -eq 'read'  -and $inBoxes.ContainsKey($f.Ext)  -and $readers.ContainsKey($t.Name)) {
            Connect $page2 $inBoxes[$f.Ext] $readers[$t.Name] 'RGB(190,160,90)' 0.4 | Out-Null; $spineEdges++
        }
        if ($t.Role -eq 'write' -and $outBoxes.ContainsKey($f.Ext) -and $writers.ContainsKey($t.Name)) {
            Connect $page2 $writers[$t.Name] $outBoxes[$f.Ext] 'RGB(190,160,90)' 0.4 | Out-Null; $spineEdges++
        }
    }

    # EVERY DIRECT MENTION BETWEEN TWO SPINE TYPES, whatever their roles.
    #
    # The first cut only drew role pairs that matched a pipeline I had in my head — read→compose,
    # compose→write — and got 8 arrows out of 34 real ones. The code does not run in the shape of my
    # diagram. In particular it threw away all ten READER→READER edges, which are the single most
    # valuable thing on this page: one reader delegating to another is convergence that already
    # happened, and two readers on the same format that DON'T touch are convergence still owed.
    $boxOf = @{}
    foreach ($k in $readers.Keys) { $boxOf[$k] = @{ Box = $readers[$k]; Col = 1 } }
    foreach ($k in $middle.Keys)  { $boxOf[$k] = @{ Box = $middle[$k];  Col = 2 } }
    foreach ($k in $writers.Keys) { $boxOf[$k] = @{ Box = $writers[$k]; Col = 3 } }

    $nameOf = @{}; foreach ($t in $spine) { $nameOf[$t.Id] = $t }
    $sameColumn = 0
    foreach ($e in $model.Mentions) {
        if (-not $nameOf.ContainsKey($e.From) -or -not $nameOf.ContainsKey($e.To)) { continue }
        $a = $nameOf[$e.From].Name; $b = $nameOf[$e.To].Name
        if (-not $boxOf.ContainsKey($a) -or -not $boxOf.ContainsKey($b)) { continue }

        # A within-column edge gets its own colour and weight: it is a type leaning on another that
        # does the same KIND of job, which is exactly what a convergence review is looking for.
        if ($boxOf[$a].Col -eq $boxOf[$b].Col) {
            Connect $page2 $boxOf[$a].Box $boxOf[$b].Box 'RGB(200,80,40)' 1.0 | Out-Null
            $sameColumn++
        } else {
            Connect $page2 $boxOf[$a].Box $boxOf[$b].Box 'RGB(120,150,180)' 0.5 | Out-Null
        }
        $spineEdges++
    }
    Write-Host "  page 2: $($spine.Count) type(s), $spineEdges edge(s) ($sameColumn within a column — the convergence signal)"

    # =========================================================================================
    # SAVE + LOOK AT IT
    # =========================================================================================
    foreach ($p in $doc.Pages) { $p.ResizeToFitContents() }

    if (Test-Path $vsdxPath) { Remove-Item $vsdxPath -Force }
    $doc.SaveAs($vsdxPath) | Out-Null

    $pngs = @()
    foreach ($p in $doc.Pages) {
        $png = Join-Path $outDir ("KOR-Application-Map-{0}.png" -f ($p.Name -replace '[^\w]+','-'))
        if (Test-Path $png) { Remove-Item $png -Force }
        $p.Export($png)
        $pngs += $png
    }

    $doc.Close()
}
finally {
    if (-not $KeepVisioOpen) { $visio.Quit() }
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($visio)
}

# ---------------------------------------------------------------------------------------------
# 3. ONE COMMAND MEASURES EVERY DELIVERABLE
# ---------------------------------------------------------------------------------------------

Write-Host ''
Write-Host 'wrote:' -ForegroundColor Green
foreach ($f in @($modelPath, $vsdxPath) + $pngs) {
    if (Test-Path $f) {
        $i = Get-Item $f
        '  {0,-52} {1,9:N0} bytes' -f $i.Name, $i.Length
    } else {
        '  {0,-52} MISSING' -f (Split-Path $f -Leaf)
    }
}

if ($Verify) {
    $bad = @()
    foreach ($f in @($modelPath, $vsdxPath) + $pngs) {
        if (-not (Test-Path $f))            { $bad += "missing: $(Split-Path $f -Leaf)" ; continue }
        if ((Get-Item $f).Length -lt 4096)  { $bad += "suspiciously small: $(Split-Path $f -Leaf)" }
    }
    if ($pngs.Count -lt 2) { $bad += "only $($pngs.Count) page(s) exported; expected at least 2" }
    if ($bad) { $bad | ForEach-Object { Write-Host "  FAIL $_" -ForegroundColor Red }; exit 1 }
    Write-Host 'verify: every page and both outputs present' -ForegroundColor Green
}
