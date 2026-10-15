<#
.SYNOPSIS
  Draws a proposal project org chart in Visio from a JSON spec. Saves .vsdx, exports .pdf and .svg,
  and rasterises a .png from the PDF (pdftoppm, 220 dpi) when pdftoppm is on PATH.

.DESCRIPTION
  Built 2026-10-15 for the City of Leduc West Campus RFP (Part III s.4: "include an organizational
  chart"). Searched first: Kor.Operations.Architecture\VisioRenderer.cs renders only the architecture
  model (Render(ArchModel ...)), so its COM pattern - DrawRectangle, Text, SaveAs, Export - is reused
  here rather than the class. No org-chart generator existed under tools/ or docs/ before this one.

  Layout is deterministic: a context box, a vertical chain of leaders, then N group columns of
  stacked people, with tree lines drawn by DrawLine. No Visio auto-layout and no Org Chart add-on
  (its masters bind text to Prop.* fields and can raise prompts). Letter landscape, Arial.

  Verify by LOOKING at the PNG, not by counting shapes.

.PARAMETER Spec
  JSON file:
    { title, subtitle,
      context: { name, lines[] },
      chain:  [ { name, cred, title, role } ... ]         top-down, each reports to the one above
      groups: [ { name, people: [ { name, cred, title, role } ... ] } ... ]   columns under the last of chain
      note }

.PARAMETER OutDir   Output folder. Files are <BaseName>.vsdx/.pdf/.svg/.png.
.PARAMETER BaseName Output file base name, e.g. KOR-Leduc-Project-Org-Chart.

.EXAMPLE
  .\tools\New-ProposalOrgChart.ps1 -Spec '...\KOR-Leduc-Project-Org-Chart.json' -OutDir '...\out' -BaseName 'KOR-Leduc-Project-Org-Chart'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Spec,
    [Parameter(Mandatory)][string]$OutDir,
    [string]$BaseName = 'Org-Chart'
)

$ErrorActionPreference = 'Stop'
# NOT $spec: PowerShell variable names are case-insensitive, so that would be the [string]$Spec
# parameter and the object would be coerced to a string with no error. First run did exactly that:
# four shapes, no text, zero errors recorded.
$model = Get-Content -LiteralPath $Spec -Raw -Encoding UTF8 | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$vsdx = Join-Path $OutDir "$BaseName.vsdx"
$pdf  = Join-Path $OutDir "$BaseName.pdf"
$svg  = Join-Path $OutDir "$BaseName.svg"
$png  = Join-Path $OutDir "$BaseName.png"
foreach ($f in @($vsdx, $pdf, $svg, $png)) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }

# ---- palette (neutral; recolour in Visio if the brand colour is wanted) ----
$Ink       = 'RGB(33,37,41)'
$Grey      = 'RGB(120,124,128)'
$LightFill = 'RGB(240,241,242)'
$LeadFill  = 'RGB(38,50,56)'
$HeadFill  = 'RGB(99,110,114)'
$White     = 'RGB(255,255,255)'
$Line      = 'RGB(130,134,138)'

function Set-Cell($shape, [string]$cell, [string]$formula) { $shape.CellsU($cell).FormulaU = $formula }

function Set-Bold($shape, [int]$begin, [int]$end) {
    # Characters.CharProps(visCharacterStyle=2) = 1 (bold). Parameterised COM property set needs InvokeMember.
    $chars = $shape.Characters
    $chars.Begin = $begin
    $chars.End   = $end
    [System.__ComObject].InvokeMember('CharProps', [System.Reflection.BindingFlags]::SetProperty, $null, $chars, @([int]2, [int]1)) | Out-Null
}

function New-Box {
    param($page, [double]$cx, [double]$cy, [double]$w, [double]$h, [string[]]$lines,
          [string]$fill, [string]$lineColor, [string]$textColor, [double]$size,
          [bool]$boldFirst = $true, [bool]$boldLast = $false, [bool]$noBorder = $false)
    $s = $page.DrawRectangle($cx - $w/2, $cy - $h/2, $cx + $w/2, $cy + $h/2)
    Set-Cell $s 'FillForegnd' $fill
    Set-Cell $s 'LineColor'   $lineColor
    Set-Cell $s 'LineWeight'  '0.75 pt'
    if ($noBorder) { Set-Cell $s 'LinePattern' '0' }
    Set-Cell $s 'Char.Color'  $textColor
    Set-Cell $s 'Char.Size'   ("{0} pt" -f $size)
    Set-Cell $s 'Char.Font'   'FONTTOID("Arial")'
    Set-Cell $s 'Para.HorzAlign' '1'
    Set-Cell $s 'VerticalAlign'  '1'
    Set-Cell $s 'LeftMargin'  '4 pt'
    Set-Cell $s 'RightMargin' '4 pt'
    Set-Cell $s 'TopMargin'   '3 pt'
    Set-Cell $s 'BottomMargin' '3 pt'
    $text = ($lines | Where-Object { $_ -ne $null -and $_ -ne '' }) -join "`n"
    $s.Text = $text
    $kept = @($lines | Where-Object { $_ -ne $null -and $_ -ne '' })
    if ($boldFirst -and $kept.Count -gt 0) { Set-Bold $s 0 $kept[0].Length }
    if ($boldLast -and $kept.Count -gt 1)  { $last = $kept[$kept.Count - 1]; Set-Bold $s ($text.Length - $last.Length) $text.Length }
    return $s
}

