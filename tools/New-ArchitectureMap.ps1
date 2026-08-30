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
    # =========================================================================================
    # A SPARSE MATRIX. Only cells that carry a value are drawn.
    #
    # A full 28x28 grid is 784 COM round trips to say "mostly nothing". Row bands and column rules
    # give the eye the grid; the filled cells are the content.
    # =========================================================================================
    function Short([string]$n) {
        # 'Kor.Operations.EngineeringTools.Core' -> 'EngineeringTools.Core'. The shared prefix is
        # the least informative part of every label on the page.
        return ($n -replace '^Kor\.(Operations|Opportunities)\.', '' -replace '^Kor\.', '')
    }

    function New-MatrixPage($doc, [string]$name, [string]$title, [string]$subtitle,
                            [string[]]$rows, [string[]]$cols, $cells, [string]$fill) {
        $page = $doc.Pages.Add()
        $rowW = 4.6; $colW = 1.30; $cellH = 0.42; $headH = 3.4
        $w = $rowW + ($cols.Count * $colW) + 1.6
        $h = $headH + ($rows.Count * $cellH) + 1.6
        Set-PageSize $page ([Math]::Max($w, 12)) ([Math]::Max($h, 10)) $name

        $top = $h - 1.1
        New-Label $page 0.7 $top $title 18 $accent | Out-Null
        New-Label $page 0.7 ($top - 0.45) $subtitle 10 $hairline | Out-Null

        $gridTop = $top - 0.95
        $x0 = 0.7 + $rowW

        # Column headings, angled so a long project name does not need a two-inch column.
        for ($c = 0; $c -lt $cols.Count; $c++) {
            $lbl = $page.DrawRectangle(($x0 + $c * $colW), ($gridTop - 2.0), ($x0 + $c * $colW + 2.0), ($gridTop - 2.0 + 0.28))
            $lbl.Text = Short $cols[$c]
            $lbl.CellsU('LinePattern').FormulaU = '0'
            $lbl.CellsU('FillPattern').FormulaU = '0'
            $lbl.CellsU('Char.Size').FormulaU = '8 pt'
            $lbl.CellsU('Char.Color').FormulaU = $ink
            $lbl.CellsU('Para.HorzAlign').FormulaU = '0'
            $lbl.CellsU('Angle').FormulaU = '60 deg'
        }

        $gridBottom = $gridTop - 2.15
        for ($r = 0; $r -lt $rows.Count; $r++) {
            $y = $gridBottom - (($r + 1) * $cellH)

            # Row band, so the eye can run along a row without a full grid.
            if ($r % 2 -eq 0) {
                $band = $page.DrawRectangle(0.7, $y, ($x0 + $cols.Count * $colW), ($y + $cellH))
                $band.CellsU('FillForegnd').FormulaU = 'RGB(246,248,250)'
                $band.CellsU('LinePattern').FormulaU = '0'
                $band.SendToBack()
            }

            $lbl = $page.DrawRectangle(0.7, $y, ($x0 - 0.1), ($y + $cellH))
            $lbl.Text = Short $rows[$r]
            $lbl.CellsU('LinePattern').FormulaU = '0'
            $lbl.CellsU('FillPattern').FormulaU = '0'
            $lbl.CellsU('Char.Size').FormulaU = '8.5 pt'
            $lbl.CellsU('Char.Color').FormulaU = $ink
            $lbl.CellsU('Para.HorzAlign').FormulaU = '2'
            $lbl.CellsU('VerticalAlign').FormulaU = '1'

            for ($c = 0; $c -lt $cols.Count; $c++) {
                $key = "$($rows[$r])||$($cols[$c])"
                if (-not $cells.ContainsKey($key)) { continue }
                $cell = $page.DrawRectangle(($x0 + $c * $colW + 0.06), ($y + 0.03),
                                            ($x0 + $c * $colW + $colW - 0.06), ($y + $cellH - 0.03))
                $cell.Text = [string]$cells[$key]
                $cell.CellsU('FillForegnd').FormulaU = $fill
                $cell.CellsU('LineColor').FormulaU = $hairline
                $cell.CellsU('LineWeight').FormulaU = '0.25 pt'
                $cell.CellsU('Char.Size').FormulaU = '8 pt'
                $cell.CellsU('Char.Color').FormulaU = $ink
                $cell.CellsU('Para.HorzAlign').FormulaU = '1'
                $cell.CellsU('VerticalAlign').FormulaU = '1'
            }
        }
        return $page
    }

    # ---- MATRIX 1: which project depends on which -------------------------------------------
    $realProjects = @($model.Projects | Where-Object { $_.Cluster -ne 'one-off tools' } |
                      Sort-Object Cluster, Name | ForEach-Object { $_.Name })
    $dsm = @{}
    $dsmCount = 0
    foreach ($p in $model.Projects) {
        if ($realProjects -notcontains $p.Name) { continue }
        foreach ($r in $p.ProjectRefs) {
            if ($realProjects -notcontains $r) { continue }
            $dsm["$($p.Name)||$r"] = '•'
            $dsmCount++
        }
    }
    New-MatrixPage $doc 'Matrix - dependencies' 'Which project depends on which' (
        "read a ROW: this project references these. $dsmCount reference(s) across $($realProjects.Count) projects, " +
        "$($model.Cycles.Count) cycle(s). The 34 one-off tools are left out."
    ) $realProjects $realProjects $dsm 'RGB(210,228,244)' | Out-Null
    Write-Host "  matrix: dependencies — $dsmCount cell(s)"

    # ---- MATRIX 2: which project handles which file format -----------------------------------
    # THE EFFICIENCY VIEW. One format handled in four projects is four answers to one question.
    $fmtCells = @{}
    $fmtRows = @($model.Formats | ForEach-Object { $_.Ext } | Sort-Object -Unique)
    $fmtColsAll = @{}
    foreach ($f in $model.Formats) {
        $proj = $f.Type.Split(':')[0]
        $key = "$($f.Ext)||$proj"
        $fmtCells[$key] = [int]$fmtCells[$key] + 1
        $fmtColsAll[$proj] = $true
    }
    $fmtCols = @($fmtColsAll.Keys | Sort-Object)
    New-MatrixPage $doc 'Matrix - formats' 'Which project handles which file format' (
        "the number is HOW MANY TYPES in that project touch that format. A format with several " +
        "columns is the same question answered in several places — $($model.Formats.Count) format edges."
    ) $fmtRows $fmtCols $fmtCells 'RGB(252,232,206)' | Out-Null
    Write-Host "  matrix: formats — $($fmtCells.Count) cell(s)"

    # =========================================================================================
    # A LIST PAGE — for things that are a list, not a graph.
    # =========================================================================================
    function New-ListPage($doc, [string]$name, [string]$title, [string]$subtitle, [string[]]$lines,
                          [string]$fill, [int]$perColumn = 40) {
        $page = $doc.Pages.Add()
        $colW = 9.4; $rowH = 0.34
        $colCount = [Math]::Max(1, [Math]::Ceiling($lines.Count / $perColumn))
        Set-PageSize $page ([Math]::Max(($colCount * $colW + 1.6), 12)) (($perColumn * $rowH) + 2.6) $name

        $h = ($perColumn * $rowH) + 2.6
        New-Label $page 0.7 ($h - 1.1) $title 18 $accent | Out-Null
        New-Label $page 0.7 ($h - 1.55) $subtitle 10 $hairline | Out-Null

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $col = [Math]::Floor($i / $perColumn)
            $row = $i % $perColumn
            $x = 0.7 + ($col * $colW)
            $y = ($h - 2.2) - (($row + 1) * $rowH)
            New-Box $page $x $y ($colW - 0.3) ($rowH - 0.05) $lines[$i] $fill 8.5 | Out-Null
        }
        return $page
    }

    # ---- CLI verbs ---------------------------------------------------------------------------
    $verbLines = @($model.Verbs | ForEach-Object { "$($_.Verb)   ·   $(Short $_.Project)" })
    New-ListPage $doc 'CLI verbs' 'Every command-line verb' (
        "$($model.Verbs.Count) verb(s), read off `args[0].Equals(""…"")` rather than grepped for"
    ) $verbLines 'RGB(226,240,226)' 24 | Out-Null
    Write-Host "  list: $($model.Verbs.Count) CLI verb(s)"

    # ---- Duplication ---------------------------------------------------------------------------
    # Names every console tool is entitled to are excluded: seventeen Programs is not a finding.
    $boilerplate = @('Program', 'ToolOptions', 'ImportOptions', 'ImportStats', 'ImportConfig', 'Options', 'Result')
    $dupes = @($model.Duplicates | Where-Object { $boilerplate -notcontains $_.Name })
    $dupLines = @($dupes | ForEach-Object {
        "$($_.Name)  —  $($_.Projects.Count)x:  $(($_.Projects | ForEach-Object { Short $_ }) -join ', ')"
    })
    New-ListPage $doc 'Duplication' 'One name, more than one project' (
        "$($dupes.Count) name(s) after excluding per-tool boilerplate ($($model.Duplicates.Count) before). " +
        "Same name in two projects is not proof of duplicated CODE — it is where to look first."
    ) $dupLines 'RGB(250,224,216)' 30 | Out-Null
    Write-Host "  list: $($dupes.Count) duplicated name(s) of $($model.Duplicates.Count)"

    # =========================================================================================
    # THE MASTER SHEET — everything on one page, four blocks sharing one row axis.
    #
    # Every project in the repository is a row. Read ACROSS a row and you have that project's whole
    # profile: what it depends on, what file formats it deals in, what it talks to outside the repo,
    # and what kinds of type it is made of. Read DOWN a column and you have every project that
    # touches one thing.
    #
    # Sharing the row axis is the point. Two rows with the same pattern are two projects doing the
    # same job, and that is visible at a glance in a way no list of names is.
    # =========================================================================================
    $mRows = @($model.Projects | Sort-Object Cluster, Name)
    $mNames = @($mRows | ForEach-Object { $_.Name })

    # file path -> project, so an external system's evidence can be attributed
    $dirOf = @{}
    foreach ($p in $model.Projects) { $dirOf[$p.Name] = $p.Dir }
    $byLongestDir = @($model.Projects | Sort-Object { -$_.Dir.Length })

    $cDep = @{}; $cFmt = @{}; $cExt = @{}; $cRole = @{}
    foreach ($p in $model.Projects) {
        foreach ($r in $p.ProjectRefs) { if ($mNames -contains $r) { $cDep["$($p.Name)||$r"] = '•' } }
    }
    foreach ($f in $model.Formats) {
        $k = "$($f.Type.Split(':')[0])||$($f.Ext)"
        $cFmt[$k] = [int]$cFmt[$k] + 1
    }
    foreach ($e in $model.Externals) {
        foreach ($ev in $e.Evidence) {
            $owner = $byLongestDir | Where-Object { $ev.StartsWith($_.Dir + '/') } | Select-Object -First 1
            if (-not $owner) { continue }
            $k = "$($owner.Name)||$($e.Name)"
            $cExt[$k] = [int]$cExt[$k] + 1
        }
    }
    foreach ($t in $model.Types) {
        $k = "$($t.Project)||$($t.Role)"
        $cRole[$k] = [int]$cRole[$k] + 1
    }

    $blocks = @(
        @{ Title = 'depends on';        Cols = $mNames;                                        Cells = $cDep;  Fill = 'RGB(210,228,244)'; ColW = 0.44; Short = $true }
        @{ Title = 'file formats';      Cols = @($model.Formats | ForEach-Object { $_.Ext } | Sort-Object -Unique); Cells = $cFmt; Fill = 'RGB(252,232,206)'; ColW = 0.56; Short = $false }
        @{ Title = 'outside the repo';  Cols = @($model.Externals | ForEach-Object { $_.Name }); Cells = $cExt; Fill = 'RGB(255,240,200)'; ColW = 0.62; Short = $false }
        @{ Title = 'types by role';     Cols = @('read','compose','classify','write','service','ui','config','model','test'); Cells = $cRole; Fill = 'RGB(224,240,224)'; ColW = 0.62; Short = $false }
    )

    $rowH = 0.34; $labelW = 4.4; $blockGap = 0.85
    $gridW = $labelW
    foreach ($b in $blocks) { $gridW += ($b.Cols.Count * $b.ColW) + $blockGap }
    $sheetH = ($mRows.Count * $rowH) + 5.2
    $sheetW = $gridW + 1.4

    $master = $doc.Pages.Add()
    Set-PageSize $master $sheetW $sheetH 'Master matrix'

    New-Label $master 0.7 ($sheetH - 1.0) 'KOR Operations — master matrix' 22 $accent | Out-Null
    New-Label $master 0.7 ($sheetH - 1.5) (
        "every project in the repository is a row. read ACROSS for one project's whole profile, DOWN for everyone who touches one thing.   ·   " +
        "$($model.Projects.Count) projects · $('{0:N0}' -f $model.Stats.Lines) lines · $($model.Types.Count) types · " +
        "$($model.Verbs.Count) CLI verbs · $($model.Cycles.Count) dependency cycles"
    ) 10 $hairline | Out-Null

    $gridTop = $sheetH - 4.0
    $masterCells = 0

    # block headings and column labels
    $bx = 0.7 + $labelW
    foreach ($b in $blocks) {
        New-Label $master $bx ($gridTop + 1.55) $b.Title 12 $accent | Out-Null
        for ($c = 0; $c -lt $b.Cols.Count; $c++) {
            $lbl = $master.DrawRectangle(($bx + $c * $b.ColW), $gridTop, ($bx + $c * $b.ColW + 1.5), ($gridTop + 0.26))
            $lbl.Text = $(if ($b.Short) { Short $b.Cols[$c] } else { $b.Cols[$c] })
            $lbl.CellsU('LinePattern').FormulaU = '0'
            $lbl.CellsU('FillPattern').FormulaU = '0'
            $lbl.CellsU('Char.Size').FormulaU = '7.5 pt'
            $lbl.CellsU('Char.Color').FormulaU = $ink
            $lbl.CellsU('Para.HorzAlign').FormulaU = '0'
            $lbl.CellsU('Angle').FormulaU = '60 deg'
        }
        $bx += ($b.Cols.Count * $b.ColW) + $blockGap
    }

    for ($r = 0; $r -lt $mRows.Count; $r++) {
        $proj = $mRows[$r]
        $y = $gridTop - (($r + 1) * $rowH)

        # the row band is the project's CLUSTER colour, so the sheet groups itself
        $band = $master.DrawRectangle(0.7, $y, ($gridW + 0.7 - $blockGap), ($y + $rowH))
        $band.CellsU('FillForegnd').FormulaU = $clusterFill[$proj.Cluster]
        $band.CellsU('FillPattern').FormulaU = $(if ($r % 2 -eq 0) { '1' } else { '0' })
        $band.CellsU('LinePattern').FormulaU = '0'
        $band.SendToBack()

        $lbl = $master.DrawRectangle(0.7, $y, (0.7 + $labelW - 0.12), ($y + $rowH))
        $lbl.Text = "$(Short $proj.Name)   ·   $('{0:N0}' -f $proj.Lines)"
        $lbl.CellsU('LinePattern').FormulaU = '0'
        $lbl.CellsU('FillPattern').FormulaU = '0'
        $lbl.CellsU('Char.Size').FormulaU = '8 pt'
        $lbl.CellsU('Char.Color').FormulaU = $ink
        $lbl.CellsU('Para.HorzAlign').FormulaU = '2'
        $lbl.CellsU('VerticalAlign').FormulaU = '1'

        $bx = 0.7 + $labelW
        foreach ($b in $blocks) {
            for ($c = 0; $c -lt $b.Cols.Count; $c++) {
                $key = "$($proj.Name)||$($b.Cols[$c])"
                if (-not $b.Cells.ContainsKey($key)) { continue }
                $cell = $master.DrawRectangle(($bx + $c * $b.ColW + 0.04), ($y + 0.035),
                                              ($bx + $c * $b.ColW + $b.ColW - 0.04), ($y + $rowH - 0.035))
                $cell.Text = [string]$b.Cells[$key]
                $cell.CellsU('FillForegnd').FormulaU = $b.Fill
                $cell.CellsU('LineColor').FormulaU = $hairline
                $cell.CellsU('LineWeight').FormulaU = '0.25 pt'
                $cell.CellsU('Char.Size').FormulaU = '7.5 pt'
                $cell.CellsU('Char.Color').FormulaU = $ink
                $cell.CellsU('Para.HorzAlign').FormulaU = '1'
                $cell.CellsU('VerticalAlign').FormulaU = '1'
                $masterCells++
            }
            $bx += ($b.Cols.Count * $b.ColW) + $blockGap
        }
    }
    Write-Host ("  MASTER: {0} rows x {1} columns, {2} filled cell(s), {3:N0} x {4:N0} in" -f
                $mRows.Count, (($blocks | ForEach-Object { $_.Cols.Count }) | Measure-Object -Sum).Sum,
                $masterCells, $sheetW, $sheetH)

    # =========================================================================================
    # THE GRAPH PAGES — nodes where the layout put them, ties drawn straight.
    #
    # Straight lines, not Visio's routed connectors. A routed connector is right when a diagram is
    # boxes in rows; on a force-directed graph it fights the layout, adds elbows the layout did not
    # ask for and takes a COM round trip each. Straight edges are what a graph of this kind is.
    # =========================================================================================
    $graphFill = @{
        'drawing intake'       = 'RGB(120,170,215)'
        'desktop app'          = 'RGB(130,190,130)'
        'BD platform'          = 'RGB(240,170,100)'
        'AI / MCP'             = 'RGB(180,150,215)'
        'email + transmittals' = 'RGB(225,190,120)'
        'shared'               = 'RGB(170,180,190)'
        'one-off tools'        = 'RGB(215,220,225)'
        'external'             = 'RGB(250,215,90)'
        'artefact'             = 'RGB(255,244,214)'
        'read'                 = 'RGB(150,195,235)'
        'compose'              = 'RGB(150,205,150)'
        'classify'             = 'RGB(215,175,235)'
        'write'                = 'RGB(245,175,120)'
    }

    function New-GraphPage($doc, $graph, [double]$w, [double]$h, [switch]$Recipe) {
        $page = $doc.Pages.Add()
        Set-PageSize $page $w $h $graph.Name

        New-Label $page 0.8 ($h - 1.1) $graph.Title 22 $accent | Out-Null
        New-Label $page 0.8 ($h - 1.6) $graph.Subtitle 10.5 $hairline | Out-Null

        $m = 1.6                                   # margin
        $plotW = $w - (2 * $m); $plotH = $h - $m - 2.6
        $at = @{}
        foreach ($n in $graph.Nodes) {
            $at[$n.Id] = @{
                X = $m + ($n.X * $plotW)
                Y = $m + ($n.Y * $plotH)
            }
        }

        # EDGES FIRST, so nodes sit on top of them rather than under.
        foreach ($e in $graph.Edges) {
            if (-not $at.ContainsKey($e.From) -or -not $at.ContainsKey($e.To)) { continue }
            $a = $at[$e.From]; $b = $at[$e.To]
            $line = $page.DrawLine($a.X, $a.Y, $b.X, $b.Y)
            $kind = $e.Kind.Split(':')[0]
            switch ($kind) {
                'duplicates' {
                    $line.CellsU('LineColor').FormulaU = 'RGB(205,60,45)'
                    $line.CellsU('LineWeight').FormulaU = ("{0} pt" -f [Math]::Min(4.0, 0.8 * [int]$e.Kind.Split(':')[1]))
                    $line.CellsU('LinePattern').FormulaU = '1'
                }
                'talks to' {
                    $line.CellsU('LineColor').FormulaU = 'RGB(215,175,60)'
                    $line.CellsU('LineWeight').FormulaU = '0.5 pt'
                }
                'same rank' {
                    $line.CellsU('LineColor').FormulaU = 'RGB(205,60,45)'
                    $line.CellsU('LineWeight').FormulaU = '1.0 pt'
                    $line.CellsU('EndArrow').FormulaU = '4'
                }
                default {
                    $line.CellsU('LineColor').FormulaU = 'RGB(120,132,145)'
                    $line.CellsU('LineWeight').FormulaU = '0.6 pt'
                    if ($Recipe) { $line.CellsU('EndArrow').FormulaU = '4' }
                }
            }
        }

        foreach ($n in $graph.Nodes) {
            $c = $at[$n.Id]
            $fill = $(if ($graphFill.ContainsKey($n.Group)) { $graphFill[$n.Group] } else { 'RGB(200,205,210)' })

            if ($Recipe) {
                # An ARTEFACT is a thing you can hold, so it is a rectangle. An OPERATION is
                # something that happens to it, so it is a diamond. That is the whole legend.
                $hw = 1.55; $hh = 0.30
                if ($n.Group -eq 'artefact') {
                    $s = $page.DrawRectangle(($c.X - $hw), ($c.Y - $hh), ($c.X + $hw), ($c.Y + $hh))
                    $s.CellsU('Rounding').FormulaU = '0.05 in'
                } else {
                    # A typed double[] — Visio wants a SAFEARRAY of doubles, and PowerShell's
                    # untyped @() arrives as Object[] and will not marshal.
                    [double[]] $pts = @(
                        ($c.X - $hw), $c.Y, $c.X, ($c.Y + $hh),
                        ($c.X + $hw), $c.Y, $c.X, ($c.Y - $hh),
                        ($c.X - $hw), $c.Y)
                    $s = $page.DrawPolyline($pts, 0)
                }
                $s.Text = $n.Label
                $s.CellsU('Char.Size').FormulaU = '8 pt'
            } else {
                # Area in proportion to size, so a 92,000-line project reads as bigger without
                # being ninety-two times wider than a 1,000-line one.
                $r = 0.16 + (1.05 * [double]$n.Weight)
                $s = $page.DrawOval(($c.X - $r), ($c.Y - $r), ($c.X + $r), ($c.Y + $r))
                $s.Text = $n.Label
                $s.CellsU('Char.Size').FormulaU = ("{0} pt" -f [Math]::Max(6.5, [Math]::Min(11, 5 + ($r * 5))))
            }

            $s.CellsU('FillForegnd').FormulaU = $fill
            $s.CellsU('LineColor').FormulaU = 'RGB(70,80,92)'
            $s.CellsU('LineWeight').FormulaU = '0.5 pt'
            $s.CellsU('Char.Color').FormulaU = $ink
            $s.CellsU('Para.HorzAlign').FormulaU = '1'
            $s.CellsU('VerticalAlign').FormulaU = '1'
        }
        return $page
    }

    foreach ($g in $model.Graphs) {
        if ($g.Name -eq 'Recipes') {
            New-GraphPage $doc $g 40 26 -Recipe | Out-Null
        } else {
            New-GraphPage $doc $g 44 40 | Out-Null
        }
        Write-Host "  graph: $($g.Name) — $($g.Nodes.Count) node(s), $($g.Edges.Count) tie(s)"
    }

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