function New-Line($page, [double]$x1, [double]$y1, [double]$x2, [double]$y2, [bool]$dashed = $false) {
    $l = $page.DrawLine($x1, $y1, $x2, $y2)
    Set-Cell $l 'LineColor'  $Line
    Set-Cell $l 'LineWeight' '1 pt'
    if ($dashed) { Set-Cell $l 'LinePattern' '2' }
    return $l
}

function Person-Lines($p) {
    @($p.name, $p.cred, $p.title, $p.role) | Where-Object { $_ -ne $null -and $_ -ne '' }
}

$visio = New-Object -ComObject Visio.InvisibleApp
try {
    $visio.AlertResponse = 1                      # never block on a dialog
    $doc  = $visio.Documents.Add('')
    $page = $doc.Pages.Item(1)
    $ps   = $page.PageSheet
    Set-Cell $ps 'PageWidth'  '11 in'
    Set-Cell $ps 'PageHeight' '8.5 in'
    Set-Cell $ps 'PrintPageOrientation' '2'
    Set-Cell $ps 'DrawingScale' '1 in'
    Set-Cell $ps 'PageScale'    '1 in'

    # ---- geometry (inches, origin bottom-left) ----
    $W = 11.0
    $cxMid = $W / 2
    # title block
    [void](New-Box -page $page -cx $cxMid -cy 8.05 -w 10.2 -h 0.36 -lines @($model.title) -fill $White -lineColor $White -textColor $Ink -size 14 -noBorder $true)
    [void](New-Box -page $page -cx $cxMid -cy 7.76 -w 10.2 -h 0.26 -lines @($model.subtitle) -fill $White -lineColor $White -textColor $Grey -size 9 -boldFirst $false -noBorder $true)

    # context box (the IPD team KOR reports into)
    $ctxH = 0.62; $ctxCy = 7.12
    $ctx = New-Box -page $page -cx $cxMid -cy $ctxCy -w 5.6 -h $ctxH -lines (@($model.context.name) + @($model.context.lines)) -fill $LightFill -lineColor $Line -textColor $Ink -size 8.5
    $prevBottom = $ctxCy - $ctxH/2
    $dashedNext = $true

    # leader chain
    $leadW = 3.5; $leadH = 0.98; $gapV = 0.30
    $cy = $prevBottom - $gapV - $leadH/2
    foreach ($p in $model.chain) {
        $box = New-Box -page $page -cx $cxMid -cy $cy -w $leadW -h $leadH -lines (Person-Lines $p) -fill $LeadFill -lineColor $LeadFill -textColor $White -size 9 -boldLast ([bool]$p.role)
        [void](New-Line $page $cxMid $prevBottom $cxMid ($cy + $leadH/2) $dashedNext)
        $dashedNext = $false
        $prevBottom = $cy - $leadH/2
        $cy = $prevBottom - $gapV - $leadH/2
    }

    # group columns
    $n = $model.groups.Count
    $colW = 2.35; $colGap = 0.28
    $total = $n * $colW + ($n - 1) * $colGap
    $x0 = ($W - $total) / 2 + $colW / 2
    $busY = $prevBottom - 0.34
    $headH = 0.42; $headCy = $busY - 0.16 - $headH/2
    $perH = 0.92; $perGap = 0.10
    [void](New-Line $page $cxMid $prevBottom $cxMid $busY)
    if ($n -gt 1) { [void](New-Line $page $x0 $busY ($x0 + ($n - 1) * ($colW + $colGap)) $busY) }
    for ($i = 0; $i -lt $n; $i++) {
        $g = $model.groups[$i]
        $cx = $x0 + $i * ($colW + $colGap)
        [void](New-Line $page $cx $busY $cx ($headCy + $headH/2))
        [void](New-Box -page $page -cx $cx -cy $headCy -w $colW -h $headH -lines @($g.name) -fill $HeadFill -lineColor $HeadFill -textColor $White -size 8.5)
        $pcy = $headCy - $headH/2 - $perGap - $perH/2
        $pb = $headCy - $headH/2
        foreach ($p in $g.people) {
            [void](New-Line $page $cx $pb $cx ($pcy + $perH/2))
            [void](New-Box -page $page -cx $cx -cy $pcy -w $colW -h $perH -lines (Person-Lines $p) -fill $White -lineColor $Line -textColor $Ink -size 8 -boldLast ([bool]$p.role))
            $pb = $pcy - $perH/2
            $pcy = $pb - $perGap - $perH/2
        }
    }

    # continuity note
    if ($model.note) {
        [void](New-Box -page $page -cx $cxMid -cy 0.55 -w 10.2 -h 0.55 -lines @($model.note) -fill $LightFill -lineColor $Line -textColor $Ink -size 8 -boldFirst $false)
    }

    $doc.SaveAs($vsdx)
    $doc.ExportAsFixedFormat(1, $pdf, 1, 0)       # visFixedFormatPDF, visDocExIntentPrint, visPrintAll
    $page.Export($svg)
    $doc.Close()
}
finally {
    $visio.Quit()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($visio)
}

$pdftoppm = Get-Command pdftoppm -ErrorAction SilentlyContinue
if ($pdftoppm) {
    & $pdftoppm.Source -r 220 -png -singlefile $pdf (Join-Path $OutDir $BaseName) | Out-Null
}

foreach ($f in @($vsdx, $pdf, $svg, $png)) {
    if (Test-Path -LiteralPath $f) { "{0,10:N0} bytes  {1}" -f (Get-Item -LiteralPath $f).Length, $f } else { "   MISSING  $f" }
}
